// PORT-ONLY: UE-3 (ED-NEW-providers-git-1). A read-only look at the processes that hold Git for Windows, so Git's update (and its Retry) says what is running before it starts the
// installer. It only reads: a process snapshot, each image path through a query-only handle, and Git's install folder from its own registry key. Nothing is stopped, signalled or changed.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Marrow.Services.Port.Updates;

public static class WindowsGitLocks
{
    // Hosts known by their executable name. Explorer is the desktop itself (Git Bash opened from Start or a folder): no host is named for it.
    private static readonly Dictionary<string, string?> KnownHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude.exe"] = "Claude Code", ["Code.exe"] = "VS Code", ["Code - Insiders.exe"] = "VS Code Insiders", ["Cursor.exe"] = "Cursor", ["Windsurf.exe"] = "Windsurf",
        ["WindowsTerminal.exe"] = "Windows Terminal", ["OpenConsole.exe"] = "Windows Terminal", ["powershell.exe"] = "Windows PowerShell", ["pwsh.exe"] = "PowerShell",
        ["cmd.exe"] = "Command Prompt", ["devenv.exe"] = "Visual Studio", ["idea64.exe"] = "IntelliJ IDEA", ["rider64.exe"] = "Rider", ["GitHubDesktop.exe"] = "GitHub Desktop",
        ["SourceTree.exe"] = "Sourcetree", ["explorer.exe"] = null,
    };

    /// <summary>The running processes whose executable is inside Git for Windows' install folder, each with the first app outside that folder that started it.</summary>
    public static Task<IReadOnlyList<GitLockingProcess>> Find(CancellationToken cancellation) => Task.Run(() => Scan(InstallFolder(), cancellation), cancellation);

    /// <summary>Git for Windows' install folder as its installer records it (HKLM\SOFTWARE\GitForWindows InstallPath), or null when it is not recorded.</summary>
    public static string? InstallFolder()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\GitForWindows");
            return key?.GetValue("InstallPath") is string path && Path.IsPathFullyQualified(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return null; }
    }

    private sealed record Entry(int Id, int Parent, string Name, string? Path, long Started);

    private static IReadOnlyList<GitLockingProcess> Scan(string? root, CancellationToken cancellation)
    {
        if (root is null) return [];
        var entries = Snapshot().ToDictionary(e => e.Id);
        var prefix = root + System.IO.Path.DirectorySeparatorChar;
        bool InGit(Entry e) => e.Path is { } path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        var found = new List<GitLockingProcess>();
        foreach (var entry in entries.Values.Where(InGit).OrderBy(e => e.Id))
        {
            cancellation.ThrowIfCancellationRequested();
            found.Add(new(entry.Name, Host(entry)));
        }
        return found;

        string? Host(Entry entry)
        {
            var seen = new HashSet<int> { entry.Id };
            var child = entry;
            while (entries.TryGetValue(child.Parent, out var parent) && seen.Add(parent.Id) && seen.Count < 64)
            {
                if (parent.Started == 0 || child.Started == 0 || parent.Started > child.Started) return null; // the parent is gone and its id was reused
                if (!InGit(parent)) return Name(parent);
                child = parent;
            }
            return null;
        }
    }

    private static string? Name(Entry host)
    {
        if (KnownHosts.TryGetValue(host.Name, out var known)) return known;
        try
        {
            var description = host.Path is null ? null : FileVersionInfo.GetVersionInfo(host.Path).FileDescription?.Trim();
            if (description is { Length: > 0 and <= 40 }) return description;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        return System.IO.Path.GetFileNameWithoutExtension(host.Name);
    }

    private static List<Entry> Snapshot()
    {
        var list = new List<Entry>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1) return list;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                var (path, started) = Query((int)entry.ProcessId);
                list.Add(new((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile, path, started));
            }
        }
        finally { CloseHandle(snapshot); }
        return list;
    }

    private static (string? Path, long Started) Query(int id)
    {
        if (id == 0) return (null, 0);
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)id);
        if (handle == 0) return (null, 0);
        try
        {
            var buffer = new char[1024];
            var length = (uint)buffer.Length;
            string? path = null;
            if (QueryFullProcessImageNameW(handle, 0, buffer, ref length))
            {
                try { path = System.IO.Path.GetFullPath(new string(buffer, 0, (int)length)); }
                catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { path = null; }
            }
            var started = GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : 0;
            return (path, started);
        }
        finally { CloseHandle(handle); }
    }

    private const uint SnapProcess = 0x00000002;
    private const uint QueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
