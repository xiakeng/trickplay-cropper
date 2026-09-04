using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class PublicationWorkflowContractSpecs
{
    private const string PublicationWorkflowRelativePath = ".github/workflows/publish-release.yml";
    private const string CiWorkflowRelativePath = ".github/workflows/ci.yml";
    private const string PreparationWorkflowRelativePath = ".github/workflows/auto-release.yml";
    private const string CheckoutStepName = "Check out repository";
    private const string MergeMethodStepName = "Verify the release pull request merged without a merge commit";
    private const string PublicationStepName = "Publish the stable release from the merged build manifest";

    private static readonly string publication = RepositoryFiles.Read(PublicationWorkflowRelativePath);
    private static readonly string ci = RepositoryFiles.Read(CiWorkflowRelativePath);
    private static readonly string preparation = RepositoryFiles.Read(PreparationWorkflowRelativePath);
    private static readonly string[] ciOnlyStepNames = ["Create SHA-256 checksum", "Upload installable package"];

    [Fact]
    public void PublicationTriggersOnlyWhenAPullRequestAgainstMainCloses()
    {
        string triggers = ReadTopLevelBlock(publication, "on:");

        Assert.Matches(@"pull_request:\s+types:\s+(?:-\s+\S+\s+)*-\s+closed\b", triggers);
        Assert.Matches(@"pull_request:[\s\S]*branches:\s+(?:-\s+\S+\s+)*-\s+main\b", triggers);
        Assert.DoesNotMatch(@"(?m)^\s*(?:push|release|schedule|workflow_dispatch|workflow_run):", triggers);
    }

    [Fact]
    public void PublicationRequiresAnInternalMergedPullRequestWithTheExactReleaseTitle()
    {
        string condition = ReadJobCondition(publication);

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
    public void PublicationRefusesAMergeCommitBeforeAnyOtherStep()
    {
        KeyValuePair<string, string>[] steps = ReadSteps(publication);
        Assert.Equal(MergeMethodStepName, steps[0].Key);

        string guard = steps[0].Value;
        Assert.Contains("MERGE_COMMIT_SHA: ${{ github.event.pull_request.merge_commit_sha }}", guard, StringComparison.Ordinal);
        Assert.Contains(".parents | length", guard, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationChecksOutTheMergedCommitRatherThanThePullRequestHead()
    {
        string checkout = BodyOf(ReadSteps(publication), CheckoutStepName);

        Assert.Contains("ref: ${{ github.event.pull_request.merge_commit_sha }}", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationRunsEveryExistingCiGateStepInOrder()
    {
        string[] gateSteps = ReadSteps(ci)
            .Select(step => step.Key)
            .Except(ciOnlyStepNames)
            .ToArray();

        Assert.NotEmpty(gateSteps);
        Assert.Equal(
            [MergeMethodStepName, .. gateSteps, PublicationStepName],
            ReadSteps(publication).Select(step => step.Key).ToArray());
    }

    [Fact]
    public void PublicationCopiesEveryCiGateStepVerbatim()
    {
        KeyValuePair<string, string>[] ciSteps = ReadSteps(ci);
        KeyValuePair<string, string>[] publicationSteps = ReadSteps(publication);
        string[] gateSteps = ciSteps
            .Select(step => step.Key)
            .Except(ciOnlyStepNames)
            .Where(name => name != CheckoutStepName)
            .ToArray();

        Assert.NotEmpty(gateSteps);
        Assert.All(gateSteps, name => Assert.Equal(BodyOf(ciSteps, name), BodyOf(publicationSteps, name)));
    }

    [Fact]
    public void PublicationGrantsExactlyTheContentWriteScope()
    {
        Assert.Equal(
            1,
            publication.Split('\n').Count(line => line.Trim().StartsWith("permissions:", StringComparison.Ordinal)));

        string[] scopes = ReadTopLevelBlock(publication, "permissions:")
            .Split('\n')
            .Select(scope => scope.Trim())
            .Where(scope => scope.Length > 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["contents: write"], scopes);
    }

    [Fact]
    public void PublicationUsesNoActionThatTheExistingWorkflowsDoNotAlreadyPin()
    {
        string[] used = ReadUsedActions(publication);
        string[] pinned = ReadUsedActions(ci).Concat(ReadUsedActions(preparation)).ToArray();

        Assert.NotEmpty(used);
        Assert.All(used, action => Assert.Matches(@"^[^@\s]+@[0-9a-f]{40}$", action));
        Assert.All(used, action => Assert.Contains(action, pinned));
    }

    [Fact]
    public void TheMergedBuildManifestIsTheOnlyVersionAndChangelogSource()
    {
        Assert.Equal(BuildManifestPath, ReadEnvValue(publication, "BUILD_MANIFEST"));

        string publish = BodyOf(ReadSteps(publication), PublicationStepName);
        Assert.Contains("jq -r '.version' \"${BUILD_MANIFEST}\"", publish, StringComparison.Ordinal);
        Assert.Contains("jq -r '.changelog' \"${BUILD_MANIFEST}\"", publish, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+\.\d+", publication);
    }

    [Fact]
    public void PublicationCreatesOneStableReleaseTaggedAtTheMergedCommit()
    {
        string publish = BodyOf(ReadSteps(publication), PublicationStepName);

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
        string publish = BodyOf(ReadSteps(publication), PublicationStepName);

        Assert.Contains("PACKAGE_PATH: ${{ steps.package.outputs.artifact }}", publish, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(publish, "\"${PACKAGE_PATH}\""));
        Assert.DoesNotMatch(@"gh release (?:create|upload)[^\n]*\*", publish);
        Assert.DoesNotContain(".sha256", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationRetriesReuseTheExistingReleaseAndItsAsset()
    {
        string publish = BodyOf(ReadSteps(publication), PublicationStepName);
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
        Assert.DoesNotMatch(@"attest|provenance|sigstore|cosign|id-token", publication);
    }

    [Fact]
    public void PublicationNeverCommitsGeneratedPackageOutputs()
    {
        Assert.DoesNotMatch(@"git (?:add|commit|push|tag|config)", publication);
        Assert.Contains("RUNNER_TEMP", publication, StringComparison.Ordinal);
        Assert.Contains("artifacts/", RepositoryFiles.Read(".gitignore"), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyThePublicationWorkflowCanPublishARelease()
    {
        string[] publishing = Directory
            .EnumerateFiles(RepositoryFiles.GetPath(".github/workflows"), "*.yml")
            .Where(path => File.ReadAllText(path).Contains("gh release", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryFiles.Root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.Equal([PublicationWorkflowRelativePath], publishing);
    }

    private static string BuildManifestPath => ReadEnvValue(preparation, "BUILD_MANIFEST");

    private static string BodyOf(KeyValuePair<string, string>[] steps, string name)
    {
        return steps.Single(step => step.Key == name).Value;
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        for (int index = text.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
        Assert.True(match.Success, "The publication job must gate itself with a job-level if condition.");

        return match.Groups["condition"].Value;
    }

    private static string ReadTopLevelBlock(string workflow, string header)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd() == header);
        Assert.True(start >= 0, $"The publication workflow must declare a top-level '{header}' block.");

        StringBuilder block = new();
        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim().Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            block.Append(line).Append('\n');
        }

        return block.ToString();
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
            StringBuilder body = new();
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

    private static string[] ReadUsedActions(string workflow)
    {
        return UsesActionRegex()
            .Matches(workflow)
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    [GeneratedRegex(@"uses:\s*(\S+)")]
    private static partial Regex UsesActionRegex();

    [GeneratedRegex(@"^(\s*)-\s*name:\s*(.+?)\s*$")]
    private static partial Regex StepNameRegex();

    [GeneratedRegex(@"(?m)^(?<indent>[ \t]+)if: >-$\n(?<condition>(?:\k<indent>[ \t].*\n)+)")]
    private static partial Regex JobConditionRegex();
}
