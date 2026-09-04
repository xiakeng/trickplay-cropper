namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string GetPath(string relativePath)
    {
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string Read(string relativePath)
    {
        return File.ReadAllText(GetPath(relativePath));
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrickplayCropper.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Trickplay Cropper repository root.");
    }
}
