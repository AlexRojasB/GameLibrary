namespace GameLibrary.Core.Libraries;

public sealed class LibraryItemNotFoundException : Exception;

public sealed class InvalidLibraryQueryException : Exception
{
    public InvalidLibraryQueryException(string message)
        : base(message)
    {
    }
}
