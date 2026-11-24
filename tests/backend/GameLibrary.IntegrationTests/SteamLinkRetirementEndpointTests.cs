using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace GameLibrary.IntegrationTests;

// Database/network-free host: any unexpected application IO fails the existing factory guard.
public sealed class SteamLinkRetirementEndpointTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Retired_routes_ignore_enable_flags_and_untrusted_payloads_without_any_io(string environment)
    {
        using var factory = new RetirementFactory(environment);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", "__Host-GameLibrary.SteamLink=synthetic-cookie");
        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Post, "/steam/link-requests?state=synthetic-state") { Content = JsonContent.Create(new { ownerId = "untrusted-owner" }) },
            new HttpRequestMessage(HttpMethod.Get, "/steam/link/start/synthetic-state"),
            new HttpRequestMessage(HttpMethod.Get, "/steam/link/callback?state=synthetic-state&openid.sig=synthetic-signature"),
        })
        {
            using (request)
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Null(response.Headers.Location);
                Assert.False(response.Headers.Contains("Set-Cookie"));
                Assert.True(response.Headers.CacheControl!.NoStore);
                Assert.True(response.Headers.CacheControl.Private);
                Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
                var text = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(text);
                var body = document.RootElement;
                Assert.Equal(5, body.EnumerateObject().Count());
                Assert.Equal("STEAM_LINKING_RETIRED", body.GetProperty("code").GetString());
                foreach (var value in new[] { "synthetic-state", "synthetic-signature", "synthetic-cookie", "untrusted-owner" })
                    Assert.False(text.Contains(value, StringComparison.Ordinal), "Retirement response echoed input; payload withheld.");
            }
        }
        foreach (var route in new[] { "/steam/link/v2/requests", "/steam/link/v2/review", "/steam/link/v2/confirm", "/steam/link/v2/cancel" })
        {
            using var response = await client.PostAsJsonAsync(route, new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        using var wrongMethod = await client.PostAsJsonAsync("/steam/link/v2/callback", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using var obsolete = await client.PostAsJsonAsync("/steam/link/complete", new { });
        Assert.Equal(HttpStatusCode.NotFound, obsolete.StatusCode);
        using var callback = await client.GetAsync("/steam/link/v2/callback?state=synthetic-state");
        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode);
        Assert.Equal("https://app.example.test/manage/steam?steamLink=failure", callback.Headers.Location!.ToString());
        Assert.False(callback.Headers.Contains("Set-Cookie"));
        using var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    private sealed class RetirementFactory(string environment) : PrivateQaCleanupFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:E2E", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
            builder.UseSetting("SteamIntegration:Enabled", "true");
            builder.UseSetting("SteamIntegration:SecureLinkingEnabled", "true");
            builder.UseSetting("SteamIntegration:PublicApiBaseUrl", "https://api.example.test");
            builder.UseSetting("SteamIntegration:FrontendBaseUrl", "https://app.example.test");
            builder.UseSetting("E2E:SteamIntegration:Enabled", "true");
        }
    }
}
