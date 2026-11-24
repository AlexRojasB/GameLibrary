using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GameLibrary.Core.Steam;

internal static class SteamSecureLinkRules
{
    // PR 5 retains the hold until separately authorized hosted/privacy/operational
    // evidence and eligibility review. Runtime flags cannot enable this build.
    internal static bool BuildReady => false;

    internal static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(32));
    internal static SteamLinkSecrets NewSecrets(Func<byte[]>? draw = null)
    {
        draw ??= () => RandomNumberGenerator.GetBytes(32);
        var state = draw();
        var proof = draw();
        if (state.Length != 32 || proof.Length != 32) throw new InvalidOperationException("Invalid secret source.");
        return new(Encode(state), Encode(proof));
    }
    internal static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[]? Decode(string? value)
    {
        if (value is not { Length: 43 } || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
            return bytes.Length == 32 && Encode(bytes) == value ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    internal static byte[] Verifier(Guid transactionId, string userId, Guid libraryId, ReadOnlySpan<byte> proof)
    {
        if (proof.Length != 32) throw new ArgumentException("Invalid browser proof.");
        var user = Encoding.UTF8.GetBytes(userId);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("GameLibrary/SteamLink/v2/browser\0"u8);
        hash.AppendData(transactionId.ToByteArray(bigEndian: true));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)user.Length));
        hash.AppendData(size);
        hash.AppendData(user);
        hash.AppendData(libraryId.ToByteArray(bigEndian: true));
        hash.AppendData(proof);
        return hash.GetHashAndReset();
    }

    internal static bool Matches(SteamLinkRequest row, string? proof)
    {
        var bytes = Decode(proof);
        return row.ProtocolVersion == 2 && row.InitiatingUserId is { Length: > 0 }
            && row.BrowserBindingHash is { Length: 32 } && bytes is not null
            && CryptographicOperations.FixedTimeEquals(row.BrowserBindingHash,
                Verifier(row.Id, row.InitiatingUserId, row.LibraryId, bytes));
    }

    internal static bool SteamId(string? value) => value is { Length: > 0 and <= 20 }
        && value[0] is >= '1' and <= '9' && value.All(char.IsAsciiDigit)
        && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    internal static bool Live(SteamLinkRequest row, DateTimeOffset now) =>
        row.CancelledAt is null && now >= row.CreatedAt && now < row.ExpiresAt;

    internal static bool FreshNonce(DateTimeOffset nonce, SteamLinkRequest row, DateTimeOffset now) =>
        nonce >= now.AddMinutes(-10) && nonce >= row.CreatedAt.AddSeconds(-60) && nonce <= now.AddSeconds(60);
}
