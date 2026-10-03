using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Parity closing pass (2026-10-03) for App.Views.Chat.TimelineRows: scenarios on the REAL timeline (ThreadTimeline with a
// real AppModel and runtime, the harness of ThreadTimelineScrollStabilityTests), for the records whose verification
// method is a mounted interaction across the thread: PR-CHAT-080 (the editor survives scrolling and leaving the thread),
// PR-CHAT-125 (the stored chronological setting has no reader) and PR-CHAT-155 (a turn that sent out heads, running then
// folded). Source: DroppyCode/App/Views/Chat/TimelineRows.swift and ThreadTimeline.swift (B3 52bc792).
[Collection("Headless")]
public sealed class TimelineRowsTimelineScenarioTests(HeadlessSession headless)
{
    private static readonly string LongReply = string.Join(" ", Enumerable.Repeat("This answer is long enough to wrap across several lines in the column.", 6));

    [ThreadStatic] private static Marrow.Testing.ManualTimeProvider? clock;

    private sealed record Scene(ThreadTimeline View, ThreadRuntime Runtime, AppModel Model, Border Host, Avalonia.Controls.Window Window,
        ManualFrameSource Frames, RecordingPresenter Presenter);

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    // Finished turns: a question, a long answer and the turn's summary; their records, so the turns can be rewound.
    private static (List<TimelineEntry> Entries, List<TurnRecord> Turns) FinishedTurns(int count)
    {
        var entries = new List<TimelineEntry>();
        var turns = new List<TurnRecord>();
        for (var i = 0; i < count; i++)
        {
            var turn = Guid.NewGuid();
            turns.Add(new TurnRecord { Id = turn, Index = i, Status = TurnStatus.Completed });
            entries.Add(Entry("user-" + i, new TimelineItem.Content.User(new UserMessage { Text = "Question number " + i + " about the layout." }), turn));
            entries.Add(Entry("reply-" + i, new TimelineItem.Content.Assistant(new AssistantMessage { Text = LongReply }), turn));
            entries.Add(Entry("end-" + i, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), turn));
        }
        return (entries, turns);
    }

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void SetTurns(ThreadRuntime runtime, IEnumerable<TurnRecord> turns) =>
        typeof(ThreadRuntime).GetProperty("Turns")!.SetValue(runtime, new ValueList<TurnRecord>(turns));

    private void WithTimeline(bool supportsRewind, Action<AppModel>? beforeView, Action<Scene> check) => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var h = new AppModelHarness();
        clock = h.Main.Time;
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = new AppModel();
        beforeView?.Invoke(model);
        var id = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
        DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult();
        var runtime = new ThreadRuntime(id, model);
        var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, supportsRewind, 480);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show();
            Settle(window, frames);
            check(new Scene(view, runtime, model, host, window, frames, presenter));
        }
        finally
        {
            clock = null;
            runtime.CancelPersistence();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    // Two seconds of frames and clock time, laying out between steps.
    private static void Settle(Avalonia.Controls.Window window, ManualFrameSource frames)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            frames.Advance(TimeSpan.FromMilliseconds(50));
            clock?.Advance(TimeSpan.FromMilliseconds(50));
            window.UpdateLayout();
        }
    }

    private static ScrollViewer Scroller(ThreadTimeline view) => view.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static void Click(Avalonia.Controls.Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Target is not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Popup? ShownEditor(RecordingPresenter presenter)
    {
        // The editor's popup is the last one whose content is a MessageEditor; it is shown when its last call presented it.
        var editor = presenter.Calls.Where(call => call.Popup.Child is MessageEditor).Select(call => call.Popup).LastOrDefault();
        if (editor is null) return null;
        return presenter.Calls.Last(call => ReferenceEquals(call.Popup, editor)).Presented ? editor : null;
    }

    // ---------------------------------------------------------------- PR-CHAT-080

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:84-87, :130-161, :197-198, :208-212; ThreadRuntime messageEdit
    public void The_message_editor_stays_open_when_its_row_scrolls_away_and_back_and_reopens_when_the_thread_comes_back() => WithTimeline(true, null, scene =>
    {
        var (entries, turns) = FinishedTurns(12);
        SetTurns(scene.Runtime, turns);
        SetEntries(scene.Runtime, entries);
        Settle(scene.Window, scene.Frames);
        var last = turns[^1].Id;
        UserMessageRow Row() => scene.View.GetVisualDescendants().OfType<UserMessageRow>()
            .Single(row => row.GetVisualDescendants().OfType<InlineText>().Any(text => text.GetVisualDescendants().OfType<TextBlock>()
                .Any(block => (block.Text ?? block.Inlines?.Text ?? "").Contains("Question number 11", StringComparison.Ordinal))));
        Button Pencil() => Row().GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), "Edit message"));

        // The pointer on the row shows the pencil; a click opens the editor on the thread's runtime, anchored to the pencil.
        Click(scene.Window, Pencil());
        Settle(scene.Window, scene.Frames);
        var edit = Assert.IsType<PromptEdit>(scene.Runtime.MessageEdit);
        Assert.Equal(last, edit.Id);
        Assert.IsType<MessageEditor>(Assert.IsType<Popup>(ShownEditor(scene.Presenter)).Child);

        // Scroll the row away (to the thread's top), then back to the end: the edit is the thread's, the editor is still up.
        var scroller = Scroller(scene.View);
        var end = scroller.Offset;
        Assert.True(end.Y > 400, "the thread must be taller than the pane");
        scroller.Offset = new Vector(0, 0);
        Settle(scene.Window, scene.Frames);
        Assert.True(Row().TranslatePoint(default, scroller)!.Value.Y > scroller.Viewport.Height, "the row did not scroll away");
        scroller.Offset = end;
        Settle(scene.Window, scene.Frames);
        Assert.Same(edit, scene.Runtime.MessageEdit);
        Assert.NotNull(ShownEditor(scene.Presenter));

        // Leaving the thread closes the popover only (:198 onDisappear editor.close()); the edit stays on the runtime.
        scene.Host.Child = null;
        Settle(scene.Window, scene.Frames);
        Assert.Null(ShownEditor(scene.Presenter));
        Assert.Same(edit, scene.Runtime.MessageEdit);
        // Coming back, the row's anchor lands in its window and reopens the editor on the edit as it was left (:148-155).
        scene.Host.Child = scene.View;
        Settle(scene.Window, scene.Frames);
        var reopened = Assert.IsType<Popup>(ShownEditor(scene.Presenter));
        Assert.IsType<MessageEditor>(reopened.Child);
        Assert.Same(edit, scene.Runtime.MessageEdit);
        // A second tap on the pencil closes it (:132-134).
        Click(scene.Window, Pencil());
        Settle(scene.Window, scene.Frames);
        Assert.Null(scene.Runtime.MessageEdit);
        Assert.Null(ShownEditor(scene.Presenter));
    });

    // ---------------------------------------------------------------- PR-CHAT-125 and PR-CHAT-155

    private static TimelineEntry ToolStep(string id, ToolCall.Kind kind, string title, Guid turn) =>
        Entry(id, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = kind, Title = title, ToolCallStatus = ToolCall.Status.Completed }), turn);

    private static T[] Mounted<T>(Control root) where T : class => root.GetVisualDescendants().OfType<T>().ToArray();

    private static Button WithTip(Control root, string tip) => Mounted<Button>(root).Single(button => Equals(ToolTip.GetTip(button), tip));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-CHAT-125")] // ThreadTimeline.swift:325-331, :2091-2092, :2155, :2185-2188; TimelineRows.swift:1523-1536, :2499-2509; AppSettings.swift:128, :237-239, :719
    public void Whatever_chronologicalTimeline_holds_a_running_turn_opens_collapsed_and_a_finished_turn_opens_folded(bool stored) =>
        WithTimeline(false, model => model.Settings.ChronologicalTimeline = stored, scene =>
        {
            Assert.Equal(stored, scene.Model.Settings.ChronologicalTimeline); // the stored value is kept, never read by the rows
            var done = Guid.NewGuid();
            var finished = new List<TimelineEntry>
            {
                Entry("u0", new TimelineItem.Content.User(new UserMessage { Text = "First question" }), done),
                ToolStep("t0a", ToolCall.Kind.Read, "Read a", done),
                ToolStep("t0b", ToolCall.Kind.Read, "Read b", done),
                ToolStep("t0c", ToolCall.Kind.Read, "Read c", done),
                Entry("r0", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "The finished answer." }), done),
                Entry("e0", new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = done, Status = TurnStatus.Completed, Duration = 3, FilesChanged = 0, Additions = 0, Deletions = 0 }), done),
            };
            SetTurns(scene.Runtime, [new TurnRecord { Id = done, Index = 0, Status = TurnStatus.Completed }]);
            SetEntries(scene.Runtime, finished);
            Settle(scene.Window, scene.Frames);
            scene.Runtime.RehearseTurn("Second question");
            var running = scene.Runtime.Turns[^1].Id;
            SetEntries(scene.Runtime, scene.Runtime.Entries.Concat(
            [
                ToolStep("t1a", ToolCall.Kind.Read, "Read one", running),
                ToolStep("t1b", ToolCall.Kind.Read, "Read two", running),
                ToolStep("t1c", ToolCall.Kind.Read, "Read three", running),
            ]));
            Settle(scene.Window, scene.Frames);

            // :2499-2509 the finished turn opens folded on its answer: no steps, the chevron offers them.
            var block = Assert.Single(Mounted<TurnFinishedBlock>(scene.View));
            Assert.Empty(Mounted<ToolRow>(block));
            Assert.Single(Mounted<FoldedAnswerView>(block));
            WithTip(block, "Show this turn's steps");
            // :2091-2092, :2155 the running card opens collapsed: no thinking rows, no work steps.
            var card = Assert.Single(Mounted<WorkingIndicator>(scene.View));
            Assert.Empty(Mounted<WorkSteps>(card));
            Assert.Empty(Mounted<ToolRow>(card));
            // Opened, it shows only the two latest work steps under the earlier-steps line (TimelineRows.swift:1523-1536).
            Click(scene.Window, WithTip(card, "Show these steps"));
            Settle(scene.Window, scene.Frames);
            var steps = Assert.Single(Mounted<WorkSteps>(card));
            Assert.Equal(2, Mounted<ToolRow>(steps).Length);
            Assert.Contains(Mounted<TextBlock>(steps), text => text.Text == "1 earlier step");
        });

    [Fact]
    [Trait("Req", "PR-CHAT-155")] // TimelineRows.swift:1564-1577, :2580-2584, :2652-2654, :2682-2686; ThreadTimeline.swift:1955-1958, :1985-1989
    public void The_heads_a_turn_sent_out_stay_in_view_under_it_while_it_runs_and_folded_or_open_once_it_ends() => WithTimeline(false, null, scene =>
    {
        scene.Runtime.RehearseTurn("Send out the heads");
        var turn = scene.Runtime.Turns[^1].Id;
        SetEntries(scene.Runtime, scene.Runtime.Entries.Concat(
        [
            ToolStep("read", ToolCall.Kind.Read, "Read the brief", turn),
            ToolStep("hank", ToolCall.Kind.Agent, "Hank: inspect the layout", turn),
            ToolStep("walter", ToolCall.Kind.Agent, "Walter: check the tests", turn),
            Entry("reply", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Both heads are out." }), turn),
        ]));
        Settle(scene.Window, scene.Frames);
        string[] expected = ["Sent out Hank: inspect the layout", "Sent out Walter: check the tests"];
        string[] Names(HydraHeadsList list) => Mounted<MiddleTruncatingTextBlock>(list).Select(text => text.FullText).ToArray();

        // Running (ThreadTimeline.swift:1955-1958): the heads come after the turn's other rows and above the working line.
        var runningBlock = Assert.Single(Mounted<TurnRunningBlock>(scene.View));
        var heads = Assert.Single(Mounted<HydraHeadsList>(runningBlock));
        Assert.Equal(expected, Names(heads));
        var working = Assert.Single(Mounted<WorkingBlockView>(runningBlock));
        Assert.True(Top(heads, scene.Window) + heads.Bounds.Height <= Top(working, scene.Window) + 0.5, "the heads sit above the working line");
        Assert.Empty(Mounted<HydraHeadsList>(working));

        // The turn ends and folds: the heads stay, under the answer, a plain list 8 apart with no header or chevron.
        scene.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        Settle(scene.Window, scene.Frames);
        var block = Assert.Single(Mounted<TurnFinishedBlock>(scene.View));
        var folded = Assert.Single(Mounted<HydraHeadsList>(block));
        Assert.Equal(expected, Names(folded));
        Assert.Equal(8, Assert.IsType<StackPanel>(folded.Child).Spacing);
        Assert.DoesNotContain(Mounted<SwiftImage>(folded), image => image.SymbolName == "chevron.right");
        var answer = Assert.Single(Mounted<FoldedAnswerView>(block));
        Assert.True(Top(folded, scene.Window) >= Top(answer, scene.Window) + answer.Bounds.Height, "the heads sit under the answer");

        // Opened, the steps card holds the read but never the heads; the heads list is still under it (:2652-2654, :2682-2686).
        Click(scene.Window, WithTip(block, "Show this turn's steps"));
        Settle(scene.Window, scene.Frames);
        var open = Assert.Single(Mounted<HydraHeadsList>(block));
        var stepsCard = Mounted<Border>(block).Single(border => border.Padding == new Thickness(14));
        Assert.Empty(Mounted<HydraHeadsList>(stepsCard));
        Assert.DoesNotContain(Mounted<MiddleTruncatingTextBlock>(stepsCard), text => text.FullText.StartsWith("Sent out", StringComparison.Ordinal));
        Assert.True(Top(open, scene.Window) >= Top(stepsCard, scene.Window) + stepsCard.Bounds.Height, "the heads sit under the steps");
        Assert.Equal(expected, Names(open));

        // Folded again, still there.
        Click(scene.Window, WithTip(block, "Hide this turn's steps"));
        Settle(scene.Window, scene.Frames);
        Assert.Equal(expected, Names(Assert.Single(Mounted<HydraHeadsList>(block))));
    });
}
