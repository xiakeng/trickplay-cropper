using System.Text.Json.Nodes;
using TrickplayCropper.ReleasePlanner;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ReleasePlannerSpecs
{
    private const string SyntheticManifest =
        """
        {
          "name": "Trickplay Cropper",
          "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
          "version": "1.0.0.0",
          "targetAbi": "10.11.0.0",
          "framework": "net9.0",
          "overview": "Return authenticated Trickplay Previews from Jellyfin-owned Source Sprites.",
          "description": "Provides authenticated, single-frame Trickplay Previews from Jellyfin-owned Source Sprites.",
          "category": "General",
          "owner": "xiakeng",
          "artifacts": [
            "Jellyfin.Plugin.TrickplayCropper.dll"
          ],
          "changelog": "Initial Trickplay Cropper v1 release."
        }
        """;

    [Fact]
    public void ParseReadsEveryFourComponentValue()
    {
        ReleaseVersion version = ReleaseVersion.Parse("1.2.3.4");

        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Build);
        Assert.Equal(4, version.Revision);
        Assert.Equal("1.2.3.4", version.ToString());
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0.0.0")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData("1.0.0.")]
    [InlineData("1.0.x.0")]
    [InlineData("1.0.-1.0")]
    public void ParseRejectsAnythingButFourNonNegativeComponents(string value)
    {
        Assert.Throws<ReleasePlanningException>(() => ReleaseVersion.Parse(value));
    }

    [Theory]
    [InlineData("1.0.0.0", "1.0.1.0")]
    [InlineData("1.2.3.4", "1.2.4.4")]
    [InlineData("2.9.0.7", "2.9.1.7")]
    public void NextRoutineIncrementsOnlyTheThirdComponent(string current, string expected)
    {
        Assert.Equal(expected, ReleaseVersion.Parse(current).NextRoutine().ToString());
    }

    [Fact]
    public void NextRoutineRejectsThirdComponentOverflow()
    {
        ReleaseVersion version = new(1, 0, int.MaxValue, 0);

        Assert.Throws<ReleasePlanningException>(() => version.NextRoutine());
    }

    [Fact]
    public void PlanRewritesOnlyTheVersionAndChangelogLines()
    {
        ReleasePlan plan = ReleaseManifestPlanner.Plan(SyntheticManifest, "- second release entry");

        Assert.Equal("1.0.1.0", plan.NextVersion.ToString());

        string[] before = SyntheticManifest.Split('\n');
        string[] after = plan.UpdatedManifest.Split('\n');
        Assert.Equal(before.Length, after.Length);

        int[] changed = before
            .Select((line, index) => (line, index))
            .Where(item => item.line != after[item.index])
            .Select(item => item.index)
            .ToArray();

        Assert.Equal(2, changed.Length);
        Assert.Contains("\"version\": \"1.0.1.0\",", after[changed[0]], StringComparison.Ordinal);
        Assert.Contains("\"changelog\": \"- second release entry\"", after[changed[1]], StringComparison.Ordinal);
    }

    [Fact]
    public void PlanPreservesEveryOtherManifestField()
    {
        ReleasePlan plan = ReleaseManifestPlanner.Plan(SyntheticManifest, "- entry");

        JsonObject before = JsonNode.Parse(SyntheticManifest)!.AsObject();
        JsonObject after = JsonNode.Parse(plan.UpdatedManifest)!.AsObject();

        Assert.Equal("1.0.1.0", after["version"]!.GetValue<string>());
        Assert.Equal("- entry", after["changelog"]!.GetValue<string>());
        foreach (string key in before.Select(pair => pair.Key))
        {
            if (key is "version" or "changelog")
            {
                continue;
            }

            Assert.Equal(before[key]!.ToJsonString(), after[key]!.ToJsonString());
        }
    }

    [Fact]
    public void PlanKeepsAnExplicitlyProposedVersionInsteadOfTheRoutineBump()
    {
        ReleasePlan plan = ReleaseManifestPlanner.Plan(
            SyntheticManifest,
            "- entry",
            ReleaseVersion.Parse("2.0.0.0"));

        Assert.Equal("2.0.0.0", plan.NextVersion.ToString());
        Assert.Equal("2.0.0.0", JsonNode.Parse(plan.UpdatedManifest)!["version"]!.GetValue<string>());
        Assert.Equal("- entry", JsonNode.Parse(plan.UpdatedManifest)!["changelog"]!.GetValue<string>());
    }

    [Fact]
    public void PlanKeepsAMultilineChangelogOnOneManifestLine()
    {
        ReleasePlan plan = ReleaseManifestPlanner.Plan(
            SyntheticManifest,
            "- first line\n- second line\n");

        JsonObject after = JsonNode.Parse(plan.UpdatedManifest)!.AsObject();
        Assert.Equal("- first line\n- second line", after["changelog"]!.GetValue<string>());
        Assert.Equal(SyntheticManifest.Split('\n').Length, plan.UpdatedManifest.Split('\n').Length);
    }

    [Fact]
    public void PlanWritesTheChangelogAsReadableJsonRatherThanHtmlEscapes()
    {
        const string Changelog = "- fix A & B + C caf\u00e9";

        ReleasePlan plan = ReleaseManifestPlanner.Plan(SyntheticManifest, Changelog);

        Assert.Contains("\"changelog\": \"- fix A & B + C caf\u00e9\"", plan.UpdatedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0026", plan.UpdatedManifest, StringComparison.Ordinal);
        Assert.Equal(Changelog, JsonNode.Parse(plan.UpdatedManifest)!["changelog"]!.GetValue<string>());
    }

    [Fact]
    public void PlanRejectsAManifestWithoutAVersion()
    {
        const string Manifest = """{ "changelog": "x" }""";

        Assert.Throws<ReleasePlanningException>(() => ReleaseManifestPlanner.Plan(Manifest, "- entry"));
    }

    [Fact]
    public void PlanRejectsAManifestWithoutAChangelog()
    {
        const string Manifest = """{ "version": "1.0.0.0" }""";

        Assert.Throws<ReleasePlanningException>(() => ReleaseManifestPlanner.Plan(Manifest, "- entry"));
    }

    [Fact]
    public void PlanRejectsDuplicateVersionFields()
    {
        const string Manifest =
            """
            {
              "version": "1.0.0.0",
              "changelog": "x",
              "version": "2.0.0.0"
            }
            """;

        Assert.Throws<ReleasePlanningException>(() => ReleaseManifestPlanner.Plan(Manifest, "- entry"));
    }
}
