namespace TrickplayCropper.PackageValidator;

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class PackageValidator
{
    private const string ProductionAssemblyFileName = "Jellyfin.Plugin.TrickplayCropper.dll";
    private const string ProductionAssemblyName = "Jellyfin.Plugin.TrickplayCropper";
    private const string PluginName = "Trickplay Cropper";
    private const string PluginGuid = "630fb758-9a29-4f2c-a54c-95793651bb8a";
    private const string PluginVersion = "1.0.0.0";
    private const string TargetAbi = "10.11.0.0";
    private const string TargetFramework = "net9.0";
    private const string TargetFrameworkName = ".NETCoreApp,Version=v9.0";
    private static readonly Version AssemblyVersion = new(1, 0, 0, 0);
    private static readonly Version JellyfinContractVersion = new(10, 11, 11, 0);
    private static readonly string[] ExpectedArtifacts = [ProductionAssemblyFileName];
    private static readonly HashSet<string> ExpectedMembers = new(StringComparer.Ordinal)
    {
        ProductionAssemblyFileName,
        "meta.json",
    };

    public static void Validate(string packagePath, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var manifest = ReadJson<BuildManifest>(manifestPath, "build manifest");
        ValidateBuildManifest(manifest);

        using var package = ZipFile.OpenRead(packagePath);
        ValidateArchiveMembers(package);
        ValidateMetadata(package, manifest);
        ValidateProductionAssembly(package);
    }

    private static void ValidateBuildManifest(BuildManifest manifest)
    {
        RequireEqual("build manifest name", manifest.Name, PluginName);
        RequireEqual("build manifest guid", manifest.Guid, PluginGuid);
        RequireEqual("build manifest version", manifest.Version, PluginVersion);
        RequireEqual("build manifest targetAbi", manifest.TargetAbi, TargetAbi);
        RequireEqual("build manifest framework", manifest.Framework, TargetFramework);

        if (manifest.Artifacts is null
            || !manifest.Artifacts.SequenceEqual(ExpectedArtifacts, StringComparer.Ordinal))
        {
            throw new PackageValidationException(
                $"Build manifest artifacts must be [{string.Join(", ", ExpectedArtifacts)}].");
        }
    }

    private static void ValidateArchiveMembers(ZipArchive package)
    {
        var memberNames = package.Entries.Select(entry => entry.FullName).ToArray();
        var unexpectedMembers = memberNames.Except(ExpectedMembers, StringComparer.Ordinal).ToArray();
        if (unexpectedMembers.Length > 0)
        {
            throw new PackageValidationException(
                $"Unexpected archive members: {string.Join(", ", unexpectedMembers)}");
        }

        var missingMembers = ExpectedMembers.Except(memberNames, StringComparer.Ordinal).ToArray();
        if (missingMembers.Length > 0)
        {
            throw new PackageValidationException(
                $"Missing archive members: {string.Join(", ", missingMembers)}");
        }

        if (memberNames.Length != ExpectedMembers.Count)
        {
            throw new PackageValidationException("Duplicate archive members are not allowed.");
        }
    }

    private static void ValidateMetadata(ZipArchive package, BuildManifest manifest)
    {
        var metadataEntry = package.GetEntry("meta.json")
            ?? throw new PackageValidationException("Package does not contain meta.json.");
        using var metadataStream = metadataEntry.Open();
        var metadata = ReadJson<PackageMetadata>(metadataStream, "meta.json");

        RequireEqual("meta.json name", metadata.Name, manifest.Name);
        RequireEqual("meta.json guid", metadata.Guid, manifest.Guid);
        RequireEqual("meta.json version", metadata.Version, manifest.Version);
        RequireEqual("meta.json targetAbi", metadata.TargetAbi, manifest.TargetAbi);
    }

    private static void ValidateProductionAssembly(ZipArchive package)
    {
        var assemblyEntry = package.GetEntry(ProductionAssemblyFileName)
            ?? throw new PackageValidationException(
                $"Package does not contain {ProductionAssemblyFileName}.");
        var temporaryAssemblyPath = Path.Combine(
            Path.GetTempPath(),
            $"{ProductionAssemblyName}-{Guid.NewGuid():N}.dll");

        try
        {
            using (var source = assemblyEntry.Open())
            using (var destination = File.Create(temporaryAssemblyPath))
            {
                source.CopyTo(destination);
            }

            var assemblyIdentity = AssemblyName.GetAssemblyName(temporaryAssemblyPath);
            RequireEqual("assembly name", assemblyIdentity.Name, ProductionAssemblyName);
            if (assemblyIdentity.Version != AssemblyVersion)
            {
                throw new PackageValidationException(
                    $"Assembly version must be {AssemblyVersion}, got {assemblyIdentity.Version}.");
            }

            var fileVersion = FileVersionInfo.GetVersionInfo(temporaryAssemblyPath).FileVersion;
            RequireEqual("file version", fileVersion, PluginVersion);

            var assembly = Assembly.Load(File.ReadAllBytes(temporaryAssemblyPath));
            var frameworkName = assembly
                .GetCustomAttribute<TargetFrameworkAttribute>()
                ?.FrameworkName;
            RequireEqual("assembly target framework", frameworkName, TargetFrameworkName);

            var jellyfinContract = assembly
                .GetReferencedAssemblies()
                .SingleOrDefault(reference => reference.Name == "MediaBrowser.Common");
            if (jellyfinContract?.Version != JellyfinContractVersion)
            {
                throw new PackageValidationException(
                    $"MediaBrowser.Common reference must be {JellyfinContractVersion}, "
                    + $"got {jellyfinContract?.Version}.");
            }

            ValidatePluginIdentity(assembly);
        }
        catch (PackageValidationException)
        {
            throw;
        }
        catch (Exception error) when (
            error is BadImageFormatException
            or FileLoadException
            or MissingMethodException
            or TargetInvocationException
            or TypeLoadException)
        {
            throw new PackageValidationException(
                $"{ProductionAssemblyFileName} is not a valid plugin assembly: {error.Message}",
                error);
        }
        finally
        {
            File.Delete(temporaryAssemblyPath);
        }
    }

    private static void ValidatePluginIdentity(Assembly assembly)
    {
        var pluginType = assembly.GetType(
            $"{ProductionAssemblyName}.Plugin",
            throwOnError: true,
            ignoreCase: false)!;
        var plugin = Activator.CreateInstance(pluginType)
            ?? throw new PackageValidationException("Plugin type could not be instantiated.");
        var name = pluginType.GetProperty("Name")?.GetValue(plugin) as string;
        var id = pluginType.GetProperty("Id")?.GetValue(plugin);

        RequireEqual("plugin name", name, PluginName);
        if (id is not Guid pluginId || pluginId != Guid.Parse(PluginGuid))
        {
            throw new PackageValidationException(
                $"Plugin ID must be {PluginGuid}, got {id}.");
        }
    }

    private static T ReadJson<T>(string path, string description)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return ReadJson<T>(stream, description);
        }
        catch (PackageValidationException)
        {
            throw;
        }
        catch (IOException error)
        {
            throw new PackageValidationException(
                $"Could not read {description}: {error.Message}",
                error);
        }
    }

    private static T ReadJson<T>(Stream stream, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(stream)
                ?? throw new PackageValidationException($"{description} must contain a JSON object.");
        }
        catch (JsonException error)
        {
            throw new PackageValidationException(
                $"{description} is not valid JSON-compatible YAML: {error.Message}",
                error);
        }
    }

    private static void RequireEqual(string field, string? actual, string? expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new PackageValidationException(
                $"{field} must be {expected ?? "<null>"}, got {actual ?? "<null>"}.");
        }
    }

    private sealed record BuildManifest(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("guid")] string? Guid,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("targetAbi")] string? TargetAbi,
        [property: JsonPropertyName("framework")] string? Framework,
        [property: JsonPropertyName("artifacts")] string[]? Artifacts);

    private sealed record PackageMetadata(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("guid")] string? Guid,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("targetAbi")] string? TargetAbi);
}
