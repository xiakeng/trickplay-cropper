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

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.PreviewHttpTestValues;
using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class PreviewHostFixture : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string temporaryDirectory;

    private PreviewHostFixture(IHost host, string temporaryDirectory, string sourceSpritePath, bool useKestrel)
    {
        this.host = host;
        this.temporaryDirectory = temporaryDirectory;
        SourceSpritePath = sourceSpritePath;
        Client = useKestrel ? CreateKestrelClient(host) : host.GetTestClient();
    }

    public RecordingPreviewCache Cache => Services.GetRequiredService<RecordingPreviewCache>();

    public string CacheRoot => Path.Combine(
        temporaryDirectory,
        "Jellyfin.Plugin.TrickplayCropper",
        "preview-v1");

    public HttpClient Client { get; }

    public int ErrorLogCount => ErrorLogs.Length;

    public RecordedLog[] ErrorLogs => Services.GetRequiredService<RecordingLogger<TrickplayPreview>>().Errors;

    public RecordedLog[] DebugLogs =>
        Services.GetRequiredService<RecordingLogger<TrickplayPreview>>().Entries
            .Where(entry => entry.Level == LogLevel.Debug)
            .ToArray();

    public RecordedLog[] ProbeDebugLogs =>
        Services.GetRequiredService<RecordingLogger<TrickplayFrameProbe>>().Entries
            .Where(entry => entry.Level == LogLevel.Debug)
            .ToArray();

    public IServiceProvider Services => host.Services;

    public string SourceSpritePath { get; }

    public int SourceSpritePathRequests =>
        Services.GetRequiredService<PreviewScenario>().SourceSpritePathRequests;

    public static Task<PreviewHostFixture> CreateAsync()
    {
        return CreateAsync(new PreviewScenario());
    }

    public static Task<PreviewHostFixture> CreateAsync(PreviewScenario scenario)
    {
        return CreateAsync(scenario, useKestrel: false);
    }

    public static Task<PreviewHostFixture> CreateWithKestrelAsync(PreviewScenario scenario)
    {
        return CreateAsync(scenario, useKestrel: true);
    }

    private static async Task<PreviewHostFixture> CreateAsync(PreviewScenario scenario, bool useKestrel)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        string sourceSpritePath = CreateSourceSprite(temporaryDirectory, scenario);
        var context = new PreviewHostContext(temporaryDirectory, sourceSpritePath, scenario);

        var hostBuilder = new HostBuilder();
        hostBuilder.ConfigureWebHost(webHost => ConfigureWebHost(webHost, context, useKestrel));
        IHost host = await hostBuilder.StartAsync(CancellationToken.None);
        return new PreviewHostFixture(host, temporaryDirectory, sourceSpritePath, useKestrel);
    }

    private static HttpClient CreateKestrelClient(IHost host)
    {
        IServer server = host.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        ArgumentNullException.ThrowIfNull(addresses);
        return new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single(), UriKind.Absolute) };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static void ConfigureWebHost(IWebHostBuilder webHost, PreviewHostContext context, bool useKestrel)
    {
        if (useKestrel)
        {
            webHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        }
        else
        {
            webHost.UseTestServer();
        }

        webHost.ConfigureServices(services => ConfigureServices(services, context));
        webHost.Configure(application =>
        {
            application.Use(async (request, next) =>
            {
                try
                {
                    await next(request);
                }
                finally
                {
                    context.Scenario.FirstRequestCompleted.TrySetResult();
                }
            });
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        });
    }

    private static void ConfigureServices(IServiceCollection services, PreviewHostContext context)
    {
        services.AddLogging();
        services.AddSingleton(context.Scenario);
        PreviewHttpAuthentication.ConfigureServices(services);
        services.AddControllers().AddApplicationPart(typeof(TrickplayPreviewController).Assembly);

        IApplicationPaths applicationPaths = CreateApplicationPaths(context.TemporaryDirectory);
        PreviewHttpJellyfinFakes.Register(services, context, applicationPaths);
        RegisterPluginServices(services, applicationPaths, context);
    }

    private static void RegisterPluginServices(
        IServiceCollection services,
        IApplicationPaths applicationPaths,
        PreviewHostContext context)
    {
        services.AddSingleton<TimeProvider>(context.Scenario.Time);
        var registrator = new PluginServiceRegistrator();
        registrator.RegisterServices(services, InterfaceMock.Create<IServerApplicationHost>().Service);

        var recordingLogger = new RecordingLogger<TrickplayPreview>();
        services.AddSingleton(recordingLogger);
        services.AddSingleton<ILogger<TrickplayPreview>>(recordingLogger);
        var probeRecordingLogger = new RecordingLogger<TrickplayFrameProbe>();
        services.AddSingleton(probeRecordingLogger);
        services.AddSingleton<ILogger<TrickplayFrameProbe>>(probeRecordingLogger);
        string cacheRoot = Path.Combine(
            context.TemporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            "preview-v1");
        string? failureMessage = context.Scenario.FailsCacheAccess
            ? $"component-secret SourceSpritePath={context.SourceSpritePath} CachePath={cacheRoot}"
            : null;
        var cache = new RecordingPreviewCache(
            new DiskPreviewCache(
                applicationPaths,
                TimeProvider.System,
                NullLogger<DiskPreviewCache>.Instance),
            failureMessage,
            context.Scenario);
        services.AddSingleton(cache);
        services.AddSingleton<IPreviewCache>(cache);
        services.AddSingleton<IPreviewCacheMaintenance>(cache);
    }

    private static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
    {
        InterfaceMockSpecs<IApplicationPaths> mock = InterfaceMock.Create<IApplicationPaths>();
        mock.Handle("get_TempDirectory", _ => temporaryDirectory);
        return mock.Service;
    }

    private static string CreateSourceSprite(string temporaryDirectory, PreviewScenario scenario)
    {
        string sourceSpritePath = Path.Combine(temporaryDirectory, "source-sprite.jpg");
        int frameWidth = scenario.Metadata == MetadataAvailability.MultipleWidths ? 640 : 320;
        int sourceWidth = scenario.SourceSprite == SourceSpriteAvailability.DimensionMismatch
            ? frameWidth
            : frameWidth * 2;
        using var bitmap = new SKBitmap(sourceWidth, 360, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        DrawCell(canvas, SKColors.Red, 0, 0, frameWidth);
        DrawCell(canvas, SKColors.Green, frameWidth, 0, frameWidth);
        DrawCell(canvas, SKColors.Blue, 0, 180, frameWidth);
        DrawCell(canvas, SKColors.Yellow, frameWidth, 180, frameWidth);
        using FileStream output = File.Create(sourceSpritePath);
        Assert.True(bitmap.Encode(output, SKEncodedImageFormat.Jpeg, quality: 100));
        output.Close();
        File.SetLastWriteTimeUtc(sourceSpritePath, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        return sourceSpritePath;
    }

    private static void DrawCell(SKCanvas canvas, SKColor color, int left, int top, int frameWidth)
    {
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(left, top, frameWidth, 180, paint);
    }
}
