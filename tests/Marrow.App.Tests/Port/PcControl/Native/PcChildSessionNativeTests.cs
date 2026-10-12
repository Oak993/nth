// PORT-ONLY: Tactus native probe NP-5, child sessions (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.13, 5.4 item 11).
// NATIVE, QUERY ONLY: records the Windows edition and build, whether child sessions are already enabled
// (WTSIsChildSessionsEnabled), the Remote Desktop switch (fDenyTSConnections) and how TermService starts. It never enables
// anything and changes no setting: whether WTSEnableChildSessions works with Remote Desktop off is tried by hand only
// with the separate workspace (P18), which the smoke script lists as blocked until then.
using System.Runtime.InteropServices;
using Marrow.App.Tests.Fixtures;
using Microsoft.Win32;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcChildSessionNativeTests
{
    [Fact]
    public void NP5_The_child_session_state_is_read_without_changing_anything()
    {
        using var rig = new PcSmokeRig("np5-child-sessions");
        using var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        using var server = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TermService");
        var queried = WTSIsChildSessionsEnabled(out var enabled);

        var start = service?.GetValue("Start") switch
        {
            2 => "automatic",
            3 => "manual",
            4 => "disabled",
            var other => "unknown (" + other + ")",
        };
        rig.Result("edition " + version?.GetValue("EditionID") + ", build " + version?.GetValue("CurrentBuild")
            + "; child sessions " + (queried ? enabled ? "enabled" : "not enabled" : "query failed (" + Marshal.GetLastPInvokeError() + ")")
            + "; Remote Desktop " + (server?.GetValue("fDenyTSConnections") is 0 ? "on" : "off")
            + "; TermService start " + start);
        Assert.True(queried);
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}
