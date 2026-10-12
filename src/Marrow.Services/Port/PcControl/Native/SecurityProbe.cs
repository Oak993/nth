// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 3.1, 4.2). Reads a process token's elevation, integrity level
// and UIAccess flag, and whether the input desktop is the secure desktop or the session is locked. Never used by
// automated tests; the pure parts (the integrity rule) are.
using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class SecurityProbe : ISecurityProbe
{
    /// <summary>SECURITY_MANDATORY_HIGH_RID: high integrity and above (system, protected process) are refused.</summary>
    public const uint HighIntegrityRid = 0x3000;

    public static bool IsHighIntegrity(uint rid) => rid >= HighIntegrityRid;

    public PcProcessSecurity Process(int processId)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return new PcProcessSecurity(false, false, false, Readable: false);
        try
        {
            if (!Win32.OpenProcessToken(process, Win32.TOKEN_QUERY, out var token)) return new PcProcessSecurity(false, false, false, Readable: false);
            try
            {
                var elevated = ReadInt(token, Win32.TokenElevation) is { } e ? e != 0 : (bool?)null;
                var uiAccess = ReadInt(token, Win32.TokenUIAccess) is { } u ? u != 0 : (bool?)null;
                var rid = IntegrityRid(token);
                if (elevated is null || uiAccess is null || rid is null) return new PcProcessSecurity(false, false, false, Readable: false);
                return new PcProcessSecurity(elevated.Value, IsHighIntegrity(rid.Value), uiAccess.Value);
            }
            finally { Win32.CloseHandle(token); }
        }
        finally { Win32.CloseHandle(process); }
    }

    public bool SecureDesktop()
    {
        var desktop = Win32.OpenInputDesktop(0, false, Win32.DESKTOP_READOBJECTS);
        // The secure desktop (UAC, Ctrl+Alt+Del, the logon screen) cannot be opened by a user process.
        if (desktop == 0) return true;
        try
        {
            var name = new StringBuilder(256);
            return !Win32.GetUserObjectInformation(desktop, Win32.UOI_NAME, name, name.Capacity * 2, out _)
                || !string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { Win32.CloseDesktop(desktop); }
    }

    public bool Locked()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == 0) return SecureDesktop();
        Win32.GetWindowThreadProcessId(foreground, out var processId);
        var exe = WindowCatalog.ExePath(processId);
        return exe is not null && string.Equals(Path.GetFileName(exe), "LockApp.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadInt(nint token, int informationClass)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try { return Win32.GetTokenInformation(token, informationClass, buffer, sizeof(int), out _) ? Marshal.ReadInt32(buffer) : null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label }: the label SID's last sub-authority is the integrity RID.
    private static uint? IntegrityRid(nint token)
    {
        Win32.GetTokenInformation(token, Win32.TokenIntegrityLevel, 0, 0, out var needed);
        if (needed <= 0 || needed > 4096) return null;
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!Win32.GetTokenInformation(token, Win32.TokenIntegrityLevel, buffer, needed, out _)) return null;
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(Win32.GetSidSubAuthorityCount(sid));
            if (count == 0) return null;
            return unchecked((uint)Marshal.ReadInt32(Win32.GetSidSubAuthority(sid, (uint)(count - 1))));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
