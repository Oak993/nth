using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

// OD-0133 (amending OD-0108 for provider checks only): Boot B hands over once the bootstrap reaches its provider refresh
// (AppModel.LaunchReady) and the preparation is done; the refresh, the sign-in checks and the catalogs after it are not
// waited for. The bootstrap before that point is still waited for (BootStageWindowTests: a scripted bootstrap that never
// reaches it keeps the window hidden), and so is the preparation.
[Collection("Headless")]
public sealed class BootLaunchReadyTests(HeadlessSession headless)
{
    private const double Step = 0.05;

    [Fact]
    [Trait("Decision", "OD-0133")]
    public void B_hands_over_once_the_bootstrap_reaches_its_provider_refresh_which_runs_on() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var refresh = new TaskCompletionSource(); // the provider refresh and everything after it: never finishes here
        var reached = new TaskCompletionSource();
        launch.Delegate.BootstrapModel = model => Run(model, reached.Task, refresh.Task);
        BootStageWindow? boot = null;
        launch.Delegate.StartBoot = (windows, show) => boot = BootStageWindow.Start(windows.PrepareMain(), reduceMotion: false, show);
        try
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Dispatcher.UIThread.RunJobs();
            var main = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(main);
            Assert.NotNull(boot);

            // before the provider refresh: hidden, however long B runs
            for (var t = 0.0; t < 6; t += Step) Advance(launch, frames, Step);
            Assert.False(main.IsVisible);

            launch.Harness.Main.OnMain(reached.SetResult); // the bootstrap reaches its provider refresh
            launch.Harness.Main.RunUntilIdle();
            for (var t = 0.0; t < 12 && boot!.Window.IsVisible; t += Step) Advance(launch, frames, Step);
            Assert.True(main.IsVisible, "the main window waited for the provider refresh");
            Assert.False(boot!.Window.IsVisible);
            Assert.False(refresh.Task.IsCompleted);
        }
        finally
        {
            refresh.TrySetResult();
            boot?.Window.Close();
        }
    });

    // a bootstrap that reaches its provider refresh when told, then waits for the refresh (AppModel.Bootstrap's shape)
    private static async Task Run(AppModel model, Task reached, Task refresh)
    {
        await reached;
        model.MarkLaunchReady();
        await refresh;
    }

    private static void Advance(LaunchHarness launch, ManualFrameSource frames, double seconds)
    {
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.RunUntilIdle();
    }
}
