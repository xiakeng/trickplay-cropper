using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class TrickplayFrameProbeSpecs
{
    private static readonly Guid itemId = Guid.Parse("5dd8f9cc-b38d-45fb-9a97-0358b5ef25e2");

    [Fact]
    public void ProductionOutcomeTypesMatchTheClosedContract()
    {
        Type outcomeType = typeof(FrameProbeOutcome);
        string[] outcomeNames = outcomeType.Assembly
            .GetTypes()
            .Where(type => type != outcomeType && outcomeType.IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(FrameProbeOutcome.BadRequest),
                nameof(FrameProbeOutcome.Forbidden),
                nameof(FrameProbeOutcome.InternalError),
                nameof(FrameProbeOutcome.NotFound),
                nameof(FrameProbeOutcome.Success),
                nameof(FrameProbeOutcome.Unauthorized),
            ],
            outcomeNames);
    }

    [Fact]
    public async Task AnswersSuccessWithTheClampedFrameIndexFromTheSharedContext()
    {
        var metadata = new TrickplayMetadata(320, 180, 10_000, 2, 2, 4);
        long positionTicks = 100_000L * TimeSpan.TicksPerMillisecond;
        var query = new PreviewQuery(itemId, null, positionTicks);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("ProbeUnitTest"));
        using var cancellation = new CancellationTokenSource();
        var contextResolver = new StubContextResolver(new PreviewContextResolution.Resolved(
            new PreviewContext(itemId, new Video { Id = itemId }, metadata, metadata.SelectFrameIndex(positionTicks))));
        TrickplayFrameProbe probe = new(contextResolver);

        FrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            query,
            principal,
            cancellation.Token);

        FrameProbeOutcome.Success success = Assert.IsType<FrameProbeOutcome.Success>(outcome);
        Assert.Equal(3, success.FrameIndex);
        Assert.Equal(1, contextResolver.CallCount);
        Assert.Equal(query, contextResolver.Query);
        Assert.Same(principal, contextResolver.Principal);
        Assert.Equal(cancellation.Token, contextResolver.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(ExpectedContextOutcomes))]
    public async Task MapsSharedContextFailureToTypedProbeOutcome(
        ContextFailureKind failureKind,
        Type expectedOutcomeType)
    {
        var contextResolver = new StubContextResolver(CreateContextResolution(failureKind));
        TrickplayFrameProbe probe = new(contextResolver);

        FrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            CancellationToken.None);

        Assert.IsType(expectedOutcomeType, outcome);
        Assert.Equal(1, contextResolver.CallCount);
    }

    [Theory]
    [InlineData(SharedContextFailureKind.InvalidMetadata)]
    [InlineData(SharedContextFailureKind.InvalidConfiguration)]
    [InlineData(SharedContextFailureKind.Unexpected)]
    public async Task ConvertsSharedContextFailureKindToInternalError(SharedContextFailureKind kind)
    {
        var contextResolver = new ThrowingContextResolver(CreateFailureException(kind));
        TrickplayFrameProbe probe = new(contextResolver);

        FrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            CancellationToken.None);

        Assert.IsType<FrameProbeOutcome.InternalError>(outcome);
    }

    [Fact]
    public async Task PropagatesRequestCancellationWithoutConvertingItToAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contextResolver = new CancellingContextResolver();
        TrickplayFrameProbe probe = new(contextResolver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ((ITrickplayFrameProbe)probe).ProbeAsync(
                new PreviewQuery(itemId, null, 0),
                new ClaimsPrincipal(),
                cancellation.Token));
    }

    public static TheoryData<ContextFailureKind, Type> ExpectedContextOutcomes => new()
    {
        { ContextFailureKind.BadRequest, typeof(FrameProbeOutcome.BadRequest) },
        { ContextFailureKind.Unauthorized, typeof(FrameProbeOutcome.Unauthorized) },
        { ContextFailureKind.Forbidden, typeof(FrameProbeOutcome.Forbidden) },
        { ContextFailureKind.NotFound, typeof(FrameProbeOutcome.NotFound) },
    };

    private static PreviewContextResolution CreateContextResolution(ContextFailureKind failureKind)
    {
        return failureKind switch
        {
            ContextFailureKind.BadRequest => new PreviewContextResolution.BadRequest(),
            ContextFailureKind.Unauthorized => new PreviewContextResolution.Unauthorized(),
            ContextFailureKind.Forbidden => new PreviewContextResolution.Forbidden(),
            ContextFailureKind.NotFound => new PreviewContextResolution.NotFound(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                failureKind,
                "Unknown shared context failure kind."),
        };
    }

    private static Exception CreateFailureException(SharedContextFailureKind kind)
    {
        return kind switch
        {
            SharedContextFailureKind.InvalidMetadata => new InvalidTrickplayMetadataException(
                new TrickplayMetadata(640, 180, 10_000, 2, 2, 4),
                "FrameWidthMatchesResolutionKey",
                640),
            SharedContextFailureKind.InvalidConfiguration => new InvalidTrickplayConfigurationException(
                "ConfiguredTargetPositive",
                0),
            SharedContextFailureKind.Unexpected => new IOException("The unit-test shared context failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown failure exception kind."),
        };
    }

    private sealed class StubContextResolver : IPreviewContextResolver
    {
        private readonly PreviewContextResolution resolution;
        private int callCount;

        public StubContextResolver(PreviewContextResolution resolution)
        {
            this.resolution = resolution;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public CancellationToken CancellationToken { get; private set; }

        public ClaimsPrincipal? Principal { get; private set; }

        public PreviewQuery? Query { get; private set; }

        public Task<PreviewContextResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Query = query;
            Principal = principal;
            CancellationToken = cancellationToken;
            return Task.FromResult(resolution);
        }
    }

    private sealed class ThrowingContextResolver : IPreviewContextResolver
    {
        private readonly Exception failure;

        public ThrowingContextResolver(Exception failure)
        {
            this.failure = failure;
        }

        public Task<PreviewContextResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            return Task.FromException<PreviewContextResolution>(failure);
        }
    }

    private sealed class CancellingContextResolver : IPreviewContextResolver
    {
        public Task<PreviewContextResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            return Task.FromCanceled<PreviewContextResolution>(cancellationToken);
        }
    }

    public enum ContextFailureKind
    {
        BadRequest,
        Unauthorized,
        Forbidden,
        NotFound,
    }

    public enum SharedContextFailureKind
    {
        InvalidMetadata,
        InvalidConfiguration,
        Unexpected,
    }
}
