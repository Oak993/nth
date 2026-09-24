using System.Text;
using System.Threading;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in and the scratch folders for the Services.Providers.CommandCodeAPI tests. Helper types live
// here, never inside a test class (R-TEST-3). The stand-in is tests/fixtures/command-code-cli/CommandCodeStandIn.cs
// (R-TEST-8), an offline program compiled with the system's .NET Framework csc.exe; nothing reaches a network or a
// real AI CLI (R-TEST-10). Every folder lives under MARROW_PORT_DATA_DIR (R-TEST-9); the fake home folders hold only
// JSON the tests write themselves, so no test ever reads the user's own ~/.commandcode files.

/// <summary>Compiles command-code-stand-in.exe once per process and copies it out per test.</summary>
public static class CommandCodeStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, named cmd.exe as Provider.swift's executableName is "cmd".</summary>
    public static string CopyFor(string label)
    {
        var folder = Path.Combine(CommandCodeScratch.Root(), "stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "cmd.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Compile()
    {
        var folder = Path.Combine(CommandCodeScratch.Root(), "stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "command-code-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The Command Code stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\command-code-cli\\CommandCodeStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\command-code-cli\\CommandCodeStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>Scratch folders under MARROW_PORT_DATA_DIR for one test.</summary>
public static class CommandCodeScratch
{
    public static string Root()
    {
        var dataDirectory = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        return Path.Combine(dataDirectory, "CommandCodeAPITests");
    }

    /// <summary>A new empty folder.</summary>
    public static string NewFolder(string label)
    {
        var folder = Path.Combine(Root(), label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A new fake home folder; when given, writes .commandcode/auth.json and .commandcode/config.json there.</summary>
    public static string NewHome(string? authJson = null, string? configJson = null)
    {
        var home = NewFolder("home");
        if (authJson is not null || configJson is not null) Directory.CreateDirectory(Path.Combine(home, ".commandcode"));
        if (authJson is not null) File.WriteAllBytes(Path.Combine(home, ".commandcode", "auth.json"), Encoding.UTF8.GetBytes(authJson));
        if (configJson is not null) File.WriteAllBytes(Path.Combine(home, ".commandcode", "config.json"), Encoding.UTF8.GetBytes(configJson));
        return home;
    }
}

/// <summary>One recorded launch of the stand-in: its argument vector, NUL-decoded (R-PROC-1: a real argv, never re-split).</summary>
public sealed record CommandCodeCall(IReadOnlyList<string> Arguments);

/// <summary>Drives one instance of the stand-in through a fresh COMMAND_CODE_STANDIN_DIR folder.</summary>
public sealed class CommandCodeScript
{
    public string ExecutablePath { get; }
    public string ControlDirectory { get; }

    public CommandCodeScript(string label)
    {
        ExecutablePath = CommandCodeStandIns.CopyFor(label);
        ControlDirectory = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "control");
        Directory.CreateDirectory(ControlDirectory);
    }

    /// <summary>
    /// The stand-in's location as a SwiftURL whose Path is the Windows path unchanged. ENGINEERING DECISION, test-only,
    /// the same as ClaudeSessionTests.FileUrl: a "file:///C:/x" SwiftURL gives the POSIX-shaped path "/C:/x", which
    /// CreateProcessW rejects, so the URL is built from a non-"file" scheme whose Path round-trips the real path.
    /// </summary>
    public SwiftURL Executable => SwiftURL.Create("marrow-native-path:" + ExecutablePath.Replace('\\', '/'))!;

    /// <summary>The environment entry that points the stand-in at this instance's control folder.</summary>
    public KeyValuePair<string, string> EnvironmentEntry => new("COMMAND_CODE_STANDIN_DIR", ControlDirectory);

    public void SetStdout(string text) => File.WriteAllBytes(Path.Combine(ControlDirectory, "stdout.bin"), Encoding.UTF8.GetBytes(text));

    public void SetStderr(string text) => File.WriteAllBytes(Path.Combine(ControlDirectory, "stderr.bin"), Encoding.UTF8.GetBytes(text));

    public void SetExitStatus(int status) =>
        File.WriteAllBytes(Path.Combine(ControlDirectory, "exit-status.txt"), Encoding.UTF8.GetBytes(status.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The runs recorded so far, in order.</summary>
    public IReadOnlyList<CommandCodeCall> Calls()
    {
        var calls = new List<CommandCodeCall>();
        for (var index = 0; File.Exists(Path.Combine(ControlDirectory, "argv-" + index + ".txt")); index++)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ControlDirectory, "argv-" + index + ".txt"));
            var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
            calls.Add(new CommandCodeCall(arguments[..^1]));
        }
        return calls;
    }
}
