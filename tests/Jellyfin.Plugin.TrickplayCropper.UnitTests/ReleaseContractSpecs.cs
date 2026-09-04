using System.Text.Json.Nodes;
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ReleaseContractSpecs
{
    private const string ProductionAssembly = "Jellyfin.Plugin.TrickplayCropper.dll";

    [Fact]
    public void BuildManifestUsesTheApprovedV1Contract()
    {
        JsonObject manifest = JsonNode.Parse(
            RepositoryFiles.Read("src/Jellyfin.Plugin.TrickplayCropper/build.yaml"))!.AsObject();
        string[] artifacts = manifest["artifacts"]!
            .AsArray()
            .Select(artifact => artifact!.GetValue<string>())
            .ToArray();

        Assert.Equal("Trickplay Cropper", manifest["name"]!.GetValue<string>());
        Assert.Equal("630fb758-9a29-4f2c-a54c-95793651bb8a", manifest["guid"]!.GetValue<string>());
        Assert.Equal("10.11.0.0", manifest["targetAbi"]!.GetValue<string>());
        Assert.Equal("net9.0", manifest["framework"]!.GetValue<string>());
        Assert.Equal([ProductionAssembly], artifacts);

        // The release workflow advances this value, so pin the four-component floor rather than an exact version.
        string versionText = manifest["version"]!.GetValue<string>();
        Assert.True(
            Version.TryParse(versionText, out Version? version)
                && version.Revision >= 0
                && version >= new Version(1, 0, 0, 0),
            $"Build manifest version must be four numeric components at or above the 1.0.0.0 floor, got '{versionText}'.");
    }

    [Fact]
    public void ProductionProjectUsesTheApprovedRuntimeContract()
    {
        XDocument project = XDocument.Load(RepositoryFiles.GetPath(
            "src/Jellyfin.Plugin.TrickplayCropper/Jellyfin.Plugin.TrickplayCropper.csproj"));

        Assert.Equal("net9.0", GetProperty(project, "TargetFramework"));
        Assert.Equal(Path.GetFileNameWithoutExtension(ProductionAssembly), GetProperty(project, "AssemblyName"));
        Assert.Equal("1.0.0.0", GetProperty(project, "Version"));
        Assert.Equal("1.0.0.0", GetProperty(project, "FileVersion"));
        Assert.Equal("1.0.0.0", GetProperty(project, "AssemblyVersion"));
        AssertRuntimeExcluded(project, "Jellyfin.Controller");
        AssertRuntimeExcluded(project, "Jellyfin.Model");
        AssertRuntimeExcluded(project, "SkiaSharp");
    }

    [Fact]
    public void DependenciesUseTheApprovedPinnedVersions()
    {
        XDocument packages = XDocument.Load(RepositoryFiles.GetPath("Directory.Packages.props"));
        Dictionary<string, string> versions = packages
            .Descendants("PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("10.11.11", versions["Jellyfin.Controller"]);
        Assert.Equal("10.11.11", versions["Jellyfin.Model"]);
        Assert.Equal("3.116.1", versions["SkiaSharp"]);
        Assert.Equal("3.116.1", versions["SkiaSharp.NativeAssets.Linux.NoDependencies"]);
        Assert.All(versions.Values, version => Assert.DoesNotContain('*', version));
    }

    [Fact]
    public void OnlyTheComponentTestsCarryPrivateLinuxNativeAssets()
    {
        string componentProjectPath = RepositoryFiles.GetPath(
            "tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/"
            + "Jellyfin.Plugin.TrickplayCropper.ComponentTests.csproj");
        XDocument componentProject = XDocument.Load(componentProjectPath);
        Assert.Equal("false", GetProperty(componentProject, "IsPackable"));
        Assert.Equal("false", GetProperty(componentProject, "IsPublishable"));

        (string Path, XElement Reference)[] nativeReferences = EnumerateProjectPaths()
            .Select(path => (Path: path, Project: XDocument.Load(path)))
            .SelectMany(
                item => item.Project.Descendants("PackageReference")
                    .Where(reference => reference.Attribute("Include")?.Value.StartsWith(
                        "SkiaSharp.NativeAssets.",
                        StringComparison.Ordinal) == true)
                    .Select(reference => (item.Path, Reference: reference)))
            .ToArray();
        (string nativeProjectPath, XElement nativeReference) = Assert.Single(nativeReferences);

        Assert.Equal(componentProjectPath, nativeProjectPath);
        Assert.Equal("SkiaSharp.NativeAssets.Linux.NoDependencies", nativeReference.Attribute("Include")!.Value);
        Assert.Equal("all", nativeReference.Attribute("PrivateAssets")!.Value);
    }

    [Fact]
    public void EveryProjectCommitsALockedDependencyGraph()
    {
        string[] projectPaths = EnumerateProjectPaths();

        Assert.NotEmpty(projectPaths);
        Assert.All(
            projectPaths,
            projectPath => Assert.True(
                File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json")),
                $"Missing packages.lock.json beside {Path.GetRelativePath(RepositoryFiles.Root, projectPath)}."));
    }

    private static void AssertRuntimeExcluded(XDocument project, string packageName)
    {
        XElement reference = Assert.Single(
            project.Descendants("PackageReference"),
            element => element.Attribute("Include")?.Value == packageName);
        Assert.Equal("runtime", reference.Element("ExcludeAssets")?.Value);
    }

    private static string[] EnumerateProjectPaths()
    {
        return Directory.EnumerateFiles(RepositoryFiles.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetProperty(XDocument project, string name)
    {
        return project.Descendants(name).SingleOrDefault()?.Value;
    }
}
