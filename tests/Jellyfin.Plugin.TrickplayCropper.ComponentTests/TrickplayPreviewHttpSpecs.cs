using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewHttpSpecs
{
    private static readonly Guid itemId = Guid.Parse("3f728b7b-4aa5-4f65-b488-a6029edb6725");
    private static readonly Guid userId = Guid.Parse("e07c89e3-a67e-49f5-9cbf-76b980ebe59a");

    [Fact]
    public async Task ServesGeneratedDefaultSourcePreview()
    {
        await using var fixture = await PreviewHostFixture.CreateAsync();
        using var response = await fixture.Client.GetAsync(
            $"/TrickplayCropper/Videos/{itemId:D}/Preview?PositionTicks=0",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.Equal(
            "\"d5b827fd3d17075e86151d7299ff22cd-f0000000000\"",
            response.Headers.ETag?.Tag);
        Assert.Equal("MISS", response.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.False(response.Headers.Contains("X-Trickplay-Cache-File"));
        Assert.Contains("lookup;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("cache;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("decode;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("encode;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);

        byte[] content = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        Assert.Equal(content.Length, response.Content.Headers.ContentLength);
        using SKBitmap decoded = SKBitmap.Decode(content);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(180, decoded.Height);
        SKColor center = decoded.GetPixel(160, 90);
        Assert.InRange(center.Red, 240, 255);
        Assert.InRange(center.Green, 0, 15);
        Assert.InRange(center.Blue, 0, 15);
        string expectedEntryPath = Path.Combine(
            fixture.CacheRoot,
            itemId.ToString("N"),
            "w0320",
            "s000000-d5b827fd3d17075e86151d7299ff22cd",
            "f0000000000.jpg");
        Assert.True(File.Exists(expectedEntryPath));
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(fixture.CacheRoot, "*.tmp", SearchOption.AllDirectories));

        Assert.Same(
            fixture.Services.GetRequiredService<ITrickplayPreview>(),
            fixture.Services.GetRequiredService<ITrickplayPreview>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewSourceResolver>(),
            fixture.Services.GetRequiredService<IPreviewSourceResolver>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewCache>(),
            fixture.Services.GetRequiredService<IPreviewCache>());
        Assert.Same(
            fixture.Services.GetRequiredService<ITrickplayPreviewEncoder>(),
            fixture.Services.GetRequiredService<ITrickplayPreviewEncoder>());
    }

    private sealed class PreviewHostFixture : IAsyncDisposable
    {
        private readonly IHost host;
        private readonly string temporaryDirectory;

        private PreviewHostFixture(IHost host, string temporaryDirectory)
        {
            this.host = host;
            this.temporaryDirectory = temporaryDirectory;
            Client = host.GetTestClient();
        }

        public HttpClient Client { get; }

        public string CacheRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            "preview-v1");

        public IServiceProvider Services => host.Services;

        public static async Task<PreviewHostFixture> CreateAsync()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            string sourceSpritePath = CreateSourceSprite(temporaryDirectory);

            var hostBuilder = new HostBuilder();
            hostBuilder.ConfigureWebHost(webHost => ConfigureWebHost(webHost, temporaryDirectory, sourceSpritePath));
            IHost host = await hostBuilder.StartAsync(CancellationToken.None);
            return new PreviewHostFixture(host, temporaryDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            Directory.Delete(temporaryDirectory, recursive: true);
        }

        private static void ConfigureWebHost(
            IWebHostBuilder webHost,
            string temporaryDirectory,
            string sourceSpritePath)
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services => ConfigureServices(services, temporaryDirectory, sourceSpritePath));
            webHost.Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        }

        private static void ConfigureServices(
            IServiceCollection services,
            string temporaryDirectory,
            string sourceSpritePath)
        {
            services.AddLogging();
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(TrickplayPreviewController).Assembly);

            services.AddSingleton(CreateUserManager());
            services.AddSingleton(CreateLibraryManager());
            services.AddSingleton(CreateMediaSourceManager());
            services.AddSingleton(CreateTrickplayManager(sourceSpritePath));
            services.AddSingleton(CreateApplicationPaths(temporaryDirectory));

            var registrator = new PluginServiceRegistrator();
            registrator.RegisterServices(services, InterfaceMock.Create<IServerApplicationHost>().Service);
        }

        private static IUserManager CreateUserManager()
        {
            var user = new User("component-user", "test-provider", "test-reset-provider")
            {
                Id = userId,
            };
            user.Permissions.Add(new Permission(PermissionKind.EnableMediaPlayback, true));

            InterfaceMockSpecs<IUserManager> mock = InterfaceMock.Create<IUserManager>();
            mock.Handle("GetUserById", arguments =>
                Equals(arguments?[0], userId) ? user : null);
            return mock.Service;
        }

        private static ILibraryManager CreateLibraryManager()
        {
            var video = new Video
            {
                Id = itemId,
                Name = "Component source video",
            };

            InterfaceMockSpecs<ILibraryManager> mock = InterfaceMock.Create<ILibraryManager>();
            mock.Handle("GetItemById", arguments =>
                Equals(arguments?[0], itemId) ? video : null);
            mock.Handle("GetLibraryOptions", _ => new LibraryOptions { SaveTrickplayWithMedia = false });
            return mock.Service;
        }

        private static IMediaSourceManager CreateMediaSourceManager()
        {
            IReadOnlyList<MediaSourceInfo> mediaSources =
            [
                new MediaSourceInfo { Id = itemId.ToString("D") },
            ];

            InterfaceMockSpecs<IMediaSourceManager> mock = InterfaceMock.Create<IMediaSourceManager>();
            mock.Handle("GetPlaybackMediaSources", _ => Task.FromResult(mediaSources));
            return mock.Service;
        }

        private static ITrickplayManager CreateTrickplayManager(string sourceSpritePath)
        {
            var metadata = new TrickplayInfo
            {
                ItemId = itemId,
                Width = 320,
                Height = 180,
                TileWidth = 2,
                TileHeight = 2,
                ThumbnailCount = 4,
                Interval = 10_000,
            };
            var resolutions = new Dictionary<int, TrickplayInfo> { [320] = metadata };

            InterfaceMockSpecs<ITrickplayManager> mock = InterfaceMock.Create<ITrickplayManager>();
            mock.Handle("GetTrickplayResolutions", arguments =>
                Equals(arguments?[0], itemId)
                    ? Task.FromResult(resolutions)
                    : Task.FromResult(new Dictionary<int, TrickplayInfo>()));
            mock.Handle("GetTrickplayTilePathAsync", arguments =>
                Equals(arguments?[1], 320)
                && Equals(arguments?[2], 0)
                && Equals(arguments?[3], false)
                    ? Task.FromResult(sourceSpritePath)
                    : Task.FromResult(string.Empty));
            return mock.Service;
        }

        private static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
        {
            InterfaceMockSpecs<IApplicationPaths> mock = InterfaceMock.Create<IApplicationPaths>();
            mock.Handle("get_TempDirectory", _ => temporaryDirectory);
            return mock.Service;
        }

        private static string CreateSourceSprite(string temporaryDirectory)
        {
            string sourceSpritePath = Path.Combine(temporaryDirectory, "source-sprite.jpg");
            using var bitmap = new SKBitmap(640, 360, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap);
            DrawCell(canvas, SKColors.Red, 0, 0);
            DrawCell(canvas, SKColors.Green, 320, 0);
            DrawCell(canvas, SKColors.Blue, 0, 180);
            DrawCell(canvas, SKColors.Yellow, 320, 180);
            using FileStream output = File.Create(sourceSpritePath);
            Assert.True(bitmap.Encode(output, SKEncodedImageFormat.Jpeg, quality: 100));
            output.Close();
            File.SetLastWriteTimeUtc(sourceSpritePath, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return sourceSpritePath;
        }

        private static void DrawCell(SKCanvas canvas, SKColor color, int left, int top)
        {
            using var paint = new SKPaint { Color = color };
            canvas.DrawRect(left, top, 320, 180, paint);
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ComponentTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Claim[] claims =
            [
                new Claim("Jellyfin-UserId", userId.ToString("N")),
            ];
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

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

    private static class InterfaceMock
    {
        public static InterfaceMockSpecs<TInterface> Create<TInterface>()
            where TInterface : class
        {
            TInterface service = DispatchProxy.Create<TInterface, InterfaceMockSpecs<TInterface>>();
            return (InterfaceMockSpecs<TInterface>)(object)service;
        }
    }
}
