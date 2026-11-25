using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.RandomPicker;

// Three current recovery slots, not completed-round history or game snapshots.
public sealed class PickerRound
{
    public Guid LibraryId { get; set; }
    public RandomPickerMode Mode { get; set; }
    public long Revision { get; set; }
    public string FiltersJson { get; set; } = "{}";
    public RandomPickerState? State { get; set; }
    public Guid? CurrentLibraryEntryId { get; set; }
    public Guid[] HistoryLibraryEntryIds { get; set; } = [];
    public Guid[] ShownLibraryEntryIds { get; set; } = [];
    public Guid LastOperationId { get; set; }
    public string LastOperationHash { get; set; } = string.Empty;
    public Library Library { get; set; } = null!;
}

public sealed record PickerRoundView(
    RandomPickerMode Mode, long Revision, RandomPickerRequest Filters,
    RandomPickerState? State, RandomPickerItemView? Result,
    IReadOnlyList<RandomPickerItemView> History, IReadOnlyList<Guid> ShownLibraryEntryIds,
    bool ProposalUnavailable);

public sealed class PickerRoundConflictException : Exception;
