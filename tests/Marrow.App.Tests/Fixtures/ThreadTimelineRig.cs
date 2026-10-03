using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// A real ThreadTimeline in a shown window, on a real ThreadRuntime, driven on the manual frame clock and the harness's
// manual time (ThreadTimelineTests.WithTimeline and ThreadTimelineScrollStabilityTests.WithLongThread are the models).
// The mounted proofs of PR-CHAT-021/022/026/027/034/035/045/046, UI-CHAT-001/003/043/060 use it, so every clause is read
// off the live view: the geometry of the column, the rows in it, the rail beside it, the working line at its end.
internal sealed class TimelineMount
{
    internal required AppModelHarness Store { get; init; }
    internal required AppModel Model { get; init; }
    internal required ThreadRuntime Runtime { get; init; }
    internal required ThreadTimeline View { get; init; }
    internal required Border Host { get; init; }
    internal required Avalonia.Controls.Window Window { get; init; }
    internal required ManualFrameSource Frames { get; init; }
    internal required ChromeScrollModel Chrome { get; init; }
    internal required TimelineScrollState State { get; init; }

    internal ScrollViewer Scroll => View.GetVisualDescendants().OfType<ScrollViewer>().First();

    internal ScrollViewer? ScrollOrNull => View.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>Frames and clock time together, laying out between steps, as real time moves both.</summary>
    internal void Advance(int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            var step = Math.Min(50, milliseconds - elapsed);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Frames.Advance(TimeSpan.FromMilliseconds(step));
            Store.Main.Advance(TimeSpan.FromMilliseconds(step));
            Window.UpdateLayout();
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    /// <summary>Everything the timeline does in its first two seconds: the 250 ms history task, the 1.5 s hold, the layout settling.</summary>
    internal void Settle() => Advance(2000);

    internal void SetEntries(IEnumerable<TimelineEntry> entries)
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(Runtime, new ValueList<TimelineEntry>(entries));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    internal void SetPhase(RuntimePhase phase)
    {
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(Runtime, phase);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    internal void SetTurnStartedAt(DateTimeOffset? at) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.TurnStartedAt))!.SetValue(Runtime, at);

    /// <summary>The block rows laid out in the scroll view, top to bottom.</summary>
    internal DisplayBlockView[] Rows => Scroll.GetVisualDescendants().OfType<DisplayBlockView>().OrderBy(row => row.TranslatePoint(default, Scroll)!.Value.Y).ToArray();

    /// <summary>The scroll view's content (the column), whose coordinates the layout clauses are read in.</summary>
    internal Control Column => (Control)Scroll.Content!;

    /// <summary>The blocks the timeline has laid out, top to bottom: each one's identity, the block it was built from and the row view it shows.</summary>
    internal (string Id, DisplayBlock Block, Control Row)[] Blocks()
    {
        var laid = Field<Dictionary<string, Control>>("laidOutRows");
        var views = (System.Collections.IDictionary)typeof(ThreadTimeline).GetField("rowViews", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(View)!;
        return laid.Select(pair => (pair.Key, (DisplayBlock)views[pair.Key]!.GetType().GetField("Item1")!.GetValue(views[pair.Key])!, pair.Value)).ToArray();
    }

    internal T Field<T>(string name) => (T)typeof(ThreadTimeline).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(View)!;

    internal static TimelineEntry Entry(string id, TimelineItem.Content content, Guid? turn = null) => new(new TimelineItem(turn, content, id));

    internal static TimelineEntry User(string id, Guid? turn = null, string text = "prompt") => Entry(id, new TimelineItem.Content.User(new UserMessage { Text = text }), turn);

    internal static TimelineEntry Reply(string id, Guid? turn = null, string text = "reply") => Entry(id, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }), turn);

    internal static TimelineEntry ToolEntry(string id, Guid? turn = null, bool head = false) =>
        Entry(id, new TimelineItem.Content.Tool(new ToolCall { Title = id, ToolCallKind = head ? ToolCall.Kind.Agent : ToolCall.Kind.Command }), turn);

    internal static TimelineEntry End(string id, Guid turn) => Entry(id, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), turn);

    /// <summary>Finished turns of a question, a reply of several lines and the turn's end marker: one block each, taller than a line.</summary>
    internal static IEnumerable<TimelineEntry> Turns(int count, string prefix = "", int sentences = 6, bool unique = false)
    {
        var reply = string.Join(" ", Enumerable.Repeat("This answer is long enough to wrap across several lines in the column.", sentences));
        for (var i = 0; i < count; i++)
        {
            var turn = Guid.NewGuid();
            yield return User(prefix + "user-" + i, turn, "Question number " + i + " about the conversation layout.");
            yield return Reply(prefix + "reply-" + i, turn, unique ? reply + " " + Guid.NewGuid() : reply);
            yield return End(prefix + "end-" + i, turn);
        }
    }
}

internal static class ThreadTimelineRig
{
    /// <summary>The timeline on an app model with one empty thread. <paramref name="showsMinimap"/> and <paramref name="columnHeight"/> are the caller's (ChatView's) inputs.</summary>
    internal static void Run(HeadlessSession headless, Action<TimelineMount> check, double width = 960, double height = 480, bool showsMinimap = true, double? columnHeight = null) =>
        headless.OnUiThread(() =>
        {
            // LoginEnvironment.Load is completed once on a pool thread first, so a later Load().Wait() on the headless UI thread returns at once.
            Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var store = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var model = new Marrow.App.AppModel();
            var id = Guid.NewGuid();
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
            DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult();
            var runtime = new ThreadRuntime(id, model);
            Mount(store, model, runtime, frames, check, width, height, showsMinimap, columnHeight ?? height);
        });

    /// <summary>The timeline on a lead thread with head threads under it (TimelineRowsFinTeam), for the finished-head pills.</summary>
    internal static void RunWithTeam(HeadlessSession headless, Action<TimelineMount> check, HydraHeadInfo[] heads, double width = 960, double height = 480) =>
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (store, model, runtime) = TimelineRowsFinTeam.Make(heads);
            using var owned = store;
            AwaitHistory(store, runtime);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            Mount(store, model, runtime, frames, check, width, height, true, height);
        });

    /// <summary>
    /// A runtime made on a stored thread reads its history from disk on a pool thread and takes it in through the main loop, which the
    /// harness's manual time cannot hurry: wait for that in real time, so a machine under load never leaves the loading mark (ThreadTimeline.swift:134-135)
    /// where the rows of a test belong.
    /// </summary>
    internal static void AwaitHistory(AppModelHarness store, ThreadRuntime runtime)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (runtime.IsLoadingHistory)
        {
            Assert.True(waited.Elapsed < AppModelHarness.Bound, "the thread's history was not taken in");
            store.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (runtime.IsLoadingHistory) System.Threading.Thread.Sleep(5);
        }
    }

    private static void Mount(AppModelHarness store, AppModel model, ThreadRuntime runtime, ManualFrameSource frames, Action<TimelineMount> check, double width, double height, bool showsMinimap, double columnHeight)
    {
        var chrome = new ChromeScrollModel();
        var state = new TimelineScrollState();
        var view = new ThreadTimeline(runtime, chrome, state, "Project", null, false, columnHeight, showsMinimap);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = width, Height = height };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show();
            window.UpdateLayout();
            check(new TimelineMount { Store = store, Model = model, Runtime = runtime, View = view, Host = host, Window = window, Frames = frames, Chrome = chrome, State = state });
        }
        finally
        {
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
