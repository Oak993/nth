using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Fixtures;

// This unit's (App.Runtime.ThreadRuntime, A33) own compiled copy of the offline stand-in for a provider CLI's
// `--version` and `update` calls (R-TEST-8), the same pattern Marrow.Services.Tests.Fixtures.ProviderProbeStandIns
// uses for ProviderRegistry's own CLI-update tests, duplicated here because a test project does not reference
// another test project's assembly (matching this repo's existing convention: Marrow.Services.Tests.Fixtures.
// ClaudeStandIns has no App.Tests counterpart to reuse either). The source compiled is the shared
// tests/fixtures/provider-probe-cli/ProviderProbeStandIn.cs (R-TEST-8): an offline program, compiled with the
// system's .NET Framework csc.exe, that reaches no network and runs no real AI CLI (R-TEST-10). These tests
// exercise ThreadRuntime.FinishTurnUpdatingCLI/ResendAfterCLIUpdate's ORCHESTRATION of ProviderRegistry's
// already-tested UpdateCLIIfDue/InstalledCLIVersion (see Marrow.Services.Tests.Providers.
// ProviderRegistryCLIUpdateTests for that method's own gates), not those methods' internals again.
public static class ProviderProbeStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in in its own folder.</summary>
    public static ProviderProbeScript CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "app-provider-probe-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "provider-probe.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return new ProviderProbeScript(path, folder);
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "app-provider-probe-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "provider-probe-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The provider-probe stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\provider-probe-cli\\ProviderProbeStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\provider-probe-cli\\ProviderProbeStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One instance of the stand-in: its executable path and the folder its control files live in.</summary>
public sealed class ProviderProbeScript(string executablePath, string folder)
{
    public string ExecutablePath { get; } = executablePath;
    public string Folder { get; } = folder;

    public ProviderProbeScript SetStdout(string text)
    {
        File.WriteAllBytes(Path.Combine(Folder, "stdout.txt"), new UTF8Encoding(false).GetBytes(text));
        return this;
    }

    public ProviderProbeScript SetExitCode(int code)
    {
        File.WriteAllText(Path.Combine(Folder, "exit.txt"), code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return this;
    }
}
