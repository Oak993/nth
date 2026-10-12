// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The popover's small parts in the design's sizes: text in the three
// glass text tones, the round "btn" (26 pt, 13 pt corner, 12 pt medium, the control fill and its hover), the quiet link, the
// (i) explanation button, the colour dot and the reveal slot the Details drawer opens in.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Ignis.Views;

internal enum KindleTone { Primary, Secondary, Tertiary, Good, Accent }

internal static class KindleUi
{
    public static SwiftColor Color(KindleTone tone) => tone switch
    {
        KindleTone.Primary => Chrome.PrimaryText,
        KindleTone.Secondary => Chrome.PrimaryText.Opacity(.63),
        KindleTone.Tertiary => Chrome.PrimaryText.Opacity(.4),
        KindleTone.Good => SwiftColor.Dynamic(SwiftColor.Rgb(0x1c8f4e), SwiftColor.Rgb(0x3ad26a)),
        KindleTone.Accent => Chrome.Accent,
        _ => Chrome.PrimaryText,
    };

    public static SwiftColor ControlFill { get; } = SwiftColor.Dynamic(new SwiftColor(24 / 255.0, 34 / 255.0, 64 / 255.0, .065), new SwiftColor(1, .075));
    public static SwiftColor ControlHover { get; } = SwiftColor.Dynamic(new SwiftColor(24 / 255.0, 34 / 255.0, 64 / 255.0, .11), new SwiftColor(1, .14));
    public static SwiftColor TrackFill { get; } = SwiftColor.Dynamic(new SwiftColor(24 / 255.0, 34 / 255.0, 64 / 255.0, .1), new SwiftColor(1, .12));

    public static TextBlock Text(string text, double size = 12, KindleTone tone = KindleTone.Secondary, bool strong = false, bool tabular = false, bool wraps = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (tabular) block.FontFeatures = KindleCapsule.Tabular();
        SwiftFont.System(size, strong ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Apply(block);
        block.Foreground = Color(tone).Brush(block);
        return block;
    }

    public static Run Run(Control owner, string text, KindleTone tone, bool strong = false) => new(text)
    {
        Foreground = Color(tone).Brush(owner),
        FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
    };

    /// <summary>The design's ".btn": a pill of the control fill, hover fill, 45 % when disabled.</summary>
    public static Button Pill(Control content, Action action, double height = 26, double padding = 11, bool ghost = false)
    {
        var face = new Border { Height = height, Padding = new Thickness(padding, 0), CornerRadius = new CornerRadius(height / 2), Child = content };
        content.VerticalAlignment = VerticalAlignment.Center;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        var button = PlainControls.Button(face, action).KeyboardFocus();
        var drawnEnabled = true;
        void Paint()
        {
            var enabled = Marrow.UI.Port.EnabledLook.Of(button, ref drawnEnabled); // held through a chat switch's fade (ED-NEW-session-hotkeys)
            var fill = !enabled ? (ghost ? null : ControlFill) : button.IsPointerOver ? ControlHover : ghost ? null : ControlFill;
            face.Background = fill?.Brush(face) ?? Brushes.Transparent;
            button.Opacity = enabled ? 1 : .45;
        }
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsPointerOverProperty || Marrow.UI.Port.EnabledLook.Changes(e.Property)) Paint();
        };
        button.ActualThemeVariantChanged += (_, _) => Paint();
        Paint();
        return button;
    }

    public static Button Pill(string words, Action action, double height = 26, double size = 12, double padding = 11, bool ghost = false)
    {
        var text = Text(words, size, KindleTone.Primary);
        SwiftFont.System(size, SwiftFont.Weight.Medium).Apply(text);
        var button = Pill(text, action, height, padding, ghost);
        AutomationProperties.SetName(button, words);
        return button;
    }

    /// <summary>The quiet ".link": accent words, no fill.</summary>
    public static Button Link(string words, Action action)
    {
        var text = Text(words, 11.5, KindleTone.Accent);
        SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(text);
        var button = PlainControls.Button(text, action).KeyboardFocus(4);
        AutomationProperties.SetName(button, words);
        return button;
    }

    /// <summary>The (i): a 14 pt circled i that explains a number in one plain sentence (its tooltip); "What is {name}?".</summary>
    public static Button Info(string name, string tip)
    {
        var glyph = new Canvas { Width = 14, Height = 14 };
        var ring = new Ellipse { Width = 12, Height = 12, StrokeThickness = 1.15 };
        Canvas.SetLeft(ring, 1); Canvas.SetTop(ring, 1);
        var stem = new Line { StartPoint = new Point(7, 6.3), EndPoint = new Point(7, 9.9), StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round };
        var dot = new Ellipse { Width = 1.7, Height = 1.7 };
        Canvas.SetLeft(dot, 6.15); Canvas.SetTop(dot, 3.4);
        glyph.Children.Add(ring); glyph.Children.Add(stem); glyph.Children.Add(dot);
        var button = PlainControls.Button(glyph, () => { }).KeyboardFocus(7);
        button.Margin = new Thickness(3, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        void Paint()
        {
            var tone = button.IsPointerOver || ToolTip.GetIsOpen(button) ? KindleTone.Accent : KindleTone.Tertiary;
            var brush = Color(tone).Brush(button);
            ring.Stroke = brush; stem.Stroke = brush; dot.Fill = brush;
        }
        button.Click += (_, _) => ToolTip.SetIsOpen(button, !ToolTip.GetIsOpen(button));
        button.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsPointerOverProperty || e.Property == ToolTip.IsOpenProperty) Paint(); };
        button.ActualThemeVariantChanged += (_, _) => Paint();
        Paint();
        ToolTip.SetTip(button, tip);
        ToolTip.SetPlacement(button, PlacementMode.Top);
        AutomationProperties.SetName(button, Marrow.Core.Port.Ignis.IgnisWords.InfoAccessibleName(name));
        AutomationProperties.SetHelpText(button, tip);
        return button;
    }

    public static Ellipse Dot(SwiftColor color)
    {
        var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot.Fill = color.Brush(dot);
        return dot;
    }

    public static Border Divider()
    {
        var bar = new Border { Height = 1, Margin = new Thickness(0, 2) };
        bar.Background = TrackFill.Brush(bar);
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
        return bar;
    }
}

/// <summary>A slot whose height is its child's times <see cref="Reveal"/> (the Details drawer: 0 closed, 1 open), clipped while between.</summary>
internal sealed class RevealSlot : Decorator
{
    private double reveal;

    public double Reveal
    {
        get => reveal;
        set
        {
            reveal = Math.Clamp(value, 0, 1);
            ClipToBounds = reveal < 1;
            if (Child is { } child) { child.IsHitTestVisible = reveal > 0; child.Opacity = reveal <= 0 ? 0 : 1; }
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(child.DesiredSize.Width, child.DesiredSize.Height * reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child) child.Arrange(new Rect(0, 0, finalSize.Width, child.DesiredSize.Height));
        return finalSize;
    }
}
