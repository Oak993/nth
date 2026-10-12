// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7 "The gate", critique C1). Whether the user's
// foreground may be borrowed now: never an elevated, high-integrity or UIAccess window (the hooks cannot see input to it,
// and Windows would refuse the input anyway), never the secure desktop or the lock screen, and never while Windows
// reports a full-screen Direct3D app, presentation mode or a busy full-screen state (a game, a slideshow, a video).
// Reading only. Never used by automated tests: they bind a fake IForegroundProtection.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class ForegroundProtection(ISecurityProbe security) : IForegroundProtection
{
    // QUERY_USER_NOTIFICATION_STATE values.
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    public PcForegroundProtection Read()
    {
        if (security.SecureDesktop()) return PcForegroundProtection.SecureDesktop;
        if (security.Locked()) return PcForegroundProtection.Locked;
        var foreground = Win32.GetForegroundWindow();
        if (foreground != 0)
        {
            Win32.GetWindowThreadProcessId(foreground, out var processId);
            if (processId != 0 && processId != Environment.ProcessId)
            {
                var process = security.Process(processId);
                // Fail closed: a token Windows will not show us is treated as an admin window.
                if (!process.Readable || process.Elevated || process.HighIntegrity) return PcForegroundProtection.Elevated;
                if (process.UiAccess) return PcForegroundProtection.UiAccess;
            }
        }
        if (SHQueryUserNotificationState(out var state) != 0) return PcForegroundProtection.None;
        return state switch
        {
            QUNS_RUNNING_D3D_FULL_SCREEN => PcForegroundProtection.Fullscreen,
            QUNS_PRESENTATION_MODE => PcForegroundProtection.Presentation,
            QUNS_BUSY => PcForegroundProtection.Busy,
            _ => PcForegroundProtection.None,
        };
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
