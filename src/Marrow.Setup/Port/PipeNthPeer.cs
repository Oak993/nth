using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

// PORT-ONLY: ED-NEW-inst-c7-4. CurrentUserOnly authenticates the account; this authenticates the executable.
internal static partial class PipeNthPeer
{
    internal static RunningNthProcess? Describe(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)
            || pid is 0 or > int.MaxValue) return null;
        var process = WindowsRunningNthProcesses.DescribeProcess((int)pid);
        return process is { StartedAtTicks: > 0 } ? process : null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
