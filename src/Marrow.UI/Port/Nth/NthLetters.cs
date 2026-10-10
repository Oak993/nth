// PORT-ONLY: OD-0136 amendment 2, step 2 option B (ED-NEW-nth-motion). Text drawn letter by letter on the render thread:
// - Breathe: the working line's word. Each letter lifts -1.5 px and back on a 2.6 s cycle, 55 ms after the one before; a new
//   word arrives letter by letter (old letters leave up 4 px over 0.24 s, 12 ms apart; new ones rise from 5 px over 0.36 s,
//   18 ms apart). Calm: still letters, a cross-fade.
// - Roll: the timer (and the MB counts of steps 6, 10, 13). Each changed character rolls out (0.3 s) and in (0.42 s); the rest
//   stay. Right-aligned, so a change never moves it. Calm: a 0.2 s cross-fade.
// The letters are the text's own shaped glyphs (Avalonia's TextLayout with the control's inherited font), one glyph run each.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace Marrow.UI.Port.Nth;

public enum NthLettersMode { Breathe, Roll }

/// <summary>One letter: its glyphs and where they go (x from the text's left edge; baseline placed), and its character.</summary>
public readonly record struct NthLetter(IImmutableGlyphRunReference Run, Matrix Place, char Character, double Right);

/// <summary>A word as letters, with its width.</summary>
public sealed record NthWord(string Text, NthLetter[] Letters, double Width, double Height);

public sealed record NthLettersState(NthLettersMode Mode, NthWord? Current, NthWord? Previous, double ChangedAt, Color Ink);

/// <summary>Text whose letters breathe (the working line's word) or roll (a timer). Set <see cref="Text"/>.</summary>
public sealed class NthLetters : Control
{
    private readonly NthVisualHost host;
    private string text = "";
    private NthWord? current, previous;
    private double changedAt = double.NegativeInfinity;
    private bool dirty = true;

    public NthLetters(NthLettersMode mode = NthLettersMode.Breathe)
    {
        Mode = mode;
        IsHitTestVisible = false;
        host = new NthVisualHost(this, () => new NthLettersPainter(), () => new NthLettersState(Mode, current, previous, changedAt, Ink));
        ActualThemeVariantChanged += (_, _) => host.Send();
    }

    public NthLettersMode Mode { get; }

    /// <summary>The text's colour (the inherited foreground unless set).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<NthLetters>();

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private Color Ink => Foreground is ISolidColorBrush solid ? Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B) : Colors.Gray;

    /// <summary>The text shown; a change animates (a new word, or the changed characters roll).</summary>
    public string Text
    {
        get => text;
        set
        {
            value ??= "";
            if (value == text) return;
            var animate = current is not null && VisualRoot is not null;
            text = value;
            previous = animate ? current : null;
            changedAt = animate ? host.Now : double.NegativeInfinity;
            current = null; dirty = true;
            InvalidateMeasure();
        }
    }

    /// <summary>Shown at once, without the change animating.</summary>
    public void SetTextInstantly(string value) { text = value ?? ""; previous = null; changedAt = double.NegativeInfinity; current = null; dirty = true; InvalidateMeasure(); }

    public NthFrame? Frame => host.Frame;
    public NthFrame Snapshot() => host.Snapshot();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty) host.Send();
        else if (change.Property == TextElement.FontFamilyProperty || change.Property == TextElement.FontSizeProperty
            || change.Property == TextElement.FontWeightProperty || change.Property == TextElement.FontStyleProperty
            || change.Property == TextElement.FontFeaturesProperty)
        { dirty = true; current = null; InvalidateMeasure(); }
    }

    private NthWord Shape(string value)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this));
        using var layout = new TextLayout(value, typeface, TextElement.GetFontSize(this), Brushes.Black, fontFeatures: TextElement.GetFontFeatures(this));
        var letters = new List<NthLetter>();
        double top = 0;
        foreach (var line in layout.TextLines)
        {
            var x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped)
                {
                    var buffer = shaped.ShapedBuffer;
                    var baseline = shaped.GlyphRun.BaselineOrigin.Y;
                    var at = 0.0;
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        var info = buffer[i];
                        // one glyph run per glyph (a cluster's marks stay with their base: the cluster's own glyph runs follow it)
                        var single = new GlyphRun(buffer.GlyphTypeface, buffer.FontRenderingEmSize, shaped.Text,
                            new[] { new GlyphInfo(info.GlyphIndex, info.GlyphCluster, info.GlyphAdvance, info.GlyphOffset) }, new Point(0, baseline));
                        var character = info.GlyphCluster >= 0 && info.GlyphCluster < value.Length ? value[info.GlyphCluster] : ' ';
                        if (single.TryCreateImmutableGlyphRunReference() is { } reference)
                            letters.Add(new NthLetter(reference, Matrix.CreateTranslation(x + at, top + line.Baseline - baseline), character, x + at + info.GlyphAdvance));
                        at += info.GlyphAdvance;
                    }
                }
                if (run is DrawableTextRun drawable) x += drawable.Size.Width;
            }
            top += line.Height;
        }
        return new NthWord(value, [.. letters], layout.WidthIncludingTrailingWhitespace, layout.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (dirty || current is null) { current = Shape(text); dirty = false; host.Send(); }
        return new Size(current.Width, current.Height);
    }
}

/// <summary>Draws breathing or rolling letters (render thread).</summary>
public sealed class NthLettersPainter : NthPainter
{
    private readonly Dictionary<uint, IImmutableBrush> inks = [];
    private object? ownedCurrent, ownedPrevious;

    public override bool Moving(NthFrame frame, double now)
    {
        if (frame.State is not NthLettersState s) return false;
        if (s.Mode == NthLettersMode.Breathe && !frame.Calm) return true;
        var length = s.Mode == NthLettersMode.Roll ? NthMotion.RollSeconds : NthMotion.WordChangeSeconds(Math.Max(s.Current?.Letters.Length ?? 0, s.Previous?.Letters.Length ?? 0), frame.Calm);
        return s.Previous is not null && now - s.ChangedAt < length + .05;
    }

    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(new Thickness(2, 8, 60, 8));

    private IImmutableBrush Ink(Color color, double alpha)
    {
        var a = (byte)Math.Round(color.A * NthEase.Clamp01(alpha));
        var key = (uint)a << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
        if (!inks.TryGetValue(key, out var brush))
        {
            if (inks.Count > 512) inks.Clear(); // a theme change: the old ink's 256 opacities go
            inks[key] = brush = new ImmutableSolidColorBrush(Color.FromUInt32(key));
        }
        return brush;
    }

    internal override void Render(ImmediateDrawingContext context, NthFrame frame, double now)
    {
        if (frame.State is not NthLettersState s) return;
        Own(s);
        var since = now - s.ChangedAt;
        var t = now - frame.Started;
        if (s.Mode == NthLettersMode.Breathe) DrawBreathe(context, s, frame.Calm, since, t);
        else DrawRoll(context, s, frame, since);
    }

    private void DrawBreathe(ImmediateDrawingContext context, NthLettersState s, bool calm, double since, double t)
    {
        var changing = s.Previous is { } old && since < NthMotion.WordChangeSeconds(Math.Max(old.Letters.Length, s.Current?.Letters.Length ?? 0), calm);
        if (changing)
            for (var i = 0; i < s.Previous!.Letters.Length; i++)
            {
                var (y, alpha) = NthMotion.LetterOut(since, i, calm);
                Letter(context, s.Previous.Letters[i], s.Ink, y + NthMotion.LetterLift(t, i, calm), alpha);
            }
        if (s.Current is not { } word) return;
        for (var i = 0; i < word.Letters.Length; i++)
        {
            var (y, alpha) = changing ? NthMotion.LetterIn(since, i, calm) : (0, 1);
            Letter(context, word.Letters[i], s.Ink, y + NthMotion.LetterLift(t, i, calm), alpha);
        }
    }

    private void DrawRoll(ImmediateDrawingContext context, NthLettersState s, NthFrame frame, double since)
    {
        if (s.Current is not { } word) return;
        var rolling = s.Previous is { } old && since < NthMotion.RollSeconds;
        var height = frame.Size.Height;
        using var clip = context.PushClip(new Rect(-60, 0, frame.Size.Width + 120, height));
        // right-aligned: characters are compared from the right, so a change never moves the rest
        var right = frame.Size.Width;
        var roll = NthMotion.Roll(since, frame.Calm);
        for (var k = 1; k <= word.Letters.Length; k++)
        {
            var letter = word.Letters[^k];
            var dx = right - word.Width;
            var same = !rolling || k <= s.Previous!.Letters.Length && s.Previous.Letters[^k].Character == letter.Character
                && Math.Abs((right - s.Previous.Width) + s.Previous.Letters[^k].Right - (dx + letter.Right)) < .01;
            if (same) { Letter(context, letter, s.Ink, 0, 1, dx); continue; }
            Letter(context, letter, s.Ink, roll.NewY * height, roll.NewAlpha, dx);
        }
        if (!rolling) return;
        var prev = s.Previous!;
        for (var k = 1; k <= prev.Letters.Length; k++)
        {
            var letter = prev.Letters[^k];
            var dx = right - prev.Width;
            var same = k <= word.Letters.Length && word.Letters[^k].Character == letter.Character
                && Math.Abs(dx + letter.Right - ((right - word.Width) + word.Letters[^k].Right)) < .01;
            if (!same) Letter(context, letter, s.Ink, roll.OldY * height, roll.OldAlpha, dx);
        }
    }

    private void Letter(ImmediateDrawingContext context, NthLetter letter, Color ink, double y, double alpha, double dx = 0)
    {
        if (alpha <= 0) return;
        using var placed = context.PushPreTransform(letter.Place * Matrix.CreateTranslation(dx, y));
        context.DrawGlyphRun(Ink(ink, alpha), letter.Run);
    }

    // Glyph references of words no longer shown are released on the render thread, once neither slot holds them.
    private void Own(NthLettersState s)
    {
        if (ReferenceEquals(s.Current, ownedCurrent) && ReferenceEquals(s.Previous, ownedPrevious)) return;
        foreach (var word in new[] { ownedCurrent as NthWord, ownedPrevious as NthWord })
            if (word is not null && !ReferenceEquals(word, s.Current) && !ReferenceEquals(word, s.Previous)) Release(word);
        ownedCurrent = s.Current; ownedPrevious = s.Previous;
    }

    private static void Release(NthWord word) { foreach (var letter in word.Letters) letter.Run.Dispose(); }

    public override void Dispose()
    {
        if (ownedCurrent is NthWord a) Release(a);
        if (ownedPrevious is NthWord b && !ReferenceEquals(b, ownedCurrent)) Release(b);
        base.Dispose();
    }
}
