namespace GameLibrary.Core.Games;

/// <summary>
/// A fixed, immutable, application-owned catalog genre. Users cannot create, edit,
/// or delete genres; the rows are seeded from <see cref="GenresCatalog"/>.
/// </summary>
public class Genre
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<GameGenre> GameGenres { get; set; } = new List<GameGenre>();
}