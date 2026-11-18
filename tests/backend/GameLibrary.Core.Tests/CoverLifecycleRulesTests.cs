using GameLibrary.Core.CoverUploads;

namespace GameLibrary.Core.Tests;

public class CoverLifecycleRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reservation_has_immutable_locator_and_bounded_live_deadlines()
    {
        var library = Guid.NewGuid();
        var upload = CoverUpload.Reserve(library, "https://storage.test/storage/v1/game-covers", 100, Now);
        Assert.Equal($"{library:N}/{upload.Id:N}/cover.jpg", upload.ObjectKey);
        Assert.Equal(CoverUploadState.Uploading, upload.State);
        Assert.Equal(Now.AddMinutes(15), upload.CleanupAfter);
        Assert.False(upload.IsLive(Now.AddMinutes(15)));
        upload.Complete(Now.AddMinutes(1));
        Assert.Equal(Now.AddMinutes(1).AddHours(24), upload.CleanupAfter);
        Assert.Null(upload.ClaimedGameId);
    }

    [Fact]
    public void Claim_is_single_consumption_and_receipt_survives_irreversible_retirement()
    {
        var upload = CoverUpload.Reserve(Guid.NewGuid(), "test-namespace", 100, Now);
        var game = Guid.NewGuid();
        Assert.Throws<CoverException>(() => upload.Claim(game, Now));
        upload.Complete(Now);
        upload.Claim(game, Now);
        Assert.Null(upload.CleanupAfter);
        Assert.Equal(game, upload.ClaimedGameId);
        Assert.Throws<CoverException>(() => upload.Claim(Guid.NewGuid(), Now));
        upload.Retire(Now.AddMinutes(1), abandoned: false);
        upload.Retire(Now.AddHours(10), abandoned: true);
        Assert.Equal(Now.AddMinutes(1), upload.RetiredAt);
        Assert.Equal(game, upload.ClaimedGameId);
        upload.Reschedule(Now.AddHours(10));
        Assert.Equal(Now.AddHours(10).AddMinutes(15), upload.CleanupAfter);
        Assert.Throws<CoverException>(() => upload.Complete(Now));
        Assert.Throws<CoverException>(() => upload.Claim(game, Now));
    }

    [Fact]
    public void Expired_upload_never_completes_or_attaches()
    {
        var uploading = CoverUpload.Reserve(Guid.NewGuid(), "namespace", 10, Now);
        Assert.Throws<CoverException>(() => uploading.Complete(Now.AddMinutes(15)));
        uploading.Complete(Now);
        Assert.Throws<CoverException>(() => uploading.Claim(Guid.NewGuid(), Now.AddHours(24)));
        uploading.Retire(Now.AddHours(24), abandoned: true);
        Assert.Equal(Now.AddHours(24).AddMinutes(10), uploading.CleanupAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2097153)]
    public void Reservation_rejects_invalid_canonical_sizes(int bytes) =>
        Assert.Throws<CoverException>(() => CoverUpload.Reserve(Guid.NewGuid(), "namespace", bytes, Now));
}
