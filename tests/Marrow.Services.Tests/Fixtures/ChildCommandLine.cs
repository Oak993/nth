// PORT-ONLY: integ-sk4-sk5 review fixture. Reads the raw command line Windows gave a running test-built stand-in exe
// (NtQueryInformationProcess, ProcessCommandLineInformation = 60, Windows 8.1+). No CLI, WMI or real installer.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marrow.Services.Tests.Fixtures;

public static class ChildCommandLine
{
    /// <summary>Waits until a process started from <paramref name="executable"/> exists and returns its exact command line.</summary>
    public static async Task<string> WaitForAsync(string executable, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WINDOWS-ONLY fixture.");
        var name = Path.GetFileNameWithoutExtension(executable);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    var line = Read(process);
                    if (line is not null) return line;
                }
            await Task.Delay(50);
        }
        throw new TimeoutException("The stand-in executable never started: " + executable);
    }

    public static void KillAll(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            using (process)
                try { process.Kill(); process.WaitForExit(5000); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }

    private static string? Read(Process process)
    {
        const int size = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(process.Handle, 60, buffer, size, out _) != 0) return null;
            var length = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return Marshal.PtrToStringUni(text, length / 2);
        }
        catch (InvalidOperationException) { return null; } // Exited between enumeration and query.
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returned);
}
