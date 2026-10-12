using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.6; ED-NEW-web-w9-2. Native adapter behind PreviewDiscovery's offline test seams.
public sealed record PreviewListener(int ProcessId, int Port);

internal static class PreviewProcesses
{
    public static IReadOnlyCollection<string> LanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(i => i.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
        .Select(a => a.Address.ToString()).ToArray();

    public static IReadOnlyCollection<int> Tree(int root)
    {
        if (!OperatingSystem.IsWindows() || root <= 0) return [];
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) return [];
        try
        {
            var parents = new Dictionary<int, List<int>>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32FirstW(snapshot, ref entry)) return [];
            var exists = false;
            do
            {
                var pid = checked((int)entry.ProcessId);
                if (pid == root) exists = true;
                var parent = checked((int)entry.ParentId);
                if (!parents.TryGetValue(parent, out var children)) parents[parent] = children = [];
                children.Add(pid);
            } while (Process32NextW(snapshot, ref entry));
            if (!exists) return [];
            var found = new HashSet<int>();
            var pending = new Stack<int>(); pending.Push(root);
            while (pending.TryPop(out var pid))
            {
                if (!found.Add(pid)) continue;
                if (parents.TryGetValue(pid, out var children)) foreach (var child in children) pending.Push(child);
            }
            return found;
        }
        finally { CloseHandle(snapshot); }
    }

    public static IReadOnlyList<PreviewListener> Listeners()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var found = new List<PreviewListener>();
        ReadTable(2, 24, stateOffset: 0, portOffset: 8, pidOffset: 20, found); // IPv4 TCP_TABLE_OWNER_PID_LISTENER
        ReadTable(23, 56, stateOffset: 48, portOffset: 20, pidOffset: 52, found); // IPv6
        return found;
    }

    private static void ReadTable(int family, int stride, int stateOffset, int portOffset, int pidOffset, List<PreviewListener> found)
    {
        var size = 0;
        if (GetExtendedTcpTable(0, ref size, false, family, 3, 0) != 122 || size < 4) return;
        // The process table can grow between the size query and the fill; retry a bounded number of times.
        for (var attempt = 0; attempt < 3 && size <= 16 * 1024 * 1024; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var allocated = size;
                var status = GetExtendedTcpTable(buffer, ref size, false, family, 3, 0);
                if (status == 122) continue;
                if (status != 0) return;
                var count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (allocated - 4) / stride) return;
                for (var i = 0; i < count; i++)
                {
                    var offset = 4 + i * stride;
                    if (Marshal.ReadInt32(buffer, offset + stateOffset) != 2) continue; // MIB_TCP_STATE_LISTEN
                    var port = Marshal.ReadByte(buffer, offset + portOffset) * 256 + Marshal.ReadByte(buffer, offset + portOffset + 1);
                    var pid = Marshal.ReadInt32(buffer, offset + pidOffset);
                    if (pid > 0 && port > 0) found.Add(new PreviewListener(pid, port));
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size; public uint Usage; public uint ProcessId; public nint Heap; public uint Module; public uint Threads;
        public uint ParentId; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(nint table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
