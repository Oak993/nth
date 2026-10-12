using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W5, the stack of task cards (E:/Droppy/work/design/web-surfaces/DESIGN.md 2.6 "Open: the new pane fades
// in and scales from 0.96 to 1 at its final slot while the others spring to theirs. Close: it fades and scales to 0.96 while the
// others fill the space. One motion, never a sequence", "Transforms and opacity only"; 3.10 "Order: running first ... then
// finished in a Done group that fades after 10 minutes"; ED-NEW-web-w5-1). No Swift counterpart. The stack lays every card out
// at its final slot at once; a card that was somewhere else is drawn there and springs to its slot by a render offset (an
// interruptible spring, so a change in mid-flight bends the motion), a new card fades and scales in, and a card that goes
// fades and scales out while the others close the gap. Nothing but opacity and a transform moves.

namespace Marrow.App.Views.Panes;

/// <summary>The animated stack of <see cref="JobCard"/>s.</summary>
public sealed class JobStack : Panel
{
    private sealed class Entry(JobCard card)
    {
        public JobCard Card { get; } = card;

        /// <summary>Where the card is drawn, in the stack's coordinates; it springs to its slot (<see cref="LayoutY"/>) and keeps its velocity when the slot moves.</summary>
        public SplitterSpring Y { get; } = new(0, epsilon: 0.05);

        public SplitterSpring Opacity { get; } = new(1, epsilon: 0.002);

        public SplitterSpring Scale { get; } = new(1, epsilon: 0.0005);

        public ScaleTransform ScaleTransform { get; } = new(1, 1);

        public TranslateTransform Translate { get; } = new(0, 0);

        public double LayoutY { get; set; }

        public double LayoutHeight { get; set; }

        public bool HasLayout { get; set; }

        public bool Leaving { get; set; }

        public bool IsMoving => Y.IsMoving || Opacity.IsMoving || Scale.IsMoving;

        public void Settle()
        {
            Y.Settle();
            Opacity.Settle();
            Scale.Settle();
        }

        public bool Step(double seconds)
        {
            var a = Y.Step(seconds);
            var b = Opacity.Step(seconds);
            var c = Scale.Step(seconds);
            return a | b | c;
        }
    }

    public const double OpenScale = 0.96;

    private readonly JobCardCommands commands;
    private readonly Dictionary<string, Entry> entries = [];
    private readonly List<Entry> order = [];
    private readonly List<Entry> leaving = [];
    private Action<TimeSpan>? pendingFrame;
    private IFrameSource? frameSource;
    private TimeSpan? lastFrame;
    private bool animate;
    private int measures;
    private double arrangedWidth = double.NaN;

    public JobStack(JobCardCommands commands)
    {
        this.commands = commands;
        DetachedFromVisualTree += (_, _) => SettleAll();
    }

    /// <summary>The cards in display order (a card on its way out is not listed).</summary>
    public IReadOnlyList<JobCard> Cards => order.Select(entry => entry.Card).ToList();

    public JobCard? CardOf(string jobId) => entries.TryGetValue(jobId, out var entry) && !entry.Leaving ? entry.Card : null;

    /// <summary>A card is on its way (a slot, a fade) or a card is leaving.</summary>
    public bool IsMoving => pendingFrame is not null || leaving.Count > 0 || entries.Values.Any(entry => entry.IsMoving);

    /// <summary>How many times the stack has been measured (tests: a tick that changes no card's shape measures nothing).</summary>
    public int Measures => measures;

    /// <summary>The cards' fade and offset right now (tests).</summary>
    public (double Opacity, double Scale, double Offset)? LookOf(string jobId) =>
        entries.TryGetValue(jobId, out var entry) ? (entry.Card.Opacity, entry.ScaleTransform.ScaleX, entry.Translate.Y) : null;

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    /// <summary>
    /// Makes the stack show <paramref name="jobs"/> (already ordered and filtered) as of <paramref name="now"/>. New jobs get cards that
    /// fade in, jobs that are gone have theirs fade out, the order follows the list, and every card that stays gets its live values.
    /// </summary>
    public void Sync(IReadOnlyList<BackgroundJob> jobs, DateTimeOffset now, bool animateChanges = true)
    {
        animate = animateChanges && FrameClock.IsBound && TopLevel.GetTopLevel(this) is not null;
        var ids = new HashSet<string>(jobs.Select(job => job.Id));
        var structural = false;

        foreach (var entry in order.ToList())
        {
            if (ids.Contains(entry.Card.JobId)) continue;
            order.Remove(entry);
            Retire(entry);
            structural = true;
        }

        var next = new List<Entry>(jobs.Count);
        foreach (var job in jobs)
        {
            if (!entries.TryGetValue(job.Id, out var entry))
            {
                entry = new Entry(new JobCard(job, now, commands)) { Card = { RenderTransformOrigin = RelativePoint.Center } };
                entry.Card.RenderTransform = new TransformGroup { Children = { entry.ScaleTransform, entry.Translate } };
                entries[job.Id] = entry;
                Children.Add(entry.Card);
                structural = true;
                if (animate)
                {
                    entry.Opacity.Snap(0);
                    entry.Opacity.SetTarget(1, ReduceMotion);
                    if (!ReduceMotion)
                    {
                        entry.Scale.Snap(OpenScale);
                        entry.Scale.SetTarget(1);
                    }
                    ApplyLook(entry);
                    RequestFrame();
                }
            }
            else
            {
                if (entry.Leaving)
                {
                    // The job came back before its card had gone: it stays, fading in again from where it is.
                    leaving.Remove(entry);
                    entry.Leaving = false;
                    entry.Card.IsHitTestVisible = true;
                    entry.Opacity.SetTarget(1, animate && ReduceMotion);
                    entry.Scale.SetTarget(1);
                    structural = true;
                    RequestFrame();
                }
                entry.Card.Apply(job, now);
            }
            next.Add(entry);
        }

        if (!structural && !order.SequenceEqual(next)) structural = true;
        order.Clear();
        order.AddRange(next);

        if (structural)
        {
            for (var i = 0; i < order.Count; i++)
            {
                var at = Children.IndexOf(order[i].Card);
                if (at != i) Children.Move(at, i);
            }
            InvalidateMeasure();
        }
    }

    private void Retire(Entry entry)
    {
        entry.Leaving = true;
        entry.Card.IsHitTestVisible = false;
        if (!animate)
        {
            entries.Remove(entry.Card.JobId);
            Children.Remove(entry.Card);
            return;
        }
        leaving.Add(entry);
        entry.Opacity.SetTarget(0, ReduceMotion);
        if (!ReduceMotion) entry.Scale.SetTarget(OpenScale);
        RequestFrame();
    }

    /// <summary>Every spring in flight lands on its target in this frame (a pointer pressing a gutter settles the pane, as everywhere).</summary>
    public void SettleAll()
    {
        if (pendingFrame is not null)
        {
            frameSource?.CancelFrame(pendingFrame);
            pendingFrame = null;
        }
        lastFrame = null;
        foreach (var entry in entries.Values.ToList()) entry.Settle();
        Finish();
        foreach (var entry in entries.Values) ApplyLook(entry);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        measures++;
        var width = double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width;
        double y = 0;
        foreach (var entry in order)
        {
            entry.Card.Measure(new Size(width, double.PositiveInfinity));
            y += entry.Card.DesiredSize.Height + JobTokens.CardGap;
        }
        foreach (var entry in leaving) entry.Card.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, Math.Max(0, y - JobTokens.CardGap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        var reduce = ReduceMotion;
        // A change of width is the window or a gutter moving: the cards follow it 1:1 and never glide.
        var resized = double.IsNaN(arrangedWidth) || Math.Abs(arrangedWidth - finalSize.Width) > 0.5;
        arrangedWidth = finalSize.Width;
        foreach (var entry in order)
        {
            var height = entry.Card.DesiredSize.Height;
            entry.Card.Arrange(new Rect(0, y, finalSize.Width, height));
            if (!entry.HasLayout || !animate || resized) entry.Y.Snap(y);
            else if (Math.Abs(entry.Y.Target - y) > 0.5)
            {
                // The slot moved: the card keeps gliding from where it is drawn, with the speed it has, to the new slot.
                entry.Y.SetTarget(y, reduce);
                RequestFrame();
            }
            entry.LayoutY = y;
            ApplyLook(entry);
            entry.LayoutHeight = height;
            entry.HasLayout = true;
            y += height + JobTokens.CardGap;
        }
        foreach (var entry in leaving)
            entry.Card.Arrange(new Rect(0, entry.LayoutY, finalSize.Width, entry.LayoutHeight));
        return finalSize;
    }

    private static void ApplyLook(Entry entry)
    {
        entry.Card.Opacity = Math.Clamp(entry.Opacity.Value, 0, 1);
        var scale = Math.Clamp(entry.Scale.Value, 0.9, 1);
        entry.ScaleTransform.ScaleX = scale;
        entry.ScaleTransform.ScaleY = scale;
        entry.Translate.Y = entry.HasLayout ? entry.Y.Value - entry.LayoutY : 0;
        entry.Card.ZIndex = entry.Leaving ? 0 : 1;
    }

    private void RequestFrame()
    {
        if (pendingFrame is not null) return;
        frameSource = FrameClock.Source.Current;
        pendingFrame = OnFrame;
        frameSource.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        var dt = lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.034) : 1.0 / 60;
        lastFrame = now;
        var any = false;
        foreach (var entry in entries.Values.ToList())
        {
            any |= entry.Step(dt);
            ApplyLook(entry);
        }
        if (any)
        {
            RequestFrame();
            return;
        }
        lastFrame = null;
        // Landed within a spring's tolerance: land exactly, so no card rests a fraction of a pixel off its slot.
        foreach (var entry in entries.Values)
        {
            entry.Settle();
            ApplyLook(entry);
        }
        Finish();
    }

    // Everything has landed: the cards that left are removed.
    private void Finish()
    {
        foreach (var entry in leaving.ToList())
        {
            if (entry.Opacity.IsMoving && entry.Opacity.Value > 0.002) continue;
            leaving.Remove(entry);
            entries.Remove(entry.Card.JobId);
            Children.Remove(entry.Card);
        }
    }
}
