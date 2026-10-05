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
/// (<c>pgLab</c> to 38 %, <c>pgLabHi</c> at 49 %, <c>pgLab</c> from 60 %) moving as <see cref="Sweep"/>, the position in the design's terms (1.35 to -0.55). The gradient is the glyph's
/// foreground, so each glyph is rasterized once: the sweep changes its colour without painting its antialiased edges twice. The box is the font's own ascent to descent (V-12.12). Text that is not shown is not in the layout at all (the page's hidden chip was painted through the sweep and showed its dot).
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
    private TextLayout? baseLayout, outgoingLayout;
    private readonly LinearGradientBrush wordsGradient = new(), nameGradient = new(), outgoingGradient = new();
    private (string, string?, double, Color, double, string?, bool) baseKey, outgoingKey;
    private double measuredWords = double.NaN, measuredPrefix = double.NaN;

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
            measuredWords = measuredPrefix = double.NaN;
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
            measuredWords = measuredPrefix = double.NaN;
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
        // This owned text draw uses the same source-ruled coverage in windows and offscreen layers.
        using var coverage = context.PushTextOptions(new TextOptions
        {
            TextRenderingMode = GreyscaleText.RenderingMode,
            TextHintingMode = GreyscaleText.HintingMode,
            BaselinePixelAlignment = BaselinePixelAlignment.Unaligned,
        });
        Configure(wordsGradient, w, h, tokens, 0, 1);
        if (languageName is not null) Configure(nameGradient, w, h, tokens, PrefixWidth, nameOpacity);
        baseLayout = Ensure(baseLayout, ref baseKey, tokens.PgLab, nameOpacity, languageName, wordsShown: true, nameGradient);
        baseLayout.Draw(context, default);
        if (outgoingName is not null && outgoingOpacity > 0)
        {
            Configure(outgoingGradient, w, h, tokens, PrefixWidth, outgoingOpacity);
            outgoingLayout = Ensure(outgoingLayout, ref outgoingKey, tokens.PgLab, outgoingOpacity, outgoingName, wordsShown: false, outgoingGradient);
            outgoingLayout.Draw(context, default);
        }
    }

    private double PrefixWidth
    {
        get
        {
            if (!double.IsNaN(measuredPrefix)) return measuredPrefix;
            using var layout = new TextLayout(words, Face(600, fontSize), fontSize, null);
            return measuredPrefix = layout.WidthIncludingTrailingWhitespace;
        }
    }

    // CSS background-clip:text: one opaque colour field, rasterized through each glyph only once.
    // Only the central tile's lit band reaches the label over the design's 1.35 to -0.55 sweep.
    private void Configure(LinearGradientBrush brush, double w, double h, VoicePillTokens tokens, double prefix, double alpha)
    {
        var imageWidth = 2.6 * w;
        var angle = 105 * Math.PI / 180;
        var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
        var length = Math.Abs(imageWidth * Math.Sin(angle)) + Math.Abs(h * Math.Cos(angle));
        var centre = new Point(-1.6 * w * sweep + imageWidth / 2 - prefix, h / 2);
        brush.StartPoint = new RelativePoint(centre - direction * (length / 2), RelativeUnit.Absolute);
        brush.EndPoint = new RelativePoint(centre + direction * (length / 2), RelativeUnit.Absolute);
        brush.SpreadMethod = GradientSpreadMethod.Pad;
        brush.Opacity = Math.Clamp(alpha, 0, 1);
        brush.GradientStops = [new GradientStop(tokens.PgLab, 0), new GradientStop(tokens.PgLab, 0.38), new GradientStop(tokens.PgLabHi, 0.49), new GradientStop(tokens.PgLab, 0.60), new GradientStop(tokens.PgLab, 1)];
    }

    private TextLayout Ensure(TextLayout? layout, ref (string, string?, double, Color, double, string?, bool) key, Color color, double alpha, string? shownName, bool wordsShown, IBrush nameBrush)
    {
        var wanted = (words, shownName, Math.Round(alpha * 255), color, fontSize, (string?)null, wordsShown);
        if (layout is not null && key == wanted) return layout;
        layout?.Dispose();
        key = wanted;
        var heavy = Face(600, fontSize);
        var light = Face(400, fontSize);
        var text = shownName is null ? words : words + EnSpace + shownName;
        var spans = new List<ValueSpan<TextRunProperties>>();
        IBrush wordsBrush = wordsShown ? wordsGradient : Brushes.Transparent;
        spans.Add(new ValueSpan<TextRunProperties>(0, words.Length, new GenericTextRunProperties(heavy, fontSize, null, wordsBrush)));
        if (shownName is not null)
        {
            spans.Add(new ValueSpan<TextRunProperties>(words.Length, text.Length - words.Length, new GenericTextRunProperties(light, fontSize, null, nameBrush)));
        }
        return new TextLayout(text, heavy, fontSize, wordsBrush, TextAlignment.Left, TextWrapping.NoWrap, null, null, FlowDirection.LeftToRight,
            double.PositiveInfinity, double.PositiveInfinity, double.NaN, 0, 0, null, spans);
    }
}
