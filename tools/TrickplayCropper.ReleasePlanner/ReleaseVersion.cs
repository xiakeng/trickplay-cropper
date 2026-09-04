using System.Globalization;

namespace TrickplayCropper.ReleasePlanner;

public readonly record struct ReleaseVersion
{
    public ReleaseVersion(int major, int minor, int build, int revision)
    {
        if (major < 0 || minor < 0 || build < 0 || revision < 0)
        {
            throw new ReleasePlanningException(
                $"Version components must be non-negative, got {major}.{minor}.{build}.{revision}.");
        }

        Major = major;
        Minor = minor;
        Build = build;
        Revision = revision;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Build { get; }

    public int Revision { get; }

    public static ReleaseVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string[] components = value.Split('.');
        if (components.Length != 4)
        {
            throw new ReleasePlanningException(
                $"Version must contain exactly four numeric components, got '{value}'.");
        }

        int[] numbers = new int[4];
        for (int index = 0; index < 4; index++)
        {
            if (!int.TryParse(
                    components[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[index]))
            {
                throw new ReleasePlanningException(
                    $"Version component '{components[index]}' is not a non-negative integer in '{value}'.");
            }
        }

        return new ReleaseVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public ReleaseVersion NextRoutine()
    {
        if (Build == int.MaxValue)
        {
            throw new ReleasePlanningException(
                $"Cannot increment the third component of {ToString()}.");
        }

        return new ReleaseVersion(Major, Minor, Build + 1, Revision);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Build}.{Revision}");
}
