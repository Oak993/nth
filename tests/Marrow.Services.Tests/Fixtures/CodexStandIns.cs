using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.CodexSession tests. Helper types live here, never inside a
// test class (R-TEST-3). The stand-in is tests/fixtures/codex-cli/CodexStandIn.cs (R-TEST-8), an offline program
// compiled with the system's .NET Framework csc.exe; nothing reaches a network or a real AI CLI (R-TEST-10).
public static class CodexStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, so IsRunning-style checks never collide across tests.</summary>
    public static string CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "codex-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "codex.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "codex-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "codex-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Codex stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\codex-cli\\CodexStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\codex-cli\\CodexStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One recorded launch of the stand-in: its argument vector, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
public sealed record CodexCall(IReadOnlyList<string> Arguments);

/// <summary>Drives one instance of the stand-in through a fresh CODEX_STANDIN_DIR folder.</summary>
public sealed class CodexScript
{
    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    public CodexScript(string label)
    {
        ExecutablePath = CodexStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
        // A launch whose environment was built elsewhere (the registry's) finds its control folder from this file.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control-dir.txt"), ControlDirectory, new UTF8Encoding(false));
    }

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("CODEX_STANDIN_DIR", ControlDirectory);

    /// <summary>The JSON to send back as "result" for every request of this method (single JSON value, no
    /// trailing newline). '/' in the method name is written through as-is; the file name replaces it with '_'.</summary>
    public void SetResponse(string method, string resultJson, int? occurrence = null) =>
        File.WriteAllText(Path.Combine(ControlDirectory, ResponseFileName("response", method, occurrence)), resultJson, new UTF8Encoding(false));

    /// <summary>Raw wire lines (notifications) the stand-in writes right after its reply to this method (ED-439).</summary>
    public void SetEmit(string method, string lines, int? occurrence = null) =>
        File.WriteAllText(Path.ChangeExtension(Path.Combine(ControlDirectory, ResponseFileName("emit", method, occurrence)), ".txt"), lines, new UTF8Encoding(false));

    /// <summary>The message text to send back as an RPCError for every request of this method.</summary>
    public void SetError(string method, string message, int? occurrence = null) =>
        File.WriteAllText(Path.Combine(ControlDirectory, ResponseFileName("error", method, occurrence)), message, new UTF8Encoding(false));

    private static string ResponseFileName(string kind, string method, int? occurrence) =>
        kind + "-" + method.Replace('/', '_') + (occurrence is { } n ? "-" + n : "") + ".json";

    public void SetExitStatus(int status) => File.WriteAllText(Path.Combine(ControlDirectory, "exit-status.txt"), status.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The raw text of the m-th non-empty stdin line (0-based) launch n (0-based) received. The stand-in
    /// reads stdin on its own thread (so it can give up on an idle process instead of blocking forever), which
    /// makes writing the captured-<c>-<m>.txt file genuinely asynchronous with the client's own pipe write, so
    /// this polls for the file to appear rather than assuming it is already there.</summary>
    public string CapturedLine(int launch, int message)
    {
        var path = Path.Combine(ControlDirectory, "captured-" + launch + "-" + message + ".txt");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) Thread.Sleep(10);
        return File.ReadAllText(path);
    }

    /// <summary>How many non-empty stdin lines launch <paramref name="launch"/> has received so far.</summary>
    public int MessageCount(int launch)
    {
        var count = 0;
        while (File.Exists(Path.Combine(ControlDirectory, "captured-" + launch + "-" + count + ".txt"))) count++;
        return count;
    }

    /// <summary>The whole environment launch <paramref name="launch"/> (0-based) started with, as NAME to value (SK-11b, DL-11).</summary>
    public IReadOnlyDictionary<string, string> LaunchEnvironment(int launch)
    {
        var path = Path.Combine(ControlDirectory, "env-" + launch + ".txt");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) Thread.Sleep(10);
        return File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The runs recorded so far, in order.</summary>
    public IReadOnlyList<CodexCall> Calls()
    {
        var calls = new List<CodexCall>();
        for (var index = 0; File.Exists(Path.Combine(ControlDirectory, "argv-" + index + ".txt")); index++)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ControlDirectory, "argv-" + index + ".txt"));
            var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
            calls.Add(new CodexCall(arguments[..^1]));
        }
        return calls;
    }
}
