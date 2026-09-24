using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.CopilotSession tests. Helper types live here, never inside a
// test class (R-TEST-3). The stand-in is tests/fixtures/copilot-cli/CopilotStandIn.cs (R-TEST-8), an offline
// program compiled with the system's .NET Framework csc.exe; nothing reaches a network or a real AI CLI
// (R-TEST-10). It answers Content-Length framed JSON-RPC requests from files in its control folder and records
// every frame it reads (see the program's header).

/// <summary>Compiles copilot-stand-in.exe once per process and copies it out per test.</summary>
public static class CopilotStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, named copilot.exe, in its own folder.</summary>
    public static string CopyFor(string label)
    {
        var folder = Path.Combine(DataRoot(), "copilot-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "copilot.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string DataRoot()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        return root;
    }

    private static string Compile()
    {
        var folder = Path.Combine(DataRoot(), "copilot-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "copilot-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Copilot stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\copilot-cli\\CopilotStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\copilot-cli\\CopilotStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>Drives one instance of the stand-in through a fresh COPILOT_STANDIN_DIR folder.</summary>
public sealed class CopilotScript
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    /// <summary>A stand-in whose ping reply carries protocolVersion 3 (CopilotSession.swift:22-26, :494-495).</summary>
    public CopilotScript(string label)
    {
        ExecutablePath = CopilotStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
        Reply("ping", "{\"protocolVersion\":3}");
    }

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("COPILOT_STANDIN_DIR", ControlDirectory);

    /// <summary>The raw JSON result for every request of <paramref name="method"/>, or only its
    /// <paramref name="occurrence"/>-th (1-based) one.</summary>
    public void Reply(string method, string resultJson, int? occurrence = null) =>
        Write(occurrence is { } k ? $"reply-{method}-{k}.json" : $"reply-{method}.json", resultJson);

    /// <summary>The raw JSON error object sent for every request of <paramref name="method"/>.</summary>
    public void Error(string method, long code, string message) =>
        Write($"error-{method}.json", JSONValue.FromDictionaryLiteral(("code", code), ("message", message)).CompactString);

    /// <summary>Requests of <paramref name="method"/> are never answered.</summary>
    public void Silent(string method) => Write($"silent-{method}", "1");

    /// <summary>Frames sent right after answering a request of <paramref name="method"/> (or only its k-th).</summary>
    public void After(string method, IEnumerable<JSONValue> frames, int? occurrence = null) =>
        Write(occurrence is { } k ? $"after-{method}-{k}.jsonl" : $"after-{method}.jsonl",
            string.Join("\n", frames.Select(f => f.CompactString)) + "\n");

    /// <summary>The run exits right after answering a request of <paramref name="method"/>.</summary>
    public void ExitAfter(string method) => Write($"exit-after-{method}", "1");

    /// <summary>Text the run writes to stderr once at start.</summary>
    public void Stderr(string text) => Write("stderr.txt", text);

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(ControlDirectory, name), text, Utf8);

    /// <summary>The argument vectors of every run so far, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Calls()
    {
        var calls = new List<IReadOnlyList<string>>();
        for (var index = 0; File.Exists(Path.Combine(ControlDirectory, "argv-" + index + ".txt")); index++)
        {
            var arguments = Utf8.GetString(File.ReadAllBytes(Path.Combine(ControlDirectory, "argv-" + index + ".txt"))).Split('\0');
            calls.Add(arguments[..^1]);
        }
        return calls;
    }

    /// <summary>The raw frame bodies run <paramref name="call"/> has read so far, in order.</summary>
    public IReadOnlyList<string> Frames(int call = 0)
    {
        var frames = new List<string>();
        for (var m = 0; File.Exists(FramePath(call, m)); m++) frames.Add(Utf8.GetString(File.ReadAllBytes(FramePath(call, m))));
        return frames;
    }

    /// <summary>The parsed frames run <paramref name="call"/> has read so far, in order.</summary>
    public IReadOnlyList<JSONValue> Messages(int call = 0) => Frames(call).Select(f => JSONValue.Parse(f)!).ToList();

    /// <summary>The requests (frames with a method) of <paramref name="method"/> run <paramref name="call"/> has read.</summary>
    public IReadOnlyList<JSONValue> Requests(string method, int call = 0) =>
        Messages(call).Where(m => m["method"]?.JSONValueString == method).ToList();

    /// <summary>The methods of every request or notification run <paramref name="call"/> has read, in order.</summary>
    public IReadOnlyList<string> Methods(int call = 0) =>
        Messages(call).Select(m => m["method"]?.JSONValueString).OfType<string>().ToList();

    /// <summary>The frames without a method (the session's replies to server requests), in order.</summary>
    public IReadOnlyList<JSONValue> Responses(int call = 0) => Messages(call).Where(m => m["method"] is null).ToList();

    /// <summary>Polls (real time: the stand-in writes on its own thread) until <paramref name="condition"/> holds.</summary>
    public bool WaitFor(Func<CopilotScript, bool> condition, double seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition(this)) return true;
            Thread.Sleep(10);
        }
        return condition(this);
    }

    private string FramePath(int call, int message) => Path.Combine(ControlDirectory, "captured-" + call + "-" + message + ".json");
}
