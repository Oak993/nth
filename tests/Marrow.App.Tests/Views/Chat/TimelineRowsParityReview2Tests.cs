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
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial round 2 of the TimelineRows parity pass, 2026-10-03). Round 1 found that an open popover goes when the
// real timeline redraws (ThreadTimeline.Draw takes every kept row out of its old stack and puts it into a new one on each
// change of runtime.Entries / IsRunning). The fix gave ChatBadge (ChatBadge.swift:74-75) and the row menu the reading
// "gone only if still out of the tree once the pass is over"; the same file's other popover, the badgePopover modifier
// (ChatBadge.swift:93-111, `.onDisappear { coordinator.close() }` at :109), kept the old one (ChatBadge.cs
// ChatBadgeExtensions.BadgePopover: `host.DetachedFromVisualTree += (_, _) => coordinator.Close()`). Three timeline pills use it:
// HydraReportRow's report (TimelineRows.swift:293), the heads pill (:470) and HydraBriefRow's brief (:583). In SwiftUI a row
// that stays in the timeline keeps its @State and its popover (ThreadTimeline.swift:388-391 ForEach in an eager VStack).
[Collection("Headless")]
public sealed class TimelineRowsParityReview2Tests(HeadlessSession headless)
{
    [ThreadStatic] private static Marrow.Testing.ManualTimeProvider? clock;

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void SetTurns(ThreadRuntime runtime, IEnumerable<TurnRecord> turns) =>
        typeof(ThreadRuntime).GetProperty("Turns")!.SetValue(runtime, new ValueList<TurnRecord>(turns));

    private void WithTimeline(Action<TimelineRowsScene> check) => WithTimeline(false, check);

    private void WithTimeline(bool supportsRewind, Action<TimelineRowsScene> check) => headless.OnUiThread(() =>
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
            check(new TimelineRowsScene(view, runtime, model, host, window, frames, presenter));
        }
        finally
        {
            clock = null;
            runtime.CancelPersistence();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

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

    private static void Press(Avalonia.Controls.Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(Math.Min(6, target.Bounds.Width / 2), Math.Min(6, target.Bounds.Height / 2)), window)
            ?? throw new InvalidOperationException("Target is not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsShown(RecordingPresenter presenter, Popup popup) =>
        presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented;

    // A note from Hydra (TimelineRows.swift:79-80 UserMessageRow -> HydraReportRow, isFromHydra) sits in the finished turn above
    // the running one; the reader opens its report (:259-261, .badgePopover at :293). The running turn then reads a file: one
    // more entry below, the note's row is kept (the same control), and its open report must stay open.
    [Fact]
    [Trait("Req", "PR-CHAT-143")] // TimelineRows.swift:241-329, :293; ChatBadge.swift:93-111; ThreadTimeline.swift:384-391
    public void REVIEW_An_open_Hydra_note_report_stays_open_while_the_running_turn_below_it_adds_a_step() => WithTimeline(scene =>
    {
        _ = AppCurrentEvent.CurrentWindow; // the pill's window anchor reads the current event's window
        var done = Guid.NewGuid();
        SetTurns(scene.Runtime, [new TurnRecord { Id = done, Index = 0, Status = TurnStatus.Completed }]);
        SetEntries(scene.Runtime,
        [
            Entry("u0", new TimelineItem.Content.User(new UserMessage { Text = "Held the heads back.\nThe lead is busy with the layout.", HydraHeads = ValueList<long>.Empty }), done),
            Entry("r0", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Noted." }), done),
            Entry("e0", new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = done, Status = TurnStatus.Completed, Duration = 3, FilesChanged = 0, Additions = 0, Deletions = 0 }), done),
        ]);
        Settle(scene.Window, scene.Frames);
        scene.Runtime.RehearseTurn("Second question");
        Settle(scene.Window, scene.Frames);
        var running = scene.Runtime.Turns[^1].Id;

        var note = Assert.Single(scene.View.GetVisualDescendants().OfType<HydraReportRow>());
        Press(scene.Window, Assert.Single(note.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Show the message")));
        Settle(scene.Window, scene.Frames);
        var report = scene.Presenter.Calls.Last(call => call.Presented).Popup;
        Assert.True(IsShown(scene.Presenter, report));
        var content = report.Child;
        Assert.NotNull(content);
        var calls = scene.Presenter.Calls.Count;

        // The running turn reads a file: one more entry, the finished turn above is untouched.
        SetEntries(scene.Runtime, scene.Runtime.Entries.Append(Entry("t1", new TimelineItem.Content.Tool(new ToolCall
            { ToolCallKind = ToolCall.Kind.Read, Title = "Read a", ToolCallStatus = ToolCall.Status.Completed }), running)));
        Settle(scene.Window, scene.Frames);
        Assert.Same(note, Assert.Single(scene.View.GetVisualDescendants().OfType<HydraReportRow>())); // the row was kept
        Assert.True(IsShown(scene.Presenter, report), "the open Hydra report closed when another row arrived below it");
        // Nothing closed it in between: no dismissal (NSPopover-style shrink, PopoverAppear) and no report built anew, which
        // would lose the reader's place in it. With a real Popup the dismissal is what the reader sees.
        Assert.DoesNotContain(scene.Presenter.Calls.Skip(calls), call => ReferenceEquals(call.Popup, report) && !call.Presented);
        Assert.Same(content, report.Child);
    });

    // PR-CHAT-080: the message editor closes when its row disappears (TimelineRows.swift:198 `.onDisappear { editor.close() }`,
    // the record's cancellation clause), not when the row merely moves. The thread is idle (the editor needs it), and an entry
    // that is no part of the edited turn arrives (a notice with no turn: the timeline appends what the thread is told while it
    // waits); the edited turn's rows are kept, so in SwiftUI the open editor and its MessageEditor @State (restoresFiles, error,
    // isSending, MessageEditor.swift:26-29) stay as they are. The port's UserMessageRow closes the editor on every
    // DetachedFromVisualTree (TimelineRows.cs, ":198"), and its anchor capture then builds a new MessageEditor on re-attach.
    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:84-87, :148-155, :197-198; MessageEditor.swift:26-29; ThreadTimeline.swift:384-391
    public void REVIEW_An_open_message_editor_stays_open_with_its_own_state_while_an_entry_arrives_below_it() => WithTimeline(true, scene =>
    {
        var turns = new List<TurnRecord>();
        var entries = new List<TimelineEntry>();
        for (var i = 0; i < 2; i++)
        {
            var turn = Guid.NewGuid();
            turns.Add(new TurnRecord { Id = turn, Index = i, Status = TurnStatus.Completed });
            entries.Add(Entry("user-" + i, new TimelineItem.Content.User(new UserMessage { Text = "Question number " + i }), turn));
            entries.Add(Entry("reply-" + i, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Answer number " + i }), turn));
            entries.Add(Entry("end-" + i, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), turn));
        }
        SetTurns(scene.Runtime, turns);
        SetEntries(scene.Runtime, entries);
        Settle(scene.Window, scene.Frames);
        UserMessageRow Row() => scene.View.GetVisualDescendants().OfType<UserMessageRow>()
            .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(block => (block.Text ?? block.Inlines?.Text ?? "").Contains("Question number 1", StringComparison.Ordinal)));
        var row = Row();
        var pencil = row.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), "Edit message"));
        var point = pencil.TranslatePoint(new Point(pencil.Bounds.Width / 2, pencil.Bounds.Height / 2), scene.Window)!.Value;
        scene.Window.MouseMove(point, RawInputModifiers.None);
        scene.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        scene.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Settle(scene.Window, scene.Frames);
        var edit = Assert.IsType<PromptEdit>(scene.Runtime.MessageEdit);
        var editor = scene.Presenter.Calls.Last(call => call.Presented && call.Popup.Child is MessageEditor).Popup;
        var content = Assert.IsType<MessageEditor>(editor.Child);
        var calls = scene.Presenter.Calls.Count;

        // A notice the thread is told while it waits: a block of its own below the turns, the edited turn untouched.
        SetEntries(scene.Runtime, scene.Runtime.Entries.Append(new TimelineEntry(new TimelineItem(null,
            new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "A head finished its work." }), "notice-1"))));
        Settle(scene.Window, scene.Frames);
        Assert.Same(row, Row()); // the row was kept
        Assert.Same(edit, scene.Runtime.MessageEdit);
        Assert.DoesNotContain(scene.Presenter.Calls.Skip(calls), call => ReferenceEquals(call.Popup, editor) && !call.Presented);
        Assert.Same(content, editor.Child); // the same MessageEditor, its @State as the reader left it
    });
}
