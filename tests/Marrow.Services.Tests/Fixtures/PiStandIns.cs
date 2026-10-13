using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.PiCLI tests. Helper types live here, never inside a test class
// (R-TEST-3). The stand-in is tests/fixtures/pi-cli/PiStandIn.cs (R-TEST-8), an offline program compiled with the
// system's .NET Framework csc.exe, as ClaudeStandIns does; nothing reaches a network or a real AI CLI (R-TEST-10).

/// <summary>Compiles pi-stand-in.exe once per process and copies it out per test.</summary>
public static class PiStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, named pi.exe, in its own folder.</summary>
    public static string CopyFor(string label)
    {
        var folder = Path.Combine(Root(), "pi-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "pi.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Root()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        return root;
    }

    private static string Compile()
    {
        var folder = Path.Combine(Root(), "pi-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "pi-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Pi stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\pi-cli\\PiStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\pi-cli\\PiStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>Drives one instance of the stand-in through a fresh PI_STANDIN_DIR folder.</summary>
public sealed class PiScript
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    public PiScript(string label)
    {
        ExecutablePath = PiStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
    }

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("PI_STANDIN_DIR", ControlDirectory);

    /// <summary>Lines written to stdout right after the <paramref name="line"/>-th (0-based) stdin line.</summary>
    public void Reply(int line, params string[] jsonLines) =>
        File.WriteAllText(Path.Combine(ControlDirectory, $"reply-{line}.jsonl"), string.Concat(jsonLines.Select(l => l + "\n")), Utf8);

    /// <summary>The argument vector of the run, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
    public IReadOnlyList<string>? Arguments()
    {
        var raw = Read("argv.txt");
        if (raw is null) return null;
        var parts = raw.Split('\0');
        return parts.Take(parts.Length - 1).ToList();
    }

    public string? WorkingDirectory() => Read("cwd.txt");

    /// <summary>The value the run saw for <paramref name="key"/>, or null when it was not set.</summary>
    public string? Environment(string key) => Read("env-" + key + ".txt");

    /// <summary>The raw stdin lines the run received, in order.</summary>
    public IReadOnlyList<string> StdinLines()
    {
        var lines = new List<string>();
        while (Read($"stdin-{lines.Count}.txt") is { } line) lines.Add(line);
        return lines;
    }

    /// <summary>A closed stdin no longer ends the run: only a terminate (or the stand-in's own 60 s limit) does (ED-NEW-perf-ui-thread-7).</summary>
    public void IgnoreEndOfInput() => File.WriteAllText(Path.Combine(ControlDirectory, "ignore-eof"), "1", Utf8);

    /// <summary>Whether the run's stdin has closed.</summary>
    public bool SawEndOfInput => File.Exists(Path.Combine(ControlDirectory, "eof.txt"));

    private string? Read(string name)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(ControlDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string lockName;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            lockName = "Local\\MarrowPiStandIn-" + BitConverter.ToString(hash.ComputeHash(identity)).Replace("-", "", StringComparison.Ordinal);
        using var publication = new Mutex(false, lockName);
        try { publication.WaitOne(); }
        catch (AbandonedMutexException) { /* Ownership is acquired even though the previous writer died. */ }
        try
        {
            var path = Path.Combine(ControlDirectory, name);
            return File.Exists(path) ? Utf8.GetString(File.ReadAllBytes(path)) : null;
        }
        finally { publication.ReleaseMutex(); }
    }
}
