using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class FrameSelectionSpecs
{
    private const long TicksPerInterval = 10_000_000;

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]
    [InlineData(TicksPerInterval - 1, 0, 0, 0, 0, 0, 0)]
    [InlineData(TicksPerInterval, 1, 0, 0, 1, 320, 0)]
    [InlineData(5 * TicksPerInterval, 5, 0, 1, 2, 640, 180)]
    [InlineData(6 * TicksPerInterval, 6, 1, 0, 0, 0, 0)]
    [InlineData(7 * TicksPerInterval, 7, 1, 0, 1, 320, 0)]
    [InlineData(long.MaxValue, 7, 1, 0, 1, 320, 0)]
    public void SelectsTheRowMajorCellAtEveryBoundary(
        long positionTicks,
        int frameIndex,
        int spriteIndex,
        int row,
        int column,
        int cropX,
        int cropY)
    {
        var metadata = new TrickplayMetadata(320, 180, 1_000, 3, 2, 8);

        FrameSelection selection = FrameSelection.Create(metadata, positionTicks);

        Assert.Equal(frameIndex, selection.FrameIndex);
        Assert.Equal(spriteIndex, selection.SpriteIndex);
        Assert.Equal(row, selection.Row);
        Assert.Equal(column, selection.Column);
        Assert.Equal(cropX, selection.CropX);
        Assert.Equal(cropY, selection.CropY);
        Assert.Equal(320, selection.CropWidth);
        Assert.Equal(180, selection.CropHeight);
    }

    [Fact]
    public void KeepsExtremeProductsInCheckedInt64Arithmetic()
    {
        var metadata = new TrickplayMetadata(1, 1, 1, int.MaxValue, int.MaxValue, int.MaxValue);

        FrameSelection selection = FrameSelection.Create(metadata, long.MaxValue);

        Assert.Equal(int.MaxValue - 1, selection.FrameIndex);
        Assert.Equal(0, selection.SpriteIndex);
        Assert.Equal(0, selection.Row);
        Assert.Equal(int.MaxValue - 1, selection.Column);
        Assert.Equal(int.MaxValue - 1, selection.CropX);
        Assert.Equal(0, selection.CropY);
    }

    [Fact]
    public void RejectsNegativePlaybackPositions()
    {
        TrickplayMetadata metadata = CreateValidMetadata();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameSelection.Create(metadata, -1));

        Assert.Equal("positionTicks", exception.ParamName);
        Assert.Equal(-1L, exception.ActualValue);
    }

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public void RejectsEveryNonPositiveMetadataValue(
        MetadataField field,
        string failedValidation,
        int failedValue)
    {
        TrickplayMetadata metadata = ChangeMetadataField(CreateValidMetadata(), field, failedValue);

        InvalidTrickplayMetadataException exception = Assert.Throws<InvalidTrickplayMetadataException>(
            () => FrameSelection.Create(metadata, 0));

        Assert.Equal(failedValidation, exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
        Assert.Same(metadata, exception.Metadata);
        Assert.Contains(failedValidation, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            failedValue.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(OverflowingCoordinates))]
    public void RejectsCoordinatesThatDoNotFitSkiaIntegers(
        CoordinateOverflow overflow,
        string failedValidation,
        long failedValue)
    {
        (TrickplayMetadata metadata, long positionTicks) = overflow switch
        {
            CoordinateOverflow.CropX => (
                new TrickplayMetadata(int.MaxValue, 1, 1, 3, 1, 3),
                2 * TimeSpan.TicksPerMillisecond),
            CoordinateOverflow.CropY => (
                new TrickplayMetadata(1, int.MaxValue, 1, 1, 3, 3),
                2 * TimeSpan.TicksPerMillisecond),
            CoordinateOverflow.CropRight => (
                new TrickplayMetadata(int.MaxValue, 1, 1, 2, 1, 2),
                TimeSpan.TicksPerMillisecond),
            CoordinateOverflow.CropBottom => (
                new TrickplayMetadata(1, int.MaxValue, 1, 1, 2, 2),
                TimeSpan.TicksPerMillisecond),
            _ => throw new ArgumentOutOfRangeException(nameof(overflow), overflow, "Unknown overflow case."),
        };

        InvalidTrickplayMetadataException exception = Assert.Throws<InvalidTrickplayMetadataException>(
            () => FrameSelection.Create(metadata, positionTicks));

        Assert.Equal(failedValidation, exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
        FrameSelectionDiagnostics diagnostics = Assert.IsType<FrameSelectionDiagnostics>(
            exception.SelectionDiagnostics);
        AssertSelectionDiagnostics(overflow, diagnostics);
    }

    public static TheoryData<MetadataField, string, int> InvalidMetadata => new()
    {
        { MetadataField.FrameWidth, "FrameWidthPositive", 0 },
        { MetadataField.FrameWidth, "FrameWidthPositive", -1 },
        { MetadataField.FrameHeight, "FrameHeightPositive", 0 },
        { MetadataField.FrameHeight, "FrameHeightPositive", -1 },
        { MetadataField.IntervalMilliseconds, "IntervalMillisecondsPositive", 0 },
        { MetadataField.IntervalMilliseconds, "IntervalMillisecondsPositive", -1 },
        { MetadataField.TileWidth, "TileWidthPositive", 0 },
        { MetadataField.TileWidth, "TileWidthPositive", -1 },
        { MetadataField.TileHeight, "TileHeightPositive", 0 },
        { MetadataField.TileHeight, "TileHeightPositive", -1 },
        { MetadataField.ThumbnailCount, "ThumbnailCountPositive", 0 },
        { MetadataField.ThumbnailCount, "ThumbnailCountPositive", -1 },
    };

    public static TheoryData<CoordinateOverflow, string, long> OverflowingCoordinates => new()
    {
        { CoordinateOverflow.CropX, "CropXInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropY, "CropYInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropRight, "CropRightInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropBottom, "CropBottomInt32", 2L * int.MaxValue },
    };

    private static TrickplayMetadata ChangeMetadataField(
        TrickplayMetadata metadata,
        MetadataField field,
        int value)
    {
        return field switch
        {
            MetadataField.FrameWidth => metadata with { FrameWidth = value },
            MetadataField.FrameHeight => metadata with { FrameHeight = value },
            MetadataField.IntervalMilliseconds => metadata with { IntervalMilliseconds = value },
            MetadataField.TileWidth => metadata with { TileWidth = value },
            MetadataField.TileHeight => metadata with { TileHeight = value },
            MetadataField.ThumbnailCount => metadata with { ThumbnailCount = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown metadata field."),
        };
    }

    private static TrickplayMetadata CreateValidMetadata()
    {
        return new TrickplayMetadata(320, 180, 1_000, 3, 2, 8);
    }

    private static void AssertSelectionDiagnostics(
        CoordinateOverflow overflow,
        FrameSelectionDiagnostics diagnostics)
    {
        Assert.Equal(0, diagnostics.SpriteIndex);
        switch (overflow)
        {
            case CoordinateOverflow.CropX:
                Assert.Equal(2, diagnostics.FrameIndex);
                Assert.Equal(0, diagnostics.Row);
                Assert.Equal(2, diagnostics.Column);
                Assert.Equal(2L * int.MaxValue, diagnostics.CropX);
                Assert.Equal(0, diagnostics.CropY);
                break;
            case CoordinateOverflow.CropY:
                Assert.Equal(2, diagnostics.FrameIndex);
                Assert.Equal(2, diagnostics.Row);
                Assert.Equal(0, diagnostics.Column);
                Assert.Equal(0, diagnostics.CropX);
                Assert.Equal(2L * int.MaxValue, diagnostics.CropY);
                break;
            case CoordinateOverflow.CropRight:
                Assert.Equal(1, diagnostics.FrameIndex);
                Assert.Equal(0, diagnostics.Row);
                Assert.Equal(1, diagnostics.Column);
                Assert.Equal(int.MaxValue, diagnostics.CropX);
                Assert.Equal(0, diagnostics.CropY);
                break;
            case CoordinateOverflow.CropBottom:
                Assert.Equal(1, diagnostics.FrameIndex);
                Assert.Equal(1, diagnostics.Row);
                Assert.Equal(0, diagnostics.Column);
                Assert.Equal(0, diagnostics.CropX);
                Assert.Equal(int.MaxValue, diagnostics.CropY);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overflow), overflow, "Unknown overflow case.");
        }
    }

    public enum MetadataField
    {
        FrameWidth,
        FrameHeight,
        IntervalMilliseconds,
        TileWidth,
        TileHeight,
        ThumbnailCount,
    }

    public enum CoordinateOverflow
    {
        CropX,
        CropY,
        CropRight,
        CropBottom,
    }
}
