using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class TrickplayFrameProbeSpecs
{
    private static readonly Guid itemId = Guid.Parse("5dd8f9cc-b38d-45fb-9a97-0358b5ef25e2");

    [Fact]
    public void ProductionOutcomeTypesMatchTheClosedContract()
    {
        Type outcomeType = typeof(TrickplayFrameProbeOutcome);
        string[] outcomeNames = outcomeType.Assembly
            .GetTypes()
            .Where(type => type != outcomeType && outcomeType.IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(TrickplayFrameProbeOutcome.BadRequest),
                nameof(TrickplayFrameProbeOutcome.Forbidden),
                nameof(TrickplayFrameProbeOutcome.InternalError),
                nameof(TrickplayFrameProbeOutcome.NotFound),
                nameof(TrickplayFrameProbeOutcome.Success),
                nameof(TrickplayFrameProbeOutcome.Unauthorized),
            ],
            outcomeNames);
    }

    [Fact]
    public async Task AnswersSuccessWithTheSelectedFrameIndex()
    {
        var metadata = new TrickplayMetadata(320, 180, 10_000, 2, 2, 4);
        var query = new PreviewQuery(itemId, null, 100_000L * TimeSpan.TicksPerMillisecond);
        using var cancellation = new CancellationTokenSource();
        var contextResolver = new StubContextResolver(
            new TrickplayFrameCalculationResolution.Selected(metadata, 3));
        TrickplayFrameProbe probe = new(contextResolver, NullLogger<TrickplayFrameProbe>.Instance);

        TrickplayFrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            query,
            cancellation.Token);

        TrickplayFrameProbeOutcome.Success success = Assert.IsType<TrickplayFrameProbeOutcome.Success>(outcome);
        Assert.Equal(3, success.FrameIndex);
        Assert.Equal(1, contextResolver.CallCount);
        Assert.Equal(query, contextResolver.Query);
        Assert.Equal(cancellation.Token, contextResolver.CancellationToken);
    }

    [Fact]
    public async Task RejectsNegativePositionWithoutResolvingSourceFacts()
    {
        var contextResolver = new StubContextResolver(
            new TrickplayFrameCalculationResolution.NotFound(PreviewUnavailableReason.Concealed));
        TrickplayFrameProbe probe = new(contextResolver, NullLogger<TrickplayFrameProbe>.Instance);

        TrickplayFrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, -1),
            CancellationToken.None);

        Assert.IsType<TrickplayFrameProbeOutcome.BadRequest>(outcome);
        Assert.Equal(0, contextResolver.CallCount);
    }

    [Theory]
    [InlineData(SharedCalculationFailureKind.InvalidMetadata)]
    [InlineData(SharedCalculationFailureKind.InvalidConfiguration)]
    [InlineData(SharedCalculationFailureKind.Unexpected)]
    public async Task ConvertsSharedCalculationFailureToInternalError(SharedCalculationFailureKind kind)
    {
        var contextResolver = new ThrowingContextResolver(CreateFailureException(kind));
        TrickplayFrameProbe probe = new(contextResolver, NullLogger<TrickplayFrameProbe>.Instance);

        TrickplayFrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, 0),
            CancellationToken.None);

        Assert.IsType<TrickplayFrameProbeOutcome.InternalError>(outcome);
    }

    [Fact]
    public async Task PropagatesRequestCancellationWithoutConvertingItToAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contextResolver = new CancellingContextResolver();
        TrickplayFrameProbe probe = new(contextResolver, NullLogger<TrickplayFrameProbe>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ((ITrickplayFrameProbe)probe).ProbeAsync(
                new PreviewQuery(itemId, null, 0),
                cancellation.Token));
    }

    [Theory]
    [InlineData("NoConfiguredTarget")]
    [InlineData("NoGeneratedMetadata")]
    [InlineData("SelectedResolutionMissing")]
    [InlineData("NoThumbnails")]
    public async Task RecordsTheStableDebugReasonForAnExpectedUnavailableOutcome(string reason)
    {
        PreviewUnavailableReason expected = Enum.Parse<PreviewUnavailableReason>(reason);
        var contextResolver = new StubContextResolver(
            new TrickplayFrameCalculationResolution.NotFound(expected));
        var logger = new RecordingLogger<TrickplayFrameProbe>();
        TrickplayFrameProbe probe = new(contextResolver, logger);

        TrickplayFrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, 0),
            CancellationToken.None);

        Assert.IsType<TrickplayFrameProbeOutcome.NotFound>(outcome);
        RecordingLogger<TrickplayFrameProbe>.RecordedLog log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, log.Level);
        Assert.Equal(1001, log.EventId.Id);
        Assert.Equal("TrickplayPreviewUnavailable", log.EventId.Name);
        Assert.Equal(expected, Assert.IsType<PreviewUnavailableReason>(log.Properties["Reason"]));
    }

    [Fact]
    public async Task RecordsNoPluginLogForAConcealedUnavailableOutcome()
    {
        var contextResolver = new StubContextResolver(
            new TrickplayFrameCalculationResolution.NotFound(PreviewUnavailableReason.Concealed));
        var logger = new RecordingLogger<TrickplayFrameProbe>();
        TrickplayFrameProbe probe = new(contextResolver, logger);

        TrickplayFrameProbeOutcome outcome = await ((ITrickplayFrameProbe)probe).ProbeAsync(
            new PreviewQuery(itemId, null, 0),
            CancellationToken.None);

        Assert.IsType<TrickplayFrameProbeOutcome.NotFound>(outcome);
        Assert.Empty(logger.Entries);
    }

    private static Exception CreateFailureException(SharedCalculationFailureKind kind)
    {
        return kind switch
        {
            SharedCalculationFailureKind.InvalidMetadata => new InvalidTrickplayMetadataException(
                new TrickplayMetadata(640, 180, 10_000, 2, 2, 4),
                "FrameWidthMatchesResolutionKey",
                640),
            SharedCalculationFailureKind.InvalidConfiguration => new InvalidTrickplayConfigurationException(
                "ConfiguredTargetPositive",
                0),
            SharedCalculationFailureKind.Unexpected => new IOException("The unit-test calculation failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown failure exception kind."),
        };
    }

    private sealed class StubContextResolver : ITrickplayFrameProbeContextResolver
    {
        private readonly TrickplayFrameCalculationResolution resolution;
        private int callCount;

        public StubContextResolver(TrickplayFrameCalculationResolution resolution)
        {
            this.resolution = resolution;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public CancellationToken CancellationToken { get; private set; }

        public PreviewQuery? Query { get; private set; }

        public Task<TrickplayFrameCalculationResolution> ResolveAsync(
            PreviewQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Query = query;
            CancellationToken = cancellationToken;
            return Task.FromResult(resolution);
        }
    }

    private sealed class ThrowingContextResolver : ITrickplayFrameProbeContextResolver
    {
        private readonly Exception failure;

        public ThrowingContextResolver(Exception failure)
        {
            this.failure = failure;
        }

        public Task<TrickplayFrameCalculationResolution> ResolveAsync(
            PreviewQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromException<TrickplayFrameCalculationResolution>(failure);
        }
    }

    private sealed class CancellingContextResolver : ITrickplayFrameProbeContextResolver
    {
        public Task<TrickplayFrameCalculationResolution> ResolveAsync(
            PreviewQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromCanceled<TrickplayFrameCalculationResolution>(cancellationToken);
        }
    }

    public enum SharedCalculationFailureKind
    {
        InvalidMetadata,
        InvalidConfiguration,
        Unexpected,
    }
}
