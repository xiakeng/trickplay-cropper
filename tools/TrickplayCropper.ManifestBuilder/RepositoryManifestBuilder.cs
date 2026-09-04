using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrickplayCropper.ManifestBuilder;

public static class RepositoryManifestBuilder
{
    private const string MetadataFileName = "meta.json";

    private static readonly JsonSerializerOptions writeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ManifestPlan Build(
        string buildManifest,
        string zipPath,
        string? existingManifest,
        string sourceUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);

        JsonObject build = ParseBuildManifest(buildManifest);
        string version = RequireString(build, "version");
        string guid = RequireString(build, "guid");
        string checksum = ComputeMd5(zipPath);
        string timestamp = ReadEmbeddedTimestamp(zipPath);

        JsonObject versionEntry = new()
        {
            ["version"] = version,
            ["changelog"] = RequireString(build, "changelog"),
            ["targetAbi"] = RequireString(build, "targetAbi"),
            ["sourceUrl"] = sourceUrl,
            ["checksum"] = checksum,
            ["timestamp"] = timestamp,
        };

        JsonObject pluginEntry = new()
        {
            ["guid"] = guid,
            ["name"] = RequireString(build, "name"),
            ["description"] = RequireString(build, "description"),
            ["overview"] = RequireString(build, "overview"),
            ["owner"] = RequireString(build, "owner"),
            ["category"] = RequireString(build, "category"),
            ["versions"] = new JsonArray(versionEntry),
        };

        JsonArray manifest = MergeManifest(existingManifest, pluginEntry, version);
        return new ManifestPlan(manifest.ToJsonString(writeOptions), version);
    }

    private static JsonArray MergeManifest(string? existingManifest, JsonObject newPlugin, string newVersion)
    {
        if (string.IsNullOrWhiteSpace(existingManifest))
        {
            return [newPlugin];
        }

        JsonArray existing = ParseExistingManifest(existingManifest);
        string guid = newPlugin["guid"]!.GetValue<string>();

        JsonArray result = [];
        bool found = false;

        foreach (JsonNode? node in existing)
        {
            JsonObject plugin = node!.AsObject();
            if (string.Equals(plugin["guid"]?.GetValue<string>(), guid, StringComparison.Ordinal))
            {
                found = true;
                result.Add(MergePlugin(plugin, newPlugin, newVersion));
            }
            else
            {
                result.Add(plugin.DeepClone());
            }
        }

        if (!found)
        {
            result.Add(newPlugin);
        }

        return result;
    }

    private static JsonObject MergePlugin(JsonObject existing, JsonObject newPlugin, string newVersion)
    {
        JsonObject merged = [];

        foreach (KeyValuePair<string, JsonNode?> field in newPlugin)
        {
            if (!string.Equals(field.Key, "versions", StringComparison.Ordinal))
            {
                merged[field.Key] = field.Value!.DeepClone();
            }
        }

        List<JsonObject> versions = [];
        JsonArray? existingVersions = existing["versions"]?.AsArray();
        if (existingVersions is not null)
        {
            foreach (JsonNode? versionNode in existingVersions)
            {
                JsonObject entry = versionNode!.AsObject();
                if (!string.Equals(entry["version"]?.GetValue<string>(), newVersion, StringComparison.Ordinal))
                {
                    versions.Add(entry.DeepClone().AsObject());
                }
            }
        }

        versions.Add(newPlugin["versions"]!.AsArray()[0]!.DeepClone().AsObject());
        versions.Sort((a, b) => ParseVersion(b["version"]!.GetValue<string>())
            .CompareTo(ParseVersion(a["version"]!.GetValue<string>())));

        JsonArray sortedVersions = [];
        foreach (JsonObject entry in versions)
        {
            sortedVersions.Add(entry);
        }

        merged["versions"] = sortedVersions;
        return merged;
    }

    private static JsonArray ParseExistingManifest(string existingManifest)
    {
        try
        {
            return JsonNode.Parse(existingManifest)?.AsArray()
                ?? throw new ManifestBuildingException("Existing repository manifest must be a JSON array.");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new ManifestBuildingException(
                $"Existing repository manifest is not a valid JSON array: {error.Message}",
                error);
        }
    }

    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out Version? version) || version.Revision < 0)
        {
            throw new ManifestBuildingException(
                $"Repository manifest version must contain four numeric components, got '{value}'.");
        }

        return version;
    }

    private static string ComputeMd5(string zipPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(zipPath);
#pragma warning disable CA5351 // Jellyfin's repository manifest contract requires an MD5 checksum of the ZIP.
            return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
#pragma warning restore CA5351
        }
        catch (IOException error)
        {
            throw new ManifestBuildingException(
                $"Could not read the plugin ZIP to compute its MD5 checksum: {error.Message}",
                error);
        }
    }

    private static string ReadEmbeddedTimestamp(string zipPath)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry metaEntry = zip.GetEntry(MetadataFileName)
                ?? throw new ManifestBuildingException(
                    $"The plugin ZIP does not contain {MetadataFileName}.");

            using Stream metaStream = metaEntry.Open();
            JsonObject meta = JsonNode.Parse(metaStream)?.AsObject()
                ?? throw new ManifestBuildingException(
                    $"{MetadataFileName} must contain a JSON object.");

            return RequireString(meta, "timestamp");
        }
        catch (Exception error) when (
            error is JsonException or InvalidOperationException or InvalidDataException)
        {
            throw new ManifestBuildingException(
                $"Could not read the embedded timestamp from {MetadataFileName}: {error.Message}",
                error);
        }
    }

    private static JsonObject ParseBuildManifest(string buildManifest)
    {
        try
        {
            return JsonNode.Parse(buildManifest)?.AsObject()
                ?? throw new ManifestBuildingException("Build manifest must contain a JSON object.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ManifestBuildingException(
                $"Build manifest is not a readable JSON object: {error.Message}",
                error);
        }
    }

    private static string RequireString(JsonObject source, string key)
    {
        string? value = source[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ManifestBuildingException(
                $"Build manifest is missing a non-empty string '{key}' field.");
        }

        return value;
    }
}
