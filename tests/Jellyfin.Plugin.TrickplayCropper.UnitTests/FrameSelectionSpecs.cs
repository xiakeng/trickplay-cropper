using Jellyfin.Plugin.TrickplayCropper.Preview;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class FrameSelectionSpecs
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(1, 0, 0, 1, 320, 0)]
    [InlineData(5, 0, 1, 2, 640, 180)]
    [InlineData(6, 1, 0, 0, 0, 0)]
    [InlineData(7, 1, 0, 1, 320, 0)]
    public void SelectsTheRowMajorCellOfEveryFrameIndex(
        int frameIndex,
        int spriteIndex,
        int row,
        int column,
        int cropX,
        int cropY)
    {
        var metadata = new TrickplayMetadata(320, 180, 1_000, 3, 2, 8);

        FrameSelection selection = FrameSelection.Create(metadata, frameIndex);

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

        FrameSelection selection = FrameSelection.Create(metadata, int.MaxValue - 1);

        Assert.Equal(int.MaxValue - 1, selection.FrameIndex);
        Assert.Equal(0, selection.SpriteIndex);
        Assert.Equal(0, selection.Row);
        Assert.Equal(int.MaxValue - 1, selection.Column);
        Assert.Equal(int.MaxValue - 1, selection.CropX);
        Assert.Equal(0, selection.CropY);
    }

    [Theory]
    [MemberData(nameof(OverflowingCoordinates))]
    public void RejectsCoordinatesThatDoNotFitSkiaIntegers(
        CoordinateOverflow overflow,
        string failedValidation,
        long failedValue)
    {
        (TrickplayMetadata metadata, int frameIndex) = overflow switch
        {
            CoordinateOverflow.CropX => (new TrickplayMetadata(int.MaxValue, 1, 1, 3, 1, 3), 2),
            CoordinateOverflow.CropY => (new TrickplayMetadata(1, int.MaxValue, 1, 1, 3, 3), 2),
            CoordinateOverflow.CropRight => (new TrickplayMetadata(int.MaxValue, 1, 1, 2, 1, 2), 1),
            CoordinateOverflow.CropBottom => (new TrickplayMetadata(1, int.MaxValue, 1, 1, 2, 2), 1),
            _ => throw new ArgumentOutOfRangeException(nameof(overflow), overflow, "Unknown overflow case."),
        };

        InvalidTrickplayMetadataException exception = Assert.Throws<InvalidTrickplayMetadataException>(
            () => FrameSelection.Create(metadata, frameIndex));

        Assert.Equal(failedValidation, exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
        FrameSelectionDiagnostics diagnostics = Assert.IsType<FrameSelectionDiagnostics>(
            exception.SelectionDiagnostics);
        AssertSelectionDiagnostics(overflow, diagnostics);
    }

    public static TheoryData<CoordinateOverflow, string, long> OverflowingCoordinates => new()
    {
        { CoordinateOverflow.CropX, "CropXInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropY, "CropYInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropRight, "CropRightInt32", 2L * int.MaxValue },
        { CoordinateOverflow.CropBottom, "CropBottomInt32", 2L * int.MaxValue },
    };

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

    public enum CoordinateOverflow
    {
        CropX,
        CropY,
        CropRight,
        CropBottom,
    }
}
