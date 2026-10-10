using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-launch-fix (6): while Boot B draws, the idle warm-ups (the model chips') compile nothing on the render thread, not
// even once their 3 s cap has run out; B lets them go when it finishes, and a hold that has nothing to hold holds nothing.
[Collection("Headless")]
public sealed class GpuWarmupBootHoldTests(HeadlessSession headless)
{
    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_hold_keeps_the_idle_warm_up_from_stepping_past_its_cap_until_it_lets_go() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(200);
        var host = new Border { Width = 40, Height = 20 };
        var window = new AvaloniaWindow { Width = 200, Height = 100, Content = host };
        window.Show();
        var hold = GpuWarmup.HoldIdle(() => true);
        try
        {
            Assert.True(GpuWarmup.Held());
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.WhenIdle(host);
            FramesFor(TimeSpan.FromSeconds(1.2), () => false); // six caps' worth of calm frames
            Assert.True(asked == GpuWarmup.StepsAsked, $"{GpuWarmup.StepsAsked - asked} steps asked while held");
            hold.Dispose();
            Assert.False(GpuWarmup.Held());
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(GpuWarmup.StepsAsked > asked, "no step once let go");
        }
        finally { hold.Dispose(); GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_hold_that_answers_false_holds_nothing_and_letting_go_twice_is_harmless() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromMilliseconds(200);
        frames.RequestFrame(_ => { }); // animating throughout: only the cap can step
        var host = new Border { Width = 40, Height = 20 };
        var window = new AvaloniaWindow { Width = 200, Height = 100, Content = host };
        window.Show();
        var hold = GpuWarmup.HoldIdle(() => false); // its window gone
        try
        {
            Assert.False(GpuWarmup.Held());
            var asked = GpuWarmup.StepsAsked;
            GpuWarmup.WhenIdle(host);
            FramesFor(TimeSpan.FromSeconds(10), () => GpuWarmup.StepsAsked > asked);
            Assert.True(GpuWarmup.StepsAsked > asked, "a hold answering false stopped the cap");
            hold.Dispose();
            hold.Dispose();
        }
        finally { hold.Dispose(); GpuWarmup.IdleCap = cap; window.Close(); }
    });

    [Theory, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    [InlineData(false)]
    [InlineData(true)]
    public void Boot_B_holds_the_idle_warm_ups_from_its_start_until_it_finishes(bool reduceMotion) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = new AvaloniaWindow { Width = 900, Height = 600, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(120, 80), Content = new Border() };
        Assert.False(GpuWarmup.Held());
        var boot = BootStageWindow.Start(main, reduceMotion, main.Show);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(GpuWarmup.Held());
            boot.Ready();
            var shown = false;
            boot.MainWindowShown += (_, _) => shown = true;
            for (var t = 0.0; t < 12 && boot.Window.IsVisible; t += .05)
            {
                if (!shown) Assert.True(GpuWarmup.Held(), $"let go at {t:F2} s, before the hand-over");
                time.Advance(TimeSpan.FromSeconds(.05));
                frames.Advance(TimeSpan.FromSeconds(.05));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(shown);
            Assert.False(boot.Window.IsVisible);
            Assert.False(GpuWarmup.Held()); // B finished: the idle warm-ups may step
        }
        finally { boot.Window.Close(); main.Close(); }
        Assert.False(GpuWarmup.Held());
    });

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_stage_closed_before_it_finished_lets_go() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = new AvaloniaWindow { Width = 900, Height = 600, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(120, 80), Content = new Border() };
        var boot = BootStageWindow.Start(main, false, main.Show);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(GpuWarmup.Held());
            boot.Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(GpuWarmup.Held());
        }
        finally { boot.Window.Close(); main.Close(); }
    });

    private static void FramesFor(TimeSpan span, Func<bool> done)
    {
        var since = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - since < span)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
