using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewEncoderConcurrencySpecs : TrickplayPreviewEncoderSharedSpecs
{
    [Fact]
    public async Task CancelsBeforeWaitingForADecodePermit()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => encoder.EncodeAsync(fixture.Source, destination, cancellation.Token));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task CancelsBetweenScanlineBatchesWhileReleasingThePermit()
    {
        var metadata = new TrickplayMetadata(CellWidth, 128, 1_000, 1, 1, 1);
        using SourceFixture fixture = SourceFixture.Create(CreateTallJpeg(128), metadata, 0, 0);
        using var cancellation = new CancellationTokenSource();
        int completedReadBatches = 0;
        using var encoder = new TrickplayPreviewEncoder(
            NullLogger<TrickplayPreviewEncoder>.Instance,
            checkpoint =>
            {
                if (checkpoint == TrickplayPreviewEncoder.PreviewEncodingCheckpoint.AfterReadBatch
                    && ++completedReadBatches == 1)
                {
                    cancellation.Cancel();
                }
            });
        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => encoder.EncodeAsync(fixture.Source, destination, cancellation.Token));

        Assert.Equal(1, completedReadBatches);
        Assert.Equal(0, destination.Length);
        await AssertFullDecodeCapacityAvailableAsync(encoder, fixture.Source);
    }

    [Fact]
    public async Task CancelsBetweenSkipBatchesWhileReleasingThePermit()
    {
        var metadata = new TrickplayMetadata(CellWidth, 64, 1_000, 1, 3, 3);
        using SourceFixture fixture = SourceFixture.Create(CreateTallJpeg(192), metadata, 2, 0);
        using var cancellation = new CancellationTokenSource();
        int completedSkipBatches = 0;
        using var encoder = new TrickplayPreviewEncoder(
            NullLogger<TrickplayPreviewEncoder>.Instance,
            checkpoint =>
            {
                if (checkpoint == TrickplayPreviewEncoder.PreviewEncodingCheckpoint.AfterSkipBatch
                    && ++completedSkipBatches == 1)
                {
                    cancellation.Cancel();
                }
            });
        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => encoder.EncodeAsync(fixture.Source, destination, cancellation.Token));

        Assert.Equal(1, completedSkipBatches);
        Assert.Equal(0, destination.Length);
        await AssertFullDecodeCapacityAvailableAsync(encoder, fixture.Source);
    }

    [Fact]
    public async Task CancelsImmediatelyBeforeEncodeWhileReleasingThePermit()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 1);
        using var cancellation = new CancellationTokenSource();
        bool reachedBeforeEncode = false;
        using var encoder = new TrickplayPreviewEncoder(
            NullLogger<TrickplayPreviewEncoder>.Instance,
            checkpoint =>
            {
                if (checkpoint == TrickplayPreviewEncoder.PreviewEncodingCheckpoint.BeforeEncode)
                {
                    reachedBeforeEncode = true;
                    cancellation.Cancel();
                }
            });
        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => encoder.EncodeAsync(fixture.Source, destination, cancellation.Token));

        Assert.True(reachedBeforeEncode);
        Assert.Equal(0, destination.Length);
        await AssertFullDecodeCapacityAvailableAsync(encoder, fixture.Source);
    }

    [Fact]
    public async Task AWaitingFifthEncodeCanCancel()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var blocked = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        BlockingWriteStream[] destinations = Enumerable.Range(0, 4)
            .Select(_ => new BlockingWriteStream(blocked, release))
            .ToArray();
        Task<PreviewEncodingTelemetry>[] owners = destinations
            .Select(destination => Task.Run(
                () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None)))
            .ToArray();

        try
        {
            Assert.True(blocked.Wait(TimeSpan.FromSeconds(10)));
            using var fifthDestination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            Task<PreviewEncodingTelemetry> fifth = encoder.EncodeAsync(
                fixture.Source,
                fifthDestination,
                cancellation.Token);
            Assert.False(fifth.IsCompleted);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifth);
            Assert.Equal(0, fifthDestination.Length);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(owners).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (BlockingWriteStream destination in destinations)
            {
                destination.Dispose();
            }
        }
    }

    [Fact]
    public async Task ReportsDecodePermitWaitingForAFifthEncode()
    {
        var logger = new DebugProtocolLogger<TrickplayPreviewEncoder>();

        IReadOnlyList<RecordedEvent> events = await RunSaturatedFifthEncodeAsync(
            logger,
            pending => Assert.Single(pending, recorded => recorded.EventId == DecodePermitWaiting));

        Assert.Single(events, recorded => recorded.EventId == DecodePermitWaiting);
        Assert.All(
            events,
            recorded => Assert.Equal(["{OriginalFormat}"], recorded.Properties.Keys.ToArray()));
    }

    [Fact]
    public async Task ReportsNoDecodePermitWaitingWhenTheHostDisablesDebugLogging()
    {
        var logger = new DebugProtocolLogger<TrickplayPreviewEncoder>(LogLevel.Information);

        IReadOnlyList<RecordedEvent> events = await RunSaturatedFifthEncodeAsync(
            logger,
            pending => Assert.Empty(pending));

        Assert.Empty(events);
    }

}
