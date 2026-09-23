// PORT-ONLY. PLATFORM BOUNDARY for TerminalSession.foregroundJobIsTheShell (TerminalStore.swift:69-76):
// ConPTY has no process groups and no tpgid, so the sysctl KERN_PROC_PID predicate has no translation.
// DEVIATION ED-046 (Q126, porting/engineering_decisions.md:191), quoted by PR-TERM-049: "On each Escape, enumerate
// live descendants of the ConPTY child PID (a snapshot of the process tree, or the shell's Job Object accounting
// if the shell is launched in one) and return true when the shell has no live descendant. Default to FALSE on any
// error, empty result, or enumeration timeout." PR-TERM-049 also records that this approximation is NOT YET
// accepted by the owner as a named deviation (PORT_CONTRACT.md:294-296 admits only owner-authorized deviations):
// it ships here per the packet's own guidance, flagged for escalation before release.
using System.Runtime.InteropServices;

namespace Marrow.App.Views.Terminal.Port;

public static class ProcessDescendants
{
    /// <summary>True when <paramref name="pid"/> has at least one live descendant in the current process snapshot;
    /// false on any error, an empty snapshot, or when <paramref name="pid"/> itself is not positive (fail closed,
    /// as the source's own sysctl failure path does).</summary>
    public static bool HasLiveDescendant(int pid)
    {
        if (pid <= 0) return false;
        var childrenByParent = new Dictionary<int, List<int>>();
        var alive = new HashSet<int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);
        if (snapshot == InvalidHandle) return false;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return false;
            do
            {
                var id = (int)entry.th32ProcessID;
                var parent = (int)entry.th32ParentProcessID;
                alive.Add(id);
                if (!childrenByParent.TryGetValue(parent, out var list)) childrenByParent[parent] = list = [];
                list.Add(id);
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
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
