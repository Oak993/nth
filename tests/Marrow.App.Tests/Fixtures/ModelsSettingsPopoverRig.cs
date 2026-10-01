using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class ModelsSettingsPopoverRig(TestRuntime main, AppModel model, ModelsSettingsPage page, RecordingPresenter presenter)
{
    public TestRuntime Main { get; } = main;
    public AppModel Model { get; } = model;
    public ModelsSettingsPage Page { get; } = page;
    public RecordingPresenter Presenter { get; } = presenter;

    /// <summary>The test's manual frame source, so a real popup's appearance animation (PopoverAppear) can be run out.</summary>
    public Action<TimeSpan>? AdvanceFrames { get; set; }

    /// <summary>Runs the popover's grow-in (about 0.22 s, PopoverAppear) to its end, as a person waits before clicking in it.</summary>
    public void FinishAnimations()
    {
        var span = TimeSpan.FromSeconds(0.3);
        Main.Advance(span);
        AdvanceFrames?.Invoke(span);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
