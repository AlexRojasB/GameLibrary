using GameLibrary.Core.Games;

namespace GameLibrary.Core.CoverImages;

public sealed record CoverImageSearchRequest(string? GameType, string? Name, string? PlatformName);

public sealed record CoverImageSearchQuery(GameType GameType, string Name, string? PlatformName, string ProviderQuery);
