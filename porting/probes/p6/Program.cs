// P6, graceful-then-forceful termination with the gap, per transport (task packet T-M0-005).
// Throwaway probe code, never production code.
//
// SOURCE FACTS reproduced (pinned dd2b86ef):
//   Shell.swift:110-118        guard isRunning; process.terminate() (SIGTERM);
//                              asyncAfter(.now() + 1) { guard isRunning; kill(group-or-pid, SIGKILL) }
//   StdioProcess.swift:114-123 close stdin (on the write queue); guard isRunning; process.terminate();
//                              asyncAfter(.now() + 3) { if isRunning { kill(pid, SIGKILL) } }
//                              NOTE: 3 s at this line, not 1 s. See RESULT.md, deviations.
//
// Windows shape under test (ENGINEERING DECISION from the probe text in stack_decision.json):
//   pty transport : graceful = byte 0x03 written into the ConPTY input pipe.
//   pipe transport: graceful = close stdin, then CTRL_BREAK_EVENT through a helper process that runs
//                   FreeConsole / AttachConsole(child) / GenerateConsoleCtrlEvent / FreeConsole, for a
//                   child created with CREATE_NEW_PROCESS_GROUP.
//   forceful      : TerminateJobObject at t0 + gap, only if the child is still running.
//   Both transports place the child in its Job Object through PROC_THREAD_ATTRIBUTE_JOB_LIST in the
//   same attribute list as PSEUDOCONSOLE or HANDLE_LIST (hand-rolled CreateProcessW, ADR-0002).
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace P6;

enum Transport { Pty, Pipe }
enum Graceful { None, CtrlC, StdinCloseOnly, StdinCloseHelper, StdinCloseNoAttach }
enum Expect { GracefulExit, ForcedAtDeadline, AliveAtDeadline, AliveWithoutForce }

sealed record CaseSpec(string Id, string Role, Transport Transport, string Fixture, Graceful Graceful,
    int GapMs, bool Force, Expect Expect, uint GracefulCode, int Trials, string Note);

sealed class TrialResult
{
    public string Case = "", Role = "", Error = "";
    public int Trial, Pid, GrandchildPid;
    public bool InJob, AliveAtDeadline, KillIssued, AliveAtObserve, IgnoredSeen, GrandchildDead, Exited;
    public double SignalSentMs = double.NaN, DeadlineFiredMs = double.NaN, KillIssuedMs = double.NaN,
                  ExitMs = double.NaN, GrandchildDeadMs = double.NaN, HelperDoneMs = double.NaN, StdinClosedMs = double.NaN;
    public uint ExitCode;
    public long HelperExit = -1;
    public bool Pass;
}

sealed class Child : IDisposable
{
    public IntPtr Process, Job, PseudoConsole, OutRead;
    public int Pid;
    public FileStream? Input;
    readonly StringBuilder _out = new();
    readonly object _gate = new();
    Thread? _reader;
    public long ExitTs;
    public uint ExitCode;
    public readonly ManualResetEventSlim Exited = new(false);

    public string Output { get { lock (_gate) return _out.ToString(); } }
    public bool Alive => Native.WaitForSingleObject(Process, 0) == Native.WAIT_TIMEOUT;

    public void StartIo()
    {
        var outRead = OutRead;
        OutRead = IntPtr.Zero; // ownership moves to the reader's FileStream
        _reader = new Thread(() =>
        {
            using var fs = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read, 1, false);
            var buf = new byte[4096];
            try
            {
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    lock (_gate) _out.Append(Encoding.UTF8.GetString(buf, 0, n));
            }
            catch (IOException) { }
        }) { IsBackground = true };
        _reader.Start();

        var watcher = new Thread(() =>
        {
            Native.WaitForSingleObject(Process, Native.INFINITE);
            ExitTs = Stopwatch.GetTimestamp();
            Native.GetExitCodeProcess(Process, out var code);
            ExitCode = code;
            Exited.Set();
        }) { IsBackground = true, Priority = ThreadPriority.Highest };
        watcher.Start();
    }

    public void Dispose()
    {
        if (Alive) Native.TerminateJobObject(Job, 0xDEAD);
        Exited.Wait(3000);
        try { Input?.Dispose(); } catch (IOException) { }
        if (PseudoConsole != IntPtr.Zero) { Native.ClosePseudoConsole(PseudoConsole); PseudoConsole = IntPtr.Zero; }
        _reader?.Join(3000);
        if (Process != IntPtr.Zero) Native.CloseHandle(Process);
        if (Job != IntPtr.Zero) Native.CloseHandle(Job); // KILL_ON_JOB_CLOSE: anything left dies here
        Process = Job = IntPtr.Zero;
    }
}

static unsafe class Program
{
    static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "fixture") return Fixtures.Run(args[1]);
        if (args.Length >= 2 && args[0] == "ctrlbreak") return Fixtures.CtrlBreak(int.Parse(args[1], CultureInfo.InvariantCulture), true);
        if (args.Length >= 2 && args[0] == "ctrlbreak-noattach") return Fixtures.CtrlBreak(int.Parse(args[1], CultureInfo.InvariantCulture), false);
        if (args.Length >= 1 && args[0] == "run") return Runner.Run(args);
        Console.Error.WriteLine("usage: p6 run [--out DIR] [--trials N]");
        return 2;
    }
}

static unsafe class Runner
{
    static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
    static double Ms(long from, long to) => (to - from) * TickMs;
    static readonly string Self = Environment.ProcessPath!;
    static string NodePath = "";
    static readonly List<string> Log = new();

    static void Say(string s) { Console.WriteLine(s); Log.Add(s); }
    static string Q(string s) => "\"" + s + "\"";
    static string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.0", CultureInfo.InvariantCulture);

    static string Command(string fixture) => fixture switch
    {
        "coop" or "stubborn" => Q(Self) + " fixture " + fixture,
        "node-coop-pty" => Q(NodePath) + " -e " + Q("process.on('SIGINT',()=>process.exit(130));console.log('READY');setInterval(()=>{},1e6)"),
        "node-coop-pipe" => Q(NodePath) + " -e " + Q("process.on('SIGBREAK',()=>process.exit(131));console.log('READY');setInterval(()=>{},1e6)"),
        "node-stubborn" => Q(NodePath) + " -e " + Q("process.on('SIGINT',()=>console.log('IGNORED 0'));process.on('SIGBREAK',()=>console.log('IGNORED 1'));console.log('READY');setInterval(()=>{},1e6)"),
        _ => throw new ArgumentException(fixture),
    };

    // ---------------------------------------------------------------- process creation
    static void Check(int ok, string what) { if (ok == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), what); }

    static IntPtr NewJob()
    {
        var job = Native.CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObjectW");
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        Check(Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, &info, sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)), "SetInformationJobObject");
        return job;
    }

    static IntPtr NewAttrList(int count)
    {
        IntPtr size = 0;
        Native.InitializeProcThreadAttributeList(IntPtr.Zero, count, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        Check(Native.InitializeProcThreadAttributeList(list, count, 0, ref size), "InitializeProcThreadAttributeList");
        return list;
    }

    static PROCESS_INFORMATION Create(string cmd, bool inherit, uint flags, STARTUPINFOEXW* si)
    {
        var buf = (cmd + "\0").ToCharArray();
        PROCESS_INFORMATION pi;
        fixed (char* p = buf)
            Check(Native.CreateProcessW(null, p, IntPtr.Zero, IntPtr.Zero, inherit ? 1 : 0, flags, IntPtr.Zero, null, si, &pi), "CreateProcessW");
        return pi;
    }

    static Child SpawnPipe(string cmd)
    {
        var job = NewJob();
        var sa = new SECURITY_ATTRIBUTES { nLength = sizeof(SECURITY_ATTRIBUTES), bInheritHandle = 1 };
        Check(Native.CreatePipe(out var inR, out var inW, &sa, 0), "CreatePipe in");
        Check(Native.SetHandleInformation(inW, Native.HANDLE_FLAG_INHERIT, 0), "SetHandleInformation");
        Check(Native.CreatePipe(out var outR, out var outW, &sa, 0), "CreatePipe out");
        Check(Native.SetHandleInformation(outR, Native.HANDLE_FLAG_INHERIT, 0), "SetHandleInformation");

        var handles = (IntPtr*)Marshal.AllocHGlobal(2 * IntPtr.Size);
        handles[0] = inR; handles[1] = outW;
        var jobs = (IntPtr*)Marshal.AllocHGlobal(IntPtr.Size);
        jobs[0] = job;
        var list = NewAttrList(2);
        Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, (IntPtr)handles, 2 * IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "HANDLE_LIST");
        Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_JOB_LIST, (IntPtr)jobs, IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "JOB_LIST");

        var si = new STARTUPINFOEXW();
        si.StartupInfo.cb = sizeof(STARTUPINFOEXW);
        si.StartupInfo.dwFlags = Native.STARTF_USESTDHANDLES;
        si.StartupInfo.hStdInput = inR;
        si.StartupInfo.hStdOutput = outW;
        si.StartupInfo.hStdError = outW;
        si.lpAttributeList = list;
        PROCESS_INFORMATION pi;
        try
        {
            pi = Create(cmd, true, Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT
                                   | Native.CREATE_NO_WINDOW | Native.CREATE_NEW_PROCESS_GROUP, &si);
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list); Marshal.FreeHGlobal((IntPtr)handles); Marshal.FreeHGlobal((IntPtr)jobs);
            Native.CloseHandle(inR); Native.CloseHandle(outW);
        }
        Native.CloseHandle(pi.hThread);
        return new Child
        {
            Process = pi.hProcess, Pid = pi.dwProcessId, Job = job, OutRead = outR,
            Input = new FileStream(new SafeFileHandle(inW, true), FileAccess.Write, 1, false),
        };
    }

    static Child SpawnPty(string cmd)
    {
        var job = NewJob();
        Check(Native.CreatePipe(out var inR, out var inW, null, 0), "CreatePipe in");
        Check(Native.CreatePipe(out var outR, out var outW, null, 0), "CreatePipe out");
        int hr = Native.CreatePseudoConsole(new COORD { X = 120, Y = 30 }, inR, outW, 0, out var hPC);
        if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole");

        var jobs = (IntPtr*)Marshal.AllocHGlobal(IntPtr.Size);
        jobs[0] = job;
        var list = NewAttrList(2);
        Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hPC, IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "PSEUDOCONSOLE");
        Check(Native.UpdateProcThreadAttribute(list, 0, Native.PROC_THREAD_ATTRIBUTE_JOB_LIST, (IntPtr)jobs, IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "JOB_LIST");

        var si = new STARTUPINFOEXW();
        si.StartupInfo.cb = sizeof(STARTUPINFOEXW);
        // Null std handles with STARTF_USESTDHANDLES: without this, a parent whose own std handles are
        // redirected passes them to the child and the child bypasses the pseudoconsole.
        si.StartupInfo.dwFlags = Native.STARTF_USESTDHANDLES;
        si.lpAttributeList = list;
        PROCESS_INFORMATION pi;
        try
        {
            pi = Create(cmd, false, Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT, &si);
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list); Marshal.FreeHGlobal((IntPtr)jobs);
            Native.CloseHandle(inR); Native.CloseHandle(outW);
        }
        Native.CloseHandle(pi.hThread);
        return new Child
        {
            Process = pi.hProcess, Pid = pi.dwProcessId, Job = job, OutRead = outR, PseudoConsole = hPC,
            Input = new FileStream(new SafeFileHandle(inW, true), FileAccess.Write, 1, false),
        };
    }

    /// Runs the helper (or the no-attach control) as its own DETACHED / NO_WINDOW process in its own
    /// process group, without blocking the caller. Returns the wait task.
    static Task<(long exit, long doneTs)> StartHelper(string mode, int pid)
    {
        var si = new STARTUPINFOEXW();
        si.StartupInfo.cb = sizeof(STARTUPINFOW);
        uint consoleFlag = mode == "ctrlbreak" ? Native.DETACHED_PROCESS : Native.CREATE_NO_WINDOW;
        var pi = Create(Q(Self) + " " + mode + " " + pid.ToString(CultureInfo.InvariantCulture), false,
                        consoleFlag | Native.CREATE_NEW_PROCESS_GROUP | Native.CREATE_UNICODE_ENVIRONMENT, &si);
        Native.CloseHandle(pi.hThread);
        var h = pi.hProcess;
        return Task.Run(() =>
        {
            Native.WaitForSingleObject(h, 10000);
            long ts = Stopwatch.GetTimestamp();
            Native.GetExitCodeProcess(h, out var code);
            Native.CloseHandle(h);
            return ((long)code, ts);
        });
    }

    static bool WaitUntil(Func<bool> cond, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(5); }
        return cond();
    }

    static int CountIgnored(string s) => Regex.Matches(s, "IGNORED").Count;

    // ---------------------------------------------------------------- one trial
    static TrialResult RunTrial(CaseSpec c, int trial)
    {
        var r = new TrialResult { Case = c.Id, Role = c.Role, Trial = trial };
        var child = c.Transport == Transport.Pty ? SpawnPty(Command(c.Fixture)) : SpawnPipe(Command(c.Fixture));
        IntPtr gc = IntPtr.Zero;
        Task<(long exit, long doneTs)>? helper = null;
        try
        {
            child.StartIo();
            r.Pid = child.Pid;
            Native.IsProcessInJob(child.Process, child.Job, out var inJob);
            r.InJob = inJob != 0;
            if (!WaitUntil(() => child.Output.Contains("READY"), 15000)) { r.Error = "no READY; output=" + child.Output.Replace("\r", " ").Replace("\n", " "); return r; }
            var m = Regex.Match(child.Output, @"GRANDCHILD (\d+) ok");
            if (m.Success)
            {
                r.GrandchildPid = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                gc = Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION, 0, r.GrandchildPid);
            }
            else if (c.Fixture == "stubborn") { r.Error = "grandchild not reported"; return r; }

            // ================= terminate(), in the source's order =================
            long t0 = Stopwatch.GetTimestamp();
            if (c.Transport == Transport.Pipe && c.Graceful != Graceful.None)
            {
                child.Input!.Dispose(); // StdioProcess.swift:115-116, stdin close first
                r.StdinClosedMs = Ms(t0, Stopwatch.GetTimestamp());
            }
            if (!child.Alive) { r.Error = "not running at terminate()"; return r; } // the guard
            // DispatchQueue.asyncAfter(deadline: .now() + gap) -> a timer continuation, not a blocked thread.
            var deadline = Task.Delay(c.GapMs).ContinueWith(_ =>
            {
                r.DeadlineFiredMs = Ms(t0, Stopwatch.GetTimestamp());
                r.AliveAtDeadline = child.Alive;          // "guard process.isRunning"
                if (c.Force && r.AliveAtDeadline)
                {
                    Native.TerminateJobObject(child.Job, Fixtures.ExitByJob);
                    r.KillIssued = true;
                    r.KillIssuedMs = Ms(t0, Stopwatch.GetTimestamp());
                }
            });
            switch (c.Graceful)
            {
                case Graceful.CtrlC:
                    child.Input!.Write(new byte[] { 0x03 });
                    child.Input.Flush();
                    r.SignalSentMs = Ms(t0, Stopwatch.GetTimestamp());
                    break;
                case Graceful.StdinCloseHelper:
                    helper = StartHelper("ctrlbreak", child.Pid);
                    r.SignalSentMs = Ms(t0, Stopwatch.GetTimestamp()); // helper launched, not yet done
                    break;
                case Graceful.StdinCloseNoAttach:
                    helper = StartHelper("ctrlbreak-noattach", child.Pid);
                    r.SignalSentMs = Ms(t0, Stopwatch.GetTimestamp());
                    break;
            }

            if (c.Expect == Expect.AliveWithoutForce)
            {
                child.Exited.Wait(2 * c.GapMs);
                r.AliveAtObserve = child.Alive; // observed at t0 + 2 * gap with the forceful step disabled
            }
            else child.Exited.Wait(c.GapMs + 3000);
            deadline.Wait();
            if (helper is not null)
            {
                var (exit, doneTs) = helper.Result;
                r.HelperExit = exit;
                r.HelperDoneMs = Ms(t0, doneTs);
            }
            r.Exited = child.Exited.IsSet;
            if (r.Exited) { r.ExitMs = Ms(t0, child.ExitTs); r.ExitCode = child.ExitCode; }
            if (gc != IntPtr.Zero && r.Exited)
            {
                r.GrandchildDead = Native.WaitForSingleObject(gc, 2000) == Native.WAIT_OBJECT_0;
                if (r.GrandchildDead) r.GrandchildDeadMs = Ms(t0, Stopwatch.GetTimestamp());
            }
            Thread.Sleep(150); // let the pty render / pipe deliver the fixture's last lines
            r.IgnoredSeen = CountIgnored(child.Output) > 0;

            r.Pass = c.Expect switch
            {
                Expect.GracefulExit => r.Exited && r.ExitMs < c.GapMs && r.ExitCode == c.GracefulCode
                                       && !r.KillIssued && !r.AliveAtDeadline,
                Expect.ForcedAtDeadline => r.Exited && r.KillIssued && r.ExitCode == Fixtures.ExitByJob
                                           && Math.Abs(r.ExitMs - c.GapMs) <= 50 && r.IgnoredSeen
                                           && (r.GrandchildPid == 0 || r.GrandchildDead),
                Expect.AliveAtDeadline => r.AliveAtDeadline && r.KillIssued && r.ExitCode == Fixtures.ExitByJob,
                Expect.AliveWithoutForce => r.AliveAtObserve && r.IgnoredSeen && !r.KillIssued,
                _ => false,
            };
            return r;
        }
        catch (Exception ex) { r.Error = ex.GetType().Name + ": " + ex.Message; return r; }
        finally
        {
            long cleanup = Stopwatch.GetTimestamp();
            child.Dispose();
            if (gc != IntPtr.Zero)
            {
                // Termination of every process in the job is asynchronous: wait (bounded) and record.
                if (Native.WaitForSingleObject(gc, 2000) == Native.WAIT_TIMEOUT) r.Error += " [grandchild survived cleanup by 2000 ms]";
                else if (!r.GrandchildDead) r.Error += $" [grandchild gone {F(Ms(cleanup, Stopwatch.GetTimestamp()))} ms after cleanup began]";
                Native.CloseHandle(gc);
            }
        }
    }

    // ---------------------------------------------------------------- write to a closed pipe
    sealed record WriteResult(bool Ok, string Type, int HResult, int Win32, string Message);

    static WriteResult TryWrite(Stream s, byte[] data, bool viaWriter)
    {
        try
        {
            if (viaWriter)
            {
                using var w = new StreamWriter(s, new UTF8Encoding(false), 16, leaveOpen: true) { AutoFlush = true };
                w.Write(Encoding.ASCII.GetString(data));
            }
            else { s.Write(data, 0, data.Length); s.Flush(); }
            return new WriteResult(true, "", 0, 0, "");
        }
        catch (IOException ex) // the recoverable path: record and carry on, like `try? handle.write`
        {
            return new WriteResult(false, ex.GetType().FullName!, ex.HResult, ex.HResult & 0xFFFF, ex.Message);
        }
    }

    static string Show(WriteResult w) => w.Ok ? "write OK (no exception)"
        : $"{w.Type} HResult=0x{w.HResult:X8} win32={w.Win32} ({(w.Win32 == 109 ? "ERROR_BROKEN_PIPE" : w.Win32 == 232 ? "ERROR_NO_DATA" : "OTHER")}) \"{w.Message}\"";

    static bool BrokenPipeTests()
    {
        Say("");
        Say("=== write to a closed pipe ===");
        bool ok = true;

        // Pipe transport: the child's stdin.
        var child = SpawnPipe(Command("coop"));
        try
        {
            child.StartIo();
            if (!WaitUntil(() => child.Output.Contains("READY"), 15000)) { Say("pipe: no READY"); return false; }
            var before = TryWrite(child.Input!, "hello\n"u8.ToArray(), false);
            Say($"pipe  CONTROL write while the child is alive : {Show(before)}");
            Native.TerminateJobObject(child.Job, Fixtures.ExitByJob);
            child.Exited.Wait(3000);
            Say($"pipe  child exited: {child.Exited.IsSet} code={child.ExitCode}");
            var raw = TryWrite(child.Input!, "hello\n"u8.ToArray(), false);
            Say($"pipe  write after exit, FileStream.Write         : {Show(raw)}");
            var viaWriter = TryWrite(child.Input!, "hello\n"u8.ToArray(), true);
            Say($"pipe  write after exit, StreamWriter(AutoFlush)  : {Show(viaWriter)}");
            // Swift's send()/write() are `try? handle.write(...)` on a background queue; the same shape:
            var bg = Task.Run(() => TryWrite(child.Input!, "x"u8.ToArray(), false)).Result;
            Say($"pipe  write after exit, from a background task   : {Show(bg)}");
            ok &= before.Ok;
            foreach (var w in new[] { raw, viaWriter, bg })
                ok &= !w.Ok && w.Type == "System.IO.IOException" && (w.Win32 == 109 || w.Win32 == 232);
        }
        finally { child.Dispose(); }

        // Pty transport: the ConPTY input pipe, before and after ClosePseudoConsole.
        var pty = SpawnPty(Command("coop"));
        try
        {
            pty.StartIo();
            if (!WaitUntil(() => pty.Output.Contains("READY"), 15000)) { Say("pty: no READY"); return false; }
            Native.TerminateJobObject(pty.Job, Fixtures.ExitByJob);
            pty.Exited.Wait(3000);
            var afterChild = TryWrite(pty.Input!, new byte[] { 0x03 }, false);
            Say($"pty   write after child exit, console still open : {Show(afterChild)}   (informational)");
            long t = Stopwatch.GetTimestamp();
            Native.ClosePseudoConsole(pty.PseudoConsole);
            pty.PseudoConsole = IntPtr.Zero;
            WriteResult afterClose = new(true, "", 0, 0, "");
            double failedAt = double.NaN;
            for (int i = 0; i < 60 && afterClose.Ok; i++)
            {
                afterClose = TryWrite(pty.Input!, new byte[] { 0x03 }, false);
                if (!afterClose.Ok) failedAt = Ms(t, Stopwatch.GetTimestamp()); else Thread.Sleep(50);
            }
            Say($"pty   write after ClosePseudoConsole             : {Show(afterClose)}  first failure at +{F(failedAt)} ms");
            ok &= !afterClose.Ok && afterClose.Type == "System.IO.IOException" && (afterClose.Win32 == 109 || afterClose.Win32 == 232);
        }
        finally { pty.Dispose(); }

        Say($"runner still running after the exceptions: True (pid {Environment.ProcessId}); every trial below runs after them");
        Say($"BROKEN PIPE CHECK: {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    // ---------------------------------------------------------------- main
    public static int Run(string[] args)
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "p6-out");
        int mainTrials = 10;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") outDir = args[i + 1];
            if (args[i] == "--trials") mainTrials = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }
        Directory.CreateDirectory(outDir);
        NodePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");

        Native.NtQueryTimerResolution(out var tmin, out var tmax, out var tcur);
        Say($"P6 run {DateTime.Now:O}");
        Say($"OS {Environment.OSVersion}  .NET {Environment.Version}  {RuntimeInformation.ProcessArchitecture}  CPUs {Environment.ProcessorCount}");
        Say($"Stopwatch.Frequency {Stopwatch.Frequency}  IsHighResolution {Stopwatch.IsHighResolution}");
        Say($"Timer resolution (100 ns units): coarsest {tmin}  finest {tmax}  current {tcur}  (= {tcur / 10000.0:0.000} ms)");
        Say($"MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        Say($"runner pid {Environment.ProcessId}  console window {Native.GetConsoleWindow()}  node {(File.Exists(NodePath) ? NodePath : "MISSING")}");

        bool brokenOk = BrokenPipeTests();

        var cases = new List<CaseSpec>
        {
            new("M1-pty-coop",        "main",    Transport.Pty,  "coop",     Graceful.CtrlC,            1000, true,  Expect.GracefulExit,      130, mainTrials, "0x03 into ConPTY; cooperating fixture must exit before t+1000"),
            new("M2-pty-stubborn",    "main",    Transport.Pty,  "stubborn", Graceful.CtrlC,            1000, true,  Expect.ForcedAtDeadline,  0,   mainTrials, "0x03 ignored; TerminateJobObject at t+1000 +/-50; grandchild dies"),
            new("M3-pipe-coop",       "main",    Transport.Pipe, "coop",     Graceful.StdinCloseHelper, 1000, true,  Expect.GracefulExit,      131, mainTrials, "stdin close + CTRL_BREAK helper; exit before t+1000"),
            new("M4-pipe-stubborn",   "main",    Transport.Pipe, "stubborn", Graceful.StdinCloseHelper, 1000, true,  Expect.ForcedAtDeadline,  0,   mainTrials, "CTRL_BREAK ignored; TerminateJobObject at t+1000 +/-50; grandchild dies"),
            new("C1-pty-no-0x03",     "control", Transport.Pty,  "coop",     Graceful.None,             1000, true,  Expect.AliveAtDeadline,   0,   3, "no 0x03: cooperating fixture must still be alive at t+1000"),
            new("C2-pipe-stdin-only", "control", Transport.Pipe, "coop",     Graceful.StdinCloseOnly,   1000, true,  Expect.AliveAtDeadline,   0,   3, "stdin close without CTRL_BREAK: must still be alive at t+1000"),
            new("C3-pipe-no-attach",  "control", Transport.Pipe, "coop",     Graceful.StdinCloseNoAttach,1000, true, Expect.AliveAtDeadline,   0,   3, "GenerateConsoleCtrlEvent without AttachConsole (helper absent): must still be alive"),
            new("C4-pty-no-force",    "control", Transport.Pty,  "stubborn", Graceful.CtrlC,            1000, false, Expect.AliveWithoutForce, 0,   3, "forceful step disabled: non-cooperating fixture must still be alive at t+2000"),
            new("C5-pipe-no-force",   "control", Transport.Pipe, "stubborn", Graceful.StdinCloseHelper, 1000, false, Expect.AliveWithoutForce, 0,   3, "forceful step disabled: non-cooperating fixture must still be alive at t+2000"),
            new("S1-pipe-coop-3000",  "source",  Transport.Pipe, "coop",     Graceful.StdinCloseHelper, 3000, true,  Expect.GracefulExit,      131, 3, "StdioProcess.swift:120 gap is 3 s"),
            new("S2-pipe-stub-3000",  "source",  Transport.Pipe, "stubborn", Graceful.StdinCloseHelper, 3000, true,  Expect.ForcedAtDeadline,  0,   3, "StdioProcess.swift:120 gap is 3 s"),
            new("N1-pty-node-coop",   "extra",   Transport.Pty,  "node-coop-pty",  Graceful.CtrlC,            1000, true, Expect.GracefulExit,     130, 3, "node runtime, SIGINT handler"),
            new("N2-pty-node-stub",   "extra",   Transport.Pty,  "node-stubborn",  Graceful.CtrlC,            1000, true, Expect.ForcedAtDeadline, 0,   3, "node runtime, SIGINT ignored"),
            new("N3-pipe-node-coop",  "extra",   Transport.Pipe, "node-coop-pipe", Graceful.StdinCloseHelper, 1000, true, Expect.GracefulExit,     131, 3, "node runtime, SIGBREAK handler"),
            new("N4-pipe-node-stub",  "extra",   Transport.Pipe, "node-stubborn",  Graceful.StdinCloseHelper, 1000, true, Expect.ForcedAtDeadline, 0,   3, "node runtime, SIGBREAK ignored"),
        };
        if (!File.Exists(NodePath)) cases.RemoveAll(c => c.Role == "extra");

        var all = new List<TrialResult>();
        foreach (var c in cases)
        {
            Say("");
            Say($"=== {c.Id} [{c.Role}] {c.Transport} fixture={c.Fixture} graceful={c.Graceful} gap={c.GapMs} force={c.Force} expect={c.Expect}");
            Say($"    {c.Note}");
            for (int t = 1; t <= c.Trials; t++)
            {
                var r = RunTrial(c, t);
                all.Add(r);
                Say($"  #{t,2} pass={r.Pass,-5} inJob={r.InJob} signal@{F(r.SignalSentMs)} helperDone@{F(r.HelperDoneMs)} helperExit={(r.HelperExit < 0 ? "-" : "0x" + r.HelperExit.ToString("X", CultureInfo.InvariantCulture))} " +
                    $"deadline@{F(r.DeadlineFiredMs)} aliveAtDeadline={r.AliveAtDeadline} kill@{F(r.KillIssuedMs)} exit@{F(r.ExitMs)} code={r.ExitCode} " +
                    $"ignoredSeen={r.IgnoredSeen} gc={r.GrandchildPid} gcDead@{F(r.GrandchildDeadMs)} aliveAtObserve={r.AliveAtObserve} {r.Error}");
            }
        }

        // ---- summary
        Say("");
        Say("=== summary (ms from t0 = start of terminate()) ===");
        foreach (var c in cases)
        {
            var rs = all.Where(r => r.Case == c.Id).ToList();
            var ex = rs.Where(r => r.Exited).Select(r => r.ExitMs).OrderBy(x => x).ToList();
            var dl = rs.Select(r => r.DeadlineFiredMs).Where(x => !double.IsNaN(x)).ToList();
            var k2e = rs.Where(r => r.KillIssued && r.Exited).Select(r => r.ExitMs - r.KillIssuedMs).ToList();
            string exs = ex.Count == 0 ? "n/a" : $"min {F(ex.First())} median {F(ex[ex.Count / 2])} max {F(ex.Last())}";
            string dls = dl.Count == 0 ? "n/a" : $"{F(dl.Min())}..{F(dl.Max())}";
            string k2es = k2e.Count == 0 ? "n/a" : $"max {F(k2e.Max())}";
            Say($"{c.Id,-20} {c.Role,-8} {rs.Count(r => r.Pass)}/{rs.Count} as expected | exit {exs} | deadline fired {dls} | kill->exit {k2es}");
        }

        bool mainOk = all.Where(r => r.Role == "main").All(r => r.Pass);
        bool controlsOk = all.Where(r => r.Role == "control").All(r => r.Pass);
        bool sourceOk = all.Where(r => r.Role == "source").All(r => r.Pass);
        bool extraOk = all.Where(r => r.Role == "extra").All(r => r.Pass);
        Say("");
        Say($"main cases        : {(mainOk ? "all as expected" : "FAILURES")}");
        Say($"controls          : {(controlsOk ? "all behaved as the absent capability predicts" : "UNEXPECTED")}");
        Say($"broken pipe       : {(brokenOk ? "PASS" : "FAIL")}");
        Say($"3000 ms (source)  : {(sourceOk ? "all as expected" : "FAILURES")}   (not part of the P6 verdict)");
        Say($"node fixtures     : {(extraOk ? "all as expected" : "FAILURES")}   (not part of the P6 verdict)");
        string verdict = !controlsOk ? "INCONCLUSIVE" : (mainOk && brokenOk ? "PASS" : "FAIL");
        Say($"P6 VERDICT: {verdict}");

        var csv = new StringBuilder("case,role,trial,pass,pid,in_job,stdin_closed_ms,signal_sent_ms,helper_done_ms,helper_exit,deadline_fired_ms,alive_at_deadline,kill_issued,kill_issued_ms,exited,exit_ms,exit_code,ignored_seen,grandchild_pid,grandchild_dead,grandchild_dead_ms,alive_at_observe,error\n");
        foreach (var r in all)
            csv.AppendLine(string.Join(",", r.Case, r.Role, r.Trial, r.Pass, r.Pid, r.InJob, F(r.StdinClosedMs), F(r.SignalSentMs), F(r.HelperDoneMs),
                r.HelperExit < 0 ? "" : "0x" + r.HelperExit.ToString("X", CultureInfo.InvariantCulture), F(r.DeadlineFiredMs), r.AliveAtDeadline, r.KillIssued, F(r.KillIssuedMs),
                r.Exited, F(r.ExitMs), r.ExitCode, r.IgnoredSeen, r.GrandchildPid, r.GrandchildDead, F(r.GrandchildDeadMs), r.AliveAtObserve,
                "\"" + r.Error.Replace("\"", "'") + "\""));
        File.WriteAllText(Path.Combine(outDir, "trials.csv"), csv.ToString());
        File.WriteAllLines(Path.Combine(outDir, "run_output.txt"), Log);
        return verdict == "PASS" ? 0 : 1;
    }
}
