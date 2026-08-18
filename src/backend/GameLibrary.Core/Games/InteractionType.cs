namespace GameLibrary.Core.Games;

/// <summary>
/// Optional BoardGame interaction type. Only <see cref="Cooperative"/> and
/// <see cref="Competitive"/> are supported by the approved Domain; the enum name
/// string is the persisted representation (varchar storage contract).
/// </summary>
public enum InteractionType
{
    Cooperative,
    Competitive,
}
