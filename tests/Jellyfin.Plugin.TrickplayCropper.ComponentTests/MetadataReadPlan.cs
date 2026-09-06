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
internal sealed class MetadataReadPlan
{
    private readonly TaskCompletionSource completed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public MetadataReadPlan(
        MetadataAvailability availability,
        MetadataReadOutcome outcome)
    {
        Availability = availability;
        Outcome = outcome;
    }

    public MetadataAvailability Availability { get; }

    public Task Completed => completed.Task;

    public MetadataReadOutcome Outcome { get; }

    public Task Started => started.Task;

    public void Release()
    {
        release.TrySetResult();
    }

    public async Task WaitForReleaseAsync()
    {
        started.TrySetResult();
        await release.Task;
    }

    public void MarkCompleted()
    {
        completed.TrySetResult();
    }
}
