using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameLibrary.IntegrationTests;

// Real disposable PostgreSQL: historical flow outcomes now all terminate at retirement.
[Collection("Database")]
public sealed class SteamLogPrivacyEndpointTests : IClassFixture<SteamTestFactory>
{
    private readonly SteamTestFactory _factory;
    public SteamLogPrivacyEndpointTests(SteamTestFactory factory) => _factory = factory;

    [Theory]
    [InlineData("success")]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("replayed")]
    [InlineData("provider-error")]
    public async Task Retired_legacy_outcomes_preserve_state_and_accounts_without_logging_sensitive_data(string outcome)
    {
        _factory.FakeSteam.Reset();
        var capture = new SteamPrivacyCapture();
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(capture)));
        using var browser = host.CreateClient(new() {
            BaseAddress = new Uri(_factory.PublicApiBaseUrl), AllowAutoRedirect = false, HandleCookies = false,
        });
        var sub = Guid.NewGuid().ToString();
        var token = TestTokens.CreateToken(sub);
        var state = SteamIntegrationRules.GenerateRawState();
        var hash = SteamIntegrationRules.HashState(state);
        const string signature = "synthetic-openid-signature-no-real-credentials";
        var steamId = Random.Shared.NextInt64(10000000000000000, 99999999999999999).ToString(CultureInfo.InvariantCulture);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var consumed = outcome == "replayed" ? now : (DateTimeOffset?)null;
        var createdAt = now.AddMinutes(-20);
        var expiresAt = outcome == "expired" ? now.AddMinutes(-10) : now.AddMinutes(10);
        var libraryId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var db = Db())
        {
            await db.Database.MigrateAsync();
            db.Libraries.Add(new Core.Libraries.Library { Id = libraryId, UserId = sub });
            db.SteamLinkRequests.Add(new SteamLinkRequest
            {
                Id = Guid.NewGuid(), LibraryId = libraryId, StateHash = hash,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
                ConsumedAt = consumed,
            });
            if (outcome == "replayed")
                db.SteamAccounts.Add(new SteamAccount { Id = accountId, LibraryId = libraryId, SteamId64 = steamId, LinkedAt = now });
            await db.SaveChangesAsync();
        }
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        browser.DefaultRequestHeaders.Add("Cookie", "__Host-GameLibrary.SteamLink=" + hash);
        var verifyCalls = 0;
        _factory.FakeSteam.BeforeVerify = _ =>
        {
            Interlocked.Increment(ref verifyCalls);
            throw new HttpRequestException("Synthetic provider failure " + signature);
        };
        var parameters = new Dictionary<string, string>
        {
            ["state"] = outcome == "invalid" ? "synthetic-unknown-state" : state,
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.identity"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
            ["openid.claimed_id"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
            ["openid.return_to"] = _factory.PublicApiBaseUrl + "/steam/link/callback?state=" + state,
            ["openid.sig"] = signature,
        };
        var callback = "/steam/link/callback?" + string.Join("&", parameters.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Post, "/steam/link-requests"),
            new HttpRequestMessage(HttpMethod.Get, "/steam/link/start/" + state),
            new HttpRequestMessage(HttpMethod.Get, callback),
            new HttpRequestMessage(HttpMethod.Get, callback),
        })
        {
            using (request)
            using (var response = await browser.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Null(response.Headers.Location);
                Assert.False(response.Headers.Contains("Set-Cookie"));
                Assert.True(response.Headers.CacheControl!.NoStore);
                var text = await response.Content.ReadAsStringAsync();
                foreach (var secret in new[] { state, hash, signature, steamId, token })
                    Assert.False(text.Contains(secret, StringComparison.Ordinal), "Retirement echoed sensitive data; payload withheld.");
            }
        }
        Assert.Equal(0, verifyCalls);
        await using (var db = Db())
        {
            var pending = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.StateHash == hash);
            Assert.Equal(consumed, pending.ConsumedAt);
            Assert.Equal(createdAt, pending.CreatedAt);
            Assert.Equal(expiresAt, pending.ExpiresAt);
            Assert.True(pending.StateHash == hash, "Legacy verifier changed; payload withheld.");
            var account = await db.SteamAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.LibraryId == libraryId);
            Assert.Equal(outcome == "replayed", account is not null);
            if (account is not null) { Assert.Equal(accountId, account.Id); Assert.Equal(steamId, account.SteamId64); }
        }
        browser.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await browser.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var status = await browser.GetAsync("/steam/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("linkAvailable").GetBoolean());
        Assert.Equal(outcome == "replayed", body.GetProperty("linked").GetBoolean());
        capture.AssertPrivate(state, hash, signature, steamId, token);
    }

    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>()
        .UseNpgsql(_factory.ConnectionString).Options);
}
