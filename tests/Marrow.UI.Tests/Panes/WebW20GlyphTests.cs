using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Panes;
using Marrow.UI.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-2")]
public sealed class WebW20GlyphTests(HeadlessSession headless)
{
    [Fact]
    public void Already_painted_live_answer_SVG_icons_follow_their_mutable_system_brush() => headless.OnUiThread(() =>
    {
        var surface = new Border { Background = Brushes.White };
        var glyph = NthUiText.Icon("check", 14, NthUiTokens.Text2);
        Assert.NotNull(glyph);
        surface.Child = glyph;
        var window = new Window { Width = 40, Height = 40, Content = surface, RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            _ = RenderedPixels.Capture(window);
            foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
            {
                EnvironmentValues.SetAccessibilityHighContrast(window, palette);
                ContrastSurface.MatchTheme(surface);
                surface.Background = palette.Brush(ContrastRole.Surface);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(palette.WindowText, Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
                var frame = RenderedPixels.Capture(window);
                var origin = glyph.TranslatePoint(default, window)!.Value;
                var levels = new List<int>();
                for (var y = 0; y < 14; y++)
                    for (var x = 0; x < 14; x++) levels.Add(frame.Level((int)origin.X + x, (int)origin.Y + y));
                Assert.True(palette.IsDark ? levels.Max() > 128 : levels.Min() < 128,
                    "the live-answer SVG stroke must actually repaint against the new surface");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Already_painted_header_glyphs_repaint_when_their_dynamic_system_brush_changes() => headless.OnUiThread(() =>
    {
        var surface = new Border { Background = Brushes.White };
        var glyph = new PaneGlyph(PaneGlyphKind.Close, 14) { Foreground = PaneTokens.Text2.Brush(surface) };
        surface.Child = glyph;
        var window = new Window { Width = 40, Height = 40, Content = surface, RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            _ = RenderedPixels.Capture(window);
            foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
            {
                EnvironmentValues.SetAccessibilityHighContrast(window, palette);
                ContrastSurface.MatchTheme(surface);
                surface.Background = palette.Brush(ContrastRole.Surface);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(palette.WindowText, Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
                var frame = RenderedPixels.Capture(window);
                var origin = glyph.TranslatePoint(default, window)!.Value;
                var levels = new List<int>();
                for (var y = 0; y < 14; y++)
                    for (var x = 0; x < 14; x++) levels.Add(frame.Level((int)origin.X + x, (int)origin.Y + y));
                Assert.True(palette.IsDark ? levels.Max() > 128 : levels.Min() < 128, "the system-coloured glyph must actually be painted against the new surface");
            }
        }
        finally { window.Close(); }
    });
}
