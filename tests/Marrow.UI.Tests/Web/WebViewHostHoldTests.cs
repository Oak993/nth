using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Web;

namespace Marrow.UI.Tests.Web;

// DESIGN 7.6 "Scrolling starts: snapshot in the same frame; scroll idle 150 ms and fully visible: live again", "The
// live/snapshot switch is invisible: same pixels": WebViewHost.HoldSnapshot, the inline widgets' hold. No capture is
// asked for (it would land a frame late); the newest frame there is goes up, on screen before the live view hides, and
// the last lease's end brings the live view back before the image goes.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w19-4")]
[Trait("Feature", "W-19")]
public sealed class WebViewHostHoldTests(HeadlessSession headless)
{
    private static readonly Color Page = Color.FromRgb(0x20, 0x80, 0xC0);

    private static (Window Window, WebViewHost Host, FakeWebViewSurface Surface, List<string> Steps) Shown()
    {
        var surface = new FakeWebViewSurface { LastSnapshot = FakeWebViewSurface.Frame(40, 30, 1, Page, 0xFF102030) };
        var host = new WebViewHost(surface, _ => new Border { Focusable = true, Background = Brushes.Red });
        var steps = new List<string>();
        host.Step += steps.Add;
        var window = new Window { Width = 400, Height = 300, Content = host };
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
    public void A_hold_puts_the_newest_frame_up_without_a_capture_and_its_end_brings_the_live_view_back_first()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, surface, steps) = Shown();
            try
            {
                Assert.Equal(WebViewHostState.Live, host.State);
                steps.Clear();
                var captures = surface.Log.Count(line => line == "Capture");

                var hold = host.HoldSnapshot();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());

                Assert.True(host.IsHeld);
                Assert.Equal(WebViewHostState.Snapshot, host.State);
                Assert.True(host.IsShowingSnapshot);
                Assert.True(At(steps, "image shown") < At(steps, "live hidden"));
                Assert.Equal(captures, surface.Log.Count(line => line == "Capture")); // no capture: the frame there was
                Assert.False(surface.IsLiveVisible);

                hold.Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());

                Assert.False(host.IsHeld);
                Assert.Equal(WebViewHostState.Live, host.State);
                Assert.True(At(steps, "live shown") < At(steps, "image removed"));
                Assert.True(surface.IsLiveVisible);
                Assert.False(host.IsShowingSnapshot);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Holds_nest_and_only_the_last_lease_ends_the_snapshot()
    {
        headless.OnUiThread(() =>
        {
            var (window, host, _, _) = Shown();
            try
            {
                var scrolling = host.HoldSnapshot();
                var opening = host.HoldSnapshot();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.Equal(WebViewHostState.Snapshot, host.State);

                scrolling.Dispose();
                scrolling.Dispose(); // a lease ends once
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.True(host.IsHeld);
                Assert.Equal(WebViewHostState.Snapshot, host.State);

                opening.Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.False(host.IsHeld);
                Assert.Equal(WebViewHostState.Live, host.State);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_hold_with_no_frame_yet_still_hides_the_live_view_over_the_page_colour()
    {
        headless.OnUiThread(() =>
        {
            var surface = new FakeWebViewSurface();
            var host = new WebViewHost(surface, _ => new Border { Background = Brushes.Red });
            var window = new Window { Width = 400, Height = 300, Content = host };
            window.Show();
            try
            {
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                var hold = host.HoldSnapshot();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.True(host.IsHeld);
                Assert.NotEqual(WebViewHostState.Live, host.State);
                Assert.False(surface.IsLiveVisible);
                hold.Dispose();
                FakeWebViewSurface.Pump(host.WhenIdleAsync());
                Assert.True(surface.IsLiveVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
