// PORT-ONLY: first-release feature unit SK-15 (DL-17). The small pieces every control of the pages is made of: text, tiles, colours, motion.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal static class LibraryUi
{
    /// <summary>The frame-clock curves of the pages (OD-0076: hover about 0.1 s, a change of state about 0.2 s, a size about 0.3 s).</summary>
    public static Animation Fade => Animation.EaseOut(duration: 0.2);

    public static Animation Glide => Animation.EaseOut(duration: 0.3);

    /// <summary>Whether movement is stilled: no frame source is bound, or the system's or the window's reduce-motion setting is on (OD-0076; the transition then lands at once).</summary>
    public static bool Still(Control owner) =>
        !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(owner) || ChromeTokens.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    /// <summary>The animation to give a change, or null when movement is stilled.</summary>
    public static Animation? Ease(Control owner, Animation curve) => Still(owner) ? null : curve;

    public static TextBlock Text(string text, double size, bool secondary = false, SwiftFont.Weight? weight = null, bool trim = false, bool wrap = false)
    {
        var block = new TextBlock { Text = text };
        SwiftFont.System(size, weight).Apply(block);
        block.Foreground = (secondary ? ChromeTokens.SecondaryText : ChromeTokens.PrimaryText).Brush(block);
        if (trim) block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        return block;
    }

    /// <summary>The family of the pages' code text.</summary>
    public static FontFamily MonoFamily { get; } = new("Cascadia Mono, Consolas, monospace");

    public static TextBlock Mono(string text, double size)
    {
        var block = new TextBlock { Text = text, FontFamily = MonoFamily, FontSize = size };
        block.Foreground = ChromeTokens.PrimaryText.Brush(block);
        block.TextWrapping = TextWrapping.NoWrap;
        return block;
    }

    /// <summary>A glyph of the symbol catalogue (Lucide behind it, OD-0010) at <paramref name="size"/>, in <paramref name="color"/>.</summary>
    public static SwiftImage Glyph(string symbol, double size, SwiftColor color, SwiftFont.Weight weight = SwiftFont.Weight.Regular) =>
        SwiftImage.SystemName(symbol).Font(SwiftFont.System(size, weight)).ForegroundStyle(color);

    /// <summary>The rounded tile a set's glyph sits in (the design's .ig: 22 in a row, 48 in the detail).</summary>
    public static Border Tile(string symbol, double side, double radius)
    {
        var tile = new Border { Width = side, Height = side, Child = Glyph(symbol, side * 0.5, ChromeTokens.Accent), VerticalAlignment = VerticalAlignment.Center }
            .RoundedRectangle(radius, RoundedCornerStyle.Continuous);
        if (tile.Child is Control glyph) { glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center; }
        tile.Background = ChromeTokens.Accent.Opacity(0.16).Brush(tile);
        return tile;
    }

    /// <summary>A panel in the design's tint (--ov6): the cost box, the details, the dialog's lists.</summary>
    public static Border Panel(Control child, double radius = 10, double tint = 0.06, Thickness? padding = null)
    {
        var panel = new Border { Child = child, Padding = padding ?? new Thickness(12, 9, 12, 10) }.RoundedRectangle(radius, RoundedCornerStyle.Continuous);
        panel.Background = ChromeTokens.Overlay(tint).Brush(panel);
        return panel;
    }

    // The unthemed app owns its real vertical content presenter (SettingsView's SettingsScrollTemplate): a ScrollViewer made here carries the same.
    private static readonly FuncControlTemplate<ScrollViewer> ScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter { Background = Brushes.Transparent, Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] };
        presenter.GestureRecognizers.Add(new Avalonia.Input.GestureRecognizers.ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });

    /// <summary>A vertical scroll area in the app's soft, weighted scrolling (OD-0071), with no bar drawn: the content scrolls under the wheel, a touch and the keys.</summary>
    public static ScrollViewer Scroller(Control content, double? maxHeight = null)
    {
        var scroll = new ScrollViewer
        {
            Template = ScrollTemplate, Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        if (maxHeight is { } max) scroll.MaxHeight = max;
        SoftScroll.SetIsEnabled(scroll, true);
        return scroll;
    }

    public static T Named<T>(this T control, string name, string? id = null) where T : Control
    {
        AutomationProperties.SetName(control, name);
        if (id is not null) AutomationProperties.SetAutomationId(control, id);
        return control;
    }

    public static string? IdOf(Control control) => AutomationProperties.GetAutomationId(control);
}
