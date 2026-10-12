using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Store;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// A thread runtime whose turns carry provider diffs, so the changes inspector has real files to show
// (DroppyCode/App/Runtime/ThreadRuntime.swift:2013-2050 parsedDiff reads each selected turn's provider patch).
// The project is a real, empty git repository made under the harness's scratch folder; nothing outside it is read.
// Helper types live here, never inside a test class.
public sealed class DiffInspectorRig : IDisposable
{
    private readonly List<AvaloniaWindow> windows = [];
    // The frame source a window's controls ask for; manual, so no animation frame runs unasked.
    private readonly IDisposable frames = FrameClock.Source.Override(new ManualFrameSource());

    private DiffInspectorRig(AppModelHarness store, AppModel app, ThreadRuntime runtime, IReadOnlyList<TurnRecord> turns)
    {
        Store = store;
        App = app;
        Runtime = runtime;
        Turns = turns;
    }

    public AppModelHarness Store { get; }
    public AppModel App { get; }
    public ThreadRuntime Runtime { get; }
    public IReadOnlyList<TurnRecord> Turns { get; }

    /// <summary>
    /// A runtime with one finished turn per entry (turn indexes 0, 1, 2...), each carrying that entry as its
    /// provider diff; a null entry is a turn with no changes at all. No entries means no turns.
    /// </summary>
    public static DiffInspectorRig Open(params string?[] turnPatches)
    {
        var store = new AppModelHarness();
        var repository = AppModelGitRepo.Empty(store.NewFolder("diff-project"));
        var project = AppModelSamples.Project("Diff inspector", repository.Root);
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        var turns = turnPatches.Select((patch, index) => new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = index,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            ProviderDiff = patch,
        }).ToList();
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(thread.Id) { Turns = new ValueList<TurnRecord>(turns) }, Storage.ThreadURL(thread.Id));
        var app = store.MakeModel();
        var runtime = store.Main.OnMain(() => app.Runtime(thread.Id));
        store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new DiffInspectorRig(store, app, runtime, turns);
    }

    /// <summary>A unified diff of one file with <paramref name="additions"/> added lines and one removed line.</summary>
    public static string Patch(string path, int additions = 1)
    {
        var text = new StringBuilder();
        text.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
        text.Append("--- a/").Append(path).Append('\n');
        text.Append("+++ b/").Append(path).Append('\n');
        text.Append("@@ -1,1 +1,").Append(additions).Append(" @@\n");
        text.Append("-old\n");
        for (var index = 0; index < additions; index++) text.Append("+new").Append(index).Append('\n');
        return text.ToString();
    }

    /// <summary>Patches of several files in one text.</summary>
    public static string Patches(params string[] paths) => string.Concat(paths.Select(path => Patch(path)));

    /// <summary>A new inspector on the runtime; it has loaded nothing yet.</summary>
    public DiffInspector NewInspector() => Store.Main.OnMain(() => new DiffInspector(Runtime));

    /// <summary>A new inspector in pane mode (web W4), the Changes pane's body; it has loaded nothing yet.</summary>
    public DiffInspector NewPaneInspector() => Store.Main.OnMain(() => new DiffInspector(Runtime, DiffInspectorMode.Pane));

    /// <summary>Puts the inspector in a headless window of its own size, as the popover does (640 by 540, or the pane cell's); this starts its first load.</summary>
    public AvaloniaWindow Present(DiffInspector inspector, double width = 640, double height = 540)
    {
        var window = new AvaloniaWindow { Width = width, Height = height, Content = inspector };
        windows.Add(window);
        Store.Main.OnMain(() =>
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
        });
        return window;
    }

    /// <summary>Runs the load the last key change started: the 180 ms wait the diff may need, then the parse; then lays out.</summary>
    public void FinishLoad(DiffInspector inspector)
    {
        Store.Main.Advance(TimeSpan.FromMilliseconds(180));
        var load = Assert.IsAssignableFrom<TaskHandle>(typeof(DiffInspector)
            .GetField("loadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inspector));
        Assert.True(PumpUntil(() => load.Completion.IsCompleted));
        Store.Main.Loop.RunUntilIdle(); // work the load posted for a later turn, such as the scroll
        Dispatcher.UIThread.RunJobs();
        foreach (var window in windows) window.UpdateLayout();
    }

    /// <summary>Makes the diff revision change, which loads the inspector's diff afresh, and waits for that load.</summary>
    public void Reload(DiffInspector inspector)
    {
        Store.Main.OnMain(Runtime.NoteDiffChanged);
        FinishLoad(inspector);
    }

    /// <summary>
    /// Puts a diff task the runtime will hand out for a finished turn's selection, through the runtime's own cache
    /// (nothing public takes a task), so a test can hold the parse open and see the inspector while it reads.
    /// </summary>
    public void SeedDiff(Task<ValueList<DiffFile>> task, Guid turn) =>
        typeof(ThreadRuntime).GetMethod("CacheDiff", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Runtime, [task, (Guid?)turn]);

    /// <summary>
    /// Pumps the main loop, waiting for work other threads post (the detached parse), until <paramref name="condition"/>
    /// holds; false when ten seconds pass first. The manual clock is never advanced here.
    /// </summary>
    public bool PumpUntil(Func<bool> condition)
    {
        try
        {
            Store.Main.Loop.RunUntil(condition, TimeSpan.FromSeconds(10));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        Dispatcher.UIThread.RunJobs();
        frames.Dispose();
        GC.KeepAlive(App);
        Store.Dispose();
    }
}
