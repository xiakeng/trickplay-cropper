using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

/// <summary>
/// Carries the stable identity and fields of one recorded log entry.
/// </summary>
/// <param name="Level">The level the entry was logged at.</param>
/// <param name="EventId">The stable Debug protocol identity of the entry.</param>
/// <param name="Properties">The structured fields the entry carried.</param>
internal sealed record RecordedEvent(
    LogLevel Level,
    EventId EventId,
    IReadOnlyDictionary<string, object?> Properties);
