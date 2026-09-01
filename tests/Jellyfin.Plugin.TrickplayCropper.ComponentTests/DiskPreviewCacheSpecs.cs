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
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            await File.WriteAllBytesAsync(finalPath, originalContent, CancellationToken.None);
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);

            PreviewCacheResult result = await cache.GetOrCreateAsync(
                identity,
                (_, _) => throw new InvalidOperationException("An existing entry must not be regenerated."),
                CancellationToken.None);
            await File.WriteAllBytesAsync(finalPath, [9, 9, 9], CancellationToken.None);

            Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
            Assert.Equal(originalContent, result.Content.ToArray());
            Assert.Null(result.EncodingTelemetry);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RegeneratesPreviewCacheEntryThatDisappearsAfterHit()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
            byte[] regeneratedContent = [0xFF, 0xD8, 3, 4, 0xFF, 0xD9];
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            await File.WriteAllBytesAsync(finalPath, originalContent, CancellationToken.None);
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);
            PreviewCacheResult hit = await cache.GetOrCreateAsync(
                identity,
                (_, _) => throw new InvalidOperationException("An existing entry must not be regenerated."),
                CancellationToken.None);
            File.Delete(finalPath);

            PreviewCacheResult miss = await cache.GetOrCreateAsync(
                identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(regeneratedContent, cancellationToken);
                    return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
                },
                CancellationToken.None);

            Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
            Assert.Equal(PreviewCacheDisposition.Miss, miss.Disposition);
            Assert.Equal(regeneratedContent, miss.Content.ToArray());
            Assert.Equal(regeneratedContent, await File.ReadAllBytesAsync(finalPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ServesCompleteOutsideWinnerWithoutOverwritingIt()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            byte[] generated = [1, 2, 3, 4];
            byte[] outsideWinner = [0xFF, 0xD8, 0xFF, 0xD9];
            string? temporaryPath = null;
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);

            PreviewCacheResult result = await cache.GetOrCreateAsync(
                identity,
                async (destination, cancellationToken) =>
                {
                    temporaryPath = Assert.IsType<FileStream>(destination).Name;
                    await destination.WriteAsync(generated, cancellationToken);
                    await File.WriteAllBytesAsync(finalPath, outsideWinner, cancellationToken);
                    return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
                },
                CancellationToken.None);

            Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
            Assert.Equal(outsideWinner, result.Content.ToArray());
            Assert.Equal(outsideWinner, await File.ReadAllBytesAsync(finalPath, CancellationToken.None));
            Assert.Null(result.EncodingTelemetry);
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(finalPath)!, "*.tmp"));
            string completedTemporaryPath = Assert.IsType<string>(temporaryPath);
            Assert.Equal(Path.GetDirectoryName(finalPath), Path.GetDirectoryName(completedTemporaryPath));
            string temporaryName = Path.GetFileName(completedTemporaryPath);
            Assert.StartsWith("f0000000000.", temporaryName, StringComparison.Ordinal);
            Assert.EndsWith(".tmp", temporaryName, StringComparison.Ordinal);
            string randomName = temporaryName[12..^4];
            Assert.True(Guid.TryParseExact(randomName, "N", out _));
            Assert.Equal(randomName.ToLowerInvariant(), randomName);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsEmptyGeneratedPreviewCacheEntryWithoutPublishingIt()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetOrCreateAsync(
                identity,
                (_, _) => Task.FromResult(
                    new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2))),
                CancellationToken.None));

            Assert.False(File.Exists(finalPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(finalPath)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenGenerationFails()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);

            await Assert.ThrowsAsync<IOException>(() => cache.GetOrCreateAsync(
                identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                    throw new IOException("Simulated destination failure.");
                },
                CancellationToken.None));

            Assert.False(File.Exists(finalPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(finalPath)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenCancelledBeforePublication()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            PreviewIdentity identity = CreateIdentity();
            string finalPath = GetFinalPath(temporaryDirectory, identity);
            var cache = new DiskPreviewCache(CreateApplicationPaths(temporaryDirectory), TimeProvider.System);
            using var cancellation = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(
                identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                    cancellation.Cancel();
                    return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
                },
                cancellation.Token));

            Assert.False(File.Exists(finalPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(finalPath)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
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

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trickplay-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetFinalPath(string temporaryDirectory, PreviewIdentity identity)
    {
        return Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            "preview-v1",
            identity.RelativePath);
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
}
