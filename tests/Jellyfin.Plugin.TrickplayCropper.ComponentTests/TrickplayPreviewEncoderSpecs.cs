using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewEncoderSpecs
{
    private const int CellHeight = 24;
    private const int CellWidth = 32;
    private const int PixelTolerance = 18;
    private const string BaselineJpeg = """
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
    private const string NonJpeg = """
        iVBORw0KGgoAAAANSUhEUgAAAGAAAAAwCAIAAABhdOiYAAAA60lEQVR4nO2ZsQkCQRQF924PEyswE0xsxYZMDIyvBFNLECuxA9tY
        EIwUA90R8SsLM9EefF4wvF34XHdaLtKNnPPTcynl45nVOA/N73djbH6SKgoCFAQMEff2nfO38vvo/CRVFAQoCBgi7u2rmYj8aXC+
        DQIUBCgIGFp8d36Zb4MABQEKAtzFKD9JFQUBCgLcxWDGBgEKAhQEdOfj7P4R8e4c1vvQ/M32EppvgwAFAQoCmt/FHo7uYv9AQYCC
        gOZ3sZwnofk2CFAQoCDA/2IwY4MABQEKAtzFYMYGAQoCFAS4i8GMDQIUBCgIuAJcOBeeCjh8vwAAAABJRU5ErkJggg==
        """;
    private const string ProgressiveJpeg = """
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

    [Theory]
    [MemberData(nameof(ValidCrops))]
    public async Task CropsIndependentJpegFixtures(JpegFixture fixture, int row, int column)
    {
        using SourceFixture sourceFixture = SourceFixture.Create(GetFixture(fixture), row, column);
        using var encoder = new TrickplayPreviewEncoder();
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
        using var encoder = new TrickplayPreviewEncoder();
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
        using var encoder = new TrickplayPreviewEncoder();
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
        using var encoder = new TrickplayPreviewEncoder();
        using var destination = new MemoryStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(outOfBounds, destination, CancellationToken.None));

        Assert.Equal("CropInsideSourceSprite", exception.Details.FailedValidation);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RejectsCorruptJpegWithoutPublishingBytes()
    {
        byte[] sourceBytes = DecodeFixture(BaselineJpeg);
        Array.Resize(ref sourceBytes, sourceBytes.Length - 1_500);
        using SourceFixture fixture = SourceFixture.Create(sourceBytes, 1, 2);
        using var encoder = new TrickplayPreviewEncoder();
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task LeavesTheDestinationOpenWhenEncodingFails()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 1);
        using var encoder = new TrickplayPreviewEncoder();
        using var destination = new FailingWriteStream();

        PreviewStageException exception = await Assert.ThrowsAsync<PreviewStageException>(
            () => encoder.EncodeAsync(fixture.Source, destination, CancellationToken.None));

        Assert.Equal("PreviewJpegEncoded", exception.Details.FailedValidation);
        Assert.True(destination.CanWrite);
    }

    [Fact]
    public async Task CancelsBeforeWaitingForADecodePermit()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder();
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => encoder.EncodeAsync(fixture.Source, destination, cancellation.Token));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task AWaitingFifthEncodeCanCancel()
    {
        using SourceFixture fixture = SourceFixture.Create(DecodeFixture(BaselineJpeg), 0, 0);
        using var encoder = new TrickplayPreviewEncoder();
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
            using var fifthDestination = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            Task<PreviewEncodingTelemetry> fifth = encoder.EncodeAsync(
                fixture.Source,
                fifthDestination,
                cancellation.Token);
            Assert.False(fifth.IsCompleted);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifth);
            Assert.Equal(0, fifthDestination.Length);
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

    public static TheoryData<JpegFixture, int, int> ValidCrops => new()
    {
        { JpegFixture.Baseline, 0, 1 },
        { JpegFixture.Baseline, 1, 2 },
        { JpegFixture.Progressive, 0, 1 },
        { JpegFixture.Progressive, 1, 2 },
    };

    private static byte[] DecodeFixture(string fixture)
    {
        string compactFixture = string.Concat(fixture.Where(character => !char.IsWhiteSpace(character)));
        return Convert.FromBase64String(compactFixture);
    }

    private static byte[] GetFixture(JpegFixture fixture)
    {
        return fixture switch
        {
            JpegFixture.Baseline => DecodeFixture(BaselineJpeg),
            JpegFixture.Progressive => DecodeFixture(ProgressiveJpeg),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown JPEG fixture."),
        };
    }

    private static void AssertPixelsAgree(SKBitmap source, SKBitmap preview, int row, int column)
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

    private sealed class SourceFixture : IDisposable
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
            string directoryPath = Path.Combine(Path.GetTempPath(), $"trickplay-encoder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            string sourcePath = Path.Combine(directoryPath, "source-sprite");
            File.WriteAllBytes(sourcePath, sourceBytes);
            var metadata = new TrickplayMetadata(CellWidth, CellHeight, 1_000, 3, 2, 6);
            int frameIndex = (row * metadata.TileWidth) + column;
            FrameSelection selection = FrameSelection.Create(
                metadata,
                frameIndex * TimeSpan.TicksPerSecond);
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

    private sealed class FailingWriteStream : MemoryStream
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

    private sealed class BlockingWriteStream : MemoryStream
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
}
