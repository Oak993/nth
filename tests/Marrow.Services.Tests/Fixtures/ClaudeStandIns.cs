using System.Text;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.ClaudeSession tests. Helper types live here, never inside a
// test class (R-TEST-3). The stand-in is tests/fixtures/claude-cli/ClaudeStandIn.cs (R-TEST-8), an offline program
// compiled with the system's .NET Framework csc.exe; nothing reaches a network or a real AI CLI (R-TEST-10).

/// <summary>Compiles claude-stand-in.exe once per process and copies it out per test.</summary>
public static class ClaudeStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, so IsRunning-style checks never collide across tests.</summary>
    public static string CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "claude-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "claude.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "claude-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "claude-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Claude stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\claude-cli\\ClaudeStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\claude-cli\\ClaudeStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One recorded launch of the stand-in: its argument vector, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
public sealed record ClaudeCall(IReadOnlyList<string> Arguments);

/// <summary>Drives one instance of the stand-in through a fresh CLAUDE_STANDIN_DIR folder.</summary>
public sealed class ClaudeScript
{
    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    public ClaudeScript(string label)
    {
        ExecutablePath = ClaudeStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
    }

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("CLAUDE_STANDIN_DIR", ControlDirectory);

    /// <summary>What --help prints on stdout.</summary>
    public void SetHelp(string text) => File.WriteAllText(Path.Combine(ControlDirectory, "help.txt"), text, new UTF8Encoding(false));

    /// <summary>The control_response "response" object template sent for every control_request; __REQUEST_ID__ is
    /// replaced with that request's own id. Pass a single JSON line (no trailing newline).</summary>
    public void SetResponse(string controlResponseJsonLine) =>
        File.WriteAllText(Path.Combine(ControlDirectory, "response.json"), controlResponseJsonLine, new UTF8Encoding(false));

    public void SetExitStatus(int status) => File.WriteAllText(Path.Combine(ControlDirectory, "exit-status.txt"), status.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The raw control_request line the stand-in received for call <paramref name="call"/> (0-based;
    /// the session process is normally the last call, after the --help probe).</summary>
    public string CapturedControlRequestLine(int call) => File.ReadAllText(Path.Combine(ControlDirectory, "captured-" + call + ".txt"));

    /// <summary>The runs recorded so far, in order.</summary>
    public IReadOnlyList<ClaudeCall> Calls()
    {
        var calls = new List<ClaudeCall>();
        for (var index = 0; File.Exists(Path.Combine(ControlDirectory, "argv-" + index + ".txt")); index++)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ControlDirectory, "argv-" + index + ".txt"));
            var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
            calls.Add(new ClaudeCall(arguments[..^1]));
        }
        return calls;
    }
}
