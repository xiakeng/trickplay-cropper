using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public abstract class TrickplayPreviewEncoderSharedSpecs
{
    private protected const int CellHeight = 24;
    private protected const int CellWidth = 32;
    private protected const int PixelTolerance = 18;
    private protected const string BaselineJpeg = """
        /9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoK
        CgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoK
        CgoKCgoKCgr/wAARCAAwAGADAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUF
        BAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVW
        V1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi
        4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAEC
        AxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVm
        Z2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq
        8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD5ztLT737z07V+Bn+qgyaX7fj5dmz3znP/AOqgCaaX7Bj5d+/3xjH/AOugBlpafe/eenag
        D6EtLT737z07Vof80YyaX7fj5dmz3znP/wCqgCaaX7Bj5d+/3xjH/wCugBlpafe/eenagD5XtLT737z07V/s4f0IMml+34+XZs98
        5z/+qgCaaX7Bj5d+/wB8Yx/+ugBlpafe/eenagDoZpft+Pl2bPfOc/8A6q/xfP8Adgmml+wY+Xfv98Yx/wDroAZaWn3v3np2oAZN
        L9vx8uzZ75zn/wDVQB9CTS/b8fLs2e+c5/8A1Vof80ZNNL9gx8u/f74xj/8AXQAy0tPvfvPTtQAyaX7fj5dmz3znP/6qAPleaX7f
        j5dmz3znP/6q/wBnD+hCaaX7Bj5d+/3xjH/66AGWlp97956dqAGTS/b8fLs2e+c5/wD1UAdVNL9gx8u/f74xj/8AXX+L5/uwMtLT
        737z07UAMml+34+XZs985z/+qgCaaX7Bj5d+/wB8Yx/+ugD6Eml+wY+Xfv8AfGMf/rrQ/wCaMZaWn3v3np2oAZNL9vx8uzZ75zn/
        APVQBNNL9gx8u/f74xj/APXQB8rzS/YMfLv3++MY/wD11/s4f0IMtLT737z07UAMml+34+XZs985z/8AqoAmml+wY+Xfv98Yx/8A
        roA/Xm0tPvfvPTtX/MOf6IDJpft+Pl2bPfOc/wD6qAJppfsGPl37/fGMf/roAZaWn3v3np2oA/LO0tPvfvPTtX/RgewMml+34+XZ
        s985z/8AqoAmml+wY+Xfv98Yx/8AroAZaWn3v3np2oA+lLS0+9+89O1flZ/g+Mml+34+XZs985z/APqoAmml+wY+Xfv98Yx/+ugB
        lpafe/eenagD61ml+34+XZs985z/APqr/CM/3IJppfsGPl37/fGMf/roAZaWn3v3np2oAZNL9vx8uzZ75zn/APVQB+Wc0v2/Hy7N
        nvnOf/1V/wBGB7BNNL9gx8u/f74xj/8AXQAy0tPvfvPTtQAyaX7fj5dmz3znP/6qAPpSaX7fj5dmz3znP/6q/Kz/AAfJppfsGPl3
        7/fGMf8A66AGWlp97956dqAGTS/b8fLs2e+c5/8A1UAfX00v2DHy79/vjGP/ANdf4Rn+5Ay0tPvfvPTtQAyaX7fj5dmz3znP/wCq
        gCaaX7Bj5d+/3xjH/wCugD8s5pfsGPl37/fGMf8A66/6MD2Blpafe/eenagBk0v2/Hy7NnvnOf8A9VAE00v2DHy79/vjGP8A9dAH
        0pNL9gx8u/f74xj/APXX5Wf4PjLS0+9+89O1ADJpft+Pl2bPfOc//qoAmml+wY+Xfv8AfGMf/roA/9k=
        """;
    private protected const string NonJpeg = """
        iVBORw0KGgoAAAANSUhEUgAAAGAAAAAwCAIAAABhdOiYAAAA60lEQVR4nO2ZsQkCQRQF924PEyswE0xsxYZMDIyvBFNLECuxA9tY
        EIwUA90R8SsLM9EefF4wvF34XHdaLtKNnPPTcynl45nVOA/N73djbH6SKgoCFAQMEff2nfO38vvo/CRVFAQoCBgi7u2rmYj8aXC+
        DQIUBCgIGFp8d36Zb4MABQEKAtzFKD9JFQUBCgLcxWDGBgEKAhQEdOfj7P4R8e4c1vvQ/M32EppvgwAFAQoCmt/FHo7uYv9AQYCC
        gOZ3sZwnofk2CFAQoCDA/2IwY4MABQEKAtzFYMYGAQoCFAS4i8GMDQIUBCgIuAJcOBeeCjh8vwAAAABJRU5ErkJggg==
        """;
    private protected const string ProgressiveJpeg = """
        /9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoK
        CgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoK
        CgoKCgoKCgr/wgARCAAwAGADAREAAhEBAxEB/8QAGAABAQEBAQAAAAAAAAAAAAAAAgEHCAX/xAAaAQEBAAIDAAAAAAAAAAAAAAAA
        CAYJAgQH/9oADAMBAAIQAxAAAAHOcBqojIaFy1okZDK7O9CIyHoRfdjIE0LlrRZAmV2d6EyBPVi+7IEZoXLWjAjMrs70KBGde6w6
        IIyHLOxjuEZDSsVg8jIa1CNyMgTlnYx3GQJpWKweyBNfhG5IEZyzsY7kCM0rFYPgRn//xAAXEAADAQAAAAAAAAAAAAAAAAAAAQIS
        /9oACAEBAAEFApkb2N4JkmRvY3gmSZG9jeCZG9jeCZG9jexvBMjexvY3gmRvY3gmRvY3gbwTI3sbwN4Jkb2N4Jkb2N4JkmRvY3gm
        SZG9jeCZG9jeCZG9jexvBMjexvY3gmRvY3gmRvY3gbwTI3sbwN4Jkb2N4P/EABQRAQAAAAAAAAAAAAAAAAAAAGD/2gAIAQMBAT8B
        Ef/EABQRAQAAAAAAAAAAAAAAAAAAAGD/2gAIAQIBAT8BEf/EABcQAQEBAQAAAAAAAAAAAAAAADEQIAD/2gAIAQEABj8Ca815rXDh
        rhw1rzXmtcOGuHDf/8QAFxABAQEBAAAAAAAAAAAAAAAAEQBRMf/aAAgBAQABPyHvMoWVbvO8yhZVu87zKFlW7zKFlW7zKFlCyrd5
        lCyhZVu8yhZVu8yhZVsq3eZQsq2VbvMoWVbvMoWVbvO8yhZVu87zKFlW7zKFlW7zKFlCyrd5lCyhZVu8yhZVu8yhZVsq3eZQsq2V
        bvMoWVb/2gAMAwEAAgADAAAAEBIBIBIJBJBJBIJIJIJBIBIBIJBJBJBIJIJIJP/EABQRAQAAAAAAAAAAAAAAAAAAAGD/2gAIAQMB
        AT8QEf/EABQRAQAAAAAAAAAAAAAAAAAAAGD/2gAIAQIBAT8QEf/EABkQAAIDAQAAAAAAAAAAAAAAAAARIaHxQf/aAAgBAQABPxCs
        4Q7bbwh20lpWcKzhDttvCHbSWlZwrOEO228IdtJaVnCHbbeEO2ktKzhDttvCHbbeEO2ktKzhDttvCHbbeEO2ktKzhDttvCHbSWlZ
        wh223hDtpLSHbSWlZwh223hDtpLSHbSWlZwh223hDtpLSs4Q7bbwh20lpWcKzhDttvCHbSWlZwrOEO228IdtJaVnCHbbeEO2ktKz
        hDttvCHbbeEO2ktKzhDttvCHbbeEO2ktKzhDttvCHbSWlZwh223hDtpLSHbSWlZwh223hDtpLSHbSWlZwh223hDtpLT/2Q==
        """;

    private protected static readonly EventId DecodePermitWaiting = new(1007, "TrickplayPreviewDecodePermitWaiting");
    private protected static async Task<IReadOnlyList<RecordedEvent>> RunSaturatedFifthEncodeAsync(
        DebugProtocolLogger<TrickplayPreviewEncoder> logger,
        Action<IReadOnlyList<RecordedEvent>> observeWhileWaiting)
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder(logger);
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
            Assert.Empty(logger.Events);

            using var fifthDestination = new MemoryStream();
            Task<PreviewEncodingTelemetry> fifth = encoder.EncodeAsync(
                fixture.Source,
                fifthDestination,
                CancellationToken.None);
            Assert.False(fifth.IsCompleted);
            observeWhileWaiting(logger.Events);

            release.Set();
            await fifth.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(owners).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fifthDestination.Length > 0);
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

        return logger.Events;
    }

    public static TheoryData<JpegFixture, int, int> ValidCrops => new()
    {
        { JpegFixture.Baseline, 0, 1 },
        { JpegFixture.Baseline, 1, 2 },
        { JpegFixture.Progressive, 0, 1 },
        { JpegFixture.Progressive, 1, 2 },
    };

    private protected static byte[] DecodeFixture(string fixture)
    {
        string compactFixture = string.Concat(fixture.Where(character => !char.IsWhiteSpace(character)));
        return Convert.FromBase64String(compactFixture);
    }

    private protected static byte[] GetFixture(JpegFixture fixture)
    {
        return fixture switch
        {
            JpegFixture.Baseline => DecodeFixture(BaselineJpeg),
            JpegFixture.Progressive => DecodeFixture(ProgressiveJpeg),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown JPEG fixture."),
        };
    }

    private protected static byte[] CreateTallJpeg(int height)
    {
        using var bitmap = new SKBitmap(CellWidth, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var destination = new MemoryStream();
        Assert.True(bitmap.Encode(destination, SKEncodedImageFormat.Jpeg, quality: 95));
        return destination.ToArray();
    }

    private protected static async Task AssertFullDecodeCapacityAvailableAsync(
        TrickplayPreviewEncoder encoder,
        ResolvedPreviewSource source)
    {
        using var blocked = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        BlockingWriteStream[] destinations = Enumerable.Range(0, 4)
            .Select(_ => new BlockingWriteStream(blocked, release))
            .ToArray();
        Task<PreviewEncodingTelemetry>[] owners = destinations
            .Select(destination => Task.Run(
                () => encoder.EncodeAsync(source, destination, CancellationToken.None)))
            .ToArray();

        try
        {
            Assert.True(blocked.Wait(TimeSpan.FromSeconds(10)));
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

    private protected static void AssertPixelsAgree(SKBitmap source, SKBitmap preview, int row, int column)
    {
        int[] sampleCoordinatesX = [4, 16, 27];
        int[] sampleCoordinatesY = [4, 12, 19];
        foreach (int sampleY in sampleCoordinatesY)
        {
            foreach (int sampleX in sampleCoordinatesX)
            {
                SKColor expected = source.GetPixel((column * CellWidth) + sampleX, (row * CellHeight) + sampleY);
                SKColor actual = preview.GetPixel(sampleX, sampleY);
                Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, PixelTolerance);
                Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, PixelTolerance);
                Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, PixelTolerance);
            }
        }
    }

    private protected sealed class SourceFixture : IDisposable
    {
        private SourceFixture(string directoryPath, ResolvedPreviewSource source)
        {
            DirectoryPath = directoryPath;
            Source = source;
        }

        public string DirectoryPath { get; }

        public ResolvedPreviewSource Source { get; }

        public static SourceFixture Create(byte[] sourceBytes, int row, int column)
        {
            var metadata = new TrickplayMetadata(CellWidth, CellHeight, 1_000, 3, 2, 6);
            return Create(sourceBytes, metadata, row, column);
        }

        public static SourceFixture Create(
            byte[] sourceBytes,
            TrickplayMetadata metadata,
            int row,
            int column)
        {
            string directoryPath = Path.Combine(Path.GetTempPath(), $"trickplay-encoder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            string sourcePath = Path.Combine(directoryPath, "source-sprite");
            File.WriteAllBytes(sourcePath, sourceBytes);
            int frameIndex = (row * metadata.TileWidth) + column;
            FrameSelection selection = FrameSelection.Create(metadata, frameIndex);
            var source = new ResolvedPreviewSource(
                Guid.Parse("04e16925-12a7-49cf-b973-c51924ea54a8"),
                sourcePath,
                sourceBytes.Length,
                File.GetLastWriteTimeUtc(sourcePath).Ticks,
                metadata,
                selection);
            return new SourceFixture(directoryPath, source);
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private protected sealed class FailingWriteStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new IOException("The test destination rejected the JPEG payload.");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw new IOException("The test destination rejected the JPEG payload.");
        }
    }

    private protected sealed class BlockingWriteStream : MemoryStream
    {
        private readonly CountdownEvent blocked;
        private readonly ManualResetEventSlim release;
        private int isBlocked;

        public BlockingWriteStream(CountdownEvent blocked, ManualResetEventSlim release)
        {
            this.blocked = blocked;
            this.release = release;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BlockOnce();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BlockOnce();
            base.Write(buffer);
        }

        private void BlockOnce()
        {
            if (Interlocked.Exchange(ref isBlocked, 1) == 0)
            {
                blocked.Signal();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The blocked encoder was not released by the test.");
                }
            }
        }
    }

    public enum JpegFixture
    {
        Baseline,
        Progressive,
    }

    public enum InvalidCropInput
    {
        NegativeX,
        NegativeY,
        NegativeWidth,
        NegativeHeight,
        ZeroWidth,
        ZeroHeight,
    }
}
