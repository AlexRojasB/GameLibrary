using GameLibrary.Api.E2E;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.RandomPicker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<PlatformService>();
builder.Services.AddScoped<VideoGameService>();
builder.Services.AddScoped<BoardGameService>();
builder.Services.AddScoped<RandomPickerService>();
builder.Services.AddScoped<PlayLogService>();

var e2eModeEnabled = E2eTestMode.IsEnabled(builder.Configuration, builder.Environment);
var connectionString = e2eModeEnabled
    ? builder.Configuration.GetConnectionString("E2E")
        ?? throw new InvalidOperationException("ConnectionStrings:E2E is required when E2E auth is enabled.")
    : builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddDbContext<GameLibraryDbContext>(options =>
    options.UseNpgsql(connectionString));

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
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
