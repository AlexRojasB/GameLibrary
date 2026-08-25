namespace GameLibrary.Core.PlayLog;

public sealed class InvalidPlayLogEntryException : Exception
{
    public InvalidPlayLogEntryException(string message)
        : base(message)
    {
    }
}

public sealed class PlayLogGameNotFoundException : Exception
{
    public PlayLogGameNotFoundException()
        : base("Game not found.")
    {
    }
}

public sealed class PlayLogEntryNotFoundException : Exception
{
    public PlayLogEntryNotFoundException()
        : base("Play log entry not found.")
    {
    }
}
