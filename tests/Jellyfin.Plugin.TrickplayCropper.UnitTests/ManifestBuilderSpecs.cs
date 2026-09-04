using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TrickplayCropper.ManifestBuilder;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ManifestBuilderSpecs
{
    private const string PluginGuid = "630fb758-9a29-4f2c-a54c-95793651bb8a";
    private const string SourceUrl =
        "https://github.com/xiakeng/trickplay-cropper/releases/download/v1.0.1.0/trickplay-cropper_1.0.1.0.zip";

    private const string SyntheticBuildManifest =
        """
        {
          "name": "Trickplay Cropper",
          "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
          "version": "1.0.1.0",
          "targetAbi": "10.11.0.0",
          "framework": "net9.0",
          "overview": "Return authenticated Trickplay Previews from Jellyfin-owned Source Sprites.",
          "description": "Provides authenticated, single-frame Trickplay Previews from Jellyfin-owned Source Sprites.",
          "category": "General",
          "owner": "xiakeng",
          "artifacts": [
            "Jellyfin.Plugin.TrickplayCropper.dll"
          ],
          "changelog": "- Second release entry"
        }
        """;

    [Fact]
    public void FirstReleaseCreatesTheManifestSkeleton()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl);

        JsonArray manifest = JsonNode.Parse(plan.UpdatedManifest)!.AsArray();
        JsonObject plugin = Assert.Single(manifest.Select(node => node!.AsObject()));

        Assert.Equal(PluginGuid, plugin["guid"]!.GetValue<string>());
        Assert.Equal("Trickplay Cropper", plugin["name"]!.GetValue<string>());
        Assert.Equal("xiakeng", plugin["owner"]!.GetValue<string>());
        Assert.Equal("General", plugin["category"]!.GetValue<string>());

        JsonArray versions = plugin["versions"]!.AsArray();
        JsonObject entry = Assert.Single(versions.Select(node => node!.AsObject()));
        Assert.Equal("1.0.1.0", entry["version"]!.GetValue<string>());
    }

    [Fact]
    public void EntryCarriesEveryRequiredField()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl);

        JsonObject entry = ReadSingleVersionEntry(plan.UpdatedManifest);

        Assert.Equal("1.0.1.0", entry["version"]!.GetValue<string>());
        Assert.Equal("- Second release entry", entry["changelog"]!.GetValue<string>());
        Assert.Equal("10.11.0.0", entry["targetAbi"]!.GetValue<string>());
        Assert.Equal(SourceUrl, entry["sourceUrl"]!.GetValue<string>());
        Assert.Equal("2026-09-02T07:00:00Z", entry["timestamp"]!.GetValue<string>());
        Assert.Matches(@"^[0-9a-f]{32}$", entry["checksum"]!.GetValue<string>());
    }

    [Fact]
    public void ChecksumIsTheMd5OfTheActualZipBytes()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");
#pragma warning disable CA5351 // The test verifies the Jellyfin manifest MD5 checksum contract.
        string expected = Convert.ToHexString(
            MD5.HashData(File.ReadAllBytes(zip.ZipPath))).ToLowerInvariant();
#pragma warning restore CA5351

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl);

        Assert.Equal(expected, ReadSingleVersionEntry(plan.UpdatedManifest)["checksum"]!.GetValue<string>());
    }

    [Fact]
    public void TimestampIsReadFromTheEmbeddedMetadata()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-03-15T12:30:45Z");

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl);

        Assert.Equal(
            "2026-03-15T12:30:45Z",
            ReadSingleVersionEntry(plan.UpdatedManifest)["timestamp"]!.GetValue<string>());
    }

    [Fact]
    public void LaterReleaseRetainsEveryExistingEntry()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.2.0", "2026-09-03T08:00:00Z");
        string existing = CreateExistingManifest(
            ("1.0.1.0", "2026-09-02T07:00:00Z", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            ("1.0.0.0", "2026-09-01T06:00:00Z", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            CreateBuildManifest("1.0.2.0"), zip.ZipPath, existing, SourceUrl);

        JsonArray versions = ReadVersions(plan.UpdatedManifest);
        Assert.Equal(3, versions.Count);
        Assert.Equal("1.0.2.0", versions[0]!["version"]!.GetValue<string>());
        Assert.Equal("1.0.1.0", versions[1]!["version"]!.GetValue<string>());
        Assert.Equal("1.0.0.0", versions[2]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void VersionsAreSortedInDescendingNumericOrder()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");
        string existing = CreateExistingManifest(
            ("2.0.0.0", "2026-09-05T00:00:00Z", "cccccccccccccccccccccccccccccccc"),
            ("1.0.0.0", "2026-09-01T00:00:00Z", "dddddddddddddddddddddddddddddddd"),
            ("1.5.3.7", "2026-09-03T00:00:00Z", "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"));

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existing, SourceUrl);

        string[] versions = ReadVersions(plan.UpdatedManifest)
            .Select(node => node!["version"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(["2.0.0.0", "1.5.3.7", "1.0.1.0", "1.0.0.0"], versions);
    }

    [Fact]
    public void SameVersionReplacesTheExistingEntry()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T09:00:00Z");
        string existing = CreateExistingManifest(
            ("1.0.1.0", "2026-09-02T07:00:00Z", "old_checksum_old_checksum_old_ch"),
            ("1.0.0.0", "2026-09-01T06:00:00Z", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existing, SourceUrl);

        JsonArray versions = ReadVersions(plan.UpdatedManifest);
        Assert.Equal(2, versions.Count);

        JsonObject replaced = versions[0]!.AsObject();
        Assert.Equal("1.0.1.0", replaced["version"]!.GetValue<string>());
        Assert.Equal("2026-09-02T09:00:00Z", replaced["timestamp"]!.GetValue<string>());
        Assert.NotEqual("old_checksum_old_checksum_old_ch", replaced["checksum"]!.GetValue<string>());
    }

    [Fact]
    public void PluginIdentityIsRefreshedFromTheCurrentBuildManifest()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");
        string existing = CreateExistingManifest(
            ("1.0.0.0", "2026-09-01T06:00:00Z", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existing, SourceUrl);

        JsonObject plugin = JsonNode.Parse(plan.UpdatedManifest)!.AsArray()[0]!.AsObject();
        Assert.Equal("Trickplay Cropper", plugin["name"]!.GetValue<string>());
        Assert.Equal(PluginGuid, plugin["guid"]!.GetValue<string>());
    }

    [Fact]
    public void RejectsAZipWithoutEmbeddedMetadata()
    {
        using ZipFixture zip = ZipFixture.CreateWithoutMetadata();

        var error = Assert.Throws<ManifestBuildingException>(
            () => RepositoryManifestBuilder.Build(
                SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl));

        Assert.Contains("meta.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMetadataWithoutATimestamp()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", timestamp: null);

        var error = Assert.Throws<ManifestBuildingException>(
            () => RepositoryManifestBuilder.Build(
                SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl));

        Assert.Contains("timestamp", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMetadataWithoutAVersion()
    {
        using ZipFixture zip = ZipFixture.CreateWithoutVersion();

        var error = Assert.Throws<ManifestBuildingException>(
            () => RepositoryManifestBuilder.Build(
                SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl));

        Assert.Contains("version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanReturnsThePublishedVersion()
    {
        using ZipFixture zip = ZipFixture.Create("1.0.1.0", "2026-09-02T07:00:00Z");

        ManifestPlan plan = RepositoryManifestBuilder.Build(
            SyntheticBuildManifest, zip.ZipPath, existingManifest: null, SourceUrl);

        Assert.Equal("1.0.1.0", plan.Version);
    }

    private static JsonObject ReadSingleVersionEntry(string manifest)
    {
        return ReadVersions(manifest).Single()!.AsObject();
    }

    private static string CreateBuildManifest(string version)
    {
        return SyntheticBuildManifest.Replace("\"1.0.1.0\"", $"\"{version}\"", StringComparison.Ordinal);
    }

    private static JsonArray ReadVersions(string manifest)
    {
        return JsonNode.Parse(manifest)!.AsArray()[0]!.AsObject()["versions"]!.AsArray();
    }

    private static string CreateExistingManifest(params (string Version, string Timestamp, string Checksum)[] entries)
    {
        JsonArray versions = [];
        foreach ((string version, string timestamp, string checksum) in entries)
        {
            versions.Add(new JsonObject
            {
                ["version"] = version,
                ["changelog"] = $"Changelog for {version}",
                ["targetAbi"] = "10.11.0.0",
                ["sourceUrl"] = $"https://github.com/xiakeng/trickplay-cropper/releases/download/v{version}/trickplay-cropper_{version}.zip",
                ["checksum"] = checksum,
                ["timestamp"] = timestamp,
            });
        }

        JsonArray manifest =
        [
            new JsonObject
            {
                ["guid"] = PluginGuid,
                ["name"] = "Trickplay Cropper",
                ["description"] = "Provides authenticated, single-frame Trickplay Previews from Jellyfin-owned Source Sprites.",
                ["overview"] = "Return authenticated Trickplay Previews from Jellyfin-owned Source Sprites.",
                ["owner"] = "xiakeng",
                ["category"] = "General",
                ["versions"] = versions,
            },
        ];

        return manifest.ToJsonString();
    }

    private sealed class ZipFixture : IDisposable
    {
        private ZipFixture(string directoryPath, string zipPath)
        {
            DirectoryPath = directoryPath;
            ZipPath = zipPath;
        }

        public string DirectoryPath { get; }

        public string ZipPath { get; }

        public static ZipFixture Create(string version, string? timestamp)
        {
            string directory = Path.Combine(
                Path.GetTempPath(), $"manifest-zip-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string zipPath = Path.Combine(directory, "plugin.zip");

            JsonObject meta = new()
            {
                ["name"] = "Trickplay Cropper",
                ["guid"] = PluginGuid,
                ["version"] = version,
                ["targetAbi"] = "10.11.0.0",
            };
            if (timestamp is not null)
            {
                meta["timestamp"] = timestamp;
            }

            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = zip.CreateEntry("meta.json");
                using Stream stream = entry.Open();
                using StreamWriter writer = new(stream);
                writer.Write(meta.ToJsonString());
            }

            return new ZipFixture(directory, zipPath);
        }

        public static ZipFixture CreateWithoutMetadata()
        {
            string directory = Path.Combine(
                Path.GetTempPath(), $"manifest-zip-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string zipPath = Path.Combine(directory, "plugin.zip");

            using (ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
            }

            return new ZipFixture(directory, zipPath);
        }

        public static ZipFixture CreateWithoutVersion()
        {
            string directory = Path.Combine(
                Path.GetTempPath(), $"manifest-zip-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string zipPath = Path.Combine(directory, "plugin.zip");

            JsonObject meta = new()
            {
                ["name"] = "Trickplay Cropper",
                ["guid"] = PluginGuid,
                ["targetAbi"] = "10.11.0.0",
                ["timestamp"] = "2026-09-02T07:00:00Z",
            };

            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = zip.CreateEntry("meta.json");
                using Stream stream = entry.Open();
                using StreamWriter writer = new(stream);
                writer.Write(meta.ToJsonString());
            }

            return new ZipFixture(directory, zipPath);
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
