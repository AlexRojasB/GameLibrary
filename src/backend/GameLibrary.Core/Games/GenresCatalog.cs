namespace GameLibrary.Core.Games;

/// <summary>
/// The authoritative code definition of the approved, immutable Genre catalog.
/// The EF seed configuration (<c>HasData</c>) derives from this catalog, so a
/// change here requires a new EF Core migration and tests that verify the
/// persisted catalog. GUIDs are fixed and deterministic so the seed and the
/// <c>game_genres</c> FKs are stable across environments and migrations.
/// </summary>
public static class GenresCatalog
{
    public static IReadOnlyList<Genre> All { get; } =
    [
        new Genre { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Action" },
        new Genre { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Adventure" },
        new Genre { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "RPG" },
        new Genre { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "Strategy" },
        new Genre { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "Simulation" },
        new Genre { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), Name = "Sports" },
        new Genre { Id = Guid.Parse("77777777-7777-7777-7777-777777777777"), Name = "Racing" },
        new Genre { Id = Guid.Parse("88888888-8888-8888-8888-888888888888"), Name = "Fighting" },
        new Genre { Id = Guid.Parse("99999999-9999-9999-9999-999999999999"), Name = "Shooter" },
        new Genre { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Platformer" },
        new Genre { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "Puzzle" },
        new Genre { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Name = "Horror" },
        new Genre { Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), Name = "Rhythm" },
        new Genre { Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), Name = "Party" },
        new Genre { Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Name = "Other" },
    ];

    /// <summary>
    /// The approved catalog names, compared order-independently against the
    /// persisted/returned set by tests.
    /// </summary>
    public static IReadOnlySet<string> Names { get; } =
        All.Select(g => g.Name).ToHashSet();
}