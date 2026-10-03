using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Chrome;

[Collection("Headless")]
public sealed class ChromeVisualPickerReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-183")] // UI/Chrome/ChromeVisualPicker.swift:73,86-102
    [Trait("Req", "UI-THEME-053")] // UI/Chrome/ChromeVisualPicker.swift:76-102
    public void A_real_symbol_gets_title2_medium_and_selection_colors_without_changing_stage_geometry() => headless.OnUiThread(() =>
    {
        var symbol = SwiftImage.SystemName("sun.max").Font(SwiftFont.Body);
        var tile = new ChromeVisualTile("Light", false, 76, () => { }, () => symbol);
        var w = new Window { Width = 300, Height = 200, Content = tile };
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        tile.VerticalAlignment = VerticalAlignment.Top;
        w.Show(); Dispatcher.UIThread.RunJobs();
        var inner = tile.GetVisualDescendants().OfType<Border>().Single(b => b.Child == symbol);
        var ring = Assert.IsType<SwiftShapeBorder>(inner.Parent); // ED-181: the ring is `.strokeBorder` of a continuous rounded rectangle
        Assert.Equal(new Size(76,48), inner.DesiredSize);
        Assert.Equal(new Size(82,54), ring.DesiredSize);
        Assert.Equal(new CornerRadius(9), ring.CornerRadius);
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium), SwiftImage.GetFont(symbol));
        Assert.Equal(ChromeTokens.SecondaryText.Resolve(symbol.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground).Color);
        Assert.Equal(ChromeTokens.Overlay(.06).Resolve(inner.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(inner.Background).Color);
        tile.UpdateSelected(true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(82,54), ring.DesiredSize);
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.Equal(ChromeTokens.Accent.Resolve(symbol.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground).Color);
        Assert.Equal(ChromeTokens.Overlay(.10).Resolve(inner.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(inner.Background).Color);
        var caption = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Light");
        Assert.Equal(11, caption.FontSize); // SwiftFont's ED-091/107 Windows subheadline mapping
        Assert.Equal(76, caption.Width);
        Assert.Equal(2, caption.MaxLines);
        Assert.Equal(ChromeTokens.PrimaryText.Resolve(caption.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(caption.Foreground).Color);
        tile.UpdateSelected(false); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(82,54), ring.DesiredSize);
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // UI/Chrome/ChromeVisualPicker.swift:72-73
    public void Selection_is_exposed_to_automation_on_first_render_and_cleared_on_deselection() => headless.OnUiThread(() =>
    {
        var tile = new ChromeVisualTile("Light", true, 76, () => { }, () => SwiftImage.SystemName("sun.max"));
        Assert.Equal("selected", AutomationProperties.GetAutomationId(tile));
        var peer = ControlAutomationPeer.CreatePeerForElement(tile);
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer);
        Assert.True(selection.IsSelected);
        tile.UpdateSelected(false);
        Assert.False(selection.IsSelected);
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(tile)));
        tile.UpdateSelected(true);
        Assert.True(selection.IsSelected);
    });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // UI/Chrome/ChromeVisualPicker.swift:37-46,65
    [Trait("Req", "UI-THEME-052")] // UI/Chrome/ChromeVisualPicker.swift:37-49
    public void Picker_is_a_horizontal_top_aligned_row_with_12_spacing_and_tile_caption_spacing_6() => headless.OnUiThread(() =>
    {
        var selected = 1;
        var picker = new ChromeVisualPicker<int, SwiftImage>([(1,"A"),(2,"B")], new(() => selected, v => selected = v),
            _ => SwiftImage.SystemName("sun.max"));
        var w = new Window { Width=300, Height=200, Content=picker };
        w.Show(); Dispatcher.UIThread.RunJobs();
        var row = picker.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Orientation == Orientation.Horizontal);
        Assert.Equal(12, row.Spacing);
        Assert.Equal(VerticalAlignment.Top, row.VerticalAlignment);
        foreach (var tile in row.Children.Cast<ChromeVisualTile>())
            Assert.Equal(6, tile.GetVisualDescendants().OfType<StackPanel>().Single().Spacing);
        w.Close();
    });
}
