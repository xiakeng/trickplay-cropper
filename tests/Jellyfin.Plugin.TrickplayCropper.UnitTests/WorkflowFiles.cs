using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

internal static partial class WorkflowFiles
{
    public static int CountBlocks(string workflow, string header)
    {
        return workflow
            .Split('\n')
            .Count(line => line.Trim().StartsWith(header, StringComparison.Ordinal));
    }

    public static string ReadTopLevelBlock(string workflow, string header)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd() == header);
        Assert.True(start >= 0, $"The workflow must declare a top-level '{header}' block.");

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

    public static string[] ReadUsedActions(string workflow)
    {
        return UsesActionRegex()
            .Matches(workflow)
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    [GeneratedRegex(@"uses:\s*(\S+)")]
    private static partial Regex UsesActionRegex();
}
