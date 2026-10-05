// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.3, V-12.9, V-12.10, V-12.12): one word of the pill's label, drawn.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>One run of the label's text and the weight it is drawn at.</summary>
internal readonly record struct VoicePillRun(string Text, int Weight);

/// <summary>
/// The pill's label for one state (voice-composer.html <c>.pl</c>): the shown words at weight 600 in <c>pgLab</c> and, once the language has been heard (V-12.10), an en space and its name at weight 400, in
/// the same layout, so one text sweep runs through all of it. The sweep (V-12.3) is the design's <c>background-clip: text</c> gradient: a 105 degree gradient 260 % as wide as the text
/// (<c>pgLab</c> to 38 %, <c>pgLabHi</c> at 49 %, <c>pgLab</c> from 60 %) moving as <see cref="Sweep"/>, the position in the design's terms (1.35 to -0.55); it is drawn as the text in <c>pgLab</c> with
/// the same text in <c>pgLabHi</c> over it through that gradient as an opacity mask, so only glyph pixels are ever painted and a glyph's foot is never cut (V-12.12: the box is the font's
/// own ascent to descent). Text that is not shown is not in the layout at all (the page's hidden chip was painted through the sweep and showed its dot).
/// </summary>
internal sealed class VoicePillLabel : Control
{
    private static readonly string EnSpace = " "; // U+2002

    private double fontSize = VoicePillGeometry.FontSize(VoiceListeningStyle.Pill);
    private string words = "";
    private string? languageName;
    private double nameOpacity = 1;
    private string? outgoingName;
    private double outgoingOpacity;
    private double sweep = 1.35;
    private TextLayout? baseLayout, highLayout, outgoingLayout;
    private (string, string?, double, Color, double, string?, bool) baseKey, highKey, outgoingKey;
    private double measuredWords = double.NaN;

    public VoicePillLabel()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The label's font size (13.5 inline, 15 float).</summary>
    public double FontSize
    {
        get => fontSize;
        set
        {
            if (fontSize == value) return;
            fontSize = value;
            measuredWords = double.NaN;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The words shown (V-12.9).</summary>
    public string Words
    {
        get => words;
        set
        {
            if (words == value) return;
            words = value;
            measuredWords = double.NaN;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The language's name (V-12.10), or null: then nothing of it is in the text.</summary>
    public string? LanguageName
    {
        get => languageName;
        set
        {
            if (languageName == value) return;
            languageName = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>How much of the name shows, 0 to 1 (it fades in over 0.3 s, V-12.10).</summary>
    public double NameOpacity
    {
        get => nameOpacity;
        set
        {
            if (nameOpacity == value) return;
            nameOpacity = value;
            InvalidateVisual();
        }
    }

    /// <summary>The name that is leaving while another comes (the crossfade, V-12.10), drawn over the same place; null when none.</summary>
    public string? OutgoingName
    {
        get => outgoingName;
        set
        {
            if (outgoingName == value) return;
            outgoingName = value;
            InvalidateVisual();
        }
    }

    public double OutgoingOpacity
    {
        get => outgoingOpacity;
        set
        {
            if (outgoingOpacity == value) return;
            outgoingOpacity = value;
            InvalidateVisual();
        }
    }

    /// <summary>The sweep's <c>background-position</c> as a fraction: 1.35 (nothing lit, before the text) to -0.55 (nothing lit, after it).</summary>
    public double Sweep
    {
        get => sweep;
        set
        {
            if (sweep == value) return;
            sweep = value;
            InvalidateVisual();
        }
    }

    /// <summary>The runs of the label's text, as laid out: the words, and the name after an en space when it is shown (a test reads them).</summary>
    public IReadOnlyList<VoicePillRun> Runs => languageName is null ? [new VoicePillRun(words, 600)] : [new VoicePillRun(words, 600), new VoicePillRun(EnSpace + languageName, 400)];

    /// <summary>The words' width rounded up (V-12.2 <c>labelWidth</c>).</summary>
    public double WordsWidth => double.IsNaN(measuredWords) ? measuredWords = Measure(words, 600) : measuredWords;

    /// <summary>The name's width, the en space and the name at weight 400 rounded up (V-12.10 <c>nameWidth</c>); 0 with no name.</summary>
    public double NameWidth => languageName is null ? 0 : Measure(EnSpace + languageName, 400);

    /// <summary>The width a text takes at a weight, rounded up (the design's canvas measure, V-12.2).</summary>
    public static double Measure(string text, int weight, double size)
    {
        using var layout = new TextLayout(text, Face(weight, size), size, null, maxWidth: double.PositiveInfinity);
        return Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
    }

    private double Measure(string text, int weight) => Measure(text, weight, fontSize);

    private static Typeface Face(int weight, double size)
    {
        var resolved = SwiftFont.SystemFont(size, weight >= 600 ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Resolve();
        return new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
    }

    /// <summary>The label's own height: the face's ascent to descent at the size (V-12.12: a box that holds the descenders).</summary>
    public double LineHeight
    {
        get
        {
            using var layout = new TextLayout("Ag", Face(600, fontSize), fontSize, null);
            return Math.Ceiling(layout.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(WordsWidth + NameWidth, LineHeight);

    public override void Render(DrawingContext context)
    {
        if (words.Length == 0) return;
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        var w = WordsWidth + NameWidth;
        var h = LineHeight;
        baseLayout = Ensure(baseLayout, ref baseKey, tokens.PgLab, nameOpacity, languageName, wordsShown: true);
        baseLayout.Draw(context, default);
        if (outgoingName is not null && outgoingOpacity > 0)
        {
            outgoingLayout = Ensure(outgoingLayout, ref outgoingKey, tokens.PgLab, outgoingOpacity, outgoingName, wordsShown: false);
            outgoingLayout.Draw(context, default);
        }
        // the sweep: the highlight colour through the gradient's lit band, tiled every 2.6 widths as the repeating background is
        highLayout = Ensure(highLayout, ref highKey, tokens.PgLabHi, nameOpacity, languageName, wordsShown: true);
        var tileWidth = 2.6 * w;
        var offset = -1.6 * w * sweep;
        var slant = h * Math.Tan(Math.PI / 12);
        for (var tile = -1; tile <= 1; tile++)
        {
            var centre = offset + 1.274 * w + tile * tileWidth;
            if (centre + 0.31 * tileWidth + slant < 0 || centre - 0.31 * tileWidth - slant > w) continue;
            using (context.PushOpacityMask(Mask(offset + tile * tileWidth, w, h), new Rect(0, 0, w, h)))
                highLayout.Draw(context, default);
        }
    }

    // linear-gradient(105deg, lab 0, lab 38 %, hi 49 %, lab 60 %, lab 100 %) over an image 260 % as wide as the text, as an alpha mask (0 where it is pgLab, 1 at pgLabHi)
    private static ImmutableLinearGradientBrush Mask(double left, double w, double h)
    {
        var imageWidth = 2.6 * w;
        var angle = 105 * Math.PI / 180;
        var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
        var length = Math.Abs(imageWidth * Math.Sin(angle)) + Math.Abs(h * Math.Cos(angle));
        var centre = new Point(left + imageWidth / 2, h / 2);
        var from = centre - direction * (length / 2);
        var to = centre + direction * (length / 2);
        var clear = Color.FromArgb(0, 0, 0, 0);
        return new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, clear), new ImmutableGradientStop(0.38, clear), new ImmutableGradientStop(0.49, Colors.Black), new ImmutableGradientStop(0.60, clear), new ImmutableGradientStop(1, clear)],
            spreadMethod: GradientSpreadMethod.Pad,
            startPoint: new RelativePoint(from, RelativeUnit.Absolute),
            endPoint: new RelativePoint(to, RelativeUnit.Absolute));
    }

    private TextLayout Ensure(TextLayout? layout, ref (string, string?, double, Color, double, string?, bool) key, Color color, double alpha, string? shownName, bool wordsShown)
    {
        var wanted = (words, shownName, Math.Round(alpha * 255), color, fontSize, (string?)null, wordsShown);
        if (layout is not null && key == wanted) return layout;
        layout?.Dispose();
        key = wanted;
        var heavy = Face(600, fontSize);
        var light = Face(400, fontSize);
        var text = shownName is null ? words : words + EnSpace + shownName;
        var spans = new List<ValueSpan<TextRunProperties>>();
        IBrush wordsBrush = wordsShown ? new ImmutableSolidColorBrush(color) : Brushes.Transparent;
        spans.Add(new ValueSpan<TextRunProperties>(0, words.Length, new GenericTextRunProperties(heavy, fontSize, null, wordsBrush)));
        if (shownName is not null)
        {
            var faded = Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B);
            spans.Add(new ValueSpan<TextRunProperties>(words.Length, text.Length - words.Length, new GenericTextRunProperties(light, fontSize, null, new ImmutableSolidColorBrush(faded))));
        }
        return new TextLayout(text, heavy, fontSize, new ImmutableSolidColorBrush(color), TextAlignment.Left, TextWrapping.NoWrap, null, null, FlowDirection.LeftToRight,
            double.PositiveInfinity, double.PositiveInfinity, double.NaN, 0, 0, null, spans);
    }
}
