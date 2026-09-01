using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PreviewIdentitySpecs
{
    [Fact]
    public void UsesTheExactCanonicalHashEntityTagAndCacheCoordinates()
    {
        ResolvedPreviewSource source = CreateSource();

        PreviewIdentity identity = PreviewIdentity.Create(source);

        Assert.Equal("06a51b2ba2d9de0bb1cc8117c4b7055a", identity.SourceStamp);
        Assert.Equal("\"06a51b2ba2d9de0bb1cc8117c4b7055a-f0000000042\"", identity.EntityTag);
        Assert.Equal(
            Path.Combine(
                "00112233445566778899aabbccddeeff",
                "w0320",
                "s000002-06a51b2ba2d9de0bb1cc8117c4b7055a",
                "f0000000042.jpg"),
            identity.RelativePath);
    }

    [Theory]
    [InlineData(IncludedSourceInput.MediaSourceId)]
    [InlineData(IncludedSourceInput.FrameWidth)]
    [InlineData(IncludedSourceInput.FrameHeight)]
    [InlineData(IncludedSourceInput.IntervalMilliseconds)]
    [InlineData(IncludedSourceInput.TileWidth)]
    [InlineData(IncludedSourceInput.TileHeight)]
    [InlineData(IncludedSourceInput.ThumbnailCount)]
    [InlineData(IncludedSourceInput.SpriteIndex)]
    [InlineData(IncludedSourceInput.SourceLength)]
    [InlineData(IncludedSourceInput.SourceLastWriteUtcTicks)]
    public void ChangesTheSourceStampForEveryCanonicalSourceInput(IncludedSourceInput input)
    {
        ResolvedPreviewSource source = CreateSource();
        PreviewIdentity baseline = PreviewIdentity.Create(source);

        PreviewIdentity changed = PreviewIdentity.Create(ChangeIncludedInput(source, input));

        Assert.NotEqual(baseline.SourceStamp, changed.SourceStamp);
    }

    [Fact]
    public void ChangesTheEntityAndEntryForAnotherFrameWithoutChangingTheSourceStamp()
    {
        ResolvedPreviewSource source = CreateSource();
        PreviewIdentity baseline = PreviewIdentity.Create(source);
        ResolvedPreviewSource nextFrame = source with
        {
            Selection = source.Selection with { FrameIndex = 43 },
        };

        PreviewIdentity changed = PreviewIdentity.Create(nextFrame);

        Assert.Equal(baseline.SourceStamp, changed.SourceStamp);
        Assert.NotEqual(baseline.EntityTag, changed.EntityTag);
        Assert.NotEqual(baseline.RelativePath, changed.RelativePath);
    }

    [Fact]
    public void ExcludesTheManagerOwnedSourceSpritePath()
    {
        ResolvedPreviewSource source = CreateSource();
        ResolvedPreviewSource movedSource = source with
        {
            SourceSpritePath = Path.Combine("different", "private", "location.jpg"),
        };

        Assert.Equal(PreviewIdentity.Create(source), PreviewIdentity.Create(movedSource));
    }

    [Fact]
    public void FormatsCanonicalValuesWithoutUsingTheCurrentCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-EG");

            PreviewIdentity identity = PreviewIdentity.Create(CreateSource());

            Assert.Equal("06a51b2ba2d9de0bb1cc8117c4b7055a", identity.SourceStamp);
            Assert.Equal("\"06a51b2ba2d9de0bb1cc8117c4b7055a-f0000000042\"", identity.EntityTag);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static ResolvedPreviewSource ChangeIncludedInput(
        ResolvedPreviewSource source,
        IncludedSourceInput input)
    {
        return input switch
        {
            IncludedSourceInput.MediaSourceId => source with
            {
                MediaSourceId = Guid.Parse("10112233-4455-6677-8899-aabbccddeeff"),
            },
            IncludedSourceInput.FrameWidth => ChangeMetadata(source, source.Metadata with { FrameWidth = 321 }),
            IncludedSourceInput.FrameHeight => ChangeMetadata(source, source.Metadata with { FrameHeight = 181 }),
            IncludedSourceInput.IntervalMilliseconds => ChangeMetadata(
                source,
                source.Metadata with { IntervalMilliseconds = 10_001 }),
            IncludedSourceInput.TileWidth => ChangeMetadata(source, source.Metadata with { TileWidth = 6 }),
            IncludedSourceInput.TileHeight => ChangeMetadata(source, source.Metadata with { TileHeight = 5 }),
            IncludedSourceInput.ThumbnailCount => ChangeMetadata(
                source,
                source.Metadata with { ThumbnailCount = 78 }),
            IncludedSourceInput.SpriteIndex => source with
            {
                Selection = source.Selection with { SpriteIndex = 3 },
            },
            IncludedSourceInput.SourceLength => source with { SourceLength = 123_457 },
            IncludedSourceInput.SourceLastWriteUtcTicks => source with
            {
                SourceLastWriteUtcTicks = 638_397_614_450_000_001,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown included identity input."),
        };
    }

    private static ResolvedPreviewSource ChangeMetadata(
        ResolvedPreviewSource source,
        TrickplayMetadata metadata)
    {
        return source with { Metadata = metadata };
    }

    private static ResolvedPreviewSource CreateSource()
    {
        Guid mediaSourceId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var metadata = new TrickplayMetadata(320, 180, 10_000, 5, 4, 77);
        var selection = new FrameSelection(42, 2, 0, 2, 640, 0, 320, 180);
        return new ResolvedPreviewSource(
            mediaSourceId,
            Path.Combine("manager", "source-sprite.jpg"),
            123_456,
            638_397_614_450_000_000,
            metadata,
            selection);
    }

    public enum IncludedSourceInput
    {
        MediaSourceId,
        FrameWidth,
        FrameHeight,
        IntervalMilliseconds,
        TileWidth,
        TileHeight,
        ThumbnailCount,
        SpriteIndex,
        SourceLength,
        SourceLastWriteUtcTicks,
    }
}
