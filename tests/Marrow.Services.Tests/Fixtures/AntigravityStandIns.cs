using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.AntigravitySession tests. Helper types live here, never inside a
// test class (R-TEST-3). The stand-in is tests/fixtures/antigravity-cli/AntigravityStandIn.cs (R-TEST-8), an offline
// program compiled with the system's .NET Framework csc.exe; nothing reaches a network or a real AI CLI (R-TEST-10).
// It answers one-shot calls and stream-json session lines from files in its control folder (see the program's header).

/// <summary>Compiles agy-stand-in.exe once per process and copies it out per test.</summary>
public static class AntigravityStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, named agy.exe, in its own folder.</summary>
    public static string CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "agy-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "agy.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "agy-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "agy-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Antigravity stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\antigravity-cli\\AntigravityStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\antigravity-cli\\AntigravityStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>Drives one instance of the stand-in through a fresh AGY_STANDIN_DIR folder.</summary>
public sealed class AntigravityScript
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    public AntigravityScript(string label)
    {
        ExecutablePath = AntigravityStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
    }

    /// <summary>A closed stdin no longer ends a session run: only a terminate (or the stand-in's own 20 s idle limit) does (ED-NEW-perf-ui-thread-7).</summary>
    public void IgnoreEndOfInput() => Set("ignore-eof", "1");

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("AGY_STANDIN_DIR", ControlDirectory);

    /// <summary>Writes one control file as UTF-8 without a byte order mark.</summary>
    public void Set(string name, string text) => File.WriteAllText(Path.Combine(ControlDirectory, name), text, Utf8);

    /// <summary>Writes one control file as raw bytes.</summary>
    public void SetBytes(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(ControlDirectory, name), bytes);

    /// <summary>The lines the session run prints at start (one JSON object per line).</summary>
    public void SetInit(params string[] lines) => Set("init.jsonl", string.Join("\n", lines));

    /// <summary>The lines the session run prints after its m-th stdin line (0-based).</summary>
    public void SetTurn(int message, params string[] lines) => Set("turn-" + message + ".jsonl", string.Join("\n", lines));

    public bool Exists(string name) => File.Exists(Path.Combine(ControlDirectory, name));

    public string Read(string name) => File.ReadAllText(Path.Combine(ControlDirectory, name));

    /// <summary>The raw text of the m-th non-empty stdin line (0-based) call n (0-based) received, polled for because
    /// the stand-in writes it asynchronously with the client's own pipe write.</summary>
    public string CapturedLine(int call, int message)
    {
        var path = Path.Combine(ControlDirectory, "captured-" + call + "-" + message + ".txt");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) Thread.Sleep(10);
        return File.ReadAllText(path);
    }

    /// <summary>How many non-empty stdin lines call <paramref name="call"/> has received so far.</summary>
    public int MessageCount(int call)
    {
        var count = 0;
        while (File.Exists(Path.Combine(ControlDirectory, "captured-" + call + "-" + count + ".txt"))) count++;
        return count;
    }

    /// <summary>The argument vectors recorded so far, in call order, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Calls()
    {
        var calls = new List<IReadOnlyList<string>>();
        for (var index = 0; File.Exists(Path.Combine(ControlDirectory, "argv-" + index + ".txt")); index++)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ControlDirectory, "argv-" + index + ".txt"));
            var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
            calls.Add(arguments[..^1]);
        }
        return calls;
    }
}
