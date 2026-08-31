namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

using SkiaSharp;
using Xunit;

public sealed class SkiaRuntimeTests
{
    [Fact]
    public void LinuxNativeAssetsCanEncodeATrickplayPreview()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1));
        bitmap.Erase(SKColors.Black);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality: 90);

        Assert.True(encoded.Size > 0);
    }
}
