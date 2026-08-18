namespace GameLibrary.Api.Platforms;

/// <summary>
/// API contracts for the Platforms endpoints. EF entities are never exposed.
/// <c>Name</c> is nullable at the HTTP boundary because a missing or null
/// <c>name</c> is a client validation error handled by <c>PlatformService</c>.
/// </summary>
public sealed record CreatePlatformRequest(string? Name);

public sealed record UpdatePlatformRequest(string? Name);

public sealed record PlatformResponse(Guid Id, string Name);
