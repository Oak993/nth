using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// Settings > General, Token activity: the chip of the selected mode wears the tint, Chrome.accent, as its label and 0.16 of
// it as its fill (Components.swift:84-85 ChipButtonStyle isActive). On a PC whose Windows accent is black (the owner's) the
// selected "Daily" was black on the dark glass and its fill black at 0.16: unreadable (settings-general-dark.png,
// 2026-10-02). The accent now reads on each appearance (SwiftColor, ED-238).
[Collection("Headless")]
public sealed class TokenActivityChipLegibilityTests(HeadlessSession headless)
{
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

    private static (Color Label, Color Fill) SelectedChip(ThemeVariant appearance)
    {
        using var scope = TokenActivityTestScope.Create();
        var section = new TokenActivitySection();
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = section, RequestedThemeVariant = appearance };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var daily = section.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Daily");
            var border = Assert.IsType<Border>(daily.Content);
            var label = Assert.IsType<TextBlock>(border.Child);
            return (Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    [Trait("Req", "UI-USAGE-045")] // TokenActivitySection.swift:61-85 the mode chips; Components.swift:84-91
    [Trait("Decision", "ED-238")]
    public void The_selected_mode_chip_reads_on_the_dark_glass_when_the_system_accent_is_black() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        using var black = SwiftColor.AccentSource.Override(() => Colors.Black);
        try
        {
            ThemeManager.Current = AppTheme.System;
            var (label, fill) = SelectedChip(ThemeVariant.Dark);
            Assert.True(Contrast(label, Color.FromRgb(0x1E, 0x1E, 0x1E)) >= 3, $"label {label} on the dark window");
            Assert.Equal(Chrome.Accent.Resolve(ThemeVariant.Dark), label); // Swift: .tint
            Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(ThemeVariant.Dark), fill); // Swift: .tint.opacity(0.16)
            Assert.NotEqual(Color.FromArgb(41, 0, 0, 0), fill); // not black at 0.16, which the dark glass swallows

            var (lightLabel, lightFill) = SelectedChip(ThemeVariant.Light);
            Assert.Equal(Chrome.Accent.Resolve(ThemeVariant.Light), lightLabel);
            Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(ThemeVariant.Light), lightFill);
        }
        finally { ThemeManager.Current = original; }
    });
}
