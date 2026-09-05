using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Runs the manual deployment milestone using only source-defined local host settings.</summary>
internal sealed class HarnessApplication
{
    private const string AssemblyName = "Jellyfin.Plugin.TrickplayCropper";
    private readonly string root;
    private readonly string pythonDirectory;
    private readonly string dotnet;

    /// <summary>Locates the checkout containing the executable, independently of the launch directory.</summary>
    public HarnessApplication()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrickplayCropper.sln")))
        {
            directory = directory.Parent;
        }

        root = directory?.FullName ?? throw new DirectoryNotFoundException("Run the harness from its repository build.");
        pythonDirectory = Path.Combine(root, "tools/TrickplayCropper.IntegrationHarness");
        dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet"));
    }

    /// <summary>Validates the input and build before crossing either privilege boundary.</summary>
    public async Task<int> RunAsync(string[] arguments)
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            throw new InvalidOperationException("Run the driver as an unprivileged Linux user.");
        }

        if (arguments.Length > 1 || (arguments.Length == 1 && arguments[0] is not ("--check" or "--verify-restoration")))
        {
            throw new ArgumentException("Usage: dotnet run --project tools/TrickplayCropper.IntegrationHarness -- [--check|--verify-restoration]");
        }

        HarnessInput input = HarnessInput.Parse(await File.ReadAllTextAsync(Path.Combine(root, "harness.json")).ConfigureAwait(false));
        using CancellationTokenSource cancellation = new();
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });
        ConsoleCancelEventHandler interrupt = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += interrupt;
        try
        {
            return await ExecuteAsync(input, arguments.SingleOrDefault() ?? string.Empty, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= interrupt;
        }
    }

    private async Task<int> ExecuteAsync(HarnessInput input, string mode, CancellationToken cancellationToken)
    {
        using HttpClientHandler handler = new() { AllowAutoRedirect = false };
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser",
            $"""Client="TrickplayHarness", Device="local", DeviceId="trickplay-harness", Version="1.0", Token="{input.Token}""");
        LocalJellyfin host = new(http);
        await host.ValidateAsync(input, cancellationToken).ConfigureAwait(false);
        int exists = await HarnessProcess.RunAsync("/usr/bin/python3",
            ["-B", Path.Combine(pythonDirectory, "subject_exists.py"), input.InvisibleItem.ToString("D")]).ConfigureAwait(false);
        if (exists != 0)
        {
            throw new InvalidDataException("Cannot prove that the invisible Item exists in the local database.");
        }

        Console.WriteLine("Subject validation passed: administrator user, two playable Items, and one existing concealed Item.");
        if (mode == "--check")
        {
            return 0;
        }

        string version = await BuildAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DeploymentCycle cycle = new(Console.Out);
        bool success = await cycle.RunAsync(
            () => PrepareAsync(version),
            () => VerifyAsync(host, input, version, mode, cancellationToken),
            () => RestoreAsync(host)).ConfigureAwait(false);
        Console.WriteLine(success
            ? "Deployment milestone passed. Debug plugin and Cache Tree retained; smoke cases #76/#77 are not part of this milestone."
            : "Deployment milestone failed; see restoration result above.");
        return success ? 0 : 1;
    }

    private async Task<string> BuildAsync()
    {
        int result = await HarnessProcess.RunAsync(dotnet,
            ["build", Path.Combine(root, "src", AssemblyName, AssemblyName + ".csproj"), "--configuration", "Debug", "--no-restore"])
            .ConfigureAwait(false);
        if (result != 0)
        {
            throw new InvalidOperationException("Debug build failed before host mutation.");
        }

        return System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(BuildDirectory, AssemblyName + ".dll"))
            .Version!.ToString();
    }

    private Task<int> PrepareAsync(string version)
    {
        Console.WriteLine("Privileged Phase 1: deploy Debug DLL/PDB, clear plugin Cache Tree, snapshot logging, enable Debug, restart.");
        return HarnessProcess.RunAsync("/usr/bin/sudo",
            ["/usr/bin/python3", "-B", Path.Combine(pythonDirectory, "host_operation.py"), "prepare", BuildDirectory, version]);
    }

    private async Task RestoreAsync(LocalJellyfin host)
    {
        Console.WriteLine("Privileged Phase 2: restore logging and restart Jellyfin.");
        int result = await HarnessProcess.RunAsync("/usr/bin/sudo",
            ["/usr/bin/python3", "-B", Path.Combine(pythonDirectory, "host_operation.py"), "restore"]).ConfigureAwait(false);
        if (result != 0)
        {
            throw new IOException("Logging restoration or restart failed.");
        }

        // Restoration health has its own deadline and survives cancellation of verification.
        await host.WaitForHealthAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task VerifyAsync(LocalJellyfin host, HarnessInput input, string version, string mode,
        CancellationToken cancellationToken)
    {
        await host.VerifyDeploymentAsync(input, version, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Health, Load-Proof, and fresh structured Debug-Proof gates passed.");
        if (mode == "--verify-restoration")
        {
            Console.WriteLine("Injecting an assertion failure after real deployment gates to exercise unconditional restoration.");
            throw new InvalidOperationException("Intentional restoration verification failure.");
        }
    }

    private string BuildDirectory => Path.Combine(root, "src", AssemblyName, "bin/Debug/net9.0");
}
