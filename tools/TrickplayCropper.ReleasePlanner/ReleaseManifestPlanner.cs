using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrickplayCropper.ReleasePlanner;

public static class ReleaseManifestPlanner
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ReleasePlan Plan(string manifest, string changelog, ReleaseVersion? proposedVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        ArgumentNullException.ThrowIfNull(changelog);

        string currentText = ReadCurrentVersion(manifest);
        ReleaseVersion next = proposedVersion ?? ReleaseVersion.Parse(currentText).NextRoutine();
        string updated = ReplaceJsonStringField(manifest, "version", next.ToString());
        updated = ReplaceJsonStringField(updated, "changelog", changelog.TrimEnd());

        return new ReleasePlan(updated, next);
    }

    private static string ReadCurrentVersion(string manifest)
    {
        try
        {
            JsonNode root = JsonNode.Parse(manifest)
                ?? throw new ReleasePlanningException("Build manifest must contain a JSON object.");
            return root["version"]?.GetValue<string>()
                ?? throw new ReleasePlanningException("Build manifest is missing a string 'version' field.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ReleasePlanningException(
                $"Build manifest is not a readable JSON object: {error.Message}",
                error);
        }
    }

    private static string ReplaceJsonStringField(string manifest, string key, string newValue)
    {
        string keyToken = $"\"{key}\"";
        int keyIndex = manifest.IndexOf(keyToken, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            throw new ReleasePlanningException($"Build manifest is missing the '{key}' field.");
        }

        int nextKeyIndex = manifest.IndexOf(keyToken, keyIndex + keyToken.Length, StringComparison.Ordinal);
        if (nextKeyIndex >= 0)
        {
            throw new ReleasePlanningException($"Build manifest contains more than one '{key}' field.");
        }

        int colonIndex = manifest.IndexOf(':', keyIndex + keyToken.Length);
        int openQuote = colonIndex >= 0 ? manifest.IndexOf('"', colonIndex + 1) : -1;
        if (openQuote < 0)
        {
            throw new ReleasePlanningException($"Build manifest '{key}' field is not a JSON string.");
        }

        int closeQuote = FindClosingQuote(manifest, openQuote + 1, key);
        return string.Concat(
            manifest.AsSpan(0, openQuote),
            JsonSerializer.Serialize(newValue, jsonOptions),
            manifest.AsSpan(closeQuote + 1));
    }

    private static int FindClosingQuote(string manifest, int start, string key)
    {
        for (int index = start; index < manifest.Length; index++)
        {
            if (manifest[index] == '\\')
            {
                index++;
                continue;
            }

            if (manifest[index] == '"')
            {
                return index;
            }
        }

        throw new ReleasePlanningException($"Build manifest '{key}' field has an unterminated string.");
    }
}
