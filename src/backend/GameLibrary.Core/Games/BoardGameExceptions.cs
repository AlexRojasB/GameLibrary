namespace GameLibrary.Core.Games;

public class InvalidBoardGameException : Exception
{
    public InvalidBoardGameException(string message)
        : base(message)
    {
    }
}

public class BoardGameNotFoundException : Exception
{
    public BoardGameNotFoundException()
        : base("Board game not found.")
    {
    }
}
