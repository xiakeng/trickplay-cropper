using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using global::TrickplayCropper.PackageValidator;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PackageValidatorSpecs
{
    [Theory]
    [InlineData("Jellyfin.Plugin.TrickplayCropper.pdb")]
    [InlineData("nested/Jellyfin.Plugin.TrickplayCropper.dll")]
    [InlineData("Jellyfin.Controller.dll")]
    [InlineData("SkiaSharp.dll")]
    [InlineData("libSkiaSharp.so")]
    [InlineData("SkiaSharp.NativeAssets.Linux.NoDependencies.dll")]
    [InlineData("runtimes/linux-x64/native/libSkiaSharp.so")]
    public void RejectsForbiddenArchiveMembers(string forbiddenMember)
    {
        using var fixture = PackageFixture.Create(extraMember: forbiddenMember);

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(forbiddenMember, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateArchiveMembers()
    {
        using var fixture = PackageFixture.Create(
            extraMember: GetAssemblyArtifact(ReadBuildManifest()));

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsAdditionalManifestArtifact()
    {
        var manifest = ReadBuildManifest();
        manifest["artifacts"]!.AsArray().Add("README.txt");
        using var fixture = PackageFixture.Create(
            extraMember: "README.txt",
            assembly: ReadProductionAssembly(),
            manifest: manifest.ToJsonString());

        PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath);
    }

    [Fact]
    public void RejectsMissingManifestArtifact()
    {
        var manifest = ReadBuildManifest();
        manifest["artifacts"]!.AsArray().Add("README.txt");
        using var fixture = PackageFixture.Create(manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("README.txt", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("guid", "not-a-guid")]
    [InlineData("version", "1.0")]
    [InlineData("targetAbi", "")]
    [InlineData("framework", "netstandard2.0")]
    public void RejectsInvalidBuildManifestValues(string key, string invalidValue)
    {
        var manifest = ReadBuildManifest();
        manifest[key] = invalidValue;
        using var fixture = PackageFixture.Create(manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("nested/Plugin.dll")]
    [InlineData("nested\\Plugin.dll")]
    [InlineData("meta.json")]
    public void RejectsNonFlatBuildManifestArtifacts(string artifact)
    {
        var manifest = ReadBuildManifest();
        manifest["artifacts"] = new JsonArray(artifact);
        using var fixture = PackageFixture.Create(manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("artifact", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("name", "Another Plugin", "plugin name")]
    [InlineData("guid", "00000000-0000-0000-0000-000000000000", "plugin ID")]
    [InlineData("version", "2.0.0.0", "assembly version")]
    [InlineData("framework", "net8.0", "assembly target framework")]
    public void RejectsAssemblyOrPluginManifestMismatch(
        string key,
        string mismatchedValue,
        string expectedError)
    {
        var manifest = ReadBuildManifest();
        manifest[key] = mismatchedValue;

        using var fixture = PackageFixture.Create(
            assembly: ReadProductionAssembly(),
            manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsManifestDrivenTargetAbi()
    {
        var manifest = ReadBuildManifest();
        manifest["targetAbi"] = "99.0.0.0";
        using var fixture = PackageFixture.Create(
            assembly: ReadProductionAssembly(),
            manifest: manifest.ToJsonString());

        PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath);
    }

    [Theory]
    [InlineData("name", "Another Plugin")]
    [InlineData("guid", "00000000-0000-0000-0000-000000000000")]
    [InlineData("version", "2.0.0.0")]
    [InlineData("targetAbi", "10.12.0.0")]
    public void RejectsIncorrectPackageMetadata(string key, string incorrectValue)
    {
        var metadata = CreateMetadata(ReadBuildManifest());
        metadata[key] = incorrectValue;
        using var fixture = PackageFixture.Create(metadata: metadata.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnInvalidProductionAssembly()
    {
        using var fixture = PackageFixture.Create(assembly: "not an assembly"u8.ToArray());

        Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));
    }

    [Fact]
    public void RejectsAnAssemblyWithTheWrongIdentity()
    {
        var unitTestAssembly = File.ReadAllBytes(typeof(PackageValidatorSpecs).Assembly.Location);
        using var fixture = PackageFixture.Create(assembly: unitTestAssembly);

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("assembly name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsTheInstallContract()
    {
        using var fixture = PackageFixture.Create(assembly: ReadProductionAssembly());

        PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath);
    }

    private static byte[] ReadProductionAssembly()
    {
        return File.ReadAllBytes(typeof(Plugin).Assembly.Location);
    }

    private static JsonObject ReadBuildManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        return JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
    }

    private static JsonObject CreateMetadata(JsonObject manifest)
    {
        return new JsonObject
        {
            ["name"] = manifest["name"]?.DeepClone(),
            ["guid"] = manifest["guid"]?.DeepClone(),
            ["version"] = manifest["version"]?.DeepClone(),
            ["targetAbi"] = manifest["targetAbi"]?.DeepClone(),
        };
    }

    private static string GetAssemblyArtifact(JsonObject manifest)
    {
        return manifest["artifacts"]!
            .AsArray()
            .Select(artifact => artifact!.GetValue<string>())
            .Single(artifact => artifact.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PackageFixture : IDisposable
    {
        private PackageFixture(string directoryPath, string packagePath, string manifestPath)
        {
            DirectoryPath = directoryPath;
            PackagePath = packagePath;
            ManifestPath = manifestPath;
        }

        public string DirectoryPath { get; }

        public string PackagePath { get; }

        public string ManifestPath { get; }

        public static PackageFixture Create(
            string? extraMember = null,
            byte[]? assembly = null,
            string? metadata = null,
            string? manifest = null)
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), $"trickplay-package-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            var packagePath = Path.Combine(directoryPath, "plugin.zip");
            var manifestPath = Path.Combine(directoryPath, "build.yaml");

            var manifestObject = manifest is null
                ? ReadBuildManifest()
                : JsonNode.Parse(manifest)!.AsObject();
            var manifestContents = manifestObject.ToJsonString();
            var metadataContents = metadata ?? CreateMetadata(manifestObject).ToJsonString();

            File.WriteAllText(manifestPath, manifestContents);
            using (var package = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(
                    package,
                    GetAssemblyArtifact(ReadBuildManifest()),
                    assembly ?? "assembly"u8.ToArray());
                WriteEntry(package, "meta.json", Encoding.UTF8.GetBytes(metadataContents));
                if (extraMember is not null)
                {
                    WriteEntry(package, extraMember, "forbidden"u8.ToArray());
                }
            }

            return new PackageFixture(directoryPath, packagePath, manifestPath);
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        private static void WriteEntry(ZipArchive package, string name, byte[] contents)
        {
            var entry = package.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(contents);
        }
    }
}
