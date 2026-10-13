using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// ED-NEW-perf-motion-11 (the lead's ruling 2026-10-13 on finding 5: where SwiftUI keeps a spring's velocity through a retarget, the port
// does too). A run that a new target interrupts used to restart from the value reached at rest: a moving thing sent elsewhere stopped
// dead and set off again (a kink). Now an additive target carries a plain run on, a popover's grow and shrink set out at the speed of
// the one they interrupt, and a moving spring transition turns round at the speed it had. A fade alone still comes back from rest.
[Collection("Headless")]
public sealed class RetargetSpeedTests(HeadlessSession headless)
{
    private const double Frame = 1 / 60.0;

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void An_additive_target_set_during_a_plain_run_carries_that_runs_speed_on() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border();
        var window = Show(owner);
        var spring = Animation.Spring(response: 0.32, dampingFraction: 0.9);
        var scalar = new AnimatedScalar(owner, 0, _ => { });
        scalar.Set(268, spring); // a plain run (the default)
        var seen = new List<double> { 0 };
        for (var i = 0; i < 5; i++) { clocks.Frame(Frame); seen.Add(scalar.Value); }
        var before = seen[^1] - seen[^2];
        Assert.True(before > 5, $"it was moving out at {before} a frame");
        var reached = scalar.Value;
        scalar.Set(0, spring, additive: true);
        Assert.Equal(reached, scalar.Value);
        clocks.Frame(Frame);
        var after = scalar.Value - reached;
        // Before: the plain run was dropped and the new one set out from rest, so the value turned back at once (after <= 0).
        Assert.True(after > 0, $"it carries on outwards for a moment, as a spring with speed does (step {after})");
        Assert.True(Math.Abs(after - before) < before, $"no sudden change of speed at the turn ({before} then {after})");
        for (var i = 0; i < 120 && scalar.IsRunning; i++) clocks.Frame(Frame);
        Assert.False(scalar.IsRunning);
        Assert.Equal(0, scalar.Value);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void A_popover_opened_again_while_it_shrinks_keeps_shrinking_for_a_moment_then_grows_and_lands_on_full_size() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var content = new Border { Width = 200, Height = 120 };
        var window = Show(content);
        PopoverAppear.Play(content, PlacementMode.Bottom);
        for (var i = 0; i < 30; i++) clocks.Frame(Frame);
        Assert.Null(content.RenderTransform); // grown in and at rest
        var closed = false;
        PopoverAppear.Dismiss(content, PlacementMode.Bottom, () => closed = true);
        clocks.Frame(Frame);
        clocks.Frame(Frame);
        var shrinking = Scale(content);
        var speed = PopoverAppear.ScaleSpeed(content);
        Assert.True(speed < -1, $"the shrink is under way at {speed} a second");
        PopoverAppear.Play(content, PlacementMode.Bottom);
        Assert.Equal(shrinking, Scale(content), 12); // no jump at the press
        clocks.Frame(Frame);
        // Before: the grow set out from rest at the scale reached and rose at once.
        Assert.True(Scale(content) < shrinking, $"it carries on shrinking for a moment ({shrinking} then {Scale(content)})");
        var path = new List<double> { shrinking, Scale(content) };
        for (var i = 0; i < 60 && content.RenderTransform is not null; i++) { clocks.Frame(Frame); if (content.RenderTransform is not null) path.Add(Scale(content)); }
        Assert.Null(content.RenderTransform);
        Assert.False(closed);
        path.Add(1);
        var steps = path.Zip(path.Skip(1), (a, b) => b - a).ToArray();
        Assert.True(Math.Abs(steps[^1]) <= Math.Abs(steps[^2]) + 1e-9, $"the last step is {steps[^1]:E2}, the one before {steps[^2]:E2}");
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void A_popover_closed_while_it_grows_sets_out_at_the_grows_speed() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var content = new Border { Width = 200, Height = 120 };
        var window = Show(content);
        PopoverAppear.Play(content, PlacementMode.Bottom);
        clocks.Frame(Frame);
        clocks.Frame(Frame);
        clocks.Frame(Frame);
        var from = Scale(content);
        var speed = PopoverAppear.ScaleSpeed(content);
        Assert.True(speed > 1, $"the grow is under way at {speed} a second");
        PopoverAppear.Dismiss(content, PlacementMode.Bottom, () => { });
        clocks.Frame(Frame);
        // The shrink's own curve from the scale reached, plus the grow's speed dying out by the close (t (1 - t/T)^2).
        var left = 1 - Frame / PopoverAppear.CloseSeconds;
        var expected = PopoverAppear.StartScale + (from - PopoverAppear.StartScale) * Math.Exp(-Frame / PopoverAppear.CloseDecay) + speed * Frame * left * left;
        Assert.Equal(expected, Scale(content), 4);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void A_sliding_spring_transition_removed_mid_insertion_turns_round_at_its_speed_while_a_fade_falls_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new StackPanel();
        var window = Show(panel);
        var moving = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Scale(0.5));
        var fading = new Border { Width = 100, Height = 40 }.Transition(SwiftTransition.Opacity);
        var spring = Animation.Smooth(0.3);
        Transaction.WithAnimation(spring, () => { SwiftTransition.Insert(panel, 0, moving); SwiftTransition.Insert(panel, 1, fading); });
        clocks.Frame(Frame);
        clocks.Frame(Frame);
        var scale = ScaleOf(moving);
        var opacity = fading.Opacity;
        Assert.InRange(scale, 0.55, 0.95);
        Assert.InRange(opacity, 0.1, 0.9);
        Transaction.WithAnimation(spring, () => { SwiftTransition.Remove(panel, moving); SwiftTransition.Remove(panel, fading); });
        Assert.Equal(scale, ScaleOf(moving), 9); // no jump at the press
        Assert.Equal(opacity, fading.Opacity, 9);
        clocks.Frame(Frame);
        // Before: the removal set out from rest, so the scale fell back at once.
        Assert.True(ScaleOf(moving) > scale, $"the scale carries on growing for a moment ({scale} then {ScaleOf(moving)})");
        Assert.True(fading.Opacity < opacity, $"a fade alone turns at once ({opacity} then {fading.Opacity})");
        for (var i = 0; i < 60 && panel.Children.Count > 0; i++) clocks.Frame(Frame);
        Assert.Empty(panel.Children);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    private static double Scale(Control content) => Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX;

    private static double ScaleOf(Control child) => child.RenderTransform switch
    {
        ScaleTransform scale => scale.ScaleX,
        null => 1,
        var other => other.Value.M11,
    };

    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
