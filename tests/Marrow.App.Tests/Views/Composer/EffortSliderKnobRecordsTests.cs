using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :711-837 the slider and :620-624 the single-level note.
// DEVIATION OD-0090 (locked 2026-10-03): the knob is the 48 px Jewel orb. PR-COMPOSER-117, 123, 125 and 126 state parts of it, and
// EffortSliderJewelReviewR8Tests forbids a Req trait on them (and on PR-COMPOSER-131, UI-COMPOSER-035, 036), so these tests carry
// Decision ED-252: the clauses of the amended records that no other test reads.
[Collection("Headless")]
public sealed class EffortSliderKnobRecordsTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content, double width = 330)
    {
        var host = new Border { Child = content, Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = width, Height = 300 };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static Ellipse[] Stops(EffortSlider slider) =>
        slider.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Width == 5).OrderBy(Canvas.GetLeft).ToArray();

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ---- PR-COMPOSER-123: the track as drawn ------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:741-775 (PR-COMPOSER-123, the 48 px knob of OD-0090): five stops a knob radius in from each end, 5 point dots, passed ones the look's stop colour, unpassed the label at 32 percent
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    public void Five_stops_sit_a_radius_in_and_a_step_apart_and_the_passed_ones_take_the_looks_stop_colour() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var index = 2;
        var slider = new EffortSlider(5, new Binding<int>(() => index, _ => { }), "Medium");
        var window = Show(slider);
        try
        {
            var dots = Stops(slider);
            Assert.Equal(5, dots.Length);
            var step = (330 - 48) / 4.0; // :748 (width - 2 * inset) / (count - 1)
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal((5, 5), (dots[i].Width, dots[i].Height)); // :770
                Assert.Equal(24 + i * step, Canvas.GetLeft(dots[i]) + 2.5, 6); // :767 the stop's centre
                Assert.Equal(24, Canvas.GetTop(dots[i]) + 2.5, 6); // on the control's middle line
            }
            var theme = slider.ActualThemeVariant;
            var passed = SwiftColor.White.Opacity(0.6).Resolve(theme); // :1015 stopColor on a colour fill
            var unpassed = Chrome.Overlay(0.32).Resolve(theme); // :769
            for (var i = 0; i < 5; i++) Assert.Equal(i <= 2 ? passed : unpassed, Solid(dots[i].Fill)); // a stop under the knob is passed (stopX <= x)
            // the fill ends at the knob's far edge and the base is the label at 10 percent across the full width, 34 tall, a capsule
            var fill = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EffortFill");
            Assert.Equal(24 + 2 * step + 24, fill.Width, 6);
            var track = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Height == 34 && b.Name != "EffortFill");
            Assert.Equal(330, track.Width, 6);
            Assert.Equal(Chrome.Overlay(0.1).Resolve(theme), Solid(track.Background)); // :746 Chrome.overlay(0.1)
            Assert.Equal(new CornerRadius(17), track.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(track)); // :745 Capsule(style: .continuous)
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:1012-1016 (PR-COMPOSER-123): on a light fill (a white brand at maximum) the passed stops are dark
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018 as amended by OD-0090 (the glow is no longer drawn)
    public void On_a_light_fill_the_passed_stops_are_dark() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = new EffortSlider(3, new Binding<int>(() => 2, _ => { }), "High", brand: EffortBrand.Silver);
        var window = Show(slider);
        try
        {
            var theme = slider.ActualThemeVariant;
            Assert.All(Stops(slider), dot => Assert.Equal(SwiftColor.Black.Opacity(0.35).Resolve(theme), Solid(dot.Fill))); // :1015 isLightFill: black at 35 percent
        }
        finally { window.Close(); }
        // below maximum the same brand's fill is the accent, not light: white stops
        var below = new EffortSlider(3, new Binding<int>(() => 1, _ => { }), "Medium", brand: EffortBrand.Silver);
        window = Show(below);
        try
        {
            var theme = below.ActualThemeVariant;
            Assert.Equal(SwiftColor.White.Opacity(0.6).Resolve(theme), Solid(Stops(below)[0].Fill));
            Assert.Equal(SwiftColor.White.Opacity(0.6).Resolve(theme), Solid(Stops(below)[1].Fill));
            Assert.Equal(Chrome.Overlay(0.32).Resolve(theme), Solid(Stops(below)[2].Fill));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:741-748,833-836 (PR-COMPOSER-123 failure case, PR-COMPOSER-125): count <= 1 makes the step 0, the knob rests at the inset and the index never moves
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:712-796 as amended by OD-0090 (the 48 px Jewel orb)
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void A_one_stop_slider_rests_at_the_inset_and_never_writes_a_nonzero_index() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        using var frameBinding = FrameClock.Source.Override(frames); using var clockBinding = SwiftClock.Provider.Override(time);
        var index = 0; var writes = 0;
        var slider = new EffortSlider(1, new Binding<int>(() => index, value => { index = value; writes++; }), "Standard");
        var window = Show(slider);
        try
        {
            void Settle() { for (var i = 0; i < 60; i++) { time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); frames.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); Dispatcher.UIThread.RunJobs(); } }
            Settle();
            Assert.Equal(24, slider.KnobX, 6); // the inset, not NaN
            var origin = slider.TranslatePoint(default, window)!.Value;
            window.MouseDown(new Point(origin.X + 300, origin.Y + 24), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(origin.X + 100, origin.Y + 24), RawInputModifiers.None);
            window.MouseUp(new Point(origin.X + 100, origin.Y + 24), MouseButton.Left, RawInputModifiers.None);
            Settle();
            Assert.Equal(0, index);
            Assert.Equal(0, writes);
            Assert.Equal(24, slider.KnobX, 6); // and it springs back to its one stop
            Assert.Single(Stops(slider));
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-126: the accessibility element ------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:824-829 (PR-COMPOSER-126): the label is "The lead's reasoning effort" in a pair, and the value is the current title
    [Trait("Req", "PR-COMPOSER-126")] // EffortSlider.swift:820-830 as amended by OD-0090 (48 px, a step springs)
    public void A_pair_slider_is_the_leads_reasoning_effort_and_its_value_tracks_the_title() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var plain = new EffortSlider(3, new Binding<int>(() => 0, _ => { }), "Low");
        var window = Show(plain);
        try
        {
            Assert.Equal("Reasoning effort", AutomationProperties.GetName(plain));
            Assert.Equal("Low", AutomationProperties.GetItemStatus(plain));
            plain.UpdateLook(true, "High", null); // the title moved on: the value follows it
            Assert.Equal("High", AutomationProperties.GetItemStatus(plain));
            Assert.Equal("Reasoning effort", AutomationProperties.GetName(plain));
        }
        finally { window.Close(); }
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex));
        var paired = new EffortSlider(3, new Binding<int>(() => 1, _ => { }), "Medium", pair: pair);
        window = Show(paired);
        try
        {
            Assert.Equal("The lead's reasoning effort", AutomationProperties.GetName(paired));
            Assert.Equal("Medium", AutomationProperties.GetItemStatus(paired));
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-117: the single-level note ----------------------------------------------------------------------------------

    private static EffortSliderCard Card(string[] levels) =>
        new("Test model", ProviderKind.Claude, new ValueList<string>(levels), null, false, Binding<string?>.Constant(null), Binding<bool>.Constant(false), () => { });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:620-624 (PR-COMPOSER-117, UI-COMPOSER-036; the 48 px of OD-0090): .frame(height:) centres the line in the slot the slider would fill, and the card keeps its size between models
    [Trait("Req", "PR-COMPOSER-117")] // EffortSlider.swift:620-624 as amended by OD-0090 (48 px)
    [Trait("Req", "UI-COMPOSER-036")] // EffortSlider.swift:620-624 as amended by OD-0090 (48 px)
    public void The_single_level_note_is_secondary_centred_in_the_48_point_slot_and_the_card_keeps_its_size() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var with = Card(["low", "medium", "high"]);
        var without = Card([]);
        var window = Show(with); var withHeight = with.Bounds.Height; window.Close();
        window = Show(without);
        try
        {
            Assert.Equal(withHeight, without.Bounds.Height, 0.5); // the card does not change size between models
            var note = without.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "This model has one reasoning level.");
            Assert.Equal(Chrome.SecondaryText.Resolve(note.ActualThemeVariant), Solid(note.Foreground)); // secondaryText
            Assert.Equal(12, note.FontSize); // .callout
            Assert.Equal(48, Assert.IsAssignableFrom<Panel>(note.Parent).Height); // OD-0090: the slot is the knob's 48 px (the source's 42), exactly
            Assert.Empty(without.GetVisualDescendants().OfType<EffortSlider>()); // in place of the slider
            // the slot is where the slider would be: 16 below the header, 48 tall; the line's ink is centred in it
            var titleBlock = without.GetVisualDescendants().OfType<MorphingTextBlock>().Single().GetVisualAncestors().OfType<StackPanel>().First(s => s.Spacing == 2);
            var headerBottom = Math.Max(titleBlock.TranslatePoint(new Point(0, titleBlock.Bounds.Height), without)!.Value.Y,
                without.GetVisualDescendants().OfType<RoundIconButton>().Single().TranslatePoint(new Point(0, 30), without)!.Value.Y);
            var slotTop = headerBottom + 16;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)without.Bounds.Width, (int)without.Bounds.Height));
            bitmap.Render(without);
            using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
            using var pixels = SKBitmap.Decode(stream);
            int first = -1, last = -1;
            for (var y = (int)Math.Ceiling(slotTop); y < Math.Min(pixels.Height, (int)(slotTop + 48)); y++)
                for (var x = 0; x < pixels.Width; x++)
                    if (pixels.GetPixel(x, y).Alpha > 24) { if (first < 0) first = y; last = y; break; }
            Assert.True(first >= 0, "the note draws no ink in the slot");
            var centre = (first + last + 1) / 2.0;
            Assert.True(Math.Abs(centre - (slotTop + 24)) <= 3, $"the line's ink is centred at {centre} in a slot from {slotTop} to {slotTop + 48} (middle {slotTop + 24})");
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-125: where a press is taken -----------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:800 .contentShape(.rect) (PR-COMPOSER-125 "presses anywhere on the track"): the whole control frame takes the press, the knob's proud 7 px and the rounded ends included
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
    public void A_press_anywhere_in_the_control_frame_takes_the_drag() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        using var frameBinding = FrameClock.Source.Override(frames); using var clockBinding = SwiftClock.Provider.Override(time);
        var index = 0;
        var slider = new EffortSlider(5, new Binding<int>(() => index, value => index = value), "Low");
        var window = Show(slider);
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var origin = slider.TranslatePoint(default, window)!.Value;
            // the 7 px above and below the 34 px track (where the 48 px knob stands proud), the track's rounded ends, and the far corner
            foreach (var (x, y) in new[] { (1.0, 1.0), (1.0, 24.0), (24.0, 2.0), (24.0, 46.0), (165.0, 1.0), (165.0, 47.0), (328.0, 24.0), (329.0, 47.0) })
            {
                window.MouseDown(new Point(origin.X + x, origin.Y + y), MouseButton.Left, RawInputModifiers.None);
                Assert.True(slider.IsHeld, $"a press at ({x}, {y}) of the 330 x 48 control is not taken");
                window.MouseUp(new Point(origin.X + x, origin.Y + y), MouseButton.Left, RawInputModifiers.None);
                Assert.False(slider.IsHeld);
                for (var i = 0; i < 60; i++) { time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); frames.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); Dispatcher.UIThread.RunJobs(); }
            }
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-123, PR-COMPOSER-131, UI-COMPOSER-035: what the knob is not -----------------------------------------------------

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "..", "..", "..", "..", "src"));

    [Fact]
    [Trait("Req", "PR-COMPOSER-123")] // EffortSlider.swift:781-792 (the glow disc and the Liquid Glass lens, OD-0090: not drawn): the knob is the orb, with its own shadow
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:781-785 look.glow is read only by the glow disc: nothing draws TrackLook.Glow now
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-035 stays guarded (EffortSliderJewelReviewR8Tests): the page's layers over the orb have no pixel comparison; this test proves a clause of it
    public void The_knob_is_the_orb_alone_with_no_glow_disc_no_glass_lens_and_nothing_reads_the_looks_glow() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        foreach (var brand in new[] { EffortBrand.Claude, EffortBrand.Silver })
            foreach (var fast in new[] { false, true })
            {
                var slider = new EffortSlider(5, new Binding<int>(() => 4, _ => { }), "Max", fast, brand);
                var window = Show(slider);
                try
                {
                    var everything = slider.GetVisualDescendants().OfType<Control>().Append(slider).ToArray();
                    // the only circles are the five stops (5 points across): no 42 or 48 point disc under the knob
                    Assert.All(everything.OfType<Avalonia.Controls.Shapes.Ellipse>(), e => Assert.Equal(5, e.Width));
                    Assert.Equal(5, everything.OfType<Avalonia.Controls.Shapes.Ellipse>().Count());
                    // no blurred glow, no glass material, on the knob or anywhere under the slider
                    Assert.All(everything, c => Assert.Null(c.Effect));
                    Assert.All(everything, c => Assert.Null(SwiftGlass.GetEffect(c)));
                    // the knob's box holds the orb's view and nothing else
                    var knob = Assert.Single(everything.OfType<Panel>(), p => p.Width == EffortSlider.ThumbSize && p.Height == EffortSlider.ThumbSize);
                    Assert.IsType<JewelKnobView>(Assert.Single(knob.Children));
                    // the only border drawn around anything of the knob's size would be the lens's black 0.14 edge on a light fill
                    Assert.DoesNotContain(everything.OfType<Border>(), b => b.Width == EffortSlider.ThumbSize && b.BorderThickness != default);
                }
                finally { window.Close(); }
            }
        // TrackLook still derives the glow by the source's rules (checked by EffortSliderTests), but no code reads it: the one
        // member that mentions it is the getter that derives it
        var reads = new List<string>();
        foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(SourceRoot(), "Marrow.App"), "*.cs", SearchOption.AllDirectories))
            foreach (var (line, number) in File.ReadLines(file).Select((line, number) => (line, number + 1)))
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b[lL]ook\.Glow\b|\bTrackLook\b.*\.Glow\b|\.Look\.Glow\b")) reads.Add($"{System.IO.Path.GetFileName(file)}:{number}");
        Assert.Empty(reads);
    });

    // ---- UI-COMPOSER-035, PR-COMPOSER-123: the knob's shadows, from the page's numbers --------------------------------------------------

    // CSS box-shadow: the shape offset by (0, dy), blurred with a Gaussian of sigma = blur / 2 (the CSS Backgrounds spec); the
    // share of the blurred disc at a pixel's centre, by summing the Gaussian over the disc on a half pixel grid
    private static double Blurred(double px, double py, double cx, double cy, double r, double sigma)
    {
        double inside = 0, all = 0; var reach = 4 * sigma;
        for (var v = py - reach; v <= py + reach; v += 0.5)
            for (var u = px - reach; u <= px + reach; u += 0.5)
            {
                var w = Math.Exp(-((u - px) * (u - px) + (v - py) * (v - py)) / (2 * sigma * sigma));
                all += w;
                if ((u - cx) * (u - cx) + (v - cy) * (v - cy) <= r * r) inside += w;
            }
        return inside / all;
    }

    private static SKBitmap ShadowsOf(bool dark, double lift)
    {
        var u = new JewelUniforms();
        u.Values[21] = dark ? 1 : 0;
        u.Lift = (float)lift;
        var bitmap = new SKBitmap(new SKImageInfo(160, 160, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        JewelKnobRenderer.For(null, 96).Shadows(canvas, u.Values, new SKRect(56, 56, 104, 104), 1);
        return bitmap;
    }

    [Theory]
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-035 stays guarded (EffortSliderJewelReviewR8Tests): the page's layers over the orb have no pixel comparison; this test proves a clause of it
    [Trait("Req", "PR-COMPOSER-123")] // OD-0090: the knob has a soft shadow beneath it that deepens as it lifts
    [InlineData(true)]
    [InlineData(false)]
    public void The_knobs_shadows_are_the_pages_offsets_blurs_and_colours(bool dark)
    {
        using var resting = ShadowsOf(dark, 0);
        using var lifted = ShadowsOf(dark, 1);
        const double cx = 80, cy = 80, r = 24;
        var (red, green, blue, alpha) = dark ? (0, 0, 0, 0.24) : (20, 32, 50, 0.2);
        // points outside the circle, below it, to its sides and above it (the shadow is offset down, so it is weakest above)
        (int X, int Y)[] points = [(80, 106), (80, 110), (80, 118), (80, 128), (50, 90), (54, 100), (108, 100), (80, 52), (80, 46), (110, 60), (80, 136)];
        var biggest = 0.0;
        foreach (var (x, y) in points)
        {
            var own = alpha * Blurred(x + 0.5, y + 0.5, cx, cy + 2, r, 3);
            var shown = resting.GetPixel(x, y);
            Assert.True(Math.Abs(shown.Alpha - 255 * own) <= 3, $"the knob's own shadow at ({x}, {y}): alpha {shown.Alpha}, the page's 0 2px 6px at {alpha} gives {255 * own:F1}");
            if (shown.Alpha >= 30) Assert.True(Math.Abs(shown.Red - red) <= 4 && Math.Abs(shown.Green - green) <= 4 && Math.Abs(shown.Blue - blue) <= 6, $"its colour at ({x}, {y}) is ({shown.Red}, {shown.Green}, {shown.Blue}), the page's ({red}, {green}, {blue})");
            biggest = Math.Max(biggest, own);
            // the lift adds 0 7px 18px at .34 and 0 2px 5px at .2 (black), the two composited as one group and then over the own shadow
            var near = 0.2 * Blurred(x + 0.5, y + 0.5, cx, cy + 2, r, 2.5);
            var far = 0.34 * Blurred(x + 0.5, y + 0.5, cx, cy + 7, r, 9);
            var group = 1 - (1 - near) * (1 - far);
            var expected = 255 * (1 - (1 - own) * (1 - group));
            Assert.True(Math.Abs(lifted.GetPixel(x, y).Alpha - expected) <= 4, $"with the lift at ({x}, {y}): alpha {lifted.GetPixel(x, y).Alpha}, the page's three shadows give {expected:F1}");
        }
        Assert.True(biggest > 0.07, "the premise: the points reach the shadow's body");
        // CSS box-shadows are not drawn under the element itself: inside the circle nothing is drawn
        Assert.Equal(0, lifted.GetPixel(80, 80).Alpha);
        Assert.Equal(0, resting.GetPixel(80, 70).Alpha);
    }

    // ---- PR-COMPOSER-125: the alignment haptic ----------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:808-813 each index change performs .alignment, and nothing else is performed; windows_path: no Windows equivalent, dropped silently, never a sound
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-035 stays guarded (EffortSliderJewelReviewR8Tests): the page's layers over the orb have no pixel comparison; this test proves a clause of it
    public void Each_index_change_fires_one_alignment_haptic_and_a_press_that_changes_nothing_fires_none() => headless.OnUiThread(() =>
    {
        var haptics = new List<HapticPattern>();
        using var sink = Haptics.Sink.Override(haptics.Add);
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        Assert.Empty(haptics);
        rig.Down(24); // on the knob at the first stop: no change, so none
        Assert.Empty(haptics);
        var changes = 0; var last = rig.Index.Value;
        foreach (var x in new double[] { 30, 60, 66, 100, 150, 200, 270, 400, 270, 140, 40, -50, 24 })
        {
            rig.Move(x);
            if (rig.Index.Value != last) { changes++; last = rig.Index.Value; }
            Assert.Equal(changes, haptics.Count); // one for each change, as it happens
        }
        rig.Up(24);
        Assert.True(changes >= 5, $"the premise: the drag crossed {changes} midpoints");
        Assert.All(haptics, pattern => Assert.Equal(HapticPattern.Alignment, pattern));
        // pressed on the track away from the knob: the index follows the pointer at once, with its one haptic
        haptics.Clear();
        for (var i = 0; i < 90; i++) rig.Frame();
        Assert.Equal(0, rig.Index.Value);
        rig.Down(200);
        Assert.Equal(2, rig.Index.Value);
        Assert.Equal([HapticPattern.Alignment], haptics);
        rig.Up(200);
        // an accessibility step is not a drag: it writes the effort and performs nothing (:824-830)
        haptics.Clear();
        Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(rig.Slider)).SetValue(3);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, rig.Index.Value);
        Assert.Empty(haptics);
    });

    // ---- PR-COMPOSER-126: the accessibility element -----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-126")] // EffortSlider.swift:820-830 one element, labelled, valued by the title, adjustable and clamped; OD-0090: 48 px high and a step springs
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-035 stays guarded (EffortSliderJewelReviewR8Tests): the page's layers over the orb have no pixel comparison; this test proves a clause of it
    public void The_slider_is_one_labelled_element_valued_by_the_title_and_adjustable_to_both_ends() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        using var frameBinding = FrameClock.Source.Override(frames); using var clockBinding = SwiftClock.Provider.Override(time);
        var selected = 1; var writes = new List<int>();
        var slider = new EffortSlider(3, new Binding<int>(() => selected, value => { selected = value; writes.Add(value); }), "Medium");
        var window = Show(slider);
        try
        {
            Assert.Equal(48, slider.Height); // OD-0090: the control reserves the knob's height
            var peer = ControlAutomationPeer.CreatePeerForElement(slider);
            Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
            Assert.Equal("Reasoning effort", peer.GetName()); // :825 exactly, without a pair
            // one element: the track, the stops and the knob are not elements of their own (the canvas that holds them is neither a content nor a control element)
            Assert.DoesNotContain(peer.GetChildren(), child => child.IsControlElement() || child.IsContentElement());
            // the value is the current effort's title (:826-827 accessibilityValue(Text(verbatim: accessibilityTitle)))
            var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.True(value.IsReadOnly);
            Assert.Equal("Medium", value.Value);
            slider.UpdateLook(true, "High", null); // the title moved on with the effort
            Assert.Equal("High", value.Value);
            slider.UpdateLook(false, "Standard", null); // and a model with no levels says "Standard"
            Assert.Equal("Standard", value.Value);
            // increment and decrement, clamped to the ends (:828-830): from the middle one step each way, then against each end
            var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
            Assert.Equal((0.0, 2.0, 1.0), (range.Minimum, range.Maximum, range.SmallChange));
            range.SetValue(range.Value + range.SmallChange); Assert.Equal(2, selected);
            range.SetValue(range.Value + range.SmallChange); Assert.Equal(2, selected); // increment clamps to count - 1
            range.SetValue(range.Value - range.SmallChange); Assert.Equal(1, selected);
            range.SetValue(range.Value - range.SmallChange); Assert.Equal(0, selected);
            range.SetValue(range.Value - range.SmallChange); Assert.Equal(0, selected); // decrement clamps to 0
            Assert.Equal([2, 2, 1, 0, 0], writes); // each adjustment writes the effort, as a drag's change does
            // OD-0090 / OD-0076: a step springs the knob to its new stop from where it is (0.2 s, damping 1), never a cut
            range.SetValue(2); Dispatcher.UIThread.RunJobs();
            Assert.True(slider.KnobX < 24 + (330 - 48) / 2.0 * 2 - 1, "the knob has not jumped to the new stop");
            for (var i = 0; i < 90; i++) { time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); frames.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(330 - 24, slider.KnobX, 6);
        }
        finally { window.Close(); }
        // in a pair the label is the lead's (:825)
        var paired = new EffortSlider(3, new Binding<int>(() => 1, _ => { }), "Medium", pair: new EffortPairBrand(new HydraPair(ProviderKind.Codex)));
        window = Show(paired);
        try { Assert.Equal("The lead's reasoning effort", ControlAutomationPeer.CreatePeerForElement(paired).GetName()); }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-131: the look's derived colours, by kind and by pair ----------------------------------------------------------------

    private static string Rgba(SwiftColor colour) { var c = colour.Resolve(Avalonia.Styling.ThemeVariant.Light); return $"{c.R},{c.G},{c.B},{c.A}"; }

    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018 TrackLook.glow, isLightFill, stopColor and spark, every kind with and without a pair; OD-0090: the glow is derived but no longer drawn
    public void The_looks_glow_light_test_stop_colour_and_spark_follow_the_source_by_kind_and_pair()
    {
        SwiftColor Black(double a) => SwiftColor.Black.Opacity(a);
        SwiftColor White(double a) => SwiftColor.White.Opacity(a);
        var claudeFill = new SwiftColor(0.80, 0.42, 0.28); // :895 fillColor, glow = fillColor at 0.6 (:930)
        var fastFill = new SwiftColor(0.93, 0.66, 0.13); // :846
        var fast = new SwiftColor(1.0, 0.84, 0.36); // :845
        // (kind, brand, glow, light fill, stop colour, spark): the source's table, without a pair
        var solo = new (TrackLook.Kind Kind, EffortBrand Brand, SwiftColor Glow, bool Light, SwiftColor Stop, SwiftColor Spark)[]
        {
            (TrackLook.Kind.Plain, EffortBrand.Claude, Black(0.28), false, White(0.6), SwiftColor.White),               // :1004 plain: black at 0.28
            (TrackLook.Kind.Plain, EffortBrand.Silver, Black(0.28), false, White(0.6), SwiftColor.White),               // a white brand below maximum is not a light fill (:1012)
            (TrackLook.Kind.Supercharged, EffortBrand.Claude, claudeFill.Opacity(0.6), false, White(0.6), SwiftColor.White), // :1005 brand.glow; :923 spark white on colour
            (TrackLook.Kind.Supercharged, EffortBrand.Silver, Black(0.35), true, Black(0.35), SwiftColor.Black),         // :928 silver's glow black at 0.35; light; :1015 dark stops; :923 spark black
            (TrackLook.Kind.Fast, EffortBrand.Claude, fastFill.Opacity(0.7), false, White(0.6), SwiftColor.White),      // :1006 fastFill at 0.7; spark white (:1018: only supercharged and fusion use the brand's)
            (TrackLook.Kind.Fast, EffortBrand.Silver, fastFill.Opacity(0.7), false, White(0.6), SwiftColor.White),
            (TrackLook.Kind.Fusion, EffortBrand.Claude, fast.Opacity(0.75), false, White(0.6), SwiftColor.White),       // :1007 fast at 0.75; the brand's spark
            (TrackLook.Kind.Fusion, EffortBrand.Silver, fast.Opacity(0.75), false, White(0.6), SwiftColor.Black),       // a white brand's fusion is not light (its far end is gold, :1010) but sparks dark
        };
        foreach (var (kind, brand, glow, light, stop, spark) in solo)
        {
            var look = new TrackLook(kind, brand);
            var what = $"{kind} {brand}";
            Assert.Equal(Rgba(glow), Rgba(look.Glow)); Assert.True(light == look.IsLightFill, $"{what}: light fill {look.IsLightFill}");
            Assert.Equal(Rgba(stop), Rgba(look.StopColor)); Assert.Equal(Rgba(spark), Rgba(look.Spark));
        }
        // with a pair, whatever the kind (:1001 glow, :1012 light, :1018 spark): heads on Antigravity end on the Gemini sweep's coral
        // (0.85, 0.40, 0.44) and are not light; heads on Codex are the silver pair, whose far end is light
        var gemini = new EffortPairBrand(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity });
        var silver = new EffortPairBrand(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex });
        foreach (var kind in new[] { TrackLook.Kind.Plain, TrackLook.Kind.Supercharged, TrackLook.Kind.Fast, TrackLook.Kind.Fusion })
        {
            var coral = new TrackLook(kind, EffortBrand.Claude, gemini);
            Assert.Equal(Rgba(new SwiftColor(0.85, 0.40, 0.44).Opacity(0.7)), Rgba(coral.Glow)); // the pair's last colour at 0.7
            Assert.False(coral.IsLightFill); Assert.Equal(Rgba(White(0.6)), Rgba(coral.StopColor)); Assert.Equal(Rgba(SwiftColor.White), Rgba(coral.Spark));
            var light = new TrackLook(kind, EffortBrand.Claude, silver);
            Assert.Equal(Rgba(Black(0.35)), Rgba(light.Glow)); // a light pair: black at 0.35
            Assert.True(light.IsLightFill); Assert.Equal(Rgba(Black(0.35)), Rgba(light.StopColor)); Assert.Equal(Rgba(SwiftColor.White), Rgba(light.Spark)); // a pair never sparks in the brand's colour
        }
    }

    // ---- PR-COMPOSER-125: where a press counts as being on the knob -----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-125")] // OD-0090: "pressed on the knob (within its 24 px radius of its drawn centre), it lifts to 1.06x where it is and never slides sideways"; pressed on the track away from it, the knob springs to the pointer
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-035 stays guarded (EffortSliderJewelReviewR8Tests): the page's layers over the orb have no pixel comparison; this test proves a clause of it
    public void A_press_within_the_knobs_radius_lifts_it_in_place_and_one_beyond_it_springs_it_to_the_pointer() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(24 + 23.9); // inside the 24 px radius
        for (var i = 0; i < 40; i++) rig.Frame();
        Assert.Equal(24, rig.Slider.KnobX, 6); // the knob did not slide sideways
        Assert.True(rig.Slider.KnobScale > 1.05, $"it lifted: {rig.Slider.KnobScale}");
        rig.Up(24 + 23.9);
        for (var i = 0; i < 90; i++) rig.Frame();
        Assert.Equal(1, rig.Slider.KnobScale, 6);
        rig.Down(24 + 24.5); // just outside it: the track
        for (var i = 0; i < 60; i++) rig.Frame();
        Assert.True(Math.Abs(rig.Slider.KnobX - (24 + 24.5)) < 0.5, $"the knob sprang to the pointer: {rig.Slider.KnobX}");
        Assert.True(rig.Slider.KnobScale > 1.05);
        rig.Up(24 + 24.5);
    });
}
