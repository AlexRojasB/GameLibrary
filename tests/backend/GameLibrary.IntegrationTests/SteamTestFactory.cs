using GameLibrary.Api.Steam;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

public sealed class SteamTestFactory : WebApplicationFactory<Program>
{
    public SteamTestFactory()
    {
        FakeSteam = new FakeSteamClient();
        ClientOptions.BaseAddress = new Uri(PublicApiBaseUrl);
    }

    public FakeSteamClient FakeSteam { get; }

    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Test")
        ?? throw new InvalidOperationException(
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run these integration tests.");

    public string PublicApiBaseUrl => "https://api.example.test";

    public string FrontendBaseUrl => "https://app.example.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("AllowedOrigins:0", FrontendBaseUrl);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("E2E:Enabled", "false");
        builder.UseSetting("SteamIntegration:Enabled", "true");
        builder.UseSetting("SteamIntegration:PublicApiBaseUrl", PublicApiBaseUrl);
        builder.UseSetting("SteamIntegration:FrontendBaseUrl", FrontendBaseUrl);
        builder.UseSetting("SteamIntegration:WebApiKey", "fake-steam-api-key");
        builder.UseSetting("SteamIntegration:TimeoutSeconds", "5");
        builder.UseSetting("SteamIntegration:LinkStateLifetimeMinutes", "10");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Configuration = null!;
                    options.MetadataAddress = null!;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestTokens.Issuer,
                        ValidateAudience = true,
                        ValidAudience = TestTokens.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(TestTokens.SigningKey),
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    };
                });

            services.RemoveAll<ISteamClient>();
            services.AddSingleton<ISteamClient>(FakeSteam);
        });
    }

}

public sealed class FakeSteamClient : ISteamClient
{
    private bool _verifyResult = true;
    private IReadOnlyList<SteamOwnedGame> _ownedGames = [];

    public bool LastVerifyResult { get; private set; }

    public IReadOnlyDictionary<string, string?> LastVerifyParameters { get; private set; } = new Dictionary<string, string?>();

    public Func<CancellationToken, Task>? BeforeVerify { get; set; }

    public void SetVerifyResult(bool result) => _verifyResult = result;

    public void SetOwnedGames(IReadOnlyList<SteamOwnedGame> games) => _ownedGames = games;

    public void Reset()
    {
        _verifyResult = true;
        _ownedGames = [];
        LastVerifyResult = false;
        LastVerifyParameters = new Dictionary<string, string?>();
        BeforeVerify = null;
    }

    public async Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        if (BeforeVerify is not null) await BeforeVerify(ct);
        LastVerifyParameters = new Dictionary<string, string?>(parameters);
        LastVerifyResult = _verifyResult;
        return _verifyResult;
    }

    public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct)
    {
        IReadOnlyList<SteamOwnedGame> result = _ownedGames;
        if (query.AppIdsFilter is { Count: > 0 })
        {
            var filter = query.AppIdsFilter.ToHashSet();
            result = _ownedGames.Where(g => filter.Contains(g.SteamAppId)).ToList();
        }
        return Task.FromResult(result);
    }
}
