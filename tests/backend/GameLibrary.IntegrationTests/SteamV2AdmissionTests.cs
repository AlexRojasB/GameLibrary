using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using GameLibrary.Api.Steam;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;

namespace GameLibrary.IntegrationTests;

public sealed class SteamV2AdmissionTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", false)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Test", false)]
    public async Task Shipped_readiness_and_Production_Staging_policy_reject_true_flags_and_test_DI_without_IO(string environment, bool replacement)
    {
        var probe = new NoDatabase();
        using var factory = new SteamV2Factory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1", environment, replacement, interceptors: [probe]);
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SteamIntegration:BuildReady", "true");
            builder.UseSetting("SteamIntegration:TestAdmissionEnabled", "true");
            builder.UseSetting("E2E:SteamIntegration:Enabled", "true");
            builder.UseSetting("E2E:Auth:Enabled", "true");
            builder.UseSetting("ConnectionStrings:E2E", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
        });
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://api.example.test"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(Guid.NewGuid().ToString()));
        client.DefaultRequestHeaders.Add("Origin", "https://app.example.test");
        foreach (var operation in new[] { "requests", "review", "confirm", "cancel" })
        {
            using var response = await client.PostAsJsonAsync("/steam/link/v2/" + operation, operation switch
            {
                "requests" => (object)new { }, "confirm" => new { transactionId = Guid.NewGuid(), expectedSteamId64 = "1" },
                _ => new { transactionId = Guid.NewGuid() },
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        using var callback = await client.GetAsync("/steam/link/v2/callback?state=synthetic");
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode); Assert.False(callback.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, probe.Calls); Assert.Equal(0, factory.Provider.Calls);
    }

    [Theory]
    [InlineData("https://user:pass@api.example.test", "https://app.example.test")]
    [InlineData("https://api.example.test?state=bad", "https://app.example.test")]
    [InlineData("https://api.example.test/#fragment", "https://app.example.test")]
    [InlineData("http://evil.example", "http://evil.example")]
    [InlineData("http://127.0.0.1:5218", "http://localhost:4200")]
    [InlineData("http://127.0.0.1:5218", "https://app.example.test")]
    public void Trusted_configuration_rejects_credentials_queries_fragments_and_nonliteral_or_mixed_local_origins(string api, string frontend)
    {
        var options = new SteamOptions { Enabled = true, PublicApiBaseUrl = api, FrontendBaseUrl = frontend, AllowLocalLinkingHttp = true };
        Assert.Null(SteamLinkConfiguration.From(options, true));
    }

    private sealed class NoDatabase : DbConnectionInterceptor
    {
        public int Calls;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken ct = default)
        { Interlocked.Increment(ref Calls); throw new InvalidOperationException("Unexpected database access in disabled build."); }
    }
}
