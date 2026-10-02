using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;
using UiChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Markdown;

// DroppyCode/UI/Markdown/LinkParagraphView.swift:485-489: a link wears Chrome.accentNSColor, the system accent for System,
// Light and Dark. On a PC whose Windows accent is black (the owner's), the link was black on the dark glass: "theming guide"
// and "issue 482" in main-thread-2-dark.png were dark grey and nearly invisible (and plain bold black in the light
// capture). The accent now reads on each appearance (SwiftColor, ED-238), and a link is drawn in it.
[Collection("Headless")]
public sealed class LinkAccentLegibilityTests(HeadlessSession headless)
{
    private static Color BrushColour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Contrast(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + 0.05) / (Math.Min(Luminance(a), Luminance(b)) + 0.05);

    private static Color LinkColourIn(ThemeVariant appearance)
    {
        var paragraph = new LinkParagraphView("go [theming guide](https://la-look.example/a) now", pointSize: 13);
        var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
        var window = new Window { Width = 400, Height = 200, Content = paragraph, RequestedThemeVariant = appearance };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        try { return BrushColour(block.Inlines!.OfType<Run>().Single(run => run.Text == "theming guide").Foreground); }
        finally { window.Close(); }
    }

    [Fact]
    [Trait("Req", "PR-THEME-140")] // LinkParagraphView.swift:485-489 linkTextAttributes
    [Trait("Decision", "ED-238")]
    public void A_link_reads_on_the_dark_window_when_the_system_accent_is_black() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        using var black = SwiftColor.AccentSource.Override(() => Colors.Black);
        try
        {
            ThemeManager.Current = AppTheme.System; // no accent of its own: the system accent
            var dark = LinkColourIn(ThemeVariant.Dark);
            Assert.True(Contrast(dark, Color.FromRgb(0x1E, 0x1E, 0x1E)) >= 3, $"link {dark} on the dark window");
            Assert.Equal(UiChrome.AccentNSColor.Resolve(ThemeVariant.Dark), dark); // Swift: the accent, not the label colour
            Assert.NotEqual(SwiftColor.LabelColor.Resolve(ThemeVariant.Dark), dark);

            var light = LinkColourIn(ThemeVariant.Light);
            Assert.Equal(UiChrome.AccentNSColor.Resolve(ThemeVariant.Light), light);
            Assert.True(Contrast(light, Colors.White) >= 3, $"link {light} on the light window");
        }
        finally { ThemeManager.Current = original; }
    });
}
