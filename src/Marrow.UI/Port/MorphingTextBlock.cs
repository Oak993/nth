// PORT-ONLY. SwiftUI's `.contentTransition(.numericText())` on a Text (EffortSlider.swift:574, UsageViews.swift:235,
// HydraMergePopover.swift:204, Popovers.swift:311): when the string changes inside an animation, the characters both
// strings share glide to their new places, the new ones arrive one after another and the old ones leave, each with a
// blur, instead of the whole label cross-fading.
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov, every frame; study of
// 2026-10-03 in study\effort-title-2026-10-03: High -> Extra High 34.84 s, Extra High -> Max 35.04 s, Max -> Extra High
// 35.635 s, Medium -> High 184.22 s, High -> Extra High 184.46 s, Extra High -> Max 184.70 s). SOURCE FACTS (video):
// - only a common beginning or end of the two strings is kept: its letters glide to their new places on the change's
//   own curve ("High" in High <-> Extra High); "Extra High" -> "Max", "Max" -> "Extra High" and "Medium" -> "High" keep
//   nothing (no "a", "x" or "i" glides);
// - every other letter rolls: an arriving one rises from below its place (its centre starts about 6.6 px, 0.39 em of the
//   17 pt title, low) while growing from about half size, blurred and faint; a leaving one rises away upward by about
//   the same (7-7.5 px) while it shrinks, blurs and fades, where it was relative to the label's leading edge (which
//   moves as the label's width animates, so a narrowing centred label carries it right, past the chevron); the same
//   direction whether the effort goes up or down (numericText's default, countsDown: false);
// - the letters take their turns left to right over about 0.14 s whatever their number ("Max" arrives about 70 ms
//   apart, "Extra" about 30 ms, the nine leaving letters of "Extra High" about 20 ms), and each then follows the change's
//   own curve (the first arriving "E" tracks the snappy spring of the slider from about 40 ms after the change); so
//   the last letters settle about 0.18 s after the change's own duration;
// - leaving letters keep the old colour, arriving ones wear the new one from their first frame (an orange "M" beside a
//   pink leaving "High");
// - a gradient title is filled through the new text's own box, set at the label's moving leading edge: its colours span
//   the new text from the first frame, so letters gliding or carried along pass through them (the "passing gradient" of
//   the 6 Luna pair's title, 183.3-185.4 s), and nothing outside that box shows ("Medium" -> "High" 184.24 s: "Medi"
//   coloured across the whole sweep, "um" gone at once; "Extra High" -> "Max" 184.715 s: "Extra" cut through its "a",
//   "High" gone). A plain colour draws every letter (the pink "High" fading past the chevron, 35.12-35.29 s).
// ENGINEERING DECISION, UNVERIFIED numbers fitted to those frames: travel 0.4 em, half-size start, 4 pt blur, the turns
// spread over 55 % of the change's duration (0.14 s for the slider's snappy 0.2), new letters starting 15 % of it
// (0.04 s) late, scaling about each letter's centre.
// Side by side with the video in study\effort-title-2026-10-03\sbs_*.png.
// While a change runs every letter is drawn as its own outline (so no neighbour is drawn with it and a gradient can be
// pinned to that box); the blur is seventeen offset copies (Avalonia's DrawingContext has no per-draw blur).
// DEVIATION OD-0094 (owner ruling 2026-10-03): with DriftPeriod set (the effort title: 7 s), a gradient's colours also
// drift at rest, mirrored so the loop never jumps, on one phase shared with the change's own drawing (a change takes it
// over and hands it back without a jump); still under reduce motion; frames only while the label is shown. ENGINEERING
// DECISION, the period: 7 s per mirrored loop moves the colours one text width in 3.5 s, about 20 pt/s on "Max" to 30
// on "Extra High": slow beside the track's own sheen (EffortSlider.swift:1208, a 2.8 s sweep) so the title stays calm
// and never races the slider under it, yet visibly alive within a glance of a few seconds.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>A TextBlock whose text can change with SwiftUI's numeric-text content transition (see the file note).</summary>
public class MorphingTextBlock : TextBlock
{
    internal const double StartScale = 0.5;
    internal const double MaxBlur = 4;
    internal const double Travel = 0.4; // of the font size: how far below its place a letter starts, and how far up one leaves
    internal const double SpreadShare = 0.55; // the letters' turns, first to last, as a share of the change's duration
    internal const double ArrivalShare = 0.15; // how long, as a share of the duration, the first new letter waits

    private Morph? morph;
    private IFrameSource? frames;
    private Action<TimeSpan>? tick;

    // Leaving letters keep their old place while the label narrows, so they may hang past its new edge (the video's
    // "High" drifts over the chevron as "Max" arrives): nothing of the label is clipped.
    public MorphingTextBlock() => ClipToBounds = false;

    /// <summary>
    /// Where the label stays put in its parent while its width changes: 0 its leading edge (the default), 0.5 its centre
    /// (a centred label such as the effort title), 1 its trailing edge. Arriving letters appear at the places they will
    /// have once the label has its new width (video: the "M" of Max at its final place from its first frame, 35.12 s,
    /// while the narrowing label's leading edge is still on its way), so they are drawn that far from the moving edge.
    /// </summary>
    public double GrowthAnchor { get; set; }

    /// <summary>
    /// OD-0094 (owner ruling, a deviation from EffortSlider.swift:543-555's static gradient): the seconds a gradient
    /// title's colours take to drift once round their loop at rest; 0 (the default) keeps them still, as the source.
    /// The gradient is mirrored (reflected past its ends), so the loop never jumps; the colours move from the leading
    /// edge to the trailing one by twice the text's width per period. A single colour never drifts.
    /// </summary>
    public double DriftPeriod
    {
        get => driftPeriod;
        set { driftPeriod = value; Adopt(); }
    }

    private double driftPeriod;
    private double driftSeconds;
    private ContinuousInstant? driftLast;
    private IFrameSource? driftFrames;
    private Action<TimeSpan>? driftCallback;
    private bool drifting;

    /// <summary>Where the drift is in its loop, 0 to 1: one phase at rest and through a change.</summary>
    internal double DriftPhase => driftPeriod > 0 ? driftSeconds / driftPeriod % 1 : 0;

    // How far the mirrored gradient has moved along a text of this width.
    private double DriftOffset(double width) => DriftPhase * 2 * width;

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
        // A change that comes while one still runs (a quick drag across the stops) carries on from where every letter is:
        // a half-arrived letter leaves from its half-arrived state, and letters already leaving go on leaving.
        var interrupted = morph is { } running ? running with { NewText = old } : null;
        var carried = interrupted is not null && TextLayout is { } current ? InFlight(interrupted, old, current) : null;
        CancelTick();
        morph = null;
        if (old.Length == 0) { InvalidateMeasure(); InvalidateVisual(); return; }
        // A copy of the old line, built now with the old colour: the TextBlock disposes its own layout on a change.
        morph = new Morph(old, CreateTextLayout(old), Foreground, Bounds.Width, animation, SwiftClock.Now, carried, Running(interrupted));
        frames = FrameClock.Source.Current;
        tick = _ =>
        {
            if (morph is null) return;
            InvalidateMeasure();
            InvalidateVisual();
            if (Running(morph) is null) { Stop(); return; }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
        InvalidateMeasure();
        InvalidateVisual();
    }

    // A morph, unless it has finished, with whichever of the morphs it interrupted still play their leaving letters.
    private static Morph? Running(Morph? state)
    {
        if (state is null) return null;
        var rest = Running(state.Interrupted);
        if (Elapsed(state) < Length(state.Animation)) return ReferenceEquals(rest, state.Interrupted) ? state : state with { Interrupted = rest };
        return rest;
    }

    // Every letter of the text the running morph is going to, at its state now and where it is drawn, by its index.
    private static Dictionary<int, (GlyphState Glyph, double X)> InFlight(Morph state, string text, TextLayout layout) =>
        Resolve(state, text, layout, Elapsed(state)).Where(g => g.Glyph.NewIndex is not null).ToDictionary(g => g.Glyph.NewIndex!.Value, g => (g.Glyph, g.X));

    // The last new letter takes its turn (ArrivalShare + SpreadShare) of the change's duration in, then runs its curve.
    internal static double Length(Animation animation) => animation.Duration * (1 + ArrivalShare + SpreadShare);

    private void CancelTick()
    {
        if (tick is not null) frames?.CancelFrame(tick);
        tick = null;
    }

    private void Stop()
    {
        CancelTick();
        if (morph is null) return;
        morph = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        foreach (var ancestor in watched) ancestor.PropertyChanged -= OnAncestorChanged;
        watched.Clear();
        attached = false;
        UpdateDrift();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Shown or hidden with its ancestors (the slider page under the model list): watched so the drift can stop.
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestor.PropertyChanged += OnAncestorChanged;
            watched.Add(ancestor);
        }
        attached = true;
        UpdateDrift();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty) Adopt();
        else if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty || change.Property == IsVisibleProperty) UpdateDrift();
    }

    private readonly List<Visual> watched = [];
    private bool attached;

    // Shown: in a window, and neither it nor any ancestor hidden (read here, not from the effective visibility, which
    // the ancestors' own change has not yet passed down when it is reported).
    private bool Shown() => attached && IsVisible && watched.All(ancestor => ancestor.IsVisible);

    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty) UpdateDrift();
    }

    // A new colour for the title: the drift's own brush (its stops copied once, reflected past their ends so the moving
    // window never meets an edge) is rebuilt; every frame then only moves its two ends.
    private void Adopt()
    {
        driftBrush = null;
        UpdateDrift();
    }

    private LinearGradientBrush? driftBrush;

    // The title's gradient through the box, its ends moved by the drift; one brush kept and moved, frame after frame.
    private LinearGradientBrush Drifted(ILinearGradientBrush gradient, Rect box)
    {
        if (driftBrush is null)
        {
            var stops = new GradientStops();
            foreach (var stop in gradient.GradientStops) stops.Add(new GradientStop(stop.Color, stop.Offset));
            driftBrush = new LinearGradientBrush { GradientStops = stops, SpreadMethod = GradientSpreadMethod.Reflect, Opacity = gradient.Opacity };
        }
        var along = new Vector(DriftOffset(box.Width), 0);
        // In the layout's own space (the caller draws it moved by origin).
        driftBrush.StartPoint = new RelativePoint(gradient.StartPoint.ToPixels(box) + along - (Vector)box.Position, RelativeUnit.Absolute);
        driftBrush.EndPoint = new RelativePoint(gradient.EndPoint.ToPixels(box) + along - (Vector)box.Position, RelativeUnit.Absolute);
        return driftBrush;
    }

    // Frames only while there is a gradient to move, the title is shown, and motion is not reduced.
    private void UpdateDrift()
    {
        var active = driftPeriod > 0 && Foreground is ILinearGradientBrush && Shown() && !EnvironmentValues.GetAccessibilityReduceMotion(this);
        if (active == drifting) return;
        drifting = active;
        if (active)
        {
            driftLast = null;
            driftFrames = FrameClock.Source.Current;
            driftCallback ??= OnDriftFrame;
            driftFrames.RequestFrame(driftCallback);
        }
        else if (driftCallback is not null) driftFrames?.CancelFrame(driftCallback);
    }

    private void OnDriftFrame(TimeSpan _)
    {
        if (!drifting) return;
        var now = SwiftClock.Now;
        // A long gap (a stalled frame) moves it on by at most a tenth of a second, never a lurch.
        if (driftLast is { } last) driftSeconds = (driftSeconds + Math.Min(0.1, Math.Max(0, (now - last).TotalSeconds))) % driftPeriod;
        driftLast = now;
        InvalidateVisual();
        driftFrames!.RequestFrame(driftCallback!);
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
            if (driftPeriod > 0 && Foreground is ILinearGradientBrush gradient && TextLayout is { } still && Outline(still) is { } outline)
            {
                // At rest a drifting gradient is drawn through the same box and phase as during a change (OD-0094).
                var box = new Rect(origin, new Size(still.WidthIncludingTrailingWhitespace, Math.Max(still.Height, Bounds.Height - origin.Y)));
                var brush = Drifted(gradient, box);
                using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                    context.DrawGeometry(brush, null, outline);
                return;
            }
            base.RenderTextLayout(context, origin);
            return;
        }
        // A gradient title is filled through the new text's own box, set at the label's moving leading edge: the gradient
        // spans the new text's width from the first frame and nothing outside that box shows (video, the 6 Luna pair:
        // "Medium" -> "High" at 184.24 s shows "Medi" coloured across the whole sweep and its "um" gone at once; "Extra
        // High" -> "Max" at 184.715 s cuts "Extra" through its "a" and drops "High"). A plain colour draws every letter.
        var frame = new Rect(origin, new Size(layout.WidthIncludingTrailingWhitespace, Math.Max(layout.Height, Bounds.Height - origin.Y)));
        using var mask = Foreground is ILinearGradientBrush ? context.PushClip(frame) : default(DrawingContext.PushedState?);
        // The leaving letters of the changes this one interrupted, oldest first, each on its own clock.
        var chain = new List<(Morph State, TextLayout Next)>();
        var next = state.OldLayout;
        for (var earlier = state.Interrupted; earlier is not null; earlier = earlier.Interrupted)
        {
            chain.Insert(0, (earlier, next));
            next = earlier.OldLayout;
        }
        foreach (var (earlier, after) in chain)
        {
            if (Elapsed(earlier) >= Length(earlier.Animation)) continue;
            foreach (var (glyph, x) in Resolve(earlier, earlier.NewText, after, Elapsed(earlier)))
                if (glyph.NewIndex is null) DrawGlyph(context, origin, frame, glyph, x, earlier.OldLayout, earlier.OldBrush);
        }
        // How far the label's leading edge still has to go, given where it stays put: where the new letters already stand.
        var arrival = (Bounds.Width - Padding.Left - Padding.Right - layout.WidthIncludingTrailingWhitespace) * GrowthAnchor;
        foreach (var (glyph, x) in Resolve(state, Text ?? "", layout, Elapsed(state)))
            DrawGlyph(context, origin, frame, glyph, glyph.OldIndex is null ? x + arrival : x, glyph.FromOld ? state.OldLayout : layout, glyph.FromOld ? state.OldBrush : Foreground);
    }

    // Every letter of a change at a moment, with where it is drawn along the line; the letters it took over from an
    // interrupted change start from where that one had them.
    private static List<(GlyphState Glyph, double X)> Resolve(Morph state, string newText, TextLayout newLayout, double seconds)
    {
        var result = new List<(GlyphState, double)>();
        foreach (var glyph in Glyphs(state.Old, newText, seconds, state.Animation))
        {
            var x = glyph.X(state.OldLayout, newLayout);
            var drawn = glyph;
            if (glyph.OldIndex is { } o && state.Carried is { } carried && carried.TryGetValue(o, out var from))
            {
                if (glyph.NewIndex is null)
                {
                    drawn = glyph with
                    {
                        Opacity = from.Glyph.Opacity * glyph.Opacity,
                        Scale = from.Glyph.Scale * glyph.Scale,
                        Blur = Math.Max(from.Glyph.Blur, glyph.Blur),
                        Rise = from.Glyph.Rise + glyph.Rise,
                    };
                    x = from.X;
                }
                else
                {
                    var q = glyph.Progress;
                    drawn = glyph with
                    {
                        Opacity = Math.Clamp(from.Glyph.Opacity + (1 - from.Glyph.Opacity) * q, 0, 1),
                        Scale = from.Glyph.Scale + (1 - from.Glyph.Scale) * q,
                        Blur = Math.Max(0, from.Glyph.Blur * (1 - q)),
                        Rise = from.Glyph.Rise * (1 - q),
                    };
                    x = from.X + (x - from.X) * q;
                }
            }
            result.Add((drawn, x));
        }
        return result;
    }

    private void DrawGlyph(DrawingContext context, Point origin, Rect frame, GlyphState glyph, double drawX, TextLayout source, IBrush? brush)
    {
        if (glyph.Opacity <= 0.001) return;
        var rect = source.HitTestTextPosition(glyph.SourceIndex);
        if (rect.Width <= 0) return; // a space
        // Scale about the letter's centre, moved to its place along the line and up or down by its roll.
        var centre = rect.Center;
        var transform = Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateScale(glyph.Scale, glyph.Scale)
            * Matrix.CreateTranslation(centre.X + drawX - rect.X, centre.Y + glyph.Rise * FontSize) * Matrix.CreateTranslation(origin.X, origin.Y);
        // The letter's own outline, placed as the line places it (so kerning stays): nothing of its neighbours is drawn
        // with it. Each line carries its own brush: the old line the old colour, the live one the new colour.
        if (brush is null || !LetterOutlines(source).TryGetValue(glyph.SourceIndex, out var letter)) return;
        using (context.PushTransform(transform))
        {
            if (brush is ILinearGradientBrush gradient && transform.TryInvert(out var inverse))
                DrawBlurred(context, glyph.Opacity, glyph.Blur, (dx, dy) =>
                {
                    var shift = Matrix.CreateTranslation(dx, dy);
                    using (context.PushTransform(shift))
                        context.DrawGeometry(Pinned(gradient, frame, shift.Invert() * inverse), null, letter);
                });
            else
                DrawBlurred(context, glyph.Opacity, glyph.Blur, (dx, dy) =>
                {
                    using (context.PushTransform(Matrix.CreateTranslation(dx, dy)))
                        context.DrawGeometry(brush, null, letter);
                });
        }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextLayout, Dictionary<int, Geometry>> letterOutlines = new();

    // Each letter's outline by its character index, in the layout's own coordinates, as TextLayout.Draw places them.
    private Dictionary<int, Geometry> LetterOutlines(TextLayout layout)
    {
        if (letterOutlines.TryGetValue(layout, out var known)) return known;
        var letters = new Dictionary<int, Geometry>();
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
                    var at = x;
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        var info = buffer[i];
                        var single = new GlyphRun(buffer.GlyphTypeface, buffer.FontRenderingEmSize, shaped.Text,
                            new[] { new GlyphInfo(info.GlyphIndex, info.GlyphCluster, info.GlyphAdvance, info.GlyphOffset) }, new Point(0, baseline));
                        if (single.BuildGeometry() is { } outline && !letters.ContainsKey(info.GlyphCluster))
                        {
                            outline.Transform = new MatrixTransform(Matrix.CreateTranslation(at, top + line.Baseline - baseline));
                            letters[info.GlyphCluster] = outline;
                        }
                        at += info.GlyphAdvance;
                    }
                }
                if (run is DrawableTextRun drawable) x += drawable.Size.Width;
            }
            top += line.Height;
        }
        letterOutlines.AddOrUpdate(layout, letters);
        return letters;
    }

    private static void DrawBlurred(DrawingContext context, double opacity, double blur, Action<double, double> draw)
    {
        void Draw(double dx, double dy, double alpha)
        {
            using (context.PushOpacity(alpha))
                draw(dx, dy);
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

    // A gradient whose ends sit where they would on the label's frame, given in the letter's own (transformed) space:
    // the letter moves through the colours instead of carrying its own.
    private LinearGradientBrush Pinned(ILinearGradientBrush gradient, Rect frame, Matrix toLetter)
    {
        // The drift moves the colours along the same box at rest and here: one phase, so a change takes it over and
        // hands it back without a jump (OD-0094).
        var along = new Vector(DriftOffset(frame.Width), 0);
        Point At(RelativePoint point) => toLetter.Transform(point.ToPixels(frame) + along);
        var stops = new GradientStops();
        foreach (var stop in gradient.GradientStops) stops.Add(new GradientStop(stop.Color, stop.Offset));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(At(gradient.StartPoint), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(At(gradient.EndPoint), RelativeUnit.Absolute),
            GradientStops = stops,
            SpreadMethod = driftPeriod > 0 ? GradientSpreadMethod.Reflect : gradient.SpreadMethod,
            Opacity = gradient.Opacity,
        };
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextLayout, Geometry> outlines = new();

    // The line's glyph outlines in the layout's own coordinates, as TextLayout.Draw places its runs.
    private Geometry? Outline(TextLayout layout)
    {
        if (outlines.TryGetValue(layout, out var known)) return known;
        var group = new GeometryGroup();
        double top = 0;
        foreach (var line in layout.TextLines)
        {
            var x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped && shaped.GlyphRun.BuildGeometry() is { } glyphs)
                {
                    glyphs.Transform = new MatrixTransform(Matrix.CreateTranslation(x, top + line.Baseline - shaped.GlyphRun.BaselineOrigin.Y));
                    group.Children.Add(glyphs);
                }
                if (run is DrawableTextRun drawable) x += drawable.Size.Width;
            }
            top += line.Height;
        }
        outlines.AddOrUpdate(layout, group);
        return group;
    }

    /// <summary>One character's state at a moment of a change; <see cref="Rise"/> is its roll in font sizes, down positive.</summary>
    internal readonly record struct GlyphState(bool FromOld, int SourceIndex, int? OldIndex, int? NewIndex, double Progress, double Opacity, double Scale, double Blur, double Rise)
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
        var shared = animation.Progress(Math.Min(seconds, animation.Duration));
        var pairs = KeptLetters(oldText, newText);
        var result = new List<GlyphState>();
        var keptOld = new HashSet<int>(pairs.Select(pair => pair.Old));
        var keptNew = new HashSet<int>(pairs.Select(pair => pair.New));
        foreach (var (o, n) in pairs)
            result.Add(new GlyphState(false, n, o, n, shared, 1, 1, 0, 0));
        var arriving = Enumerable.Range(0, newText.Length).Where(i => !keptNew.Contains(i) && newText[i] != ' ').ToList();
        var leaving = Enumerable.Range(0, oldText.Length).Where(i => !keptOld.Contains(i) && oldText[i] != ' ').ToList();
        for (var k = 0; k < leaving.Count; k++)
        {
            var p = Turn(k, leaving.Count, seconds, animation, 0);
            var faded = Math.Clamp(p, 0, 1);
            result.Add(new GlyphState(true, leaving[k], leaving[k], null, p, 1 - faded, 1 - (1 - StartScale) * p, MaxBlur * faded, -Travel * p));
        }
        for (var k = 0; k < arriving.Count; k++)
        {
            var p = Turn(k, arriving.Count, seconds, animation, ArrivalShare * animation.Duration);
            var shown = Math.Clamp(p, 0, 1);
            result.Add(new GlyphState(false, arriving[k], null, arriving[k], p, shown, StartScale + (1 - StartScale) * p, MaxBlur * (1 - shown), Travel * (1 - p)));
        }
        return result;
    }

    // One letter's progress: the change's own curve from its turn, the turns spread evenly, first to last, over
    // SpreadShare of the change's duration (so a short word's letters go far apart and a long one's close together).
    // New letters start a little after the change (the video's first arriving letter about 40 ms after the glide of
    // the kept "High" began, and "M" of Max about as late), leaving ones at once.
    private static double Turn(int order, int count, double seconds, Animation animation, double wait)
    {
        var delay = wait + (count <= 1 ? 0 : order * SpreadShare * animation.Duration / (count - 1));
        var local = seconds - delay;
        if (local <= 0) return 0;
        return local >= animation.Duration - 1e-9 ? 1 : animation.Progress(local);
    }

    /// <summary>
    /// The characters both strings keep, as (old index, new index) pairs in order: their common beginning and their
    /// common end ("High" of "High" and "Extra High"); nothing in the middle is matched.
    /// </summary>
    internal static List<(int Old, int New)> KeptLetters(string a, string b)
    {
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
        var pairs = new List<(int, int)>();
        for (var i = 0; i < prefix; i++) pairs.Add((i, i));
        for (var j = suffix; j > 0; j--) pairs.Add((a.Length - j, b.Length - j));
        return pairs;
    }

    // Carried: the in-flight state of the old text's letters when this change interrupted another; Interrupted: that
    // change, whose leaving letters still play out; NewText: the text a change went to, set once it is interrupted.
    private sealed record Morph(string Old, TextLayout OldLayout, IBrush? OldBrush, double OldWidth, Animation Animation, ContinuousInstant Start,
        Dictionary<int, (GlyphState Glyph, double X)>? Carried, Morph? Interrupted)
    {
        public string NewText { get; init; } = "";
    }
}
