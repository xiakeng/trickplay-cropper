using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class BusinessDocsWorkflowContractSpecs
{
    private const string WorkflowRelativePath = ".github/workflows/business-docs-analysis.yml";
    private const string FindStepName = "Find the pending business documentation analysis issue";
    private const string CreateStepName = "Create the pending business documentation analysis issue";

    private static readonly string workflow = RepositoryFiles.Read(WorkflowRelativePath);

    private const string ApprovedIssueTitle = "Review and maintain business documentation";

    private const string ApprovedIssueBody =
        "Using the base commit recorded in `docs/business/README.md`, analyze subsequent code "
        + "changes and maintain the relevant documentation under `docs/business/`.\n\n"
        + "If no documentation updates are needed, close this issue directly.";

    [Fact]
    public void TheWorkflowTriggersOnlyOnPullRequestsClosedAgainstMain()
    {
        string triggers = WorkflowFiles.ReadTopLevelBlock(workflow, "on:");

        Assert.Equal(
            """
              pull_request:
                types:
                  - closed
                branches:
                  - main
            """,
            triggers.TrimEnd('\n'));
    }

    [Fact]
    public void TheJobRunsOnlyForMergedPullRequests()
    {
        string job = WorkflowFiles.ExtractJobSection(workflow, "maintain-analysis-issue");

        string condition = WorkflowFiles.ReadJobCondition(job);

        Assert.Contains("github.event.pull_request.merged == true", condition, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowGrantsExactlyTheTokenScopesItNeeds()
    {
        Assert.Equal(1, WorkflowFiles.CountHeaderLines(workflow, "permissions:"));

        Assert.Equal(["issues: write"], WorkflowFiles.ReadPermissionScopes(workflow));
    }

    [Fact]
    public void TheWorkflowAuthenticatesWithTheGithubTokenOnly()
    {
        Assert.Equal(
            "${{ secrets.GITHUB_TOKEN }}",
            WorkflowFiles.ReadEnvValue(workflow, "GH_TOKEN"));
    }

    [Fact]
    public void TheLookupSearchesOnlyOpenIssuesCarryingTheDedicatedLabel()
    {
        Assert.Equal("docs:business-analysis", WorkflowFiles.ReadEnvValue(workflow, "ANALYSIS_LABEL"));

        string lookup = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(workflow), FindStepName);

        Assert.Contains("gh issue list", lookup, StringComparison.Ordinal);
        Assert.Contains("--label \"${ANALYSIS_LABEL}\"", lookup, StringComparison.Ordinal);
        Assert.Contains("--state open", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("--search", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("--title", lookup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingOpenAnalysisIssueIsLeftCompletelyUntouched()
    {
        Assert.Contains(
            "if: steps.pending.outputs.existing == ''",
            workflow,
            StringComparison.Ordinal);

        Assert.DoesNotContain("gh issue edit", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue comment", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue close", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue reopen", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue lock", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue pin", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue transfer", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowCreatesOneStandaloneIssueWithTheFixedTitle()
    {
        Assert.Equal(ApprovedIssueTitle, WorkflowFiles.ReadEnvValue(workflow, "ANALYSIS_TITLE"));

        string creation = WorkflowFiles.ReadStepBody(WorkflowFiles.ReadSteps(workflow), CreateStepName);

        Assert.Contains("gh issue create", creation, StringComparison.Ordinal);
        Assert.Contains("--title \"${ANALYSIS_TITLE}\"", creation, StringComparison.Ordinal);
        Assert.Contains("--body-file \"${RUNNER_TEMP}/analysis-issue-body.md\"", creation, StringComparison.Ordinal);
        Assert.Contains("--label \"${ANALYSIS_LABEL}\"", creation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCreatedIssueCarriesExactlyTheApprovedFixedBody()
    {
        Assert.Equal(ApprovedIssueBody + "\n", ReadIssueBodyHeredoc(workflow));
    }

    [Fact]
    public void TheWorkflowPerformsOnlyTheLookupAndTheCreation()
    {
        string[] commands = GhCommandRegex()
            .Matches(workflow)
            .Select(match => match.Value)
            .ToArray();

        Assert.Equal(["gh issue list", "gh issue create"], commands);
    }

    [Fact]
    public void TheWorkflowNeverChecksOutTheRepositoryOrInspectsTheAnalysisBase()
    {
        Assert.Empty(WorkflowFiles.ReadUsedActions(workflow));
        Assert.DoesNotContain("git ", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentRunsSerializeInsteadOfCancellingEachOther()
    {
        Assert.Matches(
            @"concurrency:\s+group:\s+\S+\s+cancel-in-progress:\s+false",
            workflow);
    }

    private static string ReadIssueBodyHeredoc(string workflow)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, line => line.Contains("<<'BODY'", StringComparison.Ordinal));
        Assert.True(start >= 0, "The workflow must write the fixed issue body from a quoted heredoc.");

        int indent = lines[start].Length - lines[start].TrimStart().Length;
        List<string> body = [];

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim() == "BODY")
            {
                return string.Join('\n', body) + "\n";
            }

            body.Add(line.Length >= indent ? line[indent..] : string.Empty);
        }

        Assert.Fail("The quoted heredoc body is not terminated.");
        return string.Empty;
    }

    [GeneratedRegex(@"gh [a-z][a-z-]* [a-z][a-z-]*")]
    private static partial Regex GhCommandRegex();
}
