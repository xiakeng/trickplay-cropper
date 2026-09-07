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
