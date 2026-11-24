using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLibrary.IntegrationTests;

[Collection("Steam secure Core")]
public sealed class SteamV2OldSchemaTests(SteamSecureFixture fixture) : IClassFixture<SteamSecureFixture>
{
    [Fact]
    public async Task Rejection_only_build_preserves_existing_account_read_and_unlink_on_previous_schema()
    {
        await using var db = fixture.Db();
        // This fixture exclusively owns its random, loopback-validated disposable DB.
        // Build the previous schema forward from empty; never execute v2 Down.
        await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261029134813_AddCoverUploads");
        var user = "old-schema-" + Guid.NewGuid().ToString("N");
        var library = new Library { Id = Guid.NewGuid(), UserId = user };
        db.Libraries.Add(library);
        db.SteamAccounts.Add(new SteamAccount { Id = Guid.NewGuid(), LibraryId = library.Id, SteamId64 = "76561198000000077", LinkedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var state = SteamIntegrationRules.HashState(SteamIntegrationRules.GenerateRawState()); var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO steam_link_requests(id, library_id, state_hash, created_at, expires_at)
            VALUES ({id}, {library.Id}, {state}, clock_timestamp(), clock_timestamp() + interval '10 minutes')
            """);
        Assert.False(await SteamSecureLinkService.HasSchemaAsync(db, default));
        var steam = new SteamIntegrationService(db, new LibraryService(db), new FakeSteamClient());
        Assert.True((await steam.GetAccountAsync(user, default)).Linked);
        await steam.UnlinkAsync(user, default);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == library.Id));
        Assert.True(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM steam_link_requests WHERE id = {id} AND consumed_at IS NULL) AS \"Value\"").SingleAsync());
    }
}
