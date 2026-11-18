using GameLibrary.Core.Games;
using GameLibrary.Core.Steam;

namespace GameLibrary.Core.Tests;

public class SteamIntegrationRulesTests
{
    [Fact]
    public void ParseSteamId64FromClaimedId_accepts_valid_steam_claimed_id()
    {
        var steamId = SteamIntegrationRules.ParseSteamId64FromClaimedId("https://steamcommunity.com/openid/id/76561198000000000");

        Assert.Equal("76561198000000000", steamId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/openid/id/76561198000000000")]
    [InlineData("https://steamcommunity.com/openid/id/not-a-number")]
    public void ParseSteamId64FromClaimedId_rejects_malformed_claimed_id(string? claimedId)
    {
        Assert.Throws<InvalidSteamOpenIdAssertionException>(() =>
            SteamIntegrationRules.ParseSteamId64FromClaimedId(claimedId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4294967295)]
    public void ValidateSteamAppId_accepts_positive_uint32_range(long value)
    {
        Assert.Equal((uint)value, SteamIntegrationRules.ValidateSteamAppId(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4294967296)]
    public void ValidateSteamAppId_rejects_out_of_range_values(long value)
    {
        Assert.Throws<InvalidSteamImportRequestException>(() => SteamIntegrationRules.ValidateSteamAppId(value));
    }

    [Fact]
    public void ValidateImportRequest_deduplicates_preserving_first_occurrence()
    {
        var appIds = SteamIntegrationRules.ValidateImportRequest([570, 730, 570, 440]);

        Assert.Equal([570u, 730u, 440u], appIds);
    }

    [Fact]
    public void ValidateImportRequest_rejects_empty_selection()
    {
        var ex = Assert.Throws<InvalidSteamImportRequestException>(() => SteamIntegrationRules.ValidateImportRequest([]));

        Assert.Equal("Select at least one Steam game to import.", ex.Message);
    }

    [Fact]
    public void ValidateImportRequest_rejects_more_than_100_values_before_deduplication()
    {
        var values = Enumerable.Range(1, 101).Select(i => (long)i).ToList();

        Assert.Throws<InvalidSteamImportRequestException>(() => SteamIntegrationRules.ValidateImportRequest(values));
    }

    [Fact]
    public void NormalizeCandidates_skips_invalid_metadata_and_marks_already_imported()
    {
        var longName = new string('x', 101);
        var candidates = SteamIntegrationRules.NormalizeCandidates(
            [
                new SteamOwnedGame(570, " Dota 2 ", 123, null, null),
                new SteamOwnedGame(0, "Invalid", 1, null, null),
                new SteamOwnedGame(730, "   ", 1, null, null),
                new SteamOwnedGame(440, longName, 1, null, null),
                new SteamOwnedGame(10, "alpha", null, null, null),
            ],
            new HashSet<uint> { 570 });

        Assert.Equal([10u, 570u], candidates.Select(c => c.SteamAppId).ToList());
        Assert.Equal("alpha", candidates[0].Name);
        Assert.Equal("Dota 2", candidates[1].Name);
        Assert.True(candidates[1].AlreadyImported);
    }

    [Theory]
    [InlineData("openid.mode", "checkid_setup")]
    [InlineData("openid.op_endpoint", "https://evil.example/openid")]
    [InlineData("openid.return_to", "https://api.example.test/wrong")]
    [InlineData("openid.realm", "https://api.example.test/wrong")]
    public void ValidateOpenIdAssertion_rejects_wrong_trusted_fields(string key, string value)
    {
        var parameters = ValidOpenIdParameters();
        parameters[key] = value;

        Assert.Throws<InvalidSteamOpenIdAssertionException>(() =>
            SteamIntegrationRules.ValidateOpenIdAssertion(
                parameters,
                "https://steamcommunity.com/openid/login",
                "https://api.example.test/steam/link/callback?state=abc",
                "https://api.example.test/",
                steamVerified: true));
    }

    [Fact]
    public void ValidateOpenIdAssertion_rejects_identity_mismatch()
    {
        var parameters = ValidOpenIdParameters();
        parameters["openid.identity"] = SteamIntegrationRules.ClaimedIdPrefix + "76561198000000001";

        Assert.Throws<InvalidSteamOpenIdAssertionException>(() =>
            SteamIntegrationRules.ValidateOpenIdAssertion(
                parameters,
                "https://steamcommunity.com/openid/login",
                "https://api.example.test/steam/link/callback?state=abc",
                "https://api.example.test/",
                steamVerified: true));
    }

    [Fact]
    public void ValidateOpenIdAssertion_rejects_false_steam_verification()
    {
        Assert.Throws<InvalidSteamOpenIdAssertionException>(() =>
            SteamIntegrationRules.ValidateOpenIdAssertion(
                ValidOpenIdParameters(),
                "https://steamcommunity.com/openid/login",
                "https://api.example.test/steam/link/callback?state=abc",
                "https://api.example.test/",
                steamVerified: false));
    }

    [Fact]
    public void ValidateOpenIdAssertion_accepts_valid_assertion()
    {
        var steamId = SteamIntegrationRules.ValidateOpenIdAssertion(
            ValidOpenIdParameters(),
            "https://steamcommunity.com/openid/login",
            "https://api.example.test/steam/link/callback?state=abc",
            "https://api.example.test/",
            steamVerified: true);

        Assert.Equal("76561198000000000", steamId);
    }

    [Theory]
    [InlineData(100L, 50L, 50L)]
    [InlineData(100L, 100L, 0L)]
    [InlineData(50L, 100L, 0L)]
    [InlineData(100L, null, 0L)]
    [InlineData(null, 50L, 0L)]
    [InlineData(null, null, 0L)]
    [InlineData(2147483648L, 0L, 2147483648L)]
    [InlineData(100L, 2147483648L, 0L)]
    public void CalculatePlaytimeDelta_returns_correct_delta(long? newPlaytime, long? storedPlaytime, long expectedDelta)
    {
        var delta = SteamIntegrationRules.CalculatePlaytimeDelta(newPlaytime, storedPlaytime);

        Assert.Equal(expectedDelta, delta);
    }

    [Theory]
    [InlineData(100L, 150, 50L, 150L)]
    [InlineData(100L, 100, 0L, 100L)]
    [InlineData(100L, 50, 0L, 50L)]
    [InlineData(100L, null, 0L, 100L)]
    [InlineData(null, 150, 0L, 150L)]
    [InlineData(null, null, 0L, null)]
    [InlineData(2147483648L, 100, 0L, 100L)]
    public void RefreshPlaytime_calculates_before_replacing_the_baseline(long? previous, int? current, long expectedDelta, long? expectedStored)
    {
        var entry = new LibraryEntry { SteamPlaytimeMinutes = previous, GameStatus = GameStatus.Playing, ProgressPercentage = 25 };

        var delta = SteamIntegrationService.RefreshPlaytime(entry, current);

        Assert.Equal(expectedDelta, delta);
        Assert.Equal(expectedStored, entry.SteamPlaytimeMinutes);
        Assert.Equal(GameStatus.Playing, entry.GameStatus);
        Assert.Equal(25, entry.ProgressPercentage);
    }

    [Fact]
    public void ValidateOpenIdAssertion_accepts_valid_assertion_with_real_rawstate()
    {
        var rawState = SteamIntegrationRules.GenerateRawState();
        var claimedId = SteamIntegrationRules.ClaimedIdPrefix + "76561198000000000";
        var parameters = new Dictionary<string, string?>
        {
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = "https://api.example.test/steam/link/callback?state=" + Uri.EscapeDataString(rawState),
            ["openid.realm"] = "https://api.example.test/",
        };

        var steamId = SteamIntegrationRules.ValidateOpenIdAssertion(
            parameters,
            "https://steamcommunity.com/openid/login",
            "https://api.example.test/steam/link/callback?state=" + Uri.EscapeDataString(rawState),
            "https://api.example.test/",
            steamVerified: true);

        Assert.Equal("76561198000000000", steamId);
    }

    [Fact]
    public void ValidateOpenIdAssertion_accepts_valid_assertion_with_state_parameter()
    {
        var rawState = SteamIntegrationRules.GenerateRawState();
        var claimedId = SteamIntegrationRules.ClaimedIdPrefix + "76561198000000000";
        var parameters = new Dictionary<string, string?>
        {
            ["state"] = rawState,
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = "https://api.example.test/steam/link/callback?state=" + Uri.EscapeDataString(rawState),
            ["openid.realm"] = "https://api.example.test/",
        };

        var steamId = SteamIntegrationRules.ValidateOpenIdAssertion(
            parameters,
            "https://steamcommunity.com/openid/login",
            "https://api.example.test/steam/link/callback?state=" + Uri.EscapeDataString(rawState),
            "https://api.example.test/",
            steamVerified: true);

        Assert.Equal("76561198000000000", steamId);
    }

    private static Dictionary<string, string?> ValidOpenIdParameters()
    {
        var claimedId = SteamIntegrationRules.ClaimedIdPrefix + "76561198000000000";
        return new Dictionary<string, string?>
        {
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = "https://api.example.test/steam/link/callback?state=abc",
            ["openid.realm"] = "https://api.example.test/",
        };
    }
}
