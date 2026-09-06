using System.Diagnostics;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Captures the identity and ownership scope of one cleanup candidate.
/// </summary>
internal abstract record CleanupCandidate(
    string Path,
    long Length,
    long LastWriteTimeUtcTicks)
{
    private const string FinalExtension = ".jpg";
    private const string TemporaryExtension = ".tmp";
    private const int FrameDigits = 10;
    private const int TemporaryTokenLength = 32;

    public static CleanupCandidate? Capture(string filePath, DateTime cleanupStartedUtc)
    {
        string canonicalPath = System.IO.Path.GetFullPath(filePath);
        string fileName = System.IO.Path.GetFileName(canonicalPath);
        string? lockPath = null;
        bool requiresExclusiveLease = false;
        if (IsFinalEntryName(fileName))
        {
            lockPath = canonicalPath;
        }
        else if (TryGetTemporaryFinalName(fileName, out string? finalName))
        {
            lockPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(canonicalPath)!, finalName!);
        }
        else if (fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal))
        {
            requiresExclusiveLease = true;
        }
        else
        {
            return null;
        }

        var file = new FileInfo(canonicalPath);
        file.Refresh();
        if (!file.Exists || file.LastWriteTimeUtc > cleanupStartedUtc)
        {
            return null;
        }

        return requiresExclusiveLease
            ? new ExclusiveCleanupCandidate(canonicalPath, file.Length, file.LastWriteTimeUtc.Ticks)
            : new EntryCleanupCandidate(
                canonicalPath,
                lockPath ?? throw new UnreachableException("An entry cleanup candidate requires a lock path."),
                file.Length,
                file.LastWriteTimeUtc.Ticks);
    }

    private static bool IsFinalEntryName(string fileName)
    {
        if (fileName.Length != 1 + FrameDigits + FinalExtension.Length
            || fileName[0] != 'f'
            || !fileName.EndsWith(FinalExtension, StringComparison.Ordinal))
        {
            return false;
        }

        return fileName.AsSpan(1, FrameDigits).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool TryGetTemporaryFinalName(string fileName, out string? finalName)
    {
        finalName = null;
        if (!fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal))
        {
            return false;
        }

        string withoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
        int separatorIndex = withoutExtension.LastIndexOf('.');
        if (separatorIndex < 0)
        {
            return false;
        }

        string entryName = withoutExtension[..separatorIndex];
        string token = withoutExtension[(separatorIndex + 1)..];
        string candidateFinalName = string.Concat(entryName, FinalExtension);
        if (!IsFinalEntryName(candidateFinalName)
            || token.Length != TemporaryTokenLength
            || !Guid.TryParseExact(token, "N", out _)
            || !string.Equals(token, token.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        finalName = candidateFinalName;
        return true;
    }
}

internal sealed record EntryCleanupCandidate(
    string Path,
    string LockPath,
    long Length,
    long LastWriteTimeUtcTicks)
    : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);

internal sealed record ExclusiveCleanupCandidate(
    string Path,
    long Length,
    long LastWriteTimeUtcTicks)
    : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);
