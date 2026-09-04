using System.Reflection;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Trickplay;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PreviewContextBoundarySpecs
{
    private static readonly Assembly pluginAssembly = typeof(PreviewContext).Assembly;

    private static readonly string[] getOnlyNamespaces =
    [
        "Jellyfin.Plugin.TrickplayCropper.Caching",
        "Jellyfin.Plugin.TrickplayCropper.Imaging",
    ];

    private static readonly string[] getOnlyTypeNames =
    [
        typeof(EntityTagHeaderValue).FullName!,
        typeof(FrameSelection).FullName!,
        typeof(FrameSelectionDiagnostics).FullName!,
        typeof(IPreviewSourceResolver).FullName!,
        typeof(ITrickplayPreview).FullName!,
        typeof(PreviewIdentity).FullName!,
        typeof(PreviewOutcome).FullName!,
        typeof(PreviewSourceResolution).FullName!,
        typeof(PreviewTelemetry).FullName!,
        typeof(ResolvedPreviewSource).FullName!,
        typeof(TrickplayPreview).FullName!,
    ];

    [Fact]
    public void SharedPreviewContextContractExposesNoGetOnlyFacility()
    {
        Type[] sharedContract =
        [
            typeof(IPreviewContextResolver),
            typeof(PreviewContextResolution),
            typeof(PreviewContext),
        ];

        Type[] exposedTypes = CollectExposedTypes(sharedContract);

        Assert.Contains(typeof(PreviewQuery), exposedTypes);
        Assert.Contains(typeof(TrickplayMetadata), exposedTypes);
        Assert.All(exposedTypes, AssertSharedType);
    }

    [Fact]
    public void SharedPreviewContextResolverDependsOnNoGetOnlyFacility()
    {
        Type implementation = Assert.Single(CollectImplementations<IPreviewContextResolver>());

        Type[] exposedTypes = CollectExposedTypes([implementation]);

        Assert.Contains(typeof(ITrickplayManager), exposedTypes);
        Assert.All(exposedTypes, AssertSharedType);
    }

    private static Type[] CollectImplementations<TContract>()
    {
        return pluginAssembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(TContract).IsAssignableFrom(type))
            .ToArray();
    }

    // Reflection reaches signatures, fields, and constructor parameters. A thrown exception's
    // diagnostics never appear there, so review owns that part of the boundary.
    private static Type[] CollectExposedTypes(Type[] roots)
    {
        var exposed = new HashSet<Type>();
        var pending = new Queue<Type>(roots);
        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            if (!exposed.Add(type))
            {
                continue;
            }

            foreach (Type member in EnumerateMemberTypes(type))
            {
                exposed.Add(member);
                if (member.Assembly == pluginAssembly)
                {
                    pending.Enqueue(member);
                }
            }
        }

        return exposed.ToArray();
    }

    private static IEnumerable<Type> EnumerateMemberTypes(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (Type interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }

        foreach (Type nestedType in type.GetNestedTypes(Declared))
        {
            yield return nestedType;
        }

        foreach (Type genericArgument in type.GetGenericArguments())
        {
            yield return genericArgument;
        }

        foreach (FieldInfo field in type.GetFields(Declared))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(Declared))
        {
            yield return property.PropertyType;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(Declared))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (MethodInfo method in type.GetMethods(Declared))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static void AssertSharedType(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        bool isGetOnlyNamespace = type.Namespace is not null
            && getOnlyNamespaces.Any(
                getOnlyNamespace => type.Namespace.StartsWith(getOnlyNamespace, StringComparison.Ordinal));
        bool isGetOnlyType = getOnlyTypeNames.Any(
            getOnlyType => fullName.Equals(getOnlyType, StringComparison.Ordinal)
                || fullName.StartsWith(string.Concat(getOnlyType, "+"), StringComparison.Ordinal));

        Assert.False(
            isGetOnlyNamespace || isGetOnlyType,
            $"The shared Preview context reaches the GET-only facility {fullName}.");
    }
}
