using System.Globalization;
using System.Text;
using GameLibrary.Core.Steam;

namespace GameLibrary.Api.Steam;

internal sealed record SteamOpenIdAssertion(string State, string SteamId64, DateTimeOffset NonceTime,
    IReadOnlyDictionary<string, string?> Fields)
{
    public override string ToString() => nameof(SteamOpenIdAssertion);
}

internal static class SteamOpenIdProtocol
{
    internal const string RequiredSigned = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> Positive = new(StringComparer.Ordinal)
    {
        "state", "openid.ns", "openid.mode", "openid.op_endpoint", "openid.claimed_id", "openid.identity",
        "openid.return_to", "openid.response_nonce", "openid.assoc_handle", "openid.signed", "openid.sig",
        "openid.realm", "openid.invalidate_handle",
    };

    internal static string AuthorizationUrl(SteamLinkConfiguration config, string state)
    {
        var values = new Dictionary<string, string>
        {
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace, ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = config.ReturnTo(state), ["openid.realm"] = config.Realm,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        };
        return SteamLinkConfiguration.Provider + "?" + string.Join('&', values.Select(p =>
            Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }

    internal static SteamOpenIdAssertion? Parse(HttpRequest request, SteamLinkConfiguration config)
    {
        var target = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>()?.RawTarget
            ?? request.Path + request.QueryString;
        if (Utf8.GetByteCount(target) > 16 * 1024) return null;
        var raw = request.QueryString.Value;
        if (string.IsNullOrEmpty(raw)) return null;
        var parts = raw[1..].Split('&');
        if (parts.Length > 32) return null;
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            foreach (var part in parts)
            {
                var equals = part.IndexOf('=');
                if (equals < 1) return null;
                var key = Decode(part[..equals]); var value = Decode(part[(equals + 1)..]);
                if (Utf8.GetByteCount(key) > 128 || Utf8.GetByteCount(value) > 8192 || value.Length == 0
                    || !fields.TryAdd(key, value)) return null;
            }
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or EncoderFallbackException) { return null; }
        if (!fields.TryGetValue("state", out var state) || SteamSecureLinkRules.Decode(state) is null
            || !fields.TryGetValue("openid.ns", out var ns) || ns != SteamIntegrationRules.OpenIdNamespace
            || !fields.TryGetValue("openid.mode", out var mode)) return null;
        // Unsigned negative/error/setup/cancel responses never authorize any write/provider call.
        if (mode != "id_res") return null;
        if (fields.Keys.Any(k => !Positive.Contains(k))
            || Positive.Except(["openid.realm", "openid.invalidate_handle"]).Any(k => !fields.ContainsKey(k))
            || fields["openid.op_endpoint"] != SteamLinkConfiguration.Provider
            || fields["openid.return_to"] != config.ReturnTo(state!)
            || fields.TryGetValue("openid.realm", out var realm) && realm != config.Realm
            || fields["openid.identity"] != fields["openid.claimed_id"]) return null;
        var identity = fields["openid.claimed_id"]!;
        if (!identity.StartsWith(SteamIntegrationRules.ClaimedIdPrefix, StringComparison.Ordinal)) return null;
        var id = identity[SteamIntegrationRules.ClaimedIdPrefix.Length..];
        if (!SteamSecureLinkRules.SteamId(id)) return null;
        var signed = fields["openid.signed"]!.Split(',');
        if (signed.Distinct(StringComparer.Ordinal).Count() != signed.Length
            || signed.Any(f => f.Length == 0 || !Positive.Contains("openid." + f))
            || RequiredSigned.Split(',').Any(f => !signed.Contains(f, StringComparer.Ordinal))) return null;
        var nonce = fields["openid.response_nonce"]!;
        if (nonce.Length <= 20 || !DateTimeOffset.TryParseExact(nonce[..20], "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var nonceTime)) return null;
        return new(state!, id, nonceTime, fields);
    }

    internal static bool Fresh(SteamOpenIdAssertion assertion, SteamLinkCallbackContext row) =>
        assertion.NonceTime >= row.DatabaseNow.AddMinutes(-10)
        && assertion.NonceTime >= row.CreatedAt.AddSeconds(-60)
        && assertion.NonceTime <= row.DatabaseNow.AddSeconds(60);

    private static string Decode(string value)
    {
        var encoded = Utf8.GetBytes(value);
        var decoded = new byte[encoded.Length]; var count = 0;
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '+') decoded[count++] = (byte)' ';
            else if (encoded[i] == '%')
            {
                if (i + 2 >= encoded.Length || !byte.TryParse(Encoding.ASCII.GetString(encoded, i + 1, 2),
                        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b)) throw new FormatException();
                decoded[count++] = b; i += 2;
            }
            else decoded[count++] = encoded[i];
        }
        return Utf8.GetString(decoded, 0, count);
    }
}
