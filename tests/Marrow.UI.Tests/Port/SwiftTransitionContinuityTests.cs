using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0076 (no change by a cut), ED-NEW-perf-motion-2: plain SwiftTransition.Insert and Remove continue from where an interrupted
// transition has brought the child, as RemoveContinuing and InsertContinuing do (ED-278 (10)): a removal during a half-done fade-in
// fades out from the opacity the child has (no flash to full), an insertion during a half-done removal comes back from there (no
// vanish), and a removal before the insertion has moved still starts from the removal's own start (the stage swapped at once).
[Collection("Headless")]
public sealed class SwiftTransitionContinuityTests(HeadlessSession headless)
{
    private const double Tolerance = 1e-6;

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-2")]
    public void A_plain_removal_during_a_fade_in_fades_out_from_the_opacity_the_child_has() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (panel, window) = Stage();
        var child = new Border { Width = 100, Height = 40 };
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        clocks.Frame(0.05);
        var shown = child.Opacity;
        Assert.InRange(shown, 0.1, 0.9);
        var removal = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(removal, () => SwiftTransition.Remove(panel, child));
        Assert.Equal(shown, child.Opacity, Tolerance);
        var last = child.Opacity;
        for (var frame = 0; frame < 30 && panel.Children.Contains(child); frame++)
        {
            clocks.Frame(1 / 60.0);
            Assert.True(child.Opacity <= last + Tolerance, $"the opacity rose to {child.Opacity} from {last}");
            last = child.Opacity;
        }
        Assert.DoesNotContain(child, panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-2")]
    public void A_plain_insertion_during_a_removal_brings_the_child_back_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (panel, window) = Stage();
        var child = new Border { Width = 100, Height = 40 };
        SwiftTransition.Place(panel, 0, child);
        var curve = Animation.EaseOut(duration: 0.3);
        Transaction.WithAnimation(curve, () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.06);
        var shown = child.Opacity;
        Assert.InRange(shown, 0.1, 0.9);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        Assert.Equal(shown, child.Opacity, Tolerance);
        Assert.Single(panel.Children);
        var last = child.Opacity;
        for (var frame = 0; frame < 30; frame++)
        {
            clocks.Frame(1 / 60.0);
            Assert.True(child.Opacity >= last - Tolerance, $"the opacity fell to {child.Opacity} from {last}");
            last = child.Opacity;
        }
        Assert.Equal(1, child.Opacity, Tolerance);
        Assert.Contains(child, panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-2")]
    public void A_plain_removal_before_the_insertion_has_moved_starts_from_the_removals_own_start() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (panel, window) = Stage();
        var child = new Border { Width = 100, Height = 40 };
        var curve = Animation.EaseOut(duration: 0.2);
        Transaction.WithAnimation(curve, () => SwiftTransition.Insert(panel, 0, child));
        Transaction.WithAnimation(curve, () => SwiftTransition.Remove(panel, child));
        Assert.Equal(1, child.Opacity, Tolerance);
        window.Close();
    });

    private static (Canvas Panel, Window Window) Stage()
    {
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, window);
    }
}
