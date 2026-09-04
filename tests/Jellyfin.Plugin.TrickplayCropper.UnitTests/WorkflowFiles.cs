using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

internal static partial class WorkflowFiles
{
    public static int CountHeaderLines(string workflow, string header)
    {
        return workflow
            .Split('\n')
            .Count(line => line.Trim().StartsWith(header, StringComparison.Ordinal));
    }

    public static string[] ReadPermissionScopes(string workflow)
    {
        return ParseScopeEntries(ReadTopLevelBlock(workflow, "permissions:").Split('\n'));
    }

    public static string[] ReadJobPermissionScopes(string jobSection)
    {
        string[] lines = jobSection.Replace("\r\n", "\n").Split('\n');
        int permStart = Array.FindIndex(
            lines, line => line.TrimEnd().EndsWith("permissions:", StringComparison.Ordinal));
        Assert.True(permStart >= 0, "The job must declare a permissions block.");

        int permIndent = lines[permStart].Length - lines[permStart].TrimStart().Length;
        List<string> block = [];

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

            block.Add(line);
        }

        return ParseScopeEntries(block.ToArray());
    }

    private static string[] ParseScopeEntries(string[] lines)
    {
        List<string> scopes = [];
        foreach (string line in lines)
        {
            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            int colon = entry.IndexOf(':');
            Assert.True(colon > 0, $"Unexpected permissions entry: '{line}'.");
            scopes.Add($"{entry[..colon].Trim()}: {entry[(colon + 1)..].Trim()}");
        }

        return scopes.Order(StringComparer.Ordinal).ToArray();
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

    public static string ExtractJobSection(string workflow, string jobName)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        int jobStart = -1;
        int jobIndent = -1;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int indent = line.Length - line.TrimStart().Length;
            if (indent == 2 && line.TrimEnd() == $"  {jobName}:")
            {
                jobStart = index;
                jobIndent = indent;
                break;
            }
        }

        Assert.True(jobStart >= 0, $"Job '{jobName}' was not found in the workflow.");

        StringBuilder section = new();
        for (int index = jobStart; index < lines.Length; index++)
        {
            string line = lines[index];
            if (index > jobStart && line.Trim().Length > 0)
            {
                int indent = line.Length - line.TrimStart().Length;
                if (indent <= jobIndent)
                {
                    break;
                }
            }

            section.Append(line).Append('\n');
        }

        return section.ToString();
    }

    public static KeyValuePair<string, string>[] ReadSteps(string workflow)
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

    public static string ReadStepBody(KeyValuePair<string, string>[] steps, string name)
    {
        return steps.Single(step => step.Key == name).Value;
    }

    public static string ReadEnvValue(string workflow, string name)
    {
        string prefix = $"{name}:";
        string line = workflow
            .Split('\n')
            .Select(candidate => candidate.Trim())
            .Single(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));

        return line[prefix.Length..].Trim();
    }

    public static string ReadJobCondition(string workflow)
    {
        Match match = JobConditionRegex().Match(workflow);
        Assert.True(match.Success, "The job must gate itself with a job-level if condition.");

        return match.Groups["condition"].Value;
    }

    [GeneratedRegex(@"uses:\s*(\S+)")]
    private static partial Regex UsesActionRegex();

    [GeneratedRegex(@"^(\s*)-\s*name:\s*(.+?)\s*$")]
    private static partial Regex StepNameRegex();

    [GeneratedRegex(@"(?m)^(?<indent>[ \t]+)if: >-$\n(?<condition>(?:\k<indent>[ \t].*\n)+)")]
    private static partial Regex JobConditionRegex();
}
