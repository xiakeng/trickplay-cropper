using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Defines the plugin-owned Cache Tree and rejects unsafe filesystem paths.
/// </summary>
internal sealed class PreviewCachePaths
{
    private const string PluginDirectoryName = "Jellyfin.Plugin.TrickplayCropper";

    private readonly StringComparison pathComparison;

    public PreviewCachePaths(string temporaryDirectory, StringComparison pathComparison)
    {
        PluginRoot = Path.GetFullPath(Path.Combine(temporaryDirectory, PluginDirectoryName));
        CacheRoot = Path.Combine(PluginRoot, PreviewIdentity.CacheNamespace);
        this.pathComparison = pathComparison;
    }

    public string CacheRoot { get; }

    public string PluginRoot { get; }

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
