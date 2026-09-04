using TrickplayCropper.ReleasePlanner;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: TrickplayCropper.ReleasePlanner <build-manifest> <changelog-file>");
    return 2;
}

string manifestPath = args[0];
string changelogPath = args[1];

try
{
    string manifest = File.ReadAllText(manifestPath);
    string changelog = File.ReadAllText(changelogPath);
    ReleasePlan plan = ReleaseManifestPlanner.Plan(manifest, changelog);
    File.WriteAllText(manifestPath, plan.UpdatedManifest);
    Console.WriteLine(plan.NextVersion.ToString());
    return 0;
}
catch (Exception error) when (error is ReleasePlanningException or IOException)
{
    Console.Error.WriteLine($"Release planning failed: {error.Message}");
    return 1;
}
