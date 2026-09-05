using System.Globalization;
using System.Text.Json;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Reads the versioned plugin event envelope carried by an unchanged Jellyfin text sink.</summary>
public sealed class DebugEventReader
{
    private const string Level = "[DBG] ";
    private const string Marker = "TrickplayDebug ";

    /// <summary>Accepts only fresh FrameSelected events with valid identities and nonnegative frame geometry.</summary>
    public static bool HasFrameSelection(string log, DateTimeOffset since)
    {
        using StringReader reader = new(log);
        while (reader.ReadLine() is { } line)
        {
            if (IsFrameSelection(line, since))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameSelection(string line, DateTimeOffset since)
    {
        int timestampEnd = line.IndexOf(']');
        if (!line.StartsWith('[') || timestampEnd < 1
            || !DateTimeOffset.TryParse(line.AsSpan(1, timestampEnd - 1), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset timestamp) || timestamp < since)
        {
            return false;
        }

        string message = line[(timestampEnd + 1)..].TrimStart();
        if (!message.StartsWith(Level, StringComparison.Ordinal))
        {
            return false;
        }

        message = message[Level.Length..];
        if (message.StartsWith('['))
        {
            int threadEnd = message.IndexOf(']');
            if (threadEnd < 2 || !int.TryParse(message.AsSpan(1, threadEnd - 1), out _))
            {
                return false;
            }

            message = message[(threadEnd + 1)..].TrimStart();
            int categoryEnd = message.IndexOf(": ", StringComparison.Ordinal);
            if (categoryEnd < 0 || !message[..categoryEnd].StartsWith(
                "Jellyfin.Plugin.TrickplayCropper.", StringComparison.Ordinal))
            {
                return false;
            }

            message = message[(categoryEnd + 2)..];
        }

        if (!message.StartsWith(Marker, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(message[Marker.Length..]);
            JsonElement value = document.RootElement;
            return value.GetProperty("EventId").GetInt32() == 1002
                && value.GetProperty("EventName").GetString() == "TrickplayPreviewFrameSelected"
                && value.GetProperty("FrameIndex").GetInt32() >= 0
                && value.GetProperty("SpriteIndex").GetInt32() >= 0;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }
}
