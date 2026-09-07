using System.Reflection;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public class InterfaceMockSpecs<TInterface> : DispatchProxy
    where TInterface : class
{
    private readonly Dictionary<string, Func<object?[]?, object?>> handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="InterfaceMockSpecs{TInterface}"/> class.
    /// </summary>
    public InterfaceMockSpecs()
    {
    }

    public TInterface Service => (TInterface)(object)this;

    public void Handle(string methodName, Func<object?[]?, object?> handler)
    {
        handlers.Add(methodName, handler);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (handlers.TryGetValue(targetMethod.Name, out Func<object?[]?, object?>? handler))
        {
            return handler(args);
        }

        throw new InvalidOperationException($"Unexpected Jellyfin call: {targetMethod.Name}.");
    }
}
