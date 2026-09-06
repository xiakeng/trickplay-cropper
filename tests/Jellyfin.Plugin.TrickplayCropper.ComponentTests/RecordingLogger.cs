using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    private readonly List<RecordedLog> entries = [];

    public RecordedLog[] Entries
    {
        get
        {
            lock (entries)
            {
                return entries.ToArray();
            }
        }
    }

    public RecordedLog[] Errors
    {
        get
        {
            lock (entries)
            {
                return entries.Where(entry => entry.Level >= LogLevel.Error).ToArray();
            }
        }
    }

    IDisposable? ILogger.BeginScope<TState>(TState state)
    {
        return null;
    }

    bool ILogger.IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    void ILogger.Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        IReadOnlyDictionary<string, object?> properties = state
            is IEnumerable<KeyValuePair<string, object?>> structuredState
            ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        var log = new RecordedLog(logLevel, eventId, formatter(state, exception), properties, exception);
        lock (entries)
        {
            entries.Add(log);
        }
    }
}
