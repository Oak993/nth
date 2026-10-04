using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

using Chrome = Marrow.UI.Chrome.Chrome;

// REVIEW (rec-tlr, second round): SwiftTransition.Remove now carries what an interrupted asymmetric insertion had put on the child. These probe the removal paths the round's own tests do not
// reach: an insertion that scales and moves (the jump-to-latest overlay, the panels), the settle glide cut at its first instant, and an insertion that comes back after a carried removal.
[Collection("Headless")]
public sealed class SwiftTransitionReviewTlrRemovalTests(HeadlessSession headless)
{
    private const double Tolerance = 1e-6;

    private static double Opacity(Visual child, Visual panel)
    {
        var opacity = 1.0;
        for (Visual? at = child; at is not null && !ReferenceEquals(at, panel); at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at)) opacity *= at.Opacity;
        return opacity;
    }

    private static Matrix Drawn(Visual child, Visual panel)
    {
        var matrix = Matrix.Identity;
        for (Visual? at = child; at is not null && !ReferenceEquals(at, panel); at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
            if (at.RenderTransform is { } transform) matrix *= transform.Value;
        return matrix;
    }

    private static (Window Window, Canvas Panel) Show()
    {
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, panel);
    }

    // ChatView.swift:1096-1101 (the jump-to-latest overlay): in Scale(0.6) + opacity + offset(10), out Scale(0.85) + opacity + offset(6). Cut in the middle of its arrival, its removal's first state
    // is the arrival's last in opacity, scale and rise, and by its end the child is gone.
    [Fact]
    [Trait("Req", "UI-CHAT-052")]
    public void REVIEW_a_scaling_and_moving_arrival_cut_short_leaves_from_where_it_had_got_to() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Show();
        var child = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Asymmetric(
            SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity).Combined(SwiftTransition.Offset(y: 10)),
            SwiftTransition.Scale(0.85).Combined(SwiftTransition.Opacity).Combined(SwiftTransition.Offset(y: 6))));
        var arrival = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(arrival, () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.06);
        var (opacity, matrix) = (Opacity(child, panel), Drawn(child, panel));
        Assert.InRange(opacity, 0.1, 0.9);
        var removal = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
        Assert.Equal(opacity, Opacity(child, panel), Tolerance);
        var start = Drawn(child, panel);
        Assert.True(Math.Abs(start.M11 - matrix.M11) < 1e-6 && Math.Abs(start.M22 - matrix.M22) < 1e-6 && Math.Abs(start.M32 - matrix.M32) < 1e-6,
            $"the removal's first state is scale {start.M11:F4}, rise {start.M32:F3}; the arrival's last was scale {matrix.M11:F4}, rise {matrix.M32:F3}");
        var last = opacity;
        foreach (var at in new[] { 0.016, 0.032, 0.048, 0.064 })
        {
            clocks.Frame(0.016);
            var now = Opacity(child, panel);
            Assert.True(now <= last + 1e-9, $"opacity went up during the removal: {last:F3} then {now:F3} at {at:F3} s");
            last = now;
        }
        clocks.Frame(0.3);
        Assert.Empty(panel.Children);
        window.Close();
    });

    // The settle glide cut at its first instant (the pill is inserted and the head's report arrives in the same flush): the arrival is at progress 0, so it is seen at nothing; the
    // removal must not draw it at full strength for the glide's first half either.
    [Fact]
    [Trait("Req", "UI-CHAT-052")]
    public void REVIEW_a_settle_glide_pill_removed_before_its_arrival_ran_a_frame_is_never_drawn() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var (window, panel) = Show();
        var child = new LayoutProbe(new Size(20, 20)).Transition(SwiftTransition.SettleGlide);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        SwiftTransition.Remove(panel, child);
        var seen = new List<double> { Opacity(child, panel) };
        for (var i = 0; i < 12; i++) { clocks.Frame(0.016); if (panel.Children.Count != 0) seen.Add(Opacity(child, panel)); }
        Assert.True(seen.Max() <= 0.01, $"the pill was drawn at opacity up to {seen.Max():F3} though its arrival had not begun");
        window.Close();
    });

    // A carried removal cut by a new arrival (the pill's head finishes again): the arrival that follows must end at full strength with no leftover from the carried removal.
    [Fact]
    [Trait("Req", "UI-CHAT-052")]
    public void REVIEW_an_arrival_that_follows_a_carried_removal_ends_at_full_opacity_and_rest() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var (window, panel) = Show();
        var child = new LayoutProbe(new Size(20, 20)).Transition(SwiftTransition.SettleGlide);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        clocks.Frame(0.1);
        SwiftTransition.Remove(panel, child);
        clocks.Frame(0.1);
        SwiftTransition.Insert(panel, 0, child);
        clocks.Frame(0.6);
        Assert.Same(child, Assert.Single(panel.Children));
        Assert.Equal(1, Opacity(child, panel), Tolerance);
        var matrix = Drawn(child, panel);
        Assert.True(Math.Abs(matrix.M32) < 1e-6 && Math.Abs(matrix.M11 - 1) < 1e-6, $"the pill rests at scale {matrix.M11:F3}, {matrix.M32:F3} low");
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });
}
