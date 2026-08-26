using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.Data;

public class GameLibraryDbContext : DbContext
{
    public GameLibraryDbContext(DbContextOptions<GameLibraryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<Platform> Platforms => Set<Platform>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<LibraryEntry> LibraryEntries => Set<LibraryEntry>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<GameGenre> GameGenres => Set<GameGenre>();

    public DbSet<GamePlatform> GamePlatforms => Set<GamePlatform>();

    public DbSet<PlayLogEntry> PlayLogEntries => Set<PlayLogEntry>();

    public DbSet<SteamAccount> SteamAccounts => Set<SteamAccount>();

    public DbSet<SteamLinkRequest> SteamLinkRequests => Set<SteamLinkRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Library>(entity =>
        {
            entity.ToTable("libraries");
            entity.HasKey(l => l.Id);

            entity.Property(l => l.Id)
                .HasColumnName("id");

            entity.Property(l => l.UserId)
                .HasColumnName("user_id")
                .HasColumnType("text")
                .IsRequired();

            entity.HasIndex(l => l.UserId)
                .IsUnique()
                .HasDatabaseName("ix_libraries_user_id");

            entity.HasMany(l => l.Platforms)
                .WithOne(p => p.Library)
                .HasForeignKey(p => p.LibraryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(l => l.LibraryEntries)
                .WithOne(le => le.Library)
                .HasForeignKey(le => le.LibraryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.SteamAccount)
                .WithOne(a => a.Library)
                .HasForeignKey<SteamAccount>(a => a.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(l => l.SteamLinkRequests)
                .WithOne(r => r.Library)
                .HasForeignKey(r => r.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Platform>(entity =>
        {
            entity.ToTable("platforms");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                .HasColumnName("id");

            entity.Property(p => p.LibraryId)
                .HasColumnName("library_id");

            entity.Property(p => p.Name)
                .HasColumnName("name")
                .HasMaxLength(PlatformService.MaxNameLength)
                .IsRequired();

            entity.Property(p => p.NameNormalized)
                .HasColumnName("name_normalized")
                .HasColumnType("text")
                .IsRequired()
                .HasComputedColumnSql("lower(name)", stored: true);

            entity.HasIndex(p => new { p.LibraryId, p.NameNormalized })
                .IsUnique()
                .HasDatabaseName("ix_platforms_library_id_name_normalized");
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("games");
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Id)
                .HasColumnName("id");

            entity.Property(g => g.GameType)
                .HasColumnName("game_type")
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(g => g.Name)
                .HasColumnName("name")
                .HasMaxLength(VideoGameRules.MaxNameLength)
                .IsRequired();

            entity.Property(g => g.CoverImageUrl)
                .HasColumnName("cover_image_url");

            entity.Property(g => g.MinimumPlayers)
                .HasColumnName("minimum_players");

            entity.Property(g => g.MaximumPlayers)
                .HasColumnName("maximum_players");

            entity.Property(g => g.ApproximateDuration)
                .HasColumnName("approximate_duration");

            entity.Property(g => g.InteractionType)
                .HasColumnName("interaction_type")
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(g => g.CreatedAt)
                .HasColumnName("created_at");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_games_game_type",
                    "game_type IN ('VideoGame', 'BoardGame')");
                t.HasCheckConstraint(
                    "ck_games_minimum_players",
                    "minimum_players IS NULL OR minimum_players >= 1");
                t.HasCheckConstraint(
                    "ck_games_maximum_players",
                    "minimum_players IS NULL OR maximum_players IS NULL OR maximum_players >= minimum_players");
                t.HasCheckConstraint(
                    "ck_games_approximate_duration",
                    "approximate_duration IS NULL OR approximate_duration > 0");
                t.HasCheckConstraint(
                    "ck_games_interaction_type",
                    "interaction_type IS NULL OR interaction_type IN ('Cooperative', 'Competitive')");
                t.HasCheckConstraint(
                    "ck_games_board_players_required",
                    "game_type = 'VideoGame' OR (minimum_players IS NOT NULL AND maximum_players IS NOT NULL)");
                t.HasCheckConstraint(
                    "ck_games_board_columns_video_null",
                    "game_type = 'BoardGame' OR (approximate_duration IS NULL AND interaction_type IS NULL)");
                t.HasCheckConstraint(
                    "ck_games_video_players_both_or_neither",
                    "game_type = 'BoardGame' OR ((minimum_players IS NULL AND maximum_players IS NULL) OR (minimum_players IS NOT NULL AND maximum_players IS NOT NULL))");
            });

            entity.HasMany(g => g.GameGenres)
                .WithOne(gg => gg.Game)
                .HasForeignKey(gg => gg.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LibraryEntry>(entity =>
        {
            entity.ToTable("library_entries");
            entity.HasKey(le => le.Id);

            entity.Property(le => le.Id)
                .HasColumnName("id");

            entity.Property(le => le.LibraryId)
                .HasColumnName("library_id");

            entity.Property(le => le.GameId)
                .HasColumnName("game_id");

            entity.Property(le => le.AcquisitionStatus)
                .HasColumnName("acquisition_status")
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(le => le.Rating)
                .HasColumnName("rating");

            entity.Property(le => le.Notes)
                .HasColumnName("notes");

            entity.Property(le => le.GameStatus)
                .HasColumnName("game_status")
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(le => le.ProgressPercentage)
                .HasColumnName("progress_percentage");

            entity.Property(le => le.SteamAppId)
                .HasColumnName("steam_app_id");

            entity.HasIndex(le => le.LibraryId)
                .HasDatabaseName("ix_library_entries_library_id");

            entity.HasIndex(le => le.GameId)
                .IsUnique()
                .HasDatabaseName("ix_library_entries_game_id");

            entity.HasIndex(le => new { le.LibraryId, le.SteamAppId })
                .IsUnique()
                .HasFilter("steam_app_id IS NOT NULL")
                .HasDatabaseName("ix_library_entries_library_id_steam_app_id");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_library_entries_rating", "rating IS NULL OR (rating BETWEEN 1 AND 5)");
                t.HasCheckConstraint(
                    "ck_library_entries_progress_percentage",
                    "progress_percentage IS NULL OR (progress_percentage BETWEEN 0 AND 100)");
                t.HasCheckConstraint(
                    "ck_library_entries_owned_status_progress",
                    "acquisition_status = 'Owned' OR (game_status IS NULL AND progress_percentage IS NULL)");
                t.HasCheckConstraint(
                    "ck_library_entries_steam_app_id",
                    "steam_app_id IS NULL OR (steam_app_id BETWEEN 1 AND 4294967295)");
            });

            entity.HasOne(le => le.Library)
                .WithMany(l => l.LibraryEntries)
                .HasForeignKey(le => le.LibraryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(le => le.Game)
                .WithOne(g => g.LibraryEntry)
                .HasForeignKey<LibraryEntry>(le => le.GameId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(le => le.GamePlatforms)
                .WithOne(gp => gp.LibraryEntry)
                .HasForeignKey(gp => gp.LibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(le => le.PlayLogEntries)
                .WithOne(entry => entry.LibraryEntry)
                .HasForeignKey(entry => entry.LibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayLogEntry>(entity =>
        {
            entity.ToTable("play_log_entries");
            entity.HasKey(entry => entry.Id)
                .HasName("pk_play_log_entries");

            entity.Property(entry => entry.Id)
                .HasColumnName("id");

            entity.Property(entry => entry.LibraryEntryId)
                .HasColumnName("library_entry_id");

            entity.Property(entry => entry.PlayedAt)
                .HasColumnName("played_at");

            entity.Property(entry => entry.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(entry => entry.DurationMinutes)
                .HasColumnName("duration_minutes");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_play_log_entries_duration_minutes_positive",
                    "duration_minutes IS NULL OR duration_minutes > 0");
            });

            entity.HasIndex(entry => entry.LibraryEntryId)
                .HasDatabaseName("ix_play_log_entries_library_entry_id");

            entity.HasOne(entry => entry.LibraryEntry)
                .WithMany(le => le.PlayLogEntries)
                .HasForeignKey(entry => entry.LibraryEntryId)
                .HasConstraintName("fk_play_log_entries_library_entries_library_entry_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.ToTable("genres");
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Id)
                .HasColumnName("id");

            entity.Property(g => g.Name)
                .HasColumnName("name")
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(g => g.Name)
                .IsUnique()
                .HasDatabaseName("ix_genres_name");

            entity.HasData(GenresCatalog.All);
        });

        modelBuilder.Entity<GameGenre>(entity =>
        {
            entity.ToTable("game_genres");
            entity.HasKey(gg => new { gg.GameId, gg.GenreId });

            entity.Property(gg => gg.GameId)
                .HasColumnName("game_id");

            entity.Property(gg => gg.GenreId)
                .HasColumnName("genre_id");

            entity.HasIndex(gg => gg.GameId)
                .HasDatabaseName("ix_game_genres_game_id");

            entity.HasIndex(gg => gg.GenreId)
                .HasDatabaseName("ix_game_genres_genre_id");

            entity.HasOne(gg => gg.Game)
                .WithMany(g => g.GameGenres)
                .HasForeignKey(gg => gg.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(gg => gg.Genre)
                .WithMany(g => g.GameGenres)
                .HasForeignKey(gg => gg.GenreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GamePlatform>(entity =>
        {
            entity.ToTable("game_platforms");
            entity.HasKey(gp => new { gp.LibraryEntryId, gp.PlatformId });

            entity.Property(gp => gp.LibraryEntryId)
                .HasColumnName("library_entry_id");

            entity.Property(gp => gp.PlatformId)
                .HasColumnName("platform_id");

            entity.HasIndex(gp => gp.PlatformId)
                .HasDatabaseName("ix_game_platforms_platform_id");

            entity.HasOne(gp => gp.LibraryEntry)
                .WithMany(le => le.GamePlatforms)
                .HasForeignKey(gp => gp.LibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(gp => gp.Platform)
                .WithMany()
                .HasForeignKey(gp => gp.PlatformId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SteamAccount>(entity =>
        {
            entity.ToTable("steam_accounts");
            entity.HasKey(a => a.Id)
                .HasName("pk_steam_accounts");

            entity.Property(a => a.Id)
                .HasColumnName("id");

            entity.Property(a => a.LibraryId)
                .HasColumnName("library_id");

            entity.Property(a => a.SteamId64)
                .HasColumnName("steam_id64")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(a => a.LinkedAt)
                .HasColumnName("linked_at");

            entity.HasIndex(a => a.LibraryId)
                .IsUnique()
                .HasDatabaseName("ix_steam_accounts_library_id");

            entity.HasIndex(a => a.SteamId64)
                .IsUnique()
                .HasDatabaseName("ix_steam_accounts_steam_id64");
        });

        modelBuilder.Entity<SteamLinkRequest>(entity =>
        {
            entity.ToTable("steam_link_requests");
            entity.HasKey(r => r.Id)
                .HasName("pk_steam_link_requests");

            entity.Property(r => r.Id)
                .HasColumnName("id");

            entity.Property(r => r.LibraryId)
                .HasColumnName("library_id");

            entity.Property(r => r.StateHash)
                .HasColumnName("state_hash")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(r => r.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(r => r.ExpiresAt)
                .HasColumnName("expires_at");

            entity.Property(r => r.ConsumedAt)
                .HasColumnName("consumed_at");

            entity.HasIndex(r => r.StateHash)
                .IsUnique()
                .HasDatabaseName("ix_steam_link_requests_state_hash");

            entity.HasIndex(r => r.LibraryId)
                .HasDatabaseName("ix_steam_link_requests_library_id");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_steam_link_requests_state_hash",
                    "char_length(state_hash) = 64");
                t.HasCheckConstraint(
                    "ck_steam_link_requests_expires_at",
                    "expires_at > created_at");
            });
        });
    }
}
