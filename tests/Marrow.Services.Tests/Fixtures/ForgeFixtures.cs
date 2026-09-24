using System.Runtime.CompilerServices;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// Scripted forge CLIs for the Services.Git.Git tests of createPullRequest and mergePullRequest (Git.swift:172-227).
// Helper types live here, never inside a test class (R-TEST-3). The stand-in is tests/fixtures/forge-cli/ForgeStandIn.cs
// (R-TEST-8), an offline program compiled with the system's .NET Framework csc.exe; nothing reaches a network or a real
// forge CLI (R-TEST-10).
//
// Why the PATH changes: Shell.run(tool:) looks "glab", "gh" and "tea" up on LoginEnvironment.current's PATH
// (Shell.swift:129-131, Shell.cs RunTool), and LoginEnvironment builds that once per process from the process's own
// environment (Shell.cs Fallback). The only way to put a stand-in under those names is to put its folder on the
// process PATH before anything in this test process reads LoginEnvironment, which a module initializer guarantees:
// it runs once, before any test body, so no test sees the process environment change (R-TEST-5). The folder lives
// under MARROW_PORT_DATA_DIR (R-TEST-9) and holds only glab.exe, gh.exe, tea.exe and the compiled stand-in.

/// <summary>The stand-in folder on PATH, and the compiled glab.exe, gh.exe and tea.exe in it.</summary>
public static class ForgeStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The folder put first on the process PATH, or null when MARROW_PORT_DATA_DIR was not set.</summary>
    public static string? Folder { get; private set; }

    [ModuleInitializer]
#pragma warning disable CA2255 // A module initializer in a test library is the point: it must run before any test reads LoginEnvironment.
    internal static void PutFolderOnPath()
#pragma warning restore CA2255
    {
        var dataDirectory = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory)) return;
        var folder = dataDirectory.TrimEnd('\\', '/') + "\\ForgeStandIns\\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("PATH", folder + ";" + Environment.GetEnvironmentVariable("PATH"));
        Folder = folder;
    }

    /// <summary>Compiles the stand-in once and copies it as glab.exe, gh.exe and tea.exe; returns the folder.</summary>
    public static string Ensure() => compiled.Value;

    /// <summary>The full path of the stand-in called <paramref name="tool"/> (glab, gh or tea).</summary>
    public static string PathOf(string tool) => Ensure() + "\\" + tool + ".exe";

    /// <summary>
    /// Whether the stand-in called <paramref name="tool"/> is running: a running image cannot be opened for exclusive
    /// write, so an open that succeeds means every run of it has exited.
    /// </summary>
    public static bool IsRunning(string tool)
    {
        try
        {
            using var stream = new FileStream(PathOf(tool), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string Compile()
    {
        var folder = Folder ?? throw new InvalidOperationException(
            "Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = folder + "\\forge-stand-in.exe";
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The forge stand-in did not compile: " + run.Result.FailureMessage);
        foreach (var tool in new[] { "glab", "gh", "tea" })
            File.Copy(output, folder + "\\" + tool + ".exe", overwrite: true);
        return folder;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\forge-cli\\ForgeStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\forge-cli\\ForgeStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One recorded run of a stand-in.</summary>
public sealed record ForgeCall(string Exe, string WorkingDirectory, string Stdin, string TerminalPrompt, string OptionalLocks,
    IReadOnlyList<string> Arguments);

/// <summary>The stand-in's script and record for one checkout: files under &lt;root&gt;\.git\forge-stand-in.</summary>
public sealed class ForgeScript
{
    private readonly string folder;

    public ForgeScript(string repositoryRoot)
    {
        ForgeStandIns.Ensure();
        folder = repositoryRoot + "\\.git\\forge-stand-in";
        Directory.CreateDirectory(folder);
    }

    /// <summary>What every run answers unless a numbered reply says otherwise.</summary>
    public void ReplyAlways(string stdout = "", string stderr = "", string status = "0") => Write("reply", stdout, stderr, status);

    /// <summary>What run <paramref name="call"/> (0-based) answers.</summary>
    public void Reply(int call, string stdout = "", string stderr = "", string status = "0") =>
        Write("reply-" + call, stdout, stderr, status);

    /// <summary>The runs recorded so far, in order.</summary>
    public IReadOnlyList<ForgeCall> Calls()
    {
        var calls = new List<ForgeCall>();
        for (var index = 0; File.Exists(Argv(index)); index++)
        {
            var info = Encoding.UTF8.GetString(File.ReadAllBytes(folder + "\\call-" + index + ".info"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
            var bytes = File.ReadAllBytes(Argv(index));
            var arguments = Encoding.UTF8.GetString(bytes).Split('\0');
            calls.Add(new ForgeCall(info["exe"], info["cwd"], info["stdin"], info["GIT_TERMINAL_PROMPT"], info["GIT_OPTIONAL_LOCKS"],
                arguments[..^1]));
        }
        return calls;
    }

    /// <summary>How many runs have been recorded.</summary>
    public int Count
    {
        get
        {
            var index = 0;
            while (File.Exists(Argv(index))) index++;
            return index;
        }
    }

    private string Argv(int index) => folder + "\\call-" + index + ".argv";

    private void Write(string stem, string stdout, string stderr, string status)
    {
        File.WriteAllBytes(folder + "\\" + stem + ".out", Encoding.UTF8.GetBytes(stdout));
        File.WriteAllBytes(folder + "\\" + stem + ".err", Encoding.UTF8.GetBytes(stderr));
        File.WriteAllBytes(folder + "\\" + stem + ".status", Encoding.UTF8.GetBytes(status));
    }
}
