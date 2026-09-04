using TrickplayCropper.ManifestBuilder;

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: TrickplayCropper.ManifestBuilder <build-manifest> <zip-path> <manifest-path> <source-url>");
    return 2;
}

try
{
    string buildManifest = File.ReadAllText(args[0]);
    string? existingManifest = File.Exists(args[2]) ? File.ReadAllText(args[2]) : null;
    ManifestPlan plan = RepositoryManifestBuilder.Build(buildManifest, args[1], existingManifest, args[3]);
    File.WriteAllText(args[2], plan.UpdatedManifest + Environment.NewLine);
    Console.WriteLine(plan.Version);
    return 0;
}
catch (Exception error) when (error is ManifestBuildingException or IOException)
{
    Console.Error.WriteLine($"Manifest building failed: {error.Message}");
    return 1;
}
