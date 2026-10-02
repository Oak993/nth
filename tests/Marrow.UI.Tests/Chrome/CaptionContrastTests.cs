using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// OD-0068 (ED-244): the Windows caption buttons contrast with every theme. Each of the 26 themes, under a light and a
// dark Windows appearance (a theme with its own scheme keeps it; System follows Windows), draws the real caption cluster
// (WindowChrome's own layer and buttons) over the theme's real window backdrop (Chrome.WindowBackdrop: the app scrim at
// the transparency setting and the theme surface tint at 0.12), and the rendered pixels are measured:
//   the glyph at rest and hovered: at least 4.5:1 against the backdrop pixel beside it (WCAG AA for text);
//   pressed (the glyph at 70 %) and in an inactive window: at least 3:1 (WCAG non-text);
//   the hover rings (their colour at their own opacity over the measured backdrop): at least 3:1, except the close
//   button's dark rose, which is OD-0044's own colour and is recorded, not asserted (ED-244 UNRESOLVED).
// WORST CASE ASSUMED (ED-244): under the glass the window shows the desktop through Windows' own Acrylic, which the
// headless platform does not draw; it stands in as a flat grey as bright as rgb(112, 112, 112) under a dark theme and as
// dark as rgb(176, 176, 176) under a light one (the native gallery, over a mid-tone wallpaper, measured the window's own
// pixels at 18 to 75 dark and 210 to 242 light, with the scrim), at the transparency settings 0 (no scrim, the worst),
// 0.5 (the default) and 1 (the solid base). The scene draws at 4x so the 1.4-pt strokes have whole pixels of their colour.
[Collection("Headless")]
public sealed class CaptionContrastTests(HeadlessSession headless)
{
    private const int Zoom = 4;
    private const double SceneWidth = 200;
    private const double SceneHeight = 64;
    private static readonly Color DarkDesktop = Color.FromRgb(112, 112, 112);
    private static readonly Color LightDesktop = Color.FromRgb(176, 176, 176);
    private static readonly double[] Transparencies = [0, 0.5, 1];
    private const double HoverSeconds = 0.6;

    private const int MinButton = 8;
    private const int MaxButton = 9;
    private const int CloseButton = 20;

    // The pulse runs on the frame-clock seam (ED-117): the scene holds a manual frame source, clock and motion setting for
    // as long as it is shown, so every render of it (not only the hover's own) sees them.

    [Fact]
    [Trait("Decision", "ED-244")]
    public void Every_theme_in_both_appearances_contrasts_at_rest_hovered_pressed_and_in_an_inactive_window() => headless.OnUiThread(() =>
    {
        var renders = Environment.GetEnvironmentVariable("MARROW_CAPTION_RENDERS");
        if (renders is not null) Directory.CreateDirectory(renders);
        var failures = new List<string>();
        var table = new StringBuilder("| Theme | Windows | Scheme drawn | Rest (worst / default) | Hover | Pressed | Inactive | Ring | Close ring |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var theme in Enum.GetValues<AppTheme>())
            foreach (var os in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var variant = theme.Scheme() ?? os;
                var dark = variant == ThemeVariant.Dark;
                var results = new Dictionary<double, CaptionMeasured>();
                foreach (var transparency in Transparencies)
                {
                    var label = $"{theme}-{(os == ThemeVariant.Dark ? "dark" : "light")}";
                    var m = Measure(theme, variant, transparency, transparency == 0.5 ? renders : null, label);
                    results[transparency] = m;
                    var where = $"{theme} (Windows {os}, drawn {variant}) at transparency {transparency}";
                    if (m.Rest < 4.5) failures.Add($"{where}: glyph at rest {m.Rest:F2}:1 < 4.5");
                    if (m.Hover < 4.5) failures.Add($"{where}: glyph hovered {m.Hover:F2}:1 < 4.5");
                    if (m.Pressed < 3) failures.Add($"{where}: glyph pressed {m.Pressed:F2}:1 < 3");
                    if (m.Inactive < 3) failures.Add($"{where}: glyph in an inactive window {m.Inactive:F2}:1 < 3");
                    if (m.Ring < 3) failures.Add($"{where}: hover ring {m.Ring:F2}:1 < 3");
                    if (!dark && m.CloseRing < 3) failures.Add($"{where}: close ring {m.CloseRing:F2}:1 < 3");
                }
                static double Worst(Dictionary<double, CaptionMeasured> r, Func<CaptionMeasured, double> f) => r.Values.Min(f);
                table.Append(CultureInfo.InvariantCulture, $"| {theme} | {os} | {variant} | {Worst(results, m => m.Rest):F2} / {results[0.5].Rest:F2} | {Worst(results, m => m.Hover):F2} | {Worst(results, m => m.Pressed):F2} | {Worst(results, m => m.Inactive):F2} | {Worst(results, m => m.Ring):F2} | {Worst(results, m => m.CloseRing):F2} / {results[0.5].CloseRing:F2} |\n");
            }
        if (renders is not null) File.WriteAllText(System.IO.Path.Combine(renders, "contrast.md"), table.ToString());
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    });

    [Fact]
    [Trait("Decision", "ED-244")]
    public void A_theme_switch_draws_the_glyphs_again_in_the_new_appearance() => headless.OnUiThread(() =>
    {
        // The light gallery windows showed white glyphs: the buttons had drawn under the dark theme first and were never
        // drawn again when the appearance changed.
        var original = ThemeManager.Current;
        try
        {
            var scene = Show(AppTheme.Dark, ThemeVariant.Dark, 0.5);
            try
            {
                var before = Glyphs(scene);
                Assert.All(before, g => Assert.True(Luminance(g.Glyph) > Luminance(g.Backdrop), $"dark theme: expected a light glyph, got {g.Glyph} on {g.Backdrop}"));
                ThemeManager.Current = AppTheme.Light;
                scene.Window.Background = new SolidColorBrush(LightDesktop); // the desktop's Acrylic turns light with Windows' appearance
                scene.Window.RequestedThemeVariant = ThemeVariant.Light;
                Settle();
                foreach (var (glyph, backdrop) in Glyphs(scene))
                {
                    Assert.True(Luminance(glyph) < Luminance(backdrop), $"light theme: expected a dark glyph, got {glyph} on {backdrop}");
                    Assert.True(Contrast(glyph, backdrop) >= 4.5, $"light theme after the switch: {Contrast(glyph, backdrop):F2}:1");
                }
            }
            finally { scene.Dispose(); }
        }
        finally { ThemeManager.Current = original; }
    });

    [Fact]
    [Trait("Decision", "ED-244")]
    public void The_buttons_follow_the_window_s_activation() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            var scene = Show(AppTheme.Light, ThemeVariant.Light, 0.5);
            try
            {
                Assert.All(scene.Buttons, b => Assert.True(b.IsWindowActive));
                var active = Glyphs(scene);
                Deactivate(scene.Window);
                Settle();
                Assert.All(scene.Buttons, b => Assert.False(b.IsWindowActive));
                var inactive = Glyphs(scene);
                for (var i = 0; i < 3; i++)
                    Assert.True(Luminance(inactive[i].Glyph) > Luminance(active[i].Glyph) + 0.03, $"the inactive glyph is dimmer: {active[i].Glyph} -> {inactive[i].Glyph}");
                Activate(scene.Window);
                Settle();
                Assert.All(scene.Buttons, b => Assert.True(b.IsWindowActive));
            }
            finally { scene.Dispose(); }
        }
        finally { ThemeManager.Current = original; }
    });

    [Fact]
    [Trait("Decision", "ED-244")]
    public void The_colours_contrast_by_appearance_and_close_keeps_its_rose_deeper_on_light() => headless.OnUiThread(() =>
    {
        // Dark on light themes, light on dark themes.
        Assert.True(Luminance(CaptionButton.GlyphColor(dark: false, windowActive: true)) < 0.02);
        Assert.True(Luminance(CaptionButton.GlyphColor(dark: true, windowActive: true)) > 0.9);
        Assert.True(Luminance(CaptionButton.GlyphColor(dark: false, windowActive: false)) > Luminance(CaptionButton.GlyphColor(false, true)));
        Assert.True(Luminance(CaptionButton.GlyphColor(dark: true, windowActive: false)) < Luminance(CaptionButton.GlyphColor(true, true)));
        // OD-0044's close rose on dark, rgb(255, 107, 139) at 90 %; OD-0068's deeper rose on light, same hue family.
        Assert.Equal(Color.FromArgb(230, 255, 107, 139), CaptionButton.RingColor(CaptionButton.Kind.Close, dark: true));
        var deep = CaptionButton.RingColor(CaptionButton.Kind.Close, dark: false);
        Assert.Equal(Color.FromArgb(230, 176, 18, 62), deep);
        Assert.True(Luminance(deep) < Luminance(CaptionButton.RingColor(CaptionButton.Kind.Close, dark: true)) / 2);
        Assert.InRange(deep.ToHsl().H, 330, 355); // rose: the dark rose's own hue is about 346
        // The other rings: white at 65 % on dark (OD-0044), the dark glyph colour at 65 % on light.
        foreach (var kind in new[] { CaptionButton.Kind.Minimize, CaptionButton.Kind.Maximize })
        {
            Assert.Equal(Color.FromArgb(166, 255, 255, 255), CaptionButton.RingColor(kind, dark: true));
            var g = CaptionButton.GlyphColor(dark: false, windowActive: true);
            Assert.Equal(Color.FromArgb(166, g.R, g.G, g.B), CaptionButton.RingColor(kind, dark: false));
        }
        // And the hovered close button draws its deep rose on a light theme.
        var original = ThemeManager.Current;
        try
        {
            var scene = Show(AppTheme.Light, ThemeVariant.Light, 0.5);
            try
            {
                Hover(scene, CloseButton, 0.1);
                var close = scene.Buttons[2];
                var r = CaptionButton.Ring(0, 0.1)!.Value;
                var x = 23 - (CaptionButton.RingDiameter / 2 * r.Scale - CaptionButton.RingStroke / 2); // the stroke's centre, left of the glyph
                var at = close.TranslatePoint(new Point(x, 20), scene.Window)!.Value;
                var corner = close.TranslatePoint(new Point(3, 3), scene.Window)!.Value;
                var pixels = Pixels(scene.Window);
                var ring = pixels.At((int)Math.Round(at.X), (int)Math.Round(at.Y));
                var backdrop = pixels.At((int)corner.X, (int)corner.Y);
                var expected = Over(Color.FromArgb((byte)Math.Round(deep.A * r.Opacity / 0.9), deep.R, deep.G, deep.B), backdrop);
                Assert.True(Math.Abs(ring.R - expected.R) <= 10 && Math.Abs(ring.G - expected.G) <= 10 && Math.Abs(ring.B - expected.B) <= 10,
                    $"expected the deep rose ring {expected} over {backdrop}, got {ring}");
            }
            finally { scene.Dispose(); }
        }
        finally { ThemeManager.Current = original; }
    });

    [Fact]
    [Trait("Decision", "ED-244")]
    public void The_turn_pulse_s_sizes_motion_and_timing_are_unchanged()
    {
        Assert.Equal(46, CaptionButton.ButtonWidth);
        Assert.Equal(40, CaptionButton.ButtonHeight);
        Assert.Equal(12, CaptionButton.GlyphSize);
        Assert.Equal(1.4, CaptionButton.GlyphStroke);
        Assert.Equal(24, CaptionButton.RingDiameter);
        Assert.Equal(1.5, CaptionButton.RingStroke);
        Assert.Equal(1.3, CaptionButton.PulseSeconds);
        Assert.Equal(0.45, CaptionButton.SecondRingDelay);
        Assert.Equal(0.7, CaptionButton.PressedGlyphOpacity);
        Assert.Equal(0.35, CaptionButton.DisabledGlyphOpacity);
        Assert.Equal((0.45, 0.9), CaptionButton.Ring(0, 0)!.Value);
        var eased = CometEasing.Bezier(0.5, .2, .6, .3, 1).Value;
        Assert.Equal(0.45 + 1.1 * eased, CaptionButton.Ring(0, 0.65)!.Value.Scale, 6);
        Assert.Null(CaptionButton.Ring(1, 0.449));
        Assert.Equal(0.86, CaptionButton.GlyphScale(0.12 * 1.3), 6);
        Assert.Equal(1.06, CaptionButton.GlyphScale(0.30 * 1.3), 6);
    }

    // ---- the scene ----

    private CaptionMeasured Measure(AppTheme theme, ThemeVariant variant, double transparency, string? renders, string label)
    {
        var original = ThemeManager.Current;
        try
        {
            var scene = Show(theme, variant, transparency);
            try
            {
                var rest = Glyphs(scene);
                Save(scene, renders, $"{label}-rest.png");
                var restMin = rest.Min(g => Contrast(g.Glyph, g.Backdrop));
                var ring = double.MaxValue;
                var closeRing = double.MaxValue;
                var hover = double.MaxValue;
                foreach (var (code, index) in new[] { (MinButton, 0), (MaxButton, 1), (CloseButton, 2) })
                {
                    Hover(scene, code, HoverSeconds);
                    if (code == MinButton) Save(scene, renders, $"{label}-hover-minimise.png");
                    if (code == CloseButton) Save(scene, renders, $"{label}-hover-close.png");
                    var g = Glyphs(scene)[index];
                    hover = Math.Min(hover, Contrast(g.Glyph, g.Backdrop));
                    var colour = CaptionButton.RingColor(scene.Buttons[index].ButtonKind, variant == ThemeVariant.Dark);
                    var ringContrast = Contrast(Over(colour, g.Backdrop), g.Backdrop);
                    if (code == CloseButton) closeRing = ringContrast;
                    else ring = Math.Min(ring, ringContrast);
                    WindowChrome.SetCaptionHover(scene.Window, 0);
                    Settle();
                }
                var pressed = double.MaxValue;
                foreach (var (code, index) in new[] { (MinButton, 0), (MaxButton, 1), (CloseButton, 2) })
                {
                    WindowChrome.SetCaptionPressed(scene.Window, code);
                    Settle();
                    var g = Glyphs(scene)[index];
                    pressed = Math.Min(pressed, Contrast(g.Glyph, g.Backdrop));
                }
                WindowChrome.SetCaptionPressed(scene.Window, 0);
                Deactivate(scene.Window);
                Settle();
                var inactive = Glyphs(scene).Min(g => Contrast(g.Glyph, g.Backdrop));
                Save(scene, renders, $"{label}-inactive.png");
                return new CaptionMeasured(restMin, hover, pressed, inactive, ring, closeRing);
            }
            finally { scene.Dispose(); }
        }
        finally { ThemeManager.Current = original; }
    }

    private static CaptionScene Show(AppTheme theme, ThemeVariant variant, double transparency)
    {
        ThemeManager.Current = theme; // the backdrop's tint reads the theme surface when it is built
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        IDisposable[] seams =
        [
            FrameClock.Source.Override(frames),
            SwiftClock.Provider.Override(time),
            Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false)),
        ];
        var window = new Window
        {
            Width = SceneWidth * Zoom,
            Height = SceneHeight * Zoom,
            RequestedThemeVariant = variant,
            // The desktop through the Acrylic, at the worst case assumed above (the headless window draws no Acrylic).
            Background = new SolidColorBrush(variant == ThemeVariant.Dark ? DarkDesktop : LightDesktop),
        };
        var root = new Panel { Width = SceneWidth, Height = SceneHeight, Children = { new WindowBackdrop(transparency), WindowChrome.CaptionLayer(window) } };
        window.Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Zoom, Zoom), Child = root };
        window.Show();
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
        var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
        var scene = new CaptionScene(window, frames, time, [miniaturize, zoom, close], seams);
        Settle();
        return scene;
    }

    private static void Hover(CaptionScene scene, int hitTest, double seconds)
    {
        WindowChrome.SetCaptionHover(scene.Window, hitTest);
        scene.Time.Advance(TimeSpan.FromSeconds(seconds));
        scene.Frames.Advance(TimeSpan.FromSeconds(seconds));
        Settle();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Deactivate(Window window) =>
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);

    private static void Activate(Window window) =>
        typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);

    private static void Save(CaptionScene scene, string? dir, string name)
    {
        if (dir is null) return;
        using var frame = scene.Window.CaptureRenderedFrame()!;
        frame.Save(System.IO.Path.Combine(dir, name), new PngBitmapEncoderOptions());
    }

    // Each button's glyph (the pixel within its 12-pt box, plus the tick's room, that contrasts most with the backdrop)
    // and the backdrop beside it (the button's own top-left corner, which neither the glyph nor a ring reaches).
    private static (Color Glyph, Color Backdrop)[] Glyphs(CaptionScene scene)
    {
        var pixels = Pixels(scene.Window);
        return scene.Buttons.Select(button =>
        {
            var corner = button.TranslatePoint(new Point(3, 3), scene.Window)!.Value;
            var backdrop = pixels.At((int)corner.X, (int)corner.Y);
            var from = button.TranslatePoint(new Point(23 - 8, 20 - 8), scene.Window)!.Value;
            var to = button.TranslatePoint(new Point(23 + 8, 20 + 8), scene.Window)!.Value;
            var best = backdrop;
            var bestContrast = 1.0;
            for (var y = (int)from.Y; y < (int)to.Y; y++)
                for (var x = (int)from.X; x < (int)to.X; x++)
                {
                    var c = pixels.At(x, y);
                    var k = Contrast(c, backdrop);
                    if (k > bestContrast) { bestContrast = k; best = c; }
                }
            return (best, backdrop);
        }).ToArray();
    }

    private static CaptionPixelGrid Pixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var format = frame.Format ?? Avalonia.Platform.PixelFormat.Rgba8888;
        var stride = frame.PixelSize.Width * 4;
        var bytes = new byte[stride * frame.PixelSize.Height];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(frame.PixelSize), pin.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { pin.Free(); }
        return new CaptionPixelGrid(bytes, stride, format == Avalonia.Platform.PixelFormat.Rgba8888);
    }

    // ---- WCAG 2 contrast ----

    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
