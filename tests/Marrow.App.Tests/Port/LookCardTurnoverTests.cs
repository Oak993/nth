using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088, OD-0076 (ED-251): the Settings window turns over when the Theme control changes the appearance, as the locked
// board's scenarios 08 and 09 do (SPEC 6.5): every window colour eases 0.32 s, CSS ease, and nothing changes in the click's own
// frame. Proved on the real Settings window (WindowManager, the manual frame clock), from its rendered frames at 60 Hz: the
// click frame is the frame before it, no part of the window takes more than half of its change in one frame (but the Theme
// control and the detail lines, which slide and crossfade by design), and sampled colours follow the curve.
[Collection("Headless")]
public sealed class LookCardTurnoverTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const double FrameSeconds = 1.0 / 60;

    // A rendered frame of the window: its bytes (RGBA), size and row stride.
    private static (byte[] Pixels, int Width, int Height, int Stride) Capture(HostWindow window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return (bytes, buffer.Size.Width, buffer.Size.Height, buffer.RowBytes);
    }

    private static int At((byte[] Pixels, int Width, int Height, int Stride) frame, int x, int y, int channel) =>
        frame.Pixels[y * frame.Stride + x * 4 + channel];

    private static void Step(SettingsRiseRig rig, HostWindow window, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            rig.Frame(FrameSeconds);
            Drain(window);
        }
    }

    private void Open(AppTheme theme, Action<SettingsRiseRig, HostWindow, LookCard, AppModel> check, bool reduceMotion = false) =>
        WithManager((h, manager) =>
        {
            using var rig = new SettingsRiseRig { Accepts = false };
            using var accent = SwiftColor.AccentSource.Override(() => Color.Parse("#0067C0"));
            manager.Model.Settings.Theme = theme;
            manager.SettingsCloak = rig.Cloak;
            manager.ShowSettings(SettingsPage.Appearance);
            Dispatcher.UIThread.RunJobs();
            var window = Settings(manager);
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            Drain(window);
            Step(rig, window, 60);
            check(rig, window, Nodes<LookCard>(View(manager)).Single(), manager.Model);
        });

    private static double Ease(int k) => CssCurve.At(CssCurve.Ease, k * 1000.0 / 60, 320);

    private static Rect Box(Control control, Visual to) => new(control.TranslatePoint(default, to)!.Value, control.Bounds.Size);

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [Trait("Decision", "ED-251")]
    public void The_theme_control_turns_the_whole_settings_window_over_in_point_32_seconds_and_nothing_cuts(string appearance) =>
        Open(appearance == "dark" ? AppTheme.Dark : AppTheme.Light, (rig, window, card, model) =>
        {
            var dark = appearance == "dark";
            var title = Nodes<TextBlock>(card.ThemeRow).Single(t => t.Text == "Theme");
            var captions = window.GetVisualDescendants().OfType<CaptionButton>().ToArray();
            Assert.Equal(3, captions.Length);
            // Moving by design (SPEC 6.6, 6.7): the control's pill and labels, and the two crossfading detail lines.
            var moving = new[] { Box(card.Segments, window).Inflate(3), Box(card.ThemeDetail, window).Inflate(2), Box(card.AccentDetail, window).Inflate(2) };
            var pre = Capture(window);
            var titleFrom = ((ISolidColorBrush)title.Foreground!).Color;

            Click(window, card.Segments.Buttons[dark ? 1 : 2]); // scenario 08: Light on a dark window (light board: Dark)
            Assert.Equal(dark ? AppTheme.Light : AppTheme.Dark, model.Settings.Theme);
            var frames = new List<(byte[] Pixels, int Width, int Height, int Stride)> { Capture(window) };
            var titles = new List<Color> { ((ISolidColorBrush)title.Foreground!).Color };
            var darkness = new List<double[]> { captions.Select(c => c.Darkness).ToArray() };
            for (var k = 1; k <= 24; k++)
            {
                Step(rig, window);
                frames.Add(Capture(window));
                titles.Add(((ISolidColorBrush)title.Foreground!).Color);
                darkness.Add(captions.Select(c => c.Darkness).ToArray());
            }
            Step(rig, window, 40);
            var end = Capture(window);
            var titleTo = ((ISolidColorBrush)title.Foreground!).Color;
            Assert.NotEqual(titleFrom, titleTo);

            // The click frame draws what the frame before it drew: no pixel of the window has moved (a cut changes them all).
            var still = 0;
            for (var y = 0; y < pre.Height; y++)
                for (var x = 0; x < pre.Width; x++)
                    for (var c = 0; c < 4; c++)
                        still = Math.Max(still, Math.Abs(At(frames[0], x, y, c) - At(pre, x, y, c)));
            Assert.True(still <= 2, $"the click frame differs from the frame before by {still} levels");

            // No frame takes a part of the window more than half of its way at once, outside what moves by design.
            var changed = 0;
            var total = new int[pre.Width, pre.Height];
            for (var y = 0; y < pre.Height; y++)
                for (var x = 0; x < pre.Width; x++)
                {
                    var d = 0;
                    for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(end, x, y, c) - At(pre, x, y, c)));
                    total[x, y] = d;
                    if (d > 8) changed++;
                }
            Assert.True(changed > pre.Width * pre.Height / 2, $"only {changed} pixels turned over");
            for (var k = 0; k < frames.Count; k++)
            {
                var before = k == 0 ? pre : frames[k - 1];
                var jumps = 0;
                for (var y = 0; y < pre.Height; y++)
                    for (var x = 0; x < pre.Width; x++)
                    {
                        if (total[x, y] <= 8 || moving.Any(r => r.Contains(new Point(x + 0.5, y + 0.5)))) continue;
                        var d = 0;
                        for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(frames[k], x, y, c) - At(before, x, y, c)));
                        if (d > total[x, y] / 2) jumps++;
                    }
                Assert.True(jumps <= changed / 100, $"frame {k}: {jumps} of {changed} turning pixels took over half their change at once");
            }

            // Sampled colours follow the curve: the window's own backdrop and sheet (top of the detail pane, clear of words),
            // the Look card's fill (below its Accent row's words) and the mini window's surface.
            var cardFill = card.TranslatePoint(new Point(40, card.Bounds.Height - 4), window)!.Value;
            var miniSurface = card.Mini.TranslatePoint(new Point(118, 124), window)!.Value;
            foreach (var (name, point) in new[] { ("window", new Point(450, 8)), ("card fill", cardFill), ("mini surface", miniSurface) })
            {
                var (px, py) = ((int)point.X, (int)point.Y);
                for (var k = 0; k < frames.Count; k++)
                    for (var c = 0; c < 4; c++)
                    {
                        var from = At(pre, px, py, c);
                        var to = At(end, px, py, c);
                        var expected = from + (to - from) * Ease(k);
                        var seen = At(frames[k], px, py, c);
                        Assert.True(Math.Abs(seen - expected) <= 0.03 * Math.Abs(to - from) + 1.5, $"{name} ({px}, {py}) channel {c} frame {k}: {seen}, curve {expected:F1} ({from} to {to})");
                    }
            }

            // The row title's colour (a ChromeRow's SwiftColor brush) and the caption glyphs, frame by frame on the curve.
            for (var k = 0; k < titles.Count; k++)
            {
                var p = Ease(k);
                var expected = CssCurve.Mix((titleFrom.R, titleFrom.G, titleFrom.B, titleFrom.A / 255.0), (titleTo.R, titleTo.G, titleTo.B, titleTo.A / 255.0), p);
                var seen = titles[k];
                Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.A / 255.0 - expected.A) <= 1.0 / 255 + 1e-9, $"title frame {k}: {seen}, curve {expected}");
                foreach (var value in darkness[k])
                    Assert.Equal(dark ? 1 - p : p, value, 3);
            }
        });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void Under_reduce_motion_the_window_takes_the_new_appearance_at_once() => Open(AppTheme.Dark, (rig, window, card, model) =>
    {
        Click(window, card.Segments.Buttons[1]);
        var now = Capture(window);
        Step(rig, window, 40);
        var end = Capture(window);
        var most = 0;
        for (var i = 0; i < now.Pixels.Length; i++) most = Math.Max(most, Math.Abs(now.Pixels[i] - end.Pixels[i]));
        Assert.True(most <= 2, $"the click frame is {most} levels from the end");
        Assert.All(window.GetVisualDescendants().OfType<CaptionButton>(), c => Assert.Equal(0, c.Darkness));
    }, reduceMotion: true);

    // ED-251: the window's wash is Chrome.glassTint as it is now (the source's WindowBackdrop :263-265, 12 %), so a named theme
    // washes the window with its surface, easing 0.32 s from the last theme's.
    [Fact]
    [Trait("Decision", "ED-251")]
    public void A_named_theme_washes_the_window_with_its_surface_easing_in() => Open(AppTheme.Dark, (rig, window, card, model) =>
    {
        var backdrop = Nodes<SettingsView>((Control)window.Content!).Single().RiseBackdrop;
        var tint = (Border)((Panel)backdrop.Child!).Children[1];
        var from = ((ISolidColorBrush)tint.Background!).Color;
        Assert.Equal(AppTheme.Dark.Spec().Surface.Opacity(0.12).Resolve(ThemeVariant.Dark), from);
        var to = SwiftColor.Hex("282A36").Opacity(0.12).Resolve(ThemeVariant.Dark); // Dracula's surface
        model.Settings.Theme = AppTheme.Dracula;
        Drain(window);
        for (var k = 0; k <= 22; k++)
        {
            if (k > 0) Step(rig, window);
            var expected = CssCurve.Mix((from.R, from.G, from.B, from.A / 255.0), (to.R, to.G, to.B, to.A / 255.0), Ease(k));
            var seen = ((ISolidColorBrush)tint.Background!).Color;
            Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.B - expected.B) <= 1, $"frame {k}: {seen}, curve {expected}");
        }
        Assert.Equal(to, ((ISolidColorBrush)tint.Background!).Color);
    });
}
