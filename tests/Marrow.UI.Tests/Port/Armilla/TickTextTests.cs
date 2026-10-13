using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-8 (DESIGN.md rev 5, 3.3 "Live values update at most 4 times a second and never re-lay out a card", 2.5 the shimmer on the
// shared clock, 16 "long text, right-to-left and large text"; ED-NEW-dextra-as8-5): one line of live text whose size never depends on
// its text, cut with an ellipsis, laid from the right edge in right-to-left, read by a screen reader as its text, and whose shimmer
// moves only when it is wanted, not calmed and not under Reduce Motion.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-5")]
public sealed class TickTextTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width = 320, FlowDirection flow = FlowDirection.LeftToRight)
    {
        var window = new Window { Content = content, Width = width, Height = 60, Background = Brushes.White, FlowDirection = flow };
        window.Show();
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }
        return window;
    }

    /// <summary>The leftmost and rightmost columns holding dark ink in the captured frame.</summary>
    private static (int Left, int Right) Ink(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var stride = frame.PixelSize.Width * 4;
        var bytes = new byte[stride * frame.PixelSize.Height];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(frame.PixelSize), pin.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { pin.Free(); }
        int left = int.MaxValue, right = -1;
        for (var y = 0; y < frame.PixelSize.Height; y++)
        for (var x = 0; x < frame.PixelSize.Width; x++)
        {
            var i = y * stride + x * 4;
            if (bytes[i] + bytes[i + 1] + bytes[i + 2] > 3 * 128) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
        return (left, right);
    }

    [Fact]
    public void Its_size_never_depends_on_its_text_and_a_new_value_never_asks_for_a_layout_pass()
    {
        headless.OnUiThread(() =>
        {
            var tick = new TickText { Text = "4s", FontSize = 11, Ink = Brushes.Black };
            var window = Show(new StackPanel { Children = { tick } });
            try
            {
                var size = tick.DesiredSize;
                Assert.Equal(0, size.Width);
                Assert.True(size.Height > 0);

                tick.Text = "1h 59m 59s · running the tests (40 of 40) and a great deal more besides";
                Assert.True(tick.IsMeasureValid);
                tick.Ink = Brushes.DarkGray;
                Assert.True(tick.IsMeasureValid);
                window.UpdateLayout();
                Assert.Equal(size, tick.DesiredSize);

                tick.FontSize = 22; // a larger text size is a new line height
                Assert.False(tick.IsMeasureValid);
                window.UpdateLayout();
                Assert.True(tick.DesiredSize.Height > size.Height * 1.6);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Text_that_does_not_fit_ends_inside_its_slot()
    {
        headless.OnUiThread(() =>
        {
            var tick = new TickText { Text = "Lead at work · running the tests (12 of 40) on the second machine", FontSize = 12, Ink = Brushes.Black, Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var window = Show(new StackPanel { Children = { tick } });
            try
            {
                Assert.True(tick.InkWidth > 60);
                Assert.True(tick.InkWidth <= 120);
                var (left, right) = Ink(window);
                Assert.True(left < 4 && right > 60, $"ink from {left} to {right}"); // the slot sits at the left edge, and the text fills it
                Assert.True(right <= 121, $"ink to {right}");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public void In_right_to_left_the_text_starts_from_the_right_edge(FlowDirection flow)
    {
        headless.OnUiThread(() =>
        {
            var tick = new TickText { Text = "12:04", FontSize = 14, Ink = Brushes.Black, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            var window = Show(new StackPanel { Children = { tick } }, 320, flow);
            try
            {
                var (left, right) = Ink(window);
                Assert.True(right > left);
                if (flow == FlowDirection.RightToLeft) Assert.True(left > 200, $"ink from {left} to {right}");
                else Assert.True(right < 120, $"ink from {left} to {right}");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void A_screen_reader_reads_it_as_text_with_its_value_as_the_name()
    {
        headless.OnUiThread(() =>
        {
            var tick = new TickText { Text = "3 agents" };
            var window = Show(new StackPanel { Children = { tick } });
            try
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(tick);
                Assert.Equal("3 agents", peer.GetName());
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                tick.Text = "4 agents";
                Assert.Equal("4 agents", peer.GetName());
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void The_shimmer_moves_only_when_wanted_not_calmed_and_not_under_Reduce_Motion()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
            var tick = new TickText { Text = "running the tests", Ink = Brushes.Black };
            var window = Show(new StackPanel { Children = { tick } });
            try
            {
                Assert.False(tick.Moves);
                tick.Sheen = true;
                Assert.True(tick.Moves);
                tick.Calm = true;
                Assert.False(tick.Moves); // its list's budget went still
                tick.Calm = false;
                EnvironmentValuesSetReduce(tick, true);
                Assert.False(tick.Moves);
                EnvironmentValuesSetReduce(tick, false);
                Assert.True(tick.Moves);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Its_shimmer_stops_asking_for_frames_once_it_is_hidden()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
            var tick = new TickText { Text = "running the tests", Ink = Brushes.Black, Sheen = true };
            var window = Show(new StackPanel { Children = { tick } });
            try
            {
                for (var i = 0; i < 4; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
                tick.IsVisible = false;
                for (var i = 0; i < 4; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
                var frames = tick.SheenFrames;
                for (var i = 0; i < 8; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(frames, tick.SheenFrames);
                Assert.False(tick.SheenTicking);
            }
            finally { window.Close(); }
        });
    }

    private static void EnvironmentValuesSetReduce(Control control, bool reduce) => Marrow.UI.Port.EnvironmentValues.SetAccessibilityReduceMotion(control, reduce);
}
