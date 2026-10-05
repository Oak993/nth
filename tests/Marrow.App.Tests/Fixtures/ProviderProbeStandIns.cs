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

    /// <summary>Hold Claude's -p listing result until Release; independent --help probes stay unblocked.</summary>
    public ProviderProbeScript WaitForRelease()
    {
        File.WriteAllBytes(Path.Combine(Folder, "wait-for-release.txt"), []);
        return this;
    }

    public void Release() => File.WriteAllBytes(Path.Combine(Folder, "release-listings.txt"), []);

    // ClaudeSession.swift:185-190: the handshake listing starts with -p and passes --no-session-persistence with
    // --input-format stream-json. A session start (ClaudeSession.swift:85-90, e.g. the runtime's warm-up) also starts
    // with -p and is not a command listing; nor is the text generator's one-shot (TextGeneration.swift, the thread
    // title after a first message: -p --output-format text --model haiku ... --no-session-persistence), which a send
    // can start at any moment; profile warmup's --help is a separate call.
    public IReadOnlyList<IReadOnlyList<string>> CommandListings() =>
        RecordedCalls().Where(arguments => arguments.Count > 0 && arguments[0] == "-p" && arguments.Contains("--no-session-persistence")
            && arguments.Contains("--input-format") && arguments.Contains("stream-json")).ToArray();

    /// <summary>What a later "--version" call reports once this stand-in has been called with exactly
    /// ["update"] (ProviderProbeStandIn.cs's post-update-stdout.txt): simulates the CLI's own updater having
    /// installed a newer build, for ResendAfterCLIUpdate's before/after version comparison.</summary>
    public ProviderProbeScript SetPostUpdateStdout(string text)
    {
        File.WriteAllBytes(Path.Combine(Folder, "post-update-stdout.txt"), new UTF8Encoding(false).GetBytes(text));
        return this;
    }

    /// <summary>The argv (NUL-decoded) of every call so far, in call order (0-based, ClaudeStandIn.cs's shape;
    /// matches Marrow.Services.Tests.Fixtures.ProviderProbeStandIns.RecordedCalls).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RecordedCalls()
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string lockName;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\MarrowProviderProbe-" + BitConverter.ToString(hash.ComputeHash(identity)).Replace("-", "", StringComparison.Ordinal);
        using var recording = new Mutex(false, lockName);
        recording.WaitOne();
        try { return ReadRecordedCalls(); }
        finally { recording.ReleaseMutex(); }
    }

    private IReadOnlyList<IReadOnlyList<string>> ReadRecordedCalls()
    {
        var calls = new List<IReadOnlyList<string>>();
        var call = 0;
        while (true)
        {
            var path = Path.Combine(Folder, "call-" + call + ".args");
            if (!File.Exists(path)) break;
            var bytes = File.ReadAllBytes(path);
            var parts = new List<string>();
            var start = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0) continue;
                parts.Add(Encoding.UTF8.GetString(bytes, start, i - start));
                start = i + 1;
            }
            calls.Add(parts);
            call++;
        }
        return calls;
    }
}
