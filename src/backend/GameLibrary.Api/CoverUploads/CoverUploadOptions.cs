using System.Net;
using System.Text.Json;

namespace GameLibrary.Api.CoverUploads;

public sealed class CoverUploadOptions
{
    public bool Enabled { get; set; }
    public string? StorageBaseUrl { get; set; }
    public string? StorageCredential { get; set; }
    public string Bucket { get; set; } = "game-covers";
    public bool AllowLocalHttp { get; set; }

    internal CoverStorageConfiguration? GetStorageConfiguration(IHostEnvironment environment)
    {
        var value = StorageBaseUrl;
        if (Bucket != "game-covers" || string.IsNullOrWhiteSpace(value) || value.Length > 900
            || value.Any(char.IsWhiteSpace) || value.IndexOfAny(['?', '#', '\\', '%']) >= 0
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            if (uri.Scheme != Uri.UriSchemeHttp || !AllowLocalHttp
                || !(environment.IsDevelopment() || environment.IsEnvironment("Test"))
                || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
                || !IPAddress.IsLoopback(address)
                // Reject names and alternate numeric spellings normalized by System.Uri.
                || !value.StartsWith("http://" + uri.Authority, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        var authorityEnd = value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        var path = authorityEnd < 0 ? "" : value[authorityEnd..].TrimEnd('/');
        if (path.Split('/').Skip(1).Any(segment => segment.Length == 0 || segment is "." or ".."
            || segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            return null;

        var credential = StorageCredential;
        if (string.IsNullOrEmpty(credential) || credential.Length > 8192
            || value.Contains(credential, StringComparison.Ordinal)
            || credential.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.'))
            return null;

        var legacy = !credential.StartsWith("sb_secret_", StringComparison.Ordinal);
        if (legacy)
        {
            // This only rejects accidental anon/user JWT configuration. Supabase verifies the signature.
            var parts = credential.Split('.');
            if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty)) return null;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
                if (!json.RootElement.TryGetProperty("role", out var role)
                    || role.ValueKind != JsonValueKind.String || role.GetString() != "service_role")
                    return null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }
        else if (credential.Length == "sb_secret_".Length || credential.Contains('.'))
            return null;

        var baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + path + "/");
        return new CoverStorageConfiguration(baseUri, baseUri.AbsoluteUri + Bucket, Bucket, credential, legacy);
    }
}

// Not a record: generated ToString output must never include a credential.
internal sealed class CoverStorageConfiguration(Uri baseUri, string storageNamespace, string bucket,
    string credential, bool legacyCredential)
{
    internal Uri BaseUri { get; } = baseUri;
    internal string StorageNamespace { get; } = storageNamespace;
    internal string Bucket { get; } = bucket;
    internal string Credential { get; } = credential;
    internal bool LegacyCredential { get; } = legacyCredential;
}
