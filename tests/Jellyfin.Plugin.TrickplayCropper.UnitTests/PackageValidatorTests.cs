namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using global::TrickplayCropper.PackageValidator;
using Xunit;

public sealed class PackageValidatorTests
{
    private const string ValidManifest = """
        {
          "name": "Trickplay Cropper",
          "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
          "version": "1.0.0.0",
          "targetAbi": "10.11.0.0",
          "framework": "net9.0",
          "artifacts": ["Jellyfin.Plugin.TrickplayCropper.dll"]
        }
        """;

    private const string ValidMetadata = """
        {
          "name": "Trickplay Cropper",
          "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
          "version": "1.0.0.0",
          "targetAbi": "10.11.0.0"
        }
        """;

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
        using var fixture = PackageFixture.Create(extraMember: "Jellyfin.Plugin.TrickplayCropper.dll");

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsAdditionalManifestArtifact()
    {
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest["artifacts"] = new JsonArray(
            "Jellyfin.Plugin.TrickplayCropper.dll",
            "README.txt");
        using var fixture = PackageFixture.Create(
            extraMember: "README.txt",
            assembly: ReadProductionAssembly(),
            manifest: manifest.ToJsonString());

        PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath);
    }

    [Fact]
    public void RejectsMissingManifestArtifact()
    {
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest["artifacts"] = new JsonArray(
            "Jellyfin.Plugin.TrickplayCropper.dll",
            "README.txt");
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
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
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
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
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
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest[key] = mismatchedValue;
        var metadata = JsonNode.Parse(ValidMetadata)!.AsObject();
        if (metadata.ContainsKey(key))
        {
            metadata[key] = mismatchedValue;
        }

        using var fixture = PackageFixture.Create(
            assembly: ReadProductionAssembly(),
            metadata: metadata.ToJsonString(),
            manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsManifestDrivenTargetAbi()
    {
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest["targetAbi"] = "99.0.0.0";
        var metadata = JsonNode.Parse(ValidMetadata)!.AsObject();
        metadata["targetAbi"] = "99.0.0.0";
        using var fixture = PackageFixture.Create(
            assembly: ReadProductionAssembly(),
            metadata: metadata.ToJsonString(),
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
        var metadata = JsonNode.Parse(ValidMetadata)!.AsObject();
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
        var unitTestAssembly = File.ReadAllBytes(typeof(PackageValidatorTests).Assembly.Location);
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

            File.WriteAllText(manifestPath, manifest ?? ValidManifest);
            using (var package = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(
                    package,
                    "Jellyfin.Plugin.TrickplayCropper.dll",
                    assembly ?? "assembly"u8.ToArray());
                WriteEntry(package, "meta.json", Encoding.UTF8.GetBytes(metadata ?? ValidMetadata));
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
