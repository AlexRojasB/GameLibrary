using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class PickerRoundMigrationTests
{
    [Fact]
    public async Task UpgradeAndRollbackPreserveCatalogAndCurrentlyPlayingAndEnforceRoundConstraints()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        var schema = "picker_round_" + Guid.NewGuid().ToString("N");
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
            const string previous = "20261125053304_AddCurrentlyPlayingSlots";
            const string current = "20261125151437_AddPickerRounds";
            await migrator.MigrateAsync(previous);
            var library = Guid.NewGuid();
            var game = Guid.NewGuid();
            var entry = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO libraries(id,user_id) VALUES ({library},{Guid.NewGuid().ToString()})");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO games(id,game_type,name,minimum_players,maximum_players,created_at)
                VALUES ({game},'BoardGame','Preserved',1,4,{DateTimeOffset.UtcNow})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_entries(id,library_id,game_id,acquisition_status) VALUES ({entry},{library},{game},'Owned')
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO currently_playing(library_id,game_type,library_entry_id) VALUES ({library},'BoardGame',{entry})
                """);
            await migrator.MigrateAsync(current);
            Assert.Empty(await db.PickerRounds.ToListAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO picker_rounds(library_id,mode,revision,filters,state,current_library_entry_id,
                    history_library_entry_ids,shown_library_entry_ids,last_operation_id,last_operation_hash)
                VALUES ({library},'All',1,{"{}"}::jsonb,NULL,NULL,ARRAY[]::uuid[],ARRAY[]::uuid[],{Guid.NewGuid()},{new string('a', 64)})
                """);
            foreach (var sql in new[]
            {
                "UPDATE picker_rounds SET mode='Invalid'", "UPDATE picker_rounds SET revision=0",
                "UPDATE picker_rounds SET state='Invalid'", "UPDATE picker_rounds SET filters='[]'::jsonb",
                "UPDATE picker_rounds SET last_operation_hash='invalid'",
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            }
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO picker_rounds SELECT * FROM picker_rounds"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            await migrator.MigrateAsync(previous);
            Assert.Equal("Preserved", (await db.Games.SingleAsync()).Name);
            Assert.Equal(entry, (await db.CurrentlyPlayingSlots.SingleAsync()).LibraryEntryId);
            // Only query columns present at this historical rollback boundary.
            Assert.Equal(game, await db.LibraryEntries.Select(e => e.GameId).SingleAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {quoted} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
