// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6 "What Stop all does", 15.4; unit AS-5; ED-NEW-dextra-as5-5). No Swift twin.
// One Windows Job Object for one agent of a mission: its provider process and every process that process starts run in
// it, so Stop ends the whole tree (a build or a dev server the agent started included), and closing the job (the
// mission ended, or nth itself ended or crashed) ends it too (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE). ProcessLauncher puts a
// child in it at CreateProcess time (PROC_THREAD_ATTRIBUTE_JOB_LIST), before the child runs one instruction, so no child
// can start a process outside it first.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Core.Port;

/// <summary>A kill-on-close Job Object (12.6).</summary>
public sealed class JobObject : IDisposable
{
    /// <summary>The exit code Terminate leaves, as ProcessLauncher's KillTree does.</summary>
    public const uint TerminatedExitCode = 9;

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectExtendedLimitInformation = 9;

    private readonly SafeWaitHandle handle;
    private int terminated;

    private JobObject(SafeWaitHandle handle) => this.handle = handle;

    /// <summary>A new job with the kill-on-close limit. Throws when Windows refuses it (then no mission agent starts).</summary>
    public static JobObject Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
        return new JobObject(job);
    }

    /// <summary>The raw handle, for ProcessLauncher's job list; valid until Dispose.</summary>
    internal SafeWaitHandle Handle => handle;

    /// <summary>Closed, or terminated: no process may start in it any more.</summary>
    public bool IsClosed => handle.IsClosed || Volatile.Read(ref terminated) != 0;

    /// <summary>How many processes in the job are alive now; 0 once it is closed.</summary>
    public int ActiveProcesses
    {
        get
        {
            if (handle.IsClosed) return 0;
            var added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                return QueryInformationJobObject(handle, JobObjectBasicAccountingInformation, out var info,
                    Marshal.SizeOf<BasicAccountingInformation>(), IntPtr.Zero)
                    ? (int)info.ActiveProcesses
                    : 0;
            }
            catch (ObjectDisposedException) { return 0; }
            finally
            {
                if (added) handle.DangerousRelease();
            }
        }
    }

    /// <summary>Ends every process in the job at once. A job that is already closed has nothing left to end.</summary>
    public void Terminate()
    {
        Volatile.Write(ref terminated, 1);
        if (handle.IsClosed) return;
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            TerminateJobObject(handle, TerminatedExitCode);
        }
        catch (ObjectDisposedException)
        {
            // Closed between the check and the reference: closing it already ended every process (kill-on-close), so there is
            // nothing left to end (ED-NEW-dextra-as5-13).
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    /// <summary>Closes the job; the kill-on-close limit ends whatever still runs in it.</summary>
    public void Dispose()
    {
        Volatile.Write(ref terminated, 1);
        handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeWaitHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetInformationJobObject(SafeWaitHandle job, int infoClass, ref ExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool QueryInformationJobObject(SafeWaitHandle job, int infoClass, out BasicAccountingInformation info, int length, IntPtr returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool TerminateJobObject(SafeWaitHandle job, uint exitCode);
}
