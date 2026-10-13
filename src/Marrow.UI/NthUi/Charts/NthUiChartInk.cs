// PORT-ONLY: web surfaces W16b (DESIGN.md 7.5 "monochrome line art with the accent only for the current value and focus,
// tabular numerals"; 10 "text at least 4.5:1, large text and controls 3:1"; ED-NEW-web-w16b-1). No Swift counterpart. How a
// chart is inked: one monochrome ramp for the series (told apart by tone, dash and marker, never by hue alone), hairlines for
// the grid, the accent for the point the reader is on, and the text of the axes and the readout in the thread's type ramp.
// Text is shaped once and kept: a chart redraws every frame while it moves, and shaping a label again each frame would be
// the whole cost.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.UI.NthUi.Charts;

/// <summary>The marker shapes of a scatter chart's series.</summary>
public enum NthUiMarker
{
    Circle,
    Square,
    Diamond,
    Triangle,
}

/// <summary>The tone, dash and marker of one series.</summary>
public sealed record NthUiSeriesInk(double Tone, double[]? Dash, NthUiMarker Marker);

/// <summary>The colours, metrics and fonts of a chart.</summary>
public static class NthUiChartInk
{
    /// <summary>A line's tones against the card: each keeps 3:1 (NthUiChartTests) in both themes.</summary>
    private static readonly double[] Tones = [1, .66, .5];

    private static readonly NthUiMarker[] Markers = [NthUiMarker.Circle, NthUiMarker.Square, NthUiMarker.Diamond, NthUiMarker.Triangle];

    /// <summary>The look of series <paramref name="index"/>: the first is the strongest and solid; later ones are paler and,
    /// from the third, dashed, with their own marker shape, so none depends on tone alone.</summary>
    public static NthUiSeriesInk Series(int index) =>
        new(Tones[index % Tones.Length], index % 3 == 2 ? [5, 3] : index >= 3 ? [1.5, 3] : null, Markers[index % Markers.Length]);

    /// <summary>The fill tone of slice or stack band <paramref name="index"/> of <paramref name="count"/>: from strong to pale.</summary>
    public static double FillTone(int index, int count) => count <= 1 ? .86 : .9 - .6 * index / (count - 1);

    public static SwiftColor Stroke(int series) => NthUiTokens.Strong.Opacity(Series(series).Tone);

    public static SwiftColor Fill(int series, int count) => NthUiTokens.Strong.Opacity(FillTone(series, count));

    /// <summary>A gridline (the card's hairline).</summary>
    public static SwiftColor Grid => NthUiTokens.Hairline;

    /// <summary>The baseline and the cursor: a little stronger than a gridline.</summary>
    public static SwiftColor Axis { get; } = SwiftColor.Dynamic(
        light: new SwiftColor(red: 0, green: 0, blue: 0, opacity: .22), dark: new SwiftColor(red: 1, green: 1, blue: 1, opacity: .24));

    /// <summary>The column or band under the pointer.</summary>
    public static SwiftColor Band => NthUiTokens.ControlFill;

    /// <summary>The hole of a marker: the card's own colour, so a hollow point reads as a ring.</summary>
    public static SwiftColor Face { get; } = SwiftColor.Dynamic(light: SwiftColor.Rgb(0xFAFAFB), dark: SwiftColor.Rgb(0x26262A));

    public static SwiftFont AxisFont { get; } = ChatFont.Chat(SwiftFont.TextStyle.Subheadline).MonospacedDigit();

    public static SwiftFont ReadoutFont { get; } = ChatFont.Chat(SwiftFont.TextStyle.Callout).MonospacedDigit();

    public static SwiftFont ReadoutStrong { get; } = ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Semibold).MonospacedDigit();

    public static SwiftFont CenterFont { get; } = ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold).MonospacedDigit();

    public static SwiftFont LabelFont { get; } = ChatFont.Chat(SwiftFont.TextStyle.Callout);

    /// <summary>The height of a plot at <paramref name="width"/>: half as high as wide, between 168 and 240.</summary>
    public static double PlotHeight(double width) => Math.Clamp(width * .5, 168, 240);

    /// <summary>A brush for <paramref name="color"/> in <paramref name="theme"/>.</summary>
    public static IBrush Brush(SwiftColor color, ThemeVariant theme) => new ImmutableSolidColorBrush(color.Resolve(theme));

    /// <summary>A pen for <paramref name="color"/> in <paramref name="theme"/>.</summary>
    public static IPen Pen(SwiftColor color, ThemeVariant theme, double thickness, double[]? dash = null, PenLineCap cap = PenLineCap.Round) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color.Resolve(theme)), thickness, dash is null ? null : new ImmutableDashStyle(dash, 0), cap, PenLineJoin.Round);
}

/// <summary>Shaped text kept between frames, by what it says, its font and its colour.</summary>
internal sealed class NthUiChartText
{
    private const int Limit = 600;

    private readonly Dictionary<(string Text, SwiftFont Font, Color Color), TextLayout> cache = [];

    /// <summary>The shaped <paramref name="text"/>.</summary>
    public TextLayout Get(string text, SwiftFont font, Color color)
    {
        if (cache.TryGetValue((text, font, color), out var layout)) return layout;
        if (cache.Count >= Limit) Clear();
        var resolved = font.Resolve();
        layout = new TextLayout(text, new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), resolved.FontSize,
            new ImmutableSolidColorBrush(color), fontFeatures: resolved.FontFeatures);
        cache[(text, font, color)] = layout;
        return layout;
    }

    public Size Measure(string text, SwiftFont font) => Measured(Get(text, font, Colors.Black));

    private static Size Measured(TextLayout layout) => new(Math.Ceiling(layout.WidthIncludingTrailingWhitespace), Math.Ceiling(layout.Height));

    public void Clear()
    {
        foreach (var layout in cache.Values) layout.Dispose();
        cache.Clear();
    }
}
