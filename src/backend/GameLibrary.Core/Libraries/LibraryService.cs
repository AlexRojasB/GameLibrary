using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Core.Libraries;

/// <summary>
/// Shared lazy Library get-or-create, used by any consumer that creates user-owned
/// data (currently Platforms and VideoGames). The row is created on the user's
/// first create; reads never create a row.
/// </summary>
public class LibraryService
{
    private readonly GameLibraryDbContext _db;

    public LibraryService(GameLibraryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the user's Library, creating it on first use. Two concurrent
    /// first-creates for the same user are resolved by the unique index on
    /// <c>libraries.user_id</c>: the loser's insert fails, the change tracker is
    /// cleared, and the existing row is re-queried. No locking is introduced.
    /// </summary>
    public async Task<Library> EnsureAsync(string userId, CancellationToken ct)
    {
        var library = await _db.Libraries.SingleOrDefaultAsync(l => l.UserId == userId, ct);
        if (library is not null)
        {
            return library;
        }

        var created = new Library { Id = Guid.NewGuid(), UserId = userId };
        _db.Libraries.Add(created);

        try
        {
            await _db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();

            var existing = await _db.Libraries.SingleOrDefaultAsync(l => l.UserId == userId, ct);
            return existing ?? throw new InvalidOperationException("Library could not be created or found.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}