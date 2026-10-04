// PORT-ONLY. PLATFORM BOUNDARY for TerminalSession.foregroundJobIsTheShell (TerminalStore.swift:69-76):
// ConPTY has no process groups and no tpgid, so the sysctl KERN_PROC_PID predicate has no translation.
// ACCEPTED DEVIATION OD-0031 (2026-09-30) / ED-046 (Q126, porting/engineering_decisions.md:189), quoted by PR-TERM-049:
// "On each Escape, enumerate live descendants of the ConPTY child PID (a snapshot of the process tree, or the shell's
// Job Object accounting if the shell is launched in one) and return true when the shell has no live descendant. Default
// to FALSE on any error, empty result, or enumeration timeout." The owner accepted it as a named deviation:
// "Escape closes the terminal drawer only when the shell has no live child program. Otherwise Escape goes to the
// terminal, and if it can't tell, it also goes to the terminal."
//
// The answer here is therefore three-valued: true (a live child owns the terminal), false (the shell has none) and
// null (cannot tell: bad pid, no snapshot, an enumeration that fails or times out, a shell pid that is not in the
// snapshot). TerminalSession.ForegroundJobIsTheShell treats only an explicit false as "the shell owns the
// foreground"; null passes Escape through to the terminal exactly as the source's sysctl failure path does.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Marrow.App.Views.Terminal.Port;

public static class ProcessDescendants
{
    /// <summary>One row of a process table snapshot.</summary>
    internal readonly record struct ProcessRow(int Id, int ParentId);

    /// <summary>The process-table reader. A null table means the snapshot could not be taken. Replaceable so a test can
    /// force a failure or a chosen tree; the default reads the real table through the Toolhelp API.</summary>
    internal static Func<IReadOnlyList<ProcessRow>?> ReadProcessTable { get; set; } = ReadToolhelpTable;

    /// <summary>ED-046 "enumeration timeout": a snapshot walk longer than this is treated as unknown.</summary>
    internal static TimeSpan EnumerationTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>True when <paramref name="pid"/> has at least one live descendant in the current process snapshot, false when
    /// it is in the snapshot and has none, and null when that cannot be told (a non-positive pid, a failed or timed-out
    /// snapshot, or a pid that is absent from the snapshot).</summary>
    public static bool? HasLiveDescendant(int pid)
    {
        if (pid <= 0) return null;
        var table = ReadProcessTable();
        if (table is null) return null;
        var childrenByParent = new Dictionary<int, List<int>>();
        var alive = new HashSet<int>();
        foreach (var row in table)
        {
            alive.Add(row.Id);
            if (!childrenByParent.TryGetValue(row.ParentId, out var list)) childrenByParent[row.ParentId] = list = [];
            list.Add(row.Id);
        }
        if (!alive.Contains(pid)) return null;
        if (!childrenByParent.TryGetValue(pid, out var direct) || direct.Count == 0) return false;
        var stack = new Stack<int>(direct);
        var visited = new HashSet<int>();
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!visited.Add(next)) continue;
            if (alive.Contains(next)) return true;
            if (childrenByParent.TryGetValue(next, out var grandchildren))
                foreach (var child in grandchildren) stack.Push(child);
        }
        return false;
    }

    private static IReadOnlyList<ProcessRow>? ReadToolhelpTable()
    {
        var clock = Stopwatch.StartNew();
        var rows = new List<ProcessRow>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
        if (snapshot == InvalidHandle) return null;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return null;
            do
            {
                if (clock.Elapsed >= EnumerationTimeout) return null;
                rows.Add(new ProcessRow((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return rows;
    }

    private const int Th32csSnapprocess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(int flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
