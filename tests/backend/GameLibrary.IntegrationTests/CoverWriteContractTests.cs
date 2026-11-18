using System.Text.Json;
using GameLibrary.Api.BoardGames;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Api.VideoGames;
using GameLibrary.Core.CoverUploads;

namespace GameLibrary.IntegrationTests;

public class CoverWriteContractTests
{
    private const string UploadId = "00000000-0000-0000-0000-000000000001";
    private const string EmptySnapshot = "{\"coverImageUrl\":null,\"coverUploadId\":null}";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Omitted_command_is_null_but_explicit_keep_retains_its_intent(bool isCreate)
    {
        Assert.Null(CoverUpdateParser.Parse(default, isCreate));
        Assert.Equal(new CoverUpdate("keep"), Parse("""{"intent":"keep"}""", isCreate));
    }

    [Theory]
    [InlineData("external")]
    [InlineData("upload")]
    [InlineData("remove")]
    public void Create_accepts_each_explicit_variant_without_a_snapshot(string intent)
    {
        var command = Parse(Command(intent), isCreate: true);

        Assert.Equal(intent, command!.Intent);
        Assert.Null(command.ExpectedCover);
        Assert.Equal(intent == "external" ? "https://example.test/cover.jpg" : null, command.Url);
        Assert.Equal(intent == "upload" ? Guid.Parse(UploadId) : (Guid?)null, command.UploadId);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("upload")]
    [InlineData("remove")]
    public void Update_accepts_each_variant_with_both_null_snapshot_fields(string intent)
    {
        var command = Parse(Command(intent, EmptySnapshot), isCreate: false);

        Assert.Equal(intent, command!.Intent);
        Assert.Equal(new CoverReference(null, null), command.ExpectedCover);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("upload")]
    [InlineData("remove")]
    public void Updates_accept_external_and_managed_snapshots_without_inferring_intent(string intent)
    {
        var external = Parse(Command(intent,
            """{"coverImageUrl":"https://example.test/cover.jpg","coverUploadId":null}"""), false);
        var managed = Parse(Command(intent,
            $$"""{"coverImageUrl":null,"coverUploadId":"{{UploadId}}"}"""), false);

        Assert.Equal(intent, external!.Intent);
        Assert.Equal(intent, managed!.Intent);
        Assert.Equal(new CoverReference("https://example.test/cover.jpg", null), external.ExpectedCover);
        Assert.Equal(new CoverReference(null, Guid.Parse(UploadId)), managed.ExpectedCover);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("upload")]
    [InlineData("remove")]
    public void Create_rejects_any_expected_cover_and_update_requires_it(string intent)
    {
        AssertInvalid(Command(intent, EmptySnapshot), true);
        AssertInvalid(Command(intent, "null"), true);
        AssertInvalid(Command(intent), false);
        AssertInvalid(Command(intent, "null"), false);
    }

    public static IEnumerable<object[]> InvalidCommands()
    {
        string[] invalid =
        [
            "null", "true", "1", "[]", "\"keep\"", "{}",
            """{"intent":null}""",
            """{"intent":1}""",
            """{"Intent":"keep"}""",
            """{"intent":"Keep"}""",
            """{"intent":" keep"}""",
            """{"intent":"unknown"}""",
            """{"intent":"keep","intent":"keep"}""",
            """{"intent":"keep","url":null}""",
            """{"intent":"keep","uploadId":null}""",
            """{"intent":"keep","expectedCover":null}""",
            $$"""{"intent":"keep","expectedCover":{{EmptySnapshot}}}""",
            """{"intent":"keep","unknown":null}""",
            """{"intent":"external"}""",
            """{"intent":"external","url":null}""",
            """{"intent":"external","url":12}""",
            """{"intent":"external","url":""}""",
            """{"intent":"external","url":"  "}""",
            """{"intent":"external","url":"not-a-url"}""",
            """{"intent":"external","url":"data:image/jpeg;base64,AAAA"}""",
            """{"intent":"external","url":"blob:https://example.test/id"}""",
            """{"intent":"external","url":"file:///cover.jpg"}""",
            """{"intent":"external","Url":"https://example.test/cover.jpg"}""",
            """{"intent":"external","url":"https://example.test/a","url":"https://example.test/b"}""",
            """{"intent":"external","url":"https://example.test/a","uploadId":null}""",
            """{"intent":"upload"}""",
            """{"intent":"upload","uploadId":null}""",
            """{"intent":"upload","uploadId":1}""",
            """{"intent":"upload","uploadId":"invalid"}""",
            $$"""{"intent":"upload","UploadId":"{{UploadId}}"}""",
            $$"""{"intent":"upload","uploadId":"{{UploadId}}","uploadId":"{{UploadId}}"}""",
            $$"""{"intent":"upload","uploadId":"{{UploadId}}","url":null}""",
            """{"intent":"remove","url":null}""",
            """{"intent":"remove","uploadId":null}"""
        ];

        foreach (var json in invalid)
        {
            yield return [json, true];
            yield return [json, false];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void Malformed_null_duplicate_wrong_case_or_incompatible_commands_are_rejected(string json, bool isCreate)
    {
        AssertInvalid(json, isCreate);
    }

    public static IEnumerable<object[]> InvalidSnapshots()
    {
        string[] invalid =
        [
            "null", "[]", "1", "true", "\"snapshot\"", "{}",
            """{"coverImageUrl":null}""",
            """{"coverUploadId":null}""",
            """{"CoverImageUrl":null,"coverUploadId":null}""",
            """{"coverImageUrl":null,"CoverUploadId":null}""",
            """{"coverImageUrl":null,"coverUploadId":null,"extra":null}""",
            """{"coverImageUrl":null,"coverImageUrl":null,"coverUploadId":null}""",
            """{"coverImageUrl":null,"coverUploadId":null,"coverUploadId":null}""",
            """{"coverImageUrl":42,"coverUploadId":null}""",
            """{"coverImageUrl":"","coverUploadId":null}""",
            """{"coverImageUrl":"blob:https://example.test/id","coverUploadId":null}""",
            """{"coverImageUrl":null,"coverUploadId":42}""",
            """{"coverImageUrl":null,"coverUploadId":"invalid"}""",
            $$"""{"coverImageUrl":"https://example.test/a","coverUploadId":"{{UploadId}}"}"""
        ];

        foreach (var snapshot in invalid)
        {
            foreach (var intent in new[] { "external", "upload", "remove" })
            {
                yield return [Command(intent, snapshot)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshots))]
    public void Snapshots_require_exactly_both_explicit_nullable_exclusive_fields(string json)
    {
        AssertInvalid(json, false);
    }

    [Fact]
    public void Update_rejects_wrong_case_or_duplicate_snapshot_field_on_command()
    {
        AssertInvalid($$"""{"intent":"remove","ExpectedCover":{{EmptySnapshot}}}""", false);
        AssertInvalid($$"""{"intent":"remove","expectedCover":{{EmptySnapshot}},"expectedCover":{{EmptySnapshot}}}""", false);
    }

    [Fact]
    public void Urls_retain_input_for_authoritative_core_normalization_but_have_bounded_http_syntax()
    {
        const string url = "  https://example.test/cover.jpg  ";
        Assert.Equal(url, Parse(JsonSerializer.Serialize(new { intent = "external", url }), true)!.Url);
        AssertInvalid(JsonSerializer.Serialize(new { intent = "external", url = "https://example.test/" + new string('a', 2048) }), true);
    }

    public static IEnumerable<object[]> LegacyWrites()
    {
        Type[] requestTypes =
        [
            typeof(CreateVideoGameRequest), typeof(UpdateVideoGameRequest),
            typeof(CreateBoardGameRequest), typeof(UpdateBoardGameRequest)
        ];
        foreach (var requestType in requestTypes)
        {
            foreach (var property in new[] { "coverImageUrl", "coverUploadId", "expectedCover", "CoverImageUrl" })
            {
                foreach (var value in new[] { "null", "42", "{}", "\"legacy\"" })
                {
                    yield return [requestType, property, value];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegacyWrites))]
    public void Every_game_request_captures_and_rejects_legacy_fields_regardless_of_value(Type requestType, string property, string value)
    {
        var request = JsonSerializer.Deserialize($$$"""{"name":"Game","{{{property}}}":{{{value}}},"coverUpdate":{"intent":"keep"}}""", requestType, JsonOptions)!;
        var additional = (Dictionary<string, JsonElement>)requestType.GetProperty("AdditionalProperties")!.GetValue(request)!;

        var exception = Assert.Throws<CoverException>(() => CoverUpdateParser.RejectLegacy(additional.Keys));

        Assert.Equal(CoverFailure.InvalidRequest, exception.Failure);
        Assert.Equal("cover_intent_required", exception.Code);
    }

    [Theory]
    [InlineData(typeof(CreateVideoGameRequest))]
    [InlineData(typeof(UpdateVideoGameRequest))]
    [InlineData(typeof(CreateBoardGameRequest))]
    [InlineData(typeof(UpdateBoardGameRequest))]
    public void Every_game_request_preserves_omission_null_and_other_full_form_fields(Type requestType)
    {
        var omitted = JsonSerializer.Deserialize("""{"name":"Game","rating":4,"isFamilyFriendly":true,"unrelated":1}""", requestType, JsonOptions)!;
        var explicitNull = JsonSerializer.Deserialize("""{"coverUpdate":null}""", requestType, JsonOptions)!;
        var coverProperty = requestType.GetProperty("CoverUpdate")!;
        var additional = (Dictionary<string, JsonElement>)requestType.GetProperty("AdditionalProperties")!.GetValue(omitted)!;

        Assert.Null(CoverUpdateParser.Parse((JsonElement)coverProperty.GetValue(omitted)!, true));
        var exception = Assert.Throws<CoverException>(() => CoverUpdateParser.Parse((JsonElement)coverProperty.GetValue(explicitNull)!, true));
        Assert.Equal(CoverFailure.InvalidRequest, exception.Failure);
        CoverUpdateParser.RejectLegacy(additional.Keys);
        Assert.Equal("Game", requestType.GetProperty("Name")!.GetValue(omitted));
        Assert.Equal(4, requestType.GetProperty("Rating")!.GetValue(omitted));
        Assert.Equal(true, requestType.GetProperty("IsFamilyFriendly")!.GetValue(omitted));
    }

    private static CoverUpdate? Parse(string json, bool isCreate)
    {
        using var document = JsonDocument.Parse(json);
        return CoverUpdateParser.Parse(document.RootElement, isCreate);
    }

    private static void AssertInvalid(string json, bool isCreate)
    {
        var exception = Assert.Throws<CoverException>(() => Parse(json, isCreate));
        Assert.Equal(CoverFailure.InvalidRequest, exception.Failure);
        Assert.Null(exception.Code);
    }

    private static string Command(string intent, string? snapshot = null)
    {
        var value = intent switch
        {
            "external" => ",\"url\":\"https://example.test/cover.jpg\"",
            "upload" => $",\"uploadId\":\"{UploadId}\"",
            _ => ""
        };
        var expected = snapshot is null ? "" : $",\"expectedCover\":{snapshot}";
        return $"{{\"intent\":\"{intent}\"{value}{expected}}}";
    }
}
