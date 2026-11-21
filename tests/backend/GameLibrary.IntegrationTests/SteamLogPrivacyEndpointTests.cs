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

// Requires the existing disposable real-PostgreSQL harness. Never substitute EF InMemory.
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
    public async Task Real_link_flow_preserves_state_and_account_semantics_without_logging_sensitive_data(string outcome)
    {
        _factory.FakeSteam.Reset();
        var capture = new SteamPrivacyCapture();
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(capture)));
        using var browser = host.CreateClient(new() {
            BaseAddress = new Uri(_factory.PublicApiBaseUrl), AllowAutoRedirect = false, HandleCookies = true,
        });
        var sub = Guid.NewGuid().ToString();
        var token = TestTokens.CreateToken(sub);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using (var db = Db()) await db.Database.MigrateAsync();
        using var created = await browser.PostAsync("/steam/link-requests", null);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var start = body.GetProperty("startUrl").GetString()!;
        var state = Uri.UnescapeDataString(new Uri(start).Segments.Last());
        var hash = SteamIntegrationRules.HashState(state);
        var signature = "synthetic-openid-signature-no-real-credentials";
        var steamId = Random.Shared.NextInt64(10000000000000000, 99999999999999999).ToString(CultureInfo.InvariantCulture);
        browser.DefaultRequestHeaders.Authorization = null;

        await using (var db = Db())
        {
            var pending = await db.SteamLinkRequests.SingleAsync(r => r.StateHash == hash);
            Assert.Null(pending.ConsumedAt);
            Assert.True(pending.ExpiresAt > pending.CreatedAt);
            if (outcome == "expired")
            {
                pending.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
                pending.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10);
                await db.SaveChangesAsync();
            }
        }
        using var redirect = await browser.GetAsync(start);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        if (outcome == "expired") Assert.EndsWith("steamLink=failure", redirect.Headers.Location!.ToString());
        else Assert.Equal("steamcommunity.com", redirect.Headers.Location!.Host);

        var parameters = new Dictionary<string, string>
        {
            ["state"] = outcome == "invalid" ? "synthetic-unknown-state" : state,
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.identity"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
            ["openid.claimed_id"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
            ["openid.return_to"] = _factory.PublicApiBaseUrl + "/steam/link/callback?state=" + state,
            ["openid.realm"] = _factory.PublicApiBaseUrl + "/",
            ["openid.sig"] = signature,
        };
        var callback = "/steam/link/callback?" + string.Join("&", parameters.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
        var verifyCalls = 0;
        _factory.FakeSteam.BeforeVerify = _ =>
        {
            Interlocked.Increment(ref verifyCalls);
            if (outcome == "provider-error") throw new HttpRequestException("Synthetic failure " + callback);
            return Task.CompletedTask;
        };
        using var result = await browser.GetAsync(callback);
        var successful = outcome is "success" or "replayed";
        Assert.Equal(outcome == "provider-error" ? HttpStatusCode.InternalServerError : HttpStatusCode.Redirect, result.StatusCode);
        if (outcome != "provider-error") Assert.EndsWith("steamLink=" + (successful ? "success" : "failure"), result.Headers.Location!.ToString());
        if (outcome == "replayed")
        {
            await using var beforeDb = Db();
            var consumedBefore = await beforeDb.SteamLinkRequests.AsNoTracking().Where(r => r.StateHash == hash)
                .Select(r => r.ConsumedAt).SingleAsync();
            using var replay = await browser.GetAsync(callback);
            Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
            Assert.EndsWith("steamLink=failure", replay.Headers.Location!.ToString());
            await using var afterDb = Db();
            Assert.Equal(consumedBefore, await afterDb.SteamLinkRequests.AsNoTracking().Where(r => r.StateHash == hash)
                .Select(r => r.ConsumedAt).SingleAsync());
        }
        Assert.Equal(outcome is "invalid" or "expired" ? 0 : 1, verifyCalls);
        await using (var db = Db())
        {
            var pending = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.StateHash == hash);
            var account = await db.SteamAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Library.UserId == sub);
            Assert.Equal(successful, pending.ConsumedAt.HasValue);
            Assert.Equal(successful, account is not null);
            if (successful) Assert.Equal(steamId, account!.SteamId64);
        }
        // Linking is not application login: callback did not grant a Supabase session.
        using var anonymous = await browser.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var status = await browser.GetAsync("/steam/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(successful, (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("linked").GetBoolean());
        capture.AssertPrivate(state, hash, signature, steamId, token);
        if (outcome == "provider-error")
            Assert.Contains(capture.Entries, e => e.EventId == 1101 && e.Level == LogLevel.Error && e.Exception == "");
    }

    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>()
        .UseNpgsql(_factory.ConnectionString).Options);
}
