// PORT-ONLY: the stand-in for Foundation Process, Pipe and FileHandle.nullDevice (ADR-0002, R-PROC-1 to R-PROC-3).
// CreateProcessW with a Job Object and ConPTY in one attribute list (probe P5), the three termination shapes of probe
// P6 (ED-047, ED-126), MSVCRT argument quoting and the 32,767 character limit (ED-024), the environment block (PA-0009,
// ED-128), executable lookup (PA-0010, ED-031, ED-129, ED-130, ED-131) and the command interpreter test (PA-0016,
// ED-127). Its Swift callers: Shell.swift:36-118, StdioProcess.swift:17-128, MCPHub.swift:286-295, :329-366 and
// :547-556, MCPProbe.swift:119-133 and :227-236, HangWatchdog.swift:95-101.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Core.Port;

/// <summary>
/// What the termination ladders call on a running process (Shell.swift:110-118, StdioProcess.swift:119-128,
/// MCPHub.swift:547-556). <see cref="ProcessLauncher"/> implements it.
/// </summary>
public interface IProcessControl
{
    /// <summary>Swift <c>process.isRunning</c>.</summary>
    bool IsRunning { get; }

    /// <summary>Swift <c>process.terminate()</c>: the graceful step (ED-047, ED-126).</summary>
    void Terminate();

    /// <summary>Swift <c>kill(getpgid(pid) == pid ? -pid : pid, SIGKILL)</c>: the tree (ED-047 (a), TerminateJobObject).</summary>
    void KillTree();

    /// <summary>Swift <c>kill(process.processIdentifier, SIGKILL)</c>: the pid alone (ED-047 (b), TerminateProcess).</summary>
    void KillProcess();
}

/// <summary>
/// The three termination shapes of probe P6 ("Use three shapes, not one"), transcribed from the Swift. The delays go
/// through <see cref="GlobalQueue.AsyncAfter"/>, so <see cref="SwiftClock"/> drives them (R-PROC-1).
/// </summary>
public static class TerminationLadder
{
    /// <summary>Shell.swift:110-118 (the same at MCPHub.swift:286-295 and MCPProbe.swift:227-236): 1 s, then the tree.</summary>
    public static void Shell(IProcessControl process)
    {
        // :111
        if (!process.IsRunning) return;
        // :112
        process.Terminate();
        // :113-117 DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 1)
        GlobalQueue.AsyncAfter(TimeSpan.FromSeconds(1), () =>
        {
            // :114
            if (!process.IsRunning) return;
            // :115-116 kill(getpgid(pid) == pid ? -pid : pid, SIGKILL): the child leads its own group (it is created
            // with CREATE_NEW_PROCESS_GROUP in its own job), so the kill is the job's tree (ED-047 (a)).
            process.KillTree();
        });
    }

    /// <summary>
    /// StdioProcess.swift:119-128 as ED-126 rules it: stdin first, running or not; then, if still running, CTRL_BREAK
    /// to the child's process group; at +3 s TerminateProcess on that pid alone, with no job.
    /// </summary>
    public static void Stdio(IProcessControl process, Action closeInput)
    {
        // :120-121 writeQueue.async { try? handle.close() }: the caller's action queues the close on its write queue.
        closeInput();
        // :122
        if (!process.IsRunning) return;
        // :123
        process.Terminate();
        // :125-127 DispatchQueue.global().asyncAfter(deadline: .now() + 3)
        GlobalQueue.AsyncAfter(TimeSpan.FromSeconds(3), () =>
        {
            // :126 if process.isRunning { kill(process.processIdentifier, SIGKILL) }
            if (process.IsRunning) process.KillProcess();
        });
    }

    /// <summary>MCPHub.swift:547-556 (MCPLiveProcesses.terminateAll, at quit): signal each, then kill each still running, no gap.</summary>
    public static void TerminateAll(IReadOnlyList<IProcessControl> processes)
    {
        // :548 let running = processes.filter(\.isRunning)
        var running = new List<IProcessControl>();
        foreach (var process in processes)
            if (process.IsRunning) running.Add(process);
        // :549-551
        foreach (var process in running) process.Terminate();
        // :552-555 for process in running where process.isRunning { kill(group or pid, SIGKILL) }
        foreach (var process in running)
            if (process.IsRunning) process.KillTree();
    }
}

/// <summary>The size of a pseudoconsole in character cells (CreatePseudoConsole's COORD, probe P5).</summary>
public readonly record struct PseudoConsoleSize(int Columns, int Rows);

/// <summary>
/// ED-024 (3): the assembled command line is longer than CreateProcessW takes (32,767 UTF-16 units including the
/// terminating NUL), so nothing was started. Never truncated.
/// </summary>
public sealed class CommandLineTooLongException(int length) : Exception(
    string.Create(CultureInfo.InvariantCulture, $"The command line is {length} characters long; Windows takes at most {ProcessLauncher.MaximumCommandLineLength}."))
{
    /// <summary>The length of the assembled command line in UTF-16 code units.</summary>
    public int Length { get; } = length;
}

/// <summary>
/// Foundation <c>Pipe</c>: an anonymous pipe whose two ends exist from construction. The launcher hands the child
/// its end and closes the parent's copy of it after the spawn, as Foundation does, so end of file arrives.
/// </summary>
public sealed class ProcessPipe
{
    private const int ChunkSize = 65_536;
    private readonly object gate = new();
    private Action<byte[]>? readabilityHandler;
    private bool readerRunning;

    public ProcessPipe()
    {
        if (!ProcessNative.CreatePipe(out var read, out var write, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        ReadEnd = read;
        WriteEnd = write;
    }

    internal SafeFileHandle ReadEnd { get; }

    internal SafeFileHandle WriteEnd { get; }

    /// <summary>
    /// <c>fileHandleForReading.readabilityHandler</c>: each chunk as it is read, off the main actor and outside any
    /// task; an empty array once at end of file. Set to null, nothing more reaches the handler that was set before.
    /// </summary>
    public Action<byte[]>? ReadabilityHandler
    {
        get
        {
            lock (gate) return readabilityHandler;
        }
        set
        {
            lock (gate)
            {
                readabilityHandler = value;
                if (value is null || readerRunning) return;
                readerRunning = true;
            }
            ProcessNative.StartBackground("Marrow pipe reader", ReadLoop);
        }
    }

    /// <summary><c>fileHandleForWriting.write(contentsOf:)</c>; an IOException once the reading side is gone (P6: 232 or 109).</summary>
    public void Write(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (WriteEnd)
        {
            if (WriteEnd.IsClosed) throw new IOException("The pipe's writing end is closed.");
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var offset = 0;
                while (offset < data.Length)
                {
                    if (!ProcessNative.WriteFile(WriteEnd, pinned.AddrOfPinnedObject() + offset, data.Length - offset, out var written, IntPtr.Zero))
                        throw ProcessNative.IOError(Marshal.GetLastWin32Error());
                    offset += written;
                }
            }
            finally
            {
                pinned.Free();
            }
        }
    }

    /// <summary><c>fileHandleForWriting.close()</c>.</summary>
    public void CloseWriting()
    {
        lock (WriteEnd) WriteEnd.Dispose();
    }

    /// <summary><c>fileHandleForReading.readDataToEndOfFile()</c>.</summary>
    public byte[] ReadDataToEndOfFile()
    {
        using var all = new MemoryStream();
        var buffer = new byte[ChunkSize];
        while (Read(buffer) is var count && count >= 0) all.Write(buffer, 0, count);
        return all.ToArray();
    }

    /// <summary>The parent's copy of the reading end, once the child has its own (a stdin pipe after the spawn).</summary>
    internal void CloseReadEndInParent() => ReadEnd.Dispose();

    /// <summary>The parent's copy of the writing end, once the child has its own (an output pipe after the spawn).</summary>
    internal void CloseWriteEndInParent()
    {
        lock (WriteEnd) WriteEnd.Dispose();
    }

    /// <summary>Bytes read into <paramref name="buffer"/>, 0 for an empty read, -1 at end of file.</summary>
    private int Read(byte[] buffer)
    {
        if (ReadEnd.IsClosed) return -1;
        return ProcessNative.ReadFile(ReadEnd, buffer, buffer.Length, out var read, IntPtr.Zero) ? read : -1;
    }

    private void ReadLoop()
    {
        var buffer = new byte[ChunkSize];
        while (true)
        {
            var count = Read(buffer);
            if (count == 0) continue; // a zero-length write, not the end
            Action<byte[]>? handler;
            lock (gate)
            {
                handler = readabilityHandler;
                if (handler is null || count < 0) readerRunning = false;
            }
            if (handler is null) return;
            if (count < 0)
            {
                SwiftTask.RunOutsideTask(() => handler([]));
                return;
            }
            var chunk = buffer.AsSpan(0, count).ToArray();
            SwiftTask.RunOutsideTask(() => handler(chunk));
        }
    }
}

/// <summary>
/// Foundation <c>Process</c> on Windows (ADR-0002): CreateProcessW with the child's job (PROC_THREAD_ATTRIBUTE_JOB_LIST)
/// and, for the terminal, its pseudoconsole (PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE) in one attribute list, so the child
/// is in its job from its first instruction (P5 A3). A pipe child gets its own hidden console and its own process
/// group (CREATE_NO_WINDOW, CREATE_NEW_PROCESS_GROUP, P6), and inherits exactly its three standard handles
/// (PROC_THREAD_ATTRIBUTE_HANDLE_LIST).
/// </summary>
public sealed class ProcessLauncher : IProcessControl
{
    /// <summary>ED-024 (3): lpCommandLine takes 32,767 characters including the terminating NUL (measured: 32,767 fails with 206).</summary>
    public const int MaximumCommandLineLength = 32_766;

    /// <summary>
    /// The exit code a forced kill leaves: 9, the terminationStatus Foundation reports after SIGKILL
    /// (StdioProcess.swift:126, Shell.swift:116). Which status a force-stopped tool reports on Windows is owner question
    /// 5 (ED-126); no test pins it.
    /// </summary>
    private const uint ForcedExitCode = 9;

    private readonly object gate = new();
    private readonly ManualResetEventSlim exited = new();
    private SafeProcessHandle? process;
    private SafeWaitHandle? job;
    private IntPtr pseudoConsole;
    private bool launched;
    private volatile bool running;
    private int processIdentifier;
    private int terminationStatus;

    /// <summary>Swift <c>executableURL</c>, as a path.</summary>
    public string? ExecutableURL { get; set; }

    /// <summary>Swift <c>arguments</c>.</summary>
    public IReadOnlyList<string> Arguments { get; set; } = [];

    /// <summary>Swift <c>currentDirectoryURL</c>; null: the parent's.</summary>
    public string? CurrentDirectoryURL { get; set; }

    /// <summary>Swift <c>environment</c>; null inherits, a value replaces the whole block (Shell.swift:49).</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; set; }

    /// <summary>Swift <c>standardInput</c>; null is <c>FileHandle.nullDevice</c> (Shell.swift:56).</summary>
    public ProcessPipe? StandardInput { get; set; }

    /// <summary>Swift <c>standardOutput</c>; null is the null device.</summary>
    public ProcessPipe? StandardOutput { get; set; }

    /// <summary>Swift <c>standardError</c>; null is the null device.</summary>
    public ProcessPipe? StandardError { get; set; }

    /// <summary>
    /// True (Shell, MCPHub, MCPProbe): the child runs in its own job, and <see cref="KillTree"/> ends the tree. False
    /// (StdioProcess, ED-047 (b)): no job, so a grandchild survives the kill as on macOS.
    /// </summary>
    public bool UsesJobObject { get; set; } = true;

    /// <summary>Set: the child runs on a pseudoconsole of this size, fed by StandardInput and read on StandardOutput (P5).</summary>
    public PseudoConsoleSize? PseudoConsole { get; set; }

    /// <summary>Swift <c>terminationHandler</c>: once after exit, off the main actor and outside any task.</summary>
    public Action<ProcessLauncher>? TerminationHandler { get; set; }

    /// <summary>Swift <c>isRunning</c>.</summary>
    public bool IsRunning => running;

    /// <summary>Swift <c>processIdentifier</c>; 0 before the launch.</summary>
    public int ProcessIdentifier => Volatile.Read(ref processIdentifier);

    /// <summary>Swift <c>terminationStatus</c>: the child's exit code.</summary>
    public int TerminationStatus => Volatile.Read(ref terminationStatus);

    /// <summary>
    /// libuv's quote_cmd_arg, the MSVCRT CommandLineToArgvW form ED-024 names: an empty argument is two quotes; one with
    /// no space, tab or quote is unchanged; one with no quote and no backslash is wrapped; otherwise each quote is
    /// escaped and each run of backslashes before a quote or before the closing quote is doubled.
    /// </summary>
    public static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length == 0) return "\"\"";
        if (argument.AsSpan().IndexOfAny(" \t\"") < 0) return argument;
        if (argument.AsSpan().IndexOfAny("\"\\") < 0) return "\"" + argument + "\"";
        // Built back to front, as libuv does, then reversed.
        var reversed = new StringBuilder(argument.Length * 2 + 2);
        var quoteHit = true;
        for (var i = argument.Length; i > 0; i--)
        {
            var character = argument[i - 1];
            reversed.Append(character);
            if (quoteHit && character == '\\')
            {
                reversed.Append('\\');
            }
            else if (character == '"')
            {
                quoteHit = true;
                reversed.Append('\\');
            }
            else
            {
                quoteHit = false;
            }
        }
        var characters = reversed.ToString().ToCharArray();
        Array.Reverse(characters);
        return "\"" + new string(characters) + "\"";
    }

    /// <summary>argv[0] is the executable path (Foundation passes the launch path), then each argument, all quoted, one space apart.</summary>
    public static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var line = new StringBuilder(QuoteArgument(executable));
        foreach (var argument in arguments) line.Append(' ').Append(QuoteArgument(argument));
        return line.ToString();
    }

    /// <summary>
    /// The CreateProcessW environment block: "KEY=VALUE" and NUL per entry, sorted by name case-insensitively in
    /// ordinal order as CreateProcessW requires, then one more NUL. Names differing only in case fold as successive
    /// OrdinalIgnoreCase assignments in the input's enumeration order, first spelling and last value (ED-128 (2), a
    /// defensive fallback: environment dictionaries are OrdinalIgnoreCase by ED-128 (1)).
    /// </summary>
    public static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment)
        {
            spellings.TryAdd(name, name);
            values[name] = value;
        }
        var block = new StringBuilder();
        foreach (var name in spellings.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            block.Append(name).Append('=').Append(values[name]).Append('\0');
        if (block.Length == 0) block.Append('\0');
        return block.Append('\0').ToString();
    }

    /// <summary>
    /// Shell.swift:415-427 <c>LoginEnvironment.which</c> with the Windows rules: a name holding '/' or '\', or
    /// drive-qualified, is a path, tilde-expanded (ED-130) and tested itself, never searched on PATH (ED-031 (3));
    /// otherwise each non-empty ';'-separated PATH entry joined with the name is tested, first match wins (ED-031 (1),
    /// (4)). The test (ED-129): a name whose extension is in PATHEXT is tried as written, any other name with each
    /// PATHEXT extension appended in PATHEXT's order; a candidate passes when it exists, a directory included
    /// (ED-131, Darwin's access(X_OK)). <paramref name="home"/> replaces a leading '~'; null is the user's profile.
    /// </summary>
    public static string? Which(string name, string path, string pathExt, string? home = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pathExt);
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);
        // :417-420
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal) || IsDriveQualified(name))
            return ExecutableCandidate(ExpandingTilde(name, home), extensions);
        // :421-425 (path.split(separator:) drops empty pieces)
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ExecutableCandidate(Path.Join(directory, name), extensions) is { } found) return found;
        }
        // :426
        return null;
    }

    /// <summary>
    /// PA-0016 steps 3 and 2 (ED-127): the canonical %SystemRoot%\System32\cmd.exe or %SystemRoot%\SysWOW64\cmd.exe
    /// in any letter case, or a file whose version resource names the Windows command interpreter: CompanyName
    /// "Microsoft Corporation" with OriginalFilename "Cmd.Exe" or "Cmd.Exe.MUI", or FileDescription "Windows Command
    /// Processor". ED-020's %SystemRoot% directory exclusion is the Command Code resolver's, not this test's.
    /// </summary>
    public static bool IsWindowsCommandInterpreter(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = SwiftTry.Optional(() => Path.GetFullPath(path)) ?? path;
        var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        // Step 3
        foreach (var system in (string[])["System32", "SysWOW64"])
        {
            if (string.Equals(full, Path.Join(root, system, "cmd.exe"), StringComparison.OrdinalIgnoreCase)) return true;
        }
        // Step 2
        if (!File.Exists(full)) return false;
        var info = SwiftTry.Optional(() => FileVersionInfo.GetVersionInfo(full));
        if (info is null) return false;
        return (info.CompanyName == "Microsoft Corporation" && info.OriginalFilename is "Cmd.Exe" or "Cmd.Exe.MUI")
            || info.FileDescription == "Windows Command Processor";
    }

    /// <summary>
    /// Swift <c>process.run()</c>. Throws when the process cannot be started; then no process exists and the
    /// termination handler is never called.
    /// </summary>
    public void Run()
    {
        var executable = ExecutableURL ?? throw new InvalidOperationException("No executable was set.");
        lock (gate)
        {
            if (launched) throw new InvalidOperationException("The process was already launched.");
        }
        RefuseCommandInterpreterScripts(executable);
        var commandLine = BuildCommandLine(executable, Arguments);
        if (commandLine.Length > MaximumCommandLineLength) throw new CommandLineTooLongException(commandLine.Length);
        lock (gate)
        {
            if (launched) throw new InvalidOperationException("The process was already launched.");
            Spawn(executable, commandLine);
            launched = true;
            running = true;
        }
        ProcessNative.StartBackground("Marrow process watcher", Watch);
    }

    /// <summary>Swift <c>waitUntilExit()</c>: returns once the process has exited (at once when it never ran).</summary>
    public void WaitUntilExit()
    {
        lock (gate)
        {
            if (!launched) return;
        }
        exited.Wait();
    }

    /// <summary>
    /// Swift <c>process.terminate()</c>, the graceful step (ED-047, ED-126). A pipe child: CTRL_BREAK_EVENT to the
    /// child's process group on its own hidden console, sent by a separate helper process that attaches to that console
    /// (P6: AttachConsole is process-wide, so the app never trades its own console away). A pseudoconsole child: 0x03
    /// into its input (P6). Never a forced kill.
    /// </summary>
    public void Terminate()
    {
        if (!running) return;
        if (PseudoConsole is not null)
        {
            SwiftTry.Ignore(() => StandardInput?.Write([0x03]));
            return;
        }
        var pid = ProcessIdentifier;
        if (pid > 0) ProcessNative.SendCtrlBreak(pid);
    }

    /// <summary>The group kill (ED-047 (a)): TerminateJobObject; with no job, the pid alone.</summary>
    public void KillTree()
    {
        lock (gate)
        {
            if (job is { IsClosed: false } live)
            {
                ProcessNative.TerminateJobObject(live, ForcedExitCode);
                return;
            }
            if (process is { IsClosed: false } child) ProcessNative.TerminateProcess(child, ForcedExitCode);
        }
    }

    /// <summary>The pid alone (ED-047 (b)): TerminateProcess, no job, so grandchildren survive.</summary>
    public void KillProcess()
    {
        lock (gate)
        {
            if (process is { IsClosed: false } child) ProcessNative.TerminateProcess(child, ForcedExitCode);
        }
    }

    /// <summary>ResizePseudoConsole for a pseudoconsole child (P5 A4); nothing otherwise.</summary>
    public void ResizePseudoConsole(PseudoConsoleSize size)
    {
        lock (gate)
        {
            if (pseudoConsole == IntPtr.Zero) return;
            var hr = ProcessNative.ResizePseudoConsole(pseudoConsole, ProcessNative.Coord.From(size));
            if (hr != 0) throw Marshal.GetExceptionForHR(hr) ?? new Win32Exception(hr);
        }
    }

    /// <summary>
    /// STOP F.ProcessLauncher #4, INTERIM ruling (owner question 4): CreateProcessW runs a .cmd or .bat through
    /// cmd.exe, which re-parses the arguments by its own rules, where Foundation passes the argv with no shell. So a
    /// target whose full path ends in .cmd or .bat is refused before CreateProcessW. GetFullPath strips trailing dots
    /// and spaces, which CreateProcessW also ignores (measured: 'tool.cmd.' and 'tool.cmd ' reach cmd.exe).
    /// </summary>
    private static void RefuseCommandInterpreterScripts(string executable)
    {
        var full = SwiftTry.Optional(() => Path.GetFullPath(executable)) ?? executable;
        var extension = Path.GetExtension(full);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("A .cmd or .bat file cannot be launched directly: " + executable);
    }

    private static bool IsDriveQualified(string name) => name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':';

    /// <summary>
    /// Shell.swift:418 <c>(name as NSString).expandingTildeInPath</c> with ED-130: a leading '~' followed by '/' or
    /// '\' becomes the home folder; '~name' stays as written.
    /// </summary>
    private static string ExpandingTilde(string name, string? home)
    {
        if (name.Length < 2 || name[0] != '~' || (name[1] != '/' && name[1] != '\\')) return name;
        var folder = home ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return folder.TrimEnd('\\', '/') + name[1..];
    }

    /// <summary>ED-129 and ED-131: the candidate itself when its extension is in PATHEXT, else candidate + each PATHEXT extension.</summary>
    private static string? ExecutableCandidate(string candidate, string[] extensions)
    {
        var extension = Path.GetExtension(candidate);
        if (extension.Length > 0 && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return Exists(candidate) ? candidate : null;
        foreach (var appended in extensions)
        {
            if (Exists(candidate + appended)) return candidate + appended;
        }
        return null;
    }

    /// <summary>R-FS-1: fileExists is File.Exists || Directory.Exists.</summary>
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private void Spawn(string executable, string commandLine)
    {
        var size = PseudoConsole;
        var toClose = new List<SafeHandle>();
        IntPtr attributeList = IntPtr.Zero;
        var attributeListReady = false;
        IntPtr handleArray = IntPtr.Zero;
        IntPtr jobArray = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        SafeWaitHandle? newJob = null;
        IntPtr newPseudoConsole = IntPtr.Zero;
        var started = false;
        try
        {
            var startup = new ProcessNative.StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<ProcessNative.StartupInfoEx>();
            startup.StartupInfo.dwFlags = ProcessNative.STARTF_USESTDHANDLES;
            var inheritHandles = false;
            var flags = ProcessNative.EXTENDED_STARTUPINFO_PRESENT | ProcessNative.CREATE_UNICODE_ENVIRONMENT;
            var attributeCount = 1 + (UsesJobObject ? 1 : 0);
            var inherited = new List<IntPtr>();
            if (size is { } console)
            {
                // P5: the pseudoconsole reads the input pipe and writes the output pipe; the child gets null standard
                // handles and inherits nothing, so it sees the console, not this host's handles.
                var input = StandardInput ?? throw new InvalidOperationException("A pseudoconsole needs StandardInput.");
                var output = StandardOutput ?? throw new InvalidOperationException("A pseudoconsole needs StandardOutput.");
                var hr = ProcessNative.CreatePseudoConsole(ProcessNative.Coord.From(console), input.ReadEnd, output.WriteEnd, 0, out newPseudoConsole);
                if (hr != 0) throw Marshal.GetExceptionForHR(hr) ?? new Win32Exception(hr);
            }
            else
            {
                // P6: a hidden console of its own and a process group of its own, so CTRL_BREAK reaches only its group.
                flags |= ProcessNative.CREATE_NEW_PROCESS_GROUP | ProcessNative.CREATE_NO_WINDOW;
                inheritHandles = true;
                SafeFileHandle? nullDevice = null;
                SafeFileHandle NullDevice()
                {
                    if (nullDevice is null)
                    {
                        nullDevice = ProcessNative.OpenNullDevice();
                        toClose.Add(nullDevice);
                    }
                    return nullDevice;
                }
                var sources = new Dictionary<IntPtr, IntPtr>();
                IntPtr Inheritable(SafeFileHandle source)
                {
                    var raw = source.DangerousGetHandle();
                    if (sources.TryGetValue(raw, out var existing)) return existing;
                    var copy = ProcessNative.DuplicateInheritable(source);
                    toClose.Add(copy);
                    sources[raw] = copy.DangerousGetHandle();
                    inherited.Add(copy.DangerousGetHandle());
                    return copy.DangerousGetHandle();
                }
                startup.StartupInfo.hStdInput = Inheritable(StandardInput?.ReadEnd ?? NullDevice());
                startup.StartupInfo.hStdOutput = Inheritable(StandardOutput?.WriteEnd ?? NullDevice());
                startup.StartupInfo.hStdError = Inheritable(StandardError?.WriteEnd ?? NullDevice());
            }
            if (UsesJobObject)
            {
                newJob = ProcessNative.CreateJobObjectW(IntPtr.Zero, null);
                if (newJob.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var listSize = IntPtr.Zero;
            ProcessNative.InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref listSize); // the sizing call fails by design
            attributeList = Marshal.AllocHGlobal(listSize);
            if (!ProcessNative.InitializeProcThreadAttributeList(attributeList, attributeCount, 0, ref listSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            attributeListReady = true;
            if (newPseudoConsole != IntPtr.Zero)
            {
                ProcessNative.Update(attributeList, ProcessNative.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, newPseudoConsole, IntPtr.Size);
            }
            else
            {
                handleArray = Marshal.AllocHGlobal(IntPtr.Size * inherited.Count);
                for (var i = 0; i < inherited.Count; i++) Marshal.WriteIntPtr(handleArray, i * IntPtr.Size, inherited[i]);
                ProcessNative.Update(attributeList, ProcessNative.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handleArray, IntPtr.Size * inherited.Count);
            }
            if (newJob is not null)
            {
                // The array of job handles must stay valid until CreateProcessW returns (P5).
                jobArray = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobArray, newJob.DangerousGetHandle());
                ProcessNative.Update(attributeList, ProcessNative.PROC_THREAD_ATTRIBUTE_JOB_LIST, jobArray, IntPtr.Size);
            }
            startup.lpAttributeList = attributeList;

            if (Environment is { } replaced) environmentBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(replaced));
            var line = (commandLine + "\0").ToCharArray();
            if (!ProcessNative.CreateProcessW(executable, line, IntPtr.Zero, IntPtr.Zero, inheritHandles, flags, environmentBlock,
                    CurrentDirectoryURL, ref startup, out var information))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            started = true;
            ProcessNative.CloseHandle(information.hThread);
            process = new SafeProcessHandle(information.hProcess, ownsHandle: true);
            job = newJob;
            pseudoConsole = newPseudoConsole;
            Volatile.Write(ref processIdentifier, information.dwProcessId);
        }
        finally
        {
            if (attributeListReady) ProcessNative.DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            if (handleArray != IntPtr.Zero) Marshal.FreeHGlobal(handleArray);
            if (jobArray != IntPtr.Zero) Marshal.FreeHGlobal(jobArray);
            if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            foreach (var handle in toClose) handle.Dispose();
            if (!started)
            {
                newJob?.Dispose();
                if (newPseudoConsole != IntPtr.Zero) ProcessNative.ClosePseudoConsole(newPseudoConsole);
            }
        }
        // As Foundation does after the launch: the parent closes its copies of the child's pipe ends, so the child's
        // exit (or its own close) is the end of file for the readers and a dead reader fails the writer. For a
        // pseudoconsole those ends belong to the console host now (P5).
        StandardInput?.CloseReadEndInParent();
        StandardOutput?.CloseWriteEndInParent();
        if (size is null) StandardError?.CloseWriteEndInParent();
    }

    private void Watch()
    {
        SafeProcessHandle child;
        lock (gate) child = process!;
        ProcessNative.WaitForSingleObject(child, ProcessNative.INFINITE);
        var status = ProcessNative.GetExitCodeProcess(child, out var code) ? unchecked((int)code) : 0;
        lock (gate)
        {
            if (pseudoConsole != IntPtr.Zero)
            {
                // P5's order: the child is gone, then ClosePseudoConsole while the output reader still drains.
                ProcessNative.ClosePseudoConsole(pseudoConsole);
                pseudoConsole = IntPtr.Zero;
            }
            // The job ends with the child: a grandchild that outlives a child which was never killed lives on, as
            // on macOS, where only the kill steps reach the group. The job sets no kill-on-close limit.
            job?.Dispose();
            Volatile.Write(ref terminationStatus, status);
            running = false;
        }
        exited.Set();
        if (TerminationHandler is { } handler) SwiftTask.RunOutsideTask(() => handler(this));
    }
}

/// <summary>The Win32 calls behind <see cref="ProcessLauncher"/> and <see cref="ProcessPipe"/>.</summary>
internal static class ProcessNative
{
    public const uint INFINITE = 0xFFFFFFFF;
    public const int STARTF_USESTDHANDLES = 0x00000100;
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const uint DETACHED_PROCESS = 0x00000008;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x0002000D;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ_WRITE = 0x00000003;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct Coord
    {
        public short X;
        public short Y;

        public static Coord From(PseudoConsoleSize size) => new() { X = checked((short)size.Columns), Y = checked((short)size.Rows) };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int count, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeHandle source, IntPtr targetProcess, out SafeFileHandle target,
        uint access, bool inherit, uint options);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr attributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size,
        IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool CreateProcessW(string? applicationName, char[] commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern SafeWaitHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool TerminateJobObject(SafeWaitHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    public static void Update(IntPtr list, IntPtr attribute, IntPtr value, int size)
    {
        if (!UpdateProcThreadAttribute(list, 0, attribute, value, (IntPtr)size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>An inheritable duplicate of <paramref name="source"/>, for the child's handle list only.</summary>
    public static SafeFileHandle DuplicateInheritable(SafeHandle source)
    {
        var self = GetCurrentProcess();
        if (!DuplicateHandle(self, source, self, out var copy, 0, true, DUPLICATE_SAME_ACCESS))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return copy;
    }

    /// <summary>FileHandle.nullDevice: NUL, readable and writable.</summary>
    public static SafeFileHandle OpenNullDevice()
    {
        var handle = CreateFile("NUL", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    /// <summary>The IOException a failed pipe write raises, carrying the Win32 code in its HResult (P6: 232 or 109).</summary>
    public static IOException IOError(int error) =>
        new(new Win32Exception(error).Message, unchecked((int)(0x80070000u | (uint)(error & 0xFFFF))));

    /// <summary>A background thread that carries no execution context: no task, no main actor, no ambient override.</summary>
    public static void StartBackground(string name, Action work)
    {
        var thread = new Thread(() => work()) { IsBackground = true, Name = name };
        using (ExecutionContext.SuppressFlow()) thread.Start();
    }

    /// <summary>
    /// The CTRL_BREAK helper of probe P6: a separate process, created with a hidden console in a process group of its
    /// own, that runs FreeConsole, AttachConsole(pid), GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, pid), FreeConsole.
    /// P6 used its own exe in a helper mode; a library has no entry point, so the helper here is Windows PowerShell
    /// declaring the three kernel32 calls through Reflection.Emit (no compiler run). It is not awaited: like
    /// Foundation's terminate(), the call returns at once, and the kill deadlines run from the call.
    /// </summary>
    public static void SendCtrlBreak(int pid)
    {
        var target = pid.ToString(CultureInfo.InvariantCulture);
        var script =
            "$a=[AppDomain]::CurrentDomain.DefineDynamicAssembly((New-Object Reflection.AssemblyName 'MarrowCtrlBreak'),'Run')\n" +
            "$t=$a.DefineDynamicModule('MarrowCtrlBreak').DefineType('MarrowCtrlBreak','Public,Class')\n" +
            "foreach($d in @(@('FreeConsole',[bool],[Type[]]@()),@('AttachConsole',[bool],[Type[]]@([uint32])),@('GenerateConsoleCtrlEvent',[bool],[Type[]]@([uint32],[uint32])))){" +
            "$m=$t.DefinePInvokeMethod($d[0],'kernel32.dll','Public,Static,PinvokeImpl',[Reflection.CallingConventions]::Standard,$d[1],$d[2]," +
            "[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Unicode);$m.SetImplementationFlags('PreserveSig')}\n" +
            "$k=$t.CreateType()\n" +
            "$null=$k::FreeConsole()\n" +
            "if(-not $k::AttachConsole(" + target + ")){exit 1}\n" +
            "$sent=$k::GenerateConsoleCtrlEvent(1," + target + ")\n" +
            // P6 Fixtures.cs:107 retains the console for 50 ms while the event is dispatched.
            "[Threading.Thread]::Sleep(50)\n" +
            "$null=$k::FreeConsole()\n" +
            "if($sent){exit 0}else{exit 2}\n";
        var powerShell = Path.Join(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var commandLine = ProcessLauncher.BuildCommandLine(powerShell,
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]) + "\0";
        var startup = new StartupInfoEx();
        startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
        // Null standard handles: the helper inherits nothing from the app.
        startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        if (!CreateProcessW(powerShell, commandLine.ToCharArray(), IntPtr.Zero, IntPtr.Zero, false,
                CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero, null, ref startup, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(information.hThread);
        CloseHandle(information.hProcess);
    }
}
