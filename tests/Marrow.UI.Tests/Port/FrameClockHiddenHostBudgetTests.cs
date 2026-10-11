using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 budget: frames delivered while every window of the app is minimised. Headless (Avalonia 12.1.2, forced
// render ticks stand for a 240 Hz display's refreshes), Debug build. Measured on the unchanged base (1fdf47b43): one frame
// per refresh (240 in 240 ticks) for a display link that ticks at every frame.
[Collection("Headless")]
public sealed class FrameClockHiddenHostBudgetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_display_link_gets_at_most_360_frames_in_240_refreshes_while_the_window_is_minimised() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 400, Height = 300, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frames = new AvaloniaFrameSource(window);
        using var source = FrameClock.Source.Override(frames);
        long ticks = 0;
        var link = FrameClock.DisplayLink(() => ticks++);
        link.Add();
        window.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 10; i++) // warm-up
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        var before = ticks;
        for (var i = 0; i < 240; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        var delivered = ticks - before;
        link.Invalidate();
        window.Close();
        Assert.True(delivered <= 360, $"{delivered} frames in 240 refreshes while minimised (budget 360; 240 on the unchanged base)");
    });
}
