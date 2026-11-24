using System.Security.Cryptography;
using System.Text;
using GameLibrary.Core.Steam;

namespace GameLibrary.Core.Tests;

public sealed class SteamSecureLinkRulesTests
{
    [Fact]
    public void Creation_uses_two_independent_RNG_draws_not_incoming_or_state_derived_proof()
    {
        var draws = 0;
        var secrets = SteamSecureLinkRules.NewSecrets(() => Enumerable.Repeat((byte)++draws, 32).ToArray());
        Assert.Equal(2, draws);
        Assert.True(SteamSecureLinkRules.Decode(secrets.State)!.All(b => b == 1));
        Assert.True(SteamSecureLinkRules.Decode(secrets.Proof)!.All(b => b == 2));
        Assert.Equal(nameof(SteamLinkSecrets), secrets.ToString());
    }
    [Fact]
    public void Verifier_uses_network_UUIDs_UTF8_byte_length_and_versioned_domain()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var library = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
        byte[] proof = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        // Independently assemble the exact byte representation (U is three UTF8 bytes).
        var input = Encoding.UTF8.GetBytes("GameLibrary/SteamLink/v2/browser\0")
            .Concat(Convert.FromHexString("00112233445566778899aabbccddeeff00000003"))
            .Concat(Encoding.UTF8.GetBytes("aé"))
            .Concat(Convert.FromHexString("ffeeddccbbaa99887766554433221100")).Concat(proof).ToArray();
        Assert.Equal(SHA256.HashData(input), SteamSecureLinkRules.Verifier(id, "aé", library, proof));
        var row = new SteamLinkRequest { Id = id, LibraryId = library, ProtocolVersion = 2,
            InitiatingUserId = "aé", BrowserBindingHash = SteamSecureLinkRules.Verifier(id, "aé", library, proof) };
        Assert.True(SteamSecureLinkRules.Matches(row, SteamSecureLinkRules.Encode(proof)));
        row.Id = Guid.NewGuid(); Assert.False(SteamSecureLinkRules.Matches(row, SteamSecureLinkRules.Encode(proof)));
        row.Id = id; row.LibraryId = Guid.NewGuid(); Assert.False(SteamSecureLinkRules.Matches(row, SteamSecureLinkRules.Encode(proof)));
        row.LibraryId = library; row.InitiatingUserId = "other"; Assert.False(SteamSecureLinkRules.Matches(row, SteamSecureLinkRules.Encode(proof)));
        row.InitiatingUserId = "aé"; row.ProtocolVersion = 1; Assert.False(SteamSecureLinkRules.Matches(row, SteamSecureLinkRules.Encode(proof)));
    }

    [Fact]
    public void Only_canonical_32_byte_secrets_round_trip()
    {
        var canonical = SteamSecureLinkRules.Encode(new byte[32]);
        Assert.Equal(new byte[32], SteamSecureLinkRules.Decode(canonical));
        foreach (var bad in new[] { null, "", canonical + "=", " " + canonical, canonical[..42], canonical[..42] + "B",
                     canonical.Replace('A', '+'), canonical + canonical, "%41" + canonical[1..], new string('é', 43) })
            Assert.Null(SteamSecureLinkRules.Decode(bad));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("18446744073709551615", true)]
    [InlineData("0", false)]
    [InlineData("01", false)]
    [InlineData("18446744073709551616", false)]
    [InlineData("+1", false)]
    [InlineData(" 1", false)]
    [InlineData("１", false)]
    [InlineData("", false)]
    public void Canonical_identity_matches_SQL_range(string value, bool valid) => Assert.Equal(valid, SteamSecureLinkRules.SteamId(value));

    [Fact]
    public void Exact_deadline_nonce_and_terminal_boundaries()
    {
        var now = DateTimeOffset.UtcNow;
        var row = new SteamLinkRequest { CreatedAt = now, ExpiresAt = now.AddMinutes(10) };
        Assert.True(SteamSecureLinkRules.Live(row, now));
        Assert.False(SteamSecureLinkRules.Live(row, row.ExpiresAt));
        Assert.False(SteamSecureLinkRules.Live(row, now.AddTicks(-1)));
        Assert.True(SteamSecureLinkRules.FreshNonce(now.AddSeconds(-60), row, now));
        Assert.False(SteamSecureLinkRules.FreshNonce(now.AddSeconds(-61), row, now));
        Assert.True(SteamSecureLinkRules.FreshNonce(now.AddSeconds(60), row, now));
        Assert.False(SteamSecureLinkRules.FreshNonce(now.AddSeconds(61), row, now));
        Assert.False(SteamSecureLinkRules.FreshNonce(now, row, now.AddMinutes(10).AddTicks(1)));
        row.CancelledAt = now; Assert.False(SteamSecureLinkRules.Live(row, now));
    }

    [Fact]
    public void Shipped_readiness_is_false_and_sensitive_contracts_do_not_stringify_payloads()
    {
        Assert.False(SteamSecureLinkRules.BuildReady);
        Assert.Equal(nameof(SteamLinkCreated), new SteamLinkCreated(Guid.NewGuid(), "state", "proof", default, default).ToString());
        Assert.Equal(nameof(SteamLinkCaller), new SteamLinkCaller("owner", default).ToString());
        Assert.Equal(nameof(SteamLinkSnapshot), new SteamLinkSnapshot("Linked", "identity", default).ToString());
        Assert.Equal("Steam linking operation could not complete.", new SteamSecureLinkException(SteamLinkFailure.OutcomeUnknown).Message);
    }
}
