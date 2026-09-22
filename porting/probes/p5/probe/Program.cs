// P5 (task packet T-M0-004). Throwaway probe, never production code.
//
// QUESTION: from C# P/Invoke, can ONE proc-thread attribute list carry both
// PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE and PROC_THREAD_ATTRIBUTE_JOB_LIST, so that CreateProcessW
// starts the child already attached to a ConPTY and already inside a Job Object, with no window in
// which the child runs outside the job?
//
// PASS CONDITIONS (case A), all checked mechanically at the end:
//   A1 pty output arrives: a stdout marker AND a stderr marker from the child both arrive on the
//      ConPTY output pipe, and commands written to the ConPTY input pipe are answered (both
//      directions of the pty carry data). One servicing thread per pipe.
//   A2 the child sees real console handles (stdout and stdin not redirected), i.e. it is on the pty.
//   A3 the child is in the job the moment CreateProcessW returns (IsProcessInJob before any other
//      call), and the creation flags are EXTENDED_STARTUPINFO_PRESENT and nothing else.
//   A4 ResizePseudoConsole changes the column count the CHILD reports (120 -> 73 -> 150), and two
//      queries with no resize between them agree (so the change is caused by the resize).
//   A5 TerminateJobObject kills the child AND the grandchild it spawned: both process handles
//      signal, both exit codes equal the code given to TerminateJobObject, the job's active process
//      count goes 2 -> 0, and neither PID is present afterwards.
//
// CONTROL (case B), which must FAIL to kill: the same fixture, the same pseudoconsole, the same
// job, but the attribute list carries only the pseudoconsole (count 1, no JOB_LIST).
//   B1 the child is NOT in the job; B2 TerminateJobObject on that job leaves child and grandchild
//      alive; B3 killing only the child (TerminateProcess) leaves the grandchild alive.
//   If the grandchild died in case B, case A's kill could not be attributed to the job.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public static class Native
{
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const int STARTF_USESTDHANDLES = 0x00000100;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;
    public static readonly IntPtr PROC_THREAD_ATTRIBUTE_JOB_LIST = (IntPtr)0x0002000D;
    public const uint WAIT_OBJECT_0 = 0, WAIT_TIMEOUT = 0x102;
    public const uint SYNCHRONIZE = 0x00100000, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_TERMINATE = 0x1;
    public const int JobObjectBasicAccountingInformation = 1, JobObjectExtendedLimitInformation = 9;
    public const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    // Blittable: only int / short / IntPtr fields, no strings, no bools, no arrays.
    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);
    [DllImport("kernel32.dll")]
    public static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);
    [DllImport("kernel32.dll")]
    public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
    [DllImport("kernel32.dll")]
    public static extern void ClosePseudoConsole(IntPtr hPC);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue,
        IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreateProcessW(IntPtr lpApplicationName, IntPtr lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        IntPtr lpCurrentDirectory, ref STARTUPINFOEXW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, IntPtr lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr info, int cbInfo);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(IntPtr hJob, int infoClass, IntPtr info, int cbInfo, IntPtr returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsProcessInJob(IntPtr hProcess, IntPtr hJob, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr h, byte[] buffer, int toRead, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr h, byte[] buffer, int toWrite, out int written, IntPtr overlapped);
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();
}

public sealed class PtySession
{
    public IntPtr Hpc, InWrite, OutRead;
    public Native.PROCESS_INFORMATION Pi;
    public uint CreationFlags;
    public int AttributeCount;
    public bool InJobAtCreate;
    public int ReaderThreadId, WriterThreadId;
    public long BytesRead;
    public double FirstByteMs = -1;
    public string ReaderEnd = "(running)";
    public string WriterNote = "(running)";

    readonly object gate = new();
    readonly StringBuilder raw = new();
    readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    readonly BlockingCollection<string> input = new();
    readonly Stopwatch clock = new();
    Thread? reader, writer;

    static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[()#][0-9A-Za-z]|\x1B[=>78DEMc]", RegexOptions.Compiled);

    static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException($"{what} failed, GetLastError={Marshal.GetLastWin32Error()}");
    }

    public static PtySession Start(string exe, string args, IntPtr jobForAttribute, IntPtr jobToCheck, short cols, short rows)
    {
        var s = new PtySession();
        Check(Native.CreatePipe(out IntPtr ptyInRead, out s.InWrite, IntPtr.Zero, 0), "CreatePipe(input)");
        Check(Native.CreatePipe(out s.OutRead, out IntPtr ptyOutWrite, IntPtr.Zero, 0), "CreatePipe(output)");
        int hr = Native.CreatePseudoConsole(new Native.COORD { X = cols, Y = rows }, ptyInRead, ptyOutWrite, 0, out s.Hpc);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole HRESULT 0x{hr:X8}");
        // The pseudoconsole holds its own duplicates; the host keeps only its ends.
        Native.CloseHandle(ptyInRead);
        Native.CloseHandle(ptyOutWrite);

        // One servicing thread per channel, started before the child exists so output is drained
        // from the first byte (Microsoft's ConPTY deadlock warning).
        s.clock.Start();
        s.reader = new Thread(s.ReadLoop) { IsBackground = true, Name = "pty-output" };
        s.writer = new Thread(s.WriteLoop) { IsBackground = true, Name = "pty-input" };
        s.reader.Start();
        s.writer.Start();

        s.AttributeCount = jobForAttribute != IntPtr.Zero ? 2 : 1;
        IntPtr size = IntPtr.Zero;
        Native.InitializeProcThreadAttributeList(IntPtr.Zero, s.AttributeCount, 0, ref size); // sizing call, fails by design
        IntPtr list = Marshal.AllocHGlobal(size);
        IntPtr jobArray = IntPtr.Zero;
        IntPtr cmd = IntPtr.Zero;
        try
        {
            Check(Native.InitializeProcThreadAttributeList(list, s.AttributeCount, 0, ref size), "InitializeProcThreadAttributeList");
            // PSEUDOCONSOLE takes the HPCON value itself as lpValue.
            Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, s.Hpc,
                (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute(PSEUDOCONSOLE)");
            if (jobForAttribute != IntPtr.Zero)
            {
                // JOB_LIST takes a pointer to an array of job HANDLEs; the memory must outlive CreateProcessW.
                jobArray = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobArray, jobForAttribute);
                Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_JOB_LIST, jobArray,
                    (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute(JOB_LIST)");
            }

            var si = new Native.STARTUPINFOEXW();
            si.StartupInfo.cb = Marshal.SizeOf<Native.STARTUPINFOEXW>();
            // Null std handles with STARTF_USESTDHANDLES: the child must not inherit this host's
            // (possibly redirected) std handles; the pseudoconsole supplies its console.
            si.StartupInfo.dwFlags = Native.STARTF_USESTDHANDLES;
            si.lpAttributeList = list;

            s.CreationFlags = Native.EXTENDED_STARTUPINFO_PRESENT;
            cmd = Marshal.StringToHGlobalUni($"\"{exe}\" {args}");
            Check(Native.CreateProcessW(IntPtr.Zero, cmd, IntPtr.Zero, IntPtr.Zero, false, s.CreationFlags,
                IntPtr.Zero, IntPtr.Zero, ref si, out s.Pi), "CreateProcessW");
            // First call after CreateProcessW returns: is the child already in the job?
            Check(Native.IsProcessInJob(s.Pi.hProcess, jobToCheck, out s.InJobAtCreate), "IsProcessInJob");
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            if (jobArray != IntPtr.Zero) Marshal.FreeHGlobal(jobArray);
            if (cmd != IntPtr.Zero) Marshal.FreeHGlobal(cmd);
        }
        return s;
    }

    void ReadLoop()
    {
        ReaderThreadId = Environment.CurrentManagedThreadId;
        var buf = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];
        while (true)
        {
            if (!Native.ReadFile(OutRead, buf, buf.Length, out int n, IntPtr.Zero) || n == 0)
            {
                ReaderEnd = $"ReadFile returned end, GetLastError={Marshal.GetLastWin32Error()} (109 = ERROR_BROKEN_PIPE)";
                return;
            }
            int c = decoder.GetChars(buf, 0, n, chars, 0);
            lock (gate)
            {
                if (FirstByteMs < 0) FirstByteMs = clock.Elapsed.TotalMilliseconds;
                BytesRead += n;
                raw.Append(chars, 0, c);
            }
        }
    }

    void WriteLoop()
    {
        WriterThreadId = Environment.CurrentManagedThreadId;
        foreach (var text in input.GetConsumingEnumerable())
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (!Native.WriteFile(InWrite, bytes, bytes.Length, out _, IntPtr.Zero))
            {
                WriterNote = $"WriteFile failed, GetLastError={Marshal.GetLastWin32Error()}";
                return;
            }
        }
        WriterNote = "input queue completed";
    }

    public void Send(string line) => input.Add(line + "\r");

    public string Raw() { lock (gate) return raw.ToString(); }
    public string Plain() => Ansi.Replace(Raw(), "");

    public Match WaitFor(string pattern, int timeoutMs)
    {
        var re = new Regex(pattern);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var m = re.Match(Plain());
            if (m.Success) return m;
            Thread.Sleep(5);
        }
        return Match.Empty;
    }

    public void CloseProcessHandles()
    {
        if (Pi.hThread != IntPtr.Zero) { Native.CloseHandle(Pi.hThread); Pi.hThread = IntPtr.Zero; }
        if (Pi.hProcess != IntPtr.Zero) { Native.CloseHandle(Pi.hProcess); Pi.hProcess = IntPtr.Zero; }
    }

    // Close order: stop the input thread, close the pseudoconsole while the output thread is still
    // draining, then wait for the output thread to see the broken pipe.
    public string Shutdown()
    {
        input.CompleteAdding();
        bool writerJoined = writer!.Join(3000);
        Native.CloseHandle(InWrite);
        var sw = Stopwatch.StartNew();
        Native.ClosePseudoConsole(Hpc);
        double closeMs = sw.Elapsed.TotalMilliseconds;
        bool readerJoined = reader!.Join(5000);
        if (readerJoined) Native.CloseHandle(OutRead);
        CloseProcessHandles();
        return $"writer joined={writerJoined} ({WriterNote}); ClosePseudoConsole took {closeMs:F1} ms; reader joined={readerJoined} ({ReaderEnd})";
    }
}

public static class Program
{
    const uint JobExitCode = 0x7E57;
    static readonly StringBuilder Log = new();

    static void Say(string s)
    {
        Console.WriteLine(s);
        Log.AppendLine(s);
    }

    static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static IntPtr NewJob()
    {
        IntPtr job = Native.CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job == IntPtr.Zero) throw new InvalidOperationException($"CreateJobObjectW failed {Marshal.GetLastWin32Error()}");
        // Safety net only: if this probe crashes, closing the job handle kills anything in it.
        // Every measurement below is taken while the job handle is still open, so this does not
        // contribute to any kill that is measured.
        const int size = 144; // JOBOBJECT_EXTENDED_LIMIT_INFORMATION on x64
        IntPtr info = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(info, i, 0);
            Marshal.WriteInt32(info, 16, Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE); // BasicLimitInformation.LimitFlags
            if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, info, size))
                throw new InvalidOperationException($"SetInformationJobObject failed {Marshal.GetLastWin32Error()}");
        }
        finally { Marshal.FreeHGlobal(info); }
        return job;
    }

    static (int total, int active) Accounting(IntPtr job)
    {
        const int size = 48; // JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
        IntPtr info = Marshal.AllocHGlobal(size);
        try
        {
            if (!Native.QueryInformationJobObject(job, Native.JobObjectBasicAccountingInformation, info, size, IntPtr.Zero))
                return (-1, -1);
            return (Marshal.ReadInt32(info, 36), Marshal.ReadInt32(info, 40)); // TotalProcesses, ActiveProcesses
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    static string Alive(IntPtr h) => Native.WaitForSingleObject(h, 0) == Native.WAIT_TIMEOUT ? "alive" : "exited";

    static string ExitCode(IntPtr h) => Native.GetExitCodeProcess(h, out uint c) ? $"0x{c:X}" : $"GetExitCodeProcess failed {Marshal.GetLastWin32Error()}";

    // After every handle to the process is closed: is the PID still present in the system?
    static string PidStatus(int pid)
    {
        string api;
        try
        {
            using var p = Process.GetProcessById(pid);
            api = $"PRESENT as '{p.ProcessName}'";
        }
        catch (ArgumentException) { api = "not present (Process.GetProcessById threw ArgumentException)"; }
        var psi = new ProcessStartInfo("tasklist.exe", $"/FI \"PID eq {pid}\" /NH /FO CSV") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var t = Process.Start(psi)!;
        string outp = t.StandardOutput.ReadToEnd().Trim();
        t.WaitForExit();
        return $"{api}; tasklist: {outp}";
    }

    static string Escaped(string s, int max)
    {
        if (s.Length > max) s = s[..max] + "\n...[truncated]";
        return s.Replace("\x1B", "\\e").Replace("\r", "\\r");
    }

    static int Int(Match m, int group = 1) => m.Success ? int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) : -1;

    public static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: p5-probe <fixture.exe> <evidence-dir>"); return 2; }
        string fixture = Path.GetFullPath(args[0]);
        string evidence = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(evidence);

        Say($"P5 probe start {Now()}");
        Say($"OS: {Environment.OSVersion.VersionString}; 64-bit process: {Environment.Is64BitProcess}; runtime: {RuntimeInformation.FrameworkDescription}");
        Say($"fixture: {fixture}");
        Say($"MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        Native.IsProcessInJob(Native.GetCurrentProcess(), IntPtr.Zero, out bool selfInJob);
        Say($"this probe process is itself inside some job: {selfInJob} (nested jobs are supported on Windows 8+)");
        int siSize = Marshal.SizeOf<Native.STARTUPINFOEXW>();
        bool blittable;
        try { GCHandle.Alloc(new Native.STARTUPINFOEXW(), GCHandleType.Pinned).Free(); blittable = true; }
        catch (ArgumentException) { blittable = false; }
        Say($"STARTUPINFOEXW: Marshal.SizeOf={siSize} (x64 native is 112); pinnable (blittable)={blittable}");

        // ---------------- CASE A: capability under test ----------------
        Say("");
        Say($"=== CASE A: attribute list count 2 (PSEUDOCONSOLE + JOB_LIST) {Now()} ===");
        IntPtr jobA = NewJob();
        var a = PtySession.Start(fixture, "child", jobA, jobA, 120, 30);
        Say($"CreateProcessW ok: child pid={a.Pi.dwProcessId}, creation flags=0x{a.CreationFlags:X8}, attribute count={a.AttributeCount}");
        Say($"A3 IsProcessInJob(child, jobA) immediately after CreateProcessW returned: {a.InJobAtCreate}");

        var ready = a.WaitFor(@"FIXTURE-READY pid=(\d+) outRedirected=(\w+) inRedirected=(\w+) END", 10000);
        Say($"first output byte at {a.FirstByteMs:F1} ms after the pty threads started; READY seen={ready.Success}: '{ready.Value}'");
        bool pidMatch = Int(ready) == a.Pi.dwProcessId;
        bool notRedirected = ready.Success && ready.Groups[2].Value == "False" && ready.Groups[3].Value == "False";
        var outMark = a.WaitFor(@"OUT-CHANNEL-MARK via-stdout", 5000);
        var errMark = a.WaitFor(@"ERR-CHANNEL-MARK via-stderr", 5000);
        Say($"A1 stdout marker arrived on pty output pipe: {outMark.Success}; stderr marker arrived on pty output pipe: {errMark.Success}");
        Say($"A2 child reports pid={Int(ready)} (matches PROCESS_INFORMATION: {pidMatch}); stdout/stdin redirected: {(ready.Success ? ready.Groups[2].Value + "/" + ready.Groups[3].Value : "n/a")}");

        const string colsRe = @"R{0} COLS=(\d+) BUF=(\d+) ROWS=(\d+) END";
        var sw = Stopwatch.StartNew();
        a.Send("cols 1");
        var r1 = a.WaitFor(string.Format(CultureInfo.InvariantCulture, colsRe, 1), 5000);
        double r1ms = sw.Elapsed.TotalMilliseconds;
        a.Send("cols 2");
        var r2 = a.WaitFor(string.Format(CultureInfo.InvariantCulture, colsRe, 2), 5000);
        Say($"A1/A4 input pipe -> child -> output pipe round trip: R1 '{r1.Value}' in {r1ms:F1} ms; R2 (no resize between) '{r2.Value}'");

        int hr1 = Native.ResizePseudoConsole(a.Hpc, new Native.COORD { X = 73, Y = 25 });
        sw.Restart();
        a.Send("cols 3");
        var r3 = a.WaitFor(string.Format(CultureInfo.InvariantCulture, colsRe, 3), 5000);
        Say($"A4 ResizePseudoConsole(73x25) HRESULT=0x{hr1:X8}; R3 '{r3.Value}' in {sw.Elapsed.TotalMilliseconds:F1} ms");
        int hr2 = Native.ResizePseudoConsole(a.Hpc, new Native.COORD { X = 150, Y = 40 });
        a.Send("cols 4");
        var r4 = a.WaitFor(string.Format(CultureInfo.InvariantCulture, colsRe, 4), 5000);
        Say($"A4 ResizePseudoConsole(150x40) HRESULT=0x{hr2:X8}; R4 '{r4.Value}'");

        a.Send("spawn");
        var gA = a.WaitFor(@"GRANDCHILD pid=(\d+) END", 5000);
        int gpidA = Int(gA);
        Say($"grandchild spawned by the child: '{gA.Value}'");
        IntPtr ghA = gpidA > 0 ? Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_TERMINATE, false, gpidA) : IntPtr.Zero;
        bool gInJobA = false;
        if (ghA != IntPtr.Zero) Native.IsProcessInJob(ghA, jobA, out gInJobA);
        var accBefore = Accounting(jobA);
        Say($"grandchild handle opened={ghA != IntPtr.Zero}; IsProcessInJob(grandchild, jobA)={gInJobA}; job accounting before kill: total={accBefore.total} active={accBefore.active}");
        Say($"before kill: child {Alive(a.Pi.hProcess)}, grandchild {(ghA != IntPtr.Zero ? Alive(ghA) : "n/a")}");

        sw.Restart();
        bool tjA = Native.TerminateJobObject(jobA, JobExitCode);
        uint wcA = Native.WaitForSingleObject(a.Pi.hProcess, 5000);
        double childDeadMs = sw.Elapsed.TotalMilliseconds;
        uint wgA = ghA != IntPtr.Zero ? Native.WaitForSingleObject(ghA, 5000) : 0xFFFFFFFF;
        double grandDeadMs = sw.Elapsed.TotalMilliseconds;
        string ecChildA = ExitCode(a.Pi.hProcess);
        string ecGrandA = ghA != IntPtr.Zero ? ExitCode(ghA) : "n/a";
        var accAfter = Accounting(jobA);
        Say($"A5 TerminateJobObject(jobA, 0x{JobExitCode:X}) returned {tjA}");
        Say($"A5 child wait={(wcA == Native.WAIT_OBJECT_0 ? "signaled" : $"0x{wcA:X}")} at {childDeadMs:F2} ms, exit code {ecChildA}");
        Say($"A5 grandchild wait={(wgA == Native.WAIT_OBJECT_0 ? "signaled" : $"0x{wgA:X}")} at {grandDeadMs:F2} ms, exit code {ecGrandA}");
        Say($"A5 job accounting after kill: total={accAfter.total} active={accAfter.active}");
        if (ghA != IntPtr.Zero) Native.CloseHandle(ghA);
        int childPidA = a.Pi.dwProcessId;
        a.CloseProcessHandles();
        string pidChildA = PidStatus(childPidA);
        string pidGrandA = gpidA > 0 ? PidStatus(gpidA) : "n/a";
        Say($"A5 after all handles closed: child pid {childPidA}: {pidChildA}");
        Say($"A5 after all handles closed: grandchild pid {gpidA}: {pidGrandA}");
        Say($"threads: output reader managed id {a.ReaderThreadId}, input writer managed id {a.WriterThreadId}, main {Environment.CurrentManagedThreadId}");
        Say($"shutdown A: {a.Shutdown()}; bytes read on output pipe={a.BytesRead}");
        File.WriteAllText(Path.Combine(evidence, "pty_transcript_caseA.txt"), Escaped(a.Raw(), 12000));
        Native.CloseHandle(jobA);

        // ---------------- CASE B: control, JOB_LIST absent ----------------
        Say("");
        Say($"=== CASE B (control): attribute list count 1 (PSEUDOCONSOLE only), same job created {Now()} ===");
        IntPtr jobB = NewJob();
        var b = PtySession.Start(fixture, "child", IntPtr.Zero, jobB, 120, 30);
        Say($"CreateProcessW ok: child pid={b.Pi.dwProcessId}, creation flags=0x{b.CreationFlags:X8}, attribute count={b.AttributeCount}");
        Say($"B1 IsProcessInJob(child, jobB) immediately after CreateProcessW returned: {b.InJobAtCreate}");
        var readyB = b.WaitFor(@"FIXTURE-READY pid=(\d+) outRedirected=(\w+) inRedirected=(\w+) END", 10000);
        Say($"B READY seen={readyB.Success}: '{readyB.Value}'");
        b.Send("spawn");
        var gB = b.WaitFor(@"GRANDCHILD pid=(\d+) END", 5000);
        int gpidB = Int(gB);
        IntPtr ghB = gpidB > 0 ? Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_TERMINATE, false, gpidB) : IntPtr.Zero;
        bool gInJobB = true;
        if (ghB != IntPtr.Zero) Native.IsProcessInJob(ghB, jobB, out gInJobB);
        var accB = Accounting(jobB);
        Say($"B grandchild '{gB.Value}'; IsProcessInJob(grandchild, jobB)={gInJobB}; job accounting: total={accB.total} active={accB.active}");

        bool tjB = Native.TerminateJobObject(jobB, JobExitCode);
        uint wcB = Native.WaitForSingleObject(b.Pi.hProcess, 1500);
        uint wgB = ghB != IntPtr.Zero ? Native.WaitForSingleObject(ghB, 1500) : 0xFFFFFFFF;
        bool b2ChildSurvived = wcB == Native.WAIT_TIMEOUT;
        bool b2GrandSurvived = wgB == Native.WAIT_TIMEOUT;
        Say($"B2 TerminateJobObject(jobB) returned {tjB}; after 1500 ms each: child {(b2ChildSurvived ? "ALIVE" : "exited")}, grandchild {(b2GrandSurvived ? "ALIVE" : "exited")}");

        bool tpB = Native.TerminateProcess(b.Pi.hProcess, 0x7E58);
        uint wcB2 = Native.WaitForSingleObject(b.Pi.hProcess, 5000);
        uint wgB2 = ghB != IntPtr.Zero ? Native.WaitForSingleObject(ghB, 2000) : 0xFFFFFFFF;
        bool b3GrandSurvived = wgB2 == Native.WAIT_TIMEOUT;
        Say($"B3 TerminateProcess(child) returned {tpB}; child wait={(wcB2 == Native.WAIT_OBJECT_0 ? "signaled" : $"0x{wcB2:X}")} exit {ExitCode(b.Pi.hProcess)}; grandchild 2000 ms later: {(b3GrandSurvived ? "ALIVE (orphaned)" : "exited")}");

        // Cleanup of the control's orphan.
        if (ghB != IntPtr.Zero)
        {
            Native.TerminateProcess(ghB, 0x7E59);
            uint wgB3 = Native.WaitForSingleObject(ghB, 5000);
            Say($"B cleanup: TerminateProcess(grandchild) -> wait {(wgB3 == Native.WAIT_OBJECT_0 ? "signaled" : $"0x{wgB3:X}")}, exit {ExitCode(ghB)}");
            Native.CloseHandle(ghB);
        }
        int childPidB = b.Pi.dwProcessId;
        b.CloseProcessHandles();
        Say($"B after cleanup: child pid {childPidB}: {PidStatus(childPidB)}");
        Say($"B after cleanup: grandchild pid {gpidB}: {(gpidB > 0 ? PidStatus(gpidB) : "n/a")}");
        Say($"shutdown B: {b.Shutdown()}");
        File.WriteAllText(Path.Combine(evidence, "pty_transcript_caseB.txt"), Escaped(b.Raw(), 6000));
        Native.CloseHandle(jobB);

        // ---------------- VERDICT ----------------
        bool A1 = outMark.Success && errMark.Success && r1.Success && r2.Success;
        bool A2 = pidMatch && notRedirected;
        bool A3 = a.InJobAtCreate && a.CreationFlags == Native.EXTENDED_STARTUPINFO_PRESENT && a.AttributeCount == 2;
        int c1 = Int(r1), c2 = Int(r2), c3 = Int(r3), c4 = Int(r4);
        bool A4 = hr1 == 0 && hr2 == 0 && c1 == 120 && c2 == 120 && c3 == 73 && c4 == 150;
        bool A5 = tjA && wcA == Native.WAIT_OBJECT_0 && wgA == Native.WAIT_OBJECT_0
                  && ecChildA == $"0x{JobExitCode:X}" && ecGrandA == $"0x{JobExitCode:X}"
                  && gInJobA && accBefore.active == 2 && accAfter.active == 0
                  && pidChildA.StartsWith("not present", StringComparison.Ordinal) && pidGrandA.StartsWith("not present", StringComparison.Ordinal);
        bool controlBehaved = !b.InJobAtCreate && !gInJobB && accB.active == 0 && b2ChildSurvived && b2GrandSurvived && b3GrandSurvived && gpidB > 0;

        Say("");
        Say($"columns reported by child: R1={c1} R2={c2} (no resize) R3={c3} (after 73) R4={c4} (after 150)");
        Say($"A1 pty output on both channels ........ {A1}");
        Say($"A2 child is on the pty ................ {A2}");
        Say($"A3 in job at creation, flags 0x80000 .. {A3}");
        Say($"A4 resize changes child's columns ..... {A4}");
        Say($"A5 job kill takes child + grandchild .. {A5}");
        Say($"CONTROL (no JOB_LIST) failed to kill .. {controlBehaved}");
        bool pass = A1 && A2 && A3 && A4 && A5 && controlBehaved;
        Say(pass ? "P5: PASS" : "P5: FAIL");
        Say($"P5 probe end {Now()}");
        File.WriteAllText(Path.Combine(evidence, "run.txt"), Log.ToString());
        return pass ? 0 : 1;
    }
}
