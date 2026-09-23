using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

// Helpers for the process launcher tests (F.ProcessLauncher). Helper types live here, never inside a test class
// (R-TEST-3). Nothing here launches a real AI CLI: the child processes are Windows system binaries (where.exe,
// sort.exe, powershell.exe running a one-line script), as R-TEST-10 allows.

/// <summary>
/// A scripted stand-in for a running Foundation Process, so the termination ladders can be driven under the manual
/// clock. It records every call in order; <see cref="IsRunning"/> is read the way the Swift guards read
/// `process.isRunning` (Shell.swift:111 and :114, StdioProcess.swift:122 and :126, MCPHub.swift:548 and :552).
/// </summary>
public sealed class FakeProcessControl(string name = "process", ProcessJournal? journal = null) : IProcessControl
{
    private readonly object gate = new();
    private readonly List<string> calls = [];
    private bool running = true;

    public string Name { get; } = name;

    /// <summary>Records something that is not a process call (the stdin close of StdioProcess.swift:121) in order.</summary>
    public void Note(string what) => Record(what, end: false);

    /// <summary>A process that has already exited when the ladder runs.</summary>
    public static FakeProcessControl Exited(string name = "process", ProcessJournal? journal = null)
    {
        var process = new FakeProcessControl(name, journal);
        process.Exit();
        return process;
    }

    /// <summary>When true, the graceful step ends the process at once (a child that honours it).</summary>
    public bool ExitsOnTerminate { get; set; }

    /// <summary>Every call so far, in order: "isRunning", "terminate", "killTree", "killProcess".</summary>
    public IReadOnlyList<string> Calls
    {
        get { lock (gate) return [.. calls]; }
    }

    public int Count(string call)
    {
        lock (gate) return calls.Count(c => c == call);
    }

    /// <summary>The process ends on its own (it exits during the grace period).</summary>
    public void Exit()
    {
        lock (gate) running = false;
    }

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                calls.Add("isRunning");
                journal?.Add(Name + ":isRunning");
                Monitor.PulseAll(gate);
                return running;
            }
        }
    }

    public void Terminate() => Record("terminate", end: ExitsOnTerminate);

    public void KillTree() => Record("killTree", end: true);

    public void KillProcess() => Record("killProcess", end: true);

    /// <summary>Blocks until <paramref name="call"/> has happened <paramref name="times"/> times; false on timeout.</summary>
    public bool WaitFor(string call, int times, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (gate)
        {
            while (calls.Count(c => c == call) < times)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(gate, left);
            }
            return true;
        }
    }

    private void Record(string call, bool end)
    {
        lock (gate)
        {
            calls.Add(call);
            journal?.Add(Name + ":" + call);
            if (end) running = false;
            Monitor.PulseAll(gate);
        }
    }
}

/// <summary>One ordered record of calls across several fake processes (MCPHub.swift:547-556 orders across them).</summary>
public sealed class ProcessJournal
{
    private readonly object gate = new();
    private readonly List<string> entries = [];

    public IReadOnlyList<string> Entries
    {
        get { lock (gate) return [.. entries]; }
    }

    public void Add(string entry)
    {
        lock (gate) entries.Add(entry);
    }
}

/// <summary>Collects what a <see cref="ProcessPipe"/> reading end delivers: the chunks, and the empty end marker.</summary>
public sealed class PipeRecorder
{
    private readonly object gate = new();
    private readonly MemoryStream bytes = new();
    private int endMarkers;

    public PipeRecorder(ProcessPipe pipe) => pipe.ReadabilityHandler = Append;

    public byte[] Bytes
    {
        get { lock (gate) return bytes.ToArray(); }
    }

    /// <summary>The bytes so far as Latin-1 text: every byte one char, for finding ASCII markers in pty output.</summary>
    public string Text
    {
        get { lock (gate) return Encoding.Latin1.GetString(bytes.ToArray()); }
    }

    public int EndMarkers
    {
        get { lock (gate) return endMarkers; }
    }

    /// <summary>Chunks that arrived after the end marker (must stay zero).</summary>
    public int ChunksAfterEnd { get; private set; }

    public bool WaitForEnd(TimeSpan timeout) => WaitUntil(() => endMarkers > 0, timeout);

    public bool WaitForText(string marker, TimeSpan timeout) =>
        WaitUntil(() => Encoding.Latin1.GetString(bytes.ToArray()).Contains(marker, StringComparison.Ordinal), timeout);

    private void Append(byte[] data)
    {
        lock (gate)
        {
            if (data.Length == 0) endMarkers += 1;
            else
            {
                if (endMarkers > 0) ChunksAfterEnd += 1;
                bytes.Write(data);
            }
            Monitor.PulseAll(gate);
        }
    }

    private bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (gate)
        {
            while (!condition())
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(gate, left);
            }
            return true;
        }
    }
}

/// <summary>Windows system binaries the launcher tests start. None is an AI CLI.</summary>
public static class SystemTools
{
    public static string Where => Path.Join(Environment.SystemDirectory, "where.exe");

    public static string Sort => Path.Join(Environment.SystemDirectory, "sort.exe");

    public static string PowerShell => Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>The argv for powershell.exe running one script, with no profile and no prompts.</summary>
    public static IReadOnlyList<string> PowerShellScript(string script) => ["-NoProfile", "-NonInteractive", "-Command", script];

    /// <summary>
    /// The same, with the script passed as -EncodedCommand (base64 of its UTF-16LE text), so a script holding double
    /// quotes, paths with spaces or several lines needs no quoting at all.
    /// </summary>
    public static IReadOnlyList<string> PowerShellEncoded(string script) => ["-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script)];

    /// <summary>The base64 UTF-16LE form powershell.exe -EncodedCommand takes.</summary>
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    public static string Interpreter => Path.Join(Environment.SystemDirectory, "cmd.exe");

    /// <summary>%SystemRoot%\SysWOW64\cmd.exe, the 32-bit interpreter PA-0016 step 3 also names.</summary>
    public static string InterpreterWow64 => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "cmd.exe");
}

/// <summary>PowerShell scripts for the child processes of the launcher tests (system binaries only, R-TEST-10).</summary>
public static class ProcessScripts
{
    /// <summary>What the Ctrl+Break child prints once its handler is in place.</summary>
    public const string Armed = "BREAK-ARMED";

    /// <summary>What the Ctrl+Break child prints from its handler, before it exits with <see cref="BreakExitCode"/>.</summary>
    public const string BreakSeen = "BREAK-SEEN";

    /// <summary>
    /// The exit code the Ctrl+Break child leaves with: 131, the marker probe P6 used for "left on CTRL_BREAK_EVENT"
    /// (porting/probes/p6/Fixtures.cs, ExitOnBreak). Nothing else in these tests exits with it.
    /// </summary>
    public const int BreakExitCode = 131;

    /// <summary>
    /// A cooperating child, the shape of P6's "coop" fixture: it registers a console control handler that, on
    /// CTRL_BREAK_EVENT (1), prints <see cref="BreakSeen"/> and calls ExitProcess(131); then prints
    /// <see cref="Armed"/> and sleeps 60 s. It never reads stdin, so closing stdin alone cannot end it (P6 control
    /// C2). Handlers run last-registered first, so this one runs before powershell.exe's own (which would enter the
    /// debugger and keep the process alive). Checked on this machine 2026-09-23 through a CREATE_NEW_PROCESS_GROUP |
    /// CREATE_NO_WINDOW pipe child and P6's AttachConsole helper: exit 131, output "BREAK-SEEN", 114 ms after the event.
    /// </summary>
    public static string CtrlBreakCooperating() =>
        """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class MarrowBreakFixture
        {
            public delegate bool Handler(uint type);
            [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
            [DllImport("kernel32.dll")] private static extern void ExitProcess(uint code);
            private static Handler keep;
            public static void Arm()
            {
                keep = OnSignal;
                SetConsoleCtrlHandler(keep, true);
            }
            private static bool OnSignal(uint type)
            {
                if (type != 1) return false;
                Console.Out.WriteLine("BREAK-SEEN");
                Console.Out.Flush();
                ExitProcess(131);
                return true;
            }
        }
        '@
        [MarrowBreakFixture]::Arm()
        [Console]::Out.WriteLine('BREAK-ARMED')
        [Console]::Out.Flush()
        Start-Sleep 60
        """;
}

/// <summary>The test host's own console attachment, to show the launcher never trades it away (probe P6 deviation 4).</summary>
public static class HostConsole
{
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetConsoleWindow();

    public static IntPtr Window => GetConsoleWindow();
}

/// <summary>
/// Tests that register process-wide console signal handlers in the test host (PosixSignalRegistration) run alone:
/// R-TEST-5 allows a process-global change only inside one non-parallel collection.
/// </summary>
[CollectionDefinition("Process signals", DisableParallelization = true)]
public sealed class ProcessSignalsCollection;

/// <summary>Real temporary folders under MARROW_PORT_DATA_DIR (R-TEST-9); never the user's profile.</summary>
public static class ProcessTestFolders
{
    public static string Create(string label)
    {
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Join(root, "process-launcher", label + " " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string Touch(string folder, string fileName)
    {
        var path = Path.Join(folder, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }
}

/// <summary>
/// CommandLineToArgvW, the parser R-PROC-1 names as the oracle for the quoting rules ("what CommandLineToArgvW
/// parses back"), and the one libuv (Node) uses to build process.argv on Windows (ED-024).
/// </summary>
public static class WindowsCommandLine
{
    [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CommandLineToArgv(string commandLine, out int count);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static IReadOnlyList<string> Parse(string commandLine)
    {
        var argv = CommandLineToArgv(commandLine, out var count);
        if (argv == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
