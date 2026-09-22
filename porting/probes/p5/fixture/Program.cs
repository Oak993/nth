// P5 fixture: a stand-in for an agent CLI (task packet T-M0-004). Throwaway, never production code.
//
// The owner's safety rules forbid launching claude.exe or any real AI CLI on this PC (it has
// signed-in agents), so the probe launches this console app instead. It:
//   - announces its PID and whether its stdout / stdin are redirected (under a real pseudoconsole
//     they are console handles, so both must read False),
//   - writes one marker to stdout and a different one to stderr,
//   - answers "cols <n>" with its console column count, as the console reports it,
//   - answers "spawn" by creating a grandchild (itself, "sleep 120") with a plain CreateProcessW,
//     creation flags 0, and printing the grandchild's PID,
//   - exits on "exit" or end of input.
// Safety net: the child exits by itself after 180 s and the grandchild after 120 s, so a crashed
// probe cannot leave them running for long.

using System.Globalization;
using System.Runtime.InteropServices;

public static class Fixture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreateProcessW(IntPtr lpApplicationName, IntPtr lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        IntPtr lpCurrentDirectory, ref STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "sleep")
        {
            Thread.Sleep(TimeSpan.FromSeconds(int.Parse(args[1], CultureInfo.InvariantCulture)));
            return 0;
        }

        var safety = new Thread(() => { Thread.Sleep(TimeSpan.FromSeconds(180)); Environment.Exit(3); }) { IsBackground = true };
        safety.Start();

        Console.Out.WriteLine($"FIXTURE-READY pid={Environment.ProcessId} outRedirected={Console.IsOutputRedirected} inRedirected={Console.IsInputRedirected} END");
        Console.Out.WriteLine("OUT-CHANNEL-MARK via-stdout");
        Console.Error.WriteLine("ERR-CHANNEL-MARK via-stderr");

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "cols":
                    var tag = parts.Length > 1 ? parts[1] : "?";
                    Console.Out.WriteLine($"R{tag} COLS={Console.WindowWidth} BUF={Console.BufferWidth} ROWS={Console.WindowHeight} END");
                    break;
                case "spawn":
                    Spawn();
                    break;
                case "exit":
                    return 0;
            }
        }
        return 0;
    }

    static void Spawn()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        IntPtr cmd = Marshal.StringToHGlobalUni($"\"{exe}\" sleep 120");
        try
        {
            var si = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>() };
            if (!CreateProcessW(IntPtr.Zero, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, IntPtr.Zero, ref si, out var pi))
            {
                Console.Out.WriteLine($"SPAWN-FAILED err={Marshal.GetLastWin32Error()} END");
                return;
            }
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            Console.Out.WriteLine($"GRANDCHILD pid={pi.dwProcessId} END");
        }
        finally
        {
            Marshal.FreeHGlobal(cmd);
        }
    }
}
