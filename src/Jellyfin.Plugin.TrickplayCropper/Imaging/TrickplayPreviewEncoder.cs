using System.Diagnostics;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using SkiaSharp;

namespace Jellyfin.Plugin.TrickplayCropper.Imaging;

/// <summary>
/// Uses the Skia JPEG horizontal-subset scanline path to encode Trickplay Previews.
/// </summary>
internal sealed class TrickplayPreviewEncoder : ITrickplayPreviewEncoder, IDisposable
{
    private const int DecodePermitCount = 4;
    private const string DecodePath = "SUBSET";
    private const int ScanlineBatchSize = 64;
    private readonly SemaphoreSlim decodePermits = new(DecodePermitCount, DecodePermitCount);

    /// <inheritdoc />
    public async Task<PreviewEncodingTelemetry> EncodeAsync(
        ResolvedPreviewSource source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await decodePermits.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Encode(source, destination, cancellationToken);
        }
        finally
        {
            decodePermits.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        decodePermits.Dispose();
    }

    private static PreviewEncodingTelemetry Encode(
        ResolvedPreviewSource source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        long decodeStarted = Stopwatch.GetTimestamp();
        SKCodec? codec = null;
        try
        {
            codec = CreateCodec(source);
            ValidateSource(codec, source);
            using SKBitmap bitmap = CreateBitmap(source, codec);
            StartSubsetDecode(codec, source);
            SkipToCrop(codec, source.Selection.CropY, source, cancellationToken);
            ReadCrop(codec, bitmap, source, cancellationToken);
            TimeSpan decodeDuration = Stopwatch.GetElapsedTime(decodeStarted);

            cancellationToken.ThrowIfCancellationRequested();
            long encodeStarted = Stopwatch.GetTimestamp();
            EncodeJpeg(bitmap, destination, source, codec);

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
        finally
        {
            codec?.Dispose();
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

    private static void ValidateSource(SKCodec codec, ResolvedPreviewSource source)
    {
        if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg)
        {
            ThrowValidationFailure(
                source,
                codec,
                "SourceSpriteIsJpeg",
                "The Source Sprite is not encoded as JPEG.");
        }

        SKImageInfo actual = codec.Info;
        if (actual.Width <= 0 || actual.Height <= 0)
        {
            ThrowValidationFailure(
                source,
                codec,
                "SourceSpriteDimensionsPositive",
                "The Source Sprite dimensions must be positive.");
        }

        long expectedWidth = checked((long)source.Metadata.TileWidth * source.Metadata.FrameWidth);
        long expectedHeight = checked((long)source.Metadata.TileHeight * source.Metadata.FrameHeight);
        if (actual.Width != expectedWidth || actual.Height != expectedHeight)
        {
            ThrowValidationFailure(
                source,
                codec,
                "SourceSpriteDimensionsMatchMetadata",
                "The Source Sprite dimensions do not match the metadata-defined grid.");
        }

        ValidateCrop(source, codec);
    }

    private static void ValidateCrop(ResolvedPreviewSource source, SKCodec codec)
    {
        FrameSelection selection = source.Selection;
        long cropRight = checked((long)selection.CropX + selection.CropWidth);
        long cropBottom = checked((long)selection.CropY + selection.CropHeight);
        bool isInside = selection.CropX >= 0
            && selection.CropY >= 0
            && selection.CropWidth > 0
            && selection.CropHeight > 0
            && cropRight <= codec.Info.Width
            && cropBottom <= codec.Info.Height;
        if (!isInside)
        {
            ThrowValidationFailure(
                source,
                codec,
                "CropInsideSourceSprite",
                "The selected crop is outside the Source Sprite bounds.");
        }
    }

    private static SKBitmap CreateBitmap(ResolvedPreviewSource source, SKCodec codec)
    {
        FrameSelection selection = source.Selection;
        var bitmap = new SKBitmap(
            selection.CropWidth,
            selection.CropHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);
        if (bitmap.IsEmpty || bitmap.GetPixels() == IntPtr.Zero)
        {
            bitmap.Dispose();
            ThrowValidationFailure(
                source,
                codec,
                "PreviewBitmapAllocated",
                "Skia could not allocate the cell-sized Trickplay Preview bitmap.");
        }

        return bitmap;
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

    private static void SkipToCrop(
        SKCodec codec,
        int cropY,
        ResolvedPreviewSource source,
        CancellationToken cancellationToken)
    {
        int remainingRows = cropY;
        while (remainingRows > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int batchSize = Math.Min(remainingRows, ScanlineBatchSize);
            bool skipped = codec.SkipScanlines(batchSize);
            cancellationToken.ThrowIfCancellationRequested();
            if (!skipped)
            {
                ThrowValidationFailure(
                    source,
                    codec,
                    "SourceSpriteRowsSkipped",
                    "Skia could not skip to the selected Source Sprite row.");
            }

            remainingRows -= batchSize;
        }
    }

    private static void ReadCrop(
        SKCodec codec,
        SKBitmap bitmap,
        ResolvedPreviewSource source,
        CancellationToken cancellationToken)
    {
        int writtenRows = 0;
        while (writtenRows < bitmap.Height)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int batchSize = Math.Min(bitmap.Height - writtenRows, ScanlineBatchSize);
            int byteOffset = checked(writtenRows * bitmap.RowBytes);
            IntPtr destination = IntPtr.Add(bitmap.GetPixels(), byteOffset);
            int decodedRows = codec.GetScanlines(destination, batchSize, bitmap.RowBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (decodedRows != batchSize)
            {
                ThrowValidationFailure(
                    source,
                    codec,
                    "SourceSpriteRowsRead",
                    $"Skia returned {decodedRows} of {batchSize} requested Source Sprite scanlines.");
            }

            writtenRows += batchSize;
        }
    }

    private static void EncodeJpeg(
        SKBitmap bitmap,
        Stream destination,
        ResolvedPreviewSource source,
        SKCodec codec)
    {
        try
        {
            var safeDestination = new DestinationWriteStream(destination);
            bool encoded = bitmap.Encode(safeDestination, SKEncodedImageFormat.Jpeg, PreviewIdentity.JpegQuality);
            if (safeDestination.Failure is not null)
            {
                throw safeDestination.Failure;
            }

            if (!encoded)
            {
                ThrowValidationFailure(
                    source,
                    codec,
                    "PreviewJpegEncoded",
                    "Skia could not encode the selected Trickplay Preview as JPEG.");
            }
        }
        catch (PreviewStageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            PreviewFailureDetails details = CreateFailureDetails(source, codec) with
            {
                FailedValidation = "PreviewJpegEncoded",
            };
            throw new PreviewStageException(exception, details);
        }
    }

    private static void ThrowValidationFailure(
        ResolvedPreviewSource source,
        SKCodec codec,
        string validation,
        string message)
    {
        var cause = new InvalidDataException(message);
        PreviewFailureDetails details = CreateFailureDetails(source, codec) with
        {
            FailedValidation = validation,
        };
        throw new PreviewStageException(cause, details);
    }

    private sealed class DestinationWriteStream : Stream
    {
        private readonly Stream destination;

        public DestinationWriteStream(Stream destination)
        {
            this.destination = destination;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public Exception? Failure { get; private set; }

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            try
            {
                destination.Flush();
            }
            catch (Exception exception)
            {
                Failure ??= exception;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Failure is not null)
            {
                return;
            }

            try
            {
                destination.Write(buffer, offset, count);
            }
            catch (Exception exception)
            {
                Failure = exception;
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Failure is not null)
            {
                return;
            }

            try
            {
                destination.Write(buffer);
            }
            catch (Exception exception)
            {
                Failure = exception;
            }
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
