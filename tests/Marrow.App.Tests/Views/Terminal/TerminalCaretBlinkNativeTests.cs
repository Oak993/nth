using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.App.Views.Terminal.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// ED-NEW-perf-idle-2 (native, not run by the author): the caret of a focused terminal blinks on the real composition path with the REAL
// opacity-loop driver, and the UI thread is asked for no frame to do it. The compositor plays the key frames on its own clock, so the
// caret's cell is sampled over about two seconds of wall time: it must be seen at (nearly) full ink, at (nearly) none, and in between,
// and the manual frame source must have delivered nothing. The lead runs it in a quiet window (the clock is the real one).
[Collection("Headless")]
public sealed class TerminalCaretBlinkNativeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void The_focused_caret_blinks_on_the_compositor_and_main_is_asked_for_no_frame() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var view = new TerminalHostView
        {
            Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
            NativeForegroundColor = new SwiftColor(1, 1),
            NativeBackgroundColor = new SwiftColor(0, 1),
            CaretColor = new SwiftColor(1, 1),
        };
        var window = new AvaloniaWindow { Width = 320, Height = 120, Content = view };
        try
        {
            window.Show();
            window.Activate();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Focus());
            var brightness = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(50);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var buffer = frame.Lock();
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                // the caret is the block in the first cell, top left: the mean of the first 6 x 12 pixels' green channel
                double sum = 0;
                for (var y = 0; y < 12; y++)
                    for (var x = 0; x < 6; x++) sum += bytes[y * buffer.RowBytes + x * 4 + 1];
                brightness.Add(sum / 72);
            }
            Assert.True(brightness.Max() - brightness.Min() > 100, $"the caret never changed: {brightness.Min():0} to {brightness.Max():0}");
            Assert.Contains(brightness, value => value > brightness.Min() + 40 && value < brightness.Max() - 40);
            Assert.Equal(0, frames.Delivered);
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
