// PORT-ONLY: Tactus, "working behind" on the pill's rows (ED-NEW-pcctl-21 item 5, ED-NEW-pcctl-37; DESIGN.md 4.5 "Where the
// target point is not visible ... labelled working behind"). An app whose pointer is aimed where the user cannot see it
// reads "working behind" on its row; aimed at a visible point again, it does not. Fake bands, monitors and pointer windows.
using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class TactusScreenBehindTests(HeadlessSession headless)
{
    private const string Notepad = "exe:c:\\windows\\notepad.exe";

    private static void Pass(PcScreenRig rig, double seconds)
    {
        rig.Pill.Clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static PcPointerAim Aim(bool visible) =>
        new(Notepad, "Notepad", "c:\\windows\\notepad.exe", PcScreenRig.Word, new PcPoint(400, 300), PcPointerAct.Move, visible);

    private static PillRowView Row(PcScreenRig rig) => Assert.Single(rig.Pill.Host.Pill(PcTestMonitors.Main.Id)!.View!.Rows);

    [Fact]
    public void A_pointer_aimed_where_the_user_cannot_see_reads_working_behind_on_its_row_until_it_is_in_view_again() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var pointers = new PointerOverlays(new PcFakePointerSurfaces(), new PcFakePointerScreens(), () => 0, () => false);
        using var rig = new PcScreenRig(pointers: pointers);
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        rig.Screen.Step(new PcAuditEntry(DateTimeOffset.UnixEpoch, PcToolSchemas.Click, "notepad.exe", "Untitled", "Save", "button", "ok", 9));
        Pass(rig, 1);
        Assert.False(Row(rig).Behind);

        rig.Assembly.Pointer.Aim(Aim(visible: false));
        Pass(rig, 0.1);
        Assert.True(pointers.Parked(Notepad));
        Assert.True(Row(rig).Behind);

        rig.Assembly.Pointer.Aim(Aim(visible: true));
        Pass(rig, 0.1);
        Assert.False(pointers.Parked(Notepad));
        Assert.False(Row(rig).Behind);
    });
}
