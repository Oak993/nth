// PORT-ONLY. SwiftUI's `.contentTransition(.numericText())` on a Text (EffortSlider.swift:574, UsageViews.swift:235,
// HydraMergePopover.swift:204, Popovers.swift:311): when the string changes inside an animation, the characters both
// strings share glide to their new places, the new ones arrive one after another and the old ones leave, each with a
// blur, instead of the whole label cross-fading.
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 34.80-35.75 s, every frame, the
// effort title going High -> Extra High -> Max at title2; INDEX.md "Findings"): "High" glides right as "Extra" arrives
// letter by letter from the left, 20-40 ms apart; each arriving letter starts at about half size, sitting on the
// baseline, blurred and faint, and settles in about 120-150 ms; leaving letters blur, shrink and fade where they were,
// relative to the label's leading edge, which moves as the label's width animates (so a shrinking centred label pushes
// them right); the leaving letters keep the old colour while the new ones already wear the new one; a whole change
// takes about the animation's duration (0.25-0.3 s). SOURCE FACT: the transition and its look (video). ENGINEERING
// DECISION, UNVERIFIED numbers estimated from those frames: half-size start, 4 pt blur, letters 25 ms apart (at most
// 30 % of the duration in all), each a smoothstep over 70 % of it (about 0.18 s for the slider's snappy 0.2), shared
// letters gliding on the change's own curve and matched by longest common subsequence; side by side with the video in comparisons/effort_title_morph_port_vs_mac_{a,b}.png.
// The blur is drawn as seventeen offset copies (Avalonia's DrawingContext has no per-draw blur).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>A TextBlock whose text can change with SwiftUI's numeric-text content transition (see the file note).</summary>
public class MorphingTextBlock : TextBlock
{
    internal const double StartScale = 0.5;
    internal const double MaxBlur = 4;
    internal const double SpreadShare = 0.3; // with GlyphShare, every letter ends by the change's end
    internal const double Stagger = 0.025;
    internal const double GlyphShare = 0.7;

    private Morph? morph;
    private IFrameSource? frames;
    private Action<TimeSpan>? tick;

    // Leaving letters keep their old place while the label narrows, so they may hang past its new edge (the video's
    // "High" drifts over the chevron as "Max" arrives): nothing of the label is clipped.
    public MorphingTextBlock() => ClipToBounds = false;

    /// <summary>Whether a change is still animating.</summary>
    public bool IsMorphing => morph is not null;

    /// <summary>
    /// Sets the text. Inside an animation (<paramref name="animation"/> not null) the change morphs as
    /// <c>.contentTransition(.numericText())</c> does; with none it is instant, as SwiftUI's is outside a transaction.
    /// </summary>
    public void SetText(string text, Animation? animation)
    {
        var old = Text ?? "";
        if (old == text) return;
        if (animation is null || old.Length == 0)
        {
            Stop();
            Text = text;
            return;
        }
        BeginMorph(animation);
        Text = text;
    }

    /// <summary>
    /// Starts a morph away from the text shown now; whatever text the block shows next is where it goes (a subclass
    /// that decides its text while measuring, such as a truncating one, calls this before it changes the text).
    /// </summary>
    protected void BeginMorph(Animation animation)
    {
        var old = Text ?? "";
        Stop();
        if (old.Length == 0) return;
        // A copy of the old line, built now with the old colour: the TextBlock disposes its own layout on a change.
        morph = new Morph(old, CreateTextLayout(old), Bounds.Width, animation, SwiftClock.Now);
        frames = FrameClock.Source.Current;
        tick = _ =>
        {
            if (morph is null) return;
            InvalidateMeasure();
            InvalidateVisual();
            if (Elapsed(morph) >= morph.Animation.Duration) { Stop(); return; }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Stop()
    {
        if (tick is not null) frames?.CancelFrame(tick);
        tick = null;
        if (morph is null) return;
        morph = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private static double Elapsed(Morph state) => (SwiftClock.Now - state.Start).TotalSeconds;

    protected override Size MeasureOverride(Size availableSize) => Glide(base.MeasureOverride(availableSize));

    /// <summary>The text's own size, without the glide (for a subclass that tries several texts while measuring).</summary>
    protected Size MeasureText(Size availableSize) => base.MeasureOverride(availableSize);

    /// <summary>
    /// The size to report while a change runs: the label's frame animates with the change (SwiftUI animates the layout
    /// in the same transaction), so a neighbour such as the title's chevron slides instead of jumping.
    /// </summary>
    protected Size Glide(Size size)
    {
        if (morph is null) return size;
        var progress = morph.Animation.Progress(Elapsed(morph));
        return size.WithWidth(Math.Max(0, morph.OldWidth + (size.Width - morph.OldWidth) * progress));
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (morph is not { } state || TextLayout is not { } layout)
        {
            base.RenderTextLayout(context, origin);
            return;
        }
        foreach (var glyph in Glyphs(state.Old, Text ?? "", Elapsed(state), state.Animation))
        {
            if (glyph.Opacity <= 0.001) continue;
            var source = glyph.FromOld ? state.OldLayout : layout;
            var rect = source.HitTestTextPosition(glyph.SourceIndex);
            if (rect.Width <= 0) continue; // a space
            var drawX = glyph.X(state.OldLayout, layout);
            // Scale about the glyph's foot, so a growing letter rises out of the baseline.
            var foot = new Point(rect.X + rect.Width / 2, rect.Bottom);
            var transform = Matrix.CreateTranslation(-foot.X, -foot.Y) * Matrix.CreateScale(glyph.Scale, glyph.Scale)
                * Matrix.CreateTranslation(foot.X + drawX - rect.X, foot.Y) * Matrix.CreateTranslation(origin.X, origin.Y);
            var clip = rect.Inflate(new Thickness(2, 4));
            using (context.PushTransform(transform))
            using (context.PushClip(clip))
                DrawBlurred(context, source, glyph.Opacity, glyph.Blur);
        }
    }

    // The whole line is drawn under a clip to one glyph, so kerning and a gradient across the word stay as they were.
    // Each line carries its own brush: the old line the old colour, the live one the (animating) new colour.
    private static void DrawBlurred(DrawingContext context, TextLayout layout, double opacity, double blur)
    {
        void Draw(double dx, double dy, double alpha)
        {
            using (context.PushOpacity(alpha))
                layout.Draw(context, new Point(dx, dy));
        }
        if (blur < 0.25)
        {
            Draw(0, 0, opacity);
            return;
        }
        // Two rings of eight copies (radius and half radius) around a faint centre: soft, without visible doubles.
        Draw(0, 0, opacity * 0.25);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            Draw(Math.Cos(angle) * blur / 2, Math.Sin(angle) * blur / 2, opacity * 0.12);
            Draw(Math.Cos(angle + Math.PI / 8) * blur, Math.Sin(angle + Math.PI / 8) * blur, opacity * 0.06);
        }
    }

    /// <summary>One character's state at a moment of a change.</summary>
    internal readonly record struct GlyphState(bool FromOld, int SourceIndex, int? OldIndex, int? NewIndex, double Progress, double Opacity, double Scale, double Blur)
    {
        public double X(TextLayout oldLayout, TextLayout newLayout)
        {
            var oldX = OldIndex is { } o ? oldLayout.HitTestTextPosition(o).X : 0;
            var newX = NewIndex is { } n ? newLayout.HitTestTextPosition(n).X : 0;
            return OldIndex is null ? newX : NewIndex is null ? oldX : oldX + (newX - oldX) * Progress;
        }
    }

    /// <summary>Every character of a change at <paramref name="seconds"/> into it (see the file note for the rules).</summary>
    internal static List<GlyphState> Glyphs(string oldText, string newText, double seconds, Animation animation)
    {
        var duration = Math.Max(animation.Duration, 1e-6);
        var shared = animation.Progress(Math.Min(seconds, duration));
        var (pairs, _) = CommonSubsequence(oldText, newText);
        var result = new List<GlyphState>();
        var keptOld = new HashSet<int>(pairs.Select(pair => pair.Old));
        var keptNew = new HashSet<int>(pairs.Select(pair => pair.New));
        foreach (var (o, n) in pairs)
            result.Add(new GlyphState(false, n, o, n, shared, 1, 1, 0));
        var arriving = Enumerable.Range(0, newText.Length).Where(i => !keptNew.Contains(i) && newText[i] != ' ').ToList();
        var leaving = Enumerable.Range(0, oldText.Length).Where(i => !keptOld.Contains(i) && oldText[i] != ' ').ToList();
        for (var k = 0; k < leaving.Count; k++)
        {
            var p = Local(k, leaving.Count, seconds, animation);
            result.Add(new GlyphState(true, leaving[k], leaving[k], null, p, 1 - p, 1 - (1 - StartScale) * p, MaxBlur * p));
        }
        for (var k = 0; k < arriving.Count; k++)
        {
            var p = Local(k, arriving.Count, seconds, animation);
            result.Add(new GlyphState(false, arriving[k], null, arriving[k], p, p, StartScale + (1 - StartScale) * p, MaxBlur * (1 - p)));
        }
        return result;
    }

    // One letter's progress: a smoothstep over its own span, after its turn in the stagger. Not the change's spring:
    // in the frames a letter stays soft and faint for most of its span instead of snapping in early.
    private static double Local(int order, int count, double seconds, Animation animation)
    {
        var duration = animation.Duration;
        var delay = count <= 1 ? 0 : order * Math.Min(Stagger, SpreadShare * duration / (count - 1));
        var linear = Math.Clamp((seconds - delay) / (GlyphShare * duration), 0, 1);
        return linear * linear * (3 - 2 * linear);
    }

    /// <summary>The characters both strings keep, as (old index, new index) pairs in order: a longest common subsequence.</summary>
    internal static (List<(int Old, int New)> Pairs, int Count) CommonSubsequence(string a, string b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < a.Length && j < b.Length;)
        {
            if (a[i] == b[j]) { pairs.Add((i, j)); i++; j++; }
            else if (table[i + 1, j] >= table[i, j + 1]) i++;
            else j++;
        }
        return (pairs, pairs.Count);
    }

    private sealed record Morph(string Old, TextLayout OldLayout, double OldWidth, Animation Animation, ContinuousInstant Start);
}
