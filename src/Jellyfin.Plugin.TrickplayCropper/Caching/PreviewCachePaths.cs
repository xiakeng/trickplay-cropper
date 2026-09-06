using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Defines the plugin-owned Cache Tree and rejects unsafe filesystem paths.
/// </summary>
internal sealed class PreviewCachePaths
{
    private const string PluginDirectoryName = "Jellyfin.Plugin.TrickplayCropper";

    private readonly StringComparison pathComparison;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewCachePaths"/> class.
    /// </summary>
    /// <param name="temporaryDirectory">The host temporary directory.</param>
    /// <param name="pathComparison">The platform path comparison.</param>
    public PreviewCachePaths(string temporaryDirectory, StringComparison pathComparison)
    {
        PluginRoot = Path.GetFullPath(Path.Combine(temporaryDirectory, PluginDirectoryName));
        CacheRoot = Path.Combine(PluginRoot, PreviewIdentity.CacheNamespace);
        this.pathComparison = pathComparison;
    }

    /// <summary>Gets the owned Preview Cache Tree root.</summary>
    public string CacheRoot { get; }

    /// <summary>Gets the plugin-owned directory root.</summary>
    public string PluginRoot { get; }

    /// <summary>Builds and validates the final path for a cache identity.</summary>
    /// <param name="identity">The stable cache identity.</param>
    /// <returns>The canonical final entry path.</returns>
    public string GetFinalPath(PreviewIdentity identity)
    {
        string finalPath = Path.GetFullPath(Path.Combine(CacheRoot, identity.RelativePath));
        string containedRoot = string.Concat(CacheRoot, Path.DirectorySeparatorChar);
        if (!finalPath.StartsWith(containedRoot, pathComparison))
        {
            throw new InvalidDataException("The Preview Cache Entry path escapes the Cache Tree.");
        }

        return finalPath;
    }

    /// <summary>Rejects reparse points along a request-owned path.</summary>
    /// <param name="path">The path to validate.</param>
    public void EnsureRequestPathIsSafe(string path)
    {
        ThrowIfReparsePoint(PluginRoot);
        string relativePath = Path.GetRelativePath(PluginRoot, path);
        string currentPath = PluginRoot;
        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            ThrowIfReparsePoint(currentPath);
        }
    }

    /// <summary>Determines whether an existing path is a reparse point.</summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns><see langword="true"/> when the path is a reparse point.</returns>
    public static bool IsReparsePoint(string path)
    {
        FileAttributes? attributes = GetExistingAttributes(path);
        return attributes.HasValue
            && (attributes.Value & FileAttributes.ReparsePoint) != 0;
    }

    private static void ThrowIfReparsePoint(string path)
    {
        FileAttributes? attributes = GetExistingAttributes(path);
        if (attributes.HasValue
            && (attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"The Cache Tree path is a reparse point: {path}");
        }
    }

    private static FileAttributes? GetExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
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
}
