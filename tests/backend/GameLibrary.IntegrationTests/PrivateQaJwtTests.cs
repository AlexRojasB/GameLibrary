using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

public sealed class PrivateQaJwtTests
{
    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-audience", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("missing-sub", HttpStatusCode.Unauthorized)]
    [InlineData("hs256", HttpStatusCode.Unauthorized)]
    public async Task Production_QA_uses_real_ES256_validation_with_only_offline_discovery_substituted(string scenario, HttpStatusCode expected)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = ECDsa.Create(signer.ExportParameters(false));
        var key = new ECDsaSecurityKey(signer) { KeyId = "synthetic-qa-key" };
        await using var factory = new PrivateQaJwtFactory(new ECDsaSecurityKey(verifier) { KeyId = key.KeyId });
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        Assert.Equal(PrivateQaJwtFactory.Issuer + "/.well-known/openid-configuration", options.MetadataAddress);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.MapInboundClaims);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.Equal(new[] { SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256 }, options.TokenValidationParameters.ValidAlgorithms);
        var signingKey = scenario == "wrong-signature" ? new ECDsaSecurityKey(otherSigner) { KeyId = key.KeyId } : key;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = scenario == "wrong-issuer" ? "https://other.invalid/auth/v1" : PrivateQaJwtFactory.Issuer,
            Audience = scenario == "wrong-audience" ? "other" : "authenticated",
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(5),
            Claims = scenario == "missing-sub" ? new Dictionary<string, object>() : new() { ["sub"] = "synthetic-qa-user" },
            SigningCredentials = scenario == "hs256"
                ? new SigningCredentials(new SymmetricSecurityKey(TestTokens.SigningKey), SecurityAlgorithms.HmacSha256)
                : new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new("Bearer", new JsonWebTokenHandler().CreateToken(descriptor));
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    [Theory]
    [InlineData("https://qa-web.example.invalid", true)]
    [InlineData("https://other.example.invalid", false)]
    public async Task Production_QA_CORS_only_allows_the_exact_frontend_origin(string origin, bool allowed)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var factory = new PrivateQaJwtFactory(new ECDsaSecurityKey(signer));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/video-games");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed) Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal(0, factory.DatabaseCalls);
    }
}

internal sealed class PrivateQaJwtFactory(SecurityKey publicKey) : PrivateQaCleanupFactory
{
    internal const string Issuer = "https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:Schemes:Bearer:MetadataAddress", Issuer + "/.well-known/openid-configuration");
        builder.UseSetting("Authentication:Schemes:Bearer:RequireHttpsMetadata", "true");
        builder.UseSetting("Authentication:Schemes:Bearer:TokenValidationParameters:ValidAudiences:0", "authenticated");
        builder.UseSetting("AllowedOrigins:0", "https://qa-web.example.invalid");
        builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>("Bearer", options =>
        {
            // Synthetic public discovery key only. Do not replace validation parameters,
            // authentication handlers or the Production environment's E2E safety guard.
            var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
            metadata.SigningKeys.Add(publicKey);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        }));
    }
}
