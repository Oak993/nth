// P6 probe (T-M0-005): scripted fixture stand-ins and the console-control helper.
// Throwaway probe code, never production code. No real AI CLI is ever launched.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace P6;

static unsafe class Fixtures
{
    // Exit codes are probe-only markers, chosen so the runner can tell WHO ended a process:
    //   130 = cooperating fixture left on CTRL_C_EVENT     (128 + SIGINT, by analogy)
    //   131 = cooperating fixture left on CTRL_BREAK_EVENT
    //   137 = TerminateJobObject at the deadline            (128 + SIGKILL, by analogy)
    public const uint ExitOnCtrlC = 130, ExitOnBreak = 131, ExitByJob = 137;

    public static int Run(string kind)
    {
        switch (kind)
        {
            case "coop":
                // Cooperating: leaves promptly on Ctrl+C or Ctrl+Break. Does NOT leave on stdin EOF,
                // so a stdin close alone cannot make it exit (the stdin-only control relies on this).
                Native.SetConsoleCtrlHandler(&CoopHandler, 1);
                Console.WriteLine($"PID {Environment.ProcessId} CONSOLEWND {Native.GetConsoleWindow()}");
                Console.WriteLine("READY");
                Thread.Sleep(Timeout.Infinite);
                return 0;

            case "stubborn":
                // Deliberately non-cooperating: swallows Ctrl+C and Ctrl+Break, and owns a grandchild
                // that does the same (Shell.swift:116 kills the whole process group; the Job Object
                // must reach the grandchild too).
                Native.SetConsoleCtrlHandler(&IgnoreHandler, 1);
                var psi = new ProcessStartInfo(Environment.ProcessPath!, "fixture sleeper")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                var gc = Process.Start(psi)!;
                var first = gc.StandardOutput.ReadLine();
                Console.WriteLine($"PID {Environment.ProcessId} GRANDCHILD {gc.Id} {(first == "SLEEPER-UP" ? "ok" : "bad")}");
                Console.WriteLine("READY");
                Thread.Sleep(Timeout.Infinite);
                return 0;

            case "sleeper":
                Native.SetConsoleCtrlHandler(&IgnoreHandler, 1);
                Console.WriteLine("SLEEPER-UP");
                Thread.Sleep(Timeout.Infinite);
                return 0;

            default:
                Console.Error.WriteLine($"unknown fixture {kind}");
                return 2;
        }
    }

    [UnmanagedCallersOnly]
    static int CoopHandler(uint type)
    {
        if (type == Native.CTRL_C_EVENT) Native.ExitProcess(ExitOnCtrlC);
        if (type == Native.CTRL_BREAK_EVENT) Native.ExitProcess(ExitOnBreak);
        return 0;
    }

    [UnmanagedCallersOnly]
    static int IgnoreHandler(uint type)
    {
        if (type == Native.CTRL_C_EVENT || type == Native.CTRL_BREAK_EVENT)
        {
            try { Console.WriteLine($"IGNORED {type}"); } catch (IOException) { }
            return 1;
        }
        return 0;
    }

    // ---- the helper -----------------------------------------------------------------------
    // Result is carried in the exit code because a DETACHED_PROCESS helper has no console:
    //   0                      ok
    //   0x10000 | GetLastError AttachConsole failed
    //   0x20000 | GetLastError GenerateConsoleCtrlEvent failed
    //   bit 0x40000000         the helper's own handler saw the event (misdelivery)
    static volatile bool _helperHit;

    [UnmanagedCallersOnly]
    static int HelperGuard(uint type)
    {
        if (type == Native.CTRL_C_EVENT || type == Native.CTRL_BREAK_EVENT) { _helperHit = true; return 1; }
        return 0;
    }

    /// attach = true : the probe's helper. FreeConsole, AttachConsole(target), GenerateConsoleCtrlEvent
    ///                 (CTRL_BREAK_EVENT, target group), FreeConsole.
    /// attach = false: the CONTROL. The same call made from a process that is NOT attached to the
    ///                 target's console (the "helper absent" case).
    public static int CtrlBreak(int pid, bool attach)
    {
        int code = 0;
        if (attach)
        {
            Native.FreeConsole();
            if (Native.AttachConsole(pid) == 0) return 0x10000 | Marshal.GetLastPInvokeError();
        }
        Native.SetConsoleCtrlHandler(&HelperGuard, 1);
        if (Native.GenerateConsoleCtrlEvent(Native.CTRL_BREAK_EVENT, pid) == 0)
            code = 0x20000 | Marshal.GetLastPInvokeError();
        Thread.Sleep(50); // give a misdelivered event time to reach our own guard
        if (attach) Native.FreeConsole();
        if (_helperHit) code |= 0x40000000;
        return code;
    }
}
