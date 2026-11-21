using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameLibrary.IntegrationTests;

public sealed class SteamLogPrivacyTests
{
    private const string State = "synthetic-private-state-not-a-real-flow";
    private const string Signature = "synthetic-private-signature";
    private const string Category = "GameLibrary.Api.Steam.LinkDiagnostics";

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Rejected_start_and_callback_emit_only_template_status_and_timing_even_with_scope_capture(string environment)
    {
        using var factory = new PrivacyFactory(environment);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        foreach (var route in Routes())
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.EndsWith("steamLink=failure", response.Headers.Location!.ToString());
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }
        Assert.Equal(2, factory.Capture.Entries.Count(e => e.Category == Category && e.EventId == 1100));
        Assert.Contains(factory.Capture.Entries, e => e.Category == Category && e.Message.Contains("/steam/link/start/{state}"));
        factory.Capture.AssertPrivate(State, SteamIntegrationRules.HashState(State), Signature);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Unexpected_exception_message_inner_exception_and_Data_never_reach_log_providers(string environment)
    {
        using var factory = new PrivacyFactory(environment, throwOnAction: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        foreach (var route in Routes())
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.False(body.Contains(State, StringComparison.Ordinal), "Response contains private state.");
            Assert.False(body.Contains(Signature, StringComparison.Ordinal), "Response contains private assertion.");
        }
        Assert.Equal(2, factory.Capture.Entries.Count(e => e.Category == Category && e.Level == LogLevel.Error && e.EventId == 1101));
        Assert.Equal(2, factory.Capture.Entries.Count(e => e.Category == Category && e.EventId == 1100));
        Assert.Contains(factory.Capture.Entries, e => e.Message.Contains(nameof(HttpRequestException)));
        factory.Capture.AssertPrivate(State, SteamIntegrationRules.HashState(State), Signature);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    [Fact]
    public async Task Only_Steam_exception_details_are_replaced_and_security_provider_diagnostics_remain_enabled()
    {
        using var factory = new PrivacyFactory("Production", throwOnAction: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(factory.Capture.Entries, e => e.Category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"
            && e.Level == LogLevel.Error && e.Exception.Contains("synthetic ordinary failure"));
        Assert.DoesNotContain(factory.Capture.Entries, e => e.Category == Category);
        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();
        foreach (var category in new[] { "Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler",
                     "Microsoft.EntityFrameworkCore.Database.Command", "GameLibrary.Api.Steam.SteamClient" })
        {
            var logger = loggers.CreateLogger(category);
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            logger.LogWarning("Synthetic safe diagnostic");
            Assert.Contains(factory.Capture.Entries, e => e.Category == category && e.Level == LogLevel.Warning);
        }
        Assert.False(loggers.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Critical));
        Assert.Equal(0, factory.DatabaseCalls);
    }

    [Fact]
    public async Task Handled_Steam_exceptions_are_not_published_to_the_framework_diagnostic_listener()
    {
        using var probe = new HandledExceptionProbe();
        using var subscription = DiagnosticListener.AllListeners.Subscribe(probe);
        using var factory = new PrivacyFactory("Production", throwOnAction: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var steam = await client.GetAsync(Routes()[1]);
        Assert.Equal(HttpStatusCode.InternalServerError, steam.StatusCode);
        Assert.Equal(0, probe.SteamEvents);
        using var ordinary = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.InternalServerError, ordinary.StatusCode);
        Assert.True(probe.OtherEvents > 0); // Proves the probe observes the unsuppressed path.
        factory.Capture.AssertPrivate(State, Signature);
        Assert.Equal(0, factory.DatabaseCalls);
    }

    [Fact]
    public async Task Negative_control_Warning_only_hosting_still_exposes_the_raw_start_path_in_scopes()
    {
        // Synthetic negative control only: proves why filtering request Information
        // events alone was insufficient. This provider is in-memory, never console.
        using var factory = new PrivacyFactory("Production", unsafeHostingScopeControl: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await client.GetAsync(Routes()[0]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(factory.Capture.Entries.Any(e => e.Scopes.Contains(State, StringComparison.Ordinal)),
            "Expected baseline scope exposure was not observed; payload withheld.");
        Assert.Equal(0, factory.DatabaseCalls);
    }

    [Fact]
    public async Task Query_tokens_do_not_establish_an_application_session_and_unmatched_Steam_paths_are_never_echoed()
    {
        using var factory = new PrivacyFactory("Production");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var auth = await client.PostAsync("/steam/link-requests?access_token=" + State, null);
        Assert.Equal(HttpStatusCode.Unauthorized, auth.StatusCode);
        using var unknown = await client.GetAsync("/steam/unknown/" + State + "?state=" + Signature);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains(factory.Capture.Entries, e => e.EventId == 1100 && e.Message.Contains("/steam/{endpoint}"));
        factory.Capture.AssertPrivate(State, Signature);
        Assert.Equal(0, factory.DatabaseCalls);
    }

    private sealed class HandledExceptionProbe : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private int _steamEvents;
        private int _otherEvents;
        public int SteamEvents => Volatile.Read(ref _steamEvents);
        public int OtherEvents => Volatile.Read(ref _otherEvents);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.AspNetCore")
                _subscriptions.Add(listener.Subscribe(this, name => name == "Microsoft.AspNetCore.Diagnostics.HandledException"));
        }
        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            var context = diagnostic.Value?.GetType().GetProperty("httpContext")?.GetValue(diagnostic.Value) as HttpContext;
            if (context?.Request.Path.StartsWithSegments("/steam") == true) Interlocked.Increment(ref _steamEvents);
            else Interlocked.Increment(ref _otherEvents);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { foreach (var subscription in _subscriptions) subscription.Dispose(); }
    }

    private static string[] Routes() => [
        "/steam/link/start/" + State,
        "/steam/link/callback?state=" + State + "&openid.sig=" + Signature
            + "&openid.return_to=" + Uri.EscapeDataString("https://api.example.test/steam/link/callback?state=" + State),
    ];

    private sealed class PrivacyFactory(string environment, bool throwOnAction = false, bool unsafeHostingScopeControl = false) : PrivateQaCleanupFactory
    {
        public SteamPrivacyCapture Capture { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("E2E:Auth:Enabled", "false");
            builder.UseSetting("SteamIntegration:Enabled", "true");
            builder.UseSetting("SteamIntegration:PublicApiBaseUrl", "https://api.example.test");
            builder.UseSetting("SteamIntegration:FrontendBaseUrl", "https://app.example.test");
            if (unsafeHostingScopeControl)
                builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics", "Warning");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Capture));
            if (throwOnAction)
                builder.ConfigureServices(services => services.Configure<MvcOptions>(options => options.Filters.Add(new ThrowingFilter())));
        }
    }

    private sealed class ThrowingFilter : IAsyncActionFilter
    {
        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.Request.Path == "/health") throw new InvalidOperationException("synthetic ordinary failure");
            var request = context.HttpContext.Request;
            var exception = new HttpRequestException("Synthetic transport failure at " + request.Path + request.QueryString,
                new InvalidOperationException(Signature));
            exception.Data["state"] = State;
            throw exception;
        }
    }
}

/// <summary>Captures formatted text, structured values, exception objects and external scopes.</summary>
internal sealed class SteamPrivacyCapture : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    public ConcurrentQueue<Entry> Entries { get; } = new();
    public sealed record Entry(string Category, LogLevel Level, int EventId, string Message, string State, string Exception, string Scopes);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void Dispose() { }

    public void AssertPrivate(params string[] sensitive)
    {
        Assert.NotEmpty(Entries);
        foreach (var entry in Entries)
        foreach (var secret in sensitive)
        {
            var all = entry.Message + entry.State + entry.Exception + entry.Scopes;
            Assert.False(all.Contains(secret, StringComparison.Ordinal), "Sensitive data reached a log provider; payload withheld.");
            Assert.False(all.Contains(Uri.EscapeDataString(secret), StringComparison.Ordinal), "Encoded sensitive data reached a log provider; payload withheld.");
        }
    }

    private sealed class CaptureLogger(SteamPrivacyCapture owner, string category) : ILogger
    {
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            owner._scopes.ForEachScope((scope, output) => output.Add(scope?.ToString() ?? ""), scopes);
            var structured = state is IEnumerable<KeyValuePair<string, object?>> values
                ? string.Join(";", values.Select(v => v.Key + "=" + v.Value)) : state?.ToString() ?? "";
            owner.Entries.Enqueue(new(category, level, id.Id, formatter(state, exception), structured,
                exception?.ToString() ?? "", string.Join(";", scopes)));
        }
    }
}
