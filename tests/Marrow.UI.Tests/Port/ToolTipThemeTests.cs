using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PortControlThemes' ToolTip theme: `.help(...)` drawn as the macOS help tag (OD-0042 "Mac look"). OD-0045: every value is
// a SOURCE FACT (Apple design resource) from the "Apple macOS 27 UI Kit" (Sketch, Tooltips / Dark and Tooltips / Light,
// Inspect panel, read 2026-10-02; E:\Droppy\work\apple-kit\macos27-uikit-controls.md).
[Collection("Headless")]
public sealed class ToolTipThemeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public void A_help_tag_recreates_its_backdrop_blur_when_the_same_popup_changes_DPI() => headless.OnUiThread(() =>
    {
        var owner = HeadlessPopupHost.OverlayWindow(new Border()); owner.Show();
        var card = new Border { Width = 120, Height = 19, Margin = new Thickness(8) };
        var backdrop = new ToolTipBackdrop(card);
        var panel = new Panel { Children = { backdrop, card } };
        var popup = new Popup { PlacementTarget = owner, Child = panel };
        var root = HeadlessPopupHost.Open(popup, owner, panel);
        try
        {
            root.Measure(new Size(160, 60)); root.Arrange(new Rect(0, 0, 160, 60));
            typeof(ToolTipBackdrop).GetMethod("Synchronize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(backdrop, null);
            var field = typeof(ToolTipBackdrop).GetField("glass", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var first = Assert.IsType<PopoverGlass>(field.GetValue(backdrop));
            var scaling = typeof(AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", true)!;
            scaling.GetMethod("SetRenderScaling")!.Invoke(root.PlatformImpl, [1.5]);
            Dispatcher.UIThread.RunJobs(); root.UpdateLayout();
            typeof(ToolTipBackdrop).GetMethod("Synchronize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(backdrop, null);
            var next = Assert.IsType<PopoverGlass>(field.GetValue(backdrop));
            Assert.NotSame(first, next);
            Assert.Equal(67.5, typeof(PopoverGlass).GetField("tooltipSigma", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(next));
        }
        finally { root.Dispose(); owner.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public void A_failed_partial_help_tag_attachment_releases_its_owned_brush_exactly_once() => headless.OnUiThread(() =>
    {
        var window = new Window();
        var glass = PopoverGlass.ForToolTip(window, 45, 1);
        var brush = new ToolTipBrushLifetime();
        var field = typeof(PopoverGlass).GetField("ownBrush", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(glass, brush);
        glass.Dispose(); glass.Dispose();
        Assert.Equal(1, brush.Disposals);
        Assert.Null(field.GetValue(glass));
        window.Close();
    });

    [Theory]
    [InlineData("Dark", 1.0)]
    [InlineData("Light", 0.8)]
    [Trait("Decision", "ED-NEW-T45")]
    public void Help_tag_has_a_separate_45_point_backdrop_blur_under_its_unchanged_fill(string variant, double saturation) => headless.OnUiThread(() =>
    {
        var (window, tip) = Open(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            var material = Assert.Single(tip.GetVisualDescendants(), node => node.GetType().Name == "ToolTipBackdrop");
            Assert.Equal(45.0, material.GetType().GetProperty("Sigma")!.GetValue(material));
            Assert.Equal(saturation, material.GetType().GetProperty("Saturation")!.GetValue(material));
            Assert.False(((Control)material).IsHitTestVisible);
        }
        finally { window.Close(); }
    });

    private static (Window Window, ToolTip Tip) Open(ThemeVariant theme)
    {
        var target = new Border { Width = 100, Height = 40, Background = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        ToolTip.SetTip(target, "Edit message");
        var layers = new VisualLayerManager { EnableOverlayLayer = true, Child = target };
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(layers, true);
        var window = new Window { Content = layers, Width = 400, Height = 300, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ToolTip.SetIsOpen(target, true);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, Assert.Single(window.GetVisualDescendants().OfType<ToolTip>()));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-231")]
    public void A_help_tag_is_a_square_cornered_translucent_card_padded_6_by_3_around_11_point_medium_text(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var dark = theme == ThemeVariant.Dark;
        var (window, tip) = Open(theme);
        try
        {
            var card = tip.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Tag");
            // Frame: fits the text, padding 6 horizontal and 3 vertical, no corner radius in the Inspect panel.
            Assert.Equal(new Thickness(6, 3), card.Padding);
            Assert.Equal(default, card.CornerRadius);
            // Fill: Dark #262626@0.8, Light #E7E7E7@0.85.
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
            Assert.Equal((dark ? SwiftColor.Rgb(0x262626).Opacity(0.8) : SwiftColor.Rgb(0xE7E7E7).Opacity(0.85)).Resolve(theme), fill);
            // Text: SF Pro Medium 11 / 13; Dark Labels Dark Vibrant 1 Primary #FFFFFF@0.96, Light #4C4C4C.
            var text = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Edit message");
            Assert.Equal(11, text.FontSize);
            Assert.Equal(FontWeight.Medium, text.FontWeight);
            Assert.Equal(13, text.LineHeight);
            Assert.Equal((dark ? new SwiftColor(white: 1, opacity: 0.96) : SwiftColor.Rgb(0x4C4C4C)).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(13 + 6, card.Bounds.Height, 3);
            // Shadows: 0 0 2 #000000@0.4 and 0 2 8 #000000@0.2; Dark adds an inner 0.6 hairline #FFFFFF@0.15.
            var shadows = card.BoxShadow;
            Assert.Equal(dark ? 3 : 2, shadows.Count);
            Assert.Equal(new BoxShadow { Blur = 2, Color = new SwiftColor(white: 0, opacity: 0.4).Resolve(theme) }, shadows[0]);
            Assert.Equal(new BoxShadow { OffsetY = 2, Blur = 8, Color = new SwiftColor(white: 0, opacity: 0.2).Resolve(theme) }, shadows[1]);
            if (dark) Assert.Equal(new BoxShadow { IsInset = true, Spread = 0.6, Color = new SwiftColor(white: 1, opacity: 0.15).Resolve(theme) }, shadows[2]);
            // The card keeps room for its shadows inside the tip (a popup clips what falls outside it).
            Assert.Equal(new Thickness(8, 8, 8, 10), card.Margin);
        }
        finally { window.Close(); }
    });
}
