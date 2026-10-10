using System.Collections;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

// Helpers for ShellTests (Core.Support.Shell). Helper types live here, never inside a test class (R-TEST-3). The
// child processes are Windows PowerShell running one-line scripts (a system binary, R-TEST-10); nothing here starts
// an AI CLI.

/// <summary>Child scripts for Shell.run, passed with -EncodedCommand so they need no quoting.</summary>
public static class ShellScripts
{
    /// <summary>What the Ctrl+Break child writes from its handler when it exits on the event.</summary>
    public const string BreakSeen = "BREAK-SEEN";

    /// <summary>What the Ctrl+Break child writes from its handler when it ignores the event and keeps running.</summary>
    public const string BreakIgnored = "BREAK-IGNORED";

    /// <summary>
    /// The exit code the cooperating child leaves with on CTRL_BREAK_EVENT: 131, as in probe P6 and
    /// <see cref="ProcessScripts.BreakExitCode"/>.
    /// </summary>
    public const int BreakExitCode = 131;

    /// <summary>A script that writes <paramref name="bytes"/> to the named standard stream ("Output" or "Error").</summary>
    public static string Write(string stream, byte[] bytes) =>
        $"$s=[Console]::OpenStandard{stream}(); $b=[Convert]::FromBase64String('{Convert.ToBase64String(bytes)}'); " +
        "$s.Write($b,0,$b.Length); $s.Flush(); ";

    /// <summary>A script that writes the PowerShell string <paramref name="expression"/> as UTF-8 to standard output.</summary>
    public static string WriteText(string expression) =>
        $"$s=[Console]::OpenStandardOutput(); $b=[Text.Encoding]::UTF8.GetBytes({expression}); $s.Write($b,0,$b.Length); $s.Flush(); ";

    /// <summary>
    /// A script that writes <see cref="Pattern"/>(<paramref name="count"/>) to the named standard stream in writes of
    /// <paramref name="chunk"/> bytes, built in the child because the command line cannot carry that many bytes.
    /// </summary>
    public static string WritePattern(string stream, int count, int chunk) =>
        $"$s=[Console]::OpenStandard{stream}(); $b=New-Object byte[] {count}; for($i=0;$i -lt {count};$i++){{$b[$i]=$i%251}}; " +
        $"for($o=0;$o -lt {count};$o+={chunk}){{$n=[Math]::Min({chunk},{count}-$o); $s.Write($b,$o,$n)}}; $s.Flush(); ";

    /// <summary>A script that copies all of standard input to standard output, byte for byte.</summary>
    public const string Echo =
        "$i=[Console]::OpenStandardInput(); $m=New-Object IO.MemoryStream; $i.CopyTo($m); " +
        "$o=[Console]::OpenStandardOutput(); $b=$m.ToArray(); $o.Write($b,0,$b.Length); $o.Flush(); ";

    /// <summary>
    /// A child that registers a console control handler for CTRL_BREAK_EVENT (1), then creates
    /// <paramref name="armedFile"/> and sleeps 120 s. With <paramref name="exitsOnBreak"/> the handler writes
    /// <see cref="BreakSeen"/> and calls ExitProcess(131); otherwise it writes <see cref="BreakIgnored"/> and returns
    /// true, so the event is handled and the child keeps sleeping until it is killed. Handlers run last-registered
    /// first, so this one runs before powershell.exe's own (the shape of <see cref="ProcessScripts.CtrlBreakCooperating"/>).
    /// </summary>
    public static string BreakChild(bool exitsOnBreak, string armedFile, string? receivedFile = null) =>
        """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class MarrowShellBreakFixture
        {
            public delegate bool Handler(uint type);
            [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
            [DllImport("kernel32.dll")] private static extern void ExitProcess(uint code);
            private static Handler keep;
            private static bool exits;
            private static string received;
            public static void Arm(bool exitsOnBreak, string receivedFile)
            {
                exits = exitsOnBreak;
                received = receivedFile;
                keep = OnSignal;
                SetConsoleCtrlHandler(keep, true);
            }
            private static bool OnSignal(uint type)
            {
                if (type != 1) return false;
                Console.Out.Write(exits ? "BREAK-SEEN" : "BREAK-IGNORED");
                Console.Out.Flush();
                if (!string.IsNullOrEmpty(received)) System.IO.File.WriteAllText(received, "seen");
                if (exits) ExitProcess(131);
                return true;
            }
        }
        '@
        """ +
        $"\n[MarrowShellBreakFixture]::Arm(${(exitsOnBreak ? "true" : "false")}, {(receivedFile is null ? "$null" : "'" + receivedFile.Replace("'", "''", StringComparison.Ordinal) + "'")})\n" +
        $"[IO.File]::WriteAllText('{armedFile.Replace("'", "''", StringComparison.Ordinal)}', 'armed')\n" +
        "Start-Sleep 120\n";

    /// <summary>
    /// The argv powershell.exe takes to run <paramref name="script"/>, with progress records off: powershell.exe writes
    /// them as "#< CLIXML" text into a redirected stream, which would add bytes the script never wrote.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string script) =>
        SystemTools.PowerShellEncoded("$ProgressPreference='SilentlyContinue'; " + script);

    /// <summary>The test host's own environment as a case-insensitive dictionary (ED-128).</summary>
    public static Dictionary<string, string> HostEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Shell.run of a PowerShell script with the host's environment unless one is given.</summary>
    public static Task<ShellResult> Run(string script, string? directory = null, IReadOnlyDictionary<string, string>? environment = null,
        byte[]? input = null, int? outputLimit = null) =>
        Shell.Run(SystemTools.PowerShell, Arguments(script), directory, environment ?? HostEnvironment(), input, outputLimit: outputLimit);

    /// <summary>Bytes 0, 1, 2 ... wrapping at 251, so every window of the stream is distinct from its neighbours.</summary>
    public static byte[] Pattern(int count, int offset = 0)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++) bytes[i] = (byte)((i + offset) % 251);
        return bytes;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// A script that writes, one per line, "NAME=value" for each of <paramref name="names"/> the child has in its
    /// environment and "NAME!" for each it lacks.
    /// </summary>
    public static string ReportVariables(IEnumerable<string> names) =>
        "$t = (@(" + string.Join(",", names.Select(name => "'" + name + "'")) + ") | ForEach-Object { " +
        "$v = [Environment]::GetEnvironmentVariable($_); if ($null -eq $v) { $_ + '!' } else { $_ + '=' + $v } }) -join [char]10; " +
        WriteText("$t");
}

/// <summary>Inputs and independently written expectations for the LoginEnvironment tests (Shell.swift:327-462).</summary>
public static class LoginEnvironmentSamples
{
    /// <summary>Shell.swift:335-339, all 15 names, in source order.</summary>
    public static readonly string[] ExcludedKeys =
    [
        "_", "SHLVL", "PWD", "OLDPWD", "TERM", "TERM_PROGRAM", "TERM_PROGRAM_VERSION",
        "TERM_SESSION_ID", "COLORTERM", "ITERM_SESSION_ID", "XPC_SERVICE_NAME",
        "__CFBundleIdentifier", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT",
    ];

    /// <summary>The seven absolute macOS locations of Shell.swift:445-447 that ED-170 (1) does not append.</summary>
    public static readonly string[] MacOSEntries =
        ["/opt/homebrew/bin", "/opt/homebrew/sbin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"];

    /// <summary>Shell.swift:445-446 with ED-170 (1): the five entries under <paramref name="home"/>, in the Swift order.</summary>
    public static string[] HomeEntries(string home) =>
    [
        Path.Join(home, ".local", "bin"), Path.Join(home, ".bun", "bin"), Path.Join(home, ".cargo", "bin"),
        Path.Join(home, ".npm-global", "bin"), Path.Join(home, ".volta", "bin"),
    ];

    /// <summary>
    /// ED-170 (1) written out for a real PATH: the non-empty ';' pieces, then each home entry no piece equals ignoring
    /// letter case after one trailing separator is trimmed. (On real paths OrdinalIgnoreCase and the Windows ordinal
    /// table agree; the one pair where they differ has its own test.)
    /// </summary>
    public static string ExpectedAugmentedPath(string? path, string home)
    {
        var entries = (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var directory in HomeEntries(home))
        {
            if (!entries.Any(entry => string.Equals(entry.EndsWith('\\') || entry.EndsWith('/') ? entry[..^1] : entry, directory,
                    StringComparison.OrdinalIgnoreCase)))
                entries.Add(directory);
        }
        return string.Join(";", entries);
    }

    /// <summary>A case-insensitive environment, as ED-128 (1) has every environment dictionary.</summary>
    public static Dictionary<string, string> Environment(params (string Name, string Value)[] variables)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in variables) environment[name] = value;
        return environment;
    }

    /// <summary>The same names and values as the pairs, for Fallback's process environment.</summary>
    public static List<KeyValuePair<string, string>> Pairs(params (string Name, string Value)[] variables) =>
        variables.Select(variable => new KeyValuePair<string, string>(variable.Name, variable.Value)).ToList();

    /// <summary>A folder under MARROW_PORT_DATA_DIR standing in for the home folder, with empty files at the given relative paths.</summary>
    public static string Home(params string[] files) => Folder("login home", files);

    /// <summary>A folder under MARROW_PORT_DATA_DIR with empty files at the given relative paths.</summary>
    public static string Folder(string label, params string[] files)
    {
        var folder = ProcessTestFolders.Create(label);
        foreach (var file in files) CreateFile(Path.Join(folder, file));
        return folder;
    }

    /// <summary>An empty file at <paramref name="path"/>, with its parent folders.</summary>
    public static string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>
    /// An environment for LoginEnvironment's git lookup (ED-174): PATH, PATHEXT ".exe" and the three variables that
    /// name Git for Windows' install folders, each pointing at a folder of the test's own.
    /// </summary>
    public static Dictionary<string, string> GitEnvironment(string path, string programFiles, string programFilesX86, string localAppData) =>
        Environment(("PATH", path), ("PATHEXT", ".exe"), ("ProgramFiles", programFiles), ("ProgramFiles(x86)", programFilesX86),
            ("LOCALAPPDATA", localAppData));
}

/// <summary>
/// Sets one variable of the test process's own environment and puts the old value back on Dispose. Used only in the
/// "Login environment" collection, which runs alone (R-TEST-5).
/// </summary>
public sealed class ProcessVariable : IDisposable
{
    private readonly string name;
    private readonly string? previous;

    public ProcessVariable(string name, string? value)
    {
        this.name = name;
        previous = System.Environment.GetEnvironmentVariable(name);
        System.Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => System.Environment.SetEnvironmentVariable(name, previous);
}

/// <summary>Tests that read or change process-wide state: LoginEnvironment's own load, and the process's PATHEXT.</summary>
[CollectionDefinition("Login environment", DisableParallelization = true)]
public sealed class LoginEnvironmentCollection;
