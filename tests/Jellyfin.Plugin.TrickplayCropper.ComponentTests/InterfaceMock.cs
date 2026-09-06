using System.Reflection;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal static class InterfaceMock
{
    public static InterfaceMockSpecs<TInterface> Create<TInterface>()
        where TInterface : class
    {
        TInterface service = DispatchProxy.Create<TInterface, InterfaceMockSpecs<TInterface>>();
        return (InterfaceMockSpecs<TInterface>)(object)service;
    }
}
