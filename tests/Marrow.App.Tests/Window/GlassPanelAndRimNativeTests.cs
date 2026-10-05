using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;
using SwiftChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-glass-g3 on real windows and real screen pixels (BitBlt with CAPTUREBLT: what the user sees), under native.lock.
//   - Part A and B: the head card (400 x 267, r 22; HydraPanel.swift:99-121: the regular glass in its shape washed with the theme tint at 0.22, over
//     the black 0.3 scrim that casts black 1.0 at radius 28, y 10) in an opaque window over a backdrop. With E:\Droppy\work\scratch\glass-g3\calib\
//     card18_backdrop.png present (frame 01 where frame 18's card is, levelled; prep.py), that is the backdrop, else a flat 56 grey. The window is
//     grabbed without the card, with it, and with the card as the port drew it before (the scrim's BoxShadow at the colour's full alpha, outside the
//     box only): the shadow is 1 - with / without, the Mac video's method (video_shadow.py), and is held to the video's to 0.025.
//   - Part C: the real main window (WindowManager's, over the system Acrylic) holding the real WindowBackdrop at transparency 0 (frame 01's), over a
//     window that shows frame 01's own wallpaper when it is present: the edge it draws, with WindowRim and without it.
// The PNGs go to E:\Droppy\work\scratch\glass-g3\native; compare.py puts each under its Mac frame.
[Collection("WindowManagerNative")]
public sealed class GlassPanelAndRimNativeTests(WindowManagerNativeSession native, Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Evidence = @"E:\Droppy\work\scratch\glass-g3\native";
    private const string Calibration = @"E:\Droppy\work\scratch\glass-g3\calib";
    private const double CardX = 60, CardY = 60, CardW = 400, CardH = 267, ShotW = 520, ShotH = 347;

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    private static Control Backdrop(string file, double width, double height, Color flat)
    {
        if (File.Exists(file))
        {
            var image = new Image { Source = new Bitmap(file), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
            return image;
        }
        return new Border { Width = width, Height = height, Background = new SolidColorBrush(flat) };
    }

    // The head card's surface as HydraPanel.cs builds it (the strip and stage aside): the caster, the scrim, the clipped glass with the tint.
    private static Grid Card(bool before)
    {
        var scrim = new Border { CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)) };
        var glass = new Border { CornerRadius = new CornerRadius(22), ClipToBounds = true }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        glass.Background = SwiftChrome.GlassTint.Opacity(0.22).Brush(glass);
        var card = new Grid
        {
            Width = CardW, Height = CardH, Margin = new Thickness(CardX, CardY, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        if (before) scrim.BoxShadow = SwiftShadow.BoxShadows(Colors.Black, 28, y: 10);
        else card.Children.Add(new SwiftShadow.Caster(scrim, Colors.Black, 28, y: 10));
        card.Children.Add(scrim);
        card.Children.Add(glass);
        return card;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g3")]
    [Trait("Req", "PR-HYDRA-229")] // HydraPanel.swift:116-120; SubagentPanel.swift:351-354; the lead's E rulings, finding B, item 3
    public void On_a_real_window_the_head_cards_shadow_is_the_mac_videos_and_shows_through_its_scrim() => native.Run(subject =>
    {
        Directory.CreateDirectory(Evidence);
        var previous = native.OnUi(() => ThemeManager.Current);
        native.OnUi(() => ThemeManager.Current = AppTheme.Monokai); // frame 18's theme
        try
        {
            var (window, host, scale) = native.OnUi(() =>
            {
                subject.Settings.Hide();
                subject.Main.Hide();
                var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
                var work = screen.WorkingArea;
                var host = new Panel { Children = { Backdrop(Path.Combine(Calibration, "card18_backdrop.png"), ShotW, ShotH, Color.FromRgb(56, 56, 56)) } };
                var window = new AvaloniaWindow
                {
                    WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(work.X + 60, work.Y + 60),
                    Width = ShotW, Height = ShotH, Background = Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Content = host,
                };
                Themeless(window);
                native.Register(window);
                window.Show();
                return (window, host, screen.Scaling);
            });
            ScreenGrab Grab(string? name)
            {
                Thread.Sleep(900);
                var origin = native.OnUi(() => host.PointToScreen(new Point(0, 0)));
                var grab = ScreenGrab.Of(origin.X, origin.Y, (int)Math.Round(ShotW * scale), (int)Math.Round(ShotH * scale));
                if (name is not null) grab.SavePng(Path.Combine(Evidence, name));
                return grab;
            }
            var without = Grab("card18-backdrop.png");
            native.OnUi(() => host.Children.Add(Card(before: true)));
            var before = Grab("card18-before.png");
            native.OnUi(() => { host.Children.RemoveAt(1); host.Children.Add(Card(before: false)); });
            var with = Grab("card18-port.png");

            // 1 - with / without, averaged along the card's top edge (d pt above) and its right edge (d pt beside), in DIPs.
            double Shadow(ScreenGrab g, bool above, int d)
            {
                double sum = 0, n = 0;
                if (above)
                    for (var x = CardX + 120; x < CardX + 280; x++)
                    {
                        int px = (int)(x * scale), py = (int)((CardY - d + 0.5) * scale);
                        sum += 1 - g.Luma(px, py) / Math.Max(1, without.Luma(px, py)); n++;
                    }
                else
                    for (var y = CardY + 60; y < CardY + 180; y++)
                    {
                        int px = (int)((CardX + CardW + d - 0.5) * scale), py = (int)(y * scale);
                        sum += 1 - g.Luma(px, py) / Math.Max(1, without.Luma(px, py)); n++;
                    }
                return sum / n;
            }
            (int D, double Mac)[] above = [(2, 0.089), (4, 0.084), (8, 0.069), (14, 0.048), (20, 0.024), (30, 0.009), (46, -0.002)];
            (int D, double Mac)[] beside = [(2, 0.146), (4, 0.141), (8, 0.123), (14, 0.109), (20, 0.084), (30, 0.037), (46, 0.028)];
            var lines = new List<string>();
            var failures = new List<string>();
            foreach (var (d, mac) in above)
            {
                var (port, old) = (Shadow(with, true, d), Shadow(before, true, d));
                lines.Add($"above {d}: Mac {mac:F3}, port {port:F3}, before {old:F3}");
                // 0.025 on the real window, where the GPU draws the nominal sigma 28 (SwiftShadowNativeTests) and the video's own fit above the card
                // reads sigma 24 to 28 (finding B): at 20 pt above that range alone is 0.011 (headless, whose raster draws 27.2, holds 0.02).
                if (Math.Abs(port - mac) > 0.025) failures.Add(lines[^1]);
            }
            foreach (var (d, mac) in beside)
            {
                var (port, old) = (Shadow(with, false, d), Shadow(before, false, d));
                lines.Add($"beside {d}: Mac {mac:F3}, port {port:F3}, before {old:F3}");
                if (Math.Abs(port - mac) > 0.025) failures.Add(lines[^1]);
            }
            // Through the scrim: the shadow under it darkens the glass's backdrop, so the card's body is darker than the card drawn before.
            double Body(ScreenGrab g)
            {
                double sum = 0, n = 0;
                for (var y = CardY + 46; y < CardY + 96; y++)
                    for (var x = CardX + 210; x < CardX + 380; x += 2) { sum += g.Luma((int)(x * scale), (int)(y * scale)); n++; }
                return sum / n;
            }
            var (bodyWith, bodyBefore, bodyUnder) = (Body(with), Body(before), Body(without));
            lines.Add($"body (card rows 46-96, x 210-380): port {bodyWith:F1}, before {bodyBefore:F1}, backdrop {bodyUnder:F1}; Mac frame 18 43.4 over 55.0");
            File.WriteAllText(Path.Combine(Evidence, "card18-shadow.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
            output.WriteLine(string.Join("; ", lines));
            Assert.True(failures.Count == 0, string.Join("; ", failures));
            Assert.True(Shadow(before, false, 2) > 0.35, $"the instrument: the BoxShadow drew the colour's full alpha beside the card ({Shadow(before, false, 2):F3})");
            Assert.True(bodyWith < bodyBefore - 1.0, $"the shadow under the scrim darkens the body: {bodyWith:F1} against {bodyBefore:F1} before");
            GC.KeepAlive(window);
        }
        finally { native.OnUi(() => ThemeManager.Current = previous); }
    }, factType: typeof(GlassPanelAndRimNativeTests));

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g3")]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:251-259 the window glass; OD-0107 (rim effects with no screen capture over the Acrylic), OD-0005
    public void On_the_real_main_window_over_the_acrylic_the_window_glass_draws_the_mac_frames_rim() => native.Run(subject =>
    {
        Directory.CreateDirectory(Evidence);
        var frame01 = Path.Combine(Calibration, "window01_wallpaper.png");
        var (behind, backdrop, scale) = native.OnUi(() =>
        {
            subject.Settings.Hide();
            var main = subject.Main;
            var content = subject.MainContent;
            content.Background = null;
            foreach (var child in content.Children) child.IsVisible = false; // the fixture's caption zones
            var backdrop = new WindowBackdrop(0) { ZIndex = -1 };
            content.Children.Add(backdrop);
            main.Topmost = true;
            var screen = main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            var behind = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(main.Position.X - 60, main.Position.Y - 60),
                Width = main.Width + 120, Height = main.Height + 120, Background = Brushes.Black,
                Content = Backdrop(frame01, main.Width + 120, main.Height + 120, Color.FromRgb(40, 70, 110)),
            };
            Themeless(behind);
            native.Register(behind);
            behind.Show();
            main.Activate();
            return (behind, backdrop, screen.Scaling);
        });
        var (w, h) = native.OnUi(() => ((int)Math.Round(backdrop.Bounds.Width * scale), (int)Math.Round(backdrop.Bounds.Height * scale)));
        ScreenGrab Grab(string name)
        {
            Thread.Sleep(1200);
            var origin = native.OnUi(() => backdrop.PointToScreen(new Point(0, 0)));
            var grab = ScreenGrab.Of(origin.X - 8, origin.Y - 8, w + 16, h + 16);
            grab.SavePng(Path.Combine(Evidence, name));
            return grab;
        }
        // The system's frame line over the client's first row (WindowRim.TopCover): it reads black with the rim and without it.
        double Row(ScreenGrab g, int y) => Enumerable.Range(8 + w / 3, w / 3).Average(x => g.Luma(x, y));
        // White's alpha over the body at the n-th physical pixel inside an edge (8 px of margin around the client area; along the top from the
        // first row the system leaves), the body 10 pt in.
        double Edge(ScreenGrab g, string side, int n, int covered)
        {
            double At(int i)
            {
                double sum = 0; var count = 0;
                if (side is "top" or "bottom")
                    for (var x = 8 + w / 3; x < 8 + 2 * w / 3; x++) { sum += g.Luma(x, side == "top" ? 8 + covered + i : 8 + h - 1 - i); count++; }
                else
                    for (var y = 8 + h / 3; y < 8 + 2 * h / 3; y++) { sum += g.Luma(side == "left" ? 8 + i : 8 + w - 1 - i, y); count++; }
                return sum / count;
            }
            var body = At((int)(10 * scale));
            return (At(n) - body) / (255 - body);
        }
        var lines = new List<string>();
        var failures = new List<string>();
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
        {
            var tag = theme == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light";
            native.OnUi(() =>
            {
                subject.Main.RequestedThemeVariant = theme;
                foreach (var rim in backdrop.GetVisualDescendants().OfType<WindowRim>()) rim.IsVisible = true;
            });
            var with = Grab($"window-port-{tag}.png");
            native.OnUi(() => { foreach (var rim in backdrop.GetVisualDescendants().OfType<WindowRim>()) rim.IsVisible = false; });
            var before = Grab($"window-before-{tag}.png");
            var covered = Row(before, 8) < 12 && Row(with, 8) < 12 ? 1 : 0;
            foreach (var side in new[] { "top", "bottom", "left", "right" })
                lines.Add($"{tag} {side}: port {Edge(with, side, 0, covered):F3} {Edge(with, side, 1, covered):F3} {Edge(with, side, 2, covered):F3}; before {Edge(before, side, 0, covered):F3} {Edge(before, side, 1, covered):F3}");
            lines.Add($"{tag}: the client's first row under the system's frame line: {covered} px (luma {Row(with, 8):F1} with the rim, {Row(before, 8):F1} without)");
            if (tag == "dark")
            {
                // Mac frame 01 is the dark appearance.
                foreach (var (side, mac) in new[] { ("top", 0.235), ("bottom", 0.21), ("left", 0.21) })
                    if (Math.Abs(Edge(with, side, 0, covered) - mac) > 0.05) failures.Add($"dark {side} first pixel {Edge(with, side, 0, covered):F3}, Mac {mac:F3}");
                if (Math.Abs(Edge(with, "top", 1, covered) - 0.042) > 0.02) failures.Add($"dark top second pixel {Edge(with, "top", 1, covered):F3}, Mac 0.042");
            }
            if (!(Edge(with, "top", 0, covered) > Edge(before, "top", 0, covered) + 0.06))
                failures.Add($"{tag}: the rim is not on the screen: {Edge(with, "top", 0, covered):F3} with it, {Edge(before, "top", 0, covered):F3} without");
        }
        lines.Add("Mac frame 01 (dark): top 0.235 0.042 0.016; bottom 0.21 0.023; left 0.21 0.008; right 0.158 (cut by the frame)");
        File.WriteAllText(Path.Combine(Evidence, "window-rim.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
        output.WriteLine(string.Join("; ", lines));
        Assert.True(failures.Count == 0, string.Join("; ", failures) + " | " + string.Join("; ", lines));
        GC.KeepAlive(behind);
    }, factType: typeof(GlassPanelAndRimNativeTests));
}
