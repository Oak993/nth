using Avalonia;
using Avalonia.Automation;
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
}
