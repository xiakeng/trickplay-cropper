using TrickplayCropper.PackageValidator;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: TrickplayCropper.PackageValidator <package.zip> [build-manifest]");
    return 2;
}

var manifestPath = args.Length == 2
    ? args[1]
    : Path.Combine("src", "Jellyfin.Plugin.TrickplayCropper", "build.yaml");

try
{
    PackageValidator.Validate(args[0], manifestPath);
}
catch (Exception error)
{
    Console.Error.WriteLine($"Package validation failed: {error.Message}");
    return 1;
}

Console.WriteLine($"Package validation passed: {args[0]}");
return 0;
