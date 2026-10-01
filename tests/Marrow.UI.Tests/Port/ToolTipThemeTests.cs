using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PortControlThemes' ToolTip theme (PORT-ONLY, proposal ED-229): a `.help(...)` tip is drawn on a card, never as bare
// text floating over the content under it.
[Collection("Headless")]
public sealed class ToolTipThemeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-107")] // (5) the shared registration point
    public void A_help_tip_is_drawn_on_an_opaque_card_with_its_text() => headless.OnUiThread(() =>
    {
        var target = new Border { Width = 100, Height = 40, Background = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        ToolTip.SetTip(target, "Edit message");
        var layers = new VisualLayerManager { EnableOverlayLayer = true, Child = target };
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(layers, true);
        var window = new Window { Content = layers, Width = 400, Height = 300 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            ToolTip.SetIsOpen(target, true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var tip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>());
            var card = Assert.IsType<Border>(Assert.Single(tip.GetVisualChildren()));
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background);
            Assert.Equal(255, fill.Color.A);
            Assert.Equal(new CornerRadius(4), card.CornerRadius);
            Assert.Contains(tip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Edit message" && text.FontSize == 12);
        }
        finally { window.Close(); }
    });
}
