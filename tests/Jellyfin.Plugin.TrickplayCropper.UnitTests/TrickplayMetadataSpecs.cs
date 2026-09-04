using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class TrickplayMetadataSpecs
{
    private const long TicksPerInterval = 10_000_000;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(TicksPerInterval - 1, 0)]
    [InlineData(TicksPerInterval, 1)]
    [InlineData(5 * TicksPerInterval, 5)]
    [InlineData(6 * TicksPerInterval, 6)]
    [InlineData(7 * TicksPerInterval, 7)]
    [InlineData(long.MaxValue, 7)]
    public void ClampsEveryPlaybackPositionToTheGeneratedFrameSequence(long positionTicks, int frameIndex)
    {
        var metadata = new TrickplayMetadata(320, 180, 1_000, 3, 2, 8);

        Assert.Equal(frameIndex, metadata.SelectFrameIndex(positionTicks));
    }

    [Fact]
    public void ClampsAnExtremePositionToTheLastGeneratedFrame()
    {
        var metadata = new TrickplayMetadata(1, 1, 1, int.MaxValue, int.MaxValue, int.MaxValue);

        Assert.Equal(int.MaxValue - 1, metadata.SelectFrameIndex(long.MaxValue));
    }

    [Fact]
    public void RejectsNegativePlaybackPositions()
    {
        TrickplayMetadata metadata = CreateValidMetadata();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => metadata.SelectFrameIndex(-1));

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
            () => metadata.Validate());

        Assert.Equal(failedValidation, exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
        Assert.Same(metadata, exception.Metadata);
        Assert.Null(exception.SelectionDiagnostics);
        Assert.Contains(failedValidation, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            failedValue.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsEveryPositiveMetadataValue()
    {
        TrickplayMetadata metadata = CreateValidMetadata();

        metadata.Validate();
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

    public enum MetadataField
    {
        FrameWidth,
        FrameHeight,
        IntervalMilliseconds,
        TileWidth,
        TileHeight,
        ThumbnailCount,
    }
}
