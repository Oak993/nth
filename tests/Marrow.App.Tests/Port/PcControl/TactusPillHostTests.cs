// PORT-ONLY: the Tactus pill on the bands (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-24; DESIGN.md 4.5, 10.3 "Which monitor",
// "Drag", "Auto-dodge", 10.6 "One overlay per monitor", "Hit testing", "Glass, motion and one clock"): facts in, the pill
// and its mirror in the chosen monitor's band, the layers each window draws while moving and at rest, the shape handed to
// the hit test when the motion settles, the glass under the window that draws the body (the dense panel without it), a
// leave that closes the band 2 s later, a click that opens the rows, a release that picks the anchor or another monitor,
// a dodge. Fake bands, fake Win32 calls, fake glass, a manual clock; the pill controls run headless.
using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class TactusPillHostTests(HeadlessSession headless)
{
    private static readonly PillFacts Working = new() { Apps = [new PillApp("excel", "Excel", "Filling row 12 of 40")] };

    private static PcPillHostRig Make(OverlayHitTestKind kind = OverlayHitTestKind.Region, bool glass = true)
    {
        var clock = new ManualTimeProvider();
        var bands = new PcFakePillBands();
        var native = new PcFakeBandNative();
        var overlays = new PillOverlays(bands, native, kind, () => clock.GetTimestamp() / (double)clock.TimestampFrequency,
            makeHitTest: k => k == OverlayHitTestKind.StyleFlip ? new StyleFlipHitTest(native, runThread: false) : OverlayHitTests.Create(k, native));
        var map = new MonitorMap(new PcFakeOverlayMonitors { Now = [PcTestMonitors.Main, PcTestMonitors.Right] });
        map.Refresh();
        var surfaces = new PcFakePillSurfaces(shadowWindow: kind != OverlayHitTestKind.StyleFlip, glass: glass);
        var screens = new PillScreens();
        var host = new TactusPillHost(overlays, surfaces, map, screens, clock: clock, driver: PillDriverKind.RenderThread);
        return new PcPillHostRig(host, overlays, bands, native, surfaces, screens, clock);
    }

    private static void Pass(PcPillHostRig rig, double seconds)
    {
        rig.Clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static double Now(PcPillHostRig rig) => rig.Clock.GetTimestamp() / (double)rig.Clock.TimestampFrequency;

    private static void Settle(PcPillHostRig rig, ControlPill pill) => Pass(rig, Math.Max(0, pill.Motion.DoneAt() - Now(rig)) + 0.1);

    [Theory]
    [InlineData(OverlayHitTestKind.Region, false, PillPaint.Body | PillPaint.Content, PillPaint.Shadow)]
    [InlineData(OverlayHitTestKind.Region, true, PillPaint.None, PillPaint.All)]
    [InlineData(OverlayHitTestKind.Pixel, false, PillPaint.Body | PillPaint.Content, PillPaint.Shadow)]
    [InlineData(OverlayHitTestKind.Pixel, true, PillPaint.Body | PillPaint.Content, PillPaint.Shadow)]
    [InlineData(OverlayHitTestKind.StyleFlip, false, PillPaint.All, PillPaint.None)]
    [InlineData(OverlayHitTestKind.StyleFlip, true, PillPaint.All, PillPaint.None)]
    public void Each_window_draws_its_layers(OverlayHitTestKind kind, bool moving, PillPaint input, PillPaint shadow) =>
        Assert.Equal((input, shadow), PillPaintPlan.For(kind, moving));

    [Fact]
    public void Facts_show_the_pill_and_its_mirror_in_the_main_band_and_hand_the_input_window_back_when_it_settles() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);

        var opened = Assert.Single(rig.Bands.Opened);
        Assert.Equal(PcTestMonitors.Main.Id, opened.Monitor.Id);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        var mirror = rig.Host.Mirror(PcTestMonitors.Main.Id)!;
        Assert.False(pill.Follower);
        Assert.True(mirror.Follower);
        var surface = rig.Surfaces.Surface(PcTestMonitors.Main.Id);
        Assert.Contains(pill, surface.Input.Children);
        Assert.Contains(mirror, surface.Shadow!.Children);
        Assert.Equal("Using Excel", pill.View!.Title);
        Assert.Equal("Using Excel", mirror.View!.Title);
        Assert.Equal(PcTestMonitors.Main.Id, rig.Host.MonitorId);
        // it grows out of the centre anchor, 8 DIP under the work area's top
        Assert.Equal(PillPresence.Arriving, pill.Motion.Presence);
        Assert.Equal(new PillVector(960, 8), pill.Motion.Final.Anchor);
        Assert.Equal(0.5, pill.Motion.Factor, 9);
        Assert.Same(pill.Motion, mirror.Motion);
        // moving: the shadow window draws everything, the input window nothing (its region is empty)
        Assert.Equal(PillPaint.None, pill.Paint);
        Assert.Equal(PillPaint.All, mirror.Paint);
        Assert.Null(rig.Native.Regions.GetValueOrDefault(opened.Band.Input));

        Settle(rig, pill);
        Assert.Equal(PillPaint.Body | PillPaint.Content, pill.Paint);
        Assert.Equal(PillPaint.Shadow, mirror.Paint);
        Assert.NotNull(rig.Native.Regions[opened.Band.Input]);
        Assert.Equal(PillPresence.Shown, pill.Motion.Presence);
    });

    [Fact]
    public void The_glass_follows_the_window_that_draws_the_body_and_without_it_the_body_is_dense() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        var inputGlass = rig.Surfaces.InputGlass(PcTestMonitors.Main.Id);
        var shadowGlass = rig.Surfaces.ShadowGlass(PcTestMonitors.Main.Id)!;
        // moving: the input window does not draw the body, so its blur is off
        Assert.True(inputGlass.Hides > 0);
        Assert.Equal(PillPaint.None, pill.Paint);

        Settle(rig, pill);
        var (drawn, radius) = inputGlass.Shown[^1];
        Assert.Equal(pill.Motion.Final.Width, drawn.Width, 6);
        Assert.Equal(960 - (pill.Motion.Final.Width / 2), drawn.X, 6);
        Assert.Equal(8, drawn.Y, 6);
        Assert.Equal(PillLayout.Radius, radius, 6);
        Assert.True(shadowGlass.Hides > 0);
        Assert.True(pill.Glass);

        var dense = Make(glass: false);
        dense.Host.Update(Working);
        var densePill = dense.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(dense, densePill);
        Assert.False(densePill.Glass);
        Assert.False(dense.Host.Mirror(PcTestMonitors.Main.Id)!.Glass);
    });

    [Fact]
    public void A_band_on_a_scaled_monitor_places_the_pill_in_its_own_DIP() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Screens.ShowOn = PillShowOn.Chosen;
        rig.Screens.ChosenId = PcTestMonitors.Right.Id;
        rig.Host.Update(Working);

        var pill = rig.Host.Pill(PcTestMonitors.Right.Id)!;
        // the right monitor's work area starts 72 px (48 DIP at 150 %) down: the band's origin, and the anchor 8 DIP below it
        Assert.Equal(new PillVector(0, 48), pill.Origin);
        Assert.Equal(new PillVector(960, 56), pill.Motion.Final.Anchor);
        Assert.Null(rig.Host.Pill(PcTestMonitors.Main.Id));
    });

    [Fact]
    public void Idle_facts_make_it_leave_and_its_band_close_two_seconds_later() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(rig, pill);
        var surface = rig.Surfaces.Surface(PcTestMonitors.Main.Id);

        rig.Host.Update(new PillFacts());
        Assert.Equal(PillPresence.Leaving, pill.Motion.Presence);
        Assert.Null(rig.Host.MonitorId);
        Settle(rig, pill);
        Assert.False(rig.Bands.Opened[0].Band.Closed);
        Pass(rig, PillOverlays.CloseAfterSeconds + 0.2);

        Assert.True(rig.Bands.Opened[0].Band.Closed);
        Assert.Null(rig.Host.Pill(PcTestMonitors.Main.Id));
        Assert.Empty(surface.Input.Children);
        Assert.Empty(surface.Shadow!.Children);
    });

    [Fact]
    public void Coming_back_before_the_band_closes_keeps_it_and_arrives_again() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(rig, pill);
        rig.Host.Update(new PillFacts());
        Settle(rig, pill);

        rig.Host.Update(Working);
        Assert.Same(pill, rig.Host.Pill(PcTestMonitors.Main.Id));
        Assert.Equal(PillPresence.Arriving, pill.Motion.Presence);
        Pass(rig, PillOverlays.CloseAfterSeconds + 0.5);
        Assert.False(rig.Bands.Opened[0].Band.Closed);
        Assert.Single(rig.Bands.Opened);
    });

    [Fact]
    public void A_click_without_asks_opens_the_rows_and_one_with_asks_goes_to_the_engine() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var commands = new List<PillCommand>();
        rig.Host.Command += commands.Add;
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;

        rig.Host.Pressed(new PillCommand(PillCommandKind.Click));
        Assert.True(pill.View!.RowsShown);
        Assert.Empty(commands);
        rig.Host.Pressed(new PillCommand(PillCommandKind.Click));
        Assert.False(pill.View!.RowsShown);

        rig.Host.Pressed(new PillCommand(PillCommandKind.PauseAll));
        rig.Host.Update(Working with { Asks = [new PillAsk("a", PillAskKind.Approval, "Outlook", "Report", "Claude")] });
        rig.Host.Pressed(new PillCommand(PillCommandKind.Click));
        Assert.Equal(new[] { new PillCommand(PillCommandKind.PauseAll), new PillCommand(PillCommandKind.Click) }, commands);
    });

    [Fact]
    public void A_new_ask_puts_the_band_on_top_again() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);
        var band = rig.Bands.Opened[0].Band;
        var before = band.Reasserts;
        var ask = new PillAsk("a", PillAskKind.Approval, "Outlook", "Report", "Claude");

        rig.Host.Update(Working with { Asks = [ask] });
        Assert.Equal(before + 1, band.Reasserts);
        // the same ask again is not new
        rig.Host.Update(Working with { Asks = [ask] });
        Assert.Equal(before + 1, band.Reasserts);
        // ticking does not put it on top again
        Pass(rig, 1);
        Assert.Equal(before + 1, band.Reasserts);
    });

    [Fact]
    public void A_release_near_the_left_edge_rests_at_the_left_anchor() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var chosen = new List<PillAnchor>();
        rig.Host.AnchorChosen += chosen.Add;
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(rig, pill);
        pill.Place(new PillVector(150, 8), 0.5);

        rig.Host.Released(rig.Host.PairOn(PcTestMonitors.Main.Id)!, new PillVector(150, 30), new PillVector(150, 8), PillVector.Zero);

        Assert.Equal(new[] { PillAnchor.Left }, chosen);
        Assert.Equal(PillAnchor.Left, rig.Host.Anchor);
        Assert.Equal(new PillVector(PillAnchors.SideGap, PillAnchors.TopGap), pill.Motion.Final.Anchor);
        Assert.Equal(0, pill.Motion.Factor, 9);
    });

    [Fact]
    public void A_release_on_another_monitor_leaves_here_and_arrives_there() => headless.OnUiThread(() =>
    {
        var rig = Make();
        var dropped = new List<string>();
        rig.Host.MonitorDropped += dropped.Add;
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(rig, pill);

        // 2,000 DIP right of the main monitor's origin is on the right monitor
        rig.Host.Released(rig.Host.PairOn(PcTestMonitors.Main.Id)!, new PillVector(2000, 100), new PillVector(2000, 8), PillVector.Zero);

        Assert.Equal(new[] { PcTestMonitors.Right.Id }, dropped);
        Assert.Equal(PillShowOn.Chosen, rig.Screens.ShowOn);
        Assert.Equal(PillPresence.Leaving, pill.Motion.Presence);
        var there = rig.Host.Pill(PcTestMonitors.Right.Id)!;
        Assert.Equal(PillPresence.Arriving, there.Motion.Presence);
        Assert.Equal(PcTestMonitors.Right.Id, rig.Host.MonitorId);
    });

    [Fact]
    public void A_dodge_moves_the_resting_place_and_its_end_brings_it_back() => headless.OnUiThread(() =>
    {
        var rig = Make();
        rig.Host.Update(Working);
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Settle(rig, pill);

        rig.Host.Dodge(new PillDodge(0, 40, "caption buttons"));
        Assert.Equal(new PillVector(960, 48), pill.Motion.Final.Anchor);
        Assert.False(pill.Motion.AtRest);
        Settle(rig, pill);
        rig.Host.Dodge(PillDodge.None);
        Assert.Equal(new PillVector(960, 8), pill.Motion.Final.Anchor);
    });

    [Fact]
    public void With_one_window_the_pill_draws_everything_and_has_no_mirror() => headless.OnUiThread(() =>
    {
        var rig = Make(OverlayHitTestKind.StyleFlip);
        rig.Host.ReduceMotion = true;
        rig.Host.Update(Working);

        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        Assert.Null(rig.Host.Mirror(PcTestMonitors.Main.Id));
        Assert.Equal(PillPaint.All, pill.Paint);
        Assert.True(pill.ReduceMotion);
        Settle(rig, pill);
        Assert.Equal(PillPaint.All, pill.Paint);
    });
}
