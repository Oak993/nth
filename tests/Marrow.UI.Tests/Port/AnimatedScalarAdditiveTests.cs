using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// ED-NEW-sidebarmodes-8: SwiftUI's springs are additive. A new target set while a run is in flight adds a run on top of the one moving, so the
// value keeps its speed and turns round smoothly; the sidebar column uses it, so a second press mid-slide never stops the column dead.
[Collection("Headless")]
public sealed class AnimatedScalarAdditiveTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 200, Height = 100, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-WIN-046")] // UI/Chrome/WindowChrome.swift:362 withAnimation(Chrome.panelSlide) { isVisible.toggle() }, pressed again mid-slide
    public void A_reversal_mid_run_keeps_the_value_and_its_speed_then_lands_on_the_new_target() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border(); var window = Show(owner);
        var spring = Animation.Spring(response: 0.32, dampingFraction: 0.9);
        var additive = new AnimatedScalar(owner, 0, _ => { });
        var replacing = new AnimatedScalar(owner, 0, _ => { });
        additive.Set(268, spring, additive: true);
        replacing.Set(268, spring);
        var seen = new List<double> { 0 };
        for (var i = 0; i < 5; i++) { clocks.Frame(0.016); seen.Add(additive.Value); }
        Assert.Equal(seen[^1], replacing.Value, 6); // one run alone is the plain run
        var before = seen[^1] - seen[^2];
        Assert.True(before > 5, "it was moving out fast");

        var reached = additive.Value;
        additive.Set(0, spring, additive: true);
        replacing.Set(0, spring);
        Assert.Equal(reached, additive.Value); // no jump at the press
        Assert.True(additive.IsRunning);
        Assert.Equal(0, additive.Target);
        clocks.Frame(0.016);
        var after = additive.Value - reached;
        var afterReplacing = replacing.Value - reached;
        Assert.True(after > 0, "it carries on outwards for a moment, as a spring with speed does"); // momentum kept
        Assert.True(afterReplacing <= 0, "the replacing run starts again from rest"); // the dead stop the additive run avoids
        Assert.True(Math.Abs(after - before) < before, "no sudden change of speed at the turn");

        var path = new List<double> { additive.Value };
        for (var i = 0; i < 80; i++) { clocks.Frame(0.016); path.Add(additive.Value); }
        Assert.False(additive.IsRunning);
        Assert.Equal(0, additive.Value);
        Assert.All(path, value => Assert.InRange(value, -15, 268)); // never past the far end, a spring's small dip at most at the near one
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076: under reduce motion an additive target is taken at once too
    public void Under_reduce_motion_an_additive_target_is_taken_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        var owner = new Border(); var window = Show(owner);
        var scalar = new AnimatedScalar(owner, 0, _ => { });
        scalar.Set(268, Animation.EaseOut(0.18), additive: true);
        Assert.Equal(268, scalar.Value);
        Assert.False(scalar.IsRunning);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });
}
