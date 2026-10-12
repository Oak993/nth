using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Web W4: a real thread in a real detail view (its chat column with the terminal drawer and the toolbar, the pane workspace
/// around it, the pane factories the app registers), on a manual frame clock, with the popup seam recording. Helper types live
/// here, never inside a test class.
/// </summary>
public sealed class ChangesTerminalScene : IDisposable
{
    private readonly IDisposable cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
    private readonly ManualFrameSource manualFrames = new();
    private readonly IDisposable frames;
    private readonly IDisposable presenter;
    private IDisposable? actor;

    public ChangesTerminalScene()
    {
        H = new AppModelHarness();
        frames = FrameClock.Source.Override(manualFrames);
        Recorder = new RecordingPresenter(); // headless cannot open a real popup (the Alt+click popover)
        presenter = Presentation.Presenter.Override(Recorder);
        Model = H.MakeModel();
        Window = new Avalonia.Controls.Window { Width = 1400, Height = 900 };
        Detail = new DetailView { PaneFactory = PaneContents.Create };
    }

    public AppModelHarness H { get; }

    public RecordingPresenter Recorder { get; }

    public AppModel Model { get; }

    public Avalonia.Controls.Window Window { get; }

    public DetailView Detail { get; }

    public Guid ThreadID { get; private set; }

    public ThreadRuntime Runtime => Model.Runtime(ThreadID);

    public PaneWorkspace Workspace => Detail.Workspace!;

    public void Open()
    {
        WindowLiveResize resize = null!;
        H.Main.OnMain(() =>
        {
            Model.Settings.ShowsUsagePanel = false;
            Model.Settings.HydraEnabled = false;
            var project = Model.AddProject(new Project("Project", H.NewFolder("project")).Url);
            ThreadID = Model.NewThread(project)!.Id;
            resize = new WindowLiveResize(Window);
            Window.Show();
        });
        H.Main.Loop.Run(async () => await Model.Runtime(ThreadID).EnsureLoaded(), AppModelHarness.Bound);
        actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        Marrow.App.Views.Diff.DiffPane.Register();
        Marrow.App.Views.Terminal.TerminalPane.Register();
        H.Main.OnMain(() =>
        {
            Model.SelectedThreadID = ThreadID;
            AppEnvironment.SetAppModel(Detail, Model);
            AppEnvironment.SetWindowLiveResize(Detail, resize);
            Window.Content = Detail;
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        Pump();
    }

    public void Pump(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            H.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            });
            H.Main.AdvanceMilliseconds(16);
            manualFrames.Advance(TimeSpan.FromMilliseconds(16));
            H.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            });
        }
    }

    /// <summary>Frames until the condition holds (at most <paramref name="limit"/>); the transitions and springs need them.</summary>
    public bool PumpUntil(Func<bool> condition, int limit = 200)
    {
        for (var i = 0; i < limit; i++)
        {
            if (condition()) return true;
            Pump();
        }
        return condition();
    }

    public IEnumerable<T> All<T>() where T : class => Detail.GetVisualDescendants().OfType<T>();

    public Button ToolbarButton(string tip) => All<Button>().First(button => ToolTip.GetTip(button) is string text && text == tip);

    public void Dispose()
    {
        H.Main.OnMain(() =>
        {
            Model.Terminals.TerminateAll();
            Window.Hide();
            Window.Content = null;
            Dispatcher.UIThread.RunJobs();
        });
        actor?.Dispose();
        presenter.Dispose();
        frames.Dispose();
        cursor.Dispose();
        H.Dispose();
    }
}
