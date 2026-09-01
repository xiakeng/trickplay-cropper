using System.Diagnostics;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using SkiaSharp;

namespace Jellyfin.Plugin.TrickplayCropper.Imaging;

/// <summary>
/// Uses the Skia JPEG horizontal-subset scanline path to encode Trickplay Previews.
/// </summary>
internal sealed class TrickplayPreviewEncoder : ITrickplayPreviewEncoder
{
    private const int JpegQuality = 90;

    /// <inheritdoc />
    public Task<PreviewEncodingTelemetry> EncodeAsync(
        ResolvedPreviewSource source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Encode(source, destination, cancellationToken));
    }

    private static PreviewEncodingTelemetry Encode(
        ResolvedPreviewSource source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        long decodeStarted = Stopwatch.GetTimestamp();
        using SKCodec codec = SKCodec.Create(source.SourceSpritePath)
            ?? throw new InvalidDataException("The Source Sprite could not be opened as an image.");
        using var bitmap = new SKBitmap(
            source.Selection.CropWidth,
            source.Selection.CropHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);
        StartSubsetDecode(codec, source);
        SkipToCrop(codec, source.Selection.CropY);
        ReadCrop(codec, bitmap);
        TimeSpan decodeDuration = Stopwatch.GetElapsedTime(decodeStarted);

        cancellationToken.ThrowIfCancellationRequested();
        long encodeStarted = Stopwatch.GetTimestamp();
        bool encoded = bitmap.Encode(destination, SKEncodedImageFormat.Jpeg, JpegQuality);
        if (!encoded)
        {
            throw new InvalidDataException("Skia could not encode the selected Trickplay Preview as JPEG.");
        }

        return new PreviewEncodingTelemetry(decodeDuration, Stopwatch.GetElapsedTime(encodeStarted));
    }

    private static void StartSubsetDecode(SKCodec codec, ResolvedPreviewSource source)
    {
        FrameSelection selection = source.Selection;
        var subset = new SKRectI(
            selection.CropX,
            0,
            checked(selection.CropX + selection.CropWidth),
            codec.Info.Height);
        // Skia requires native dimensions here; the horizontal subset limits the pixels written per scanline.
        var decodeInfo = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);
        SKCodecResult result = codec.StartScanlineDecode(decodeInfo, new SKCodecOptions(subset));
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"Skia rejected the JPEG SUBSET scanline path with result {result}.");
        }
    }

    private static void SkipToCrop(SKCodec codec, int cropY)
    {
        if (cropY > 0 && !codec.SkipScanlines(cropY))
        {
            throw new InvalidDataException("Skia could not skip to the selected Source Sprite row.");
        }
    }

    private static void ReadCrop(SKCodec codec, SKBitmap bitmap)
    {
        int decodedRows = codec.GetScanlines(bitmap.GetPixels(), bitmap.Height, bitmap.RowBytes);
        if (decodedRows != bitmap.Height)
        {
            throw new InvalidDataException(
                $"Skia returned {decodedRows} of {bitmap.Height} requested Source Sprite scanlines.");
        }
    }
}
