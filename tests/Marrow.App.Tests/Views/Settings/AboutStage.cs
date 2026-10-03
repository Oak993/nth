using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

/// <summary>The stage the About update view tests share (a helper, not a test class): the manual frame clock, the main executor, a running 1.7.0 and an update checker and install progress of its own.</summary>
internal sealed class AboutStage : IDisposable
{
    private readonly TestRuntime runtime;
    private readonly ManualFrameSource frames = new();
    private readonly IDisposable frameScope, main, info;
    public readonly UpdateChecker Checker;
    private readonly IDisposable shared;
    public readonly UpdateInstallProgress Progress = UpdateInstallProgressFixtures.NewProgress();
    private readonly IDisposable running;
    public Avalonia.Controls.Window? Window;

    public AboutStage(TestRuntime runtime)
    {
        this.runtime = runtime;
        main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        frameScope = FrameClock.Source.Override(frames);
        info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        Checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        shared = UpdateChecker.OverrideShared(Checker);
        running = UpdateInstallProgress.Running.Override(Progress);
    }

    public void Step(int milliseconds)
    {
        runtime.Time.Advance(TimeSpan.FromMilliseconds(milliseconds)); frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.RunJobs();
        Window?.UpdateLayout();
    }

    public void Dispose() { Window?.Close(); running.Dispose(); shared.Dispose(); info.Dispose(); frameScope.Dispose(); main.Dispose(); }
}
