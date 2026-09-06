using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewEncoderCropSpecs : TrickplayPreviewEncoderSharedSpecs
{
    [Theory]
    [MemberData(nameof(ValidCrops))]
    public async Task CropsIndependentJpegFixtures(JpegFixture fixture, int row, int column)
    {
        using SourceFixture sourceFixture = SourceFixture.Create(GetFixture(fixture), row, column);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewEncodingTelemetry telemetry = await encoder.EncodeAsync(
            sourceFixture.Source,
            destination,
            CancellationToken.None);

        Assert.True(destination.CanWrite);
        Assert.True(telemetry.Decode >= TimeSpan.Zero);
        Assert.True(telemetry.Encode >= TimeSpan.Zero);
        using SKBitmap source = SKBitmap.Decode(sourceFixture.Source.SourceSpritePath);
        using SKBitmap preview = SKBitmap.Decode(destination.ToArray());
        Assert.Equal(CellWidth, preview.Width);
        Assert.Equal(CellHeight, preview.Height);
        AssertPixelsAgree(source, preview, row, column);
    }

    [Fact]
    public async Task RejectsNonJpegInput()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(NonJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal("SourceSpriteIsJpeg", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RejectsMetadataDimensionMismatch()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        ResolvedPreviewSource mismatched = fixture.Source with
        {
            Metadata = fixture.Source.Metadata with { TileWidth = 2 },
        };
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(mismatched, destination, CancellationToken.None));

        Assert.Equal("SourceSpriteDimensionsMatchMetadata", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RejectsOutOfBoundsCrop()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        FrameSelection selection = fixture.Source.Selection with { CropX = 80 };
        ResolvedPreviewSource outOfBounds = fixture.Source with { Selection = selection };
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(outOfBounds, destination, CancellationToken.None));

        Assert.Equal("CropInsideSourceSprite", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData(InvalidCropInput.NegativeX)]
    [InlineData(InvalidCropInput.NegativeY)]
    [InlineData(InvalidCropInput.NegativeWidth)]
    [InlineData(InvalidCropInput.NegativeHeight)]
    [InlineData(InvalidCropInput.ZeroWidth)]
    [InlineData(InvalidCropInput.ZeroHeight)]
    public async Task RejectsEveryNonPositiveCropInput(InvalidCropInput input)
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        FrameSelection selection = input switch
        {
            InvalidCropInput.NegativeX => fixture.Source.Selection with { CropX = -1 },
            InvalidCropInput.NegativeY => fixture.Source.Selection with { CropY = -1 },
            InvalidCropInput.NegativeWidth => fixture.Source.Selection with { CropWidth = -1 },
            InvalidCropInput.NegativeHeight => fixture.Source.Selection with { CropHeight = -1 },
            InvalidCropInput.ZeroWidth => fixture.Source.Selection with { CropWidth = 0 },
            InvalidCropInput.ZeroHeight => fixture.Source.Selection with { CropHeight = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown invalid crop input."),
        };
        ResolvedPreviewSource invalid = fixture.Source with { Selection = selection };
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(invalid, destination, CancellationToken.None));

        Assert.Equal("CropInsideSourceSprite", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RejectsReportedShortReadWithoutPublishingBytes()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 1, 2);
        using var encoder = new TrickplayPreviewEncoder(
            NullLogger<TrickplayPreviewEncoder>.Instance,
            static _ => { },
            static (_, _, count, _) => count - 1);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal("SourceSpriteRowsRead", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RejectsCorruptJpegWithoutPublishingBytes()
    {
        byte[] sourceBytes = DecodeFixture(BaselineJpeg);
        Array.Resize(ref sourceBytes, sourceBytes.Length - 1_500);
        using SourceFixture fixture = SourceFixture.Create(sourceBytes, 1, 2);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal("SourceSpriteCodecCreated", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task LeavesTheDestinationOpenWhenEncodingFails()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 1);
        using var encoder = new TrickplayPreviewEncoder(NullLogger<TrickplayPreviewEncoder>.Instance);
        using var destination = new FailingWriteStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal("PreviewJpegEncoded", exception.Details.FailedValidation);
        Assert.True(destination.CanWrite);
        Assert.Equal(0, destination.Length);
    }
}
