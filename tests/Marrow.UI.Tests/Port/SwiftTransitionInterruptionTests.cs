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
    [Trait("Decision", "OD-0076")]
    [Trait("Decision", "ED-NEW-parity-chatview")]
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
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
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
    [Trait("Decision", "OD-0076")]
    [Trait("Decision", "ED-NEW-parity-chatview")]
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
        Transaction.WithAnimation(curve, () => SwiftTransition.Remove(panel, child));
        Assert.Equal(0, Top(child, panel), Tolerance);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frame(0.1);
        Assert.Equal(40 * curve.Progress(0.1), Top(child, panel), Tolerance);
        window.Close();
    });
}
