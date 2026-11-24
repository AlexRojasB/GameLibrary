using GameLibrary.Api.E2E;
using GameLibrary.Api.CoverImages;
using GameLibrary.Api.Auth;
using GameLibrary.Api.Steam;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverImages;
using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.RandomPicker;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// Permit the approved v2 query envelope; the protocol independently rejects >16 KiB.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestLineSize = 16 * 1024 + 64);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<PlatformService>();
builder.Services.AddScoped<VideoGameService>();
builder.Services.AddScoped<BoardGameService>();
builder.Services.AddScoped<RandomPickerService>();
builder.Services.AddScoped<PlayLogService>();
builder.Services.AddScoped<CoverImageSearchService>();
builder.Services.AddScoped<SteamIntegrationService>();
builder.Services.AddSingleton<ISteamLinkReadiness, ShippedSteamLinkReadiness>();
builder.Services.AddScoped<SteamLinkActivation>();
builder.Services.AddSingleton<SteamLinkDrainObserver>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CoverImageProcessor>();
builder.Services.AddScoped<CoverUploadService>();
var coverCleanupDisabled = CoverCleanupConfiguration.Register(builder.Services, builder.Configuration);
builder.Services.Configure<CoverUploadOptions>(builder.Configuration.GetSection("CoverUploads"));
builder.Services.Configure<CoverImageSearchOptions>(builder.Configuration.GetSection("CoverImageSearch"));
builder.Services.Configure<SteamOptions>(builder.Configuration.GetSection("SteamIntegration"));

var e2eModeEnabled = E2eTestMode.IsEnabled(builder.Configuration, builder.Environment);
if (E2eSteamLinking.Validate(builder.Configuration, builder.Environment))
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ISteamLinkReadiness, E2eSteamLinking>();
}
var fakeCovers = builder.Configuration.GetValue<bool>("E2E:CoverUploads:Enabled");
if (fakeCovers && !(e2eModeEnabled && (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))))
    throw new InvalidOperationException("E2E cover Storage requires Development/Test and the E2E authentication safety guard.");
if (fakeCovers)
{
    builder.Services.AddSingleton<E2eCoverStorage>();
    builder.Services.AddSingleton<ICoverStorage>(services => services.GetRequiredService<E2eCoverStorage>());
}
else
{
    builder.Services.AddHttpClient<ICoverStorage, SupabaseCoverStorage>(client => client.Timeout = TimeSpan.FromSeconds(15))
        .ConfigurePrimaryHttpMessageHandler(SupabaseCoverStorage.CreateHttpMessageHandler)
        .RemoveAllLoggers();
}
var connectionStringName = e2eModeEnabled ? "E2E" : "Default";
var connectionString = builder.Configuration.GetConnectionString(connectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException($"ConnectionStrings:{connectionStringName} is not configured.");
}

builder.Services.AddDbContext<GameLibraryDbContext>(options =>
    options.UseNpgsql(connectionString));

if (e2eModeEnabled && builder.Configuration.GetValue<bool>("E2E:CoverImageSearch:Enabled"))
{
    builder.Services.AddScoped<ICoverImageSearchClient, E2eCoverImageSearchClient>();
}
else
{
    builder.Services.AddHttpClient<ICoverImageSearchClient, BraveCoverImageSearchClient>((services, client) =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CoverImageSearchOptions>>().CurrentValue;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
    });
}

if (e2eModeEnabled && builder.Configuration.GetValue<bool>("E2E:SteamIntegration:Enabled"))
{
    builder.Services.AddSingleton<ISteamClient, E2eSteamClient>();
}
else
{
    builder.Services.AddHttpClient<ISteamClient, SteamClient>((services, client) =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<SteamOptions>>().CurrentValue;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
    }).ConfigurePrimaryHttpMessageHandler(SteamClient.CreateHttpMessageHandler).RemoveAllLoggers();
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    CoverHttp.ConfigureLimits(options);
    SteamLinkHttp.ConfigureLimits(options);
    options.AddPolicy("CoverImageSearch", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.GetSupabaseUserId() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("Steam", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.GetSupabaseUserId() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = e2eModeEnabled ? 20 : 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Retry-After", "Location");
    });
    options.AddPolicy("SteamSecureLink", policy =>
    {
        policy.SetIsOriginAllowed(origin => SteamLinkConfiguration.From(appOptions(),
                builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test")) is { } config && origin == config.FrontendOrigin)
            .WithMethods("POST").WithHeaders("Authorization", "Content-Type").AllowCredentials();
    });
});

SteamOptions appOptions() => builder.Configuration.GetSection("SteamIntegration").Get<SteamOptions>() ?? new();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.GetSupabaseUserId() is null)
                    context.Fail("A usable subject claim is required.");
                return Task.CompletedTask;
            },
        };
        if (e2eModeEnabled)
        {
            options.MetadataAddress = string.Empty;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = E2eTestMode.TokenValidationParameters(builder.Configuration);
        }
        else
        {
            options.TokenValidationParameters.ValidAudiences = builder.Configuration
                .GetSection("Authentication:Schemes:Bearer:TokenValidationParameters:ValidAudiences")
                .Get<string[]>();
            options.TokenValidationParameters.ValidAlgorithms =
            [
                SecurityAlgorithms.EcdsaSha256,
                SecurityAlgorithms.RsaSha256,
            ];
        }
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (coverCleanupDisabled)
    app.Logger.LogWarning("Private QA cover cleanup is disabled. Do not import managed-cover metadata or configure Storage in this mode.");

using (var scope = app.Services.CreateScope())
{
    if (scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CoverUploadOptions>>().Value.Enabled
        && scope.ServiceProvider.GetRequiredService<ICoverStorage>().StorageNamespace is null)
        app.Logger.LogWarning("New cover uploads are unavailable: configure trusted private Storage settings before enabling them.");
}

// Register before exception handling, routing and authentication so challenges/errors are private too.
app.Use(async (context, next) =>
{
    if (CoverHttp.IsPrivate(context))
        context.Response.OnStarting(() => { CoverHttp.ApplyPrivacy(context); return Task.CompletedTask; });
    await next();
});

app.Use(SteamLinkDrainObserver.ObserveAsync);
app.Use(SteamLogPrivacy.LogCompletionAsync);

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    AllowStatusCode404Response = true,
    SuppressDiagnosticsCallback = SteamLogPrivacy.SuppressExceptionDiagnostics,
    ExceptionHandler = async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is CoverException cover)
        {
            await CoverHttp.WriteFailureAsync(context, cover);
            return;
        }
        if (CoverHttp.IsPrivate(context))
        {
            var status = exception is BadHttpRequestException bad && bad.StatusCode is 400 or 408 or 413 ? bad.StatusCode : 500;
            await CoverHttp.WriteProblemAsync(context, status,
                status == 413 ? "Cover image too large" : "An error occurred while processing the request.",
                null, null, context.RequestAborted);
            return;
        }
        await Results.Problem(
                title: "An error occurred while processing the request.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["requestId"] = context.TraceIdentifier
                })
            .ExecuteAsync(context);
    },
});

app.UseRouting();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
