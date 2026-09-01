using System.Reflection;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class DiskPreviewCacheSpecs
{
    [Fact]
    public async Task BuffersExistingPreviewCacheEntryBeforeReturning()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, originalContent, CancellationToken.None);

        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("An existing entry must not be regenerated."),
            CancellationToken.None);
        await File.WriteAllBytesAsync(fixture.FinalPath, [9, 9, 9], CancellationToken.None);

        Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
        Assert.Equal(originalContent, result.Content.ToArray());
        Assert.Null(result.EncodingTelemetry);
    }

    [Fact]
    public async Task RegeneratesPreviewCacheEntryThatDisappearsAfterHit()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        byte[] regeneratedContent = [0xFF, 0xD8, 3, 4, 0xFF, 0xD9];
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, originalContent, CancellationToken.None);
        PreviewCacheResult hit = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("An existing entry must not be regenerated."),
            CancellationToken.None);
        File.Delete(fixture.FinalPath);

        PreviewCacheResult miss = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(regeneratedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
        Assert.Equal(PreviewCacheDisposition.Miss, miss.Disposition);
        Assert.Equal(regeneratedContent, miss.Content.ToArray());
        Assert.Equal(
            regeneratedContent,
            await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
    }

    [Fact]
    public async Task RetainsSingleEntryOwnerWhileAWaiterCancels()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] generatedContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        Task<PreviewCacheResult> owner = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                ownerStarted.SetResult();
                await releaseOwner.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);
        await ownerStarted.Task;
        using var waiterCancellation = new CancellationTokenSource();
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("A same-entry waiter must not encode concurrently."),
            waiterCancellation.Token);

        waiterCancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        }
        finally
        {
            releaseOwner.TrySetResult();
        }

        PreviewCacheResult result = await owner;

        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
    }

    [Fact]
    public async Task ServesCompleteOutsideWinnerWithoutOverwritingIt()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        byte[] generated = [1, 2, 3, 4];
        byte[] outsideWinner = [0xFF, 0xD8, 0xFF, 0xD9];
        string? temporaryPath = null;
        var encodingTelemetry = new PreviewEncodingTelemetry(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(2));

        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
                temporaryPath = Assert.Single(Directory.EnumerateFiles(directoryPath, "*.tmp"));
                await destination.WriteAsync(generated, cancellationToken);
                await File.WriteAllBytesAsync(fixture.FinalPath, outsideWinner, cancellationToken);
                return encodingTelemetry;
            },
            CancellationToken.None);

        Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
        Assert.Equal(outsideWinner, result.Content.ToArray());
        Assert.Equal(
            outsideWinner,
            await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        Assert.Equal(encodingTelemetry, result.EncodingTelemetry);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
        string completedTemporaryPath = Assert.IsType<string>(temporaryPath);
        Assert.Equal(Path.GetDirectoryName(fixture.FinalPath), Path.GetDirectoryName(completedTemporaryPath));
        string temporaryName = Path.GetFileName(completedTemporaryPath);
        Assert.StartsWith("f0000000000.", temporaryName, StringComparison.Ordinal);
        Assert.EndsWith(".tmp", temporaryName, StringComparison.Ordinal);
        string randomName = temporaryName[12..^4];
        Assert.True(Guid.TryParseExact(randomName, "N", out _));
        Assert.Equal(randomName.ToLowerInvariant(), randomName);
    }

    [Fact]
    public async Task RejectsEmptyGeneratedPreviewCacheEntryWithoutPublishingIt()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => Task.FromResult(
                new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2))),
            CancellationToken.None));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenGenerationFails()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

        await Assert.ThrowsAsync<IOException>(() => fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                throw new IOException("Simulated destination failure.");
            },
            CancellationToken.None));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenCancelledBeforePublication()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                cancellation.Cancel();
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            cancellation.Token));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    private static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
    {
        IApplicationPaths paths = DispatchProxy.Create<IApplicationPaths, ApplicationPathsSpecs>();
        ((ApplicationPathsSpecs)(object)paths).TemporaryDirectory = temporaryDirectory;
        return paths;
    }

    private static PreviewIdentity CreateIdentity()
    {
        return new PreviewIdentity(
            "0123456789abcdef0123456789abcdef",
            "\"0123456789abcdef0123456789abcdef-f0000000000\"",
            Path.Combine(
                "3f728b7b4aa54f65b488a6029edb6725",
                "w0320",
                "s000000-0123456789abcdef0123456789abcdef",
                "f0000000000.jpg"));
    }

    public class ApplicationPathsSpecs : DispatchProxy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationPathsSpecs"/> class.
        /// </summary>
        public ApplicationPathsSpecs()
        {
        }

        public string TemporaryDirectory { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name == "get_TempDirectory"
                ? TemporaryDirectory
                : throw new InvalidOperationException($"Unexpected application-paths call: {targetMethod.Name}.");
        }
    }

    private sealed class TemporaryCacheFixture : IDisposable
    {
        private readonly string temporaryDirectory;

        private TemporaryCacheFixture(
            string temporaryDirectory,
            PreviewIdentity identity,
            string finalPath,
            DiskPreviewCache cache)
        {
            this.temporaryDirectory = temporaryDirectory;
            Identity = identity;
            FinalPath = finalPath;
            Cache = cache;
        }

        public DiskPreviewCache Cache { get; }

        public string FinalPath { get; }

        public PreviewIdentity Identity { get; }

        public static TemporaryCacheFixture Create()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            PreviewIdentity identity = CreateIdentity();
            string finalPath = Path.Combine(
                temporaryDirectory,
                "Jellyfin.Plugin.TrickplayCropper",
                "preview-v1",
                identity.RelativePath);
            var cache = new DiskPreviewCache(
                CreateApplicationPaths(temporaryDirectory),
                TimeProvider.System);
            return new TemporaryCacheFixture(temporaryDirectory, identity, finalPath, cache);
        }

        public void Dispose()
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
