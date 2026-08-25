using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.Api.E2E;

public static class E2eTestMode
{
    public const string Issuer = "https://test-issuer.example/auth/v1";
    public const string Audience = "authenticated";
    public const string DefaultUserId = "00000000-0000-0000-0000-000000000008";
    public const string DefaultSigningKey = "test-signing-key-32-bytes-minimum!!";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && configuration.GetValue<bool>("E2E:Auth:Enabled");

    public static TokenValidationParameters TokenValidationParameters(IConfiguration configuration) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(SigningKey(configuration)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

    public static string UserId(IConfiguration configuration) =>
        configuration["E2E:Auth:UserId"] ?? DefaultUserId;

    public static string CreateToken(IConfiguration configuration, string subject, DateTime expires)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(SigningKey(configuration)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = subject },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static byte[] SigningKey(IConfiguration configuration) =>
        Encoding.UTF8.GetBytes(configuration["E2E:Auth:SigningKey"] ?? DefaultSigningKey);
}
