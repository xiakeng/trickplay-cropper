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
    public void EveryMermaidDiagramSatisfiesTheApprovedShapeRules()
    {
        // These structural checks guard the approved sizing constraints in the editor's
        // loop. Two review activities stay human: rendering every diagram and reviewing
        // its rendered dimensions during development, and the one-time visual check of
        // GitHub's own renderer (its cross-origin viewscreen iframe), which no local
        // browser session can complete.
        const int MaxNodesPerRank = 4;
        // The approved constraint is "about eight ranks"; the ceiling carries the approved
        // tolerance the prototype was measured against before the set was approved.
        const int MaxRanks = 10;
        const int MaxLeftToRightNodes = 6;
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
                    $"{file} declares '{declaration}', which is not a supported diagram shape.");

                if (declaration.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
                {
                    // A sequence diagram has no ranks; the sizing rules target flowcharts.
                    continue;
                }

                if (declaration.StartsWith("flowchart LR", StringComparison.OrdinalIgnoreCase))
                {
                    // Left-to-right is reserved for a short chain that fits the reading column.
                    Assert.True(
                        FlowchartNodeCount(diagram) <= MaxLeftToRightNodes,
                        $"{file} declares a left-to-right flowchart with more than {MaxLeftToRightNodes} nodes.");
                    continue;
                }

                MermaidFlow flow = ParseFlowchart(diagram);

                Assert.True(
                    flow.RankWidth <= MaxNodesPerRank,
                    $"{file} has a rank with {flow.RankWidth} nodes; keep ranks to about {MaxNodesPerRank}.");
                Assert.True(
                    flow.Depth <= MaxRanks,
                    $"{file} is {flow.Depth} ranks deep; split the view instead of stretching it.");

                foreach (string shared in flow.TerminalsSharedAcrossRanks)
                {
                    Assert.Fail(
                        $"{file} feeds terminal '{shared}' from more than one rank; give each rank its own exit.");
                }
            }
        }

        Assert.True(diagrams > 0, "The business documentation set must contain Mermaid diagrams.");
    }

    private static int FlowchartNodeCount(string diagram)
        => ParseFlowchart(diagram).Nodes.Count;

    private static MermaidFlow ParseFlowchart(string diagram)
    {
        List<string> lines = diagram
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        HashSet<string> subgraphIds = CollectSubgraphIds(lines);
        Dictionary<string, string> subgraphEntries = MapSubgraphEntries(lines, subgraphIds);

        Dictionary<string, int> ranks = [];
        Dictionary<string, List<string>> edgesFrom = [];
        Dictionary<string, HashSet<int>> incomingSourceRanks = [];
        HashSet<string> nodes = [];

        foreach (string line in lines)
        {
            if (IsFlowchartDirective(line))
            {
                continue;
            }

            foreach ((string from, string to) in ParseFlowchartEdges(line))
            {
                string source = ResolveNode(subgraphEntries, from);
                string target = ResolveNode(subgraphEntries, to);
                if (source.Length == 0
                    || target.Length == 0
                    || subgraphIds.Contains(source)
                    || subgraphIds.Contains(target))
                {
                    continue;
                }

                if (!edgesFrom.TryGetValue(source, out List<string>? targets))
                {
                    targets = [];
                    edgesFrom[source] = targets;
                }

                targets.Add(target);
                nodes.Add(source);
                nodes.Add(target);

                int sourceRank = ranks.GetValueOrDefault(source, 0);
                ranks[target] = Math.Max(ranks.GetValueOrDefault(target, 0), sourceRank + 1);
                if (!incomingSourceRanks.TryGetValue(target, out HashSet<int>? sourceRanks))
                {
                    sourceRanks = [];
                    incomingSourceRanks[target] = sourceRanks;
                }

                sourceRanks.Add(sourceRank);
            }
        }

        foreach (string node in nodes)
        {
            ranks.TryAdd(node, 0);
        }

        int width = ranks.Values.GroupBy(rank => rank).Max(group => group.Count());
        return new MermaidFlow(
            nodes,
            width,
            ranks.Values.Max() + 1,
            FindTerminalsSharedAcrossRanks(ranks, edgesFrom, incomingSourceRanks));
    }

    private static HashSet<string> CollectSubgraphIds(List<string> lines)
    {
        HashSet<string> ids = [];
        foreach (string line in lines)
        {
            Match match = SubgraphDeclarationRegex().Match(line);
            if (match.Success)
            {
                ids.Add(match.Groups["id"].Value);
            }
        }

        return ids;
    }

    private static Dictionary<string, string> MapSubgraphEntries(
        List<string> lines,
        HashSet<string> subgraphIds)
    {
        // An edge aimed at a subgraph lands on its first inner node for layout purposes.
        Dictionary<string, string> entries = [];
        List<string> stack = [];

        foreach (string line in lines)
        {
            Match match = SubgraphDeclarationRegex().Match(line);
            if (match.Success)
            {
                stack.Add(match.Groups["id"].Value);
                continue;
            }

            if (line == "end")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            if (stack.Count == 0 || IsFlowchartDirective(line))
            {
                continue;
            }

            string sanitized = FlowchartLabelRegex().Replace(line, match => match.Value[0].ToString());
            string? firstNode = FlowchartIdentifierRegex().Matches(sanitized)
                .Select(match => match.Groups["id"].Value)
                .FirstOrDefault(id => !subgraphIds.Contains(id));

            if (firstNode is null)
            {
                continue;
            }

            foreach (string subgraph in stack)
            {
                entries.TryAdd(subgraph, firstNode);
            }
        }

        return entries;
    }

    private static bool IsFlowchartDirective(string line)
        => line.Length == 0
            || line.StartsWith("direction", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("class ", StringComparison.Ordinal)
            || line.StartsWith("style ", StringComparison.Ordinal)
            || line.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase)
            || line == "end";

    private static string ResolveNode(Dictionary<string, string> subgraphEntries, string node)
        => subgraphEntries.GetValueOrDefault(node, node);

    private static List<(string From, string To)> ParseFlowchartEdges(string line)
    {
        // Drop labels and quoted text first, so only node identifiers remain beside the arrows.
        string sanitized = FlowchartLabelRegex().Replace(line, match => match.Value[0].ToString());
        string[] segments = FlowchartArrowRegex().Split(sanitized);
        if (segments.Length < 2)
        {
            return [];
        }

        List<(string, string)> edges = [];
        for (int index = 0; index < segments.Length - 1; index++)
        {
            string from = LastFlowchartIdentifier(segments[index]);
            string to = FirstFlowchartIdentifier(segments[index + 1]);
            if (from.Length > 0 && to.Length > 0)
            {
                edges.Add((from, to));
            }
        }

        return edges;
    }

    private static string FirstFlowchartIdentifier(string segment)
        => FlowchartIdentifierRegex().Match(segment) is { Success: true } match
            ? match.Groups["id"].Value
            : string.Empty;

    private static string LastFlowchartIdentifier(string segment)
    {
        MatchCollection matches = FlowchartIdentifierRegex().Matches(segment);
        return matches.Count > 0 ? matches[^1].Groups["id"].Value : string.Empty;
    }

    private static List<string> FindTerminalsSharedAcrossRanks(
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyDictionary<string, List<string>> edgesFrom,
        IReadOnlyDictionary<string, HashSet<int>> incomingSourceRanks)
    {
        HashSet<string> sourcesWithOutgoing = edgesFrom.Keys.ToHashSet();
        return
        [
            .. ranks
                .Where(entry => !sourcesWithOutgoing.Contains(entry.Key))
                .Where(entry => incomingSourceRanks.GetValueOrDefault(entry.Key, []).Count > 1)
                .Select(entry => entry.Key),
        ];
    }

    private sealed record MermaidFlow(
        IReadOnlyCollection<string> Nodes,
        int RankWidth,
        int Depth,
        IReadOnlyCollection<string> TerminalsSharedAcrossRanks);

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
            if (closing <= index)
            {
                continue;
            }

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

    [GeneratedRegex(@"-{2,}>|=\{2,}>|-\.-?>?|~{3}")]
    private static partial Regex FlowchartArrowRegex();

    [GeneratedRegex(@"""[^""]*""|\[[^\]]*\]|\([^)]*\)|\{[^}]*\}")]
    private static partial Regex FlowchartLabelRegex();

    [GeneratedRegex(@"^subgraph\s+(?<id>\w+)")]
    private static partial Regex SubgraphDeclarationRegex();

    [GeneratedRegex(@"\b(?<id>[A-Za-z][A-Za-z0-9_]*)\b")]
    private static partial Regex FlowchartIdentifierRegex();
}
