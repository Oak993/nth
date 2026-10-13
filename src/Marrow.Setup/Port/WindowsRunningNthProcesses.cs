using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a and 10, unit U4, ED-NEW-inst-u4-1). Finds and ends nth's programs with the Win32 calls that need
/// the least access: a process is looked at with <c>PROCESS_QUERY_LIMITED_INFORMATION</c> (it works on an elevated nth too, so an elevated nth
/// is found, then reported as not allowed instead of being missed). Nothing here is reflection, so it is safe in the trimmed setup.
/// <para>
/// Which programs: <c>nth.exe</c>, and <c>Marrow.App.exe</c> for an install made before the rename (PLAN.md 7). Inside the root means the
/// file the process runs lies below it (<see cref="RunningNthPaths"/>), whatever the process was started as.
/// </para>
/// </summary>
public sealed class WindowsRunningNthProcesses : IRunningNthProcesses
{
    /// <summary>The program names (without <c>.exe</c>) that are the app.</summary>
    public static readonly IReadOnlyList<string> AppProgramNames = ["nth", "Marrow.App"];

    /// <summary>How long ending a process waits for it to be gone.</summary>
    public static readonly TimeSpan EndWait = TimeSpan.FromSeconds(3);

    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x0010_0000;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private const uint WaitObject0 = 0;

    public IReadOnlyList<RunningNthProcess> FindInside(string installRoot)
    {
        var found = new List<RunningNthProcess>();
        foreach (var name in AppProgramNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(name);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception)
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                using (candidate)
                {
                    if (Describe(candidate.Id) is { } info && RunningNthPaths.IsInside(installRoot, info.Path))
                        found.Add(new RunningNthProcess(candidate.Id, info.Path, info.StartedAt));
                }
            }
        }
        return found;
    }

    public bool IsRunning(RunningNthProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, 0, process.ProcessId);
        if (handle == IntPtr.Zero) return Marshal.GetLastWin32Error() != ErrorInvalidParameter; // no such process; any other refusal means "can't tell", so "still there"
        try
        {
            if (WaitForSingleObject(handle, 0) == WaitObject0) return false; // signalled: it has exited
            return process.StartedAtTicks == 0 || StartedAt(handle) is not { } started || started == process.StartedAtTicks; // a recycled id is another program
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    public ProcessEndResult End(RunningNthProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var handle = OpenProcess(ProcessTerminate | Synchronize | ProcessQueryLimitedInformation, 0, process.ProcessId);
        if (handle == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() switch
            {
                ErrorInvalidParameter => ProcessEndResult.Gone,
                ErrorAccessDenied => ProcessEndResult.AccessDenied,
                _ => ProcessEndResult.Failed,
            };
        }
        try
        {
            if (WaitForSingleObject(handle, 0) == WaitObject0) return ProcessEndResult.Gone;
            var identity = DescribeHandle(handle); // Verify this acquired handle; never reopen a possibly recycled PID.
            if (CheckEndIdentity(process, identity?.Path, identity?.StartedAt) is { } identityFailure)
                return identityFailure;
            if (TerminateProcess(handle, 1) == 0)
                return Marshal.GetLastWin32Error() == ErrorAccessDenied ? ProcessEndResult.AccessDenied : ProcessEndResult.Failed;
            return WaitForSingleObject(handle, (uint)EndWait.TotalMilliseconds) == WaitObject0 ? ProcessEndResult.Ended : ProcessEndResult.Failed;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    // ED-NEW-inst-c7-4: retained uncertain records never authorize terminating an unverifiable handle.
    internal static ProcessEndResult? CheckEndIdentity(RunningNthProcess process, string? executable, long? started)
    {
        if (process.StartedAtTicks <= 0 || started is not > 0) return ProcessEndResult.Failed;
        if (started != process.StartedAtTicks) return ProcessEndResult.Gone;
        if (string.IsNullOrWhiteSpace(executable) || !string.Equals(process.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase))
            return ProcessEndResult.Failed;
        return null;
    }

    // The file a process runs and when it started, or null when Windows will not say (then it is not counted: the setup cannot reason about it).
    internal static RunningNthProcess? DescribeProcess(int processId) => Describe(processId) is { } info
        ? new RunningNthProcess(processId, info.Path, info.StartedAt) : null;

    private static (string Path, long StartedAt)? Describe(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, 0, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            return DescribeHandle(handle);
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static (string Path, long StartedAt)? DescribeHandle(IntPtr handle)
    {
        var buffer = new char[1024];
        var size = (uint)buffer.Length;
        if (QueryFullProcessImageNameW(handle, 0, buffer, ref size) == 0) return null;
        return (new string(buffer, 0, (int)size), StartedAt(handle) ?? 0);
    }

    private static long? StartedAt(IntPtr handle) =>
        GetProcessTimes(handle, out var created, out _, out _, out _) != 0 ? created : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, int inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] buffer, ref uint size);
}
