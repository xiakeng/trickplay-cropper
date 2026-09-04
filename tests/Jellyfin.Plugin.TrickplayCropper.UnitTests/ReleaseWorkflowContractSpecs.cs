using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class ReleaseWorkflowContractSpecs
{
    private const string WorkflowRelativePath = ".github/workflows/auto-release.yml";

    private static readonly string repositoryRoot = FindRepositoryRoot();

    private static readonly string workflow = ReadWorkflow();

    [Fact]
    public void TheWorkflowTriggersOnEveryPushToMain()
    {
        Assert.Matches(
            @"on:\s+push:\s+branches:\s+(?:-\s+\S+\s+)*-\s+main\b",
            workflow);
    }

    [Fact]
    public void TheWorkflowGrantsOnlyTheTokenScopesItNeeds()
    {
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("pull-requests: write", workflow, StringComparison.Ordinal);
        Assert.Contains("administration: read", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryActionIsFirstPartyAndPinnedToACommitSha()
    {
        string[] uses = UsesActionRegex().Matches(workflow)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(uses);
        Assert.All(uses, action => Assert.Matches(@"^actions/[^@\s]+@[0-9a-f]{40}$", action));
    }

    [Fact]
    public void TheWorkflowAuthorsTheFixedBranchPullRequestWithTheGithubToken()
    {
        Assert.Contains("GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}", workflow, StringComparison.Ordinal);
        Assert.Contains("auto-release new version", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr create", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr edit", workflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_BRANCH: auto-release", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowFailsClosedOnIncompatiblePrerequisites()
    {
        Assert.Contains("set -euo pipefail", workflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_BOT_PAT", workflow, StringComparison.Ordinal);
        Assert.Contains("permissions.push", workflow, StringComparison.Ordinal);
        Assert.Contains("can_approve_pull_request_reviews", workflow, StringComparison.Ordinal);
        Assert.Contains("rules/branches/main", workflow, StringComparison.Ordinal);
        Assert.Contains("required_pull_request_review", workflow, StringComparison.Ordinal);
        Assert.Contains("required_status_checks", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowUsesTheBuildManifestAsTheSingleVersionSource()
    {
        Assert.Contains("TrickplayCropper.ReleasePlanner", workflow, StringComparison.Ordinal);
        Assert.Contains("src/Jellyfin.Plugin.TrickplayCropper/build.yaml", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.0.0", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.1.0", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowSpansTheChangelogFromThePreviousTagOrTheRoot()
    {
        Assert.Contains("git tag --list", workflow, StringComparison.Ordinal);
        Assert.Contains("git log", workflow, StringComparison.Ordinal);
        Assert.Contains("..HEAD", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowCommitsOnlyTheBuildManifestAndNeverPublishes()
    {
        Assert.Contains("git add \"${BUILD_MANIFEST}\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git add -A", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git add .", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git add -u", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--tags", workflow, StringComparison.Ordinal);
        Assert.All(
            GitTagRegex().Matches(workflow).Select(match => match.Value),
            usage => Assert.Equal("git tag --list", usage));
    }

    [Fact]
    public void TheWorkflowKeepsGeneratedOutputsOutsideTheRepository()
    {
        Assert.Contains("RUNNER_TEMP", workflow, StringComparison.Ordinal);
        Assert.Contains("${RUNNER_TEMP}/changelog.md", workflow, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"uses:\s*(\S+)")]
    private static partial Regex UsesActionRegex();

    [GeneratedRegex(@"git tag(?:\s+--list)?")]
    private static partial Regex GitTagRegex();

    private static string ReadWorkflow()
    {
        string path = Path.Combine(
            repositoryRoot,
            WorkflowRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
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
