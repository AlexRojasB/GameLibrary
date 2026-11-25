using System.Globalization;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Steam;

namespace GameLibrary.IntegrationTests;

public sealed class SteamNonceFreshnessTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("future", -1, false)]
    [InlineData("future", 0, true)]
    [InlineData("future", 1, true)]
    [InlineData("age", -1, true)]
    [InlineData("age", 0, true)]
    [InlineData("age", 1, false)]
    [InlineData("creation", -1, true)]
    [InlineData("creation", 0, true)]
    [InlineData("creation", 1, false)]
    public void Nonce_freshness_has_exact_inclusive_database_time_boundaries(string boundary, int microseconds, bool expected)
    {
        // PostgreSQL timestamps retain microseconds; the nonce is a whole second.
        // Move the DB context, not the nonce, to exercise both sides precisely.
        var delta = TimeSpan.FromTicks(microseconds * 10L);
        var created = boundary == "creation" ? Epoch.AddSeconds(60).Add(delta) : Epoch.AddMinutes(-2);
        var now = boundary switch
        {
            "future" => Epoch.AddSeconds(-60).Add(delta),
            "age" => Epoch.AddMinutes(10).Add(delta),
            _ => created.AddSeconds(1),
        };
        AssertFresh(Epoch, created, now, expected);
    }

    [Fact]
    public void Nonce_at_host_now_plus_61_seconds_can_become_fresh_at_the_next_database_second()
    {
        var generatedAt = Epoch.AddTicks(TimeSpan.TicksPerSecond - 10); // ...00.999999
        var wire = generatedAt.AddSeconds(61).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var nonce = DateTimeOffset.ParseExact(wire, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var created = Epoch.AddSeconds(-1);
        Assert.Equal(Epoch.AddSeconds(61), nonce);
        Assert.Equal(TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(10)), nonce - generatedAt);
        AssertFresh(nonce, created, generatedAt, false);
        // No sleeps or clock mocking: just 1 microsecond of elapsed DB time makes
        // the same serialized nonce exactly +60 seconds, which policy permits.
        AssertFresh(nonce, created, Epoch.AddSeconds(1), true);
    }

    private static void AssertFresh(DateTimeOffset nonce, DateTimeOffset created, DateTimeOffset now, bool expected)
    {
        var row = new SteamLinkRequest { CreatedAt = created, ExpiresAt = now.AddMinutes(10) };
        Assert.True(SteamSecureLinkRules.Live(row, now));
        var assertion = new SteamOpenIdAssertion("synthetic-state", "1", nonce, new Dictionary<string, string?>());
        Assert.Equal(expected, SteamOpenIdProtocol.Fresh(assertion, new(row.CreatedAt, row.ExpiresAt, now)));
        Assert.Equal(expected, SteamSecureLinkRules.FreshNonce(nonce, row, now));
    }
}
