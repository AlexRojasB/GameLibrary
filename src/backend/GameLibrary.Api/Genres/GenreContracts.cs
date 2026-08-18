namespace GameLibrary.Api.Genres;

/// <summary>
/// API contract for the read-only Genre catalog. Users cannot create, edit, or
/// delete genres.
/// </summary>
public sealed record GenreResponse(Guid Id, string Name);