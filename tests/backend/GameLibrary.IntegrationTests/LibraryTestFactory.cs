using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

public sealed class LibraryTestFactory : WebApplicationFactory<Program>
{
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Test")
        ?? throw new InvalidOperationException(
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run these integration tests.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);

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
