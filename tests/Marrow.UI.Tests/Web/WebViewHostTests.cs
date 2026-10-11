using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Web;

namespace Marrow.UI.Tests.Web;

// DESIGN 2.5 and 13.3: WebViewHost with a fake web view. Live -> Snapshot -> Settling ordering: the image is on screen
// (a composed frame carried it) before the live view hides, and the live view shows before the image goes, so no frame
// shows neither. The image is anchored top-left at its device scale and never stretches; growing reveals the page
// colour; the hidden live view is resized at most every 100 ms. DESIGN 2.1 window transitions, 2.7 failure cards,
// 2.3 suspended and discarded views, 10 Tab out of the page.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w7-3")]
[Trait("Feature", "W-7")]
public sealed class WebViewHostTests(HeadlessSession headless)
{
    private static readonly Color Page = Color.FromRgb(0x20, 0x80, 0xC0);

    private static (Window Window, WebViewHost Host, FakeWebViewSurface Surface, List<string> Steps) Shown(TimeProvider? time = null, double width = 400, double height = 300)
    {
        var surface = new FakeWebViewSurface { LastSnapshot = FakeWebViewSurface.Frame(40, 30, 1, Page, 0xFF102030) };
        var host = new WebViewHost(surface, _ => new Border { Focusable = true, Background = Brushes.Red }, time);
        var steps = new List<string>();
        host.Step += steps.Add;
        var window = new Window { Width = width, Height = height, Content = host };
        window.Show();
        FakeWebViewSurface.Pump(host.WhenIdleAsync());
        Dispatcher.UIThread.RunJobs();
        return (window, host, surface, steps);
    }

    private static int At(List<string> steps, string step)
    {
        var index = steps.IndexOf(step);
        Assert.True(index >= 0, $"no '{step}' in {string.Join(", ", steps)}");
        return index;
    }

    [Fact]
    public void A_gesture_puts_the_image_on_screen_before_the_live_view_hides_and_back_the_other_way()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.NotNull(host.LiveContent);
                var seen = new List<(bool Visible, bool ImageShown, bool Presented)>();
                surface.OnLiveVisible = visible => seen.Add((visible, host.SnapshotImage.IsVisible, steps.Contains("presented")));
                surface.GestureFrame = new WebGestureFrame(FakeWebViewSurface.Frame(40, 30, 1, Page, 0xFF102030), null);

                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);

                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.True(At(steps, "image shown") < At(steps, "presented"));
                Assert.True(At(steps, "presented") < At(steps, "live hidden"));
                Assert.Equal((false, true, true), Assert.Single(seen));
                Assert.Contains("Capture", surface.Log);

                FakeWebViewSurface.Done(gesture).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());

                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.True(At(steps, "settling") < At(steps, "frames"));
                Assert.True(At(steps, "frames") < At(steps, "live shown"));
                Assert.True(At(steps, "live shown") < At(steps, "image removed"));
                Assert.Equal((true, true, true), seen[1]); // the image was still up when the live view came back
                Assert.False(host.SnapshotImage.IsVisible);
                Assert.False(host.IsShowingSnapshot);
                Assert.True(surface.IsLiveVisible);
                var settleBounds = surface.Log.FindLastIndex(l => l.StartsWith("Bounds ", StringComparison.Ordinal));
                Assert.True(settleBounds < surface.Log.LastIndexOf("Frames 2"), string.Join(", ", surface.Log));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_snapshot_is_anchored_top_left_at_its_device_scale_and_never_stretches()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, _) = Shown();
            try
            {
                surface.GestureFrame = new WebGestureFrame(FakeWebViewSurface.Frame(300, 200, 2, Page, 0xFF102030), null);
                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);
                Assert.Equal(new Rect(0, 0, 150, 100), host.SnapshotImage.Bounds);

                window.Width = 600;
                window.Height = 500;
                FakeWebViewSurface.PumpUntil(() => host.Bounds.Width == 600);

                Assert.Equal(new Rect(0, 0, 150, 100), host.SnapshotImage.Bounds); // growing reveals, never stretches
                Assert.Equal(Stretch.Fill, host.SnapshotImage.Stretch); // filling exactly its own fixed device-pixel size
                Assert.True(host.ClipToBounds);
                FakeWebViewSurface.Done(gesture).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Growing_reveals_the_page_colour_and_else_the_frame_edge()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, _) = Shown();
            try
            {
                surface.GestureFrame = new WebGestureFrame(FakeWebViewSurface.Frame(40, 30, 1, Page, 0xFF102030), null);
                var first = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(first);
                var backdrop = host.GetVisualChildren().OfType<Border>().First(b => b.IsVisible && b.Background is ISolidColorBrush);
                Assert.Equal(Color.FromUInt32(0xFF102030), ((ISolidColorBrush)backdrop.Background!).Color);
                FakeWebViewSurface.Done(first).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());

                surface.GestureFrame = new WebGestureFrame(FakeWebViewSurface.Frame(40, 30, 1, Page), null);
                var second = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(second);
                backdrop = host.GetVisualChildren().OfType<Border>().First(b => b.IsVisible && b.Background is ISolidColorBrush);
                Assert.Equal(Page, ((ISolidColorBrush)backdrop.Background!).Color);
                FakeWebViewSurface.Done(second).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void While_the_snapshot_shows_the_hidden_live_view_takes_a_new_size_at_most_every_100_ms()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var (window, host, surface, _) = Shown(time);
            try
            {
                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);
                Assert.Equal(new Size(400, 300), host.LiveContent!.Bounds.Size);

                window.Width = 500;
                FakeWebViewSurface.PumpUntil(() => host.Bounds.Width == 500);
                Assert.Equal(new Size(400, 300), host.LiveContent.Bounds.Size);
                Assert.DoesNotContain("Bounds 500x300@1", surface.Log);

                time.Advance(WebViewHost.HeldResizeInterval);
                host.InvalidateMeasure();
                host.InvalidateArrange();
                window.UpdateLayout();

                Assert.Equal(new Size(500, 300), host.LiveContent.Bounds.Size);
                Assert.Contains("Bounds 500x300@1", surface.Log);
                FakeWebViewSurface.Done(gesture).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_crash_card_shows_over_the_dimmed_snapshot_and_its_buttons_reach_the_view()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                surface.Fail(WebFailureCard.For(WebFailureKind.RendererCrashed));
                FakeWebViewSurface.Pump(host.WhenIdleAsync());

                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.False(surface.IsLiveVisible);
                Assert.Equal(WebFailureKind.RendererCrashed, host.ShownCard?.Kind);
                Assert.True(host.FailureLayer.IsVisible);
                var texts = host.FailureLayer.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("This page stopped working.", texts);
                Assert.Contains("Reload opens the page again. Anything typed into a form on it is gone.", texts);
                Assert.Contains("Reload", texts);
                Assert.Contains("Close tab", texts);
                var dim = host.GetVisualChildren().OfType<Border>().Single(b => b.Background is ISolidColorBrush { Color.A: 0xFF, Color.R: 0, Opacity: WebViewHost.FailureDimOpacity });
                Assert.True(dim.IsVisible);

                var buttons = host.FailureLayer.GetVisualDescendants().OfType<Button>().ToList();
                Assert.Equal(2, buttons.Count);
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([WebFailureAction.Reload, WebFailureAction.CloseTab], surface.Actions);

                surface.Fail(null);
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.False(host.FailureLayer.IsVisible);
                Assert.True(At(steps, "card hidden") > At(steps, "card shown"));
                Assert.True(surface.IsLiveVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_failure_card_comes_in_with_the_pane_spring_by_opacity_and_scale_only()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var clock = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var swift = Marrow.Core.Port.SwiftClock.Provider.Override(clock);
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
            var (window, host, surface, _) = Shown();
            try
            {
                var dim = host.GetVisualChildren().OfType<Border>().Single(b => b.Background is ISolidColorBrush { Color.A: 0xFF, Color.R: 0, Opacity: WebViewHost.FailureDimOpacity });
                surface.Fail(WebFailureCard.For(WebFailureKind.RendererCrashed));
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.False(surface.IsLiveVisible); // the motion starts only once the live view is hidden
                var card = host.FailureLayer.Children.Single();
                Assert.Equal(0, card.Opacity);
                Assert.Equal(0, dim.Opacity);
                Assert.Equal(WebViewHost.CardStartScale, Assert.IsType<ScaleTransform>(card.RenderTransform).ScaleX, 6);
                Dispatcher.UIThread.RunJobs();
                var bounds = card.Bounds;

                Step(0.08);
                Assert.InRange(card.Opacity, 0.05, 0.99);
                Assert.InRange(Assert.IsType<ScaleTransform>(card.RenderTransform).ScaleX, WebViewHost.CardStartScale + 0.001, 0.9999);
                Assert.Equal(card.Opacity, dim.Opacity, 6);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(bounds, card.Bounds); // layout never moves: opacity and the render transform only

                Step(1);
                Assert.Equal(1, card.Opacity);
                Assert.Null(card.RenderTransform);
                Assert.Equal(1, dim.Opacity);

                // Another card replacing it: the dim carries on from where it is, the new card comes in.
                surface.Fail(WebFailureCard.For(WebFailureKind.RendererHung));
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(1, dim.Opacity);
                Assert.Equal(0, host.FailureLayer.Children.Single().Opacity);
                Step(1);
                Assert.Equal(1, host.FailureLayer.Children.Single().Opacity);

                // Reduce Motion: a 0.18 s ease-out and no scale.
                surface.Fail(null);
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                using var still = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
                surface.Fail(WebFailureCard.For(WebFailureKind.RendererCrashed));
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                var calm = host.FailureLayer.Children.Single();
                Assert.Equal(0, calm.Opacity);
                Assert.Null(calm.RenderTransform);
                Step(0.09);
                Assert.InRange(calm.Opacity, 0.05, 0.99);
                Assert.Null(calm.RenderTransform);
                Step(0.1);
                Assert.Equal(1, calm.Opacity);
            }
            finally
            {
                window.Close();
            }

            void Step(double seconds)
            {
                clock.Advance(TimeSpan.FromSeconds(seconds));
                frames.Advance(TimeSpan.FromSeconds(seconds));
            }
        });
    }

    [Fact]
    public void A_failure_for_a_pane_that_shows_no_card_keeps_the_snapshot_without_one()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, _) = Shown();
            try
            {
                surface.Fail(WebFailureCard.For(WebFailureKind.BrowserGone, showsCard: false));
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.Null(host.ShownCard);
                Assert.False(host.FailureLayer.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Suspended_and_discarded_views_show_their_snapshot_and_a_click_wakes_them()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                surface.Set(WebLiveness.Suspended);
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.Same(surface.LastSnapshot, host.ShownFrame);
                var live = host.LiveContent;
                Assert.NotNull(live);

                window.MouseDown(new Point(20, 20), MouseButton.Left);
                window.MouseUp(new Point(20, 20), MouseButton.Left);
                Assert.Contains("Activate", surface.Log);

                surface.Set(WebLiveness.Discarded);
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Null(host.LiveContent);
                Assert.Contains("live dropped", steps);
                Assert.True(host.IsShowingSnapshot);

                surface.Set(WebLiveness.Pending); // restored: a new platform view, the snapshot stays until it is live
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.NotNull(host.LiveContent);
                Assert.NotSame(live, host.LiveContent);
                Assert.Equal(WebViewHostState.Snapshot, host.State);

                surface.Set(WebLiveness.Live);
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.True(surface.IsLiveVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void With_no_usable_runtime_no_platform_view_is_made_and_the_install_card_shows()
    {
        headless.OnUiThread(() =>
        {
            var surface = new FakeWebViewSurface();
            surface.Fail(WebFailureCard.For(WebFailureKind.RuntimeMissing));
            surface.Set(WebLiveness.Closed);
            var made = 0;
            var host = new WebViewHost(surface, _ =>
            {
                made++;
                return new Border();
            });
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            try
            {
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(0, made);
                Assert.Null(host.LiveContent);
                Assert.Equal(WebFailureKind.RuntimeMissing, host.ShownCard?.Kind);
                Assert.Contains("Install", host.FailureLayer.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                Assert.Null(host.GetVisualChildren().OfType<Border>().FirstOrDefault(b => b.IsVisible && b.Background is ISolidColorBrush { Opacity: WebViewHost.FailureDimOpacity }));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_window_transition_swaps_every_web_view_in_that_window_and_no_other()
    {
        headless.OnUiThread(() =>
        {
            var surfaces = Enumerable.Range(0, 3).Select(_ => new FakeWebViewSurface { LastSnapshot = FakeWebViewSurface.Frame(10, 10, 1, Page) }).ToList();
            var hosts = surfaces.Select(s => new WebViewHost(s, _ => new Border())).ToList();
            var fading = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { hosts[0], hosts[1] } } };
            var other = new Window { Width = 400, Height = 300, Content = hosts[2] };
            fading.Show();
            other.Show();
            try
            {
                foreach (var host in hosts) FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(2, WebViewHost.HostsIn(fading).Count);

                var transition = WebViewHost.BeginWindowTransitionAsync(fading);
                FakeWebViewSurface.Pump(transition);

                Assert.Equal(WebViewHostState.Snapshot, hosts[0].State);
                Assert.Equal(WebViewHostState.Snapshot, hosts[1].State);
                Assert.Equal(WebViewHostState.Live, hosts[2].State);
                Assert.False(surfaces[0].IsLiveVisible);
                Assert.True(surfaces[2].IsLiveVisible);

                FakeWebViewSurface.Done(transition).Dispose();
                foreach (var host in hosts) FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.All(hosts, h => Assert.Equal(WebViewHostState.Live, h.State));
                Assert.All(surfaces, s => Assert.True(s.IsLiveVisible));
            }
            finally
            {
                fading.Close();
                other.Close();
            }
        });
    }

    [Fact]
    public void A_fresh_capture_that_lands_late_replaces_the_cached_frame()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                var cached = FakeWebViewSurface.Frame(40, 30, 1, Colors.Black);
                var fresh = FakeWebViewSurface.Frame(40, 30, 1, Colors.White);
                var later = new TaskCompletionSource<WebSnapshot?>();
                surface.GestureFrame = new WebGestureFrame(cached, later.Task);

                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);
                Assert.Same(cached, host.ShownFrame);

                later.SetResult(fresh);
                FakeWebViewSurface.PumpUntil(() => steps.Contains("fresh frame"));

                Assert.Same(fresh, host.ShownFrame);
                Assert.Equal(WebViewHostState.Snapshot, host.State);
                FakeWebViewSurface.Done(gesture).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Under_Reduce_Motion_the_fresh_frame_replaces_the_cached_one_without_a_fade()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var (window, host, surface, steps) = Shown(time);
            try
            {
                EnvironmentValues.SetAccessibilityReduceMotion(window, true);
                var fresh = FakeWebViewSurface.Frame(40, 30, 1, Colors.White);
                var later = new TaskCompletionSource<WebSnapshot?>();
                surface.GestureFrame = new WebGestureFrame(FakeWebViewSurface.Frame(40, 30, 1, Colors.Black), later.Task);
                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);

                later.SetResult(fresh);
                FakeWebViewSurface.PumpUntil(() => steps.Contains("fresh frame")); // the manual clock never moved: no fade ran

                Assert.Same(fresh, host.ShownFrame);
                FakeWebViewSurface.Done(gesture).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_gesture_with_no_frame_keeps_the_view_live()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, _) = Shown();
            try
            {
                surface.LastSnapshot = null;
                surface.GestureFrame = new WebGestureFrame(null, null);
                var gesture = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(gesture);
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.DoesNotContain("SetLiveVisible False", surface.Log);
                FakeWebViewSurface.Done(gesture).Dispose();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_gesture_that_starts_while_settling_keeps_the_snapshot()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                var first = host.BeginGestureAsync();
                FakeWebViewSurface.Pump(first);
                surface.HeldFrames = new TaskCompletionSource<bool>();
                FakeWebViewSurface.Done(first).Dispose();
                FakeWebViewSurface.PumpUntil(() => surface.Log.Contains("Frames 2"));
                Assert.Equal(WebViewHostState.Settling, host.State);

                var second = host.BeginGestureAsync();
                surface.HeldFrames.SetResult(true);
                FakeWebViewSurface.Pump(second);

                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.DoesNotContain("live shown", steps);
                Assert.False(surface.IsLiveVisible);

                surface.HeldFrames = null;
                FakeWebViewSurface.Done(second).Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.True(surface.IsLiveVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_live_view_the_engine_hid_shows_again_after_its_frames()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                surface.HideLive();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.True(surface.IsLiveVisible);
                Assert.True(At(steps, "frames") < At(steps, "live shown"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Tab_out_of_the_page_moves_focus_on_in_nths_order()
    {
        headless.OnUiThread(() =>
        {
            var surface = new FakeWebViewSurface();
            var before = new Button { Content = "before" };
            var after = new Button { Content = "after" };
            var host = new WebViewHost(surface, _ => new Border { Focusable = true, Width = 100, Height = 50 });
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { before, host, after } } };
            window.Show();
            try
            {
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                var focus = window.FocusManager!;

                surface.RaiseFocusExit(forward: true);
                Assert.Same(after, focus.GetFocusedElement());

                surface.RaiseFocusExit(forward: false);
                Assert.Same(before, focus.GetFocusedElement());
            }
            finally
            {
                window.Close();
            }
        });
    }
}
