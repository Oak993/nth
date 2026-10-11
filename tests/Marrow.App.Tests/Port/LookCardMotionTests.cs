using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088, OD-0076 (ED-250): every transition of the Look card on the manual frame clock, frame by frame at 60 Hz from the
// action, against SPEC 6.10's table (sampled from the locked board in headless Chrome) within 0.02 of the travel, against
// the CSS curve the spec names (solved independently, CssCurve) within 0.002, and with no single-frame jump larger than
// the curve's own. Reduce Motion puts every value at its end at once.
[Collection("Headless")]
public sealed class LookCardMotionTests(HeadlessSession headless) : LookCardFixture(headless)
{
    // SPEC 6.10, one value per 1/60 s frame from the action (frame 0 is the action's own).
    private static readonly double[] RingSlide = [0, .137, .308, .527, .720, .821, .877, .913, .937, .955, .968, .978, .985, .990, .994, .997, .999, 1];
    private static readonly double[] FoldHeight = [0, 1.38, 4.05, 7.94, 12.45, 16.84, 20.72, 23.98, 26.67, 28.88, 30.67, 32.13, 33.28, 34.20, 34.89, 35.39, 35.73, 35.94];
    private static readonly double[] FoldOpening = [0, 0, 0, 0, 0, 0, 0, 0, .010, .124, .331, .540, .699, .812, .890, .943, .977, .995];
    private static readonly double[] FoldShutting = [1, .686, .294, .099, .017, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly double[] LineY = [-6, -5.77, -5.32, -4.68, -3.92, -3.19, -2.55, -2.00, -1.55, -1.19, -.89, -.64, -.45, -.30, -.18, -.10, -.04, -.01];
    private static readonly double[] MoreOut = [1, .78, .42, .20, .08, .02, 0, 0, 0, 0, 0, 0, 0];
    private static readonly double[] LessIn = [0, 0, 0, 0, 0, .01, .22, .52, .74, .87, .95, .99, 1];
    private static readonly double[] ChevronTurn = [0, 24.6, 55.4, 94.9, 129.7, 147.8, 157.9, 164.3, 168.7, 171.8, 174.2, 175.9, 177.3, 178.2, 178.9, 180];
    private static readonly double[] RingOutOpacity = [1, .916, .738, .532, .362, .237, .148, .085, .043, .017, .003, 0];
    private static readonly double[] RingOutScale = [1, .965, .918, .864, .836, .823, .815, .810, .806, .804, .802, .801, .801, .800];
    private static readonly double[] RingInOpacity = [0, .084, .262, .468, .638, .763, .852, .915, .957, .984, .997, 1];
    private static readonly double[] RingInScale = [.8, .835, .882, .936, .964, .978, .985, .990, .994, .996, .998, .999, .999, 1];
    private static readonly double[] MoreRingOpacity = [0, .038, .113, .221, .346, .468, .576, .666, .741, .802, .852, .893, .925, .950, .969, .984, .993, .998];
    private static readonly double[] MoreRingScale = [.9, .914, .931, .953, .972, .982, .988, .991, .994, .996, .997, .998, .999, .999, .999, 1];
    private static readonly double[] MoreDotWidth = [0, .81, 1.84, 3.16, 4.31, 4.92, 5.25, 5.47, 5.61, 5.72, 5.80, 5.86, 5.91, 5.94, 5.95, 5.97, 5.98, 5.98];
    private static readonly double[] MoreDotScale = [.4, .505, .647, .809, .893, .932, .956, .971, .981, .988, .993, .996, .998, .999, 1];
    private static readonly double[] ChevronWidth = [7, 6.03, 4.84, 3.30, 1.95, 1.25, .84, .61, .44, .31, .22, .16, .09, .06, .03, .02, 0];
    private static readonly double[] Crossfade32 = [0, .035, .101, .198, .314, .431, .537, .629, .706, .769, .822, .866, .901, .930, .953, .970, .984, .993];
    private static readonly double[] MenuOpening = [0, .071, .221, .408, .576, .706, .802, .874, .925, .961, .984, .996, 1];
    private static readonly double[] MenuScaleOpening = [.96, .973, .983, .990, .994, .996, .998, .999, .999, 1];
    private static readonly double[] MenuClosing = [1, .887, .654, .424, .259, .148, .075, .031, .007, 0];
    private static readonly double[] MenuScaleClosing = [1, .996, .986, .977, .970, .966, .963, .961, .960];
    private static readonly double[] HoverScale = [1, 1.020, 1.056, 1.085, 1.102, 1.113, 1.118, 1.12];
    private static readonly double[] PressScale = [1.12, 1.038, .936, .886, .864, .860];
    private static readonly double[] TextIn = [0, .071, .221, .408, .576, .706, .802, .874, .925, .961, .984, .996, 1];

    private static void Run(AppModelHarness h, HostWindow w, int frames, params LookTrack[] tracks)
    {
        foreach (var track in tracks) track.Sample(0);
        for (var k = 1; k <= frames; k++)
        {
            Frame(h);
            foreach (var track in tracks) track.Sample(k);
        }
    }

    private static double Largest(IReadOnlyList<double> values) => values.Zip(values.Skip(1), (a, b) => Math.Abs(b - a)).Max();

    // The board's curves: progress at t ms of a transition of d ms after a delay.
    private static Func<double, double> Ez(double from, double to, double d, double delay = 0) => t => from + (to - from) * CssCurve.At(CssCurve.Ez, t, d, delay);
    private static Func<double, double> Ease(double from, double to, double d, double delay = 0) => t => from + (to - from) * CssCurve.At(CssCurve.Ease, t, d, delay);
    private static Func<double, double> PopIn(double from, double to, double d) => t => from + (to - from) * CssCurve.At(CssCurve.PopIn, t, d);

    private static void Release(HostWindow w, Avalonia.Controls.Control control)
    {
        var at = Centre(control, w);
        w.MouseMove(at);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_pick_on_the_same_line_slides_its_ring_over_point_3_seconds_and_the_accent_eases_over_point_32() => Card((card, _, h, w) =>
    {
        Hover(w, card.Classic.Swatches[8]);
        Settle(h, w);
        Release(w, card.Classic.Swatches[8]); // Windows accent -> Teal (scenario 01)
        var ring = new LookTrack("ring slide", () => card.Classic.Ring.X.Value / 208, RingSlide, Ez(0, 1, 300), 1);
        ring.Sample(0);
        for (var k = 1; k <= 21; k++)
        {
            Frame(h);
            ring.Sample(k);
            // The accent (--ac, --pac): #4B9FFC to #40C8E0, .32 s ease, each channel within 1 of the curve's mix.
            var p = CssCurve.At(CssCurve.Ease, k * FrameMs, 320);
            var expected = CssCurve.Mix((0x4B, 0x9F, 0xFC, 1), (0x40, 0xC8, 0xE0, 1), p);
            var drawn = card.Accent.Value;
            Assert.True(Math.Abs(drawn.R - expected.R) <= 1 && Math.Abs(drawn.G - expected.G) <= 1 && Math.Abs(drawn.B - expected.B) <= 1, $"accent frame {k}: {drawn}, p {p:F3}");
            if (k < Crossfade32.Length) Assert.True(Math.Abs(p - Crossfade32[k]) <= 0.02, $"the curve itself at frame {k}");
        }
        Assert.Equal(208, card.Classic.Ring.X.Value);
        Assert.True(Largest(ring.Seen) <= 0.22, $"largest ring step {Largest(ring.Seen):F3}"); // SPEC 6.10: 0.219 at frames 2-3
        Assert.False(card.Motion.IsMoving);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void More_folds_the_signature_line_open_and_less_shuts_it_on_the_board_s_curves() => Card((card, _, h, w) =>
    {
        Release(w, card.More); // scenario 02
        var height = new LookTrack("fold height", () => card.Fold.FoldHeight.Value, FoldHeight, Ease(0, 36, 300), 36);
        var opacity = new LookTrack("fold opacity", () => card.Fold.LineOpacity.Value, FoldOpening, Ease(0, 1, 170, 130), 1);
        var y = new LookTrack("line y", () => card.Fold.LineY.Value, LineY, Ease(-6, 0, 300), 6);
        var more = new LookTrack("More out", () => card.More.Labels.Single(l => l.Text == "More").Opacity, MoreOut, Ease(1, 0, 100), 1);
        var less = new LookTrack("Less in", () => card.More.Labels.Single(l => l.Text == "Less").Opacity, LessIn, Ease(0, 1, 120, 80), 1);
        var chevron = new LookTrack("chevron", () => card.More.Chevron.Value, ChevronTurn, Ez(0, 180, 300), 180);
        Run(h, w, 21, height, opacity, y, more, less, chevron);
        Assert.True(Largest(height.Seen) <= 4.55, $"largest fold step {Largest(height.Seen):F2} px"); // SPEC 6.10: 4.5 px (to one place)
        Assert.Equal(36, card.Fold.Bounds.Height);
        var row = RowHeight(card); // 58 with the source's ChromeRows (ED-290 (1), lead ruling), taller at the Settings sizes (ED-NEW-textsize-2)
        Assert.Equal(128 + row + 1 + row + 36, card.Bounds.Height); // the card and everything below follow the fold (281 with the 58-point rows; SPEC 2.1's 273)
        Release(w, card.More); // scenario 03
        var shut = new LookTrack("fold shutting", () => card.Fold.LineOpacity.Value, FoldShutting, Ease(1, 0, 80), 1);
        var down = new LookTrack("fold height shutting", () => card.Fold.FoldHeight.Value, FoldHeight.Select(v => 36 - v).ToArray(), Ease(36, 0, 300), 36);
        Run(h, w, 21, shut, down);
        Assert.Equal(128 + row + 1 + row, card.Bounds.Height); // 128 + 58 + 1 + 58 = 245 with the 58-point rows (SPEC 2.1's 237 before the line boxes)
    });

    // ED-253: what follows the card moves down by the fold's height rounded to the nearest pixel, as the locked board's frames
    // draw it (canvas-frames 02: +1, +4, +8, +12 at frames 1 to 4, the fold being 1.38, 4.05, 7.94, 12.45), not rounded up.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void What_follows_the_card_moves_by_the_folds_height_rounded_to_the_nearest_pixel() => Card((card, _, h, w) =>
    {
        Avalonia.Controls.Control below = card;
        while (below.Parent is not Avalonia.Controls.StackPanel { Children.Count: > 1 } stack || stack.Children.IndexOf(below) == stack.Children.Count - 1)
            below = (Avalonia.Controls.Control)below.Parent!;
        var list = (Avalonia.Controls.StackPanel)below.Parent!;
        var next = list.Children[list.Children.IndexOf(below) + 1];
        double Top() => next.TranslatePoint(default, list)!.Value.Y;
        var rest = Top();
        Release(w, card.More); // scenario 02
        var seen = new List<double>();
        for (var k = 1; k <= 21; k++)
        {
            Frame(h);
            Drain(w);
            seen.Add(Top() - rest);
            Assert.Equal(Math.Round(Ease(0, 36, 300)(k * FrameMs), MidpointRounding.AwayFromZero), Top() - rest);
        }
        Assert.Equal([1.0, 4, 8, 12], seen.Take(4));
        Assert.Equal(36, seen[^1]);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_pick_on_the_other_line_fades_one_ring_out_in_place_and_places_the_other_and_fades_it_in() => Card((card, _, h, w) =>
    {
        Click(w, card.More);
        Settle(h, w);
        Release(w, card.Signature.Swatches[4]); // Windows accent -> Orchid (scenario 04)
        Assert.Equal(104, card.Signature.Ring.X.Value); // placed at once
        Assert.Equal(0, card.Classic.Ring.X.Value); // the old ring stays where it is
        var outOpacity = new LookTrack("ring out opacity", () => card.Classic.Ring.Fade.Value, RingOutOpacity, Ease(1, 0, 180), 1);
        var outScale = new LookTrack("ring out scale", () => card.Classic.Ring.Scale.Value, RingOutScale, Ez(1, 0.8, 240), 0.2);
        var inOpacity = new LookTrack("ring in opacity", () => card.Signature.Ring.Fade.Value, RingInOpacity, Ease(0, 1, 180), 1);
        var inScale = new LookTrack("ring in scale", () => card.Signature.Ring.Scale.Value, RingInScale, Ez(0.8, 1, 240), 0.2);
        Run(h, w, 21, outOpacity, outScale, inOpacity, inScale);
        Assert.Equal(104, card.Signature.Ring.X.Value);
        // Orchid -> Slate on the Signature line: that ring slides (scenario 05).
        Release(w, card.Signature.Swatches[11]);
        var slide = new LookTrack("signature slide", () => (card.Signature.Ring.X.Value - 104) / 182, RingSlide, Ez(0, 1, 300), 1);
        Run(h, w, 21, slide);
        Assert.Equal(286, card.Signature.Ring.X.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Shutting_over_a_signature_pick_rings_more_and_gives_it_the_picks_dot_and_opening_runs_it_back() => Card((card, _, h, w) =>
    {
        Click(w, card.More);
        Settle(h, w);
        Click(w, card.Signature.Swatches[4]);
        Settle(h, w);
        Release(w, card.More); // scenario 06
        var ringO = new LookTrack("More ring opacity", () => card.More.RingOpacity.Value, MoreRingOpacity, Ease(0, 1, 300), 1);
        var ringS = new LookTrack("More ring scale", () => card.More.RingScale.Value, MoreRingScale, Ez(0.9, 1, 300), 0.1);
        var dotW = new LookTrack("dot width", () => card.More.DotWidth.Value, MoreDotWidth, Ez(0, 6, 300), 6);
        var dotS = new LookTrack("dot scale", () => card.More.DotScale.Value, MoreDotScale, Ez(0.4, 1, 240), 0.6);
        var chevW = new LookTrack("chevron width", () => card.More.ChevronWidth.Value, ChevronWidth, Ez(7, 0, 300), 7);
        var sigOut = new LookTrack("signature ring out", () => card.Signature.Ring.Fade.Value, RingOutOpacity, Ease(1, 0, 180), 1);
        Run(h, w, 21, ringO, ringS, dotW, dotS, chevW, sigOut);
        Assert.Equal(Avalonia.Media.Color.Parse("#E298C6"), card.More.DotColor.Value);
        // Opening again runs every one in reverse, and the Signature ring is placed and fades in (scenario 07).
        Release(w, card.More);
        var back = new LookTrack("More ring opacity back", () => card.More.RingOpacity.Value, MoreRingOpacity.Select(v => 1 - v).ToArray(), Ease(1, 0, 300), 1);
        var sigIn = new LookTrack("signature ring in", () => card.Signature.Ring.Fade.Value, RingInOpacity, Ease(0, 1, 180), 1);
        Run(h, w, 21, back, sigIn);
        Assert.Equal(7, card.More.ChevronWidth.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_theme_of_the_other_appearance_slides_the_pill_and_turns_the_cards_colours_over_point_32_seconds() => Card((card, _, h, w) =>
    {
        var from = card.Segments.PillLeft.Value;
        Release(w, card.Segments.Buttons[1]); // Dark -> Light (scenario 08)
        Drain(w);
        var to = card.Segments.Buttons[1].Bounds.X;
        var pill = new LookTrack("pill", () => (card.Segments.PillLeft.Value - from) / (to - from), RingSlide, Ez(0, 1, 300), 1);
        var turn = new LookTrack("turnover", () => 1 - card.Turn.Value, Crossfade32, Ease(0, 1, 320), 1);
        var surface = new LookTrack("mini surface", () => (card.Surface.Value.R - 0x1E) / (double)(0xFF - 0x1E), Crossfade32, Ease(0, 1, 320), 1, quantum: 0.5 / (0xFF - 0x1E)); // a colour channel: within half a level
        Run(h, w, 21, pill, turn, surface);
        Assert.True(Largest(turn.Seen) <= 0.19, $"largest crossfade step {Largest(turn.Seen):F3}"); // SPEC 6.10: .19
    });

    // ED-254: segment 4 narrowing from "More themes" (106) to "Dracula" (75) moves the control's left edge on the width's curve
    // (.3 s ez) to its very last frame, its right edge fixed; no whole-pixel settle at the end (the layout is not rounded).
    [Fact]
    [Trait("Decision", "ED-254")]
    public void A_segment_width_change_glides_the_controls_left_edge_on_its_curve_and_its_right_edge_stays() => Card((card, model, h, w) =>
    {
        Rect Drawn() => In(card.Segments.Track, card); // the track: the control as it is drawn
        var start = Drawn();
        Assert.Equal(269, start.Width);
        Assert.Equal(520 - Chrome.RowControlTrailingPadding, start.Right);
        model.Settings.Theme = AppTheme.Dracula;
        Drain(w);
        const double travel = 106 - 75;
        for (var k = 0; k <= 21; k++)
        {
            if (k > 0) { Frame(h); Drain(w); }
            var now = Drawn();
            Assert.Equal(start.Right, now.Right, 6);
            var progress = (now.X - start.X) / travel;
            var curve = CssCurve.At(CssCurve.Ez, k * FrameMs, 300);
            Assert.True(Math.Abs(progress - curve) <= 0.002, $"frame {k}: {progress:F4}, curve {curve:F4}");
            Assert.Equal(card.Segments.Buttons[0].Bounds.X - 2, card.Segments.Track.Bounds.X, 6); // the segments ride on the track
        }
        Assert.Equal(238, Drawn().Width);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_menu_grows_in_from_its_origin_and_escape_shrinks_it_out_in_point_15_seconds() => Card((card, _, h, w) =>
    {
        Release(w, card.Segments.Buttons[3]); // scenario 11
        Assert.True(card.Menu.IsVisible);
        var opacity = new LookTrack("menu opacity", () => card.Menu.OpacityValue.Value, MenuOpening, Ease(0, 1, 200), 1);
        var scale = new LookTrack("menu scale", () => card.Menu.Grow.Value, MenuScaleOpening, PopIn(0.96, 1, 220), 0.04);
        var lift = new LookTrack("menu lift", () => card.Menu.Lift.Value, MenuScaleOpening.Select(v => -4 + 4 * (v - 0.96) / 0.04).ToArray(), PopIn(-4, 0, 220), 4);
        Run(h, w, 15, opacity, scale, lift);
        Assert.Equal(new RelativePoint(264 - 44, 0, RelativeUnit.Absolute), card.Menu.RenderTransformOrigin);
        Press(w, Key.Escape); // scenario 15
        var closing = new LookTrack("menu closing", () => card.Menu.OpacityValue.Value, MenuClosing, Ease(1, 0, 150), 1);
        var shrink = new LookTrack("menu scale closing", () => card.Menu.Grow.Value, MenuScaleClosing, Ease(1, 0.96, 150), 0.04);
        Run(h, w, 11, closing, shrink);
        Assert.False(card.Menu.IsVisible); // hidden at .15 s
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_swatch_grows_on_hover_shrinks_on_press_and_its_name_crossfades_into_the_detail_line() => Card((card, _, h, w) =>
    {
        var blue = card.Classic.Swatches[1];
        w.MouseMove(Centre(blue, w)); // scenario 18
        var hover = new LookTrack("hover scale", () => blue.Scale.Value, HoverScale, Ease(1, 1.12, 120), 0.12);
        var text = new LookTrack("detail in", () => card.AccentDetail.Lines.Single(l => l.Text == "Blue").Opacity, TextIn, Ease(0, 1, 200), 1);
        var old = new LookTrack("detail out", () => card.AccentDetail.Lines.Where(l => l.Text != "Blue").Select(l => l.Opacity).DefaultIfEmpty(0).Single(), TextIn.Select(v => 1 - v).ToArray(), Ease(1, 0, 200), 1);
        Run(h, w, 12, hover, text, old);
        w.MouseDown(Centre(blue, w), MouseButton.Left); // scenario 19
        var press = new LookTrack("press scale", () => blue.Scale.Value, PressScale, Ease(1.12, 0.86, 80), 0.26);
        Run(h, w, 8, press);
        w.MouseUp(Centre(blue, w), MouseButton.Left); // scenario 20: the pick, back to the hover scale
        var release = new LookTrack("release scale", () => blue.Scale.Value, [], Ease(0.86, 1.12, 120), 0.26);
        Run(h, w, 9, release);
        Assert.Equal("blue", card.Settings.Accent);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_segment_presses_to_point_96_and_its_label_lights_on_hover_over_point_15() => Card((card, _, h, w) =>
    {
        var light = card.Segments.Buttons[1];
        w.MouseMove(Centre(light, w));
        var lit = new LookTrack("label lit", () => light.Lit.Value, [], Ease(0, 1, 150), 1);
        Run(h, w, 10, lit);
        w.MouseDown(Centre(light, w), MouseButton.Left); // scenario 24
        var press = new LookTrack("segment press", () => light.Scale.Value, [], Ease(1, 0.96, 120), 0.04);
        Run(h, w, 8, press);
        w.MouseUp(Centre(light, w), MouseButton.Left);
        Settle(h, w);
        Assert.Equal(1, light.Scale.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void More_lights_its_capsule_on_hover_and_presses_to_point_94() => Card((card, _, h, w) =>
    {
        w.MouseMove(Centre(card.More, w)); // scenario 22
        var hover = new LookTrack("capsule hover", () => card.More.Hovered.Value, [], Ease(0, 1, 120), 1);
        Run(h, w, 9, hover);
        w.MouseDown(Centre(card.More, w), MouseButton.Left); // scenario 23
        var press = new LookTrack("More press", () => card.More.Scale.Value, [], Ease(1, 0.94, 80), 0.06);
        Run(h, w, 6, press);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Under_reduce_motion_every_change_is_drawn_at_its_end_at_once_and_asks_for_no_frame() => Card((card, model, h, w) =>
    {
        void Still()
        {
            Drain(w);
            Assert.False(card.Motion.IsMoving);
        }
        Click(w, card.Classic.Swatches[8]);
        Still();
        Assert.Equal(208, card.Classic.Ring.X.Value);
        Assert.Equal(Avalonia.Media.Color.Parse("#40C8E0"), card.Accent.Value);
        Assert.Equal([("Teal", 1.0)], card.AccentDetail.Lines.Where(l => l.Opacity > 0));
        Click(w, card.More);
        Still();
        Assert.Equal(36, card.Fold.FoldHeight.Value);
        Assert.Equal(1, card.Fold.LineOpacity.Value);
        Click(w, card.Signature.Swatches[4]);
        Click(w, card.More);
        Still();
        Assert.Equal(1, card.More.RingOpacity.Value);
        Assert.Equal(6, card.More.DotWidth.Value);
        Click(w, card.Segments.Buttons[1]);
        Still();
        Assert.Equal(0, card.Turn.Value);
        Assert.Equal(card.Segments.Buttons[1].Bounds.X, card.Segments.PillLeft.Value);
        Click(w, card.Segments.Buttons[3]);
        Still();
        Assert.Equal(1, card.Menu.OpacityValue.Value);
        Press(w, Key.Escape);
        Still();
        Assert.False(card.Menu.IsVisible);
        Hover(w, card.Classic.Swatches[2]);
        Still();
        Assert.Equal(1.12, card.Classic.Swatches[2].Scale.Value);
        Assert.Equal(AppTheme.Light, model.Settings.Theme);
    }, reduceMotion: true);
}
