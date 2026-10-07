using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :711-837. DEVIATION OD-0090 (locked 2026-10-03,
// E:/Droppy/work/design/jewel-knob-locked/jewel-knob.html): the knob is the Jewel orb, 1.4x the track's height; pressed on
// the knob it lifts where it is, pressed on the track it springs to the pointer; it follows the drag and springs onto the
// nearest stop on release. Frame-by-frame expectations come from the page's own spring() and makeSlider/stepSlider, run
// with node by tests/fixtures/jewel-knob/expected.mjs (a 298 px track with four stops: the app's popover).
[Collection("Headless")]
public sealed class EffortSliderJewelKnobTests(HeadlessSession headless)
{
    private static Panel Knob(EffortSlider slider) => Assert.Single(slider.GetVisualDescendants().OfType<Panel>(), p => p.Width == EffortSlider.ThumbSize && p.Height == EffortSlider.ThumbSize);
    private static Border Fill(EffortSlider slider) => Assert.Single(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "EffortFill");

    private static void AssertFrames(JewelRig rig, IEnumerable<JsonElement> expected)
    {
        foreach (var e in expected)
        {
            rig.Frame();
            var t = e.GetProperty("t").GetDouble();
            var x = e.GetProperty("x").GetDouble();
            Assert.True(Math.Abs(rig.Slider.KnobX - x) < 1e-3, $"x at {t}: page {x}, port {rig.Slider.KnobX}");
            Assert.True(Math.Abs(rig.Slider.KnobScale - e.GetProperty("scale").GetDouble()) < 1e-5, $"scale at {t}: page {e.GetProperty("scale")}, port {rig.Slider.KnobScale}");
            Assert.True(Math.Abs(rig.Slider.LiftAmount - e.GetProperty("lift").GetDouble()) < 1e-4, $"lift at {t}");
            Assert.Equal(e.GetProperty("index").GetInt32(), rig.Index.Value);
            // what is drawn follows: the knob's box, its scale, the fill to its far edge
            Assert.Equal(rig.Slider.KnobX - 24, Canvas.GetLeft(Knob(rig.Slider)), 9);
            Assert.Equal(rig.Slider.KnobScale, Assert.IsType<ScaleTransform>(Knob(rig.Slider).RenderTransform).ScaleX, 9);
            Assert.Equal(rig.Slider.KnobX + 24, Fill(rig.Slider).Width, 9);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:712-713,740-760 (PR-COMPOSER-123's track; its 42 point lens superseded by OD-0090); DEVIATION OD-0090: the knob is 1.4 x the 34 px track, 48 px
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    [Trait("Req", "PR-COMPOSER-126")] // EffortSlider.swift:820-830 as amended by OD-0090 (48 px, a step springs)
    public void The_knob_is_one_point_four_times_the_track_and_the_control_is_as_tall_as_the_knob() => headless.OnUiThread(() =>
    {
        Assert.Equal(34, EffortSlider.TrackHeight);
        Assert.Equal(EffortSlider.ThumbSize, Math.Round(1.4 * EffortSlider.TrackHeight));
        Assert.Equal(48, EffortSlider.ThumbSize);
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        Assert.Equal(48, rig.Slider.Height);
        var knob = Knob(rig.Slider);
        Assert.Equal(RelativePoint.Center, knob.RenderTransformOrigin);
        var track = Assert.Single(rig.Slider.GetVisualDescendants().OfType<Border>(), b => b.Height == 34 && b.Name != "EffortFill");
        Assert.Equal(7, Canvas.GetTop(track)); // the knob stands proud of the track by 7 px above and below
        Assert.Equal(298, track.Width);
    });

    [Theory]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:743-748,756 (a part of PR-COMPOSER-123): stop centres a knob radius in from each end, the fill to the knob's far edge
    [InlineData(0, 24.0)]
    [InlineData(1, 24 + 250.0 / 3)]
    [InlineData(2, 24 + 500.0 / 3)]
    [InlineData(3, 274.0)]
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    public void Each_stop_puts_the_knob_a_radius_in_and_the_fill_to_its_far_edge(int index, double centre) => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: index);
        rig.Frame();
        Assert.Equal(centre, rig.Slider.KnobX, 9);
        Assert.Equal(centre - 24, Canvas.GetLeft(Knob(rig.Slider)), 9);
        Assert.Equal(centre + 24, Fill(rig.Slider).Width, 9);
        if (index == 0) Assert.Equal(0, Canvas.GetLeft(Knob(rig.Slider)), 9);       // flush with the track's start
        if (index == 3) Assert.Equal(298, Fill(rig.Slider).Width, 9);               // and with its end
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:802-818 (PR-COMPOSER-125's springs, 0.22/0.8 to the pointer, 0.32/0.78 back to the stop, as the page's); OD-0090 page trackPress
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void Pressing_the_track_springs_the_knob_to_the_pointer_and_release_springs_it_to_the_nearest_stop() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var run = JewelFixtures.Expected.GetProperty("slider").GetProperty("trackPress").EnumerateArray().ToArray();
        rig.Down(200);
        Assert.Equal(24, rig.Slider.KnobX); // no jump at the press
        Assert.Equal(2, rig.Index.Value);   // the effort follows the pointer at once
        AssertFrames(rig, run.Take(36));
        rig.Up(200);
        Assert.Equal(run[35].GetProperty("x").GetDouble(), rig.Slider.KnobX, 3); // no jump at the release either
        AssertFrames(rig, run.Skip(36));
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9);
        Assert.Equal(1, rig.Slider.KnobScale);
        Assert.Equal(0, rig.Slider.LiftAmount);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:793 scaleEffect 1.06 while held (a part of PR-COMPOSER-123); OD-0090: pressed on the knob it lifts where it is and keeps the grip
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void Pressing_the_knob_lifts_it_where_it_is_and_the_drag_keeps_the_grip() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var run = JewelFixtures.Expected.GetProperty("slider").GetProperty("knobPress").EnumerateArray().ToArray();
        rig.Down(34); // 10 px right of the knob's centre
        AssertFrames(rig, run.Take(30)); // it lifts to 1.06 and stays at 24: no move sideways
        Assert.Equal(24, rig.Slider.KnobX);
        Assert.True(rig.Slider.IsHeld);
        rig.Move(150);
        AssertFrames(rig, run.Skip(30).Take(30)); // the centre follows the pointer less the grip: 140
        Assert.Equal(140, rig.Slider.KnobX, 9);
        rig.Up(150);
        AssertFrames(rig, run.Skip(60));
        Assert.Equal(24 + 250.0 / 3, rig.Slider.KnobX, 9);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:808-813,833-836 (nearestStop, PR-COMPOSER-125, not its springs or haptic); OD-0090: the effort changes as the knob passes the midpoint between stops
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void The_effort_changes_where_the_knob_passes_the_midpoint_between_two_stops() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(24); // on the knob, at its centre
        var midpoint = 24 + 250.0 / 3 / 2; // 65.67
        rig.Move(midpoint - 0.05);
        Assert.Equal(0, rig.Index.Value);
        rig.Move(midpoint + 0.05);
        Assert.Equal(1, rig.Index.Value);
        rig.Move(24 + 250.0 / 3 * 1.5 + 0.05);
        Assert.Equal(2, rig.Index.Value);
        rig.Move(400); // past the end: the knob clamps at the last stop
        Assert.Equal(3, rig.Index.Value);
        rig.Move(-100);
        Assert.Equal(0, rig.Index.Value);
        rig.Up(-100);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:822-830 an adjustable-action step; OD-0090/OD-0076: it springs (page key step: response 0.2, damping 1)
    [Trait("Req", "PR-COMPOSER-126")] // EffortSlider.swift:820-830 as amended by OD-0090 (48 px, a step springs)
    public void An_accessibility_step_springs_the_knob_to_its_stop() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1);
        rig.Frame();
        Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.Slider)).SetValue(2);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(24 + 250.0 / 3, rig.Slider.KnobX, 9); // not a jump
        AssertFrames(rig, JewelFixtures.Expected.GetProperty("slider").GetProperty("key").EnumerateArray());
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:826-831 (the accessibility element of PR-COMPOSER-126, not its label, value or actions): no focus ring; owner, 2026-10-03: no highlight boundary on a click
    public void A_pointer_press_and_drag_show_no_focus_ring() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(24); rig.Move(120); rig.Frame(); rig.Up(120); rig.Frame();
        Assert.False(rig.Slider.Focusable);
        Assert.False(rig.Slider.IsFocused);
        var focused = TopLevel.GetTopLevel(rig.Slider)!.FocusManager?.GetFocusedElement() as Visual;
        Assert.True(focused is null || !rig.Slider.IsVisualAncestorOf(focused) && !focused.IsVisualAncestorOf(rig.Slider), $"focus went to {focused}");
        foreach (var visual in rig.Slider.GetSelfAndVisualDescendants().OfType<Control>())
            Assert.False(visual.IsFocused && visual.FocusAdorner is not null, $"{visual} shows a focus adorner");
        if (AdornerLayer.GetAdornerLayer(rig.Slider) is { } layer) Assert.Empty(layer.Children);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090 (jewel-knob.html recolour/stepFade): a model change crossfades the knob over 0.6 s, smoothstep
    public void A_model_change_crossfades_the_knob_over_six_tenths_of_a_second() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(brand: EffortBrand.Silver, fadeFrom: EffortBrand.Claude);
        var claude = JewelMaterial.For(EffortBrand.Claude);
        var openAI = JewelMaterial.For(EffortBrand.Silver);
        rig.Frame(0);
        Assert.Equal(JewelColor.Hex(claude.Think.ColorC), JewelColor.Hex(rig.Slider.Material.Think.ColorC)); // it starts in the old maker's colours
        foreach (var step in JewelFixtures.Expected.GetProperty("fade").EnumerateArray().Skip(1).Take(3))
        {
            var t = step.GetProperty("t").GetDouble();
            rig.Frame(t - (rig.Frames.Now.TotalSeconds));
            var expected = JewelMaterial.Mix(claude, openAI, step.GetProperty("u").GetDouble());
            Assert.True(JewelColor.Hex(expected.Think.ColorC) == JewelColor.Hex(rig.Slider.Material.Think.ColorC), $"at {t} s");
            Assert.True(JewelColor.Hex(expected.Idle.ColorA) == JewelColor.Hex(rig.Slider.Material.Idle.ColorA), $"at {t} s");
        }
        rig.Frame(0.6 - rig.Frames.Now.TotalSeconds);
        Assert.Same(openAI, rig.Slider.Material);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the card hands the maker shown before a model change to its slider, which fades from it
    public void The_card_of_a_newly_chosen_model_fades_its_knob_from_the_last_makers_colours() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var card = new EffortSliderCard("GPT", Marrow.Core.Models.ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
            new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { }, fadeFrom: EffortBrand.Claude);
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = card };
        window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
        var slider = Assert.Single(card.GetVisualDescendants().OfType<EffortSlider>());
        frames.Advance(TimeSpan.Zero); Dispatcher.UIThread.RunJobs();
        Assert.Same(JewelMaterial.For(EffortBrand.Claude), slider.Material);
        time.Advance(TimeSpan.FromSeconds(0.6)); frames.Advance(TimeSpan.FromSeconds(0.6)); Dispatcher.UIThread.RunJobs();
        Assert.Same(JewelMaterial.For(EffortBrand.Silver), slider.Material);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the liquid and the lightning's tint crossfade with the knob (the page's stepFade: opacity and mixTint)
    public void A_model_change_crossfades_the_liquid_and_the_lightning_too() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 3, fast: true, brand: EffortBrand.Silver, fadeFrom: EffortBrand.Claude);
        rig.Frame(0);
        var effect = Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>());
        var start = effect.TimeAt(SwiftDate.Now);
        Assert.Equal(0, effect.FadeAt(start), 3);
        rig.Frame(0.3);
        var now = effect.TimeAt(SwiftDate.Now);
        Assert.Equal(0.5, effect.FadeAt(now), 3);
        var (gradient, tint) = effect.Colours(LiquidSpec.For(effect.Look), 200, Avalonia.Styling.ThemeVariant.Dark, now);
        var half = StrikeTint.Mix(StrikeTint.For(EffortBrand.Claude), StrikeTint.For(EffortBrand.Silver), effect.FadeAt(now));
        Assert.Equal(half, tint);
        var from = LiquidTrack.GradientFor(LiquidSpec.For(effect.Look with { Brand = EffortBrand.Claude }), gradient.Length, Avalonia.Styling.ThemeVariant.Dark);
        var to = LiquidTrack.GradientFor(LiquidSpec.For(effect.Look), gradient.Length, Avalonia.Styling.ThemeVariant.Dark);
        var u = effect.FadeAt(now);
        Assert.Equal((byte)Math.Round(from[0].Red + (to[0].Red - from[0].Red) * u), gradient[0].Red); // the incoming liquid over the outgoing at the fade's opacity
        rig.Frame(0.31);
        Assert.Equal(1, effect.FadeAt(effect.TimeAt(SwiftDate.Now)));
        Assert.Equal(StrikeTint.For(EffortBrand.Silver), effect.Colours(LiquidSpec.For(effect.Look), 200, Avalonia.Styling.ThemeVariant.Dark, effect.TimeAt(SwiftDate.Now)).Tint);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the gem flows at rest (2.6), its motion integrated frame by frame
    public void At_rest_the_gem_keeps_flowing() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var before = rig.Slider.Orb.Phase;
        for (var i = 0; i < 30; i++) rig.Frame();
        Assert.Equal(before + 0.5 * 2.6, rig.Slider.Orb.Phase, 4);
        Assert.True(rig.Frames.Pending > 0, "the knob keeps asking for frames while it is shown");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: reduce motion stills the gem's flow (light only); the springs still settle
    public void Reduce_motion_stills_the_gem_on_the_slider() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        EnvironmentValues.SetAccessibilityReduceMotion(rig.Window, true);
        rig.Frame();
        var before = rig.Slider.Orb.Phase;
        for (var i = 0; i < 10; i++) rig.Frame();
        Assert.Equal(before, rig.Slider.Orb.Phase);
        rig.Down(200);
        for (var i = 0; i < 60; i++) rig.Frame();
        Assert.Equal(200, rig.Slider.KnobX, 3);
        rig.Up(200);
        // one frame 1 s long: the page's tick() moves the springs by 0.05 s of it (jewel-knob.html:813), as recorded by
        // expected.mjs (slider.longRelease: the same press, hold, release and frame), then they settle onto the stop
        var run = JewelFixtures.Expected.GetProperty("slider").GetProperty("longRelease").EnumerateArray().ToArray();
        rig.Frame(1);
        Assert.True(Math.Abs(run[60].GetProperty("x").GetDouble() - rig.Slider.KnobX) < 1e-3, $"after the long frame: page {run[60].GetProperty("x")}, port {rig.Slider.KnobX}");
        for (var i = 61; i < run.Length; i++) rig.Frame();
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9);
        Assert.Equal(before, rig.Slider.Orb.Phase);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:867-871 paces its loop by strike-engine.js:56-66 frameGate (at 240 Hz every 4th refresh, 60 a second); measured natively, the knob had run at the display's 240
    public void On_a_fast_display_the_knob_steps_sixty_times_a_second() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var steps = 0;
        var last = rig.Slider.Orb.Phase;
        for (var i = 0; i < 240; i++)
        {
            rig.Frame(1.0 / 240);
            if (rig.Slider.Orb.Phase != last) { steps++; last = rig.Slider.Orb.Phase; }
        }
        Assert.InRange(steps, 58, 61); // one second of 240 Hz frames
        Assert.Equal(2.6, last - 0, 0.2); // and the gem still flows at 2.6 a second
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:56-66 frameGate: the refresh measured from the gaps (16 ms here), every = 1, a frame drawn once half a refresh has passed
    public void On_a_sixty_hertz_display_whose_frames_come_16_or_17_ms_apart_the_knob_steps_at_every_frame() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        var steps = 0;
        var last = rig.Slider.Orb.Phase;
        double[] gaps = [0.016, 0.017, 0.017]; // 50 ms over three frames, as a 60 Hz display delivers them
        for (var i = 0; i < 60; i++)
        {
            rig.Frame(gaps[i % gaps.Length]);
            if (rig.Slider.Orb.Phase != last) { steps++; last = rig.Slider.Orb.Phase; }
        }
        Assert.Equal(60, steps); // not about 2 in 3 (40 a second) when a 16 ms frame falls short of 16.667 ms
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: under reduce motion the gem is still, so once the springs settle the knob asks for no more frames
    public void Under_reduce_motion_the_knob_stops_asking_for_frames_once_settled() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(reduceMotion: true);
        rig.Frame();
        rig.Down(200);
        for (var i = 0; i < 10; i++) rig.Frame();
        Assert.True(rig.Frames.Pending > 0, "the springs ask for frames while they move");
        rig.Up(200);
        for (var i = 0; i < 90 && rig.Frames.Pending > 0; i++) rig.Frame();
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html stepSlider: the knob may overshoot its end by 3 px while it springs, no more
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void The_knob_may_overshoot_the_end_by_three_pixels() => headless.OnUiThread(() =>
    {
        // a 600 px track: from the first stop the spring (0.22 s, 0.8) to the far end would overshoot about 4.6 px
        using var rig = JewelFixtures.Rig(width: 600);
        rig.Frame();
        rig.Down(598); // the pointer target clamps to the last stop, 576
        var most = 0.0;
        for (var i = 0; i < 60; i++) { rig.Frame(); most = Math.Max(most, rig.Slider.KnobX); }
        Assert.Equal(576 + 3, most, 9); // held at the room's edge
        Assert.Equal(576, rig.Slider.KnobX, 6);
        rig.Up(598);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090 (jewel-knob.html stepFade: reduce ? 0.2 : FADE): under reduce motion the knob, the liquid and the strike tint fade over 0.2 s
    public void Under_reduce_motion_a_model_change_fades_over_two_tenths_of_a_second() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 3, fast: true, brand: EffortBrand.Silver, fadeFrom: EffortBrand.Claude, reduceMotion: true);
        var claude = JewelMaterial.For(EffortBrand.Claude);
        var openAI = JewelMaterial.For(EffortBrand.Silver);
        rig.Frame(0);
        var effect = Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>());
        rig.Frame(0.1);
        Assert.Equal(JewelColor.Hex(JewelMaterial.Mix(claude, openAI, 0.5).Think.ColorC), JewelColor.Hex(rig.Slider.Material.Think.ColorC));
        var now = effect.TimeAt(SwiftDate.Now);
        Assert.Equal(0.5, effect.FadeAt(now), 3); // not a cut: the liquid and the tint are halfway too
        var (_, tint) = effect.Colours(LiquidSpec.For(effect.Look), 200, Avalonia.Styling.ThemeVariant.Dark, now);
        Assert.Equal(StrikeTint.Mix(StrikeTint.For(EffortBrand.Claude), StrikeTint.For(EffortBrand.Silver), 0.5), tint);
        rig.Frame(0.1);
        Assert.Same(openAI, rig.Slider.Material);
        Assert.Equal(1, effect.FadeAt(effect.TimeAt(SwiftDate.Now)));
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090 / OD-0084: the knob reads the lightning as the page's part()/shape()/orbAim() read it (A2 strikes with the Mix charge)
    public void The_knob_reads_the_strikes_signal_as_the_page_does()
    {
        var s = JewelFixtures.Expected.GetProperty("signal");
        var seed = s.GetProperty("seed").GetInt32();
        var geo = s.GetProperty("geometry");
        // the bolt leaves where the page's slider puts it for the knob at 274 (jewel-knob.html:593 tl.exAt: 272)
        var g = new StrikeGeometry(geo.GetProperty("w").GetDouble(), geo.GetProperty("h").GetDouble(), geo.GetProperty("ex").GetDouble(), geo.GetProperty("radius").GetDouble());
        foreach (var strike in s.GetProperty("strikes").EnumerateArray())
        {
            var k = strike.GetProperty("k").GetInt32();
            Assert.Equal(strike.GetProperty("at").GetDouble(), Strike.StrikeAt(k, seed), 12);
            Assert.Equal(strike.GetProperty("charge").GetDouble(), KnobSignal.ChargeOf(k, seed), 12);
            Assert.Equal(strike.GetProperty("th").GetDouble(), KnobSignal.ExitOf(g, seed, k), 9);
        }
        var signal = new KnobSignal();
        var m = JewelMaterial.For(EffortBrand.Claude);
        foreach (var f in s.GetProperty("frames").EnumerateArray())
        {
            var t = f.GetProperty("t").GetDouble();
            signal.Read(g, seed, t, t);
            void Near(string key, double actual, double tol = 1e-9) => Assert.True(Math.Abs(f.GetProperty(key).GetDouble() - actual) < tol, $"{key} at {t}: page {f.GetProperty(key)}, port {actual}");
            Near("rawCharge", signal.RawCharge); Near("rawFlare", signal.RawFlare);
            Near("charge", signal.Charge); Near("flare", signal.Flare); Near("exit", signal.ExitAngle);
            Assert.Equal(f.GetProperty("phase").GetString(), signal.Phase.ToString().ToLowerInvariant());
            var shared = f.GetProperty("shared").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(shared[0], signal.Charge * m.FxCharge, 9); // the material's share (fx)
            Assert.Equal(shared[1], signal.Flare * m.FxFlare, 9);
        }
    }
    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: KnobSignal.Settled, which the knob's loop reads under reduce motion: the shaped light has reached the raw (the charge's settle over, the flare's hold over and both its stages within 1e-4 of it)
    public void The_signal_is_settled_only_once_the_shaped_light_has_reached_what_the_bolt_shows()
    {
        var s = JewelFixtures.Expected.GetProperty("signal");
        var seed = s.GetProperty("seed").GetInt32();
        var geo = s.GetProperty("geometry");
        var g = new StrikeGeometry(geo.GetProperty("w").GetDouble(), geo.GetProperty("h").GetDouble(), geo.GetProperty("ex").GetDouble(), geo.GetProperty("radius").GetDouble());
        const double frame = 1.0 / 60;
        var signal = new KnobSignal();
        var clock = 0.0;
        void Read(StrikeGeometry? geometry, double t) { signal.Read(geometry, seed, t, clock); clock += frame; }
        Assert.True(signal.Settled); // nothing read yet: nothing to reach

        // a flare only (0.3 s after strike 2: its charge is over, its glow is not), then the lightning gone: the flare is
        // held 0.18 s and then falls on its two stages while the charge, 0 throughout, has nothing to settle
        var glowing = Strike.StrikeAt(2, seed) + 0.3;
        Read(g, glowing);
        Assert.Equal(0, signal.RawCharge);
        Assert.True(signal.RawFlare > 0.1, $"flare {signal.RawFlare}");
        Assert.True(signal.Settled, "the first reading starts the envelope on the raw values");
        for (var i = 0; i < 5; i++) Read(g, glowing);
        Assert.True(signal.Settled, "the same reading again changes nothing");
        Read(null, 0);
        Assert.Equal(0, signal.RawFlare);
        Assert.False(signal.Settled, "the flare is held before it falls: the knob is lit and the raw is not");
        var frames = 1;
        while (!signal.Settled && frames < 600) { Read(null, 0); frames++; }
        Assert.InRange(frames, 12, 599); // the hold alone is 0.18 s
        Assert.Equal(0, signal.Flare);
        Assert.Equal(0, signal.Charge);

        // a charge only (the orb charging before a leader), then the lightning gone: the charge settles over 0.65 s while the
        // flare, 0 throughout, has nothing to hold or fall
        var k = Enumerable.Range(1, 8).First(j => Strike.StrikeAt(j, seed) - Strike.Lead - KnobSignal.ChargeOf(j, seed) / 2 - Strike.StrikeAt(j - 1, seed) > 1.9);
        var charging = Strike.StrikeAt(k, seed) - Strike.Lead - KnobSignal.ChargeOf(k, seed) / 2;
        Read(g, charging);
        Assert.InRange(signal.RawCharge, 0.05, 0.95);
        Assert.Equal(0, signal.RawFlare);
        Assert.True(signal.Settled, "a rising charge is the raw at once");
        Read(null, 0);
        Assert.Equal(0, signal.RawCharge);
        Assert.True(signal.Charge > 0.05, "the charge lets go over 0.65 s, not at once");
        Assert.False(signal.Settled, "the charge is settling; the flare has nothing to do");
        frames = 1;
        while (!signal.Settled && frames < 600) { Read(null, 0); frames++; }
        Assert.InRange(frames, 30, 60); // 0.65 s at 60 frames a second
        Assert.Equal(0, signal.Charge);
    }
    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: KnobSignal.Settled looks at the flare's first stage as well as its second: the second passing through the raw on its way (the light coming back to where the slower stage is) is not the light at rest
    public void The_signal_is_not_settled_while_the_flares_first_stage_is_away_from_the_raw()
    {
        var s = JewelFixtures.Expected.GetProperty("signal");
        var seed = s.GetProperty("seed").GetInt32();
        var geo = s.GetProperty("geometry");
        var g = new StrikeGeometry(geo.GetProperty("w").GetDouble(), geo.GetProperty("h").GetDouble(), geo.GetProperty("ex").GetDouble(), geo.GetProperty("radius").GetDouble());
        const double frame = 1.0 / 60;
        var glowing = Strike.StrikeAt(2, seed) + 0.3; // a flare and no charge
        // a flare held, then falling for <fall> frames (its first stage ahead of its second), then, if asked, the glow back at
        // <gain> of its flare for one frame
        (KnobSignal Signal, double Clock) Build(int fall, double? gain)
        {
            var made = new KnobSignal();
            var clock = 0.0;
            for (var i = 0; i < 6; i++) { made.Read(g, seed, glowing, clock); clock += frame; }
            for (var i = 0; i < fall; i++) { made.Read(null, seed, 0, clock); clock += frame; }
            if (gain is { } x) { made.Read(g, seed, glowing, clock, x); clock += frame; }
            return (made, clock);
        }
        var unit = new KnobSignal();
        unit.Read(g, seed, glowing, 0);
        var full = unit.RawFlare;
        foreach (var fall in new[] { 14, 20, 30, 40 })
        {
            var (falling, _) = Build(fall, null);
            Assert.InRange(falling.Flare, 0.002, full * 0.95);
            var onTheRaw = 0;
            for (var i = 0; i < 4000; i++)
            {
                // around where the slower stage is: the raw comes back and, in the frame it does, the second stage lands within 1e-4 of it
                var gain = falling.Flare / full * (0.9 + 0.2 * i / 4000);
                var (signal, clock) = Build(fall, gain);
                if (Math.Abs(signal.Flare - signal.RawFlare) >= 1e-4 || signal.Charge != signal.RawCharge) continue;
                var settled = signal.Settled;
                var now = signal.Flare;
                var drift = 0.0; // how far the flare strays from here while the same raw is read on
                for (var j = 0; j < 90; j++) { signal.Read(g, seed, glowing, clock, gain); clock += frame; drift = Math.Max(drift, Math.Abs(signal.Flare - now)); }
                if (drift > 3e-4) onTheRaw++;
                Assert.True(!settled || drift <= 3e-4, $"{fall} frames of fall, gain {gain}: settled with the second stage on the raw ({now}), and it then strayed by {drift}");
            }
            Assert.True(onTheRaw > 0, $"{fall} frames of fall: the scan reached a frame where the second stage is on the raw and the first is not");
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0084 with OD-0090's knob: the bolt leaves the 48 px knob where the page's does (jewel-knob.html:593, :335), and the orb charges before the leader
    public void In_fast_mode_the_bolt_leaves_the_knob_and_the_orb_charges_first() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, fast: true);
        rig.Frame();
        var effect = Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>());
        var g = effect.StrikeGeometryFor(effect.Bounds.Size);
        // the page's tl.exAt for the knob at stop 1 (its centre 107.33): 2 px before the centre, on a quarter pixel (105.25)
        Assert.Equal(JewelFixtures.Expected.GetProperty("exit")[1].GetProperty("ex").GetDouble(), g.Knob, 9);
        Assert.Equal(24, g.KnobRadius);
        // step to just before a strike's leader: the knob is charging and the tube has no bolt yet. The strike is the first
        // whose previous one has left the knob's reading (1.9 s after it, jewel-knob.html orbList) by then: the seed comes
        // from how many tracks the process has made, and with some seeds strike 0's tail still lights strike 1's charge
        var seed = effect.StrikeSeed;
        var first=(int)Math.Floor(effect.StrikeFrameTime/2.4)-1;
        var plan=Enumerable.Range(first,30).Select(k=>StrikeMix.Timing(k,seed)).First(p=>p.Charge>0&&p.At-p.Lead-p.Charge/2>effect.StrikeFrameTime&&p.At-p.Lead-p.Charge/2-StrikeMix.Timing(p.K-1,seed).At>1.9);
        var leader = plan.At-plan.Lead-effect.StrikeOffset;
        var charging = leader-plan.Charge/2; // SPEC A-21/B-09
        rig.Frame(charging - effect.TimeAt(SwiftDate.Now));
        Assert.True(rig.Slider.Signal.Phase==KnobPhase.Charge,$"Mix k={plan.K} charging={charging:R} page={effect.Time:R} shared={effect.StrikeFrameTime:R} offset={effect.StrikeOffset:R} raw={rig.Slider.Signal.RawCharge:R} phase={rig.Slider.Signal.Phase}");
        Assert.Equal(KnobPhase.Charge, rig.Slider.Signal.Phase);
        Assert.InRange(rig.Slider.Signal.Charge, 0.05, 0.95);
        Assert.Equal(0, rig.Slider.Signal.RawFlare);
        Assert.True(rig.Slider.Orb.Activation > 0, "the orb lights before the bolt");
        rig.Frame(leader + 0.07 - effect.TimeAt(SwiftDate.Now));
        Assert.Equal(KnobPhase.Strike, rig.Slider.Signal.Phase);
        Assert.Equal(1, rig.Slider.Signal.RawCharge);
    });
}
