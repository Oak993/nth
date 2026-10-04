using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// OD-0099 Q-TEXT A (owner, 2026-10-04): text is drawn greyscale everywhere, no subpixel (ClearType) fringes, on every surface;
// the macOS source draws all its text greyscale. Avalonia 12.1.2 asks Skia for subpixel text unless TextOptions.TextRenderingMode
// says otherwise, and only gets it where the layer is opaque, so the proof is a render over an opaque neutral backdrop: every
// pixel of text in a neutral colour must have R == G == B. PortControlThemes.RegisterTextOptions sets the mode for every
// TopLevel (ED-NEW-text-greyscale).
[Collection("Headless")]
public sealed class GreyscaleTextRenderTests(HeadlessSession headless)
{
    private static readonly Color Backdrop = Color.FromRgb(40, 40, 40);

    private static void Drain()
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static (byte[] Bytes, int Width) Capture(Window window)
    {
        Drain();
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        return (bytes, size.Width);
    }

    /// <summary>The pixels in <paramref name="area"/> that differ from the backdrop (text ink), and how many of those have
    /// unequal channels (a colour fringe). R and B swap places between pixel formats; equality does not care.</summary>
    private static (int Ink, int Fringed) Fringes(Window window, Rect area)
    {
        var (bytes, width) = Capture(window);
        int ink = 0, fringed = 0;
        for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
            for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            {
                var p = (y * width + x) * 4;
                byte r = bytes[p], g = bytes[p + 1], b = bytes[p + 2];
                if (r == Backdrop.R && g == Backdrop.G && b == Backdrop.B) continue;
                ink++;
                if (r != g || g != b) fringed++;
            }
        return (ink, fringed);
    }

    private static Rect Area(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("not in the window");
        return new Rect(origin, control.Bounds.Size);
    }

    private static Window Show(params Control[] content)
    {
        var panel = new StackPanel { Background = new SolidColorBrush(Backdrop), Spacing = 8 };
        foreach (var control in content) panel.Children.Add(control);
        var window = new Window { Width = 360, Height = 260, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Backdrop), Content = panel };
        window.Show();
        return window;
    }

    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned defaults, PortControlThemes (ED-NEW-text-greyscale, OD-0099 Q-TEXT A)
    public void A_sidebar_row_title_has_no_colour_fringes() => headless.OnUiThread(() =>
    {
        var row = new SidebarRow("Refactor the session store", () => { }, () => new Border { Width = 14, Height = 14 }, _ => new Border(), isSelected: false);
        var window = Show(row);
        try
        {
            Drain();
            var title = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Refactor the session store");
            var (ink, fringed) = Fringes(window, Area(window, title));
            Assert.True(ink > 40, $"the title drew {ink} pixels of ink");
            Assert.Equal(0, fringed);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_chat_paragraph_has_no_colour_fringes() => headless.OnUiThread(() =>
    {
        var markdown = new MarkdownView("The quick brown fox jumps over the lazy dog, then sorts the **session list** by recency.") { Width = 320 };
        var window = Show(markdown);
        try
        {
            Drain();
            var (ink, fringed) = Fringes(window, Area(window, markdown));
            Assert.True(ink > 200, $"the paragraph drew {ink} pixels of ink");
            Assert.Equal(0, fringed);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_text_on_a_glass_pill_has_no_colour_fringes() => headless.OnUiThread(() =>
    {
        var label = new TextBlock { Text = "Check now", Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pill = new Border { Width = 120, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Child = label };
        SwiftGlass.SetEffect(pill, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)));
        var window = Show(pill);
        try
        {
            var (bytes, width) = Capture(window);
            var area = Area(window, label);
            // The glass frosts the flat backdrop to a flat tint; ink is whatever is brighter than the pill's own fill beside the label.
            var fill = bytes[((int)area.Top - 2) * width * 4 + ((int)area.Left - 2) * 4];
            int ink = 0, fringed = 0;
            for (var y = (int)area.Top; y < (int)Math.Ceiling(area.Bottom); y++)
                for (var x = (int)area.Left; x < (int)Math.Ceiling(area.Right); x++)
                {
                    var p = (y * width + x) * 4;
                    if (bytes[p] != bytes[p + 1] || bytes[p + 1] != bytes[p + 2]) fringed++;
                    if (bytes[p] > fill + 24) ink++;
                }
            Assert.True(ink > 20, $"the label drew {ink} pixels of ink over the glass");
            Assert.Equal(0, fringed);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Every_top_level_gets_greyscale_antialiasing_and_no_hinting_before_its_first_frame() => headless.OnUiThread(() =>
    {
        var first = new Window { Width = 100, Height = 60, Content = new TextBlock { Text = "a" } };
        var second = new Window { Width = 100, Height = 60, Content = new TextBlock { Text = "b" } };
        first.Show(); second.Show();
        try
        {
            foreach (var window in new[] { first, second })
            {
                Assert.Equal(TextRenderingMode.Antialias, TextOptions.GetTextRenderingMode(window));
                Assert.Equal(TextHintingMode.None, TextOptions.GetTextHintingMode(window));
            }
        }
        finally { first.Close(); second.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void The_hinting_mode_the_style_sets_reaches_the_rasterizer() => headless.OnUiThread(() =>
    {
        // Same text, same window; only the hinting differs. If the rasterizer ignored the mode, the two renders would match.
        int[] Render(TextHintingMode mode)
        {
            var text = new TextBlock { Text = "Hamburgefonstiv 0123456789 The quick brown fox", Foreground = Brushes.White, FontSize = 13 };
            var window = Show(text);
            try
            {
                TextOptions.SetTextHintingMode(window, mode);
                var (bytes, width) = Capture(window);
                var area = Area(window, text);
                var values = new List<int>();
                for (var y = (int)area.Top; y < (int)Math.Ceiling(area.Bottom); y++)
                    for (var x = (int)area.Left; x < (int)Math.Ceiling(area.Right); x++) values.Add(bytes[(y * width + x) * 4]);
                return values.ToArray();
            }
            finally { window.Close(); }
        }
        Assert.NotEqual(Render(TextHintingMode.None), Render(TextHintingMode.Strong));
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_window_asking_for_subpixel_text_draws_fringes_so_the_fringe_check_can_fail() => headless.OnUiThread(() =>
    {
        // Sensitivity: the same render with the mode switched to SubpixelAntialias on the text must show fringes here,
        // or the proofs above would pass whatever the app does.
        var text = new TextBlock { Text = "The quick brown fox jumps over the lazy dog", Foreground = Brushes.White, FontSize = 13 };
        var window = Show(text);
        try
        {
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.SubpixelAntialias); // after the style set Antialias on it
            Drain();
            var (ink, fringed) = Fringes(window, Area(window, text));
            Assert.True(ink > 100);
            Assert.True(fringed > 0, "subpixel text drew no colour fringe in this headless render, so the proofs above cannot see one");
        }
        finally { window.Close(); }
    });
}
