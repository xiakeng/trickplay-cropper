using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ManifestWorkflowContractSpecs
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
        WorkflowFiles.ExtractJobSection(publication, ManifestJobName);

    [Fact]
    public void ManifestJobDependsOnThePublishJob()
    {
        Assert.Contains("needs: publish", manifestJob, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestJobGatesOnTheSameMergedReleasePullRequest()
    {
        string condition = WorkflowFiles.ReadJobCondition(manifestJob);

        Assert.Contains("github.event.pull_request.merged == true", condition, StringComparison.Ordinal);
        Assert.Contains(
            $"github.event.pull_request.title == '{WorkflowFiles.ReadEnvValue(preparation, "RELEASE_TITLE")}'",
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
        string[] scopes = WorkflowFiles.ReadJobPermissionScopes(manifestJob);

        Assert.Equal(["contents: write", "pull-requests: write"], scopes);
    }

    [Fact]
    public void PrerequisitesGuardFailsClosedBeforeAnyMutation()
    {
        KeyValuePair<string, string>[] steps = WorkflowFiles.ReadSteps(manifestJob);
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
        string guard = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), PrerequisitesStepName);

        Assert.Contains(".type == \"pull_request\"", guard, StringComparison.Ordinal);
        Assert.Contains(".type == \"required_status_checks\"", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("required_pull_request_review", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePatAuthorsTheManifestPullRequest()
    {
        string submit = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), SubmitStepName);

        Assert.Contains("GH_TOKEN: ${{ secrets.RELEASE_BOT_PAT }}", submit, StringComparison.Ordinal);
        Assert.Contains("gh pr create", submit, StringComparison.Ordinal);
        Assert.Contains("git push", submit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGithubTokenApprovesAndMergesWithoutThePat()
    {
        string approve = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), ApproveStepName);

        Assert.DoesNotContain("RELEASE_BOT_PAT", approve, StringComparison.Ordinal);
        Assert.Contains("gh pr review", approve, StringComparison.Ordinal);
        Assert.Contains("--approve", approve, StringComparison.Ordinal);
        Assert.Contains("gh pr merge", approve, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMergeUsesNoRulesetBypass()
    {
        string approve = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), ApproveStepName);

        Assert.DoesNotContain("--admin", approve, StringComparison.Ordinal);
        Assert.DoesNotContain("--bypass", approve, StringComparison.Ordinal);
        Assert.Contains("--squash", approve, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePatIsNeverUsedToMerge()
    {
        string approve = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), ApproveStepName);
        string wait = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), WaitStepName);

        Assert.DoesNotContain("RELEASE_BOT_PAT", approve, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"GH_TOKEN:.*RELEASE_BOT_PAT", approve);
        Assert.DoesNotMatch(@"GH_TOKEN:.*RELEASE_BOT_PAT", wait);
    }

    [Fact]
    public void RequiredChecksAreWatchedBeforeTheMerge()
    {
        KeyValuePair<string, string>[] steps = WorkflowFiles.ReadSteps(manifestJob);
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
        string manifestBranch = WorkflowFiles.ReadEnvValue(manifestJob, "MANIFEST_BRANCH");
        string releaseBranch = WorkflowFiles.ReadEnvValue(preparation, "RELEASE_BRANCH");

        Assert.NotEqual(releaseBranch, manifestBranch);
    }

    [Fact]
    public void TheManifestBuilderToolIsUsedToDeriveTheEntry()
    {
        Assert.Contains("TrickplayCropper.ManifestBuilder", manifestJob, StringComparison.Ordinal);
        Assert.Contains("gh release download", manifestJob, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExistingManifestIsSeededFromOriginMainBeforeTheBuilderRuns()
    {
        string build = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), "Build the repository manifest entry");

        Assert.Contains("git show \"origin/main:${MANIFEST_FILE}\"", build, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePushAuthenticatesAsThePatRatherThanTheGithubToken()
    {
        string submit = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), SubmitStepName);

        Assert.Contains("gh auth setup-git", submit, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN: ${{ secrets.RELEASE_BOT_PAT }}", submit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBotMergedManifestChangeCannotRecursivelyOpenAReleasePullRequest()
    {
        string approve = WorkflowFiles.ReadStepBody(
            WorkflowFiles.ReadSteps(manifestJob), ApproveStepName);

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
}
