// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-2, ED-NEW-pcws-6; DESIGN.md 2.13, 3.1). Windows'
// child-session switch. ChildSessionReadiness only reads it (WTSIsChildSessionsEnabled) for the status line and the engine.
// ChildSessionSwitch writes it (WTSEnableChildSessions), and only WorkspaceCommands makes one, for the
// --tactus-workspace-enable and -disable commands the user runs in a terminal they opened as administrator; nothing else in
// nth holds one, so nth never changes the setting by itself.
using System.Runtime.InteropServices;
using System.Security.Principal;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>Reads whether child sessions are on; null when Windows could not say.</summary>
internal sealed class ChildSessionReadiness : IChildSessionReadiness
{
    public bool? Enabled() => ChildSessionNative.WTSIsChildSessionsEnabled(out var enabled) ? enabled : null;
}

/// <summary>Turns child sessions on or off: only for the user's own --tactus-workspace-enable and -disable commands.</summary>
internal sealed class ChildSessionSwitch : IChildSessionSwitch
{
    public bool Set(bool enabled, out int error)
    {
        var done = ChildSessionNative.WTSEnableChildSessions(enabled);
        error = done ? 0 : Marshal.GetLastWin32Error();
        return done;
    }
}

internal static class ChildSessionNative
{
    /// <summary>Whether this process runs as administrator (an elevated token), as the setting commands require.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSEnableChildSessions([MarshalAs(UnmanagedType.Bool)] bool enable);
}
