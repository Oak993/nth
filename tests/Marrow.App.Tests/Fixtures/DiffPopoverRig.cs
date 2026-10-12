using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Diff;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// The changes popover's coordinator over two thread runtimes, with the popup seam recording what it is asked to show
// (Avalonia headless cannot open a real popup; see RecordingPresenter). The anchors are real controls in a headless
// window. Helper types live here, never inside a test class.
public sealed class DiffPopoverRig : IDisposable
{
    private readonly AppModelHarness store;
    private readonly AppModel app;
    private readonly IDisposable presenterScope;

    private DiffPopoverRig(AppModelHarness store, AppModel app, ThreadRuntime first, ThreadRuntime second)
    {
        this.store = store;
        this.app = app;
        First = first;
        Second = second;
        Recorder = new RecordingPresenter();
        presenterScope = Presentation.Presenter.Override(Recorder);
        Tab = new Border { Width = 100, Height = 20 };
        Composer = new Border { Width = 100, Height = 20 };
        Row = new Border { Width = 100, Height = 20 };
        Window = new AvaloniaWindow
        {
            Width = 400,
            Height = 300,
            Content = new StackPanel { Children = { Tab, Composer, Row } },
        };
        Window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Coordinator = new DiffPopoverCoordinator();
    }

    public ThreadRuntime First { get; }
    public ThreadRuntime Second { get; }
    public DiffPopoverCoordinator Coordinator { get; }
    public RecordingPresenter Recorder { get; }

    /// <summary>The app model behind both runtimes; its settings hold the popover's size (web W1).</summary>
    public AppModel App => app;

    /// <summary>The changes tab's own view, in the window.</summary>
    public Border Tab { get; }

    /// <summary>The composer area, in the window.</summary>
    public Border Composer { get; }

    /// <summary>A tool row, in the window.</summary>
    public Border Row { get; }

    public AvaloniaWindow Window { get; }

    /// <summary>Whether the last thing the coordinator asked of the popup seam was to present it.</summary>
    public bool IsOpen => Recorder.Calls.Count != 0 && Recorder.Calls[^1].Presented;

    /// <summary>The popup the coordinator asked the seam to present last.</summary>
    public Popup LastPopup => Recorder.Calls[^1].Popup;

    /// <summary>Runs <paramref name="test"/> on the main loop with a fresh rig; everything is closed after it.</summary>
    public static void Run(Action<DiffPopoverRig> test)
    {
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Diff popover", store.NewFolder("diff-popover"));
        var firstThread = AppModelSamples.Thread(project);
        var secondThread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [firstThread, secondThread]);
        AppModelHarness.WriteDocument(firstThread.Id);
        AppModelHarness.WriteDocument(secondThread.Id);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            using var rig = new DiffPopoverRig(store, app, new ThreadRuntime(firstThread.Id, app), new ThreadRuntime(secondThread.Id, app));
            test(rig);
        });
        GC.KeepAlive(app);
    }

    /// <summary>The coordinator serving the first thread with its popover open on the changes tab.</summary>
    public void OpenOnFirst()
    {
        Coordinator.SetAnchor(Tab);
        Coordinator.Sync(false, First);
        First.IsDiffVisible = true;
        Coordinator.Sync(true, First);
    }

    public void Dispose()
    {
        Coordinator.Close();
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        presenterScope.Dispose();
        GC.KeepAlive(app);
        GC.KeepAlive(store);
    }
}
