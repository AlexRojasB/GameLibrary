namespace GameLibrary.Core.Games;

public class InvalidVideoGameException : Exception
{
    public InvalidVideoGameException(string message)
        : base(message)
    {
    }
}

public class VideoGameNotFoundException : Exception
{
    public VideoGameNotFoundException()
        : base("Video game not found.")
    {
    }
}