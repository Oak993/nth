using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the auto-dodge loop (OD-0145, ED-NEW-pcctl-36; DESIGN.md 10.3 "Auto-dodge", "Dodge rules",
// 10.6). On fakes: a window's caption buttons under the resting pill move it sideways by the least amount that clears them,
// as a travel, once the read is in; it goes back 300 ms after they go; nothing moves while the pointer is on the pill, and
// the dodge follows once it leaves; a window being dragged reads once, when it pauses; a reorder puts the band on top again;
// a showing top taskbar moves it down; a new resting place (another anchor) reads again; the bands never count; a foreground
// change makes the full-screen watch read again; after Dispose no event reads.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-36")]
public sealed class TactusDodgeTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PcDodgeRig Make()
    {
        var rig = PcDodgeRig.Create();
        rig.Host.Host.Update(new PillFacts { Apps = [Excel] });
        Pass(rig.Host, 2);
        return rig;
    }

    // Time passes in 50 ms steps, the UI thread running what each step posted.
    private static void Pass(PcPillHostRig rig, double seconds)
    {
        for (var passed = 0.0; passed < seconds - 1e-9; passed += 0.05)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(0.05));
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Raise(PcDodgeRig rig, DodgeEventKind kind)
    {
        rig.Events.Raise(kind);
        Dispatcher.UIThread.RunJobs();
    }

    private static ControlPill Pill(PcDodgeRig rig) => rig.Host.Host.Pill(PcTestMonitors.Main.Id)!;

    private static PillRect Rest(PcDodgeRig rig) => rig.Host.Host.DodgeArea()!.Value.Place;

    // A maximized window whose minimize, maximize and close buttons (46 x 32 each) start 60 DIP left of the pill's right edge.
    private static void ButtonsUnderTheRightEnd(PcDodgeRig rig)
    {
        var rest = Rest(rig);
        var left = (int)Math.Round(rest.Right - 60);
        rig.Windows.ZOrder.Add((0x500, new PcRect(0, 0, 1920, 1032), [new PcRect(left, 0, 46, 32), new PcRect(left + 46, 0, 46, 32), new PcRect(left + 92, 0, 46, 32)]));
    }

    [Fact]
    public void Caption_buttons_under_the_resting_pill_move_it_sideways_by_a_travel_and_it_returns_300_ms_after_they_go() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var pill = Pill(rig);
        var home = pill.Motion.Final.Anchor;
        var rest = Rest(rig);
        ButtonsUnderTheRightEnd(rig);

        Raise(rig, DodgeEventKind.Foreground);
        // the least sideways move that clears the buttons by 12 DIP: the pill's right edge to 12 DIP left of them
        var left = Math.Round(rest.Right - 60);
        var dx = -(rest.Right - (left - PillDodgePlanner.Clearance));
        Assert.Equal(new PillDodge(dx, 0, "caption buttons"), rig.Dodge.Current);
        Assert.Equal(home.X + dx, pill.Motion.Final.Anchor.X, 6);
        Assert.Equal(home.Y, pill.Motion.Final.Anchor.Y, 6);
        // a travel from where it was, not a jump
        Assert.True(pill.Motion.Travels);
        Assert.Equal(home.X, pill.Motion.At(pill.Now).Anchor.X, 6);
        Assert.Equal(1, rig.Invalidated());
        // Tactus's own bands are never a reason
        Assert.NotEmpty(rig.Host.Overlays.Handles);
        Assert.Subset(rig.Windows.Ignored.ToHashSet(), rig.Host.Overlays.Handles.ToHashSet());
        Pass(rig.Host, 1);
        Assert.Equal(home.X + dx, pill.Motion.At(pill.Now).Anchor.X, 1);

        // the window goes: a moved or closed window reads once it pauses, the return waits 300 ms clear (tried every 100 ms)
        rig.Windows.ZOrder.Clear();
        Raise(rig, DodgeEventKind.Location);
        Pass(rig.Host, 0.1);
        Assert.Equal(PillDodge.None, rig.Dodge.Planned);
        Pass(rig.Host, 0.15);
        Assert.Equal(home.X + dx, pill.Motion.Final.Anchor.X, 6);
        Pass(rig.Host, 0.3);
        Assert.Equal(PillDodge.None, rig.Dodge.Current);
        Assert.Equal(home.X, pill.Motion.Final.Anchor.X, 6);
    });

    [Fact]
    public void Nothing_moves_while_the_pointer_is_on_the_pill_and_the_dodge_follows_once_it_leaves() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var pill = Pill(rig);
        var home = pill.Motion.Final.Anchor;
        var drawn = pill.Motion.At(pill.Now).Drawn;
        PillVector? pointer = new PillVector(drawn.X + (drawn.Width / 2), drawn.Y + (drawn.Height / 2));
        rig.Host.Host.PointerAt = _ => pointer;
        ButtonsUnderTheRightEnd(rig);

        Raise(rig, DodgeEventKind.Foreground);
        Assert.True(rig.Dodge.Planned.Active);
        Assert.Equal(PillDodge.None, rig.Dodge.Current);
        Pass(rig.Host, 1);
        Assert.Equal(home.X, pill.Motion.Final.Anchor.X, 6);

        pointer = null;
        Pass(rig.Host, 0.15);
        Assert.Equal(rig.Dodge.Planned, rig.Dodge.Current);
        Assert.Equal(home.X + rig.Dodge.Planned.Dx, pill.Motion.Final.Anchor.X, 6);
    });

    [Fact]
    public void A_dragged_window_reads_once_it_pauses_and_a_reorder_puts_the_band_on_top_again() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var before = rig.Windows.Reads;
        for (var i = 0; i < 5; i++)
        {
            Raise(rig, DodgeEventKind.Location);
            Pass(rig.Host, 0.05);
        }
        Assert.Equal(before, rig.Windows.Reads);
        Pass(rig.Host, 0.3);
        Assert.Equal(before + 1, rig.Windows.Reads);

        var band = rig.Host.Bands.Opened.Last(o => o.Monitor.Id == PcTestMonitors.Main.Id).Band;
        var reasserts = band.Reasserts;
        Raise(rig, DodgeEventKind.Reorder);
        Assert.Equal(reasserts + 1, band.Reasserts);
        Assert.Equal(before + 2, rig.Windows.Reads);
    });

    [Fact]
    public void A_showing_top_taskbar_moves_it_down_and_a_new_resting_place_reads_again() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var pill = Pill(rig);
        var home = pill.Motion.Final.Anchor;
        var rest = Rest(rig);
        rig.Windows.Taskbar = 40;
        Raise(rig, DodgeEventKind.Display);
        var dy = 40 + PillDodgePlanner.BelowTaskbar - rest.Y;
        Assert.True(dy > 0);
        Assert.Equal(new PillDodge(0, dy, "taskbar"), rig.Dodge.Current);
        Assert.Equal(home.Y + dy, pill.Motion.Final.Anchor.Y, 6);
        Assert.Equal(home.X, pill.Motion.Final.Anchor.X, 6);

        // another anchor is another resting place: the next look reads under it
        var reads = rig.Windows.Reads;
        rig.Host.Host.Anchor = PillAnchor.Right;
        rig.Host.Host.Refresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(reads + 1, rig.Windows.Reads);
        // the same place again reads nothing more
        rig.Host.Host.Refresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(reads + 1, rig.Windows.Reads);
    });

    [Fact]
    public void After_Dispose_no_event_reads_and_nothing_moves() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var pill = Pill(rig);
        var home = pill.Motion.Final.Anchor;
        rig.Dodge.Dispose();
        var reads = rig.Windows.Reads;
        ButtonsUnderTheRightEnd(rig);
        Raise(rig, DodgeEventKind.Foreground);
        Pass(rig.Host, 0.5);
        Assert.Equal(reads, rig.Windows.Reads);
        Assert.Equal(home, pill.Motion.Final.Anchor);
        Assert.Equal(0, rig.Invalidated());
    });
}
