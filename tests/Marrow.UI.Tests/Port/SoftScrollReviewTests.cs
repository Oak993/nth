using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-246 (review of 975fcae2a, points 1-10): the soft scrolling on real ScrollViewers, frame by frame on the manual
// frame clock, with input events at their own times between frames (SoftScrollRig.Play). Every motion is checked over
// its whole sequence of frames, the last one included.
[Collection("Headless")]
public sealed class SoftScrollReviewTests(HeadlessSession headless)
{
    private static string Steps(IEnumerable<double> steps) =>
        string.Join(", ", steps.Select(s => s.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

    private static double[] StepsOf(List<(double T, double Y)> trace, double from = 0) =>
        trace.Zip(trace.Skip(1), (a, b) => (b.T, D: b.Y - a.Y)).Where(s => s.T > from).Select(s => s.D).ToArray();

    // From the largest step on, no step is larger than the one before it (the last one included), and nothing goes back.
    private static void EndsWithoutAJump(List<(double T, double Y)> trace, string what)
    {
        var steps = StepsOf(trace).SkipWhile(s => s == 0).ToArray();
        var moving = steps.Reverse().SkipWhile(s => s == 0).Reverse().ToArray();
        Assert.True(moving.Length > 2, what + ": it did not move");
        var sign = Math.Sign(moving.Sum());
        Assert.All(moving, s => Assert.True(s * sign >= -1e-9, what + " went backwards: " + Steps(moving)));
        var peak = Array.FindIndex(moving, s => Math.Abs(s) == moving.Max(Math.Abs));
        for (var i = peak + 1; i < moving.Length; i++)
            Assert.True(Math.Abs(moving[i]) <= Math.Abs(moving[i - 1]) + 1e-9,
                FormattableString.Invariant($"{what}: step {i} of {moving.Length} jumped {moving[i]:F4} after {moving[i - 1]:F4}; last steps {Steps(moving.TakeLast(6))}"));
    }

    // ---- 1. A high-resolution (fractional) mouse wheel is a wheel: the two-stage glide, no touchpad coast ----

    // The two stages' response to reports of `points` at `times` (ED-NEW-softscroll-2), aimed RestSpeed's worth past their sum.
    private static double Glide(IReadOnlyList<double> times, double points, double t)
    {
        static double Step(double u) => u <= 0 ? 0 : 1 - (1 + u / ScrollPhysics.GlideStage) * Math.Exp(-u / ScrollPhysics.GlideStage);
        var sum = times.Count * points;
        var y = ScrollPhysics.RestSpeed * ScrollPhysics.GlideStage * Step(t - times[0]) + times.Sum(at => points * Step(t - at));
        return Math.Min(y, sum);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-246")]
    [Trait("Decision", "ED-NEW-softscroll-2")]
    public void A_high_resolution_mouse_wheel_glides_along_the_two_stages_and_never_coasts(int hz) => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        // A Logitech-style high-resolution wheel: 15 units (an eighth of WHEEL_DELTA) per report, at the wheel's own
        // uneven pace. 8 reports = one notch's worth, 100 points at the default 3 lines.
        var times = new[] { 0, 0.011, 0.019, 0.031, 0.040, 0.054, 0.063, 0.079 };
        var trace = rig.Play(times.Select(t => (t, new Vector(0, -0.125))), hz, 1.2);
        Assert.Equal(100, trace[^1].Y, 6); // exactly the wheel's distance: no coast added on top
        var restAt = trace.First(f => Math.Abs(f.Y - 100) < 1e-9).T;
        Assert.True(restAt < 0.079 + 0.45, FormattableString.Invariant($"still moving {restAt - 0.079:F2} s after the last report"));
        // Every frame is the reports' own responses added up (one target), the tail included: no coast of a touchpad's.
        foreach (var (t, y) in trace) Assert.InRange(y, Glide(times, 12.5, t) - 1e-3, Glide(times, 12.5, t) + 1e-3);
        // From the last report's interval on (the wheel's own uneven pace before it is the input's), it only slows.
        EndsWithoutAJump(trace.Where(f => f.T >= 0.079 + 0.016).ToList(), "fine wheel");
    });

    // ---- 2. The touchpad: no coast of the port's own; a hold stays held; Windows' own inertia is not stacked ----

    private static IEnumerable<(double T, Vector Delta)> Touchpad(IEnumerable<int> units, double cadence = 0.008, double from = 0) =>
        units.Select((u, i) => (from + i * cadence, new Vector(0, -u / 120.0)));

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_touchpad_stroke_that_moves_then_holds_stops_where_the_fingers_stopped() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        // Two fingers moving at about 600 points a second for 100 ms, then resting on the pad: no more reports.
        var units = new[] { 7, 11, 13, 13, 14, 13, 14, 13, 13, 14, 13, 14, 13 };
        var travel = units.Sum() * ScrollPhysics.WheelLine / 120;
        var trace = rig.Play(Touchpad(units), 240, 1.5);
        var lastEvent = (units.Length - 1) * 0.008;
        Assert.All(trace, f => Assert.True(f.Y <= travel + 1e-6, FormattableString.Invariant($"ran on past the fingers at {f.T:F3}: {f.Y:F2} > {travel:F2}")));
        Assert.Equal(travel, trace[^1].Y, 6);
        var restAt = trace.First(f => Math.Abs(f.Y - travel) < 1e-9).T;
        Assert.True(restAt - lastEvent < 0.08, FormattableString.Invariant($"still moving {restAt - lastEvent:F3} s after the fingers stopped"));
        EndsWithoutAJump(trace.Where(f => f.T > lastEvent).ToList(), "hold");
    });

    [Fact]
    [Trait("Decision", "ED-246")]
    public void Windows_inertia_after_a_lift_is_followed_and_nothing_is_stacked_on_it() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        // A flick at about 1300 points a second, then a lift: Windows continues the stream with its own decaying deltas
        // (UNVERIFIED shape, see ED-246; probe in E:/Droppy/work/review-scroll-physics/probe).
        var stroke = Enumerable.Repeat(25, 8).ToList();
        var tail = new List<int>();
        for (var u = 25.0 * 0.93; u >= 1; u *= 0.93) tail.Add((int)Math.Round(u));
        var units = stroke.Concat(tail).ToArray();
        var travel = units.Sum() * ScrollPhysics.WheelLine / 120;
        var trace = rig.Play(Touchpad(units), 240, 2.5);
        Assert.Equal(travel, trace[^1].Y, 6);
        Assert.All(trace, f => Assert.True(f.Y <= travel + 1e-6));
        var lastEvent = (units.Length - 1) * 0.008;
        var restAt = trace.First(f => Math.Abs(f.Y - travel) < 1e-9).T;
        Assert.True(restAt - lastEvent < 0.08, FormattableString.Invariant($"the port coasted {restAt - lastEvent:F2} s on top of Windows' tail"));
    });

    // ---- 3. A touchpad stroke starts and runs smoothly, frame by frame, at 60 and 240 Hz ----

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-246")]
    public void A_steady_touchpad_stroke_starts_in_the_first_frame_and_runs_without_a_stutter(int hz) => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        // Perfectly regular input: 4 points every 8 ms (500 points a second; 9.6 units, a fraction of WHEEL_DELTA).
        var trace = rig.Play(Enumerable.Range(0, 40).Select(i => (i * 0.008, new Vector(0, -0.08))), hz, 0.15);
        var steps = StepsOf(trace);
        var steady = 500.0 / hz;
        Assert.True(steps[0] > 0, "the first frame did not move");
        // The first 150 ms, step by step: rising, never falling, until within 0.1 % of steady (40 ms, or 3 frames at 60 Hz); then within 0.1 %.
        var reached = Array.FindIndex(steps, s => s >= steady * 0.999);
        Assert.True(reached >= 0 && reached <= Math.Max(0.04 * hz, 3), FormattableString.Invariant($"{hz} Hz: not steady after 40 ms (or 3 frames): {Steps(steps)}"));
        for (var i = 1; i <= reached; i++)
            Assert.True(steps[i] >= steps[i - 1] - 1e-9, FormattableString.Invariant($"{hz} Hz: step {i} fell during the start: {Steps(steps)}"));
        foreach (var s in steps.Skip(reached))
            Assert.InRange(s, steady * 0.999, steady * 1.001);
    });

    // ---- 4. Every motion ends without a settle jump: the last step is never larger than the one before ----

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-246")]
    public void A_notch_ends_without_a_settle_jump(int hz) => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        var trace = rig.Play([(0.0, new Vector(0, -1))], hz, 1.5);
        Assert.Equal(100, trace[^1].Y);
        EndsWithoutAJump(trace, hz + " Hz notch");
    });

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_page_key_and_a_programmatic_scroll_end_without_a_settle_jump() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        Assert.True(rig.Button.Focus());
        rig.Press(Key.PageDown);
        var trace = rig.Play([], 240, 1.0);
        Assert.Equal(rig.Scroll.Viewport.Height, trace[^1].Y);
        EndsWithoutAJump(trace, "page down");
        SoftScroll.ScrollTo(rig.Scroll, new Vector(0, 1500), Animation.Smooth(0.35));
        trace = rig.Play([], 240, 1.0);
        Assert.Equal(1500, trace[^1].Y);
        EndsWithoutAJump(trace, "smooth(0.35)");
    });

    // ---- 10. A carried speed dies inside the animation; a touch coast runs out to its very end ----

    [Fact]
    [Trait("Decision", "ED-246")]
    public void An_animation_that_carries_a_fast_scroll_in_ends_exactly_without_a_snap() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig(content: new Border { Height = 40000, Width = 100 });
        var presenter = rig.Scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        // A fast touch flick (about 20 000 points a second) still moving when a .smooth(0.25) scroll starts.
        for (var i = 0; i < 6; i++)
        {
            presenter.RaiseEvent(new ScrollGestureEventArgs(9, new Vector(0, 160)));
            rig.Frame(125);
        }
        presenter.RaiseEvent(new ScrollGestureEndedEventArgs(9));
        rig.Frame();
        var at = rig.Scroll.Offset.Y;
        SoftScroll.ScrollTo(rig.Scroll, new Vector(0, at + 600), Animation.Smooth(0.25));
        var trace = rig.Play([], 240, 1.0);
        Assert.Equal(at + 600, trace[^1].Y, 9);
        var steps = StepsOf(trace).Reverse().SkipWhile(s => s == 0).Reverse().ToArray();
        // The last step is a sliver, never the leftover of a speed that did not die out.
        Assert.True(Math.Abs(steps[^1]) <= Math.Abs(steps[^2]) + 1e-9 && Math.Abs(steps[^1]) < 0.05, "it snapped at the end: " + Steps(steps.TakeLast(6)));
    });

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_touch_coast_runs_out_to_its_end_instead_of_stopping_short() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig(content: new Border { Height = 40000, Width = 100 });
        var presenter = rig.Scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
        for (var i = 0; i < 6; i++)
        {
            presenter.RaiseEvent(new ScrollGestureEventArgs(7, new Vector(0, 10)));
            rig.Frame(125); // 1250 points a second
        }
        presenter.RaiseEvent(new ScrollGestureEndedEventArgs(7));
        var trace = rig.Play([], 240, 6);
        EndsWithoutAJump(trace, "coast");
        var steps = StepsOf(trace).Reverse().SkipWhile(s => s == 0).ToArray();
        // It stops only once it has slowed to a crawl (2 points a second: a hundredth of a pixel a frame), not at 10.
        Assert.True(steps[0] * 240 <= 2.5, FormattableString.Invariant($"stopped from {steps[0] * 240:F1} points a second"));
    });

    // ---- 5. No start lag: a wheel flick moves in its first frame, from rest; the ends are reached from speed ----

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [Trait("Decision", "ED-246")]
    [Trait("Decision", "ED-NEW-softscroll-2")]
    public void A_wheel_flick_moves_in_the_first_frame_from_rest_and_peaks_a_stage_after_its_notches(int hz) => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        // Five notches in 50 ms (a flick); the frames run on their own grid.
        var trace = rig.Play(Enumerable.Range(0, 5).Select(i => (i * 0.0125, new Vector(0, -1))), hz, 1.0);
        // It moves in the first frame, starting from rest: 4.8 points at 60 Hz, a third of a point at 240 Hz.
        Assert.InRange(trace[0].Y, hz == 240 ? 0.32 : 4.7, hz == 240 ? 0.34 : 4.9);
        var speeds = trace.Zip(trace.Skip(1), (a, b) => (T: b.T, V: (b.Y - a.Y) * hz)).ToArray();
        var peak = speeds.MaxBy(s => s.V);
        // Peak about a stage after the burst's middle (81 ms), within a frame.
        Assert.InRange(peak.T, 0.080, 0.0825 + 1.0 / hz + 1e-9);
        // Weighted: distance over peak speed 0.144 s (0.146 s from 60 Hz frames).
        Assert.InRange(500 / peak.V, 0.143, 0.147);
        Assert.Equal(500, trace[^1].Y);
    });

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_glide_into_the_end_arrives_from_speed_and_brakes_briefly() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig(content: new Border { Height = 300 + 276, Width = 100 }); // the theme list: 276 points of overflow
        var trace = rig.Play(Enumerable.Range(0, 4).Select(i => (i * 0.0125, new Vector(0, -1))), 240, 1.0); // 400 points asked (100 a notch)
        Assert.All(trace, f => Assert.InRange(f.Y, 0, 276));
        Assert.Equal(276, trace[^1].Y);
        var speeds = trace.Zip(trace.Skip(1), (a, b) => (T: b.T, Y: b.Y, V: (b.Y - a.Y) * 240)).ToArray();
        var peak = speeds.Max(s => s.V);
        // 10 points before the end it is still going at a good part of its peak (the Mac: 0.19-0.88 of it) ...
        var near = speeds.First(s => s.Y >= 266);
        Assert.True(near.V >= 0.2 * peak, FormattableString.Invariant($"eased in: {near.V:F0} of {peak:F0} points a second, 10 points out"));
        // ... and it is at rest within a tenth of a second of that, not after a 0.4 s ease.
        var restAt = trace.First(f => f.Y == 276).T;
        Assert.True(restAt - near.T <= 0.1, FormattableString.Invariant($"took {restAt - near.T:F3} s over the last 10 points"));
        EndsWithoutAJump(trace, "end");
    });

    // ---- 8. The focused control handles its keys first ----

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_focused_slider_keeps_home_end_and_the_page_keys() => headless.OnUiThread(() =>
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Width = 140, Height = 24, LargeChange = 0.25 };
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { slider, new Border { Height = 2000 } } });
        Assert.True(slider.Focus());
        rig.Press(Key.End);
        Assert.Equal(1, slider.Value);
        rig.Press(Key.Home);
        Assert.Equal(0, slider.Value);
        rig.Press(Key.PageUp);
        Assert.Equal(0.25, slider.Value, 9);
        rig.Press(Key.PageDown);
        Assert.Equal(0, slider.Value, 9);
        rig.Run(0.6);
        Assert.Equal(0, rig.Scroll.Offset.Y); // the page never moved
    });

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_focused_list_item_keeps_the_keys_for_its_list() => headless.OnUiThread(() =>
    {
        var list = new ListBox
        {
            Height = 120,
            ItemsSource = Enumerable.Range(0, 40).Select(i => "item " + i).ToArray(),
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, Height = 24 }),
            ItemContainerTheme = new Avalonia.Styling.ControlTheme(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Avalonia.Styling.Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ListBoxItem>((item, scope) => new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.ContentProperty] = item[!ContentControl.ContentProperty],
                        [!ContentPresenter.ContentTemplateProperty] = item[!ContentControl.ContentTemplateProperty],
                    }.RegisterInNameScope(scope))),
                },
            },
            Template = new FuncControlTemplate<ListBox>((owner, scope) =>
            {
                var own = new ScrollViewer { Name = "PART_ScrollViewer", Content = new ItemsPresenter { Name = "PART_ItemsPresenter" }.RegisterInNameScope(scope) };
                SoftScroll.SetIsEnabled(own, true);
                return own.RegisterInNameScope(scope);
            }),
        };
        // An item container that pages on its own (a custom list row).
        var row = new PagingRow { Height = 30, Focusable = true };
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { row, list, new Border { Height = 2000 } } });
        rig.Window.UpdateLayout();
        list.SelectedIndex = 0;
        rig.Window.UpdateLayout();
        var item = list.ContainerFromIndex(0)!;
        Assert.True(item.Focus());
        rig.Press(Key.End);
        rig.Run(0.6);
        Assert.Equal(39, list.SelectedIndex); // the list's own selection moved
        rig.Press(Key.Home);
        rig.Run(0.6);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, rig.Scroll.Offset.Y); // the page around the list stayed
        Assert.True(row.Focus());
        rig.Press(Key.PageDown);
        rig.Run(0.6);
        Assert.Equal(1, row.Pages);
        Assert.Equal(0, rig.Scroll.Offset.Y);
    });

    // ---- 9. Wheel chaining: a nested scroll view at its end hands the wheel on, as Avalonia's presenter does ----

    [Fact]
    [Trait("Decision", "ED-246")]
    public void A_nested_scroll_view_at_its_end_passes_the_wheel_to_the_page() => headless.OnUiThread(() =>
    {
        var inner = new ScrollViewer { Height = 200, VerticalAlignment = VerticalAlignment.Top, Content = new Border { Height = 500, Width = 100 } };
        SoftScroll.SetIsEnabled(inner, true);
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { inner, new Border { Height = 2000 } } });
        var over = new Point(50, 50); // over the inner view wherever the page has scrolled to (by 100 at most)
        rig.Play([(0.0, new Vector(0, -1))], 240, 0.6, over);
        Assert.Equal(100, inner.Offset.Y); // not at its end: the inner view takes it
        Assert.Equal(0, rig.Scroll.Offset.Y);
        rig.Play([(0.0, new Vector(0, -4))], 240, 0.6, over);
        Assert.Equal(300, inner.Offset.Y); // at its end now
        rig.Play([(0.0, new Vector(0, -1))], 240, 0.6, over);
        Assert.Equal(300, inner.Offset.Y);
        Assert.Equal(100, rig.Scroll.Offset.Y); // the page took it
        rig.Play([(0.0, new Vector(0, 1))], 240, 0.6, over);
        Assert.Equal(200, inner.Offset.Y); // back up: the inner view can move that way again
        Assert.Equal(100, rig.Scroll.Offset.Y);
        // With chaining off the inner view keeps the wheel even at its end (ScrollViewer.IsScrollChainingEnabled).
        ScrollViewer.SetIsScrollChainingEnabled(inner, false);
        rig.Play([(0.0, new Vector(0, -4))], 240, 0.6, over);
        Assert.Equal(300, inner.Offset.Y);
        Assert.Equal(100, rig.Scroll.Offset.Y);
    });

}
