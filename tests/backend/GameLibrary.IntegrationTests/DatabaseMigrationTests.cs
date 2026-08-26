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
                "play_log_entries",
                "steam_accounts",
                "steam_link_requests",
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
            "ck_games_video_players_both_or_neither",
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

    [Fact]
    public async Task AddPlayLogEntries_AddsApprovedTableForeignKeyAndIndex()
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
                WHERE table_schema = 'public' AND table_name = 'play_log_entries'
                """)
            .ToListAsync();

        Assert.Equal(
            new[] { "id", "library_entry_id", "played_at", "created_at", "duration_minutes" }.OrderBy(x => x),
            columns.OrderBy(x => x));
        Assert.DoesNotContain("library_id", columns);

        var durationColumn = await db.Database
            .SqlQuery<string>($"""
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'play_log_entries'
                  AND column_name = 'duration_minutes'
                """)
            .SingleAsync();
        Assert.Equal("YES", durationColumn);

        var foreignKey = await db.Database
            .SqlQuery<string>($"""
                SELECT c.conname AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class rt ON rt.oid = c.confrelid
                WHERE c.contype = 'f'
                  AND t.relname = 'play_log_entries'
                  AND rt.relname = 'library_entries'
                  AND c.confdeltype = 'c'
                """)
            .ToListAsync();
        Assert.Equal(new[] { "fk_play_log_entries_library_entries_library_entry_id" }, foreignKey);

        var directLibraryForeignKey = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class rt ON rt.oid = c.confrelid
                WHERE c.contype = 'f'
                  AND t.relname = 'play_log_entries'
                  AND rt.relname = 'libraries'
                """)
            .SingleAsync();
        Assert.Equal(0, directLibraryForeignKey);

        var indexes = await db.Database
            .SqlQuery<string>($"""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public' AND tablename = 'play_log_entries'
                """)
            .ToListAsync();
        Assert.Contains("ix_play_log_entries_library_entry_id", indexes);

        var checks = await db.Database
            .SqlQuery<string>($"""
                SELECT c.conname AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE c.contype = 'c'
                  AND t.relname = 'play_log_entries'
                """)
            .ToListAsync();
        Assert.Contains("ck_play_log_entries_duration_minutes_positive", checks);
    }

    [Fact]
    public async Task AddSteamIntegration_AddsApprovedTablesColumnConstraintsAndIndexes()
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

        var steamAppIdColumnType = await db.Database
            .SqlQuery<string>($"""
                SELECT data_type AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'library_entries'
                  AND column_name = 'steam_app_id'
                """)
            .SingleAsync();
        Assert.Equal("bigint", steamAppIdColumnType);

        var checks = await db.Database
            .SqlQuery<string>($"""
                SELECT c.conname AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE c.contype = 'c'
                  AND t.relname IN ('library_entries', 'steam_link_requests')
                """)
            .ToListAsync();
        Assert.Contains("ck_library_entries_steam_app_id", checks);
        Assert.Contains("ck_steam_link_requests_state_hash", checks);
        Assert.Contains("ck_steam_link_requests_expires_at", checks);

        var indexes = await db.Database
            .SqlQuery<string>($"""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname IN (
                    'ix_library_entries_library_id_steam_app_id',
                    'ix_steam_accounts_library_id',
                    'ix_steam_accounts_steam_id64',
                    'ix_steam_link_requests_state_hash')
                """)
            .ToListAsync();
        Assert.Contains("ix_library_entries_library_id_steam_app_id", indexes);
        Assert.Contains("ix_steam_accounts_library_id", indexes);
        Assert.Contains("ix_steam_accounts_steam_id64", indexes);
        Assert.Contains("ix_steam_link_requests_state_hash", indexes);

        var filteredIndex = await db.Database
            .SqlQuery<string>($"""
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname = 'ix_library_entries_library_id_steam_app_id'
                """)
            .SingleAsync();
        Assert.Contains("WHERE (steam_app_id IS NOT NULL)", filteredIndex);

        var libraryDeleteRules = await db.Database
            .SqlQuery<string>($"""
                SELECT t.relname || ':' || c.confdeltype::text AS "Value"
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_class rt ON rt.oid = c.confrelid
                WHERE c.contype = 'f'
                  AND t.relname IN ('steam_accounts', 'steam_link_requests')
                  AND rt.relname = 'libraries'
                ORDER BY t.relname
                """)
            .ToListAsync();
        Assert.Equal(["steam_accounts:c", "steam_link_requests:c"], libraryDeleteRules);
    }
}
