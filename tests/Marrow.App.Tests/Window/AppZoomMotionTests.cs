using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

// ED-NEW-textsize-3 (owner, 2026-10-11: "no static movement anywhere; zoom in and out must also be smooth"): Ctrl+Plus,
// Ctrl+Minus and Ctrl+0 (and the Settings zoom buttons: every change of the zoom setting) move the app to the new scale
// along a critically damped spring started as an ease-out, laid out at each frame's scale, the reading position kept, the
// last frame the new scale exactly; a press while it moves carries on from where the scale is. Every check runs on a fake
// frame clock and a fake SwiftClock, moved together.
[Collection("Headless")]
public sealed class AppZoomMotionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private readonly HeadlessSession session = headless;

    private const double At100 = AppZoom.BaseScale; // 1.3
    private const double At110 = 1.1 * AppZoom.BaseScale; // 1.43
    private const double At125 = 1.25 * AppZoom.BaseScale; // 1.625

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void A_zoom_change_eases_out_onto_the_new_scale_and_rests_on_it_exactly() => session.OnUiThread(() =>
    {
        using var rig = new ZoomMotionRig();
        rig.Motion.To(At110);
        Assert.Equal(At100, rig.Motion.Value); // nothing is drawn before the next frame
        var drawn = rig.RunOut();
        // An ease-out: it moves in the first frame, every frame moves on, each step is smaller than the one before, never past.
        Assert.True(drawn[0] > At100);
        for (var index = 1; index < drawn.Count; index++)
        {
            Assert.True(drawn[index] >= drawn[index - 1], $"frame {index} went back: {drawn[index - 1]} then {drawn[index]}");
            if (index < drawn.Count - 1) // the last frame lands on the scale: checked below
                Assert.True(drawn[index] - drawn[index - 1] <= drawn[index - 1] - (index > 1 ? drawn[index - 2] : At100) + 1e-12, $"frame {index} sped up");
        }
        Assert.All(drawn, value => Assert.True(value <= At110, $"{value} overshot {At110}"));
        // 99 % of the way by 0.2 s; at rest by the 16th frame (0.267 s), on the new scale exactly, with nothing left waiting for a frame.
        Assert.True((drawn[11] - At100) / (At110 - At100) >= 0.99, $"at 0.2 s only {(drawn[11] - At100) / (At110 - At100):P1} of the way");
        Assert.InRange(drawn.Count, 15, 16);
        Assert.Equal(At110, drawn[^1]);
        Assert.Equal(At110, rig.Motion.Value);
        Assert.False(rig.Motion.IsMoving);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Equal(1, rig.Rests);
        Assert.Equal(drawn, rig.Applied); // each frame drew its scale once
        // The last frame's step is under half a pixel at the far edge of a window 2560 pixels wide: no settle jump.
        Assert.True((drawn[^1] - drawn[^2]) / At110 * 2560 < 0.5, $"the last step is {(drawn[^1] - drawn[^2]) / At110 * 2560:0.00} px");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void The_motion_is_the_same_curve_at_60_and_144_Hz() => session.OnUiThread(() =>
    {
        var slow = new ZoomMotionRig();
        slow.Motion.To(At125);
        for (var frame = 0; frame < 5; frame++) slow.Frame(60); // 1/12 s
        var atSixty = slow.Motion.Value;
        slow.Dispose();
        using var fast = new ZoomMotionRig();
        fast.Motion.To(At125);
        for (var frame = 0; frame < 12; frame++) fast.Frame(144); // 1/12 s
        Assert.Equal(atSixty, fast.Motion.Value, 6); // the frames' spans differ by the 100 ns rounding of 1/60 and 1/144 s
        var drawn = fast.RunOut(144);
        Assert.Equal(At125, drawn[^1]);
        Assert.InRange((12 + drawn.Count) / 144.0, 0.2, 0.32); // at rest within about a quarter of a second
    });

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void A_press_while_the_zoom_moves_carries_on_from_where_the_scale_is() => session.OnUiThread(() =>
    {
        using var rig = new ZoomMotionRig();
        rig.Motion.To(At110);
        for (var frame = 0; frame < 4; frame++) rig.Frame();
        var reached = rig.Motion.Value;
        var speed = rig.Motion.Velocity;
        Assert.InRange(reached, At100 + 0.001, At110 - 0.001); // under way
        rig.Motion.To(At125); // Ctrl+Plus again
        Assert.Equal(reached, rig.Motion.Value); // the press does not move the scale itself
        Assert.True(rig.Motion.Velocity > speed); // it adds its push to the motion under way
        var drawn = rig.RunOut();
        Assert.True(drawn[0] > reached);
        for (var index = 1; index < drawn.Count; index++) Assert.True(drawn[index] >= drawn[index - 1]);
        Assert.All(drawn, value => Assert.True(value <= At125));
        Assert.Equal(At125, drawn[^1]);
        Assert.True(drawn.Count <= 20, $"{drawn.Count} frames after the second press");

        // Back the other way while it moves (Ctrl+Plus, then Ctrl+Minus): it turns, no step is larger than the first step
        // of a fresh change of that size, and it rests on the scale it was sent back to exactly.
        rig.Motion.To(1.5 * AppZoom.BaseScale);
        for (var frame = 0; frame < 3; frame++) rig.Frame();
        rig.Motion.To(At125);
        var back = rig.RunOut();
        using var fresh = new ZoomMotionRig(At125);
        fresh.Motion.To(1.5 * AppZoom.BaseScale);
        var largest = fresh.Frame() - At125;
        var previous = back[0];
        foreach (var value in back.Skip(1))
        {
            Assert.True(Math.Abs(value - previous) <= largest, $"a step of {Math.Abs(value - previous)} beyond {largest}");
            previous = value;
        }
        Assert.All(back, value => Assert.True(value >= At125 - 1e-12, $"{value} went under {At125}"));
        Assert.Equal(At125, back[^1]);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void Under_Reduce_Motion_the_scale_changes_at_once() => session.OnUiThread(() =>
    {
        using var rig = new ZoomMotionRig();
        EnvironmentValues.SetAccessibilityReduceMotion(rig.Owner, true);
        rig.Motion.To(At125);
        Assert.Equal(At125, rig.Motion.Value);
        Assert.Equal(new[] { At125 }, rig.Applied);
        Assert.False(rig.Motion.IsMoving);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Equal(1, rig.Rests);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-textsize-3")]
    [InlineData(100, 110)] // Ctrl+Plus: the text wraps narrower at the press and grows into the pane
    [InlineData(110, 100)] // Ctrl+Minus: the text wraps wider at the press (past the pane's edge for a moment) and shrinks into it
    public void The_line_under_the_pointer_stays_on_its_row_of_pixels_while_a_long_thread_zooms(int from, int to) =>
        ThreadTimelineRig.Run(session, mount =>
        {
            mount.Model.Settings.AppZoom = from;
            mount.Window.Content = null;
            var root = new HostedEnvironment(mount.Model, new WindowLiveResize(mount.Window)).Body(mount.Host);
            mount.Window.Content = root;
            mount.SetEntries(TimelineMount.Turns(40, sentences: 8, unique: true));
            mount.Settle();
            var scroll = mount.Scroll;
            var end = scroll.Extent.Height - scroll.Viewport.Height;
            Assert.True(end > 2000, $"the thread is {end} points past one screen");
            SoftScroll.ScrollTo(scroll, new Vector(0, end / 2), null); // a reader in the middle of the thread
            mount.Advance(400);
            var focus = new Point(500, 430);
            mount.Window.MouseMove(focus);
            Dispatcher.UIThread.RunJobs();
            // The innermost thing drawn under the pointer (a line of the reply), and the point of it under the pointer as a fraction of its height.
            var text = scroll.GetVisualDescendants().OfType<Control>().Where(part => part.IsEffectivelyVisible && part.Bounds.Height > 0 && OnWindow(part, mount).Contains(focus))
                .OrderBy(part => part.Bounds.Height).First();
            var fraction = (focus.Y - OnWindow(text, mount).Y) / OnWindow(text, mount).Height;
            double Row() { var box = OnWindow(text, mount); return box.Y + fraction * box.Height; }
            var motion = Assert.IsType<ZoomMotion>(ZoomMotion.Of(root));
            var right = new List<double>();
            var rows = new List<double>();
            var widths = new List<double>();

            mount.Model.Settings.AppZoom = to;
            Dispatcher.UIThread.RunJobs();
            Assert.True(motion.IsMoving);
            // The press: the text takes its width at the new zoom (it wraps once, now), the line under the pointer kept.
            mount.Window.UpdateLayout();
            Assert.InRange(Row(), focus.Y - 1, focus.Y + 1);
            var held = text.Bounds.Width;
            for (var frame = 0; frame < 60 && motion.IsMoving; frame++)
            {
                var step = TimeSpan.FromSeconds(1 / 60.0);
                mount.Store.Main.Time.Advance(step);
                mount.Frames.Advance(step);
                Dispatcher.UIThread.RunJobs();
                mount.Window.UpdateLayout();
                rows.Add(Row());
                right.Add(OnWindow(text, mount).Right);
                widths.Add(text.Bounds.Width);
            }
            // Every frame: the line under the pointer within a pixel of where it was; the text keeps its wrap width (no rewrap a
            // frame) and follows the scale, its edge moving one way only, and the last frame moves it under a pixel.
            var way = Math.Sign(to - from);
            Assert.True(rows.All(row => Math.Abs(row - focus.Y) <= 1), $"the line under the pointer left its row: {string.Join(", ", rows.Select(row => row.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
            Assert.All(widths, width => Assert.Equal(held, width, 6));
            for (var index = 1; index < right.Count; index++) Assert.True((right[index] - right[index - 1]) * way >= -0.01, $"the text's edge went back at frame {index}: {string.Join(", ", right.Select(edge => edge.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
            Assert.True((right[^1] - right[0]) * way > 10, "the text did not follow the scale");
            Assert.True(Math.Abs(right[^1] - right[^2]) < 1, "the last frame moved the text a pixel or more");
            Assert.False(motion.IsMoving);
            Assert.Equal(to / 100.0 * AppZoom.BaseScale, motion.Value);
            Assert.Equal(to / 100.0 * AppZoom.BaseScale, Assert.IsType<ScaleTransform>(mount.Host.RenderTransform).ScaleX);
            // At rest the text's width is the pane's own, which is the width it held: nothing rewraps, nothing moves later
            // (the timeline's own scroll keeping does not take the reader elsewhere).
            mount.Advance(600);
            Assert.Equal(held, text.Bounds.Width, 6);
            Assert.InRange(Row(), focus.Y - 1, focus.Y + 1);
        }, width: 1000, height: 860); // narrow enough that the column rewraps at 110 % (it is 820 points at most)

    [Theory]
    [Trait("Decision", "ED-NEW-perfmotion-3")]
    [InlineData(100, 110)]
    [InlineData(110, 100)]
    public void With_the_pointer_on_the_composer_the_threads_reading_line_stays_on_its_row_while_it_zooms(int from, int to) =>
        ThreadTimelineRig.Run(session, mount =>
        {
            // The thread above a composer that is not in its scroll view (ChatView's '*,Auto' rows), the pointer resting on the composer.
            mount.Model.Settings.AppZoom = from;
            mount.Window.Content = null;
            var composer = new Border { Height = 120, Background = Brushes.Transparent };
            var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            page.Children.Add(mount.Host);
            Grid.SetRow(composer, 1);
            page.Children.Add(composer);
            var root = new HostedEnvironment(mount.Model, new WindowLiveResize(mount.Window)).Body(page);
            mount.Window.Content = root;
            mount.SetEntries(TimelineMount.Turns(40, sentences: 8, unique: true));
            mount.Settle();
            var scroll = mount.Scroll;
            var end = scroll.Extent.Height - scroll.Viewport.Height;
            SoftScroll.ScrollTo(scroll, new Vector(0, end / 2), null);
            mount.Advance(400);
            var onComposer = OnWindow(composer, mount).Center;
            Assert.False(OnWindow(scroll, mount).Contains(onComposer));
            mount.Window.MouseMove(onComposer);
            Dispatcher.UIThread.RunJobs();
            // The thread's own reading line: half its width across, a third of its height down.
            var box = OnWindow(scroll, mount);
            var line = new Point(box.X + box.Width / 2, box.Y + box.Height * ZoomMotion.ReadingLine);
            var text = scroll.GetVisualDescendants().OfType<Control>().Where(part => part.IsEffectivelyVisible && part.Bounds.Height > 0 && OnWindow(part, mount).Contains(line))
                .OrderBy(part => part.Bounds.Height).First();
            var fraction = (line.Y - OnWindow(text, mount).Y) / OnWindow(text, mount).Height;
            double Row() { var at = OnWindow(text, mount); return at.Y + fraction * at.Height; }
            var motion = Assert.IsType<ZoomMotion>(ZoomMotion.Of(root));
            var rows = new List<double>();

            mount.Model.Settings.AppZoom = to;
            Dispatcher.UIThread.RunJobs();
            Assert.True(motion.IsMoving);
            mount.Window.UpdateLayout();
            rows.Add(Row());
            for (var frame = 0; frame < 60 && motion.IsMoving; frame++)
            {
                var step = TimeSpan.FromSeconds(1 / 60.0);
                mount.Store.Main.Time.Advance(step);
                mount.Frames.Advance(step);
                Dispatcher.UIThread.RunJobs();
                mount.Window.UpdateLayout();
                rows.Add(Row());
            }
            Assert.False(motion.IsMoving);
            Assert.True(rows.All(row => Math.Abs(row - line.Y) <= 1), $"the thread's reading line left its row: {string.Join(", ", rows.Select(row => row.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
        }, width: 1000, height: 860);

    private static Rect OnWindow(Visual visual, TimelineMount mount) =>
        new(visual.TranslatePoint(default, mount.Window)!.Value, visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), mount.Window)!.Value);

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void Every_window_moves_its_scale_with_the_caption_and_shows_the_zoom_on_a_glass_capsule() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        manager.ShowMain();
        var main = MainOf(manager);
        RunFrames(time, main);
        var indicator = Assert.Single(main.GetVisualDescendants().OfType<ZoomIndicator>());
        Assert.False(indicator.IsVisible);
        Assert.Null(indicator.Child); // hidden, the capsule holds no words to lay out or draw
        Assert.False(indicator.IsHitTestVisible);
        var cluster = WindowChrome.CaptionCluster(main);
        var zoom = main.GetVisualDescendants().OfType<ZoomLayout>().First();
        var shown = new List<string>();
        var scales = new List<double>();
        var captions = new List<double>();

        manager.Model.Settings.AppZoom = 110;
        RunFrames(time, main, seconds: 0.4, each: () =>
        {
            shown.Add(indicator.Text);
            scales.Add(AppZoom.ScaleOfWindow(main));
            captions.Add(Assert.IsType<ScaleTransform>(cluster.RenderTransform).ScaleX);
            Assert.True(indicator.IsVisible);
            Assert.Equal(indicator.Text, Assert.IsType<TextBlock>(indicator.Child).Text);
        });
        // The content and the caption buttons at the same scale every frame, rising to 110 % exactly; the capsule counts with it.
        Assert.Equal(scales, captions);
        Assert.True(scales[0] > At100 && scales[0] < At110);
        Assert.Equal(At110, scales[^1]);
        Assert.Equal(At110, zoom.Scale);
        var counted = shown.Select(text => int.Parse(text.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(counted.OrderBy(value => value), counted);
        Assert.Contains(counted, value => value > 100 && value < 110);
        Assert.Equal("110%", shown[^1]);
        Assert.Equal(1, indicator.Opacity);
        // It holds, then fades out over frames, and is gone.
        var fading = new List<double>();
        RunFrames(time, main, seconds: 1.5, each: () => fading.Add(indicator.Opacity));
        Assert.Contains(fading, opacity => opacity > 0 && opacity < 1);
        Assert.False(indicator.IsVisible);
        Assert.Null(indicator.Child); // faded out, its words are taken down with it
    }, realSettings: false);

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-3")]
    public void Under_Reduce_Motion_the_window_takes_the_new_zoom_at_once_and_only_the_capsule_shows_it() => WithManager((_, manager) =>
    {
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        manager.ShowMain();
        var main = MainOf(manager);
        EnvironmentValues.SetAccessibilityReduceMotion(main, true);
        RunFrames(time, main);
        var indicator = Assert.Single(main.GetVisualDescendants().OfType<ZoomIndicator>());
        manager.Model.Settings.AppZoom = 125;
        Drain(main);
        Assert.Equal(At125, AppZoom.ScaleOfWindow(main)); // at once, before any frame
        Assert.True(indicator.IsVisible);
        Assert.Equal(1, indicator.Opacity);
        Assert.Equal("125%", indicator.Text);
        var opacities = new List<double>();
        RunFrames(time, main, seconds: 1.5, each: () => opacities.Add(indicator.Opacity));
        Assert.All(opacities, opacity => Assert.True(opacity is 0 or 1, $"a fade frame at {opacity}"));
        Assert.False(indicator.IsVisible);
    }, realSettings: false);
}
