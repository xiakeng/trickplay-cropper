using System.Runtime.ExceptionServices;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Stores Preview Cache Entries beneath Jellyfin temporary storage.
/// </summary>
internal sealed class DiskPreviewCache : IPreviewCache
{
    private const string PluginDirectoryName = "Jellyfin.Plugin.TrickplayCropper";

    private readonly string cacheRoot;
    private readonly PreviewCacheCoordination coordination;
    private readonly StringComparison pathComparison;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskPreviewCache"/> class.
    /// </summary>
    public DiskPreviewCache(IApplicationPaths applicationPaths, TimeProvider timeProvider)
        : this(applicationPaths, timeProvider, new PreviewCacheCoordination())
    {
    }

    /// <summary>
    /// Initializes a cache with an explicit process-local coordination collaborator.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="timeProvider">The source of cleanup time.</param>
    /// <param name="coordination">Coordinates Cache Tree ownership and deterministic boundaries.</param>
    internal DiskPreviewCache(
        IApplicationPaths applicationPaths,
        TimeProvider timeProvider,
        PreviewCacheCoordination coordination)
    {
        cacheRoot = Path.GetFullPath(
            Path.Combine(applicationPaths.TempDirectory, PluginDirectoryName, PreviewIdentity.CacheNamespace));
        this.coordination = coordination;
        pathComparison = coordination.PathComparison;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PreviewCacheResult> GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string finalPath = GetFinalPath(identity);
        return await coordination.ExecuteEntryAsync(
            finalPath,
            token => GetOrCreateOwnedAsync(finalPath, writer, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = timeProvider.GetUtcNow();
        progress.Report(100);
        return Task.CompletedTask;
    }

    private async Task<PreviewCacheResult> GetOrCreateOwnedAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        byte[]? existingContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (existingContent is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PreviewCacheResult(existingContent, PreviewCacheDisposition.Hit, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        return await GenerateAsync(finalPath, writer, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreviewCacheResult> GenerateAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        string temporaryPath = CreateTemporaryPath(finalPath);
        try
        {
            PreviewEncodingTelemetry telemetry = await WriteTemporaryEntryAsync(
                temporaryPath,
                writer,
                cancellationToken).ConfigureAwait(false);
            ValidateCompletedOutput(temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            (ReadOnlyMemory<byte> content, PreviewCacheDisposition disposition) = await PublishAsync(
                finalPath,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            return new PreviewCacheResult(content, disposition, telemetry);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)> PublishAsync(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        byte[]? existingContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (existingContent is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (existingContent, PreviewCacheDisposition.Hit);
        }

        coordination.ObserveBeforePublication();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Move(temporaryPath, finalPath, overwrite: false);
        }
        catch (IOException exception)
        {
            return await ReadWinningPublicationAsync(finalPath, exception, cancellationToken).ConfigureAwait(false);
        }

        coordination.ObserveAfterPublication();
        cancellationToken.ThrowIfCancellationRequested();
        byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (content, PreviewCacheDisposition.Miss);
    }

    private static async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)>
        ReadWinningPublicationAsync(
            string finalPath,
            IOException publicationFailure,
            CancellationToken cancellationToken)
    {
        byte[]? winningContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (winningContent is null)
        {
            ExceptionDispatchInfo.Throw(publicationFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (winningContent, PreviewCacheDisposition.Hit);
    }

    private static async Task<byte[]?> TryReadExistingAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
            return content.Length > 0
                ? content
                : throw new InvalidDataException("The existing Preview Cache Entry is empty.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static async Task<PreviewEncodingTelemetry> WriteTemporaryEntryAsync(
        string temporaryPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        PreviewEncodingTelemetry telemetry = await writer(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return telemetry;
    }

    private static void ValidateCompletedOutput(string temporaryPath)
    {
        var output = new FileInfo(temporaryPath);
        if (!output.Exists || output.Length == 0)
        {
            throw new InvalidDataException("The generated Preview Cache Entry is empty or missing.");
        }
    }

    private string GetFinalPath(PreviewIdentity identity)
    {
        string finalPath = Path.GetFullPath(Path.Combine(cacheRoot, identity.RelativePath));
        string containedRoot = string.Concat(cacheRoot, Path.DirectorySeparatorChar);
        if (!finalPath.StartsWith(containedRoot, pathComparison))
        {
            throw new InvalidDataException("The Preview Cache Entry path escapes the Cache Tree.");
        }

        return finalPath;
    }

    private static string CreateTemporaryPath(string finalPath)
    {
        string directoryPath = Path.GetDirectoryName(finalPath)!;
        string entryName = Path.GetFileNameWithoutExtension(finalPath);
        return Path.Combine(directoryPath, $"{entryName}.{Guid.NewGuid():N}.tmp");
    }

}
