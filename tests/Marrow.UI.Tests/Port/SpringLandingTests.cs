using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// ED-NEW-perf-motion-10 (ED-333's UNRESOLVED run-out; the lead's ruling 2026-10-13: no motion may end with a step larger than its
// previous frame's step). A spring was cut at its settling time with up to 0.79 % of its travel left, and its last frame jumped it:
// 3 to 17 times the step before at 60 and 144 Hz. The remainder is now folded into the run's second half, so the curve lands on its
// end with no speed; the duration and the first half of the curve are unchanged. The zoom glide's stepped spring lands the same way.
[Collection("Headless")]
public sealed class SpringLandingTests(HeadlessSession headless)
{
    // A step under a millionth of the travel is the curve at rest (2.6 thousandths of a pixel across a 2560-pixel window).
    private const double AtRest = 1e-6;

    // Every spring the app animates with (the response and damping of each call site's Animation.Spring, Smooth and Snappy).
    public static TheoryData<double, double> AppSprings() => new()
    {
        { 0.15, 1 }, { 0.2, 1 }, { 0.22, 1 }, { 0.25, 1 }, { 0.28, 1 }, { 0.3, 1 }, { 0.35, 1 }, { 0.4, 1 }, { 0.45, 1 }, { 1.2, 1 }, { 0.21, 1 },
        { 0.18, 0.85 }, { 0.2, 0.85 }, { 0.22, 0.85 }, { 0.24, 0.85 }, { 0.3, 0.85 }, { 0.5, 0.85 },
        { 0.32, 0.8 }, { 0.28, 0.82 }, { 0.34, 0.86 }, { 0.3, 0.7 }, { 0.25, 0.8 }, { 0.5, 0.6 }, { 0.38, 0.82 }, { 0.38, 0.78 },
        { 0.36, 0.8 }, { 0.36, 0.78 }, { 0.32, 0.9 }, { 0.3, 0.45 }, { 0.22, 0.9 }, { 0.55, 0.75 }, { 0.45, 0.8 }, { 0.4, 0.88 },
        { 0.6, 0.86 }, { 2 * Math.PI / Math.Sqrt(280), 32 / (2 * Math.Sqrt(280)) },
    };

    [Theory]
    [MemberData(nameof(AppSprings))]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void A_spring_ends_with_no_step_larger_than_the_one_before_at_60_and_144_hz(double response, double damping)
    {
        var spring = Animation.Spring(response: response, dampingFraction: damping);
        foreach (var hertz in new[] { 60.0, 144.0 })
            for (var phase = 0; phase < 24; phase++)
            {
                // The frames a run sees: one every 1/hertz from a start part way into a frame; done 1 ms early (ED-117 (2)).
                var frame = 1 / hertz;
                var steps = new List<double>();
                var last = 0.0;
                for (var t = phase / 24.0 * frame; ; t += frame)
                {
                    var done = t + 0.001 >= spring.Duration;
                    var value = done ? 1 : spring.Progress(t);
                    steps.Add(value - last);
                    last = value;
                    if (done) break;
                }
                Assert.True(steps.Count >= 3, $"{steps.Count} frames");
                var end = Math.Abs(steps[^1]);
                var before = Math.Abs(steps[^2]);
                Assert.True(end <= before || end < AtRest,
                    $"spring({response:F3}, {damping:F3}) at {hertz} Hz, phase {phase}/24: the last step is {end:E2}, the one before {before:E2}");
            }
    }

    [Theory]
    [InlineData(0.35)]
    [InlineData(0.21)]
    [InlineData(0.25)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void A_critically_damped_spring_lands_on_its_end_with_no_speed_and_keeps_its_duration_and_its_first_half(double response)
    {
        var spring = Animation.Spring(response: response, dampingFraction: 1.0);
        var omega = 2 * Math.PI / response;
        Assert.Equal(Math.Log(1000) / omega, spring.Duration, 12); // the settling time is the approved one (ED-011)
        double Free(double t) => 1 - Math.Exp(-omega * t) * (1 + omega * t);
        // The first half is the free spring, exactly.
        for (var t = 0.0; t <= spring.Duration / 2; t += spring.Duration / 40) Assert.Equal(Free(t), spring.Progress(t), 12);
        // The end: exactly 1, with no speed; before, 0.79 % was left there and jumped.
        Assert.True(1 - Free(spring.Duration) > 0.0078, "the free spring is short at its settling time");
        Assert.Equal(1, spring.Progress(spring.Duration), 12);
        Assert.Equal(1, spring.Progress(spring.Duration * 3), 12);
        Assert.InRange(spring.Speed(spring.Duration - 1e-6), 0, 1e-3);
        // Monotone, never past 1, and its speed only falls over the second half (no push near the end).
        var previous = 0.0;
        var speed = double.MaxValue;
        for (var t = 0.0; t <= spring.Duration; t += spring.Duration / 400)
        {
            var value = spring.Progress(t);
            Assert.InRange(value, previous - 1e-12, 1 + 1e-12);
            if (t > spring.Duration / 2)
            {
                var now = spring.Speed(t);
                Assert.True(now <= speed + 1e-9, $"the speed rose to {now} from {speed} at {t:F4} s");
                speed = now;
            }
            previous = value;
        }
    }

    // An underdamped spring whose last crossing of its end falls in the second half (panelSlide, the glide spring, a 0.85 snappy one)
    // keeps the free curve up to that crossing and then passes 1 as the free spring does, only drawn in to land: its overshoot is kept
    // (UI-SIDEBAR-036: panelSlide's progress passes 1 before it settles), and it is 1 with no speed at the settling time.
    [Theory]
    [InlineData(0.32, 0.9)]
    [InlineData(0.6, 0.86)]
    [InlineData(0.3, 0.85)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void An_underdamped_spring_keeps_its_late_overshoot_and_lands_on_its_end_with_no_speed(double response, double damping)
    {
        var spring = Animation.Spring(response: response, dampingFraction: damping);
        var omega = 2 * Math.PI / response;
        var damped = omega * Math.Sqrt(1 - damping * damping);
        double Free(double t) => 1 - Math.Exp(-damping * omega * t) * (Math.Cos(damped * t) + damping * omega / damped * Math.Sin(damped * t));
        var end = spring.Duration;
        var first = (Math.PI - Math.Atan(damped / (damping * omega))) / damped;
        var crossing = first + Math.Floor((end - first) * damped / Math.PI) * Math.PI / damped;
        Assert.InRange(crossing, end / 2, end); // the premise: the free curve last crosses 1 in the second half
        Assert.Equal(1, Free(crossing), 9);
        for (var t = 0.0; t <= crossing; t += crossing / 40) Assert.Equal(Free(t), spring.Progress(t), 12);
        var peak = Enumerable.Range(1, 99).Select(i => spring.Progress(crossing + (end - crossing) * i / 100)).Max();
        var freePeak = Enumerable.Range(1, 99).Select(i => Free(crossing + (end - crossing) * i / 100)).Max();
        Assert.True(peak > 1 + (freePeak - 1) / 2, $"past its end by {peak - 1:E2}, the free spring by {freePeak - 1:E2}");
        Assert.All(Enumerable.Range(1, 99), i => Assert.True(spring.Progress(crossing + (end - crossing) * i / 100) > 1, "it stays past its end until it lands"));
        Assert.Equal(1, spring.Progress(end), 12);
        Assert.InRange(Math.Abs(spring.Speed(end - 1e-6)), 0, 1e-3);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void An_animated_scalar_on_smooth_lands_with_no_step_larger_than_the_one_before(int hertz) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border();
        var window = Show(owner);
        var scalar = new AnimatedScalar(owner, 0, _ => { });
        scalar.Set(4000, Animation.Smooth(0.35)); // the jump-to-latest glide over a long thread (ED-333)
        var seen = new List<double> { 0 };
        for (var i = 0; i < 200 && scalar.IsRunning; i++) { clocks.Frame(1.0 / hertz); seen.Add(scalar.Value); }
        Assert.False(scalar.IsRunning);
        Assert.Equal(4000, seen[^1]);
        var end = seen[^1] - seen[^2];
        var before = seen[^2] - seen[^3];
        Assert.True(end <= before, $"the last step is {end:F3} pt, the one before {before:F3} pt");
        Assert.True(end < 1, $"the last step is {end:F3} pt; it was about 40 pt before the run-out was folded in");
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Theory]
    [InlineData(60, 1.1)]
    [InlineData(144, 1.1)]
    [InlineData(60, 1.5)]
    [InlineData(144, 0.9)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void The_zoom_glide_lands_on_its_scale_with_no_step_larger_than_the_one_before(int hertz, double target) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border();
        var window = Show(owner);
        var drawn = new List<double> { 1 };
        var zoom = new ZoomMotion(owner, 1, scale => drawn.Add(scale));
        zoom.To(target);
        for (var i = 0; i < 400 && zoom.IsMoving; i++) clocks.Frame(1.0 / hertz);
        Assert.False(zoom.IsMoving);
        Assert.Equal(target, drawn[^1]);
        var steps = drawn.Zip(drawn.Skip(1), (a, b) => b - a).Where(step => step != 0).ToArray();
        Assert.True(Math.Abs(steps[^1]) <= Math.Abs(steps[^2]), $"the last step is {steps[^1]:E2}, the one before {steps[^2]:E2}");
        // It still rests when the approved spring does: a change of a tenth by 262 ms (ZoomMotion.Rate's note).
        if (target == 1.1) Assert.InRange(steps.Length / (double)hertz, 0.24, 0.28);
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void A_second_zoom_press_mid_glide_keeps_the_speed_and_still_lands_with_no_last_step(int hertz) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var owner = new Border();
        var window = Show(owner);
        var drawn = new List<double> { 1 };
        var zoom = new ZoomMotion(owner, 1, scale => drawn.Add(scale));
        zoom.To(1.1);
        for (var i = 0; i < hertz / 20; i++) clocks.Frame(1.0 / hertz);
        var speed = zoom.Velocity;
        Assert.True(speed > 0.3, $"it was moving at {speed}");
        zoom.To(1.2);
        Assert.True(zoom.Velocity > speed, "a press in a row adds its push to the speed under way");
        var pressedAt = drawn.Count;
        for (var i = 0; i < 400 && zoom.IsMoving; i++) clocks.Frame(1.0 / hertz);
        Assert.Equal(1.2, drawn[^1]);
        var steps = drawn.Zip(drawn.Skip(1), (a, b) => b - a).Where(step => step != 0).ToArray();
        Assert.True(Math.Abs(steps[^1]) <= Math.Abs(steps[^2]), $"the last step is {steps[^1]:E2}, the one before {steps[^2]:E2}");
        // Across the press the glide never stalls: the step after it is the step before plus the press's own push (ED-NEW-textsize-3).
        var across = drawn[pressedAt] - drawn[pressedAt - 1];
        var before = drawn[pressedAt - 1] - drawn[pressedAt - 2];
        Assert.True(across > before, $"the step after the press is {across:E2}, the one before {before:E2}");
        window.Close(); Dispatcher.UIThread.RunJobs();
    });

    private static Window Show(Control content)
    {
        var window = new Window { Width = 200, Height = 100, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
