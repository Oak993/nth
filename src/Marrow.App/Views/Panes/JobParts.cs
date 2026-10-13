using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W5, the small parts of a Background tasks card (E:/Droppy/work/design/web-surfaces/index.html: .tcard,
// .ops, .prog, .m; DESIGN.md 3.10, 3.12 "cards are tinted fills inside the pane", 10 "text at least 4.5:1"; ED-NEW-web-w5-1).
// No Swift counterpart. The colours are the canvas's dark values with their mirror on nth's light window.

namespace Marrow.App.Views.Panes;

/// <summary>The colours of a task card.</summary>
public static class JobTokens
{
    private static SwiftColor Ink(double opacity) => new(red: 0, green: 0, blue: 0, opacity: opacity);

    private static SwiftColor Paper(double opacity) => new(red: 1, green: 1, blue: 1, opacity: opacity);

    /// <summary>The card's tint (canvas .tcard: white .045).</summary>
    public static SwiftColor Fill { get; } = SwiftColor.Dynamic(light: Ink(.045), dark: Paper(.045));

    /// <summary>A finished card recedes: a fainter tint (the canvas dims the whole card to .55; here only the card does, so its words keep 4.5:1).</summary>
    public static SwiftColor DoneFill { get; } = SwiftColor.Dynamic(light: Ink(.022), dark: Paper(.022));

    /// <summary>The card's hairline (canvas .tcard: inset .5px white .07).</summary>
    public static SwiftColor Rim { get; } = SwiftColor.Dynamic(light: Ink(.07), dark: Paper(.07));

    /// <summary>A control's outline (canvas --line2: white .14).</summary>
    public static SwiftColor Outline { get; } = SwiftColor.Dynamic(light: Ink(.16), dark: Paper(.14));

    /// <summary>A control under the pointer (canvas --fill2).</summary>
    public static SwiftColor Hover { get; } = PaneTokens.HoverFill;

    /// <summary>The progress track (canvas --fill2).</summary>
    public static SwiftColor Track { get; } = SwiftColor.Dynamic(light: Ink(.10), dark: Paper(.10));

    public static SwiftColor BarFrom { get; } = SwiftColor.Dynamic(light: new SwiftColor(red: 0.30, green: 0.30, blue: 0.34), dark: new SwiftColor(red: 216 / 255.0, green: 216 / 255.0, blue: 222 / 255.0));

    public static SwiftColor BarTo { get; } = SwiftColor.Dynamic(light: new SwiftColor(red: 0.11, green: 0.11, blue: 0.13), dark: new SwiftColor(red: 1, green: 1, blue: 1));

    // ---- legibility (DESIGN 10: text 4.5:1) ----

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    /// <summary>WCAG contrast of <paramref name="text"/> (any alpha) on the opaque <paramref name="background"/>.</summary>
    public static double Contrast(Color text, Color background)
    {
        var high = Math.Max(Luminance(Over(text, background)), Luminance(background));
        var low = Math.Min(Luminance(Over(text, background)), Luminance(background));
        return (high + 0.05) / (low + 0.05);
    }

    /// <summary>
    /// A running task's card as it nominally looks: the window's background (macOS 27 UI Kit: dark #1E1E1E, light white), the pane card's tint
    /// at its worst stop for words (the lightest in dark, the darkest in light), and the task card's tint on top.
    /// </summary>
    public static Color NominalCard(ThemeVariant theme, bool finished = false)
    {
        var dark = theme == ThemeVariant.Dark;
        var window = dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;
        var stops = ((LinearGradientBrush)PaneTokens.CardFill(theme, expanded: false)).GradientStops.Select(stop => stop.Color).OrderBy(Luminance).ToList();
        var pane = Over(dark ? stops[^1] : stops[0], window);
        return Over((finished ? DoneFill : Fill).Resolve(theme), pane);
    }

    /// <summary>
    /// The colour of a card's text actions ("View transcript", "Open preview"): nth's accent, moved toward white in dark and black in light
    /// until it reads at 4.5:1 on the card (an accent the person picked may be darker or paler than that).
    /// </summary>
    public static Color ActionColor(ThemeVariant? theme)
    {
        theme = theme == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return ActionColor(theme, Marrow.UI.Chrome.Chrome.Accent.Resolve(theme));
    }

    /// <summary>The same for any accent.</summary>
    public static Color ActionColor(ThemeVariant theme, Color accent)
    {
        var card = NominalCard(theme);
        var toward = theme == ThemeVariant.Dark ? Colors.White : Colors.Black;
        for (var step = 0; step <= 20; step++)
        {
            var t = step / 20.0;
            var lifted = Color.FromRgb(
                (byte)Math.Round(accent.R + (toward.R - accent.R) * t),
                (byte)Math.Round(accent.G + (toward.G - accent.G) * t),
                (byte)Math.Round(accent.B + (toward.B - accent.B) * t));
            if (Contrast(lifted, card) >= 4.5) return lifted;
        }
        return toward;
    }

    public const double CardRadius = 11;

    public const double ButtonSize = 24;

    public const double ButtonRadius = 7;

    public const double GlyphSize = 11;

    public const double CardGap = 6;

    public const double ListPadding = 8;

    public const double RowGap = 2;
}

public enum JobGlyphKind
{
    Stop,
    Pause,
    Play,
}

/// <summary>The canvas's stop, pause and play glyphs (#i-stop, #i-pause, #i-play), in the inherited text colour.</summary>
public sealed class JobGlyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<JobGlyph>();

    public static readonly StyledProperty<JobGlyphKind> KindProperty = AvaloniaProperty.Register<JobGlyph, JobGlyphKind>(nameof(Kind));

    private const double Box = 24;
    private const double Stroke = 1.6;

    private static readonly Geometry StopShape = StreamGeometry.Parse("M8.6 7h6.8A1.6 1.6 0 0 1 17 8.6v6.8a1.6 1.6 0 0 1-1.6 1.6H8.6A1.6 1.6 0 0 1 7 15.4V8.6A1.6 1.6 0 0 1 8.6 7z");
    private static readonly Geometry PauseShape = StreamGeometry.Parse("M9 6.5v11M15 6.5v11");
    private static readonly Geometry PlayShape = StreamGeometry.Parse("M8 5.5l10.5 6.5L8 18.5z");

    static JobGlyph() => AffectsRender<JobGlyph>(ForegroundProperty, KindProperty);

    public JobGlyph()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Width = Height = JobTokens.GlyphSize;
    }

    public JobGlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Width, Height);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush || Bounds.Width <= 0) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Box;
        var immutable = brush.ToImmutable();
        var pen = new ImmutablePen(immutable, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - Box * scale) / 2, (Bounds.Height - Box * scale) / 2)))
        {
            switch (Kind)
            {
                case JobGlyphKind.Stop: context.DrawGeometry(immutable, pen, StopShape); break;
                case JobGlyphKind.Play: context.DrawGeometry(immutable, pen, PlayShape); break;
                default: context.DrawGeometry(null, pen, PauseShape); break;
            }
        }
    }
}

/// <summary>A 3 DIP progress line (canvas .prog): drawn, so a new value repaints and lays out nothing.</summary>
public sealed class ProgressLine : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ProgressLine, double>(nameof(Value));

    static ProgressLine() => AffectsRender<ProgressLine>(ValueProperty);

    public ProgressLine()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Height = 3;
    }

    /// <summary>0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1));
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 3);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0) return;
        var theme = ActualThemeVariant;
        context.DrawRectangle(new ImmutableSolidColorBrush(JobTokens.Track.Resolve(theme)), null, new Rect(Bounds.Size), 1.5, 1.5);
        var width = Bounds.Width * Value;
        if (width <= 0) return;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(JobTokens.BarFrom.Resolve(theme), 0), new GradientStop(JobTokens.BarTo.Resolve(theme), 1)],
        };
        context.DrawRectangle(fill, null, new Rect(0, 0, width, Bounds.Height), 1.5, 1.5);
    }
}

/// <summary>
/// The metrics line of a card (canvas .m: a wrapping flex row, 10 DIP between items, 2 DIP between lines). Every item but the
/// last is a fixed-width live value. The last item (the activity) takes what is left of the first line, or its own line when
/// less than <see cref="MinFlex"/> is left, and is trimmed to that width, so its words never decide where anything wraps:
/// the layout depends on the width offered and on which items are shown, never on a number or a word.
/// </summary>
public sealed class MetricsRow : Panel
{
    public const double ColumnGap = 10;
    public const double MinFlex = 72;

    private readonly List<Rect> plan = [];

    private double Plan(double width)
    {
        plan.Clear();
        var shown = Children.Where(child => child.IsVisible).ToList();
        double x = 0, y = 0, lineHeight = 0;
        for (var i = 0; i < shown.Count; i++)
        {
            var child = shown[i];
            var flex = i == shown.Count - 1 && child is TextBlock;
            if (!flex)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > width + 0.01)
                {
                    y += lineHeight + JobTokens.RowGap;
                    x = 0;
                    lineHeight = 0;
                }
                plan.Add(new Rect(x, y, size.Width, size.Height));
                x += size.Width + ColumnGap;
                lineHeight = Math.Max(lineHeight, size.Height);
                continue;
            }
            var room = width - x;
            if (x > 0 && room < MinFlex)
            {
                y += lineHeight + JobTokens.RowGap;
                x = 0;
                lineHeight = 0;
                room = width;
            }
            room = Math.Max(0, room);
            child.Measure(new Size(room, double.PositiveInfinity));
            var height = child.DesiredSize.Height;
            plan.Add(new Rect(x, y, room, height));
            lineHeight = Math.Max(lineHeight, height);
        }
        return shown.Count == 0 ? 0 : y + lineHeight;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 10000 : availableSize.Width;
        var height = Plan(width);
        var used = plan.Count == 0 ? 0 : plan.Max(rect => rect.Right);
        return new Size(double.IsInfinity(availableSize.Width) ? used : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Plan(finalSize.Width);
        var shown = Children.Where(child => child.IsVisible).ToList();
        for (var i = 0; i < shown.Count && i < plan.Count; i++) shown[i].Arrange(plan[i]);
        return finalSize;
    }
}
