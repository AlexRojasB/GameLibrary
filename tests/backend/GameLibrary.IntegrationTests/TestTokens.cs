using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Test-only JWT constants and token minting for the authorization-behavior tests.
/// These tests validate against a deterministic symmetric test key and never touch
/// the production Supabase metadata/JWKS.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://test-issuer.example/auth/v1";
    public const string Audience = "authenticated";

    public static readonly byte[] SigningKey =
        Encoding.UTF8.GetBytes("test-signing-key-32-bytes-minimum!!");

    public static readonly byte[] OtherSigningKey =
        Encoding.UTF8.GetBytes("another-test-signing-key-32-bytes!!");

    public static string CreateToken(
        string? subject,
        byte[]? signingKey = null,
        DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(signingKey ?? SigningKey),
                SecurityAlgorithms.HmacSha256),
        };

        if (subject is not null)
        {
            descriptor.Claims = new Dictionary<string, object> { ["sub"] = subject };
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}