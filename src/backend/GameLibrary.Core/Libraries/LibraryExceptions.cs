namespace GameLibrary.Core.Libraries;

public sealed class InvalidLibraryQueryException : Exception
{
    public InvalidLibraryQueryException(string message)
        : base(message)
    {
    }
}
