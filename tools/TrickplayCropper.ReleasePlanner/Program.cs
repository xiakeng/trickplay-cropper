using TrickplayCropper.ReleasePlanner;

if (args.Length is < 2 or > 4
    || args.Length == 3
    || (args.Length == 4 && args[2] != "--version"))
{
    Console.Error.WriteLine(
        "Usage: TrickplayCropper.ReleasePlanner <build-manifest> <changelog-file> [--version <a.b.c.d>]");
    return 2;
}

try
{
    string manifest = File.ReadAllText(args[0]);
    string changelog = File.ReadAllText(args[1]);
    ReleaseVersion? proposedVersion = args.Length == 4 ? ReleaseVersion.Parse(args[3]) : null;
    ReleasePlan plan = ReleaseManifestPlanner.Plan(manifest, changelog, proposedVersion);
    File.WriteAllText(args[0], plan.UpdatedManifest);
    Console.WriteLine(plan.NextVersion.ToString());
    return 0;
}
catch (Exception error) when (error is ReleasePlanningException or IOException)
{
    Console.Error.WriteLine($"Release planning failed: {error.Message}");
    return 1;
}
