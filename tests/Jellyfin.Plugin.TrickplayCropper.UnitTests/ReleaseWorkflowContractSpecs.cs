using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class ReleaseWorkflowContractSpecs
{
    private const string WorkflowRelativePath = ".github/workflows/auto-release.yml";

    private static readonly string workflow = RepositoryFiles.Read(WorkflowRelativePath);

    [Fact]
    public void TheWorkflowTriggersOnEveryPushToMain()
    {
        Assert.Matches(
            @"on:\s+push:\s+branches:\s+(?:-\s+\S+\s+)*-\s+main\b",
            workflow);
    }

    [Fact]
    public void TheWorkflowGrantsExactlyTheTokenScopesItNeeds()
    {
        int permissionBlocks = workflow
            .Split('\n')
            .Count(line => line.Trim().StartsWith("permissions:", StringComparison.Ordinal));
        Assert.Equal(1, permissionBlocks);

        string[] scopes = ReadTopLevelPermissions()
            .Select(scope => $"{scope.Key}: {scope.Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["administration: read", "contents: write", "pull-requests: write"],
            scopes);
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
    public void TheWorkflowFailsClosedOnIncompatiblePrerequisitesBeforeAnyMutation()
    {
        int guardStart = workflow.IndexOf("Verify release prerequisites", StringComparison.Ordinal);
        int guardEnd = workflow.IndexOf("Set up .NET", StringComparison.Ordinal);
        Assert.True(guardStart >= 0 && guardEnd > guardStart, "The prerequisites guard step is missing.");

        string guard = workflow[guardStart..guardEnd];
        Assert.Contains("RELEASE_BOT_PAT", guard, StringComparison.Ordinal);
        Assert.Contains("permissions.push", guard, StringComparison.Ordinal);
        Assert.Contains("can_approve_pull_request_reviews", guard, StringComparison.Ordinal);
        Assert.Contains("rules/branches/main", guard, StringComparison.Ordinal);
        Assert.Contains("required_pull_request_review", guard, StringComparison.Ordinal);
        Assert.Contains("required_status_checks", guard, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard, StringComparison.Ordinal);

        Assert.True(
            workflow.IndexOf("git checkout -B", StringComparison.Ordinal) > guardEnd,
            "Branch mutation must follow the prerequisites guard.");
        Assert.True(
            workflow.IndexOf("gh pr create", StringComparison.Ordinal) > guardEnd,
            "Pull request creation must follow the prerequisites guard.");
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
    public void TheWorkflowPreservesAnOpenPullRequestVersionWhileRefreshingTheChangelog()
    {
        Assert.Contains("--state open", workflow, StringComparison.Ordinal);
        Assert.Contains("--version", workflow, StringComparison.Ordinal);
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

    private static Dictionary<string, string> ReadTopLevelPermissions()
    {
        string[] lines = workflow.Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd('\r') == "permissions:");
        Assert.True(start >= 0, "The workflow must declare a top-level permissions block.");

        Dictionary<string, string> scopes = new(StringComparer.Ordinal);
        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            int colon = entry.IndexOf(':');
            Assert.True(colon > 0, $"Unexpected permissions entry: '{line}'.");
            scopes[entry[..colon].Trim()] = entry[(colon + 1)..].Trim();
        }

        return scopes;
    }

    [GeneratedRegex(@"uses:\s*(\S+)")]
    private static partial Regex UsesActionRegex();

    [GeneratedRegex(@"git tag(?:\s+--list)?")]
    private static partial Regex GitTagRegex();
}
