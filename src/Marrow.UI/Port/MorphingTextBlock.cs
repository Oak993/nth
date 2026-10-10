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
//   apart, "Extra" about 30 ms, the nine leaving letters of "Extra High" about 20 ms), and each then follows the slider's
//   snappy curve (the first arriving "E" tracks it from about 40 ms after the change); so the last letters settle about
//   0.18 s after the change's own duration; they keep that pace when the change itself runs on another curve (the changes
//   into and out of maximum run on smooth(0.3), EffortSlider.swift:582; final review round 5, LETTERS' PACE below);
// - leaving letters keep the old colour, arriving ones wear the new one from their first frame (an orange "M" beside a
//   pink leaving "High");
// - a gradient title is filled through the new text's own box, set at the label's moving leading edge: its colours span
//   the new text from the first frame, so letters gliding or carried along pass through them (the "passing gradient" of
//   the 6 Luna pair's title, 183.3-185.4 s), and nothing outside that box shows ("Medium" -> "High" 184.24 s: "Medi"
//   coloured across the whole sweep, "um" gone at once; "Extra High" -> "Max" 184.715 s: "Extra" cut through its "a",
//   "High" gone). A plain colour draws every letter (the pink "High" fading past the chevron, 35.12-35.29 s).
// ENGINEERING DECISION, UNVERIFIED numbers fitted to those frames: travel 0.4 em, half-size start, 4 pt blur, the turns
// spread over 55 % of the letters' pace's duration (the slider's snappy 0.2: 0.14 s), new letters starting 15 % of it
// (0.04 s) late, scaling about each letter's centre.
// Side by side with the video in study\effort-title-2026-10-03\sbs_*.png.
// While a change runs every letter is drawn as its own glyph run of one (so no neighbour is drawn with it and a gradient
// can be pinned to that box), by the text renderer as at rest, so a kept letter keeps its weight and edges as a change
// takes it over and hands it back (REVIEW round 1: drawn as outlines it snapped between greyscale geometry and the text
// renderer's drawing); the blur is seventeen offset copies (Avalonia's DrawingContext has no per-draw blur).
// DEVIATION OD-0094 (owner ruling 2026-10-03): with DriftPeriod set (the effort title: 7 s), a gradient's colours also
// drift at rest, mirrored so the loop never jumps, on one phase shared with the change's own drawing (a change takes it
// over and hands it back without a jump); still under reduce motion; frames only while the label is shown. ENGINEERING
// DECISION, the period: 7 s per mirrored loop moves the colours one text width in 3.5 s, about 20 pt/s on "Max" to 30
// on "Extra High": slow beside the track's own sheen (EffortSlider.swift:1208, a 2.8 s sweep) so the title stays calm
// and never races the slider under it, yet visibly alive within a glance of a few seconds.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    internal const double SpreadShare = 0.55; // the letters' turns, first to last, as a share of the letters' pace's duration
    internal const double ArrivalShare = 0.15; // how long, as a share of that duration, the first new letter waits

    // LETTERS' PACE (final review round 5). SOURCE FACT (EffortSlider.swift:579-583): a change into or out of maximum runs the
    // effort title on smooth(0.3), not the slider's snappy(0.2) (:631); 2-chat-use.mov agrees for the label's frame (the
    // chevron's centre x, frame by frame: Extra High -> Max and Max -> Extra High follow smooth(0.3), rms 0.013 and 0.007, and
    // High -> Extra High snappy(0.2), rms 0.008; work/claude-verify/resume-2026-10-03/effort-rev-r5-chevron/chevron_fit.out).
    // The letters do not slow with the frame. SOURCE FACT, read by eye (2-chat-use.mov, Extra High -> Max from 35.051 s, frames
    // 35.037-35.337 s; final review round 6): "Max" arrives about 70 ms a letter (the snappy pace's 0.142 s of turns gives 71 ms,
    // the 0.181 s that 55 % of smooth(0.3)'s settling would give 90 ms), its "x" is already visible at 35.250 s (the snappy pace
    // turns it at 35.232 s, smooth's at 35.282 s) and the leaving "h" is already faint there (smooth's pace would still draw it
    // whole); the nine leaving letters of "Extra High" going about 20 ms apart are consistent with either pace (17.8 ms on
    // snappy's, 22.6 ms on smooth's) and tell them apart by nothing. The narrowing takeover's "a" (TAKEOVER below) keeps the
    // video's ink only if it arrives at the snappy pace (on smooth(0.3) it is still 0.76 arrived at its fullest frame).
    // ENGINEERING DECISION, with no new number: a change's arriving and leaving letters take their turns and run their curve
    // at one pace, the slider's snappy(0.2) on which they were measured (the first new letter 0.04 s in, the turns over 0.14
    // s), whatever the change's animation; the change's own animation moves the label's frame (its width, so the chevron, and
    // where arriving letters stand from it) and the kept letters, which ride with the frame (KeptLead, NarrowingKeptLead). The
    // other sites (the usage count on Chrome.panelSlide, the copy label and MiddleTruncatingText on snappy(0.22)) roll at the
    // same pace: extrapolated, no video shows them.
    internal static readonly Animation LetterPace = Animation.Snappy(0.2);

    // WIDENING: the kept letters run ahead of the label's frame (video, High -> Extra High at 34.84 s, every frame: the gap
    // between the kept "High" and the chevron goes 9, 1, 3, 4, 6, 7, then back to 9; "High" makes a third of its way before
    // the chevron moves). ENGINEERING DECISION, fitted to those five frames (worst frame 1.5 px off, the frame's curve fitted on
    // the chevron alone within 0.4 px): the kept letters run the change's own curve 23 ms ahead of the frame, drawn out by
    // 1.22 (for the slider's snappy 0.2 spring, a 0.244 spring of the same damping), so they lead it at once, most at
    // about 20 ms, and meet it again without passing it.
    internal const double KeptLead = 0.023;
    internal const double KeptStretch = 1.22;

    // NARROWING (final review, round 1: the widening's lead, applied to a narrowing, pulls "High" away from the chevron, as
    // the gap is dW times the frame's progress less the letters', and the chevron is what moves first). SOURCE FACT, measured
    // frame by frame in 2-chat-use.mov, Extra High -> High at 35.77-35.96 s (the change takes over the unfinished Max ->
    // Extra High, as the drag goes down): the gap between the kept "High" and the chevron stays below its settled 8 px while
    // the chevron goes, by 2, 5, 3 px at 60, 73 and 80 % of the chevron's way, then 1, 1, 2, 2, 2 px at 93 and 100 %, and is
    // back at 8 px at 35.965. The kept letters never lead the frame here; they follow it. ENGINEERING DECISION, fitted to those
    // frames by sweeping the kept letters' clock against the video (study effort-title-2026-10-03, sweep_*): 10 ms behind
    // the frame on its own curve, not drawn out (-0.010, 1: 6, 5, 3, 3, 3, 2, 2, 2, 1, 1 px short; the widening's 0.023 and 1.22
    // leave the gap 3 to 0 px wider than settled, the lock-step holds it at 0, and 0.0 with 1.2 keeps it 5 px short through
    // most of the chevron's way). REFITTED in final review round 5 on the source's curves (the round 1 sweep ran Max -> Extra
    // High on snappy(0.2) from 35.635 s and taken over 118 ms in; it runs on smooth(0.3) from 35.623 s, taken over 130 ms in:
    // LETTERS' PACE): 5 ms behind the frame. At the video's frames 54, 69, 90, 112, 134, 154, 175, 190 and 212 ms after the
    // takeover (shortfalls 2, 5, 3, 1, 1, 2, 2, 2, 0 px), -0.005 is short by 6, 5, 2, 1, 1, 1, 1, 1, 1 (rms 1.53 px; 0.79 without
    // the first frame); -0.010 leaves the kept "High" too faint to show at 54 ms and 6, 4, 2, 2, 1, 1, 1, 1 short after (rms
    // 1.0 without that frame), and misses the late bound of MorphingTextBlockNarrowingTests by a pixel; 0.0 (the lock-step)
    // 4, 3, 2, 0, 1, 1, 1, 1, 1 (rms 1.25; 1.12 without the first) no longer follows the frame (work/claude-verify/
    // resume-2026-10-03/effort-fix-r7.sweep.txt, probe on the label's own rendering).
    internal const double NarrowingKeptLead = -0.005;
    internal const double NarrowingKeptStretch = 1;

    // TAKEOVER (final review, round 2: the leaving letters of a change that takes over an unfinished one were frozen as they
    // were at the takeover, so the letters still arriving left faint and too soon). SOURCE FACT, 2-chat-use.mov frame by
    // frame (study effort-title-2026-10-03, sbs_takeover_dec.png and sbs_takeover_inc.png): the letters the change taken over
    // was still bringing in go on arriving (whole by 35.078-35.100 s in Extra High -> Max at 35.05 s, the "t", "r" and "a" of
    // "Extra" plainly drawn at 35.807-35.843 s in Extra High -> High at 35.753 s) and then leave in turn, rising and fading:
    // in the first, all nine letters of "Extra High" leave about 18 ms apart, as any nine do; in the second the five of
    // "Extra" leave closer than five from rest (36 ms apart) and farther than nine: the "E" starts its leave at once (about
    // 35.76 s) and the "a", measured alone over the settled "h" (ED-238 final round 3 (4); effort-fix-r5.narrowing-takeover-
    // measure.txt), keeps 1.00, 0.75, 0.50, 0.28, 0.22, 0.04 and 0 of its ink at 35.865, 35.887, 35.907, 35.928, 35.943, 35.965
    // and 35.987 s, rising as it fades: this very leave with its turn about 105 ms after the takeover, about 25 ms a place.
    // ENGINEERING DECISION, fitted to those two events with no new number (final review round 4, lead decision 2026-10-03):
    // a change's leaving letters take their turns first to last over SpreadShare of its duration as from rest, but share
    // them with the letters the changes it interrupts are still taking away, each counted by how much of it still shows
    // (Lingering: its opacity as drawn when the change begins). The widening's takeover interrupts High -> Extra High, which
    // takes nothing away: the nine go 17.8 ms apart, as before. The narrowing's interrupts Max -> Extra High, which the source
    // runs on smooth(0.3) from 35.623 s (final review round 5; round 4 fitted it on snappy(0.2) from 35.635 s), 130 ms in, its
    // letters at their pace (LETTERS' PACE): the "M", "a" and "x" of "Max" still show 0.03, 0.40 and 1, so the five of
    // "Extra" share the turns of 6.43, 26.2 ms apart, the "a" at 105 ms, where the frames put it. Measured on the label's
    // drawing at the video's frames (work/claude-verify/resume-2026-10-03/effort-fix-r7.takeover-probe.txt): the "a" 0.96,
    // 0.75, 0.50, 0.29, 0.18, 0.08, 0.03 at 35.865-35.987 s against the ink above (rms 0.029; the ink is a floor, as a leaving
    // letter also shrinks and blurs; round 4's fit on snappy: 0.92, 0.71, 0.47, 0.27, 0.16, 0.07, 0.02, rms 0.045); "x", "t",
    // "r", "a" at 0.75, 0.93, 0.86, 0.74 at 35.807 s; the widening's "r" and "a" 0.95 and 0.79 at 35.100 s, 0.10 and 0.19 at
    // 35.207 s, unchanged. Side by side: sbs_takeover_dec_r6.png, sbs_dec_after_r6.png, sbs_takeover_inc_r6.png,
    // sbs_inc_after_r6.png, sbs_pair_after_r6.png (round 4's r5 and r4 sheets kept). Replaced: round 2's
    // turns by place among all the old line's letters, the kept ones counted, which put the "a" at 72 ms (0.57, 0.35, 0.19,
    // 0.09, 0.05, 0.01; effort-fix-r6.takeover-probe.head.txt) and, for a change keeping a prefix ("12" -> "13" -> "14"),
    // left the leaving "3" waiting for the last turn of the line; it now leaves at once. The "x", "t" and "r" cannot be
    // measured apart from the moving "High" (35.807-35.843 s, read by eye: "xtra" then "Hra" plainly drawn).

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
    private AmbientMotionController? driftAmbient;
    private double? driftAmbientLast;
    private bool driftPending;

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
        var oldLayout = CreateTextLayout(old);
        var earlier = Running(interrupted);
        morph = new Morph(old, oldLayout, Foreground, Bounds.Width, animation, SwiftClock.Now, carried, earlier)
        {
            Taken = carried is null ? null : interrupted,
            Lingering = Lingering(earlier, oldLayout),
        };
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
        Resolve(state, text, layout, Elapsed(state), newLettersOnly: true).ToDictionary(g => g.Glyph.NewIndex!.Value, g => (g.Glyph, g.X));

    // The letters of the text a change taken over was going to, as that change has them now (it runs on under the leave).
    private static Dictionary<int, (GlyphState Glyph, double X)> Live(Morph taken, TextLayout layout) => InFlight(taken, taken.NewText, layout);

    // How much still shows, now, of the letters that the changes a new one interrupts are still taking away: each leaving
    // letter of theirs counted by its opacity as drawn (one half gone counts a half), over the same chain and layouts the
    // drawing walks (RenderTextLayout). The new change's own leaving letters share their turns with these (see TAKEOVER).
    private static double Lingering(Morph? chain, TextLayout after)
    {
        var showing = 0.0;
        for (var earlier = chain; earlier is not null; after = earlier.OldLayout, earlier = earlier.Interrupted)
        {
            if (Elapsed(earlier) >= Length(earlier.Animation)) continue;
            foreach (var (glyph, _) in Resolve(earlier, earlier.NewText, after, Elapsed(earlier)))
                if (glyph.NewIndex is null) showing += Math.Clamp(glyph.Opacity, 0, 1);
        }
        return showing;
    }

    // The last new letter takes its turn (ArrivalShare + SpreadShare) of its pace's duration in, then runs its curve; the frame
    // and the kept letters end within the same share past the change's own duration (KeptStretch, NarrowingKeptLead): the
    // change lasts until both have.
    internal static double Length(Animation animation) => Math.Max(animation.Duration, LetterPace.Duration) * (1 + ArrivalShare + SpreadShare);

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
        driftAmbient?.Dispose(); driftAmbient = null; driftAmbientLast = null;
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
        if (AmbientWindowState.Acquire(this) is { } ambientWindow)
        {
            driftAmbient = new(ambientWindow, this);
            driftAmbient.Bind(RequestDriftFrame);
        }
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

    // A new colour for the title: the drift's own brushes (their stops copied once, reflected past their ends so the
    // moving window never meets an edge) are rebuilt.
    private void Adopt()
    {
        driftBrushes.Clear();
        UpdateDrift();
    }
    /// <summary>C25 live RGB stops, already prepared by the palette actor. No second clock or new stop arrays.</summary>
    public void UpdateLiveForeground()
    {
        if(Foreground is ILinearGradientBrush gradient)
        {
            if(pinnedStops.TryGetValue(gradient,out var stops))for(var i=0;i<Math.Min(stops.Length,gradient.GradientStops.Count);i++)stops[i]=new ImmutableGradientStop(gradient.GradientStops[i].Offset,gradient.GradientStops[i].Color);
            foreach(var item in driftBrushes)for(var i=0;i<Math.Min(item.Brush.GradientStops.Count,gradient.GradientStops.Count);i++)item.Brush.GradientStops[i].Color=gradient.GradientStops[i].Color;
        }
        InvalidateVisual();
    }

    // One mutable brush per run of the line at rest: the drift moves the colours by moving these brushes' two ends, frame
    // after frame, and nothing else. The compositor carries a brush's own change to what it already draws (a brush is a
    // composition resource the drawn label observes), so the label is not drawn again (OD-0094: cheap, no per-frame
    // allocation beyond the two changes' notices; a TranslateTransform on the brush would be the natural carrier, but
    // Avalonia 12.1.2 does not send a mutable transform's change to the compositor: TranslateTransform.OnPropertyChanged
    // only raises Changed, read in the decompiled Avalonia.Base, so its drift stood still on screen).
    private readonly List<(LinearGradientBrush Brush, Point Start, Point End)> driftBrushes = [];
    private double driftWidth;

    // The title's gradient through the box, in the space of the line's run number <paramref name="index"/> (drawn moved
    // by origin, then by its place), its ends moved by the drift.
    private LinearGradientBrush Drifted(ILinearGradientBrush gradient, Rect box, Point origin, Matrix place, int index)
    {
        while (driftBrushes.Count <= index)
        {
            var stops = new GradientStops();
            foreach (var stop in gradient.GradientStops) stops.Add(new GradientStop(stop.Color, stop.Offset));
            driftBrushes.Add((new LinearGradientBrush { GradientStops = stops, SpreadMethod = GradientSpreadMethod.Reflect, Opacity = gradient.Opacity }, default, default));
        }
        var into = -(Vector)origin - new Vector(place.M31, place.M32);
        var brush = driftBrushes[index].Brush;
        driftBrushes[index] = (brush, gradient.StartPoint.ToPixels(box) + into, gradient.EndPoint.ToPixels(box) + into);
        driftWidth = box.Width;
        MoveDrift();
        return brush;
    }

    // Every rest brush's ends to the drift's place now.
    private void MoveDrift()
    {
        var along = new Vector(DriftOffset(driftWidth), 0);
        foreach (var (brush, start, end) in driftBrushes)
        {
            brush.StartPoint = new RelativePoint(start + along, RelativeUnit.Absolute);
            brush.EndPoint = new RelativePoint(end + along, RelativeUnit.Absolute);
        }
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
            driftAmbientLast = null;
            driftFrames = FrameClock.Source.Current;
            driftCallback ??= OnDriftFrame;
            RequestDriftFrame();
        }
        else if (driftCallback is not null) { driftFrames?.CancelFrame(driftCallback); driftPending = false; }
    }

    private void RequestDriftFrame()
    {
        var now = SwiftClock.Provider.Current.GetTimestamp() / (double)SwiftClock.Provider.Current.TimestampFrequency;
        driftAmbient?.Sample(now, false);
        if (!drifting || driftPending || (driftAmbient is not null && !driftAmbient.Request(now, false))) return;
        driftPending = true;
        driftFrames!.RequestFrame(driftCallback!);
    }

    private void OnDriftFrame(TimeSpan frameTime)
    {
        driftPending = false;
        if (!drifting) return;
        var now = SwiftClock.Now;
        if (driftAmbient is not null)
        {
            var seconds = now.Ticks / (double)TimeSpan.TicksPerSecond;
            driftAmbient.Sample(seconds, false);
            if (!driftAmbient.Available) return;
            // ED-NEW-frame-pacing: the drift moves at the 60 Hz slots of the refreshes (the frame's time names its refresh)
            if (driftAmbient.Pace(frameTime.Ticks, seconds, false) == AmbientMotionController.Paced.Wait) { RequestDriftFrame(); return; }
            var phaseTime = driftAmbient.Clock.Time;
            if (driftAmbientLast is { } previous) driftSeconds = (driftSeconds + Math.Max(0, phaseTime - previous)) % driftPeriod;
            driftAmbientLast = phaseTime;
        }
        // A long gap (a stalled frame) moves it on by at most a tenth of a second, never a lurch.
        else if (driftLast is { } last) driftSeconds = (driftSeconds + Math.Min(0.1, Math.Max(0, (now - last).TotalSeconds))) % driftPeriod;
        driftLast = now;
        // At rest only the brushes' translations move; a change draws every frame anyway and reads the phase itself.
        if (morph is null) MoveDrift();
        else InvalidateVisual();
        RequestDriftFrame();
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

    // The text's own box: from where its line starts (a line aligned inside a wider label starts in from its edge), its
    // width, down the label.
    private Rect TextBox(TextLayout layout, Point origin)
    {
        var start = layout.TextLines.Count > 0 ? layout.TextLines[0].Start : 0;
        return new Rect(origin.X + start, origin.Y, layout.WidthIncludingTrailingWhitespace, Math.Max(layout.Height, Bounds.Height - origin.Y));
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (morph is not { } state || TextLayout is not { } layout)
        {
            if (driftPeriod > 0 && Foreground is ILinearGradientBrush gradient && TextLayout is { } still)
            {
                // At rest a drifting gradient is drawn through the same box and phase as during a change (OD-0094), by the
                // text renderer as the line's own glyph runs (as a change draws its letters, so neither snaps to the other).
                var box = TextBox(still, origin);
                using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                {
                    var runs = LineRuns(still);
                    for (var i = 0; i < runs.Count; i++)
                        using (context.PushTransform(runs[i].Place))
                            context.DrawGlyphRun(Drifted(gradient, box, origin, runs[i].Place, i), runs[i].Run);
                }
                return;
            }
            base.RenderTextLayout(context, origin);
            return;
        }
        // A gradient title is filled through the new text's own box, set at the label's moving leading edge: the gradient
        // spans the new text's width from the first frame and nothing outside that box shows (video, the 6 Luna pair:
        // "Medium" -> "High" at 184.24 s shows "Medi" coloured across the whole sweep and its "um" gone at once; "Extra
        // High" -> "Max" at 184.715 s cuts "Extra" through its "a" and drops "High"). A plain colour draws every letter.
        var frame = TextBox(layout, origin);
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
    // interrupted change start from where that one had them (its leaving ones go on with that change, see below).
    // newLettersOnly: only the letters of the new text (what a change taken over hands on).
    private static List<(GlyphState Glyph, double X)> Resolve(Morph state, string newText, TextLayout newLayout, double seconds, bool newLettersOnly = false)
    {
        var result = new List<(GlyphState, double)>();
        Dictionary<int, (GlyphState Glyph, double X)>? live = null;
        foreach (var glyph in Glyphs(state.Old, newText, seconds, state.Animation, newLayout.WidthIncludingTrailingWhitespace < state.OldLayout.WidthIncludingTrailingWhitespace, state.Lingering))
        {
            if (newLettersOnly && glyph.NewIndex is null) continue;
            var x = glyph.X(state.OldLayout, newLayout);
            var drawn = glyph;
            if (glyph.OldIndex is { } o && state.Carried is { } carried && carried.TryGetValue(o, out var from))
            {
                if (glyph.NewIndex is null)
                {
                    // A letter the change taken over was still bringing in goes on arriving under its leave, on that change's
                    // own clock, and is not frozen as it was at the takeover. SOURCE FACT (2-chat-use.mov, final review round
                    // 2, study effort-title-2026-10-03/sbs_takeover_*): Extra High -> High taking over Max -> Extra High at
                    // 35.753 s, the "t", "r" and "a" of "Extra", faint at the takeover, are plainly drawn at 35.807-35.843 and
                    // the "a" on the "h" at 35.865-35.907; Extra High -> Max taking over High -> Extra High at 35.05 s, the
                    // small, faint "r" and "a" are whole at 35.078-35.122 and then rise away fading.
                    var now = state.Taken is { } taken && (live ??= Live(taken, state.OldLayout)).TryGetValue(o, out var going) ? going : from;
                    drawn = glyph with
                    {
                        Opacity = now.Glyph.Opacity * glyph.Opacity,
                        Scale = now.Glyph.Scale * glyph.Scale,
                        Blur = Math.Max(now.Glyph.Blur, glyph.Blur),
                        Rise = now.Glyph.Rise + glyph.Rise,
                    };
                    x = now.X;
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
                    // From where it was to its place in the new line, along the change's own curve: not from where it was
                    // to the point the change has brought it to (x), which eased the letter twice and left it behind.
                    x = from.X + (newLayout.HitTestTextPosition(glyph.NewIndex!.Value).X - from.X) * q;
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
        // The letter's own glyph, drawn by the text renderer as a run of one placed as the line places it (so kerning
        // stays, and a kept letter is drawn exactly as the label draws it at rest: no snap of weight or edge as a change
        // takes it over or hands it back): nothing of its neighbours is drawn with it. Each line carries its own brush:
        // the old line the old colour, the live one the new colour.
        if (brush is null || !LetterRuns(source).TryGetValue(glyph.SourceIndex, out var letter)) return;
        using (context.PushTransform(transform))
        {
            if (brush is ILinearGradientBrush gradient)
            {
                // One brush for the letter, pinned through its own place: its blur copies (at most 4 pt off) share it.
                if (!(letter.Place * transform).TryInvert(out var toLetter)) return;
                var pinned = Pinned(gradient, frame, toLetter);
                DrawBlurred(context, glyph.Opacity, glyph.Blur, (dx, dy) =>
                {
                    using (context.PushTransform(Matrix.CreateTranslation(dx, dy)))
                    using (context.PushTransform(letter.Place))
                        context.DrawGlyphRun(pinned, letter.Run);
                });
            }
            else
                DrawBlurred(context, glyph.Opacity, glyph.Blur, (dx, dy) =>
                {
                    using (context.PushTransform(Matrix.CreateTranslation(dx, dy)))
                    using (context.PushTransform(letter.Place))
                        context.DrawGlyphRun(brush, letter.Run);
                });
        }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextLayout, Dictionary<int, (GlyphRun Run, Matrix Place)>> letterRuns = new();

    // Each letter as a glyph run of one by its character index, with where it goes in the layout's own coordinates, as
    // TextLayout.Draw places it.
    private Dictionary<int, (GlyphRun Run, Matrix Place)> LetterRuns(TextLayout layout)
    {
        if (letterRuns.TryGetValue(layout, out var known)) return known;
        var letters = new Dictionary<int, (GlyphRun, Matrix)>();
        foreach (var (shaped, place) in ShapedRuns(layout))
        {
            var buffer = shaped.ShapedBuffer;
            var baseline = shaped.GlyphRun.BaselineOrigin.Y;
            var at = 0.0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var info = buffer[i];
                if (!letters.ContainsKey(info.GlyphCluster))
                {
                    var single = new GlyphRun(buffer.GlyphTypeface, buffer.FontRenderingEmSize, shaped.Text,
                        new[] { new GlyphInfo(info.GlyphIndex, info.GlyphCluster, info.GlyphAdvance, info.GlyphOffset) }, new Point(0, baseline));
                    letters[info.GlyphCluster] = (single, Matrix.CreateTranslation(at, 0) * place);
                }
                at += info.GlyphAdvance;
            }
        }
        letterRuns.AddOrUpdate(layout, letters);
        return letters;
    }

    // The line's shaped runs, each with where TextLayout.Draw puts it (TextLine.Draw: the run moved along the line and
    // down to the line's baseline less the run's own), in the layout's own coordinates.
    private static IEnumerable<(ShapedTextRun Run, Matrix Place)> ShapedRuns(TextLayout layout)
    {
        double top = 0;
        foreach (var line in layout.TextLines)
        {
            var x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped)
                    yield return (shaped, Matrix.CreateTranslation(x, top + line.Baseline - shaped.GlyphRun.BaselineOrigin.Y));
                if (run is DrawableTextRun drawable) x += drawable.Size.Width;
            }
            top += line.Height;
        }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextLayout, List<(GlyphRun Run, Matrix Place)>> lineRuns = new();

    // The line's own glyph runs with their places, as TextLayout.Draw draws them.
    private List<(GlyphRun Run, Matrix Place)> LineRuns(TextLayout layout)
    {
        if (lineRuns.TryGetValue(layout, out var known)) return known;
        var runs = ShapedRuns(layout).Select(pair => (pair.Run.GlyphRun, pair.Place)).ToList();
        lineRuns.AddOrUpdate(layout, runs);
        return runs;
    }

    private static void DrawBlurred(DrawingContext context, double opacity, double blur, Action<double, double> draw)
    {
        void Draw(double dx, double dy, double alpha)
        {
            if (alpha >= 1)
            {
                draw(dx, dy); // whole: no layer, so the text renderer draws it as the label does at rest
                return;
            }
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
    // Each colour's stops, copied once as immutable stops and shared by every letter's brush (REVIEW round 1: a stop list
    // and a brush for every letter's every blur copy each frame, about 430 KB a frame for a gradient change).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ILinearGradientBrush, ImmutableGradientStop[]> pinnedStops = new();

    // The letter's brush is a small immutable one (its ends are the letter's own each frame), on the cached stops.
    internal ImmutableLinearGradientBrush Pinned(ILinearGradientBrush gradient, Rect frame, Matrix toLetter)
    {
        // The drift moves the colours along the same box at rest and here: one phase, so a change takes it over and
        // hands it back without a jump (OD-0094).
        var along = new Vector(DriftOffset(frame.Width), 0);
        Point At(RelativePoint point) => toLetter.Transform(point.ToPixels(frame) + along);
        if (!pinnedStops.TryGetValue(gradient, out var stops))
        {
            stops = gradient.GradientStops.Select(stop => new ImmutableGradientStop(stop.Offset, stop.Color)).ToArray();
            pinnedStops.AddOrUpdate(gradient, stops);
        }
        return new ImmutableLinearGradientBrush(stops, gradient.Opacity, spreadMethod: driftPeriod > 0 ? GradientSpreadMethod.Reflect : gradient.SpreadMethod,
            startPoint: new RelativePoint(At(gradient.StartPoint), RelativeUnit.Absolute), endPoint: new RelativePoint(At(gradient.EndPoint), RelativeUnit.Absolute));
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
    /// <param name="lingering">How much still shows of the letters the changes this one interrupted are still taking away
    /// (0 for a change from rest): the leaving letters share their turns with them (see TAKEOVER in the file).</param>
    internal static List<GlyphState> Glyphs(string oldText, string newText, double seconds, Animation animation, bool narrowing = false, double lingering = 0)
    {
        // The kept letters' own clock (see KeptLead, NarrowingKeptLead).
        var keptTime = (seconds + (narrowing ? NarrowingKeptLead : KeptLead)) / (narrowing ? NarrowingKeptStretch : KeptStretch);
        if (keptTime < 0) keptTime = 0;
        var shared = keptTime >= animation.Duration - 1e-9 ? 1 : animation.Progress(keptTime);
        var pairs = KeptLetters(oldText, newText);
        var result = new List<GlyphState>();
        var keptOld = new HashSet<int>(pairs.Select(pair => pair.Old));
        var keptNew = new HashSet<int>(pairs.Select(pair => pair.New));
        foreach (var (o, n) in pairs)
            result.Add(new GlyphState(false, n, o, n, shared, 1, 1, 0, 0));
        var arriving = Enumerable.Range(0, newText.Length).Where(i => !keptNew.Contains(i) && newText[i] != ' ').ToList();
        var leaving = Enumerable.Range(0, oldText.Length).Where(i => !keptOld.Contains(i) && oldText[i] != ' ').ToList();
        // The leaving letters take their turns first to last, spread as if the letters still being taken away by the changes
        // this one interrupted were among them, each counted by how much of it still shows (lingering; TAKEOVER).
        for (var k = 0; k < leaving.Count; k++)
        {
            var p = Turn(k, leaving.Count + lingering, seconds, 0);
            var faded = Math.Clamp(p, 0, 1);
            result.Add(new GlyphState(true, leaving[k], leaving[k], null, p, 1 - faded, 1 - (1 - StartScale) * p, MaxBlur * faded, -Travel * p));
        }
        for (var k = 0; k < arriving.Count; k++)
        {
            var p = Turn(k, arriving.Count, seconds, ArrivalShare * LetterPace.Duration);
            var shown = Math.Clamp(p, 0, 1);
            result.Add(new GlyphState(false, arriving[k], null, arriving[k], p, shown, StartScale + (1 - StartScale) * p, MaxBlur * (1 - shown), Travel * (1 - p)));
        }
        return result;
    }

    // One letter's progress: the letters' pace's curve from its turn, the turns spread evenly, first to last, over
    // SpreadShare of its duration (so a short word's letters go far apart and a long one's close together), whatever the
    // change's own animation (LETTERS' PACE). New letters start a little after the change (the video's first arriving
    // letter about 40 ms after the glide of the kept "High" began, and "M" of Max about as late), leaving ones at once.
    // The count need not be whole: a leaving letter of an interrupted change still half shown counts a half (TAKEOVER).
    private static double Turn(int order, double count, double seconds, double wait)
    {
        var delay = wait + (count <= 1 ? 0 : order * SpreadShare * LetterPace.Duration / (count - 1));
        var local = seconds - delay;
        if (local <= 0) return 0;
        return local >= LetterPace.Duration - 1e-9 ? 1 : LetterPace.Progress(local);
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
    // change, whose leaving letters still play out; NewText: the text a change went to, set once it is interrupted; Taken:
    // the change this one took over, whose arrivals its leaving letters go on with (see Resolve); Lingering: how much of
    // the interrupted changes' leaving letters still showed when this one began (see Lingering, TAKEOVER).
    private sealed record Morph(string Old, TextLayout OldLayout, IBrush? OldBrush, double OldWidth, Animation Animation, ContinuousInstant Start,
        Dictionary<int, (GlyphState Glyph, double X)>? Carried, Morph? Interrupted)
    {
        public string NewText { get; init; } = "";

        public Morph? Taken { get; init; }

        public double Lingering { get; init; }
    }
}
