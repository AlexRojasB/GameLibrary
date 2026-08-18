using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Test factory that post-configures the Bearer JWT options to validate against a
/// known symmetric test signing key. This disables Supabase metadata/JWKS discovery so
/// the authorization-behavior tests run deterministically offline. No test-only branch
/// is added to Program.cs; the production JWT configuration is asserted separately by
/// <see cref="JwtValidationConfigurationTests"/>.
/// </summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
        });
    }
}