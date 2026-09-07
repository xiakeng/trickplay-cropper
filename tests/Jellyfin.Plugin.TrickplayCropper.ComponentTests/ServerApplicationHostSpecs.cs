using System.Reflection;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public class ServerApplicationHostSpecs : DispatchProxy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerApplicationHostSpecs"/> class.
    /// </summary>
    public ServerApplicationHostSpecs()
    {
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        throw new InvalidOperationException($"Unexpected application-host call: {targetMethod?.Name}.");
    }
}
