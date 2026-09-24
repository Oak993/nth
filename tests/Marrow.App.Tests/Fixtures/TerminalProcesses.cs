using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace Marrow.App.Tests.Fixtures;

// Helpers for the terminal store tests (App.Views.Terminal.TerminalStore). Helper types live here, never inside a
// test class (R-TEST-3). The only processes involved are the shells the store itself starts (Windows PowerShell or
// PowerShell 7, OD-0008 C5) and, in one test, a PowerShell grandchild that sleeps; never a real AI CLI (R-TEST-10).

/// <summary>Real temporary folders under MARROW_PORT_DATA_DIR (R-TEST-9); never the user's profile.</summary>
public static class TerminalTestFolders
{
    /// <summary>
    /// A folder name with a space, an apostrophe, a percent-delimited variable name, an ampersand and a non-ASCII
    /// letter: TerminalStore.swift:33-34 passes the directory into the environment with no quoting or escaping
    /// (PR-TERM-041), and PORT_CONTRACT section 11 asks for paths with spaces and non-ASCII characters.
    /// </summary>
    public const string AwkwardName = "shell dir it's %PATH% & caf\U000000E9"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE

    public static string Create(string label)
    {
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Join(root, "terminal-store", label + " " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.GetFullPath(folder);
    }

    /// <summary>A project directory with <see cref="AwkwardName"/> inside a fresh folder.</summary>
    public static string CreateProjectDirectory(string label)
    {
        var folder = Path.Join(Create(label), AwkwardName);
        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>Waits that keep the headless UI thread pumping, so posted main-actor work and terminal output run.</summary>
public static class UiPump
{
    /// <summary>The longest any wait on a real shell may take: PowerShell with the user's profile can be slow.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    /// <summary>Runs the dispatcher until <paramref name="condition"/> holds; false when the bound passes first.</summary>
    public static bool Until(Func<bool> condition, TimeSpan bound)
    {
        var deadline = DateTime.UtcNow + bound;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(20);
        }
    }

    /// <summary>Runs the dispatcher for <paramref name="span"/>, for "nothing happens" checks.</summary>
    public static void For(TimeSpan span) => Until(() => false, span);

    /// <summary>The text of a file a script writes, once it exists and is non-empty; null when the bound passes.</summary>
    public static string? WaitForText(string path, TimeSpan bound)
    {
        string? text = null;
        Until(() =>
        {
            if (!File.Exists(path)) return false;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return false;
                text = Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n');
                return true;
            }
            catch (IOException)
            {
                return false; // still being written
            }
        }, bound);
        return text;
    }
}

/// <summary>What the operating system reports for a running shell: read from its process parameters.</summary>
public sealed record ShellProcessInfo(
    int ProcessId,
    string ImagePath,
    string CommandLine,
    IReadOnlyList<string> Arguments,
    string CurrentDirectory,
    IReadOnlyList<string> EnvironmentEntries)
{
    /// <summary>The value of the first entry whose name matches exactly, as the child sees it; null when absent.</summary>
    public string? Variable(string name)
    {
        foreach (var entry in EnvironmentEntries)
            if (entry.StartsWith(name + "=", StringComparison.Ordinal)) return entry[(name.Length + 1)..];
        return null;
    }

    /// <summary>How many entries name <paramref name="name"/> in any letter case (Windows names are case-insensitive).</summary>
    public int CountVariable(string name) =>
        EnvironmentEntries.Count(e => e.Length > name.Length && e[name.Length] == '=' &&
                                      string.Equals(e[..name.Length], name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Finds the shell a terminal session started, by its image name (pwsh or powershell, OD-0008 C5) and its working
/// directory, which every test makes unique; then reads its command line, working directory and environment from
/// the process environment block (x64 layout: PEB+0x20 ProcessParameters; RTL_USER_PROCESS_PARAMETERS+0x38
/// CurrentDirectory.DosPath, +0x60 ImagePathName, +0x70 CommandLine, +0x80 Environment, +0x3F0 EnvironmentSize).
/// </summary>
public static class ShellProbe
{
    public static readonly string[] ShellImageNames = ["pwsh", "powershell"];

    /// <summary>The live shell whose working directory is <paramref name="directory"/>, skipping <paramref name="except"/>.</summary>
    public static ShellProcessInfo? Find(string directory, ISet<int>? except = null)
    {
        foreach (var name in ShellImageNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (except is not null && except.Contains(process.Id)) continue;
                    var info = TryRead(process.Id);
                    if (info is null) continue;
                    if (SameDirectory(info.CurrentDirectory, directory)) return info;
                }
            }
        }
        return null;
    }

    /// <summary>Waits, pumping the UI thread, for the shell in <paramref name="directory"/> to appear.</summary>
    public static ShellProcessInfo? WaitFor(string directory, TimeSpan bound, ISet<int>? except = null)
    {
        ShellProcessInfo? found = null;
        UiPump.Until(() => (found = Find(directory, except)) is not null, bound);
        return found;
    }

    /// <summary>A trailing separator is not part of the comparison; letters are compared exactly.</summary>
    public static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    public static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Waits, pumping the UI thread, until the process is gone; false when the bound passes first.</summary>
    public static bool WaitForExit(int processId, TimeSpan bound) => UiPump.Until(() => !IsAlive(processId), bound);

    /// <summary>Test cleanup only: ends a process a failed assertion left behind.</summary>
    public static void KillIfAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    public static ShellProcessInfo? TryRead(int processId)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var basic = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref basic, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0) return null;
            var parameters = ReadPointer(handle, basic.PebBaseAddress + 0x20);
            if (parameters == IntPtr.Zero) return null;
            var directory = ReadUnicodeString(handle, parameters + 0x38);
            var image = ReadUnicodeString(handle, parameters + 0x60);
            var commandLine = ReadUnicodeString(handle, parameters + 0x70);
            var environment = ReadPointer(handle, parameters + 0x80);
            var environmentSize = ReadPointer(handle, parameters + 0x3F0);
            if (directory is null || image is null || commandLine is null || environment == IntPtr.Zero) return null;
            var entries = ReadEnvironment(handle, environment, (long)environmentSize);
            if (entries is null) return null;
            return new ShellProcessInfo(processId, image, commandLine, WindowsCommandLine.Parse(commandLine), directory, entries);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IntPtr ReadPointer(IntPtr process, IntPtr address)
    {
        var buffer = new byte[IntPtr.Size];
        if (!ReadProcessMemory(process, address, buffer, buffer.Length, out var read) || read != buffer.Length) return IntPtr.Zero;
        return (IntPtr)BitConverter.ToInt64(buffer, 0);
    }

    private static string? ReadUnicodeString(IntPtr process, IntPtr address)
    {
        var header = new byte[16];
        if (!ReadProcessMemory(process, address, header, header.Length, out var read) || read != header.Length) return null;
        var length = BitConverter.ToUInt16(header, 0);
        var buffer = (IntPtr)BitConverter.ToInt64(header, 8);
        if (length == 0) return "";
        var text = new byte[length];
        if (!ReadProcessMemory(process, buffer, text, text.Length, out read) || read != text.Length) return null;
        return Encoding.Unicode.GetString(text);
    }

    private static List<string>? ReadEnvironment(IntPtr process, IntPtr address, long size)
    {
        if (size <= 0 || size > 1 << 22) return null;
        var bytes = new byte[size];
        if (!ReadProcessMemory(process, address, bytes, bytes.Length, out var read) || read == IntPtr.Zero) return null;
        var text = Encoding.Unicode.GetString(bytes, 0, (int)read & ~1);
        var end = text.IndexOf("\0\0", StringComparison.Ordinal);
        if (end < 0) return null;
        return [.. text[..end].Split('\0')];
    }

    private const int ProcessQueryInformation = 0x0400;
    private const int ProcessVmRead = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, ref ProcessBasicInformation information, int length, out int returned);
}

/// <summary>CommandLineToArgvW, the parser R-PROC-1 names for what a child sees as its argv.</summary>
public static class WindowsCommandLine
{
    [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CommandLineToArgv(string commandLine, out int count);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static IReadOnlyList<string> Parse(string commandLine)
    {
        var argv = CommandLineToArgv(commandLine, out var count);
        if (argv == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
