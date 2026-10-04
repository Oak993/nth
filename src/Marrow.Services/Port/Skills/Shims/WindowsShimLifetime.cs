using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6, ED-NEW-f-sk-6. The shim itself joins the kill-on-close job BEFORE spawn,
// so every descendant inherits membership without a start/assign race.
public sealed class WindowsShimLifetime : IShimLifetime
{
    private nint job;
    public void Arm()
    {
        if (!OperatingSystem.IsWindows() || job != 0) return;
        var created = CreateJobObjectW(0, 0);
        if (created == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ShimJobExtendedLimits();
        limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(created, 9, ref limits, (uint)Marshal.SizeOf<ShimJobExtendedLimits>()) ||
            !AssignProcessToJobObject(created, Process.GetCurrentProcess().Handle))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(created);
            throw new Win32Exception(error);
        }
        // Deliberately process-lifetime: closing while we are alive would also kill this shim.
        // The non-inheritable handle closes on normal exit OR external termination.
        job = created;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, nint name);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, ref ShimJobExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
