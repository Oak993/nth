using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// OD-0113 supersedes the Comet startup; the existing entry point must run B.
[Collection("Headless")]
public sealed class AuroraBootIntegrationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_existing_boot_entry_point_uses_the_Aurora_stage() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var main = new Avalonia.Controls.Window { Width = 900, Height = 700, Content = new Border() };
        var boot = BootStageWindow.Start(main, false, main.Show);
        try { Assert.Equal("AuroraBootStage", boot.Window.Content!.GetType().Name); }
        finally { boot.Window.Close(); main.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-252")]
    public void Startup_prepares_behind_the_stage_and_never_reveals_the_window_until_preparation_finishes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var preparation = new TaskCompletionSource();
        var warmStarted = false;
        var reveals = 0;
        BootStageWindow? boot = null;
        launch.Manager.MainContent = () => new RootView();
        launch.Delegate.FirstRun = new FirstRunPreparation(new FirstRunPreparation.Seams(() => false,
            () => [], _ => Task.CompletedTask, (host, _, _) =>
            {
                warmStarted = true; Assert.Same(boot!.Window, host); return preparation.Task;
            }), TimeProvider.System);
        launch.Delegate.BootstrapModel = _ => Task.CompletedTask;
        launch.Delegate.StartBoot = (windows, show) => boot = BootStageWindow.Start(windows.PrepareMain(), false, () => { reveals++; show(); });
        try
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
            Assert.True(warmStarted);
            for (var i = 0; i < 240; i++)
            {
                launch.Harness.Main.Advance(TimeSpan.FromSeconds(1.0 / 60)); frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(0, reveals);
            launch.Harness.Main.OnMain(preparation.SetResult);
            for (var i = 0; i < 240; i++)
            {
                launch.Harness.Main.Advance(TimeSpan.FromSeconds(1.0 / 60)); frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(1, reveals);
        }
        finally { preparation.TrySetResult(); boot?.Window.Close(); }
    });
}
