using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed partial class BusinessDocumentationSpecs
{
    private const string BusinessRoot = "docs/business";

    private static readonly string[] participantsChapters =
    [
        "client.md",
        "jellyfin-server.md",
        "frame-probe.md",
        "preview-request.md",
        "cache-tree.md",
        "cleanup-task.md",
    ];

    private static readonly string[] lifecycleChapters =
    [
        "source-resolution.md",
        "frame-probe.md",
        "frame-selection.md",
        "preview-generation.md",
        "preview-cache.md",
        "cache-coordination.md",
        "response-contract.md",
        "scheduled-cleanup.md",
    ];

    private static readonly string[] designChapters =
    [
        "authorization-and-visibility.md",
        "resolution-exactness.md",
        "frame-determinism.md",
        "probe-isolation.md",
        "cache-identity-and-freshness.md",
        "concurrency-safety.md",
        "resource-bounds.md",
    ];

    [Fact]
    public void BusinessDocumentationSetContainsTheApprovedChapterInventory()
    {
        string[] expected =
        [
            "README.md",
            "participants/README.md",
            .. participantsChapters.Select(chapter => $"participants/{chapter}"),
            "lifecycle/README.md",
            .. lifecycleChapters.Select(chapter => $"lifecycle/{chapter}"),
            "design/README.md",
            .. designChapters.Select(chapter => $"design/{chapter}"),
        ];

        string[] actual = Directory
            .EnumerateFiles(RepositoryFiles.GetPath(BusinessRoot), "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryFiles.GetPath(BusinessRoot), path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expected.Order(StringComparer.Ordinal).ToArray(),
            actual);
    }

    [Fact]
    public void EveryRelativeLinkResolvesInsideTheRepository()
    {
        foreach ((string file, string markdown) in ReadEveryDocumentedFile())
        {
            foreach (Match link in MarkdownLinkRegex().Matches(markdown))
            {
                string target = link.Groups["target"].Value;
                if (target.Length == 0
                    || target.StartsWith('#')
                    || Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(RepositoryFiles.GetPath(file))!, target));
                Assert.True(
                    File.Exists(resolved),
                    $"{file} links to '{target}', which does not exist.");
                Assert.False(
                    resolved.StartsWith(
                        Path.GetFullPath(RepositoryFiles.GetPath("docs/spec")),
                        StringComparison.Ordinal),
                    $"{file} must not link the legacy specification area through '{target}'.");
            }
        }
    }

    [Fact]
    public void OnlyLifecycleChaptersCarryCodeAnchors()
    {
        foreach (string chapter in lifecycleChapters)
        {
            string markdown = RepositoryFiles.Read($"{BusinessRoot}/lifecycle/{chapter}");
            int anchorsHeading = markdown.IndexOf("## Anchors", StringComparison.Ordinal);

            Assert.True(
                anchorsHeading >= 0,
                $"lifecycle/{chapter} must end with an '## Anchors' section.");

            string anchors = markdown[anchorsHeading..];
            Assert.Contains('`', anchors);
            Assert.DoesNotMatch(LineAnchorRegex(), anchors);
            Assert.False(
                SectionHeadingRegex().IsMatch(anchors["## Anchors".Length..]),
                $"lifecycle/{chapter} must end with its '## Anchors' section.");
        }

        string[] chaptersWithoutAnchors =
        [
            .. participantsChapters.Select(chapter => $"participants/{chapter}"),
            .. designChapters.Select(chapter => $"design/{chapter}"),
        ];

        foreach (string chapter in chaptersWithoutAnchors)
        {
            Assert.DoesNotContain(
                "## Anchors",
                RepositoryFiles.Read($"{BusinessRoot}/{chapter}"),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryMermaidDiagramDeclaresATopDownShape()
    {
        int diagrams = 0;

        foreach ((string file, string markdown) in ReadEveryDocumentedFile())
        {
            foreach (string diagram in ExtractMermaidDiagrams(markdown))
            {
                diagrams++;
                string declaration = diagram
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .First();

                Assert.True(
                    DiagramDeclarationRegex().IsMatch(declaration),
                    $"{file} declares '{declaration}', which is not a supported top-down shape.");
            }
        }

        Assert.True(diagrams > 0, "The business documentation set must contain Mermaid diagrams.");
    }

    private static IEnumerable<(string File, string Markdown)> ReadEveryDocumentedFile()
    {
        yield return ("README.md", RepositoryFiles.Read("README.md"));

        foreach (string path in Directory
            .EnumerateFiles(RepositoryFiles.GetPath(BusinessRoot), "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            yield return (Path.GetRelativePath(RepositoryFiles.Root, path)
                .Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path));
        }
    }

    private static List<string> ExtractMermaidDiagrams(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        List<string> diagrams = [];

        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != "```mermaid")
            {
                continue;
            }

            int closing = Array.FindIndex(lines, index + 1, line => line.Trim() == "```");
            Assert.True(closing > index, "A Mermaid diagram is missing its closing fence.");
            diagrams.Add(string.Join('\n', lines[(index + 1)..closing]));
            index = closing;
        }

        return diagrams;
    }

    [GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]*)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?i)(:\d+|line\s+\d)")]
    private static partial Regex LineAnchorRegex();

    [GeneratedRegex(@"(?m)^##\s")]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"^(flowchart (TD|TB|LR)|sequenceDiagram)\b")]
    private static partial Regex DiagramDeclarationRegex();
}
