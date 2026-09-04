namespace TrickplayCropper.ReleasePlanner;

public sealed class ReleasePlanningException : Exception
{
    public ReleasePlanningException(string message)
        : base(message)
    {
    }

    public ReleasePlanningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
