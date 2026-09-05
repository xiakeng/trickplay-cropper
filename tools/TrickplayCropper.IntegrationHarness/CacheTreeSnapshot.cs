using System.Text.RegularExpressions;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Inspects canonical JPEGs without following links or mutating the retained Cache Tree.</summary>
public sealed class CacheTreeSnapshot
{
    private const string CanonicalPath = "^[0-9a-f]{32}/w[0-9]{4,}/s[0-9]{6,}-[0-9a-f]{32}/f[0-9]{10}\\.jpg$";

    /// <summary>Captures a complete canonical tree, rejecting duplicate identities and publication residue.</summary>
    public static async Task<IReadOnlyDictionary<string, byte[]>> ReadAsync(string root, CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (string path in EnumerateFiles(root, cancellationToken))
        {
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!Regex.IsMatch(relative, CanonicalPath, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                || !identities.Add(relative.Split('/')[0] + "/" + Path.GetFileName(relative)))
            {
                throw new InvalidDataException("Cache Tree has a noncanonical file, duplicate identity, or temporary residue.");
            }

            files.Add(relative, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

    /// <summary>Requires precisely the expected canonical paths and the bytes returned by GET.</summary>
    public static async Task<bool> MatchesAsync(string root, IReadOnlyDictionary<string, byte[]> expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            IReadOnlyDictionary<string, byte[]> actual = await ReadAsync(root, cancellationToken).ConfigureAwait(false);
            bool matches = actual.Count == expected.Count && expected.All(entry =>
                actual.TryGetValue(entry.Key, out byte[]? bytes) && entry.Value.AsSpan().SequenceEqual(bytes));
            cancellationToken.ThrowIfCancellationRequested();
            return matches;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        for (DirectoryInfo? parent = new(Path.GetFullPath(root)); parent is not null; parent = parent.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefuseLink(parent.FullName);
        }

        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = RefuseLink(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
                else
                {
                    yield return path;
                }
            }
        }
    }

    private static FileAttributes RefuseLink(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Cache Tree inspection refuses symbolic links.");
        }

        return attributes;
    }
}
