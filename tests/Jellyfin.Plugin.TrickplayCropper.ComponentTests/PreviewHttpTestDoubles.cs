using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;
internal sealed class RecordingPreviewCache : IPreviewCache
{
    private readonly string? failureMessage;
    private readonly List<PreviewIdentity> identities = [];
    private readonly IPreviewCache inner;
    private readonly PreviewScenario scenario;
    private int callCount;

    public RecordingPreviewCache(
        IPreviewCache inner,
        string? failureMessage,
        PreviewScenario scenario)
    {
        this.inner = inner;
        this.failureMessage = failureMessage;
        this.scenario = scenario;
    }

    public int CallCount => Volatile.Read(ref callCount);

    public PreviewIdentity[] Identities
    {
        get
        {
            lock (identities)
            {
                return identities.ToArray();
            }
        }
    }

    async Task<PreviewCacheResult> IPreviewCache.GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref callCount);
        lock (identities)
        {
            identities.Add(identity);
        }

        if (failureMessage is not null)
        {
            throw new InvalidOperationException(failureMessage);
        }

        if (scenario.BlocksCacheAccessUntilCancellation)
        {
            scenario.CacheAccessStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        return await inner.GetOrCreateAsync(identity, writer, cancellationToken).ConfigureAwait(false);
    }

    Task IPreviewCacheMaintenance.ClearAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        return inner.ClearAsync(progress, cancellationToken);
    }
}

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

internal sealed record RecordedLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);

public class InterfaceMockSpecs<TInterface> : DispatchProxy
    where TInterface : class
{
    private readonly Dictionary<string, Func<object?[]?, object?>> handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="InterfaceMockSpecs{TInterface}"/> class.
    /// </summary>
    public InterfaceMockSpecs()
    {
    }

    public TInterface Service => (TInterface)(object)this;

    public void Handle(string methodName, Func<object?[]?, object?> handler)
    {
        handlers.Add(methodName, handler);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (handlers.TryGetValue(targetMethod.Name, out Func<object?[]?, object?>? handler))
        {
            return handler(args);
        }

        throw new InvalidOperationException($"Unexpected Jellyfin call: {targetMethod.Name}.");
    }
}

internal static class InterfaceMock
{
    public static InterfaceMockSpecs<TInterface> Create<TInterface>()
        where TInterface : class
    {
        TInterface service = DispatchProxy.Create<TInterface, InterfaceMockSpecs<TInterface>>();
        return (InterfaceMockSpecs<TInterface>)(object)service;
    }
}
