using System.Globalization;
using System.Text.Json;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Reads the versioned plugin event envelope carried by an unchanged Jellyfin text sink.</summary>
public sealed class DebugEventReader
{
    private const string Level = "[DBG] ";
    private const string Marker = "TrickplayDebug ";

    /// <summary>Accepts only fresh FrameSelected events with valid identities and nonnegative frame geometry.</summary>
    public static bool HasFrameSelection(string log, DateTimeOffset since) =>
        Read(log, since).Any(value => value.EventId == 1002);

    /// <summary>Reads only the known stable Debug protocol, retaining event multiplicity.</summary>
    public static IReadOnlyList<ProtocolEvent> Read(string log, DateTimeOffset since)
    {
        List<ProtocolEvent> events = [];
        using StringReader reader = new(log);
        while (reader.ReadLine() is { } line)
        {
            if (ReadLine(line, since) is { } value)
            {
                events.Add(value);
            }
        }

        return events;
    }

    private static ProtocolEvent? ReadLine(string line, DateTimeOffset since)
    {
        int timestampEnd = line.IndexOf(']');
        if (!line.StartsWith('[') || timestampEnd < 1
            || !DateTimeOffset.TryParse(line.AsSpan(1, timestampEnd - 1), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset timestamp) || timestamp < since)
        {
            return null;
        }

        string message = line[(timestampEnd + 1)..].TrimStart();
        if (!message.StartsWith(Level, StringComparison.Ordinal))
        {
            return null;
        }

        message = message[Level.Length..];
        if (message.StartsWith('['))
        {
            int threadEnd = message.IndexOf(']');
            if (threadEnd < 2 || !int.TryParse(message.AsSpan(1, threadEnd - 1), out _))
            {
                return null;
            }

            message = message[(threadEnd + 1)..].TrimStart();
            int categoryEnd = message.IndexOf(": ", StringComparison.Ordinal);
            if (categoryEnd < 0 || !message[..categoryEnd].StartsWith(
                "Jellyfin.Plugin.TrickplayCropper.", StringComparison.Ordinal))
            {
                return null;
            }

            message = message[(categoryEnd + 2)..];
        }

        if (!message.StartsWith(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(message[Marker.Length..]);
            JsonElement value = document.RootElement;
            int id = value.GetProperty("EventId").GetInt32();
            string? name = value.GetProperty("EventName").GetString();
            return (id, name) switch
            {
                (1002, "TrickplayPreviewFrameSelected") => ReadFrame(value),
                (1003, "TrickplayPreviewCacheDisposition") => ReadDisposition(value),
                (1004, "TrickplayPreviewEntryLockWaiting") or (1005, "TrickplayPreviewEntryLockOwned")
                    or (1006, "TrickplayPreviewCacheTreeLeaseWaiting") or (1007, "TrickplayPreviewDecodePermitWaiting")
                    => new ProtocolEvent(id, null, null, null),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    private static ProtocolEvent? ReadFrame(JsonElement value)
    {
        int frame = value.GetProperty("FrameIndex").GetInt32();
        int sprite = value.GetProperty("SpriteIndex").GetInt32();
        return frame >= 0 && sprite >= 0 ? new ProtocolEvent(1002, frame, sprite, null) : null;
    }

    private static ProtocolEvent? ReadDisposition(JsonElement value)
    {
        string? disposition = value.GetProperty("CacheDisposition").GetString();
        return disposition is "Hit" or "Miss" ? new ProtocolEvent(1003, null, null, disposition.ToUpperInvariant()) : null;
    }

    /// <summary>A validated stable event; optional fields belong only to their declared event identity.</summary>
    public sealed record ProtocolEvent(int EventId, int? FrameIndex, int? SpriteIndex, string? Disposition);
}
