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

    [Theory]
    [InlineData("name", "Another Plugin")]
    [InlineData("guid", "00000000-0000-0000-0000-000000000000")]
    [InlineData("version", "2.0.0.0")]
    [InlineData("targetAbi", "10.12.0.0")]
    [InlineData("framework", "net10.0")]
    public void RejectsIncorrectBuildManifestValues(string key, string incorrectValue)
    {
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest[key] = incorrectValue;
        using var fixture = PackageFixture.Create(manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsIncorrectBuildManifestArtifacts()
    {
        var manifest = JsonNode.Parse(ValidManifest)!.AsObject();
        manifest["artifacts"] = new JsonArray("Jellyfin.Plugin.TrickplayCropper.pdb");
        using var fixture = PackageFixture.Create(manifest: manifest.ToJsonString());

        var error = Assert.Throws<PackageValidationException>(
            () => PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath));

        Assert.Contains("artifacts", error.Message, StringComparison.OrdinalIgnoreCase);
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
        var productionAssembly = File.ReadAllBytes(typeof(Plugin).Assembly.Location);
        using var fixture = PackageFixture.Create(assembly: productionAssembly);

        PackageValidator.Validate(fixture.PackagePath, fixture.ManifestPath);
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
