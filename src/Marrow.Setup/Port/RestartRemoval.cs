using System.Runtime.InteropServices;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8: "The temp copy deletes itself (MoveFileEx with delete-on-reboot as a fallback)"; unit
/// U6, ED-NEW-inst-u6-1). <c>MoveFileExW(path, NULL, MOVEFILE_DELAY_UNTIL_REBOOT)</c>: Windows removes the file (or an empty
/// folder) at the next restart. It writes <c>PendingFileRenameOperations</c> under HKLM, so Windows grants it to an
/// administrator only; for a per-user uninstaller it normally fails, and the copy is left for the sweeps (unit U9). Called
/// only from src/Marrow.Setup/Program.cs as the uninstaller's copy ends; no test calls it.
/// </summary>
public static partial class RestartRemoval
{
    private const uint MoveFileDelayUntilReboot = 0x4;

    public static bool DeleteAtRestart(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return OperatingSystem.IsWindows() && MoveFileExW(path, null, MoveFileDelayUntilReboot);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string existing, string? replacement, uint flags);
}
