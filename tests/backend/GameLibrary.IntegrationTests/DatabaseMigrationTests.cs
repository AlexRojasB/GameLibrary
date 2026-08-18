using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Serialized with the other database-touching integration tests so schema
/// assertions and migrations never race each other.
/// </summary>
[Collection("Database")]
public class DatabaseMigrationTests
{
    [Fact]
    public async Task Migrations_ApplyToRealPostgres_AndCreateOnlyEfInfrastructure()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        Assert.True(
            !string.IsNullOrWhiteSpace(connectionString),
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run this integration test.");

        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new GameLibraryDbContext(options);

        Assert.True(await db.Database.CanConnectAsync());

        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQuery<string>($"""
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                """)
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                "__EFMigrationsHistory",
                "libraries",
                "platforms",
                "games",
                "library_entries",
                "genres",
                "game_genres",
                "game_platforms",
            }.OrderBy(x => x),
            tables.OrderBy(x => x));
    }

    [Fact]
    public async Task AddBoardGameManagement_AddsBoardGameColumnsAndCheckConstraints()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        Assert.True(
            !string.IsNullOrWhiteSpace(connectionString),
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run this integration test.");

        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new GameLibraryDbContext(options);
        await db.Database.MigrateAsync();

        var columns = await db.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'games'
                """)
            .ToListAsync();

        foreach (var column in new[] { "minimum_players", "maximum_players", "approximate_duration", "interaction_type" })
        {
            Assert.Contains(column, columns);
        }

        var constraints = await db.Database
            .SqlQuery<string>($"""
                SELECT conname AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE c.contype = 'c' AND t.relname = 'games'
                """)
            .ToListAsync();

        foreach (var constraint in new[]
        {
            "ck_games_minimum_players",
            "ck_games_maximum_players",
            "ck_games_approximate_duration",
            "ck_games_interaction_type",
            "ck_games_board_players_required",
            "ck_games_board_columns_video_null",
        })
        {
            Assert.Contains(constraint, constraints);
        }
    }

    [Fact]
    public async Task AddVideoGameManagement_SeedsFifteenGenres_AndHasAtMostOneGameIdIndex()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        Assert.True(
            !string.IsNullOrWhiteSpace(connectionString),
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run this integration test.");

        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new GameLibraryDbContext(options);
        await db.Database.MigrateAsync();

        Assert.Equal(15, await db.Genres.CountAsync());

        var gameIdIndex = await db.Database
            .SqlQuery<string>($"""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public' AND indexname = 'ix_library_entries_game_id'
                """)
            .ToListAsync();
        Assert.Single(gameIdIndex);

        var platformFkDeleteRule = await db.Database
            .SqlQuery<string>($"""
                SELECT confdeltype::text AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class rt ON rt.oid = c.confrelid
                WHERE c.contype = 'f'
                  AND t.relname = 'game_platforms'
                  AND rt.relname = 'platforms'
                """)
            .ToListAsync();
        Assert.Equal(new[] { "r" }, platformFkDeleteRule);
    }
}