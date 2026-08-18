using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Core.Platforms;

/// <summary>
/// Application service owning all Platform rules: ownership scoping (always
/// derived from the authenticated <c>userId</c>, never client-supplied), name
/// validation, case-insensitive duplicate detection, and the lazy Library
/// get-or-create. This is the authoritative backend enforcement layer.
/// </summary>
public class PlatformService
{
    public const int MaxNameLength = 100;

    private readonly GameLibraryDbContext _db;
    private readonly LibraryService _libraries;

    public PlatformService(GameLibraryDbContext db, LibraryService libraries)
    {
        _db = db;
        _libraries = libraries;
    }

    /// <summary>
    /// Returns the caller's Platforms ordered by name (case-insensitive
    /// ascending). Empty when the caller has no Library yet.
    /// </summary>
    public async Task<IReadOnlyList<Platform>> ListAsync(string userId, CancellationToken ct)
    {
        return await _db.Platforms
            .Where(p => p.Library!.UserId == userId)
            .OrderBy(p => p.NameNormalized)
            .ToListAsync(ct);
    }

    public async Task<Platform> CreateAsync(string userId, string? name, CancellationToken ct)
    {
        var trimmedName = ValidateAndTrim(name);

        var library = await _libraries.EnsureAsync(userId, ct);

        await EnsureNameAvailableAsync(library.Id, trimmedName, excludePlatformId: null, ct);

        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            Name = trimmedName,
        };

        _db.Platforms.Add(platform);
        await SaveOrMapDuplicateAsync(ct);

        return platform;
    }

    public async Task<Platform> UpdateAsync(string userId, Guid platformId, string? name, CancellationToken ct)
    {
        var trimmedName = ValidateAndTrim(name);

        var platform = await LoadOwnedAsync(userId, platformId, ct);

        await EnsureNameAvailableAsync(platform.LibraryId, trimmedName, excludePlatformId: platformId, ct);

        platform.Name = trimmedName;
        await SaveOrMapDuplicateAsync(ct);

        return platform;
    }

    public async Task DeleteAsync(string userId, Guid platformId, CancellationToken ct)
    {
        var platform = await LoadOwnedAsync(userId, platformId, ct);

        _db.Platforms.Remove(platform);
        await SaveOrMapInUseAsync(ct);
    }

    /// <summary>
    /// Validates the name against the approved rules, in order: trim, then
    /// non-empty, then trimmed length ≤ 100. Returns the trimmed name.
    /// </summary>
    private static string ValidateAndTrim(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new InvalidPlatformNameException("Platform name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidPlatformNameException("Platform name must be at most 100 characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Saves the current changes. A Platform referenced by a <c>game_platforms</c>
    /// row cannot be deleted (the join's <c>platform_id</c> FK is
    /// <c>ON DELETE RESTRICT</c>); the resulting FK violation is surfaced as the
    /// approved <c>409 Platform in use</c>. No constraint name, SQL, or other
    /// database internal is exposed.
    /// </summary>
    private async Task SaveOrMapInUseAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new PlatformInUseException();
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    private async Task<Platform> LoadOwnedAsync(string userId, Guid platformId, CancellationToken ct)
    {
        var platform = await _db.Platforms
            .Where(p => p.Id == platformId && p.Library!.UserId == userId)
            .FirstOrDefaultAsync(ct);

        return platform ?? throw new PlatformNotFoundException();
    }

    private async Task EnsureNameAvailableAsync(Guid libraryId, string trimmedName, Guid? excludePlatformId, CancellationToken ct)
    {
        var query = _db.Platforms
            .Where(p => p.LibraryId == libraryId && p.NameNormalized == trimmedName.ToLowerInvariant());

        if (excludePlatformId is not null)
        {
            query = query.Where(p => p.Id != excludePlatformId.Value);
        }

        if (await query.AnyAsync(ct))
        {
            throw new PlatformNameConflictException("A platform with this name already exists.");
        }
    }

    /// <summary>
    /// Saves the current changes. A concurrent insert that violates the
    /// <c>(library_id, name_normalized)</c> unique index is the authoritative
    /// backstop for the duplicate pre-check and is surfaced as a friendly 409.
    /// </summary>
    private async Task SaveOrMapDuplicateAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new PlatformNameConflictException("A platform with this name already exists.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
