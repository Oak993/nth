using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0076 (no change by a cut), ED-NEW-parity-chatview: a removal that interrupts an insertion still in flight starts from
// where the insertion has brought the child. For a transition with no asymmetric part the insertion's state at progress p
// is the removal's at 1 - p, so the child goes back from there on the removal's curve: no frame shows it at its identity.
// The site that needs it is the terminal drawer closed while it is still opening (App/Views/Chat/ChatView.swift:276-279,290,
// `.move(edge: .bottom).combined(with: .opacity)` under `.animation(_, value: isTerminalVisible)`).
// Geometry: a 100 x 40 child at the panel origin; Move(.bottom) offsets it by its own height times the distance from identity.
[Collection("Headless")]
public sealed class SwiftTransitionInterruptionTests(HeadlessSession headless)
{
    private const double Tolerance = 1e-6; // frames land on whole ticks of SwiftClock

    private static double Top(Control child, Visual panel) => child.TranslatePoint(new Point(0, 0), panel)!.Value.Y;

    private static double EffectiveOpacity(Visual child, Visual panel)
    {
        var opacity = child.Opacity;
        foreach (var ancestor in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(child))
        {
            if (ReferenceEquals(ancestor, panel)) return opacity;
            opacity *= ancestor.Opacity;
        }
        throw new InvalidOperationException("the child is not under the panel");
    }

    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    // Decision ED-NEW-parity-chatview (10): the trait is added when the lead numbers the entry (the placeholder is not a well-formed id).
    public void A_removal_during_the_insertion_goes_back_from_where_the_insertion_has_brought_the_child() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.05);
        var p = curve.Progress(0.05);
        Assert.Equal(40 * (1 - p), Top(child, panel), Tolerance);
        Assert.Equal(p, EffectiveOpacity(child, panel), Tolerance);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.RemoveContinuing(panel, child));
        // The removal's first state is the insertion's last: no jump to identity (top 0, opaque).
        Assert.Equal(40 * (1 - p), Top(child, panel), Tolerance);
        Assert.Equal(p, EffectiveOpacity(child, panel), Tolerance);
        var from = 1 - p;
        var elapsed = 0.0;
        foreach (var at in new[] { 0.04, 0.1, 0.2 })
        {
            clocks.Frame(at - elapsed);
            elapsed = at;
            var q = from + (1 - from) * removal.Progress(at);
            Assert.Equal(40 * q, Top(child, panel), Tolerance);
            Assert.Equal(1 - q, EffectiveOpacity(child, panel), Tolerance);
        }
        clocks.Frame(0.11); // 0.31 s: past the removal's 0.3 s
        Assert.Empty(panel.Children);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    // Decision ED-NEW-parity-chatview (10): the trait is added when the lead numbers the entry (the placeholder is not a well-formed id).
    public void The_insertions_progress_is_read_on_its_clock_at_the_interruption_not_at_its_last_frame() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.03);
        clocks.Time.Advance(TimeSpan.FromMilliseconds(40)); // 0.07 s on the clock; the last frame presented 0.03 s.
        Transaction.WithAnimation(curve, () => SwiftTransition.RemoveContinuing(panel, child));
        var p = curve.Progress(0.07);
        Assert.Equal(40 * (1 - p), Top(child, panel), Tolerance);
        Assert.Equal(p, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    // Decision ED-NEW-parity-chatview (10): the trait is added when the lead numbers the entry (the placeholder is not a well-formed id).
    public void A_removal_after_the_insertion_has_settled_starts_from_identity() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        Assert.Equal(0, clocks.Frames.Pending);
        Transaction.WithAnimation(curve, () => SwiftTransition.RemoveContinuing(panel, child));
        Assert.Equal(0, Top(child, panel), Tolerance);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frame(0.1);
        Assert.Equal(40 * curve.Progress(0.1), Top(child, panel), Tolerance);
        window.Close();
    });

    // An asymmetric transition's insertion progress names no removal state (the two halves differ), so RemoveContinuing leaves it as
    // Remove does, from the removal half's identity (documented on RemoveContinuing). Insertion: Move(.bottom); removal: Opacity.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void A_removal_during_an_asymmetric_insertion_leaves_from_identity_not_from_one_minus_the_insertions_progress() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Asymmetric(SwiftTransition.Move(Edge.Bottom), SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.05);
        var p = curve.Progress(0.05);
        Assert.InRange(p, 0.2, 0.8); // mid-flight, so that 1 - p and 0 differ
        Assert.Equal(40 * (1 - p), Top(child, panel), Tolerance);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.RemoveContinuing(panel, child));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance); // the removal half's identity: fully opaque
        clocks.Frame(0.1);
        Assert.Equal(1 - removal.Progress(0.1), EffectiveOpacity(child, panel), Tolerance); // from 0 on the removal's own curve
        clocks.Frame(0.21); // 0.31 s
        Assert.Empty(panel.Children);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    // The counterpart of RemoveContinuing (OD-0076: no change by a cut): an insertion that interrupts the child's removal still in flight
    // brings the same child back from where the removal has taken it (1 - the removal's progress for a symmetric transition). The child
    // stays in its panel (no second copy, no rebuilt wrapper), the removal's frames and completion are cancelled, and no frame shows the
    // child at its removal's end or at the insertion's start.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void An_insertion_during_the_removal_brings_the_same_child_back_from_where_the_removal_has_taken_it() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.1);
        var r = removal.Progress(0.1);
        Assert.Equal(40 * r, Top(child, panel), Tolerance);
        var back = Animation.EaseOut(duration: 0.25);
        Transaction.WithAnimation(back, () => SwiftTransition.InsertContinuing(panel, 0, child));
        Assert.Same(child, Assert.Single(panel.Children)); // the same child, once
        // The insertion's first state is the removal's last: no jump to its end (top 40, clear) or to the insertion's start.
        Assert.Equal(40 * r, Top(child, panel), Tolerance);
        Assert.Equal(1 - r, EffectiveOpacity(child, panel), Tolerance);
        var from = 1 - r;
        var elapsed = 0.0;
        foreach (var at in new[] { 0.04, 0.1, 0.22 }) // 0.22 s is past where the cancelled removal would have ended (0.2 s after the interruption)
        {
            clocks.Frame(at - elapsed);
            elapsed = at;
            var s = from + (1 - from) * back.Progress(at);
            Assert.Same(child, Assert.Single(panel.Children));
            Assert.Equal(40 * (1 - s), Top(child, panel), Tolerance);
            Assert.Equal(s, EffectiveOpacity(child, panel), Tolerance);
        }
        clocks.Frame(0.04); // 0.26 s: past the insertion's 0.25 s
        Assert.Same(child, Assert.Single(panel.Children));
        Assert.Equal(0, Top(child, panel), Tolerance);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    // A removal that itself continued an insertion presents from 1 - p, not from 0: the instant's state an interruption reads is the
    // presented one (the start plus what is left of it times the curve), so the chain open, close while opening, open while closing
    // has no jump anywhere.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void A_chain_of_interruptions_reads_the_presented_state_of_a_continuing_removal_not_its_curve_alone() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.05);
        var p = curve.Progress(0.05);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.RemoveContinuing(panel, child));
        clocks.Frame(0.1);
        var from = 1 - p;
        var r = from + (1 - from) * removal.Progress(0.1); // the removal's presented progress now
        Assert.Equal(40 * r, Top(child, panel), Tolerance);
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, child));
        Assert.Equal(40 * r, Top(child, panel), Tolerance);
        Assert.Equal(1 - r, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frame(0.04);
        var s = (1 - r) + r * curve.Progress(0.04);
        Assert.Equal(40 * (1 - s), Top(child, panel), Tolerance);
        window.Close();
    });

    // The same instant's state is also what a close that interrupts a CONTINUING insertion reads: the insertion's presented progress.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void A_removal_during_a_continuing_insertion_goes_back_from_the_presented_state_of_that_insertion() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.1);
        var r = removal.Progress(0.1);
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, child));
        clocks.Frame(0.05);
        var s = (1 - r) + r * curve.Progress(0.05); // the continuing insertion's presented progress now
        Assert.Equal(40 * (1 - s), Top(child, panel), Tolerance);
        Transaction.WithAnimation(removal, () => SwiftTransition.RemoveContinuing(panel, child));
        Assert.Equal(40 * (1 - s), Top(child, panel), Tolerance); // no jump: the removal goes on from the insertion's presented state
        Assert.Equal(s, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    // An asymmetric transition's removal state names no insertion state, so InsertContinuing inserts as Insert does: from the insertion's
    // start, the lingering removal given up (one child in the panel).
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void An_insertion_during_an_asymmetric_removal_starts_from_the_insertions_own_start() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Asymmetric(SwiftTransition.Move(Edge.Bottom), SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.1);
        Assert.InRange(EffectiveOpacity(child, panel), 0.05, 0.95); // mid-removal
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, child));
        Assert.Same(child, Assert.Single(panel.Children));
        Assert.Equal(40, Top(child, panel), Tolerance); // the insertion's start: a full height below
        clocks.Frame(0.1);
        Assert.Equal(40 * (1 - curve.Progress(0.1)), Top(child, panel), Tolerance);
        window.Close();
    });

    // Nothing to continue: no removal in flight (the child never joined the panel, or its removal has finished) is a plain Insert.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void An_insertion_with_no_removal_in_flight_starts_from_its_own_start() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var curve = Animation.EaseOut(duration: 0.2);
        var fresh = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, fresh));
        window.UpdateLayout();
        Assert.Equal(40, Top(fresh, panel), Tolerance);
        Assert.Equal(0, EffectiveOpacity(fresh, panel), Tolerance);
        clocks.Frame(0.25);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, fresh));
        clocks.Frame(0.35); // the removal has finished and the child is out of the panel
        Assert.Empty(panel.Children);
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, fresh));
        Assert.Same(fresh, Assert.Single(panel.Children));
        Assert.Equal(40, Top(fresh, panel), Tolerance);
        Assert.Equal(0, EffectiveOpacity(fresh, panel), Tolerance);
        window.Close();
    });

    // The removal's completion (the disposal a list row runs once it has left) belongs to a removal that finishes: an insertion that
    // takes the child back cancels it, so the child that is back on screen is not disposed.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void An_insertion_that_takes_the_child_back_drops_the_removals_completion() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        var completed = 0;
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.3), () => SwiftTransition.Remove(panel, child, () => completed++));
        clocks.Frame(0.1);
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, child));
        clocks.Frame(0.5); // past both runs
        Assert.Equal(0, completed);
        Assert.Same(child, Assert.Single(panel.Children));
        // A later removal of the same child completes once, as ever.
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.3), () => SwiftTransition.Remove(panel, child, () => completed++));
        clocks.Frame(0.4);
        Assert.Equal(1, completed);
        Assert.Empty(panel.Children);
        window.Close();
    });

    // A cancelled removal leaves its completion nowhere to fire later: the reconciliation's forced finish (FinishRemoval, ViewLifetime) of
    // a child that was taken back runs nothing, so a row that is back on screen is not disposed by a stale callback.
    [Fact]
    [Trait("Rule", "R-VIEW-4")] // OD-0076: no change by a cut
    [Trait("Decision", "ED-159")] // the interruption follow-up
    public void A_child_taken_back_by_an_insertion_has_no_completion_left_for_a_forced_finish() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Move(Edge.Bottom).Combined(SwiftTransition.Opacity));
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.25);
        var completed = 0;
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.3), () => SwiftTransition.Remove(panel, child, () => completed++));
        clocks.Frame(0.1);
        Transaction.WithAnimation(curve, () => SwiftTransition.InsertContinuing(panel, 0, child));
        SwiftTransition.FinishRemoval(panel, child);
        Assert.Equal(0, completed);
        window.Close();
    });
}
