using System.Reflection;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PreviewObservationSpecs
{
    [Fact]
    public async Task PreviewRefreshesTheFrameIndexAfterAProbeUsesAnOlderSnapshot()
    {
        var fixture = new ObservationFixture();

        TrickplayFrameCalculationResolution initial = await fixture.Probe.ResolveAsync(fixture.Query, CancellationToken.None);
        Assert.Equal(3, Assert.IsType<TrickplayFrameCalculationResolution.Selected>(initial).FrameIndex);

        fixture.IntervalMilliseconds = 20_000;
        TrickplayFrameCalculationResolution older = await fixture.Probe.ResolveAsync(fixture.Query, CancellationToken.None);
        Assert.Equal(3, Assert.IsType<TrickplayFrameCalculationResolution.Selected>(older).FrameIndex);
        Assert.Equal(1, fixture.MetadataReads);

        PreviewContextResolution current = await fixture.Preview.ResolveAsync(
            fixture.Query, fixture.Principal, CancellationToken.None);
        Assert.Equal(1, Assert.IsType<PreviewContextResolution.Resolved>(current).Context.FrameIndex);
        Assert.Equal(2, fixture.MetadataReads);

        TrickplayFrameCalculationResolution refreshed = await fixture.Probe.ResolveAsync(fixture.Query, CancellationToken.None);
        Assert.Equal(1, Assert.IsType<TrickplayFrameCalculationResolution.Selected>(refreshed).FrameIndex);
        Assert.Equal(2, fixture.MetadataReads);
    }

    [Theory]
    [InlineData(HiddenVideo.Logical)]
    [InlineData(HiddenVideo.Source)]
    public async Task ConcealsGeneratedVideoFromPreviewDespiteASuccessfulProbe(HiddenVideo hiddenVideo)
    {
        var fixture = new ObservationFixture { Hidden = hiddenVideo };

        TrickplayFrameCalculationResolution available = await fixture.Probe.ResolveAsync(fixture.Query, CancellationToken.None);
        Assert.Equal(3, Assert.IsType<TrickplayFrameCalculationResolution.Selected>(available).FrameIndex);
        Assert.Equal(1, fixture.MetadataReads);

        PreviewContextResolution denied = await fixture.Preview.ResolveAsync(
            fixture.Query, fixture.Principal, CancellationToken.None);
        Assert.Equal(PreviewUnavailableReason.Concealed, Assert.IsType<PreviewContextResolution.NotFound>(denied).Reason);
        Assert.Equal(1, fixture.MetadataReads);
    }

    private sealed class ObservationFixture
    {
        private readonly Video logical = new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        private readonly Video source = new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        private readonly User user = new("unit-user", "test-provider", "test-reset-provider")
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        };

        public ObservationFixture()
        {
            user.Permissions.Add(new Permission(PermissionKind.EnableMediaPlayback, true));
            Query = new PreviewQuery(logical.Id, source.Id, 30_000L * TimeSpan.TicksPerMillisecond);
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JellyfinPreviewContextResolver.JellyfinUserIdClaim, user.Id.ToString("N"))],
                "unit-authentication"));
            var configuration = new ServerConfiguration
            {
                TrickplayOptions = new TrickplayOptions { WidthResolutions = [320] },
            };
            IServerConfigurationManager settings = CreateHostDouble<IServerConfigurationManager>((method, _) =>
                method == "get_Configuration" ? configuration : throw new InvalidOperationException(method));
            ITrickplayManager metadata = CreateHostDouble<ITrickplayManager>((method, arguments) =>
            {
                Assert.Equal("GetTrickplayResolutions", method);
                Assert.Equal(source.Id, arguments[0]);
                MetadataReads++;
                return Task.FromResult(new Dictionary<int, TrickplayInfo>
                {
                    [320] = new()
                    {
                        ItemId = source.Id,
                        Width = 320,
                        Height = 180,
                        TileWidth = 2,
                        TileHeight = 2,
                        ThumbnailCount = 4,
                        Interval = IntervalMilliseconds,
                    },
                });
            });
            ILibraryManager library = CreateHostDouble<ILibraryManager>(ResolveVideo);
            IMediaSourceManager sources = CreateHostDouble<IMediaSourceManager>((method, arguments) =>
            {
                Assert.Equal("GetPlaybackMediaSources", method);
                Assert.Same(logical, arguments[0]);
                return Task.FromResult<IReadOnlyList<MediaSourceInfo>>([new MediaSourceInfo
                {
                    Id = source.Id.ToString("N"),
                    MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Width = 320 }],
                }]);
            });
            IUserManager users = CreateHostDouble<IUserManager>((method, arguments) =>
            {
                Assert.Equal("GetUserById", method);
                Assert.Equal(user.Id, arguments[0]);
                return user;
            });
            var clock = new FixedTimeProvider();
            var sourceFacts = new TrickplaySourceFactsCache(library, sources, clock);
            ITrickplayFrameCalculationResolver calculation = new JellyfinTrickplayFrameCalculationResolver(
                new TrickplayMetadataCache(metadata, clock), settings);
            Probe = new JellyfinTrickplayFrameProbeContextResolver(sourceFacts, calculation);
            Preview = new JellyfinPreviewContextResolver(users, library, sources, calculation, sourceFacts);
        }

        public HiddenVideo Hidden { get; init; }

        public int IntervalMilliseconds { get; set; } = 10_000;

        public int MetadataReads { get; private set; }

        public PreviewQuery Query { get; }

        public ClaimsPrincipal Principal { get; }

        public JellyfinTrickplayFrameProbeContextResolver Probe { get; }

        public JellyfinPreviewContextResolver Preview { get; }

        private object? ResolveVideo(string method, object?[] arguments)
        {
            Assert.Equal("GetItemById", method);
            var id = Assert.IsType<Guid>(arguments[0]);
            Video video = id == logical.Id ? logical : source;
            Assert.Equal(video.Id, id);
            bool hidden = (Hidden == HiddenVideo.Logical && id == logical.Id)
                || (Hidden == HiddenVideo.Source && id == source.Id);
            return hidden && arguments.Length > 1 && ReferenceEquals(arguments[1], user) ? null : video;
        }
    }

    private static T CreateHostDouble<T>(Func<string, object?[], object?> handler)
        where T : class
    {
        T service = DispatchProxy.Create<T, HostDoubleSpecs>();
        ((HostDoubleSpecs)(object)service).Handler = handler;
        return service;
    }

    public class HostDoubleSpecs : DispatchProxy
    {
        public Func<string, object?[], object?> Handler { get; set; } =
            (_, _) => throw new InvalidOperationException("No host behavior configured.");

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod!.Name, args ?? []);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    public enum HiddenVideo
    {
        None,
        Logical,
        Source,
    }
}
