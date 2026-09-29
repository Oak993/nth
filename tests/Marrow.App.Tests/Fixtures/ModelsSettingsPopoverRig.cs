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
}
