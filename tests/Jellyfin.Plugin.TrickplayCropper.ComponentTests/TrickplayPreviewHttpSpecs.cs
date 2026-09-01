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
    private static readonly Guid alternateSourceId = Guid.Parse("9fe0dc1f-c780-483e-86c8-fc16267127f6");
    private static readonly Guid itemId = Guid.Parse("3f728b7b-4aa5-4f65-b488-a6029edb6725");
    private static readonly Guid unavailableSourceId = Guid.Parse("59036707-aa98-4b65-8875-d63c9d110906");
    private static readonly Guid unknownUserId = Guid.Parse("68ebfc33-0160-4f11-8faf-b1be419449fe");
    private static readonly Guid userId = Guid.Parse("e07c89e3-a67e-49f5-9cbf-76b980ebe59a");

    [Fact]
    public async Task ServesGeneratedDefaultSourcePreview()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage response = await fixture.GetAsync();

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

    [Fact]
    public async Task ServesAuthorizedAlternateSourcePreview()
    {
        var scenario = new PreviewScenario { UsesAlternateSource = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Guid[] expectedLookups = [itemId, alternateSourceId];
        Assert.Equal(expectedLookups, scenario.LibraryLookupIds);
        Assert.Single(fixture.Cache.Identities);
        Assert.StartsWith(
            string.Concat(alternateSourceId.ToString("N"), Path.DirectorySeparatorChar),
            fixture.Cache.Identities[0].RelativePath,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AuthenticationState.Missing)]
    [InlineData(AuthenticationState.Invalid)]
    [InlineData(AuthenticationState.UnusableUserSession)]
    public async Task RejectsUnusableUserSession(AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.RequestFailureLogCount);
    }

    [Theory]
    [InlineData(ForbiddenCondition.ApiKeyWithoutCurrentUser)]
    [InlineData(ForbiddenCondition.LogicalVideoPlaybackDenied)]
    [InlineData(ForbiddenCondition.SelectedVideoPlaybackDenied)]
    public async Task ForbidsAuthenticatedPlaybackDenial(ForbiddenCondition condition)
    {
        PreviewScenario scenario = CreateForbiddenScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.RequestFailureLogCount);
    }

    [Theory]
    [InlineData(NotFoundCondition.LogicalVideoMissing)]
    [InlineData(NotFoundCondition.LogicalVideoHidden)]
    [InlineData(NotFoundCondition.LogicalItemWrongType)]
    [InlineData(NotFoundCondition.SelectedSourceNotMember)]
    [InlineData(NotFoundCondition.SelectedSourceMembershipMalformed)]
    [InlineData(NotFoundCondition.SelectedVideoMissing)]
    [InlineData(NotFoundCondition.SelectedVideoHidden)]
    [InlineData(NotFoundCondition.SelectedItemWrongType)]
    [InlineData(NotFoundCondition.ExactMetadataMissing)]
    [InlineData(NotFoundCondition.ThumbnailsMissing)]
    [InlineData(NotFoundCondition.ManagerPathMissing)]
    [InlineData(NotFoundCondition.SourceSpriteMissing)]
    public async Task ConcealsUnavailableResource(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.RequestFailureLogCount);
    }

    [Fact]
    public async Task AuthorizesBeforeReadingSharedPreviewCacheEntry()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage authorizedResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        Assert.Equal(1, fixture.Cache.CallCount);
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));

        fixture.SetPlaybackAccess(false);
        using HttpResponseMessage deniedResponse = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal(1, fixture.Cache.CallCount);
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));
        Assert.Equal(0, fixture.RequestFailureLogCount);
    }

    private static PreviewScenario CreateForbiddenScenario(ForbiddenCondition condition)
    {
        return condition switch
        {
            ForbiddenCondition.ApiKeyWithoutCurrentUser => new PreviewScenario
            {
                Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
            },
            ForbiddenCondition.LogicalVideoPlaybackDenied => new PreviewScenario
            {
                DeniesLogicalVideoPlayback = true,
            },
            ForbiddenCondition.SelectedVideoPlaybackDenied => new PreviewScenario
            {
                DeniesSelectedVideoPlayback = true,
                UsesAlternateSource = true,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown forbidden condition."),
        };
    }

    private static PreviewScenario CreateNotFoundScenario(NotFoundCondition condition)
    {
        return condition switch
        {
            NotFoundCondition.LogicalVideoMissing => CreateLogicalAvailabilityScenario(ItemAvailability.Missing),
            NotFoundCondition.LogicalVideoHidden => CreateLogicalAvailabilityScenario(ItemAvailability.Hidden),
            NotFoundCondition.LogicalItemWrongType => CreateLogicalAvailabilityScenario(ItemAvailability.WrongType),
            NotFoundCondition.SelectedSourceNotMember => new PreviewScenario
            {
                Membership = SourceMembership.NotMember,
                UsesAlternateSource = true,
            },
            NotFoundCondition.SelectedSourceMembershipMalformed => new PreviewScenario
            {
                Membership = SourceMembership.Malformed,
                UsesAlternateSource = true,
            },
            NotFoundCondition.SelectedVideoMissing => CreateSelectedAvailabilityScenario(ItemAvailability.Missing),
            NotFoundCondition.SelectedVideoHidden => CreateSelectedAvailabilityScenario(ItemAvailability.Hidden),
            NotFoundCondition.SelectedItemWrongType => CreateSelectedAvailabilityScenario(ItemAvailability.WrongType),
            NotFoundCondition.ExactMetadataMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.ExactWidthMissing,
            },
            NotFoundCondition.ThumbnailsMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.NoThumbnails,
            },
            NotFoundCondition.ManagerPathMissing => new PreviewScenario
            {
                SourceSprite = SourceSpriteAvailability.ManagerPathMissing,
            },
            NotFoundCondition.SourceSpriteMissing => new PreviewScenario
            {
                SourceSprite = SourceSpriteAvailability.FileMissing,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown not-found condition."),
        };
    }

    private static PreviewScenario CreateLogicalAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario { LogicalVideo = availability };
    }

    private static PreviewScenario CreateSelectedAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario
        {
            SelectedVideo = availability,
            UsesAlternateSource = true,
        };
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

        public RecordingPreviewCache Cache => Services.GetRequiredService<RecordingPreviewCache>();

        public string CacheRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            "preview-v1");

        public HttpClient Client { get; }

        public int RequestFailureLogCount =>
            Services.GetRequiredService<RecordingLogger<TrickplayPreview>>().RequestFailureCount;

        public IServiceProvider Services => host.Services;

        public static Task<PreviewHostFixture> CreateAsync()
        {
            return CreateAsync(new PreviewScenario());
        }

        public static async Task<PreviewHostFixture> CreateAsync(PreviewScenario scenario)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            string sourceSpritePath = CreateSourceSprite(temporaryDirectory);
            var context = new PreviewHostContext(temporaryDirectory, sourceSpritePath, scenario);

            var hostBuilder = new HostBuilder();
            hostBuilder.ConfigureWebHost(webHost => ConfigureWebHost(webHost, context));
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

        public Task<HttpResponseMessage> GetAsync()
        {
            PreviewScenario scenario = Services.GetRequiredService<PreviewScenario>();
            string mediaSourceQuery = scenario.UsesAlternateSource
                ? $"MediaSourceId={alternateSourceId:D}&"
                : string.Empty;
            string requestPath = $"/TrickplayCropper/Videos/{itemId:D}/Preview?{mediaSourceQuery}PositionTicks=0";
            return Client.GetAsync(requestPath, CancellationToken.None);
        }

        public void SetPlaybackAccess(bool hasPlaybackAccess)
        {
            SetPlaybackPermission(Services.GetRequiredService<User>(), hasPlaybackAccess);
        }

        private static void ConfigureWebHost(IWebHostBuilder webHost, PreviewHostContext context)
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services => ConfigureServices(services, context));
            webHost.Configure(application =>
            {
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
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(TrickplayPreviewController).Assembly);

            User user = CreateUser(context.Scenario);
            IApplicationPaths applicationPaths = CreateApplicationPaths(context.TemporaryDirectory);
            services.AddSingleton(user);
            services.AddSingleton(CreateUserManager(user));
            services.AddSingleton(CreateLibraryManager(context.Scenario, user));
            services.AddSingleton(CreateMediaSourceManager(context.Scenario, user));
            services.AddSingleton(CreateTrickplayManager(context));
            services.AddSingleton(applicationPaths);

            var registrator = new PluginServiceRegistrator();
            registrator.RegisterServices(services, InterfaceMock.Create<IServerApplicationHost>().Service);

            var recordingLogger = new RecordingLogger<TrickplayPreview>();
            services.AddSingleton(recordingLogger);
            services.AddSingleton<ILogger<TrickplayPreview>>(recordingLogger);
            var cache = new RecordingPreviewCache(new DiskPreviewCache(applicationPaths, TimeProvider.System));
            services.AddSingleton(cache);
            services.AddSingleton<IPreviewCache>(cache);
        }

        private static User CreateUser(PreviewScenario scenario)
        {
            var user = new User("component-user", "test-provider", "test-reset-provider")
            {
                Id = userId,
            };
            SetPlaybackPermission(user, !scenario.DeniesLogicalVideoPlayback);
            return user;
        }

        private static IUserManager CreateUserManager(User user)
        {
            InterfaceMockSpecs<IUserManager> mock = InterfaceMock.Create<IUserManager>();
            mock.Handle("GetUserById", arguments =>
                Equals(arguments?[0], user.Id) ? user : null);
            return mock.Service;
        }

        private static ILibraryManager CreateLibraryManager(PreviewScenario scenario, User user)
        {
            var logicalVideo = new Video
            {
                Id = itemId,
                Name = "Component logical video",
            };
            var selectedVideo = new Video
            {
                Id = scenario.SelectedSourceId,
                Name = "Component selected source video",
            };

            InterfaceMockSpecs<ILibraryManager> mock = InterfaceMock.Create<ILibraryManager>();
            mock.Handle("GetItemById", arguments => ResolveVideoLookup(
                arguments,
                scenario,
                user,
                logicalVideo,
                selectedVideo));
            mock.Handle("GetLibraryOptions", arguments =>
                ReferenceEquals(arguments?[0], selectedVideo)
                    ? new LibraryOptions { SaveTrickplayWithMedia = false }
                    : throw new InvalidOperationException("Library options were requested for the wrong video."));
            return mock.Service;
        }

        private static Video? ResolveVideoLookup(
            object?[]? arguments,
            PreviewScenario scenario,
            User user,
            Video logicalVideo,
            Video selectedVideo)
        {
            if (arguments?.Length != 2 || !ReferenceEquals(arguments[1], user) || arguments[0] is not Guid requestedId)
            {
                throw new InvalidOperationException("The video lookup did not use the current user-scoped overload.");
            }

            scenario.LibraryLookupIds.Add(requestedId);
            if (scenario.LibraryLookupIds.Count == 1)
            {
                return scenario.LogicalVideo == ItemAvailability.Available ? logicalVideo : null;
            }

            if (requestedId != scenario.SelectedSourceId)
            {
                return null;
            }

            if (scenario.DeniesSelectedVideoPlayback)
            {
                SetPlaybackPermission(user, false);
            }

            return scenario.SelectedVideo == ItemAvailability.Available ? selectedVideo : null;
        }

        private static IMediaSourceManager CreateMediaSourceManager(PreviewScenario scenario, User user)
        {
            InterfaceMockSpecs<IMediaSourceManager> mock = InterfaceMock.Create<IMediaSourceManager>();
            mock.Handle("GetPlaybackMediaSources", arguments =>
            {
                if (arguments?.Length != 5
                    || arguments[0] is not Video video
                    || video.Id != itemId
                    || !ReferenceEquals(arguments[1], user))
                {
                    throw new InvalidOperationException("Playback sources were not enumerated for the current user.");
                }

                string memberId = scenario.Membership switch
                {
                    SourceMembership.Member => scenario.SelectedSourceId.ToString("D"),
                    SourceMembership.NotMember => unavailableSourceId.ToString("D"),
                    SourceMembership.Malformed => "not-a-guid",
                    _ => throw new InvalidOperationException("Unknown source-membership scenario."),
                };
                IReadOnlyList<MediaSourceInfo> mediaSources = [new MediaSourceInfo { Id = memberId }];
                return Task.FromResult(mediaSources);
            });
            return mock.Service;
        }

        private static ITrickplayManager CreateTrickplayManager(PreviewHostContext context)
        {
            TrickplayInfo metadata = CreateMetadata(context.Scenario);
            Dictionary<int, TrickplayInfo> resolutions = context.Scenario.Metadata == MetadataAvailability.ExactWidthMissing
                ? new Dictionary<int, TrickplayInfo> { [640] = metadata }
                : new Dictionary<int, TrickplayInfo> { [320] = metadata };

            InterfaceMockSpecs<ITrickplayManager> mock = InterfaceMock.Create<ITrickplayManager>();
            mock.Handle("GetTrickplayResolutions", arguments =>
                Equals(arguments?[0], context.Scenario.SelectedSourceId)
                    ? Task.FromResult(resolutions)
                    : Task.FromResult(new Dictionary<int, TrickplayInfo>()));
            mock.Handle("GetTrickplayTilePathAsync", arguments => ResolveSourceSpritePath(arguments, context));
            return mock.Service;
        }

        private static TrickplayInfo CreateMetadata(PreviewScenario scenario)
        {
            return new TrickplayInfo
            {
                ItemId = scenario.SelectedSourceId,
                Width = scenario.Metadata == MetadataAvailability.ExactWidthMissing ? 640 : 320,
                Height = 180,
                TileWidth = 2,
                TileHeight = 2,
                ThumbnailCount = scenario.Metadata == MetadataAvailability.NoThumbnails ? 0 : 4,
                Interval = 10_000,
            };
        }

        private static Task<string> ResolveSourceSpritePath(object?[]? arguments, PreviewHostContext context)
        {
            if (!Equals(arguments?[1], 320)
                || !Equals(arguments?[2], 0)
                || !Equals(arguments?[3], false))
            {
                throw new InvalidOperationException("The Source Sprite path request was not normalized.");
            }

            string path = context.Scenario.SourceSprite switch
            {
                SourceSpriteAvailability.Available => context.SourceSpritePath,
                SourceSpriteAvailability.ManagerPathMissing => string.Empty,
                SourceSpriteAvailability.FileMissing => Path.Combine(
                    context.TemporaryDirectory,
                    "missing-source-sprite.jpg"),
                _ => throw new InvalidOperationException("Unknown Source Sprite scenario."),
            };
            return Task.FromResult(path);
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

        private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
        private const string UserIdClaim = "Jellyfin-UserId";

        private readonly PreviewScenario scenario;

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            PreviewScenario scenario)
            : base(options, logger, encoder)
        {
            this.scenario = scenario;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            AuthenticateResult result = scenario.Authentication switch
            {
                AuthenticationState.UserSession => CreateAuthenticatedResult(userId, false),
                AuthenticationState.ApiKeyWithoutCurrentUser => CreateAuthenticatedResult(Guid.Empty, true),
                AuthenticationState.Missing => AuthenticateResult.NoResult(),
                AuthenticationState.Invalid => AuthenticateResult.Fail("The component-test session is invalid."),
                AuthenticationState.UnusableUserSession => CreateAuthenticatedResult(unknownUserId, false),
                _ => throw new InvalidOperationException("Unknown authentication scenario."),
            };
            return Task.FromResult(result);
        }

        private static AuthenticateResult CreateAuthenticatedResult(Guid authenticatedUserId, bool isApiKey)
        {
            Claim[] claims =
            [
                new Claim(UserIdClaim, authenticatedUserId.ToString("N")),
                new Claim(IsApiKeyClaim, isApiKey.ToString()),
            ];
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return AuthenticateResult.Success(ticket);
        }
    }

    private sealed class RecordingPreviewCache : IPreviewCache
    {
        private readonly List<PreviewIdentity> identities = [];
        private readonly IPreviewCache inner;
        private int callCount;

        public RecordingPreviewCache(IPreviewCache inner)
        {
            this.inner = inner;
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

            return await inner.GetOrCreateAsync(identity, writer, cancellationToken).ConfigureAwait(false);
        }

        Task IPreviewCache.ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return inner.ClearAsync(progress, cancellationToken);
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private int requestFailureCount;

        public int RequestFailureCount => Volatile.Read(ref requestFailureCount);

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
            if (eventId.Id == 1000)
            {
                Interlocked.Increment(ref requestFailureCount);
            }
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

    private sealed record PreviewHostContext(
        string TemporaryDirectory,
        string SourceSpritePath,
        PreviewScenario Scenario);

    private sealed class PreviewScenario
    {
        public AuthenticationState Authentication { get; init; } = AuthenticationState.UserSession;

        public bool DeniesLogicalVideoPlayback { get; init; }

        public bool DeniesSelectedVideoPlayback { get; init; }

        public List<Guid> LibraryLookupIds { get; } = [];

        public ItemAvailability LogicalVideo { get; init; } = ItemAvailability.Available;

        public SourceMembership Membership { get; init; } = SourceMembership.Member;

        public MetadataAvailability Metadata { get; init; } = MetadataAvailability.Available;

        public ItemAvailability SelectedVideo { get; init; } = ItemAvailability.Available;

        public Guid SelectedSourceId => UsesAlternateSource ? alternateSourceId : itemId;

        public SourceSpriteAvailability SourceSprite { get; init; } = SourceSpriteAvailability.Available;

        public bool UsesAlternateSource { get; init; }
    }

    public enum AuthenticationState
    {
        UserSession,
        ApiKeyWithoutCurrentUser,
        Missing,
        Invalid,
        UnusableUserSession,
    }

    public enum ForbiddenCondition
    {
        ApiKeyWithoutCurrentUser,
        LogicalVideoPlaybackDenied,
        SelectedVideoPlaybackDenied,
    }

    private enum ItemAvailability
    {
        Available,
        Missing,
        Hidden,
        WrongType,
    }

    private enum MetadataAvailability
    {
        Available,
        ExactWidthMissing,
        NoThumbnails,
    }

    public enum NotFoundCondition
    {
        LogicalVideoMissing,
        LogicalVideoHidden,
        LogicalItemWrongType,
        SelectedSourceNotMember,
        SelectedSourceMembershipMalformed,
        SelectedVideoMissing,
        SelectedVideoHidden,
        SelectedItemWrongType,
        ExactMetadataMissing,
        ThumbnailsMissing,
        ManagerPathMissing,
        SourceSpriteMissing,
    }

    private enum SourceMembership
    {
        Member,
        NotMember,
        Malformed,
    }

    private enum SourceSpriteAvailability
    {
        Available,
        ManagerPathMissing,
        FileMissing,
    }

    private static void SetPlaybackPermission(User user, bool hasPlaybackAccess)
    {
        user.Permissions.Clear();
        user.Permissions.Add(new Permission(PermissionKind.EnableMediaPlayback, hasPlaybackAccess));
    }
}
