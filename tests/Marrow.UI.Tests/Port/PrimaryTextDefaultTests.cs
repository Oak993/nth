using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SwiftUI draws a Text with no foreground style of its own in `.primary` (labelColor, SwiftColor.LabelColor per
// ED-051), following the appearance. PortControlThemes gives every TopLevel that default (PortControlThemes.cs,
// RegisterPrimaryTextDefault); before it, such text was Avalonia's opaque black on the dark window.
[Collection("Headless")]
public sealed class PrimaryTextDefaultTests(HeadlessSession headless)
{
    private static Color Drawn(TextBlock text) => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;

    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned defaults (no theme package)
    [Trait("Decision", "ED-051")] // labelColor's literals
    public void Text_with_no_colour_of_its_own_is_label_colour_in_each_appearance_and_follows_a_switch() => headless.OnUiThread(() =>
    {
        var plain = new TextBlock { Text = "continue" };
        var window = new Window { Width = 200, Height = 100, RequestedThemeVariant = ThemeVariant.Dark, Content = new StackPanel { Children = { plain } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Dark), Drawn(plain)); // white 0.85, not black
            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Light), Drawn(plain)); // black 0.85
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_colour_a_site_sets_is_kept() => headless.OnUiThread(() =>
    {
        var red = new TextBlock { Text = "x", Foreground = Brushes.Red };
        var inherited = new TextBlock { Text = "y" };
        var parent = new StackPanel { Children = { inherited } };
        Avalonia.Controls.Documents.TextElement.SetForeground(parent, Brushes.Blue);
        var window = new Window { Width = 200, Height = 100, RequestedThemeVariant = ThemeVariant.Dark, Content = new StackPanel { Children = { red, parent } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(Colors.Red, Drawn(red));
            Assert.Equal(Colors.Blue, Drawn(inherited)); // a container's colour still reaches its texts
        }
        finally { window.Close(); }
    });
}
