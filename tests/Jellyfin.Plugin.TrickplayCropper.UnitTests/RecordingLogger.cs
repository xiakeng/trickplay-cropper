using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

/// <summary>
/// Records every structured log entry so tests can assert stable identities and fields, not message text.
/// </summary>
/// <typeparam name="TCategory">The logger category under test.</typeparam>
internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    private readonly List<RecordedLog> entries = [];

    public IReadOnlyList<RecordedLog> Entries
    {
        get
        {
            lock (entries)
            {
                return entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        IReadOnlyDictionary<string, object?> properties = state
            is IEnumerable<KeyValuePair<string, object?>> structuredState
            ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        lock (entries)
        {
            entries.Add(new RecordedLog(logLevel, eventId, formatter(state, exception), properties, exception));
        }
    }

    internal sealed record RecordedLog(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties,
        Exception? Exception);
}
