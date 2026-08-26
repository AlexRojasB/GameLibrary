using GameLibrary.Api.E2E;
using GameLibrary.Api.CoverImages;
using GameLibrary.Api.Auth;
using GameLibrary.Api.Steam;
using GameLibrary.Core.CoverImages;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.RandomPicker;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.Configure<CoverImageSearchOptions>(builder.Configuration.GetSection("CoverImageSearch"));
builder.Services.Configure<SteamOptions>(builder.Configuration.GetSection("SteamIntegration"));

var e2eModeEnabled = E2eTestMode.IsEnabled(builder.Configuration, builder.Environment);
var connectionString = e2eModeEnabled
    ? builder.Configuration.GetConnectionString("E2E")
        ?? throw new InvalidOperationException("ConnectionStrings:E2E is required when E2E auth is enabled.")
    : builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
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
    builder.Services.AddScoped<ISteamClient, E2eSteamClient>();
}
else
{
    builder.Services.AddHttpClient<ISteamClient, SteamClient>((services, client) =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<SteamOptions>>().CurrentValue;
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
                PermitLimit = 5,
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
            .AllowAnyMethod();
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
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

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        await Results.Problem(
                title: "An error occurred while processing the request.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["requestId"] = context.TraceIdentifier
                })
            .ExecuteAsync(context);
    });
});

app.UseCors("Frontend");

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
