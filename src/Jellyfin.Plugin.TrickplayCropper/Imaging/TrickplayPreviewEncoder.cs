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
    private const string DecodePath = "SUBSET";

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
        using SKCodec codec = CreateCodec(source);
        try
        {
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
            bool encoded = bitmap.Encode(destination, SKEncodedImageFormat.Jpeg, PreviewIdentity.JpegQuality);
            if (!encoded)
            {
                throw new InvalidDataException("Skia could not encode the selected Trickplay Preview as JPEG.");
            }

            return new PreviewEncodingTelemetry(decodeDuration, Stopwatch.GetElapsedTime(encodeStarted));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PreviewStageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PreviewStageException(exception, CreateFailureDetails(source, codec));
        }
    }

    private static SKCodec CreateCodec(ResolvedPreviewSource source)
    {
        SKCodec? codec;
        try
        {
            codec = SKCodec.Create(source.SourceSpritePath);
        }
        catch (Exception exception)
        {
            PreviewFailureDetails failureDetails = CreateFailureDetails(source, null) with
            {
                FailedValidation = "SourceSpriteCodecCreated",
            };
            throw new PreviewStageException(exception, failureDetails);
        }

        if (codec is null)
        {
            var cause = new InvalidDataException("The Source Sprite could not be opened as an image.");
            PreviewFailureDetails details = CreateFailureDetails(source, null) with
            {
                FailedValidation = "SourceSpriteCodecCreated",
            };
            throw new PreviewStageException(cause, details);
        }

        return codec;
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
            var cause = new InvalidDataException(
                $"Skia rejected the JPEG SUBSET scanline path with result {result}.");
            PreviewFailureDetails details = CreateFailureDetails(source, codec) with
            {
                FailedValidation = "SubsetScanlineDecodeStarted",
                SkiaResult = result.ToString(),
            };
            throw new PreviewStageException(cause, details);
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

    private static PreviewFailureDetails CreateFailureDetails(
        ResolvedPreviewSource source,
        SKCodec? codec)
    {
        return new PreviewFailureDetails
        {
            ActualHeight = codec?.Info.Height,
            ActualWidth = codec?.Info.Width,
            DecodePath = DecodePath,
            Metadata = source.Metadata,
            Selection = source.Selection,
            SourceLength = source.SourceLength,
            SourceLastWriteUtcTicks = source.SourceLastWriteUtcTicks,
        };
    }
}
