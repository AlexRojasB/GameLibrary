using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class PickerExclusionMigrationTests
{
    [Fact]
    public async Task UpgradeIncludesExistingTypesByDefaultAndRollbackPreservesCatalogSlotsAndRounds()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var schema = "picker_exclusion_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        using var identifiers = new NpgsqlCommandBuilder();
        var quoted = identifiers.QuoteIdentifier(schema);
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {quoted}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema };
            await using var db = new GameLibraryDbContext(new DbContextOptionsBuilder<GameLibraryDbContext>()
                .UseNpgsql(isolated.ConnectionString, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
            var migrator = db.GetService<IMigrator>();
            const string previous = "20261125151437_AddPickerRounds";
            await migrator.MigrateAsync(previous);
            var library = Guid.NewGuid();
            var video = Guid.NewGuid();
            var board = Guid.NewGuid();
            var videoEntry = Guid.NewGuid();
            var boardEntry = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO libraries(id,user_id) VALUES ({library},{Guid.NewGuid().ToString()})");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO games(id,game_type,name,minimum_players,maximum_players,created_at)
                VALUES ({video},'VideoGame','Existing video',1,4,{DateTimeOffset.UtcNow}),
                       ({board},'BoardGame','Existing board',1,4,{DateTimeOffset.UtcNow})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_entries(id,library_id,game_id,acquisition_status)
                VALUES ({videoEntry},{library},{video},'Wishlist'),({boardEntry},{library},{board},'Owned')
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO currently_playing(library_id,game_type,library_entry_id) VALUES ({library},'BoardGame',{boardEntry})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO picker_rounds(library_id,mode,revision,filters,state,current_library_entry_id,
                    history_library_entry_ids,shown_library_entry_ids,last_operation_id,last_operation_hash)
                VALUES ({library},'All',1,{"{}"}::jsonb,'Success',{boardEntry},ARRAY[{boardEntry}],ARRAY[{boardEntry}],{Guid.NewGuid()},{new string('a', 64)})
                """);
            await migrator.MigrateAsync();
            var rows = await db.LibraryEntries.AsNoTracking().OrderBy(e => e.GameId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, e => Assert.False(e.IsExcludedFromPicker));
            var column = await db.Database.SqlQuery<string>($"""
                SELECT data_type || ':' || is_nullable || ':' || column_default AS "Value"
                FROM information_schema.columns WHERE table_schema={schema}
                AND table_name='library_entries' AND column_name='is_excluded_from_picker'
                """).SingleAsync();
            Assert.Equal("boolean:NO:false", column);
            // Omitting the flag on a newly inserted entry also uses the DB default.
            var extra = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO games(id,game_type,name,minimum_players,maximum_players,created_at)
                VALUES ({extra},'BoardGame','Default included',1,4,{DateTimeOffset.UtcNow})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_entries(id,library_id,game_id,acquisition_status)
                VALUES ({Guid.NewGuid()},{library},{extra},'Owned')
                """);
            Assert.False(await db.LibraryEntries.Where(e => e.GameId == extra).Select(e => e.IsExcludedFromPicker).SingleAsync());
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE library_entries SET is_excluded_from_picker=NULL"));
            Assert.Equal(PostgresErrorCodes.NotNullViolation, invalid.SqlState);
            Assert.False(db.Database.HasPendingModelChanges());
            await migrator.MigrateAsync(previous);
            Assert.Equal(3, await db.Games.CountAsync());
            Assert.Equal(3, await db.LibraryEntries.CountAsync());
            Assert.Equal(boardEntry, (await db.CurrentlyPlayingSlots.SingleAsync()).LibraryEntryId);
            var round = await db.PickerRounds.AsNoTracking().SingleAsync();
            Assert.Equal(boardEntry, round.CurrentLibraryEntryId);
            Assert.Equal(new[] { boardEntry }, round.HistoryLibraryEntryIds);
            Assert.Equal(new[] { boardEntry }, round.ShownLibraryEntryIds);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {quoted} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
