using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Chat;

// PR-CHAT-073 against the dark rail on the reference frames (2-chat-use.mov C 12.000, CATALOG scene 22 "Turn-position tick ruler": 26 ticks, one pink one;
// and the hover of C 177.60 to 178.20 on a six-tick thread: the hovered tick white, the others lifted). 1 px = 1 pt. Source: DroppyCode/App/Views/Chat/TimelineMinimap.swift:193-199,
// :258-282 (tick fill and width), :298-329 (rail metrics).
//
// MEASURED ON THE FRAMES (python, E:\Droppy\work\scratch\frames\railmeasure.py, railm2.py), pixel rows and columns counted, the ticks' colour as the fraction
// of the way from the window to white (the label colour in the dark theme is white at 0.85, so 0.25 of it is 0.2125 and 0.4 of it 0.34):
//   rest: 16 ticks 22 wide, one 26 wide (the lit one), 9 ticks 12 wide, every one 2 high and 10 apart, from column 26 of the crop = 8 in from the rail's edge;
//         the unselected ticks' strength 0.19 to 0.235 (mean 0.21);
//   hover: the hovered tick 26 wide, the others 2 wider (12 -> 14, 22 -> 24, the lit one stays 26), their strength 0.34 to 0.35 (steady), the hovered tick's 0.79
//         once it has risen (216 of 255 over a 67 window: 0.79; the label colour at 0.95 is 0.81).
// The lit tick's colour on a 2-point bar is smeared by the video's chroma subsampling (it reads 152,61,91 for the accent 249,38,114), so the port's is held to
// the accent itself and to the geometry.
[Collection("Headless")]
public sealed class TimelineMinimapFrameTests(HeadlessSession headless)
{
    private static TimelineMinimapEntry Entry(string id, double weight) => new() { Id = id, Title = id, Snippet = "", Weight = weight };
    private static void Hover(TimelineMinimapRail rail, string? id) => typeof(TimelineMinimapRail)
        .GetMethod("SetHovered", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rail, [id]);
    private static Control[] Cards(TimelineMinimapRail rail) => rail.GetVisualDescendants().OfType<Control>().Where(control => SwiftGlass.GetEffect(control) is not null).ToArray();
    private static void Step(TestRuntime runtime, ManualFrameSource frames, int milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static SKBitmap Capture(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return SKBitmap.Decode(stream.ToArray());
    }

    // The tick that starts at row y: its extent along the row y and the colour of its middle, against the window beside it.
    private static (int Width, double Strength, SKColor Colour) Tick(SKBitmap pixels, int y, SKColor window)
    {
        var width = 0;
        for (var x = 0; x < 34; x++) // the preview card of a hovered tick starts at 34 (:326)
        {
            var c = pixels.GetPixel(x, y);
            if (Math.Abs(c.Green - window.Green) > 12 || Math.Abs(c.Red - window.Red) > 12) width = x + 1;
        }
        width -= 8; // the rail's 8 point leading padding (:263)
        var middle = pixels.GetPixel(8 + Math.Min(width, 22) / 2, y);
        return (width, (middle.Green - window.Green) / (255.0 - window.Green), middle);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-073")] // TimelineMinimap.swift:193-199,262-268 and :258-282 against C 12.000 and C 177.60-178.20 (dark); the light half has no frame: OD-0098 (the Swift is the proof, the colours per theme asserted in TimelineMinimapAnimationAuditTests)
    public void The_dark_rail_is_the_frames_geometry_and_strengths_at_rest_and_expanded_and_hovered() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var previousTheme = ThemeManager.Current;
        ThemeManager.Current = AppTheme.Monokai; // the recorder's theme: the accent F92672
        // C 12.000: 16 long messages, the lit block, then 9 short ones (a message of 420 characters or more draws the full 22, one of 63 or fewer the narrowest).
        var entries = Enumerable.Range(0, 16).Select(i => Entry("long" + i, 1)).Append(Entry("lit", 1)).Concat(Enumerable.Range(0, 9).Select(i => Entry("short" + i, 0.15))).ToArray();
        var rail = new TimelineMinimapRail(entries, 400, "lit", (_, _) => { });
        var windowColour = new SKColor(38, 68, 59); // the frame's window beside the rail
        var window = new Avalonia.Controls.Window
        {
            Width = 80, Height = 400, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Background = new SolidColorBrush(Color.FromRgb(windowColour.Red, windowColour.Green, windowColour.Blue)), Content = rail,
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            // 26 ticks of 2 with 25 gaps of 8 are 252 high, centred in 400: the first starts at row 74 (:298-329).
            const int First = 74;
            using (var rest = Capture(window))
            {
                for (var i = 0; i < 26; i++)
                {
                    var (width, strength, colour) = Tick(rest, First + 10 * i, windowColour);
                    Assert.Equal(windowColour, rest.GetPixel(70, First + 10 * i)); // nothing else is drawn in the rail
                    Assert.Equal(windowColour, rest.GetPixel(30, First + 10 * i + 4)); // 2 high: the rows between ticks are the window's
                    if (i == 16) { Assert.Equal(26, width); Assert.InRange((int)colour.Red, 0xF9 - 3, 0xF9); Assert.InRange((int)colour.Green, 0x26 - 3, 0x26 + 3); Assert.InRange((int)colour.Blue, 0x72 - 3, 0x72 + 3); continue; } // the lit block is the accent F92672 at full strength
                    Assert.True(width >= (i < 16 ? 22 : 11) && width <= (i < 16 ? 22 : 12), $"tick {i}: {width} wide");
                    Assert.InRange(strength, 0.19, 0.235); // the frame's 0.19 to 0.235
                }
                Assert.Equal(windowColour, rest.GetPixel(30, First - 4)); // nothing above the first
            }
            // C 177.60-178.20: a pointer on one tick: the hovered one 26 wide and ink, every other 2 wider (the lit one stays 26) and lifted to 0.34.
            Hover(rail, "long3");
            Step(runtime, frames, 1000);
            // OD-0147: the frame's hovered tick is read beside the release preview card and its shadow; the shipped canvas card is the case below
            foreach (var card in Cards(rail)) SwiftGlass.SetEffect(card, SwiftGlass.GetEffect(card)!.WithLook(GlassLook.Release));
            Step(runtime, frames, 16);
            using (var hovered = Capture(window))
            {
                var (hoveredWidth, hoveredStrength, _) = Tick(hovered, First + 30, windowColour);
                Assert.Equal(26, hoveredWidth);
                Assert.InRange(hoveredStrength, 0.77, 0.83); // the frame's 0.79; the label colour at 0.95 is 0.81
                var (width, strength, _) = Tick(hovered, First + 10, windowColour);
                Assert.Equal(24, width); // 22 + 2
                Assert.InRange(strength, 0.32, 0.36); // the frame's 0.34 to 0.35
                var (shortWidth, shortStrength, _) = Tick(hovered, First + 10 * 20, windowColour);
                Assert.InRange(shortWidth, 13, 14); // 11.8 + 2
                Assert.InRange(shortStrength, 0.32, 0.36);
                Assert.Equal(26, Tick(hovered, First + 160, windowColour).Width); // the lit tick stays at its 26
            }
            // :195-199 the change is eased on .smooth(duration: 0.22): half way through, an unselected tick is between 0.25 and 0.4 of the label colour at the curve's place.
            Hover(rail, null);
            Step(runtime, frames, 1000);
            Hover(rail, "long3");
            Step(runtime, frames, 110);
            var middle = Animation.Smooth(0.22).Progress(0.11);
            Assert.InRange(middle, 0.05, 0.95);
            Assert.Equal(0.25 + (0.4 - 0.25) * middle, rail.GetLogicalDescendants().OfType<Button>().ElementAt(1).Opacity, 2);
            // The light theme has no frame (OD-0098: the Swift is the proof): the label colour of that theme at the same opacities, the accent on the lit tick.
            window.Content = null; window.Close();
            var lightRail = new TimelineMinimapRail(entries, 400, "lit", (_, _) => { });
            var lightWindow = new Avalonia.Controls.Window { Width = 80, Height = 400, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Content = lightRail };
            lightWindow.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var buttons = lightRail.GetLogicalDescendants().OfType<Button>().ToArray();
                var label = Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Light);
                Assert.NotEqual(Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Dark), label);
                Assert.Equal(label, Assert.IsType<SolidColorBrush>(buttons[1].Background).Color);
                Assert.Equal(0.25, buttons[1].Opacity);
                Assert.Equal(Chrome.Accent.Resolve(Avalonia.Styling.ThemeVariant.Light), Assert.IsType<SolidColorBrush>(buttons[16].Background).Color);
                Assert.Equal(1, buttons[16].Opacity);
                // :195-199 with reduced motion (or a live window resize) the change is instant: no curve, the expanded value at once.
                Marrow.UI.Port.EnvironmentValues.SetAccessibilityReduceMotion(lightRail, true);
                Hover(lightRail, "long3");
                Assert.Equal(0.4, buttons[1].Opacity);
                Assert.Equal(0.95, buttons[3].Opacity);
            }
            finally { lightWindow.Content = null; lightWindow.Close(); }
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); ThemeManager.Current = previousTheme; }
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the shipped canvas preview card (the canvas's popover glass, whose shadow falls on the rail): with it set aside the hovered tick is the frame's, 26 wide at 0.77 to 0.83
    public void Beside_the_canvas_preview_card_the_hovered_tick_is_the_frames_own() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var previousTheme = ThemeManager.Current;
        ThemeManager.Current = AppTheme.Monokai;
        var entries = Enumerable.Range(0, 16).Select(i => Entry("long" + i, 1)).Append(Entry("lit", 1)).Concat(Enumerable.Range(0, 9).Select(i => Entry("short" + i, 0.15))).ToArray();
        var rail = new TimelineMinimapRail(entries, 400, "lit", (_, _) => { });
        var windowColour = new SKColor(38, 68, 59);
        var window = new Avalonia.Controls.Window
        {
            Width = 80, Height = 400, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Background = new SolidColorBrush(Color.FromRgb(windowColour.Red, windowColour.Green, windowColour.Blue)), Content = rail,
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            const int First = 74;
            Hover(rail, "long3");
            Step(runtime, frames, 1000);
            var cards = Cards(rail);
            Assert.NotEmpty(cards);
            Assert.All(cards, card => Assert.Equal(GlassKind.Popover, GlassLooks.Of(SwiftGlass.GetEffect(card)!).Canvas!.Kind));
            foreach (var card in cards) card.IsVisible = false;
            Step(runtime, frames, 16);
            using var hovered = Capture(window);
            var (hoveredWidth, hoveredStrength, _) = Tick(hovered, First + 30, windowColour);
            Assert.Equal(26, hoveredWidth);
            Assert.InRange(hoveredStrength, 0.77, 0.83); // the frame's 0.79; the label colour at 0.95 is 0.81
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); ThemeManager.Current = previousTheme; }
    });
}
