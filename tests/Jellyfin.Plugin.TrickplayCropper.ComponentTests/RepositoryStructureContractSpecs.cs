using System.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class RepositoryStructureContractSpecs
{
    [Fact]
    public void PhysicalLineLimitHonorsBothTerminalNewlineFormsAndRejectsLine501()
    {
        using var repository = new TemporaryContractRepository();

        repository.Write("source.cs", RepeatLines(500, terminalNewline: true));
        AssertSuccess(repository.Verify());

        repository.Write("source.cs", RepeatLines(500, terminalNewline: false));
        AssertSuccess(repository.Verify());

        repository.Write("source.cs", RepeatLines(501, terminalNewline: true));
        AssertFailure(repository.Verify(), "source.cs: 501 physical lines; maximum is 500.");
    }

    [Fact]
    public void ScriptDetectionRejectsWindowsAndExtensionlessExecutableScripts()
    {
        using var repository = new TemporaryContractRepository();

        repository.Write("build.cmd", RepeatLines(501, terminalNewline: true));
        AssertFailure(repository.Verify(), "build.cmd: 501 physical lines; maximum is 500.");

        repository.Write("build.cmd", "@echo off\n");
        repository.Write("build-tool", RepeatLines(501, terminalNewline: true));
        repository.MarkExecutable("build-tool");
        AssertFailure(repository.Verify(), "build-tool: 501 physical lines; maximum is 500.");
    }

    [Fact]
    public void CodeMapTokenLimitRejectsToken1001AndBeyond()
    {
        using var repository = new TemporaryContractRepository();
        repository.Write("docs/code-maps/README.md", string.Concat(Enumerable.Repeat("token ", 1_500)));

        AssertFailure(repository.Verify(), "cl100k_base tokens; maximum is 1000.");
    }

    private static string RepeatLines(int count, bool terminalNewline)
    {
        string content = string.Concat(Enumerable.Repeat("line\n", count));
        return terminalNewline ? content : content.TrimEnd('\n');
    }

    private static void AssertSuccess(ProcessResult result)
    {
        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.Contains("Verified", result.StandardOutput, StringComparison.Ordinal);
    }

    private static void AssertFailure(ProcessResult result, string expectedDiagnostic)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedDiagnostic, result.StandardError, StringComparison.Ordinal);
    }

    private sealed class TemporaryContractRepository : IDisposable
    {
        private readonly string root;

        public TemporaryContractRepository()
        {
            root = Path.Combine(Path.GetTempPath(), $"trickplay-structure-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, ".github", "scripts"));
            Directory.CreateDirectory(Path.Combine(root, "docs", "code-maps"));
            File.Copy(
                Path.Combine(FindRepositoryRoot(), ".github", "scripts", "verify_repository_structure.py"),
                Path.Combine(root, ".github", "scripts", "verify_repository_structure.py"));
            File.WriteAllText(Path.Combine(root, "docs", "code-maps", "README.md"), "# Map\n");
            AssertCommandSucceeded(Run("git", "init", "--quiet"));
            AssertCommandSucceeded(Run("git", "add", "-A"));
        }

        public void Write(string relativePath, string content)
        {
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            AssertCommandSucceeded(Run("git", "add", "--", relativePath));
        }

        public void MarkExecutable(string relativePath)
        {
            AssertCommandSucceeded(Run("git", "update-index", "--chmod=+x", "--", relativePath));
        }

        public ProcessResult Verify()
        {
            return Run("python3", ".github/scripts/verify_repository_structure.py");
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
        }

        private ProcessResult Run(string fileName, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = root,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null
                   && !File.Exists(Path.Combine(directory.FullName, "TrickplayCropper.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException(
                    "Could not locate the Trickplay Cropper repository root.");
        }

        private static void AssertCommandSucceeded(ProcessResult result)
        {
            Assert.True(result.ExitCode == 0, result.CombinedOutput);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => string.Concat(StandardOutput, Environment.NewLine, StandardError);
    }
}
