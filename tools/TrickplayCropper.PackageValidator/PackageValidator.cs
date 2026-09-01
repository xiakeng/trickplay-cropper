using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrickplayCropper.PackageValidator;

public static class PackageValidator
{
    private const string MetadataFileName = "meta.json";

    public static void Validate(string packagePath, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var manifest = ReadJson<BuildManifest>(manifestPath, "build manifest");
        var contract = CreatePackageContract(manifest);

        using var package = ZipFile.OpenRead(packagePath);
        ValidateArchiveMembers(package, contract.ExpectedMembers);
        ValidateMetadata(package, contract);
        ValidateProductionAssembly(package, contract);
    }

    private static PackageContract CreatePackageContract(BuildManifest manifest)
    {
        var name = RequireValue("build manifest name", manifest.Name);
        var guidText = RequireValue("build manifest guid", manifest.Guid);
        var pluginGuid = ParsePluginGuid(guidText);
        var versionText = RequireValue("build manifest version", manifest.Version);
        var pluginVersion = ParsePluginVersion(versionText);
        var targetAbi = RequireValue("build manifest targetAbi", manifest.TargetAbi);
        var framework = RequireValue("build manifest framework", manifest.Framework);
        var targetFrameworkName = GetTargetFrameworkName(framework);
        var artifacts = CreateArtifactSet(manifest.Artifacts);
        var assemblyFileName = GetAssemblyArtifact(artifacts);
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyFileName);
        var expectedMembers = CreateExpectedMembers(artifacts);

        return new PackageContract(
            name,
            guidText,
            pluginGuid,
            versionText,
            pluginVersion,
            targetAbi,
            targetFrameworkName,
            assemblyFileName,
            assemblyName,
            expectedMembers);
    }

    private static Guid ParsePluginGuid(string guidText)
    {
        if (!Guid.TryParse(guidText, out var pluginGuid))
        {
            throw new PackageValidationException(
                $"Build manifest guid must be a valid GUID, got {guidText}.");
        }

        return pluginGuid;
    }

    private static Version ParsePluginVersion(string versionText)
    {
        if (!Version.TryParse(versionText, out var pluginVersion)
            || pluginVersion.Revision < 0)
        {
            throw new PackageValidationException(
                $"Build manifest version must contain four numeric components, got {versionText}.");
        }

        return pluginVersion;
    }

    private static HashSet<string> CreateArtifactSet(string[]? manifestArtifacts)
    {
        if (manifestArtifacts is null || manifestArtifacts.Length == 0)
        {
            throw new PackageValidationException("Build manifest artifacts must not be empty.");
        }

        var artifacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in manifestArtifacts)
        {
            ValidateArtifact(artifact);
            if (!artifacts.Add(artifact))
            {
                throw new PackageValidationException(
                    $"Build manifest contains duplicate artifact {artifact}.");
            }
        }

        return artifacts;
    }

    private static void ValidateArtifact(string artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact)
            || artifact.Contains('/')
            || artifact.Contains('\\')
            || !string.Equals(artifact, Path.GetFileName(artifact), StringComparison.Ordinal)
            || string.Equals(artifact, MetadataFileName, StringComparison.Ordinal))
        {
            throw new PackageValidationException(
                $"Build manifest artifact must be a flat package member, got {artifact ?? "<null>"}.");
        }
    }

    private static string GetAssemblyArtifact(IEnumerable<string> artifacts)
    {
        var assemblyArtifacts = artifacts
            .Where(artifact => artifact.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assemblyArtifacts.Length != 1)
        {
            throw new PackageValidationException(
                "Build manifest must contain exactly one assembly artifact.");
        }

        return assemblyArtifacts[0];
    }

    private static HashSet<string> CreateExpectedMembers(IEnumerable<string> artifacts)
    {
        return new HashSet<string>(artifacts, StringComparer.Ordinal)
        {
            MetadataFileName,
        };
    }

    private static void ValidateArchiveMembers(
        ZipArchive package,
        IReadOnlySet<string> expectedMembers)
    {
        var memberNames = package.Entries.Select(entry => entry.FullName).ToArray();
        var unexpectedMembers = memberNames.Except(expectedMembers, StringComparer.Ordinal).ToArray();
        if (unexpectedMembers.Length > 0)
        {
            throw new PackageValidationException(
                $"Unexpected archive members: {string.Join(", ", unexpectedMembers)}");
        }

        var missingMembers = expectedMembers.Except(memberNames, StringComparer.Ordinal).ToArray();
        if (missingMembers.Length > 0)
        {
            throw new PackageValidationException(
                $"Missing archive members: {string.Join(", ", missingMembers)}");
        }

        if (memberNames.Length != expectedMembers.Count)
        {
            throw new PackageValidationException("Duplicate archive members are not allowed.");
        }
    }

    private static void ValidateMetadata(ZipArchive package, PackageContract contract)
    {
        var metadataEntry = package.GetEntry(MetadataFileName)
            ?? throw new PackageValidationException($"Package does not contain {MetadataFileName}.");
        using var metadataStream = metadataEntry.Open();
        var metadata = ReadJson<PackageMetadata>(metadataStream, MetadataFileName);

        RequireEqual("meta.json name", metadata.Name, contract.Name);
        RequireEqual("meta.json guid", metadata.Guid, contract.GuidText);
        RequireEqual("meta.json version", metadata.Version, contract.VersionText);
        RequireEqual("meta.json targetAbi", metadata.TargetAbi, contract.TargetAbi);
    }

    private static void ValidateProductionAssembly(ZipArchive package, PackageContract contract)
    {
        var assemblyEntry = package.GetEntry(contract.AssemblyFileName)
            ?? throw new PackageValidationException(
                $"Package does not contain {contract.AssemblyFileName}.");
        var temporaryAssemblyPath = Path.Combine(
            Path.GetTempPath(),
            $"{contract.AssemblyName}-{Guid.NewGuid():N}.dll");

        try
        {
            CopyArchiveEntry(assemblyEntry, temporaryAssemblyPath);
            ValidateAssembly(temporaryAssemblyPath, contract);
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
                $"{contract.AssemblyFileName} is not a valid plugin assembly: {error.Message}",
                error);
        }
        finally
        {
            File.Delete(temporaryAssemblyPath);
        }
    }

    private static void CopyArchiveEntry(ZipArchiveEntry assemblyEntry, string temporaryAssemblyPath)
    {
        using var source = assemblyEntry.Open();
        using var destination = File.Create(temporaryAssemblyPath);
        source.CopyTo(destination);
    }

    private static void ValidateAssembly(string temporaryAssemblyPath, PackageContract contract)
    {
        var assemblyIdentity = AssemblyName.GetAssemblyName(temporaryAssemblyPath);
        RequireEqual("assembly name", assemblyIdentity.Name, contract.AssemblyName);
        ValidateAssemblyVersion(assemblyIdentity, contract);

        var fileVersion = FileVersionInfo.GetVersionInfo(temporaryAssemblyPath).FileVersion;
        RequireEqual("file version", fileVersion, contract.VersionText);

        var assembly = Assembly.Load(File.ReadAllBytes(temporaryAssemblyPath));
        var frameworkName = assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;
        RequireEqual("assembly target framework", frameworkName, contract.TargetFrameworkName);
        ValidatePluginIdentity(assembly, contract);
    }

    private static void ValidateAssemblyVersion(
        AssemblyName assemblyIdentity,
        PackageContract contract)
    {
        if (assemblyIdentity.Version != contract.Version)
        {
            throw new PackageValidationException(
                $"Assembly version must be {contract.Version}, got {assemblyIdentity.Version}.");
        }
    }

    private static void ValidatePluginIdentity(Assembly assembly, PackageContract contract)
    {
        var pluginType = assembly.GetType(
            $"{contract.AssemblyName}.Plugin",
            throwOnError: true,
            ignoreCase: false)!;
        var plugin = Activator.CreateInstance(pluginType)
            ?? throw new PackageValidationException("Plugin type could not be instantiated.");
        var name = pluginType.GetProperty("Name")?.GetValue(plugin) as string;
        var id = pluginType.GetProperty("Id")?.GetValue(plugin);

        RequireEqual("plugin name", name, contract.Name);
        if (id is not Guid pluginId || pluginId != contract.Guid)
        {
            throw new PackageValidationException(
                $"Plugin ID must be {contract.GuidText}, got {id}.");
        }
    }

    private static string GetTargetFrameworkName(string framework)
    {
        const string NetPrefix = "net";
        if (!framework.StartsWith(NetPrefix, StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(framework[NetPrefix.Length..], out var version)
            || version.Major < 5
            || version.Build >= 0)
        {
            throw new PackageValidationException(
                $"Build manifest framework must use the modern net<major>.<minor> form, got {framework}.");
        }

        return new FrameworkName(".NETCoreApp", version).FullName;
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

    private static string RequireValue(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PackageValidationException($"{field} must not be empty.");
        }

        return value;
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

    private sealed record PackageContract(
        string Name,
        string GuidText,
        Guid Guid,
        string VersionText,
        Version Version,
        string TargetAbi,
        string TargetFrameworkName,
        string AssemblyFileName,
        string AssemblyName,
        IReadOnlySet<string> ExpectedMembers);
}
