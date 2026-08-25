namespace GameLibrary.Core.CoverImages;

public class InvalidCoverImageSearchException : Exception
{
    public InvalidCoverImageSearchException(string message) : base(message)
    {
    }
}

public abstract class CoverImageSearchUnavailableException : Exception
{
    protected CoverImageSearchUnavailableException(string message) : base(message)
    {
    }
}

public sealed class CoverImageSearchNotConfiguredException : CoverImageSearchUnavailableException
{
    public CoverImageSearchNotConfiguredException() : base("Cover image search is not configured.")
    {
    }
}

public sealed class CoverImageSearchTimeoutException : CoverImageSearchUnavailableException
{
    public CoverImageSearchTimeoutException() : base("Cover image search timed out. Please try again.")
    {
    }
}

public sealed class CoverImageSearchProviderException : CoverImageSearchUnavailableException
{
    public CoverImageSearchProviderException() : base("Unable to search cover images right now.")
    {
    }
}
