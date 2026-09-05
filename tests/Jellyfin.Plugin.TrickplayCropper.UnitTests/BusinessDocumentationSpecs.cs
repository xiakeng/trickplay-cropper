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
                AssertLinkResolves(file, link.Groups["target"].Value);
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
            "README.md",
            "participants/README.md",
            "lifecycle/README.md",
            "design/README.md",
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
        // These structural checks run in the editor's loop on every test run. Rendering
        // every diagram and reviewing its rendered dimensions is a development-time
        // review recorded on the change's pull request; the only check that stays human
        // is GitHub's own renderer, whose cross-origin viewscreen iframe no local
        // browser session can complete.
        const int MaxNodesPerRank = 4;
        // The approved constraint is "about eight ranks"; the ceiling carries the approved
        // tolerance the prototype was measured against before the set was approved.
        const int MaxRanks = 10;
        const int MaxLeftToRightNodes = 6;
        int diagrams = 0;

        foreach ((string file, string markdown) in ReadEveryDocumentedFile())
        {
            foreach (string diagram in ExtractMermaidDiagrams(file, markdown))
            {
                diagrams++;
                AssertDiagramSatisfiesShapeRules(
                    file,
                    diagram,
                    MaxNodesPerRank,
                    MaxRanks,
                    MaxLeftToRightNodes);
            }
        }

        Assert.True(diagrams > 0, "The business documentation set must contain Mermaid diagrams.");
    }

    private static void AssertLinkResolves(string file, string target)
    {
        if (target.Length == 0
            || target.StartsWith('#')
            || Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return;
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

    private static void AssertDiagramSatisfiesShapeRules(
        string file,
        string diagram,
        int maxNodesPerRank,
        int maxRanks,
        int maxLeftToRightNodes)
    {
        string declaration = diagram
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First();

        Assert.True(
            DiagramDeclarationRegex().IsMatch(declaration),
            $"{file} declares '{declaration}', which is not a supported diagram shape.");

        if (declaration.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
        {
            // A sequence diagram has no ranks; the sizing rules target flowcharts.
            return;
        }

        MermaidFlow flow = ParseFlowchart(diagram);

        if (declaration.StartsWith("flowchart LR", StringComparison.OrdinalIgnoreCase))
        {
            // Left-to-right is reserved for a short chain that fits the reading column.
            Assert.True(
                flow.Nodes.Count <= maxLeftToRightNodes,
                $"{file} declares a left-to-right flowchart with more than {maxLeftToRightNodes} nodes.");
            return;
        }

        Assert.True(
            flow.RankWidth <= maxNodesPerRank,
            $"{file} has a rank with {flow.RankWidth} nodes; keep ranks to about {maxNodesPerRank}.");
        Assert.True(
            flow.Depth <= maxRanks,
            $"{file} is {flow.Depth} ranks deep; split the view instead of stretching it.");

        foreach (string shared in flow.TerminalsSharedAcrossRanks)
        {
            Assert.Fail(
                $"{file} feeds terminal '{shared}' from more than one rank; give each rank its own exit.");
        }
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

    private static List<string> ExtractMermaidDiagrams(string file, string markdown)
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
            Assert.True(
                closing > index,
                $"{file} opens a Mermaid diagram that never closes its fence.");
            diagrams.Add(string.Join('\n', lines[(index + 1)..closing]));
            index = closing;
        }

        return diagrams;
    }

    private static MermaidFlow ParseFlowchart(string diagram)
    {
        List<string> lines = diagram
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        HashSet<string> subgraphIds = CollectSubgraphIds(lines);
        Dictionary<string, string> subgraphEntries = MapSubgraphEntries(lines, subgraphIds);

        HashSet<string> nodes = [];
        Dictionary<string, int> ranks = [];
        Dictionary<string, List<string>> edgesFrom = [];
        foreach ((string from, string to) in EnumerateFlowchartEdges(lines, subgraphIds, subgraphEntries))
        {
            nodes.Add(from);
            nodes.Add(to);
            if (!edgesFrom.TryGetValue(from, out List<string>? targets))
            {
                targets = [];
                edgesFrom[from] = targets;
            }

            targets.Add(to);
        }

        // Edges may appear in any order, so propagate ranks until they stabilize.
        AssignStableRanks(ranks, edgesFrom, nodes);
        if (nodes.Count == 0)
        {
            return new MermaidFlow(nodes, 0, 0, []);
        }

        int width = ranks.Values.GroupBy(rank => rank).Max(group => group.Count());
        return new MermaidFlow(
            nodes,
            width,
            ranks.Values.Max() + 1,
            FindTerminalsSharedAcrossRanks(ranks, edgesFrom));
    }

    private static void AssignStableRanks(
        Dictionary<string, int> ranks,
        IReadOnlyDictionary<string, List<string>> edgesFrom,
        HashSet<string> nodes)
    {
        foreach (string node in nodes)
        {
            ranks.TryAdd(node, 0);
        }

        // A longest path in an acyclic graph is at most the node count, so more rounds
        // than that can only mean a cycle; fail rather than hang on it.
        int maxRounds = nodes.Count + 1;
        for (int round = 0; round < maxRounds; round++)
        {
            bool changed = false;
            foreach ((string from, List<string> targets) in edgesFrom)
            {
                int sourceRank = ranks[from] + 1;
                foreach (string target in targets)
                {
                    if (ranks[target] < sourceRank)
                    {
                        ranks[target] = sourceRank;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                return;
            }
        }

        Assert.Fail("The flowchart contains a cycle, so its ranks never stabilize.");
    }

    private static IEnumerable<(string From, string To)> EnumerateFlowchartEdges(
        List<string> lines,
        HashSet<string> subgraphIds,
        Dictionary<string, string> subgraphEntries)
    {
        foreach (string line in lines)
        {
            if (IsFlowchartDirective(line))
            {
                continue;
            }

            foreach ((string from, string to) in ParseFlowchartEdges(line))
            {
                // Group-to-group edges decorate ownership between subgraphs; Mermaid lays
                // them out as cluster borders, not as ranks, so they do not join the graph.
                if (subgraphIds.Contains(from) && subgraphIds.Contains(to))
                {
                    continue;
                }

                string source = subgraphEntries.GetValueOrDefault(from, from);
                string target = subgraphEntries.GetValueOrDefault(to, to);
                if (source.Length > 0
                    && target.Length > 0
                    && !subgraphIds.Contains(source)
                    && !subgraphIds.Contains(target))
                {
                    yield return (source, target);
                }
            }
        }
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

            string? firstNode = FindFirstInnerNode(line, subgraphIds);
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

    private static string? FindFirstInnerNode(string line, HashSet<string> subgraphIds)
    {
        string sanitized = SanitizeFlowchartLine(line);
        return FlowchartIdentifierRegex().Matches(sanitized)
            .Select(match => match.Groups["id"].Value)
            .FirstOrDefault(id => !subgraphIds.Contains(id));
    }

    private static List<(string From, string To)> ParseFlowchartEdges(string line)
    {
        // Drop labels and quoted text first, so only node identifiers remain beside the arrows.
        string[] segments = FlowchartArrowRegex().Split(SanitizeFlowchartLine(line));
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

    private static string SanitizeFlowchartLine(string line)
        => FlowchartLabelRegex().Replace(line, match => match.Value[0].ToString());

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
        Dictionary<string, int> ranks,
        IReadOnlyDictionary<string, List<string>> edgesFrom)
    {
        HashSet<string> sourcesWithOutgoing = edgesFrom.Keys.ToHashSet();
        Dictionary<string, HashSet<int>> incomingSourceRanks = [];
        foreach ((string from, List<string> targets) in edgesFrom)
        {
            foreach (string target in targets)
            {
                if (!incomingSourceRanks.TryGetValue(target, out HashSet<int>? sourceRanks))
                {
                    sourceRanks = [];
                    incomingSourceRanks[target] = sourceRanks;
                }

                sourceRanks.Add(ranks[from]);
            }
        }

        // A terminal fed from more than one rank is drawn below all of them,
        // stretching every early exit into a long edge.
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

    [GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]*)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?i)(:\d+|lines?\s+\d|\bL\d+\b)")]
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
