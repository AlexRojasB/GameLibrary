using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class SteamEndpointTests : IClassFixture<SteamTestFactory>, IAsyncLifetime
{
    private const string SteamId64 = "76561198000000000";
    private const string AnotherSteamId64 = "76561198000000001";

    private readonly SteamTestFactory _factory;

    public SteamEndpointTests(SteamTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _factory.FakeSteam.Reset();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Status

    [Fact]
    public async Task Status_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/steam/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Status_token_without_sub_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var response = await client.GetAsync("/steam/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_with_no_library_returns_linked_false_and_creates_no_library()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        await using var db = CreateDbContext();
        var beforeCount = await db.Libraries.CountAsync(l => l.UserId == sub);

        var response = await client.GetAsync("/steam/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.True(body.GetProperty("linkAvailable").GetBoolean());
        Assert.True(body.GetProperty("importAvailable").GetBoolean());
        Assert.False(body.GetProperty("linked").GetBoolean());
        Assert.Null(body.GetProperty("steamId64").GetString());
        Assert.True(body.GetProperty("linkedAt").ValueKind == JsonValueKind.Null);

        await using var afterDb = CreateDbContext();
        var afterCount = await afterDb.Libraries.CountAsync(l => l.UserId == sub);
        Assert.Equal(beforeCount, afterCount);
    }

    #endregion

    #region Link Request

    [Fact]
    public async Task LinkRequests_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/steam/link-requests", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkRequests_creates_library_and_link_state_and_returns_start_url()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsync("/steam/link-requests", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var startUrl = body.GetProperty("startUrl").GetString();
        Assert.NotNull(startUrl);
        Assert.Contains("/steam/link/start/", startUrl!);
        Assert.Contains("state=", startUrl);
        Assert.True(body.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LinkRequests_stores_only_state_hash_not_raw_state()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        var response = await client.PostAsync("/steam/link-requests", null);
        response.EnsureSuccessStatusCode();

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var linkRequest = await db.SteamLinkRequests.SingleAsync(r => r.LibraryId == library.Id);

        Assert.Equal(64, linkRequest.StateHash.Length);
        Assert.Matches("^[a-f0-9]{64}$", linkRequest.StateHash);
        Assert.Null(linkRequest.ConsumedAt);
        Assert.True(linkRequest.ExpiresAt > linkRequest.CreatedAt);
    }

    [Fact]
    public async Task LinkRequests_when_already_linked_returns_409()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        await LinkSteamAccount(sub);

        var response = await client.PostAsync("/steam/link-requests", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Steam account already linked", body.GetProperty("title").GetString());
    }

    #endregion

    #region Link Start

    [Fact]
    public async Task LinkStart_with_unknown_state_redirects_to_failure()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/steam/link/start/unknown-state");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    #endregion

    #region Link Callback

    [Fact]
    public async Task LinkCallback_valid_fake_openid_creates_steam_account()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=success", location);

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var account = await db.SteamAccounts.SingleAsync(a => a.LibraryId == library.Id);
        Assert.Equal(SteamId64, account.SteamId64);

        var linkRequest = await db.SteamLinkRequests.SingleAsync(r => r.LibraryId == library.Id);
        Assert.NotNull(linkRequest.ConsumedAt);
    }

    [Fact]
    public async Task LinkCallback_rejects_expired_state()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub, expired: true);

        var callbackParams = BuildCallbackParams(rawState);
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);

        await using var db = CreateDbContext();
        Assert.False(await db.SteamAccounts.AnyAsync());
    }

    [Fact]
    public async Task LinkCallback_rejects_consumed_state()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);
        await ConsumeState(sub, rawState);

        var callbackParams = BuildCallbackParams(rawState);
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_missing_state()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/steam/link/callback", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_malformed_claimed_id()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.claimed_id"] = "https://evil.example/id/76561198000000000";
        callbackParams["openid.identity"] = callbackParams["openid.claimed_id"];
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);

        await using var db = CreateDbContext();
        Assert.False(await db.SteamAccounts.AnyAsync());
    }

    [Fact]
    public async Task LinkCallback_rejects_wrong_mode()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.mode"] = "checkid_setup";
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_wrong_provider_endpoint()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.op_endpoint"] = "https://evil.example/openid";
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_wrong_return_to()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.return_to"] = _factory.PublicApiBaseUrl + "/steam/link/callback?state=wrong";
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_wrong_realm()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.realm"] = "https://evil.example.test/";
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_identity_mismatch()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        callbackParams["openid.identity"] = "https://steamcommunity.com/openid/id/76561198000000001";
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_false_steam_verification()
    {
        _factory.FakeSteam.SetVerifyResult(false);
        var sub = NewSub();
        var rawState = await CreateRawState(sub);

        var callbackParams = BuildCallbackParams(rawState);
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);

        await using var db = CreateDbContext();
        Assert.False(await db.SteamAccounts.AnyAsync());
    }

    [Fact]
    public async Task Parallel_callbacks_same_state_allow_only_one_link()
    {
        var sub = NewSub();
        var rawState = await CreateRawState(sub);
        var callbackParams = BuildCallbackParams(rawState);
        var url = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();
        var response1 = await client1.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        var response2 = await client2.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        var redirects = new[] { response1, response2 }
            .Select(r => r.Headers.Location?.ToString() ?? "")
            .ToList();

        Assert.Contains(redirects, l => l.Contains("/manage/steam?steamLink=success"));
        Assert.Contains(redirects, l => l.Contains("/manage/steam?steamLink=failure"));

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        Assert.Single(await db.SteamAccounts.Where(a => a.LibraryId == library.Id).ToListAsync());
    }

    #endregion

    #region Unlink

    [Fact]
    public async Task Unlink_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/steam/link");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unlink_when_linked_deletes_steam_account()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);

        var response = await client.DeleteAsync("/steam/link");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = CreateDbContext();
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.Library.UserId == sub));
    }

    [Fact]
    public async Task Unlink_when_not_linked_returns_204()
    {
        var client = ClientFor(NewSub());

        var response = await client.DeleteAsync("/steam/link");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unlink_leaves_imported_steam_app_id_unchanged()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570, 730]);
        var client = ClientFor(sub);

        var response = await client.DeleteAsync("/steam/link");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entries = await db.LibraryEntries
            .Where(le => le.LibraryId == library.Id && le.SteamAppId != null)
            .ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.SteamAppId == 570);
        Assert.Contains(entries, e => e.SteamAppId == 730);
    }

    [Fact]
    public async Task Unlink_allows_subsequent_linking()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);

        await client.DeleteAsync("/steam/link");

        var response = await client.PostAsync("/steam/link-requests", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Linking_different_account_requires_unlink_first()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);

        _factory.FakeSteam.SetVerifyResult(true);
        var response = await client.PostAsync("/steam/link-requests", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region Preview

    [Fact]
    public async Task Preview_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/steam/library/preview", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_without_linked_account_returns_404()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsync("/steam/library/preview", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Steam account not linked", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Preview_with_linked_account_returns_normalized_candidates()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([
            new SteamOwnedGame(570, " Dota 2 ", 123),
            new SteamOwnedGame(0, "Invalid", 1),
            new SteamOwnedGame(730, "Counter-Strike 2", null),
        ]);
        var client = ClientFor(sub);

        var response = await client.PostAsync("/steam/library/preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Success", body.GetProperty("state").GetString());
        var candidates = body.GetProperty("candidates");
        Assert.Equal(2, candidates.GetArrayLength());
        Assert.Equal(730, candidates[0].GetProperty("steamAppId").GetInt64());
        Assert.Equal("Counter-Strike 2", candidates[0].GetProperty("name").GetString());
        Assert.Equal(570, candidates[1].GetProperty("steamAppId").GetInt64());
        Assert.Equal("Dota 2", candidates[1].GetProperty("name").GetString());
        Assert.False(candidates[1].GetProperty("alreadyImported").GetBoolean());
    }

    [Fact]
    public async Task Preview_no_usable_games_returns_NoVisibleGames()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([]);
        var client = ClientFor(sub);

        var response = await client.PostAsync("/steam/library/preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NoVisibleGames", body.GetProperty("state").GetString());
        Assert.Equal(0, body.GetProperty("candidates").GetArrayLength());
        Assert.NotNull(body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Preview_marks_already_imported_candidates()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570]);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123), new SteamOwnedGame(730, "CS2", null)]);
        var client = ClientFor(sub);

        var response = await client.PostAsync("/steam/library/preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var candidates = body.GetProperty("candidates");
        var dota = candidates.EnumerateArray().Single(c => c.GetProperty("steamAppId").GetInt64() == 570);
        Assert.True(dota.GetProperty("alreadyImported").GetBoolean());
    }

    [Fact]
    public async Task Preview_provider_timeout_returns_503()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([]);
        var client = ClientFor(sub);

        var response = await client.PostAsync("/steam/library/preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Import

    [Fact]
    public async Task Import_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new[] { 570 } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_without_linked_account_returns_404()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new[] { 570L } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_empty_selection_returns_400()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = Array.Empty<long>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid Steam import", body.GetProperty("title").GetString());
        Assert.Equal("Select at least one Steam game to import.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Import_rejects_appid_0_before_provider_call()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 0 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_rejects_more_than_100_appids()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        var client = ClientFor(sub);
        var appIds = Enumerable.Range(1, 101).Select(i => (long)i).ToList();

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = appIds });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_creates_owned_video_games_with_steam_platform()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([
            new SteamOwnedGame(570, "Dota 2", 123),
            new SteamOwnedGame(730, "Counter-Strike 2", 456),
        ]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570, 730 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("imported").GetArrayLength());
        Assert.Equal(0, body.GetProperty("alreadyImported").GetArrayLength());
        Assert.Equal(0, body.GetProperty("unavailable").GetArrayLength());
        Assert.Equal(0, body.GetProperty("invalid").GetArrayLength());

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entries = await db.LibraryEntries
            .Include(le => le.Game)
            .Include(le => le.GamePlatforms).ThenInclude(gp => gp.Platform)
            .Where(le => le.LibraryId == library.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(AcquisitionStatus.Owned, entry.AcquisitionStatus);
            Assert.NotNull(entry.SteamAppId);
            Assert.Equal(GameType.VideoGame, entry.Game.GameType);
            Assert.Null(entry.Game.CoverImageUrl);
            Assert.Null(entry.GameStatus);
            Assert.Null(entry.ProgressPercentage);
            Assert.Null(entry.Rating);
            Assert.Null(entry.Notes);
            Assert.Null(entry.Game.MinimumPlayers);
            Assert.Null(entry.Game.MaximumPlayers);
            var platform = Assert.Single(entry.GamePlatforms).Platform;
            Assert.Equal("Steam", platform.Name);
        }
    }

    [Fact]
    public async Task Import_skips_already_imported_appids()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570]);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null), new SteamOwnedGame(730, "CS2", null)]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570, 730 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("imported").GetArrayLength());
        Assert.Equal(730, body.GetProperty("imported")[0].GetProperty("steamAppId").GetInt64());
        Assert.Contains(570L, body.GetProperty("alreadyImported").EnumerateArray().Select(e => e.GetInt64()));
    }

    [Fact]
    public async Task Import_appids_not_returned_by_steam_reported_as_unavailable()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null)]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570, 999 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(999L, body.GetProperty("unavailable").EnumerateArray().Select(e => e.GetInt64()));
    }

    [Fact]
    public async Task Import_does_not_create_play_log_entries()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123)]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.PlayLogEntries.CountAsync());
    }

    [Fact]
    public async Task Import_reuses_existing_case_insensitive_steam_platform()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);

        await using (var db = CreateDbContext())
        {
            var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
            db.Platforms.Add(new Core.Platforms.Platform
            {
                Id = Guid.NewGuid(),
                LibraryId = library.Id,
                Name = " steam ",
            });
            await db.SaveChangesAsync();
        }

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null)]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var afterDb = CreateDbContext();
        var library2 = await afterDb.Libraries.SingleAsync(l => l.UserId == sub);
        var steamPlatforms = await afterDb.Platforms
            .Where(p => p.LibraryId == library2.Id && p.NameNormalized == "steam")
            .ToListAsync();
        Assert.Single(steamPlatforms);
    }

    [Fact]
    public async Task Import_returns_correct_steam_app_id_in_response()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null)]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var imported = body.GetProperty("imported")[0];
        Assert.Equal(570, imported.GetProperty("steamAppId").GetInt64());
        Assert.Equal("Dota 2", imported.GetProperty("name").GetString());
        Assert.False(imported.GetProperty("videoGameId").GetGuid() == Guid.Empty);
    }

    #endregion

    #region Config

    [Fact]
    public async Task Status_when_disabled_returns_enabled_false()
    {
        await using var factory = new DisabledSteamTestFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(NewSub()));

        var response = await client.GetAsync("/steam/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.False(body.GetProperty("linkAvailable").GetBoolean());
        Assert.False(body.GetProperty("importAvailable").GetBoolean());
        Assert.NotNull(body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Status_when_missing_web_api_key_shows_import_unavailable()
    {
        await using var factory = new MissingWebApiKeyTestFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(NewSub()));

        var response = await client.GetAsync("/steam/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("linkAvailable").GetBoolean());
        Assert.False(body.GetProperty("importAvailable").GetBoolean());
    }

    #endregion

    #region Helpers

    private static string NewSub() => Guid.NewGuid().ToString();

    private HttpClient ClientFor(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        return client;
    }

    private GameLibraryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .Options;
        return new GameLibraryDbContext(options);
    }

    private async Task LinkSteamAccount(string sub)
    {
        var client = ClientFor(sub);
        var response = await client.PostAsync("/steam/link-requests", null);
        response.EnsureSuccessStatusCode();

        var linkBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        var startUrl = linkBody.GetProperty("startUrl").GetString()!;
        var rawState = startUrl.Split("state=")[1];

        var callbackParams = BuildCallbackParams(rawState);
        var callbackUrl = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        var callbackResponse = await client.GetAsync(callbackUrl, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
    }

    private async Task<string> CreateRawState(string sub, bool expired = false)
    {
        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var rawState = Core.Steam.SteamIntegrationRules.GenerateRawState();
        var stateHash = Core.Steam.SteamIntegrationRules.HashState(rawState);
        var now = DateTimeOffset.UtcNow;
        db.SteamLinkRequests.Add(new Core.Steam.SteamLinkRequest
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            StateHash = stateHash,
            CreatedAt = expired ? now.AddMinutes(-20) : now,
            ExpiresAt = expired ? now.AddMinutes(-10) : now.AddMinutes(10),
        });
        await db.SaveChangesAsync();
        return rawState;
    }

    private async Task ConsumeState(string sub, string rawState)
    {
        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var stateHash = Core.Steam.SteamIntegrationRules.HashState(rawState);
        var linkRequest = await db.SteamLinkRequests.SingleAsync(r => r.LibraryId == library.Id && r.StateHash == stateHash);
        linkRequest.ConsumedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task ImportGames(string sub, uint[] appIds)
    {
        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var steamPlatform = await db.Platforms.FirstOrDefaultAsync(
            p => p.LibraryId == library.Id && p.NameNormalized == "steam");
        if (steamPlatform is null)
        {
            steamPlatform = new Core.Platforms.Platform { Id = Guid.NewGuid(), LibraryId = library.Id, Name = "Steam" };
            db.Platforms.Add(steamPlatform);
            await db.SaveChangesAsync();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var appId in appIds)
        {
            var game = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = $"Game {appId}",
                CoverImageUrl = null,
                CreatedAt = now,
            };
            var entry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = library.Id,
                GameId = game.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
                SteamAppId = appId,
            };
            game.LibraryEntry = entry;
            entry.GamePlatforms.Add(new Core.Games.GamePlatform { LibraryEntryId = entry.Id, PlatformId = steamPlatform.Id });
            db.Games.Add(game);
            now = now.AddSeconds(1);
        }
        await db.SaveChangesAsync();
    }

    private Dictionary<string, string?> BuildCallbackParams(string rawState)
    {
        var claimedId = SteamIntegrationRules.ClaimedIdPrefix + SteamId64;
        return new Dictionary<string, string?>
        {
            ["state"] = rawState,
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = _factory.PublicApiBaseUrl + "/steam/link/callback?state=" + Uri.EscapeDataString(rawState),
            ["openid.realm"] = _factory.PublicApiBaseUrl + "/",
        };
    }

    #endregion
}

public sealed class DisabledSteamTestFactory : WebApplicationFactory<Program>
{
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Test")
        ?? throw new InvalidOperationException("ConnectionStrings__Test is required.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("E2E:Enabled", "false");
        builder.UseSetting("SteamIntegration:Enabled", "false");

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

public sealed class MissingWebApiKeyTestFactory : WebApplicationFactory<Program>
{
    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Test")
        ?? throw new InvalidOperationException("ConnectionStrings__Test is required.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("E2E:Enabled", "false");
        builder.UseSetting("SteamIntegration:Enabled", "true");
        builder.UseSetting("SteamIntegration:PublicApiBaseUrl", "https://api.example.test");
        builder.UseSetting("SteamIntegration:FrontendBaseUrl", "https://app.example.test");
        builder.UseSetting("SteamIntegration:WebApiKey", "");

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
