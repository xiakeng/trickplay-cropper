using System.Runtime.ExceptionServices;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Reads, generates, atomically publishes, and buffers one owned Preview Cache Entry.
/// </summary>
internal sealed class DiskPreviewCacheEntryStore
{
    private readonly PreviewCacheCoordination coordination;
    private readonly PreviewCachePaths paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskPreviewCacheEntryStore"/> class.
    /// </summary>
    /// <param name="paths">The cache path boundary.</param>
    /// <param name="coordination">The shared entry coordination.</param>
    public DiskPreviewCacheEntryStore(PreviewCachePaths paths, PreviewCacheCoordination coordination)
    {
        this.paths = paths;
        this.coordination = coordination;
    }

    /// <summary>
    /// Reads an existing entry or generates, publishes, and buffers a new entry.
    /// </summary>
    /// <param name="identity">The stable cache identity.</param>
    /// <param name="writer">The encoder that writes a temporary entry.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The buffered cache result.</returns>
    public async Task<PreviewCacheResult> GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string finalPath = paths.GetFinalPath(identity);
        return await coordination.ExecuteEntryAsync(
            finalPath,
            token => GetOrCreateOwnedAsync(finalPath, writer, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreviewCacheResult> GetOrCreateOwnedAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        byte[]? existingContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (existingContent is not null)
        {
            return new PreviewCacheResult(existingContent, PreviewCacheDisposition.Hit, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        paths.EnsureRequestPathIsSafe(finalPath);
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
            paths.EnsureRequestPathIsSafe(temporaryPath);
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
            DeleteTemporaryEntryIfSafe(temporaryPath);
        }
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)> PublishAsync(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        byte[]? existingContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (existingContent is not null)
        {
            return (existingContent, PreviewCacheDisposition.Hit);
        }

        try
        {
            PublishTemporaryEntry(finalPath, temporaryPath, cancellationToken);
        }
        catch (IOException exception)
        {
            return await ReadWinningPublicationAsync(finalPath, exception, cancellationToken).ConfigureAwait(false);
        }

        byte[] content = await ReadPublishedEntryAsync(finalPath, cancellationToken).ConfigureAwait(false);
        return (content, PreviewCacheDisposition.Miss);
    }

    private void PublishTemporaryEntry(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        coordination.ObserveBeforePublication();
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureRequestPathIsSafe(finalPath);
        paths.EnsureRequestPathIsSafe(temporaryPath);
        File.Move(temporaryPath, finalPath, overwrite: false);
    }

    private async Task<byte[]> ReadPublishedEntryAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        coordination.ObserveAfterPublication();
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureRequestPathIsSafe(finalPath);
        byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
        paths.EnsureRequestPathIsSafe(finalPath);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)>
        ReadWinningPublicationAsync(
            string finalPath,
            IOException publicationFailure,
            CancellationToken cancellationToken)
    {
        byte[]? winningContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (winningContent is null)
        {
            ExceptionDispatchInfo.Throw(publicationFailure);
        }

        return (winningContent, PreviewCacheDisposition.Hit);
    }

    private async Task<byte[]?> TryReadSafeExistingAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        paths.EnsureRequestPathIsSafe(finalPath);
        byte[]? content = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (content is not null)
        {
            paths.EnsureRequestPathIsSafe(finalPath);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return content;
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

    private void DeleteTemporaryEntryIfSafe(string temporaryPath)
    {
        try
        {
            paths.EnsureRequestPathIsSafe(temporaryPath);
            File.Delete(temporaryPath);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static string CreateTemporaryPath(string finalPath)
    {
        string directoryPath = Path.GetDirectoryName(finalPath)!;
        string entryName = Path.GetFileNameWithoutExtension(finalPath);
        return Path.Combine(directoryPath, $"{entryName}.{Guid.NewGuid():N}.tmp");
    }
}
