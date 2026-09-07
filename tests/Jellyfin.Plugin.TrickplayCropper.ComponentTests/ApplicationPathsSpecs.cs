using System.Reflection;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public class ApplicationPathsSpecs : DispatchProxy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationPathsSpecs"/> class.
    /// </summary>
    public ApplicationPathsSpecs()
    {
    }

    public string TemporaryDirectory { get; set; } = string.Empty;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return targetMethod.Name == "get_TempDirectory"
            ? TemporaryDirectory
            : throw new InvalidOperationException($"Unexpected application-paths call: {targetMethod.Name}.");
    }
}
