using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus's separate workspace (unit P18, ED-NEW-pcws-3, ED-NEW-pcws-4; DESIGN.md 2.13). pc_launch_app's place
// against "Use for new apps", the preview gate and the readiness; the apps remembered as desk-only; the workspace's ids.
[Trait("Decision", "ED-NEW-pcws-3")]
public sealed class WorkspaceRouterTests
{
    private const string Notepad = "exe:c:\\apps\\notepad.exe";

    private static readonly WorkspaceStatus Ready = WorkspaceStatus.Ready;
    private static readonly WorkspaceStatus Running = new(WorkspaceState.Running, 2);

    [Fact]
    public void Desk_is_always_the_desk()
    {
        var router = new WorkspaceRouter();
        Assert.Equal(WorkspaceRoute.Desk, router.Route("desk", "always", true, Running, false, Notepad));
        Assert.Equal(WorkspaceRoute.Desk, router.Route("desk", "never", false, WorkspaceStatus.NotSetUp, true, Notepad));
    }

    [Fact]
    public void An_explicit_workspace_is_refused_with_its_reason_when_it_cannot_be_used()
    {
        var router = new WorkspaceRouter();
        Assert.Equal(WorkspaceRouter.NotReleased, router.Route("workspace", "auto", false, Running, false, Notepad).Refusal);
        Assert.Equal(WorkspaceRouter.NotSetUp, router.Route("workspace", "auto", true, WorkspaceStatus.NotSetUp, false, Notepad).Refusal);
        Assert.Equal(WorkspaceRouter.UserSaidNever, router.Route("workspace", "never", true, Ready, false, Notepad).Refusal);
        router.OpenedOnDesk(Notepad);
        Assert.Equal(WorkspaceRouter.OneCopy, router.Route("workspace", "auto", true, Ready, false, Notepad).Refusal);
        Assert.Equal(WorkspaceRoute.Workspace, router.Route("workspace", "auto", true, Running, true, "exe:c:\\apps\\paint.exe"));
    }

    [Fact]
    public void Auto_picks_the_workspace_only_when_it_is_usable_and_the_app_is_not_on_the_desk()
    {
        var router = new WorkspaceRouter();
        Assert.Equal(WorkspaceRoute.Workspace, router.Route("auto", "auto", true, Ready, false, Notepad));
        Assert.Equal(WorkspaceRoute.Desk, router.Route("auto", "auto", true, Ready, true, Notepad));
        Assert.Equal(WorkspaceRoute.Workspace, router.Route("auto", "always", true, Ready, true, Notepad));
        Assert.Equal(WorkspaceRoute.Desk, router.Route("auto", "never", true, Ready, false, Notepad));
        Assert.Equal(WorkspaceRoute.Desk, router.Route("auto", "always", false, Ready, false, Notepad));
        Assert.Equal(WorkspaceRoute.Desk, router.Route("auto", "always", true, WorkspaceStatus.NotSetUp, false, Notepad));
        router.OpenedOnDesk(Notepad);
        Assert.Equal(WorkspaceRoute.Desk, router.Route("auto", "always", true, Ready, false, Notepad));
    }

    [Fact]
    public void Desk_only_apps_are_capped_and_the_oldest_goes_first()
    {
        var router = new WorkspaceRouter();
        for (var i = 0; i < WorkspaceRouter.MaxDeskOnly + 10; i++) router.OpenedOnDesk("exe:app" + i);
        router.OpenedOnDesk("exe:app20");
        Assert.Equal(WorkspaceRouter.MaxDeskOnly, router.DeskOnlyCount);
        Assert.False(router.DeskOnly("exe:app0"));
        Assert.False(router.DeskOnly("exe:app9"));
        Assert.True(router.DeskOnly("exe:app10"));
        Assert.True(router.DeskOnly("exe:app265"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-4")]
    public void Workspace_ids_carry_bit_62_and_looks_start_above_two_to_the_forty()
    {
        nint handle = 0x1a2b;
        var tagged = WorkspaceRefs.Tag(handle);
        Assert.True(WorkspaceRefs.IsWorkspace(tagged));
        Assert.False(WorkspaceRefs.IsWorkspace(handle));
        Assert.Equal(handle, WorkspaceRefs.Untag(tagged));
        Assert.Equal(tagged, WorkspaceRefs.Tag(tagged));
        Assert.Equal("w:4000000000001a2b", WorkspaceRefs.Format(handle));
        Assert.True(PcWindowRef.TryParse("w:4000000000001a2b", out var parsed) && parsed == tagged);
        Assert.False(WorkspaceRefs.IsWorkspaceObservation("o:12"));
        Assert.True(WorkspaceRefs.IsWorkspaceObservation(PcObsId.Format(WorkspaceRefs.ObservationBase + 1)));
        Assert.False(WorkspaceRefs.IsWorkspaceObservation(null));
    }

    [Fact]
    public void Status_text_and_readiness()
    {
        Assert.Equal("Not set up", WorkspaceStatus.NotSetUp.Text);
        Assert.Equal("Ready", WorkspaceStatus.Ready.Text);
        Assert.Equal("Running, 2 apps", Running.Text);
        Assert.Equal("Running, 1 app", new WorkspaceStatus(WorkspaceState.Running, 1).Text);
        Assert.Equal(WorkspaceStatus.Ready, WorkspaceStatus.Of(true));
        Assert.Equal(WorkspaceStatus.NotSetUp, WorkspaceStatus.Of(false));
        Assert.Equal(WorkspaceStatus.NotSetUp, WorkspaceStatus.Of(null));
        Assert.False(WorkspaceStatus.NotSetUp.Usable);
        Assert.True(Running.Usable);
        Assert.False(WorkspacePreview.Released);
    }
}
