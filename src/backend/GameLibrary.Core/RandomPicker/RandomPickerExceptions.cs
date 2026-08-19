namespace GameLibrary.Core.RandomPicker;

public class InvalidRandomPickerQueryException : Exception
{
    public InvalidRandomPickerQueryException(string message)
        : base(message)
    {
    }
}
