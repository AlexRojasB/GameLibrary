namespace GameLibrary.Api.CurrentlyPlaying;

public sealed record SelectGameRequest(Guid? GameId);
public sealed record CurrentlyPlayingResponse(CurrentlyPlayingItemResponse? VideoGame, CurrentlyPlayingItemResponse? BoardGame);
public sealed record CurrentlyPlayingItemResponse(
    Guid GameId, Guid LibraryEntryId, string GameType, string Name, string? CoverImageUrl, Guid? CoverUploadId);
