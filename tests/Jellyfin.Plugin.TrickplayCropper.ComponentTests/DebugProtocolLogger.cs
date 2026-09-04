using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

/// <summary>
/// Records structured log entries so tests can assert stable Debug protocol identities and fields, not message text.
/// </summary>
/// <typeparam name="TCategory">The logger category under test.</typeparam>
internal sealed class DebugProtocolLogger<TCategory> : ILogger<TCategory>
{
    private readonly LogLevel enabledLevel;
    private readonly List<RecordedEvent> events = [];

    /// <summary>
    /// Initializes a logger that enables every level from Debug upward.
    /// </summary>
    public DebugProtocolLogger()
        : this(LogLevel.Debug)
    {
    }

    /// <summary>
    /// Initializes a logger that enables every level from the specified level upward.
    /// </summary>
    /// <param name="enabledLevel">The lowest level the host enables.</param>
    public DebugProtocolLogger(LogLevel enabledLevel)
    {
        this.enabledLevel = enabledLevel;
    }

    public IReadOnlyList<RecordedEvent> Events
    {
        get
        {
            lock (events)
            {
                return events.ToArray();
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
        return logLevel >= enabledLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Dictionary<string, object?> properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : [];
        lock (events)
        {
            events.Add(new RecordedEvent(logLevel, eventId, properties));
        }
    }
}
