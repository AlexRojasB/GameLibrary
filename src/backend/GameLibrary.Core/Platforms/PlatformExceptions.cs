namespace GameLibrary.Core.Platforms;

public class InvalidPlatformNameException : Exception
{
    public InvalidPlatformNameException(string message)
        : base(message)
    {
    }
}

public class PlatformNameConflictException : Exception
{
    public PlatformNameConflictException(string message)
        : base(message)
    {
    }
}

public class PlatformNotFoundException : Exception
{
    public PlatformNotFoundException()
        : base("Platform not found.")
    {
    }
}

public class PlatformInUseException : Exception
{
    public PlatformInUseException()
        : base("This platform is in use and cannot be deleted.")
    {
    }
}
