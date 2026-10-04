using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6, ED-309. The shim itself joins the kill-on-close job BEFORE spawn,
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
        // The non-inheritable handle closes on external termination, which kills the tree.
        job = created;
        // On a normal exit the target has finished, and a background helper it left running must live on as it would without the shim (the plan asks for the tree to die
        // when the shim is KILLED). A crash or a kill never raises ProcessExit, so the tree is still ended there.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();
    }

    private void Release()
    {
        var none = new ShimJobExtendedLimits();
        SetInformationJobObject(job, 9, ref none, (uint)Marshal.SizeOf<ShimJobExtendedLimits>());
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
