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
        await db.SteamAccounts.ExecuteDeleteAsync();
        await db.SteamLinkRequests.ExecuteDeleteAsync();
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
        var client = RedirectClient();

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

        var client = RedirectClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam", location);
        Assert.Contains("steamLink=success", location);

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

        var client = RedirectClient();
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

        var client = RedirectClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/manage/steam?steamLink=failure", location);
    }

    [Fact]
    public async Task LinkCallback_rejects_missing_state()
    {
        var client = RedirectClient();

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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client = RedirectClient();
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

        var client1 = RedirectClient();
        var client2 = RedirectClient();
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
            new SteamOwnedGame(570, " Dota 2 ", 123, null, null),
            new SteamOwnedGame(0, "Invalid", 1, null, null),
            new SteamOwnedGame(730, "Counter-Strike 2", null, null, null),
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
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, null), new SteamOwnedGame(730, "CS2", null, null, null)]);
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
            new SteamOwnedGame(570, "Dota 2", 123, null, null),
            new SteamOwnedGame(730, "Counter-Strike 2", 456, null, null),
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
            Assert.False(entry.Game.IsFamilyFriendly);
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
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, null), new SteamOwnedGame(730, "CS2", null, null, null)]);
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
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, null)]);
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
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, null)]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
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

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, null)]);
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
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, null)]);
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

    #region Steam Playtime & Cover Enhancements

    [Fact]
    public async Task Preview_and_import_wait_for_the_same_library_refresh_lock()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570]);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Game 570", 150, null, null)]);

        await using var guard = CreateDbContext();
        var library = await guard.Libraries.SingleAsync(l => l.UserId == sub);
        await using var transaction = await guard.Database.BeginTransactionAsync();
        await guard.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {library.Id} FOR UPDATE");
        var blocker = await guard.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var preview = ClientFor(sub).PostAsync("/steam/library/preview", null);
        var import = ClientFor(sub).PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var observer = CreateDbContext();
        var waiting = 0;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (waiting < 2 && DateTime.UtcNow < deadline)
        {
            // The second queued row lock can wait on the first waiter, not directly on the guard.
            waiting = await observer.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND EXISTS (
                    SELECT 1 FROM unnest(pg_blocking_pids(pid)) AS blockers(blocking_pid)
                    WHERE blocking_pid = {blocker} OR {blocker} = ANY(pg_blocking_pids(blocking_pid)))
                """).SingleAsync();
            if (waiting < 2) await Task.Delay(20);
        }
        Assert.Equal(2, waiting);
        Assert.False(preview.IsCompleted);
        Assert.False(import.IsCompleted);
        var entry = await guard.LibraryEntries.SingleAsync(e => e.LibraryId == library.Id);
        entry.SteamPlaytimeMinutes = 120;
        await guard.SaveChangesAsync();
        await transaction.CommitAsync();

        var responses = await Task.WhenAll(preview, import);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await using var after = CreateDbContext();
        var stored = await after.LibraryEntries.SingleAsync(e => e.LibraryId == library.Id);
        Assert.Equal(150L, stored.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(after, sub));
    }

    [Theory]
    [InlineData("preview", 100L, 150, 150L)]
    [InlineData("preview", 100L, 100, 100L)]
    [InlineData("preview", 100L, 50, 50L)]
    [InlineData("preview", 100L, null, 100L)]
    [InlineData("preview", null, 150, 150L)]
    [InlineData("preview", null, null, null)]
    [InlineData("import", 100L, 150, 150L)]
    [InlineData("import", 100L, 100, 100L)]
    [InlineData("import", 100L, 50, 50L)]
    [InlineData("import", 100L, null, 100L)]
    [InlineData("import", null, 150, 150L)]
    [InlineData("import", null, null, null)]
    public async Task Refresh_uses_the_persisted_baseline_without_changing_manual_data_or_logging(
        string operation, long? previous, int? current, long? expected)
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570]);
        await using (var db = CreateDbContext())
        {
            var entry = await db.LibraryEntries.Include(e => e.Game).SingleAsync(e => e.Library.UserId == sub);
            entry.SteamPlaytimeMinutes = previous;
            entry.GameStatus = GameStatus.Playing;
            entry.ProgressPercentage = 25;
            entry.Game.IsFamilyFriendly = true;
            entry.Game.CoverImageUrl = "https://manual.example/cover.jpg";
            await db.SaveChangesAsync();
        }

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Steam name", current, null, "new_logo")]);
        var client = ClientFor(sub);
        var response = operation == "preview"
            ? await client.PostAsync("/steam/library/preview", null)
            : await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        response.EnsureSuccessStatusCode();

        await using var after = CreateDbContext();
        var stored = await after.LibraryEntries.Include(e => e.Game).SingleAsync(e => e.Library.UserId == sub);
        Assert.Equal(expected, stored.SteamPlaytimeMinutes);
        Assert.Equal(GameStatus.Playing, stored.GameStatus);
        Assert.Equal(25, stored.ProgressPercentage);
        Assert.True(stored.Game.IsFamilyFriendly);
        Assert.Equal("Game 570", stored.Game.Name);
        Assert.Equal("https://manual.example/cover.jpg", stored.Game.CoverImageUrl);
        Assert.Equal(0, await CountPlayLogEntriesAsync(after, sub));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_already_imported_takes_precedence_over_missing_or_invalid_provider_data(bool invalidMetadata)
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        await ImportGames(sub, [570]);
        await using (var db = CreateDbContext())
        {
            var entry = await db.LibraryEntries.SingleAsync(e => e.Library.UserId == sub);
            entry.SteamPlaytimeMinutes = 100;
            await db.SaveChangesAsync();
        }
        _factory.FakeSteam.SetOwnedGames(invalidMetadata
            ? [new SteamOwnedGame(570, " ", 150, null, null)]
            : []);

        var response = await ClientFor(sub).PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570, 730 } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(result.GetProperty("imported").EnumerateArray());
        Assert.Equal(570L, Assert.Single(result.GetProperty("alreadyImported").EnumerateArray()).GetInt64());
        Assert.Equal(730L, Assert.Single(result.GetProperty("unavailable").EnumerateArray()).GetInt64());
        Assert.Empty(result.GetProperty("invalid").EnumerateArray());
        await using var after = CreateDbContext();
        var stored = await after.LibraryEntries.SingleAsync(e => e.Library.UserId == sub);
        Assert.Equal(invalidMetadata ? 150L : 100L, stored.SteamPlaytimeMinutes);
        Assert.Equal(1, await after.Platforms.CountAsync(p => p.Library.UserId == sub));
        Assert.Equal(0, await CountPlayLogEntriesAsync(after, sub));
    }

    [Fact]
    public async Task Import_missing_provider_app_does_not_expose_another_librarys_import()
    {
        var owner = NewSub();
        await LinkSteamAccount(owner);
        await ImportGames(owner, [570]);
        await ClientFor(owner).DeleteAsync("/steam/link");
        var caller = NewSub();
        await LinkSteamAccount(caller);
        _factory.FakeSteam.SetOwnedGames([]);

        var response = await ClientFor(caller).PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(result.GetProperty("imported").EnumerateArray());
        Assert.Empty(result.GetProperty("alreadyImported").EnumerateArray());
        Assert.Empty(result.GetProperty("invalid").EnumerateArray());
        Assert.Equal(570L, Assert.Single(result.GetProperty("unavailable").EnumerateArray()).GetInt64());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Steam_refresh_and_reimport_preserve_manually_edited_family_friendly(bool classification)
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo")]);
        var client = ClientFor(sub);
        var import = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        import.EnsureSuccessStatusCode();
        var games = (await client.GetFromJsonAsync<JsonElement[]>("/video-games"))!;
        var game = Assert.Single(games);
        Assert.False(game.GetProperty("isFamilyFriendly").GetBoolean());
        var gameId = game.GetProperty("id").GetGuid();
        var edit = await client.PutAsJsonAsync($"/video-games/{gameId}", new
        {
            name = "Manually edited", acquisitionStatus = "Owned",
            platformIds = game.GetProperty("platformIds").EnumerateArray().Select(p => p.GetGuid()).ToArray(),
            isFamilyFriendly = classification,
        });
        edit.EnsureSuccessStatusCode();

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Steam name", 150, null, "new_logo")]);
        var preview = await client.PostAsync("/steam/library/preview", null);
        preview.EnsureSuccessStatusCode();
        await AssertClassification();

        var reimport = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        reimport.EnsureSuccessStatusCode();
        var result = await reimport.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(result.GetProperty("imported").EnumerateArray());
        Assert.Equal(570, Assert.Single(result.GetProperty("alreadyImported").EnumerateArray()).GetInt64());
        await AssertClassification();

        async Task AssertClassification()
        {
            var current = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/video-games"))!);
            Assert.Equal(classification, current.GetProperty("isFamilyFriendly").GetBoolean());
            await using var db = CreateDbContext();
            var stored = await db.Games.Include(g => g.LibraryEntry).SingleAsync(g => g.Id == gameId);
            Assert.Equal(classification, stored.IsFamilyFriendly);
            Assert.Equal("Manually edited", stored.Name);
            Assert.Equal(150L, stored.LibraryEntry.SteamPlaytimeMinutes);
        }
    }

    [Fact]
    public async Task Import_stores_steam_playtime_in_library_entry()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(123L, entry.SteamPlaytimeMinutes);
    }

    [Fact]
    public async Task Import_uses_steam_logo_url_for_cover_when_available()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var game = await db.Games.SingleAsync(g => g.LibraryEntry.LibraryId == library.Id);
        Assert.Equal("https://cdn.akamai.steamstatic.com/steam/apps/570/logo_hash_123.jpg", game.CoverImageUrl);
    }

    [Fact]
    public async Task Import_does_not_overwrite_manually_assigned_cover()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);

        await using (var db = CreateDbContext())
        {
            var lib = await db.Libraries.SingleAsync(l => l.UserId == sub);
            var steamPlatform = new Core.Platforms.Platform { Id = Guid.NewGuid(), LibraryId = lib.Id, Name = "Steam" };
            db.Platforms.Add(steamPlatform);
            var g = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = "Dota 2",
                CoverImageUrl = "https://manual.example/cover.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var entry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = lib.Id,
                GameId = g.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
                SteamAppId = 570,
            };
            g.LibraryEntry = entry;
            entry.GamePlatforms.Add(new Core.Games.GamePlatform { LibraryEntryId = entry.Id, PlatformId = steamPlatform.Id });
            db.Games.Add(g);
            await db.SaveChangesAsync();
        }

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(570L, body.GetProperty("alreadyImported").EnumerateArray().Select(e => e.GetInt64()));

        await using var afterDb = CreateDbContext();
        var lib2 = await afterDb.Libraries.SingleAsync(l => l.UserId == sub);
        var g2 = await afterDb.Games.SingleAsync(ga => ga.LibraryEntry.LibraryId == lib2.Id);
        Assert.Equal("https://manual.example/cover.jpg", g2.CoverImageUrl);
    }

    [Fact]
    public async Task Reimport_same_game_calculates_playtime_delta()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 150, null, "logo_hash_123")]);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(570L, body.GetProperty("alreadyImported").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(50, SteamIntegrationRules.CalculatePlaytimeDelta(150, 100));

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(150L, entry.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Reimport_same_game_with_unchanged_playtime_keeps_total_and_delta_zero()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, SteamIntegrationRules.CalculatePlaytimeDelta(100, 100));

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(100L, entry.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Reimport_same_game_with_decreased_playtime_updates_baseline_and_delta_zero()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 50, null, "logo_hash_123")]);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, SteamIntegrationRules.CalculatePlaytimeDelta(50, 100));

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(50L, entry.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Import_missing_steam_playtime_stores_null()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Null(entry.SteamPlaytimeMinutes);
    }

    [Fact]
    public async Task Reimport_same_game_with_missing_playtime_preserves_existing_total()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", null, null, "logo_hash_123")]);

        var response = await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, SteamIntegrationRules.CalculatePlaytimeDelta(null, 100));

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(100L, entry.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Preview_refreshes_already_imported_steam_playtime()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 150, null, "logo_hash_123")]);

        var response = await client.PostAsync("/steam/library/preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var entry = await db.LibraryEntries.SingleAsync(le => le.LibraryId == library.Id);
        Assert.Equal(150L, entry.SteamPlaytimeMinutes);
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Reimport_does_not_create_play_log_entries()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 100, null, "logo_hash_123")]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 150, null, "logo_hash_123")]);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        Assert.Equal(0, await CountPlayLogEntriesAsync(db, sub));
    }

    [Fact]
    public async Task Import_without_steam_logo_uses_no_cover()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, null, null)]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var game = await db.Games.SingleAsync(g => g.LibraryEntry.LibraryId == library.Id);
        Assert.Null(game.CoverImageUrl);
    }

    [Fact]
    public async Task Import_with_steam_icon_but_no_logo_uses_no_cover()
    {
        var sub = NewSub();
        await LinkSteamAccount(sub);
        _factory.FakeSteam.SetOwnedGames([new SteamOwnedGame(570, "Dota 2", 123, "icon_hash_123", null)]);
        var client = ClientFor(sub);

        await client.PostAsJsonAsync("/steam/library/import", new { steamAppIds = new long[] { 570 } });

        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleAsync(l => l.UserId == sub);
        var game = await db.Games.SingleAsync(g => g.LibraryEntry.LibraryId == library.Id);
        Assert.Null(game.CoverImageUrl);
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

    private HttpClient RedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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
        // startUrl format: https://api.example.test/steam/link/start/{rawState}
        var rawState = startUrl.Split("/steam/link/start/")[1];

        var callbackParams = BuildCallbackParams(rawState);
        var callbackUrl = "/steam/link/callback?" + string.Join("&", callbackParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        var callbackClient = RedirectClient();
        var callbackResponse = await callbackClient.GetAsync(callbackUrl, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
    }

    private async Task<string> CreateRawState(string sub, bool expired = false)
    {
        await using var db = CreateDbContext();
        var library = await db.Libraries.SingleOrDefaultAsync(l => l.UserId == sub);
        if (library is null)
        {
            library = new Core.Libraries.Library { Id = Guid.NewGuid(), UserId = sub };
            db.Libraries.Add(library);
            await db.SaveChangesAsync();
        }
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

    private static Task<int> CountPlayLogEntriesAsync(GameLibraryDbContext db, string sub) =>
        db.PlayLogEntries.CountAsync(e => e.LibraryEntry.Library.UserId == sub);

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
