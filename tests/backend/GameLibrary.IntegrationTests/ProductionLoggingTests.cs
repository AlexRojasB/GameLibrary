using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameLibrary.IntegrationTests;

public sealed class ProductionLoggingTests
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    public async Task Only_Production_filters_successful_SQL_while_preserving_warnings_and_errors(
        string environment, bool commandInformationEnabled)
    {
        using var factory = new LoggingFactory(environment);
        using var client = factory.CreateClient();
        Assert.Equal(environment, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();
        var commands = loggers.CreateLogger(CommandCategory);
        Assert.Equal(commandInformationEnabled, commands.IsEnabled(LogLevel.Information));
        Assert.True(commands.IsEnabled(LogLevel.Warning));
        Assert.True(commands.IsEnabled(LogLevel.Error));
        Assert.True(commands.IsEnabled(LogLevel.Critical));

        // Exercise the real host/provider filters without issuing SQL or importing games.
        for (var i = 0; i < 1000; i++)
            commands.LogInformation(new EventId(20101), "Synthetic successful command");
        commands.LogWarning(new EventId(20100), "Synthetic command warning");
        commands.LogError(new EventId(20102), "Synthetic failed command");
        commands.LogCritical(new EventId(20103), "Synthetic critical command failure");

        var entries = factory.Capture.Entries.ToArray();
        Assert.Equal(commandInformationEnabled ? 1000 : 0,
            entries.Count(e => e.Category == CommandCategory && e.Level == LogLevel.Information));
        foreach (var level in new[] { LogLevel.Warning, LogLevel.Error, LogLevel.Critical })
            Assert.Contains(entries, e => e.Category == CommandCategory && e.Level == level);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    [Fact]
    public void Production_keeps_application_summaries_and_security_provider_and_query_warnings()
    {
        using var factory = new LoggingFactory("Production");
        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();
        var application = loggers.CreateLogger("GameLibrary.Api.Steam.SteamClient");
        Assert.True(application.IsEnabled(LogLevel.Information));
        application.LogInformation("Synthetic operation summary: {ImportedCount}", 2);

        var warningCategories = new[]
        {
            "Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler",
            "Microsoft.EntityFrameworkCore.Query",
            "GameLibrary.Api.Steam.SteamClient",
            "GameLibrary.Api.CoverUploads.SupabaseCoverStorage",
        };
        foreach (var category in warningCategories)
        {
            var logger = loggers.CreateLogger(category);
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            logger.LogWarning("Synthetic diagnostic warning");
        }

        Assert.Contains(factory.Capture.Entries,
            e => e.Category == "GameLibrary.Api.Steam.SteamClient" && e.Level == LogLevel.Information);
        foreach (var category in warningCategories)
            Assert.Contains(factory.Capture.Entries, e => e.Category == category && e.Level == LogLevel.Warning);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    private sealed class LoggingFactory(string environment) : PrivateQaCleanupFactory
    {
        public CaptureProvider Capture { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("E2E:Auth:Enabled", "false");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Capture));
        }
    }

    private sealed record Entry(string Category, LogLevel Level);

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue(new Entry(category, logLevel));
    }
}
