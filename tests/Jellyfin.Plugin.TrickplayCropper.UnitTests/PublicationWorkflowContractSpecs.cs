using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PublicationWorkflowContractSpecs
{
    private const string PublicationWorkflowRelativePath = ".github/workflows/publish-release.yml";
    private const string CiWorkflowRelativePath = ".github/workflows/ci.yml";
    private const string PreparationWorkflowRelativePath = ".github/workflows/auto-release.yml";
    private const string CheckoutStepName = "Check out repository";
    private const string MergeMethodStepName = "Verify the release pull request merged without a merge commit";
    private const string PublicationStepName = "Publish the stable release from the merged build manifest";

    private static readonly string publication = RepositoryFiles.Read(PublicationWorkflowRelativePath);
    private static readonly string publishJob = WorkflowFiles.ExtractJobSection(publication, "publish");
    private static readonly string ci = RepositoryFiles.Read(CiWorkflowRelativePath);
    private static readonly string preparation = RepositoryFiles.Read(PreparationWorkflowRelativePath);
    private static readonly string[] ciOnlyStepNames = ["Create SHA-256 checksum", "Upload installable package"];

    [Fact]
    public void PublicationTriggersOnlyWhenAPullRequestAgainstMainCloses()
    {
        string triggers = WorkflowFiles.ReadTopLevelBlock(publication, "on:");

        Assert.Matches(@"pull_request:\s+types:\s+(?:-\s+\S+\s+)*-\s+closed\b", triggers);
        Assert.Matches(@"pull_request:[\s\S]*branches:\s+(?:-\s+\S+\s+)*-\s+main\b", triggers);
        Assert.DoesNotMatch(@"(?m)^\s*(?:push|release|schedule|workflow_dispatch|workflow_run):", triggers);
    }

    [Fact]
    public void PublicationRequiresAnInternalMergedPullRequestWithTheExactReleaseTitle()
    {
        string condition = WorkflowFiles.ReadJobCondition(publishJob);

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
    public void PublicationRefusesAMergeCommitBeforeAnyOtherStep()
    {
        KeyValuePair<string, string>[] steps = WorkflowFiles.ReadSteps(publishJob);
        Assert.Equal(MergeMethodStepName, steps[0].Key);

        string guard = steps[0].Value;
        Assert.Contains("MERGE_COMMIT_SHA: ${{ github.event.pull_request.merge_commit_sha }}", guard, StringComparison.Ordinal);
        Assert.Contains(".parents | length", guard, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationChecksOutTheMergedCommitRatherThanThePullRequestHead()
    {
        string checkout = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(publishJob), CheckoutStepName);

        Assert.Contains("ref: ${{ github.event.pull_request.merge_commit_sha }}", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationRunsEveryExistingCiGateStepInOrder()
    {
        string[] gateSteps = WorkflowFiles.ReadSteps(ci)
            .Select(step => step.Key)
            .Except(ciOnlyStepNames)
            .ToArray();

        Assert.NotEmpty(gateSteps);
        Assert.Equal(
            [MergeMethodStepName, .. gateSteps, PublicationStepName],
            WorkflowFiles.ReadSteps(publishJob).Select(step => step.Key).ToArray());
    }

    [Fact]
    public void PublicationCopiesEveryCiGateStepVerbatim()
    {
        KeyValuePair<string, string>[] ciSteps = WorkflowFiles.ReadSteps(ci);
        KeyValuePair<string, string>[] publicationSteps = WorkflowFiles.ReadSteps(publishJob);
        string[] gateSteps = ciSteps
            .Select(step => step.Key)
            .Except(ciOnlyStepNames)
            .Where(name => name != CheckoutStepName)
            .ToArray();

        Assert.NotEmpty(gateSteps);
        Assert.All(
            gateSteps,
            name => Assert.Equal(
                WorkflowFiles.ReadStepBody(ciSteps, name),
                WorkflowFiles.ReadStepBody(publicationSteps, name)));
    }

    [Fact]
    public void PublicationGrantsExactlyTheContentWriteScope()
    {
        Assert.Equal(["contents: write"], WorkflowFiles.ReadPermissionScopes(publication));
    }

    [Fact]
    public void PublicationUsesNoActionThatTheExistingWorkflowsDoNotAlreadyPin()
    {
        string[] used = WorkflowFiles.ReadUsedActions(publication);
        string[] pinned = WorkflowFiles.ReadUsedActions(ci)
            .Concat(WorkflowFiles.ReadUsedActions(preparation))
            .ToArray();

        Assert.NotEmpty(used);
        Assert.All(used, action => Assert.Matches(@"^[^@\s]+@[0-9a-f]{40}$", action));
        Assert.All(used, action => Assert.Contains(action, pinned));
    }

    [Fact]
    public void TheMergedBuildManifestIsTheOnlyVersionAndChangelogSource()
    {
        string approvedManifestPath = WorkflowFiles.ReadEnvValue(preparation, "BUILD_MANIFEST");
        Assert.Equal(approvedManifestPath, WorkflowFiles.ReadEnvValue(publication, "BUILD_MANIFEST"));

        string publish = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(publishJob), PublicationStepName);
        Assert.Contains("jq -r '.version' \"${BUILD_MANIFEST}\"", publish, StringComparison.Ordinal);
        Assert.Contains("jq -r '.changelog' \"${BUILD_MANIFEST}\"", publish, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+\.\d+", publication);
    }

    [Fact]
    public void PublicationCreatesOneStableReleaseTaggedAtTheMergedCommit()
    {
        string publish = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(publishJob), PublicationStepName);

        Assert.Contains("gh release create \"v${version}\" \"${PACKAGE_PATH}\"", publish, StringComparison.Ordinal);
        Assert.Contains("--title \"Trickplay Cropper ${version}\"", publish, StringComparison.Ordinal);
        Assert.Contains("--notes-file \"${RUNNER_TEMP}/release-notes.md\"", publish, StringComparison.Ordinal);
        Assert.Contains("--target \"${MERGE_COMMIT_SHA}\"", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("--draft", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("--prerelease", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationUploadsExactlyTheSoleJprmArtifact()
    {
        string publish = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(publishJob), PublicationStepName);

        Assert.Contains("PACKAGE_PATH: ${{ steps.package.outputs.artifact }}", publish, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(publish, "\"${PACKAGE_PATH}\""));
        Assert.DoesNotMatch(@"gh release (?:create|upload)[^\n]*\*", publish);
        Assert.DoesNotContain(".sha256", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationRetriesReuseTheExistingReleaseAndItsAsset()
    {
        string publish = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(publishJob), PublicationStepName);
        int view = publish.IndexOf("gh release view", StringComparison.Ordinal);
        int upload = publish.IndexOf("gh release upload", StringComparison.Ordinal);
        int create = publish.IndexOf("gh release create", StringComparison.Ordinal);

        Assert.True(view >= 0, "A retry must look for an existing release.");
        Assert.True(upload > view, "An existing release must be reused by uploading into it.");
        Assert.True(create > upload, "A new release must only be created when none exists.");
        Assert.Contains("gh release upload \"v${version}\" \"${PACKAGE_PATH}\" --clobber", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationAddsNoPostBuildIdentityAttestation()
    {
        Assert.DoesNotMatch(@"(?i)attest|provenance|sigstore|cosign|id-token", publication);
    }

    [Fact]
    public void PublicationNeverCommitsGeneratedPackageOutputs()
    {
        Assert.DoesNotMatch(@"git (?:add|commit|push|tag|config)", publishJob);
        Assert.Contains("RUNNER_TEMP", publishJob, StringComparison.Ordinal);
        Assert.Contains("artifacts/", RepositoryFiles.Read(".gitignore"), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyThePublicationWorkflowCanPublishARelease()
    {
        string[] publishing = Directory
            .EnumerateFiles(RepositoryFiles.GetPath(".github/workflows"), "*.y*ml")
            .Where(path => File.ReadAllText(path).Contains("gh release", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryFiles.Root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.Equal([PublicationWorkflowRelativePath], publishing);
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
