using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed record RecordedLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
