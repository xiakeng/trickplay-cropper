namespace TrickplayCropper.ManifestBuilder;

public sealed class ManifestBuildingException : Exception
{
    public ManifestBuildingException(string message)
        : base(message)
    {
    }

    public ManifestBuildingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
