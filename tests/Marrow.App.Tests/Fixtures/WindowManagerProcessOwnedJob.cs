using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.Core.Port;
using Microsoft.Win32.SafeHandles;

namespace Marrow.App.Tests.Fixtures;

// Test-only ownership/cleanup around the EXISTING ProcessLauncher. It assigns its own job in
// CreateProcessW's attribute list (ProcessLauncher.cs:614-655), before child code can execute.
// Its watcher closes that job when the immediate runner exits (:679-699); the duplicated handle
// below deliberately survives that event so testhost descendants remain observable and cleanable.
internal sealed class WindowManagerProcessOwnedJob : IDisposable
{
    private readonly SafeWaitHandle job;
    private bool disposed;

    internal WindowManagerProcessOwnedJob(ProcessLauncher launcher)
    {
        var type = typeof(ProcessLauncher);
        var gate = type.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher)
            ?? throw new MissingFieldException(type.FullName, "gate");
        lock (gate)
        {
            var source = type.GetField("job", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher) as SafeWaitHandle
                ?? throw new MissingFieldException(type.FullName, "job");
            if (source.IsClosed || source.IsInvalid)
                throw new InvalidOperationException("The runner exited before its job ownership was retained.");
            if (!DuplicateHandle(GetCurrentProcess(), source, GetCurrentProcess(), out job, 0, false, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            if (!QueryLimits(job, 9, out var limits, Marshal.SizeOf<WindowManagerJobLimits>(), out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // This is the launcher's fresh test-owned job, never an enclosing job or a foreign process.
            limits.Basic.LimitFlags |= 0x00002000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<WindowManagerJobLimits>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch
        {
            _ = TerminateJobObject(job, 76);
            job.Dispose();
            throw;
        }
    }

    internal void RequireMember(Process candidate)
    {
        // Use a retained OS process handle; never rely on a name or an unpinned PID for cleanup.
        if (!IsProcessInJob(candidate.SafeHandle, job, out var belongs))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!belongs) throw new InvalidOperationException("Reported child is not in the job this parent launched.");
    }

    internal uint ActiveProcesses
    {
        get
        {
            if (!QueryAccounting(job, 1, out var info, Marshal.SizeOf<WindowManagerJobAccounting>(), out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return info.ActiveProcesses;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (ActiveProcesses != 0 && !TerminateJobObject(job, 76))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            WindowManagerProcessProtocol.Until(() => ActiveProcesses == 0,
                WindowManagerProcessProtocol.CleanupBound, "Owned runner/testhost processes did not leave the job.");
        }
        finally { job.Dispose(); }
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool DuplicateHandle(nint sourceProcess, SafeWaitHandle source, nint targetProcess,
        out SafeWaitHandle duplicate, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeWaitHandle job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool TerminateJobObject(SafeWaitHandle job, uint exitCode);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool QueryAccounting(SafeWaitHandle job, int kind, out WindowManagerJobAccounting info,
        int length, out int returnedLength);

    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool QueryLimits(SafeWaitHandle job, int kind, out WindowManagerJobLimits info,
        int length, out int returnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetInformationJobObject(SafeWaitHandle job, int kind, ref WindowManagerJobLimits info,
        int length);
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowManagerJobBasicLimits
{
    public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass, SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowManagerJobIoCounters
{
    public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
    public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowManagerJobLimits
{
    public WindowManagerJobBasicLimits Basic;
    public WindowManagerJobIoCounters Io;
    public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowManagerJobAccounting
{
    public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
}
