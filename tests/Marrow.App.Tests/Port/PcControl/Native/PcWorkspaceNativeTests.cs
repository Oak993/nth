// PORT-ONLY: Tactus native probe NP-5b, the separate workspace's readiness (unit P18, ED-NEW-pcws-11; DESIGN.md 2.13, NP-5).
// NATIVE, QUERY ONLY: runs only when the owner schedules native tests (every regular run excludes FullyQualifiedName~Native).
// The owner first turns child sessions on himself, with the command Settings › Tactus › Separate workspace › Set up shows,
// in a Terminal he opened as administrator; this test then reads, without changing anything, what nth reads: whether
// child sessions are on (WTSIsChildSessionsEnabled through ChildSessionReadiness), what the Settings group shows, and
// whether that worked with Remote Desktop off and TermService manual (NP-5's question). It never enables or disables
// anything and starts no session. NP-6 and NP-7 need the real desktop and the Remote Desktop control, so they are probes
// of the DEBUG app (WorkspaceProbe, scripts/pc-control-overlay-spike.ps1), not tests.
using Marrow.App.Port.PcControl.Workspace;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.PcControl.Workspace;
using Microsoft.Win32;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcws-11")]
public sealed class PcWorkspaceNativeTests
{
    [Fact]
    public void NP5_After_the_owner_runs_the_setup_command_nth_reads_the_workspace_ready()
    {
        using var rig = new PcSmokeRig("np5-workspace-ready");
        using var server = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TermService");
        var enabled = new ChildSessionReadiness().Enabled();
        var status = WorkspaceStatus.Of(enabled);
        var start = service?.GetValue("Start") switch
        {
            2 => "automatic",
            3 => "manual",
            4 => "disabled",
            var other => "unknown (" + other + ")",
        };
        rig.Result("child sessions " + (enabled switch { true => "on", false => "off", null => "query failed" })
            + "; Remote Desktop " + (server?.GetValue("fDenyTSConnections") is 0 ? "on" : "off")
            + "; TermService start " + start
            + "; Settings shows " + status.State
            + "; preview released " + WorkspacePreview.Released);
        Assert.Equal(WorkspaceStatus.Ready, status);
    }
}
