using System.Text.Json;
using GameLibrary.Core.CoverUploads;

namespace GameLibrary.Api.CoverUploads;

public static class CoverUpdateParser
{
    public static CoverUpdate? Parse(JsonElement element, bool isCreate)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidRequest();
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, property.Value))
            {
                throw InvalidRequest();
            }
        }

        if (!fields.TryGetValue("intent", out var intentElement)
            || intentElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidRequest();
        }

        var intent = intentElement.GetString();
        string[] allowedFields = intent switch
        {
            "keep" => ["intent"],
            "external" => isCreate ? ["intent", "url"] : ["intent", "url", "expectedCover"],
            "upload" => isCreate ? ["intent", "uploadId"] : ["intent", "uploadId", "expectedCover"],
            "remove" => isCreate ? ["intent"] : ["intent", "expectedCover"],
            _ => throw InvalidRequest()
        };

        if (fields.Count != allowedFields.Length || allowedFields.Any(field => !fields.ContainsKey(field)))
        {
            throw InvalidRequest();
        }

        string? url = null;
        Guid? uploadId = null;
        if (intent == "external")
        {
            url = ReadUrl(fields["url"]);
        }
        else if (intent == "upload")
        {
            uploadId = ReadUploadId(fields["uploadId"]);
        }

        CoverReference? expectedCover = null;
        if (!isCreate && intent != "keep")
        {
            var snapshot = fields["expectedCover"];
            if (snapshot.ValueKind != JsonValueKind.Object)
            {
                throw InvalidRequest();
            }

            var references = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in snapshot.EnumerateObject())
            {
                if (!references.TryAdd(property.Name, property.Value))
                {
                    throw InvalidRequest();
                }
            }

            if (references.Count != 2
                || !references.TryGetValue("coverImageUrl", out var expectedUrlElement)
                || !references.TryGetValue("coverUploadId", out var expectedUploadElement))
            {
                throw InvalidRequest();
            }

            var expectedUrl = expectedUrlElement.ValueKind == JsonValueKind.Null ? null : ReadUrl(expectedUrlElement);
            Guid? expectedUploadId = expectedUploadElement.ValueKind == JsonValueKind.Null ? null : ReadUploadId(expectedUploadElement);
            if (expectedUrl is not null && expectedUploadId is not null)
            {
                throw InvalidRequest();
            }

            expectedCover = new CoverReference(expectedUrl, expectedUploadId);
        }

        return new CoverUpdate(intent!, url, uploadId, expectedCover);
    }

    public static void RejectLegacy(IEnumerable<string> properties)
    {
        if (properties.Any(property => property.Equals("coverImageUrl", StringComparison.OrdinalIgnoreCase)
            || property.Equals("coverUploadId", StringComparison.OrdinalIgnoreCase)
            || property.Equals("expectedCover", StringComparison.OrdinalIgnoreCase)))
        {
            throw new CoverException(CoverFailure.InvalidRequest,
                "Refresh the application and use coverUpdate to change a cover.", "cover_intent_required");
        }
    }

    private static string ReadUrl(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw InvalidRequest();
        }

        var url = element.GetString()!;
        var trimmed = url.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 2048
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw InvalidRequest();
        }

        return url;
    }

    private static Guid ReadUploadId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || !element.TryGetGuid(out var id))
        {
            throw InvalidRequest();
        }

        return id;
    }

    private static CoverException InvalidRequest() =>
        new(CoverFailure.InvalidRequest, "Invalid cover request.");
}
