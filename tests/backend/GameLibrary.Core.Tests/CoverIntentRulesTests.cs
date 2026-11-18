using GameLibrary.Core.CoverUploads;

namespace GameLibrary.Core.Tests;

public class CoverIntentRulesTests
{
    [Fact]
    public void Omission_is_keep_and_only_explicit_existing_changes_require_snapshot()
    {
        Assert.Equal("keep", CoverRules.Validate(null, false, null).Intent);
        Assert.Equal("keep", CoverRules.Validate(new("keep"), true, null).Intent);
        Assert.Equal("remove", CoverRules.Validate(new("remove"), true, null).Intent);
        Assert.Throws<CoverException>(() => CoverRules.Validate(new("remove"), false, null));
        Assert.Equal(new CoverReference(null, null), CoverRules.Validate(
            new("remove", ExpectedCover: new(null, null)), false, null).ExpectedCover);
    }

    [Theory]
    [InlineData("Keep")]
    [InlineData("unknown")]
    [InlineData("")]
    public void Intent_is_case_sensitive_and_unknown_is_invalid(string intent) =>
        Assert.Throws<CoverException>(() => CoverRules.Validate(new(intent), true, null));

    [Fact]
    public void Incompatible_fields_and_create_preconditions_are_rejected()
    {
        CoverUpdate[] invalid =
        [
            new("keep", Url: "https://example.test/image.jpg"),
            new("remove", UploadId: Guid.NewGuid()),
            new("upload"), new("upload", UploadId: Guid.Empty),
            new("external", Url: "https://example.test/image.jpg", UploadId: Guid.NewGuid()),
            new("keep", ExpectedCover: new(null, null)),
        ];
        foreach (var update in invalid) Assert.Throws<CoverException>(() => CoverRules.Validate(update, false, null));
        Assert.Throws<CoverException>(() => CoverRules.Validate(new("remove", ExpectedCover: new(null, null)), true, null));
        Assert.Throws<CoverException>(() => CoverRules.Validate(new("remove", ExpectedCover: new("https://example.test/x", Guid.NewGuid())), false, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("data:image/jpeg;base64,aA==")]
    [InlineData("blob:https://app.test/abc")]
    [InlineData("file:///image.jpg")]
    [InlineData("https://app.test/cover-uploads/00000000-0000-0000-0000-000000000001/content")]
    [InlineData("https://project.test/storage/v1/object/sign/game-covers/x?token=secret")]
    [InlineData("https://project.test/storage/v1/object/authenticated/game-covers/x")]
    public void Invalid_and_managed_external_sources_are_rejected(string url) =>
        Assert.Throws<CoverException>(() => CoverRules.Validate(new("external", Url: url), true, null));

    [Fact]
    public void Urls_are_trimmed_not_case_folded_and_expected_values_remain_ordinal()
    {
        var result = CoverRules.Validate(new("external", Url: " https://example.test/Cover.JPG ",
            ExpectedCover: new(" https://example.test/Old.JPG ", null)), false, null);
        Assert.Equal("https://example.test/Cover.JPG", result.Url);
        Assert.Equal("https://example.test/Old.JPG", result.ExpectedCover!.CoverImageUrl);
    }
}
