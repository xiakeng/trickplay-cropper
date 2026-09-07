using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class BusinessDocumentationWorkflowContractSpecs
{
    private const string WorkflowRelativePath = ".github/workflows/business-docs-analysis.yml";

    private static readonly string workflow = RepositoryFiles.Read(WorkflowRelativePath);

    [Fact]
    public void WorkflowRunsOnlyAfterAPullRequestMergesToMain()
    {
        string triggers = WorkflowFiles.ReadTopLevelBlock(workflow, "on:");

        Assert.Matches(@"pull_request:\s+types:\s+(?:-\s+\S+\s+)*-\s+closed\b", triggers);
        Assert.Matches(@"pull_request:[\s\S]*branches:\s+(?:-\s+\S+\s+)*-\s+main\b", triggers);
        Assert.Contains("github.event.pull_request.merged == true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingIssueKeepsBusinessMaintenanceSeparateFromCodeMapIncrements()
    {
        Assert.Contains(
            "Using the base commit recorded in `docs/business/README.md`, analyze subsequent code changes",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Treat Business Documentation and Code Maps as independent documentation surfaces.",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "process every Code Maps increment recorded in this issue's comments",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMergedPullRequestQueuesOneIdempotentCodeMapIncrement()
    {
        Assert.Contains("code-maps-analysis-pr:${PR_NUMBER}", workflow, StringComparison.Ordinal);
        Assert.Contains("gh issue view \"${ANALYSIS_ISSUE}\" --json body,comments", workflow, StringComparison.Ordinal);
        Assert.Contains("grep -Fqx \"${marker}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("gh issue comment \"${ANALYSIS_ISSUE}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ github.event.pull_request.html_url }}", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Verify affected paths, symbols, responsibilities, relationships, and test entry points",
            workflow,
            StringComparison.Ordinal);
    }
}
