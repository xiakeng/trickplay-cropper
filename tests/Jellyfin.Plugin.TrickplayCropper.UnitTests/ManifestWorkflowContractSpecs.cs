using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class ManifestWorkflowContractSpecs
{
    private const string PublicationWorkflowRelativePath = ".github/workflows/publish-release.yml";
    private const string PreparationWorkflowRelativePath = ".github/workflows/auto-release.yml";
    private const string ManifestJobName = "manifest";
    private const string PrerequisitesStepName = "Verify manifest prerequisites";
    private const string SubmitStepName = "Submit the manifest pull request";
    private const string ApproveStepName = "Approve and merge the manifest pull request";
    private const string WaitStepName = "Wait for required checks on the manifest pull request";

    private static readonly string publication = RepositoryFiles.Read(PublicationWorkflowRelativePath);
    private static readonly string preparation = RepositoryFiles.Read(PreparationWorkflowRelativePath);
    private static readonly string manifestJob =
        PublicationWorkflowContractSpecs.ExtractJobSection(publication, ManifestJobName);

    [Fact]
    public void ManifestJobDependsOnThePublishJob()
    {
        Assert.Contains("needs: publish", manifestJob, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestJobGatesOnTheSameMergedReleasePullRequest()
    {
        string condition = ReadJobCondition(manifestJob);

        Assert.Contains("github.event.pull_request.merged == true", condition, StringComparison.Ordinal);
        Assert.Contains(
            $"github.event.pull_request.title == '{ReadEnvValue(preparation, "RELEASE_TITLE")}'",
            condition,
            StringComparison.Ordinal);
        Assert.Contains(
            "github.event.pull_request.head.repo.full_name == github.repository",
            condition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestJobGrantsContentAndPullRequestWriteScopes()
    {
        string[] scopes = ReadJobPermissionScopes(manifestJob);

        Assert.Equal(["contents: write", "pull-requests: write"], scopes);
    }

    [Fact]
    public void PrerequisitesGuardFailsClosedBeforeAnyMutation()
    {
        KeyValuePair<string, string>[] steps = ReadSteps(manifestJob);
        Assert.Equal(PrerequisitesStepName, steps[0].Key);

        string guard = steps[0].Value;
        Assert.Contains("RELEASE_BOT_PAT", guard, StringComparison.Ordinal);
        Assert.Contains("permissions.push", guard, StringComparison.Ordinal);
        Assert.Contains("can_approve_pull_request_reviews", guard, StringComparison.Ordinal);
        Assert.Contains("rules/branches/main", guard, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void PrerequisitesGuardUsesTheRulesApiRuleTypeNames()
    {
        string guard = ReadBody(ReadSteps(manifestJob), PrerequisitesStepName);

        Assert.Contains(".type == \"pull_request\"", guard, StringComparison.Ordinal);
        Assert.Contains(".type == \"required_status_checks\"", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("required_pull_request_review", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePatAuthorsTheManifestPullRequest()
    {
        string submit = ReadBody(ReadSteps(manifestJob), SubmitStepName);

        Assert.Contains("GH_TOKEN: ${{ secrets.RELEASE_BOT_PAT }}", submit, StringComparison.Ordinal);
        Assert.Contains("gh pr create", submit, StringComparison.Ordinal);
        Assert.Contains("git push", submit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGithubTokenApprovesAndMergesWithoutThePat()
    {
        string approve = ReadBody(ReadSteps(manifestJob), ApproveStepName);

        Assert.DoesNotContain("RELEASE_BOT_PAT", approve, StringComparison.Ordinal);
        Assert.Contains("gh pr review", approve, StringComparison.Ordinal);
        Assert.Contains("--approve", approve, StringComparison.Ordinal);
        Assert.Contains("gh pr merge", approve, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMergeUsesNoRulesetBypass()
    {
        string approve = ReadBody(ReadSteps(manifestJob), ApproveStepName);

        Assert.DoesNotContain("--admin", approve, StringComparison.Ordinal);
        Assert.DoesNotContain("--bypass", approve, StringComparison.Ordinal);
        Assert.Contains("--squash", approve, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePatIsNeverUsedToMerge()
    {
        string approve = ReadBody(ReadSteps(manifestJob), ApproveStepName);
        string wait = ReadBody(ReadSteps(manifestJob), WaitStepName);

        Assert.DoesNotContain("RELEASE_BOT_PAT", approve, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"GH_TOKEN:.*RELEASE_BOT_PAT", approve);
        Assert.DoesNotMatch(@"GH_TOKEN:.*RELEASE_BOT_PAT", wait);
    }

    [Fact]
    public void RequiredChecksAreWatchedBeforeTheMerge()
    {
        KeyValuePair<string, string>[] steps = ReadSteps(manifestJob);
        int waitIndex = Array.FindIndex(steps, step => step.Key == WaitStepName);
        int approveIndex = Array.FindIndex(steps, step => step.Key == ApproveStepName);

        Assert.True(waitIndex >= 0, "The manifest job must wait for required checks.");
        Assert.True(approveIndex > waitIndex, "The merge must follow the check wait.");

        string wait = steps[waitIndex].Value;
        Assert.Contains("gh pr checks", wait, StringComparison.Ordinal);
        Assert.Contains("--watch", wait, StringComparison.Ordinal);
    }

    [Fact]
    public void TheManifestFileIsAtTheRepositoryRoot()
    {
        Assert.Contains("MANIFEST_FILE: manifest.json", manifestJob, StringComparison.Ordinal);
    }

    [Fact]
    public void TheManifestBranchIsDistinctFromTheReleaseBranch()
    {
        string manifestBranch = ReadEnvValue(manifestJob, "MANIFEST_BRANCH");
        string releaseBranch = ReadEnvValue(preparation, "RELEASE_BRANCH");

        Assert.NotEqual(releaseBranch, manifestBranch);
    }

    [Fact]
    public void TheManifestBuilderToolIsUsedToDeriveTheEntry()
    {
        Assert.Contains("TrickplayCropper.ManifestBuilder", manifestJob, StringComparison.Ordinal);
        Assert.Contains("gh release download", manifestJob, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBotMergedManifestChangeCannotRecursivelyOpenAReleasePullRequest()
    {
        string approve = ReadBody(ReadSteps(manifestJob), ApproveStepName);

        Assert.Contains("gh pr merge", approve, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_BOT_PAT", approve, StringComparison.Ordinal);

        string preparationTriggers = WorkflowFiles.ReadTopLevelBlock(preparation, "on:");
        Assert.Matches(@"push:\s+branches:\s+(?:-\s+\S+\s+)*-\s+main\b", preparationTriggers);
    }

    [Fact]
    public void NoWorkflowConfiguresARulesetBypassActor()
    {
        Assert.DoesNotMatch(@"(?i)bypass.?actor|bypass_actors", publication);
    }

    private static string[] ReadJobPermissionScopes(string jobSection)
    {
        string[] lines = jobSection.Replace("\r\n", "\n").Split('\n');
        int permStart = Array.FindIndex(
            lines, line => line.TrimEnd().EndsWith("permissions:", StringComparison.Ordinal));
        Assert.True(permStart >= 0, "The manifest job must declare a permissions block.");

        int permIndent = lines[permStart].Length - lines[permStart].TrimStart().Length;
        List<string> scopes = [];

        for (int index = permStart + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;
            if (indent <= permIndent)
            {
                break;
            }

            string entry = line.Trim();
            int colon = entry.IndexOf(':');
            Assert.True(colon > 0, $"Unexpected permissions entry: '{line}'.");
            scopes.Add($"{entry[..colon].Trim()}: {entry[(colon + 1)..].Trim()}");
        }

        return scopes.Order(StringComparer.Ordinal).ToArray();
    }

    private static string ReadBody(KeyValuePair<string, string>[] steps, string name)
    {
        return steps.Single(step => step.Key == name).Value;
    }

    private static string ReadEnvValue(string workflow, string name)
    {
        string prefix = $"{name}:";
        string line = workflow
            .Split('\n')
            .Select(candidate => candidate.Trim())
            .Single(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));

        return line[prefix.Length..].Trim();
    }

    private static string ReadJobCondition(string workflow)
    {
        Match match = JobConditionRegex().Match(workflow);
        Assert.True(match.Success, "The manifest job must gate itself with a job-level if condition.");

        return match.Groups["condition"].Value;
    }

    private static KeyValuePair<string, string>[] ReadSteps(string workflow)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        List<KeyValuePair<string, string>> steps = [];

        for (int index = 0; index < lines.Length; index++)
        {
            Match name = StepNameRegex().Match(lines[index]);
            if (!name.Success)
            {
                continue;
            }

            int indent = name.Groups[1].Value.Length;
            System.Text.StringBuilder body = new();
            for (int line = index + 1; line < lines.Length; line++)
            {
                string text = lines[line];
                int textIndent = text.Length - text.TrimStart().Length;
                if (text.Trim().Length > 0 && textIndent <= indent)
                {
                    break;
                }

                body.Append(text.Length > indent + 2 ? text[(indent + 2)..] : string.Empty).Append('\n');
            }

            steps.Add(new(name.Groups[2].Value, body.ToString().TrimEnd('\n')));
        }

        return steps.ToArray();
    }

    [GeneratedRegex(@"^(\s*)-\s*name:\s*(.+?)\s*$")]
    private static partial Regex StepNameRegex();

    [GeneratedRegex(@"(?m)^(?<indent>[ \t]+)if: >-$\n(?<condition>(?:\k<indent>[ \t].*\n)+)")]
    private static partial Regex JobConditionRegex();
}
