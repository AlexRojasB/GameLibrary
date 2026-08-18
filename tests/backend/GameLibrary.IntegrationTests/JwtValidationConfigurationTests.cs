using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Verifies the production Supabase JWT validation configuration on the unmodified host
/// without a live network call: metadata/JWKS discovery, audience, claim mapping, and full
/// signature/issuer/audience/lifetime validation.
/// </summary>
public class JwtValidationConfigurationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JwtValidationConfigurationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void BearerOptions_UseSupabaseMetadataAndFullValidation()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(
            "https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/openid-configuration",
            options.MetadataAddress);
        Assert.Contains("authenticated", options.TokenValidationParameters.ValidAudiences);
        Assert.False(options.MapInboundClaims);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.Contains(SecurityAlgorithms.EcdsaSha256, options.TokenValidationParameters.ValidAlgorithms);
        Assert.Contains(SecurityAlgorithms.RsaSha256, options.TokenValidationParameters.ValidAlgorithms);
    }
}