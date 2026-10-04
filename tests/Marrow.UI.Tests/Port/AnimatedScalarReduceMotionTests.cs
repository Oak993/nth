using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// OD-0076: "the system's reduce-motion setting stills every transition". AnimatedScalar is the port's number a view draws with (an opacity, a
// scale); like AnimatedColor it takes its target at once under reduce motion, whether the window's environment says so
// (EnvironmentValues.AccessibilityReduceMotion) or the Chrome platform does (Chrome.ReduceMotionPlatform). Every test sets the platform's setting
// explicitly, so none depends on the PC's own Windows "Animation effects" switch.
[Collection("Headless")]
public sealed class AnimatedScalarReduceMotionTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 200, Height = 100, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076: with reduce motion off in the platform and the window, a scalar moves along its animation on the frame clock, so the stilled cases below are not just a scalar that never animates
    public void With_reduce_motion_off_a_scalar_moves_along_its_animation_frame_by_frame() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border(); var window = Show(owner);
        var scalar = new AnimatedScalar(owner, 0, _ => { });
        scalar.Set(1, Animation.EaseOut(0.2));
        Assert.Equal(0, scalar.Value); // nothing moves before a frame
        clocks.Frame(0.1); Assert.InRange(scalar.Value, 0.01, 0.99);
        clocks.Frame(0.2); Assert.Equal(1, scalar.Value);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076: the Chrome platform's reduce-motion setting (Chrome.ReduceMotionPlatform) makes a scalar take its target at once, before any frame
    public void Under_the_platforms_reduce_motion_a_scalar_takes_its_target_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        var owner = new Border(); var window = Show(owner); var applied = new List<double>();
        var scalar = new AnimatedScalar(owner, 0, applied.Add); applied.Clear();
        scalar.Set(1, Animation.EaseOut(0.2));
        Assert.Equal(1, scalar.Value); Assert.Equal([1.0], applied); // no frame has run, and none is wanted
        scalar.Set(0.25, Animation.Snappy(0.2)); Assert.Equal(0.25, scalar.Value);
        clocks.Frame(0.1); Assert.Equal(0.25, scalar.Value); Assert.Equal([1.0, 0.25], applied);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076: the window environment's reduce motion (EnvironmentValues.AccessibilityReduceMotion), with the platform's setting off, makes a scalar take its target at once
    public void Under_the_window_environments_reduce_motion_a_scalar_takes_its_target_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border(); var window = Show(owner);
        EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        var scalar = new AnimatedScalar(owner, 0, _ => { });
        scalar.Set(1, Animation.EaseOut(0.2));
        Assert.Equal(1, scalar.Value);
        EnvironmentValues.SetAccessibilityReduceMotion(window, false); // and when the window stops asking, it moves again
        scalar.Set(0, Animation.EaseOut(0.2)); Assert.Equal(1, scalar.Value);
        clocks.Frame(0.1); Assert.InRange(scalar.Value, 0.01, 0.99);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });
}
