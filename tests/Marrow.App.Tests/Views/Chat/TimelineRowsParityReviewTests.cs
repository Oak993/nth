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

// REVIEW (adversarial round 1 of the TimelineRows parity pass, 2026-10-03). Scenarios on the REAL timeline (the harness of
// TimelineRowsTimelineScenarioTests): ThreadTimeline.Draw takes every kept row out of its old stack and puts it into a new
// one on every change of runtime.Entries / IsRunning (ThreadTimeline.cs:137-149), so each kept row leaves the visual tree
// and comes back. In SwiftUI the rows keep their identity across those changes (ThreadTimeline.swift:388-391
// ForEach(visible) in an eager VStack whose .id(stackGeneration) changes only on the blank-viewport repairs at :787, :887),
// so a row's @State, and a popover that state holds, survive another row arriving.
[Collection("Headless")]
public sealed class TimelineRowsParityReviewTests(HeadlessSession headless)
{
    [ThreadStatic] private static Marrow.Testing.ManualTimeProvider? clock;

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void SetTurns(ThreadRuntime runtime, IEnumerable<TurnRecord> turns) =>
        typeof(ThreadRuntime).GetProperty("Turns")!.SetValue(runtime, new ValueList<TurnRecord>(turns));

    private void WithTimeline(Action<TimelineRowsScene> check) => headless.OnUiThread(() =>
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
        var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, false, 480);
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

    private static void Press(Avalonia.Controls.Window window, Control target, MouseButton button)
    {
        var point = target.TranslatePoint(new Point(Math.Min(6, target.Bounds.Width / 2), Math.Min(6, target.Bounds.Height / 2)), window)
            ?? throw new InvalidOperationException("Target is not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, button, RawInputModifiers.None);
        window.MouseUp(point, button, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsShown(RecordingPresenter presenter, Popup popup) =>
        presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented;

    // MessagePopoverMenu.swift:4-18: the menu's anchor is the row's own @State; a row that stays in the timeline keeps it,
    // so its open menu stays open while the running turn below it adds a step (the 1.7.0 timeline is an eager VStack,
    // ThreadTimeline.swift:384-391, so a row never leaves it by scrolling either). The parity pass's MessagePopoverMenu fix
    // (DetachedFromVisualTree closes the menu, d88d442ed) closes it at the next Draw instead: with that handler disabled
    // this test passes (checked by mutation 2026-10-03), so the fix itself brought the regression.
    [Fact]
    [Trait("Req", "PR-CHAT-087")] // MessagePopoverMenu.swift:4-18
    [Trait("Req", "PR-CHAT-089")] // TimelineRows.swift:1415-1439; ThreadTimeline.swift:384-391
    public void REVIEW_An_open_reply_menu_stays_open_while_the_running_turn_below_it_adds_a_step() => WithTimeline(scene =>
    {
        var done = Guid.NewGuid();
        SetTurns(scene.Runtime, [new TurnRecord { Id = done, Index = 0, Status = TurnStatus.Completed }]);
        SetEntries(scene.Runtime,
        [
            Entry("u0", new TimelineItem.Content.User(new UserMessage { Text = "First question" }), done),
            Entry("r0", new TimelineItem.Content.Assistant(new AssistantMessage { Text = "The finished answer to copy." }), done),
            Entry("e0", new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = done, Status = TurnStatus.Completed, Duration = 3, FilesChanged = 0, Additions = 0, Deletions = 0 }), done),
        ]);
        Settle(scene.Window, scene.Frames);
        scene.Runtime.RehearseTurn("Second question");
        Settle(scene.Window, scene.Frames);
        var running = scene.Runtime.Turns[^1].Id;

        var answer = Assert.Single(scene.View.GetVisualDescendants().OfType<FoldedAnswerView>());
        Press(scene.Window, Assert.Single(answer.GetVisualDescendants().OfType<MarkdownView>()), MouseButton.Right);
        var menu = scene.Presenter.Calls.Last(call => call.Presented && call.Popup.Child is PopoverMenu).Popup;
        Assert.True(IsShown(scene.Presenter, menu));

        // The running turn reads a file: one more entry, the folded turn above is untouched.
        SetEntries(scene.Runtime, scene.Runtime.Entries.Append(Entry("t1", new TimelineItem.Content.Tool(new ToolCall
            { ToolCallKind = ToolCall.Kind.Read, Title = "Read a", ToolCallStatus = ToolCall.Status.Completed }), running)));
        Settle(scene.Window, scene.Frames);
        Assert.Same(answer, Assert.Single(scene.View.GetVisualDescendants().OfType<FoldedAnswerView>())); // the row was kept
        Assert.True(IsShown(scene.Presenter, menu), "the open menu closed when another row arrived below it");
    });

    // TimelineRows.swift:2257-2316 PlanCard and ChatBadge.swift:20 (@State coordinator): the parity pass's ChatBadge.Update
    // keeps the badge across the plan's own updates in a standalone card (TimelineRowsParityScenarioTests). On the real
    // timeline a plan drafts inside a running turn whose other rows keep arriving; the card keeps its identity there in
    // SwiftUI, so the plan popover the reader opened stays open (PR-CHAT-113: "The popover is open while the markdown
    // arrives") until the reader closes it. Observed 2026-10-03 on unit/parity-timelinerows ec533f9a7: the plan's own
    // deltas keep the popover (ChatBadge.Update), but the first other row of the running turn rebuilds the turn's block and
    // with it a new PlanCard, and the open popover goes (the same block-identity gap the pass recorded for PR-CHAT-149).
    [Fact]
    [Trait("Req", "PR-CHAT-113")] // TimelineRows.swift:2257-2316, :2287; ChatBadge.swift:20, :67-75; ThreadTimeline.swift:388-391
    public void REVIEW_An_open_plan_popover_stays_open_on_the_real_timeline_while_the_turn_keeps_streaming() => WithTimeline(scene =>
    {
        _ = AppCurrentEvent.CurrentWindow; // the badge's window anchor reads the current event's window
        scene.Runtime.RehearseTurn("Plan the work");
        scene.Runtime.Rehearse(new ProviderEvent.PlanDelta("plan-1", "First paragraph of the plan."));
        Settle(scene.Window, scene.Frames);
        var card = Assert.Single(scene.View.GetVisualDescendants().OfType<PlanCard>());
        var badge = card.GetVisualDescendants().OfType<Button>().First();
        Assert.True(badge.IsEffectivelyEnabled);
        Press(scene.Window, badge, MouseButton.Left);
        Settle(scene.Window, scene.Frames);
        var popup = scene.Presenter.Calls.Last(call => call.Presented).Popup;
        Assert.NotEmpty(LogicalQuery.SelfAndDescendants<MarkdownView>(popup.Child!));

        // More of the plan streams in.
        scene.Runtime.Rehearse(new ProviderEvent.PlanDelta("plan-1", "\n\nSecond paragraph."));
        Settle(scene.Window, scene.Frames);
        Assert.True(IsShown(scene.Presenter, popup), "the open plan popover closed when the plan streamed on");

        // The agent says a line under the plan: a new entry in the same running turn.
        scene.Runtime.Rehearse(new ProviderEvent.MessageDelta("msg-1", "Here is the plan."));
        Settle(scene.Window, scene.Frames);
        Assert.True(IsShown(scene.Presenter, popup), "the open plan popover closed when another row arrived in its turn");
    });

    // PR-CHAT-166 windows_path: "a text surface that always takes its own hit test, a pointing-hand cursor over links and an
    // I-beam over prose". The reference runtime shows the I-beam over selectable timeline text: 2-chat-use.mov 14.74 s
    // (CATALOG.md segment 15, "Pointer becomes an I-beam over message text"), the pointer resting on the error notice's
    // message (TimelineRows.swift:2394 .textSelection(.enabled)). The reply's prose run (LinkTextView) shows it; the
    // notice's message, the sent bubble's words (:123, VeiledText since the parity pass) and a code block's text
    // (MarkdownView.swift:1229) are selectable SelectableTextBlocks with no cursor of their own, so the arrow stays.
    [Fact]
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:123, :2394; MarkdownView.swift:1131, :1229
    public void REVIEW_The_pointer_is_an_I_beam_over_selectable_notice_bubble_and_code_text_as_over_a_replys_prose() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Avalonia.Controls.Window window = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                var notice = new NoticeRow(TimelineRowsPrSupport.NoticeEntry(Notice.Level.Error, "No message found with message.uuid of: 17366422"));
                var bubble = new UserMessageRow(TimelineRowsPrSupport.UserEntry("plain words in a bubble", Guid.NewGuid()), h.Runtime, canRevert: false);
                var reply = new AssistantMessageRow(TimelineRowsPrSupport.Reply("Prose of the answer, long enough to hover.\n\n```\nlet code = 1\n```"), null, h.Runtime);
                window = TimelineRowsPrSupport.Show(new StackPanel { Spacing = 40, Children = { notice, bubble, reply } });

                string? CursorOver(Control text)
                {
                    TimelineRowsPrSupport.Hover(window, text);
                    return text.Cursor?.ToString();
                }
                // The reply's prose: the I-beam (LinkTextView.OnPointerMoved).
                Assert.Equal("Ibeam", CursorOver(Assert.Single(reply.GetVisualDescendants().OfType<ParagraphTextPresenter>())));
                var cursors = new[]
                {
                    ("notice", CursorOver(Assert.Single(notice.GetVisualDescendants().OfType<SelectableTextBlock>()))),
                    ("bubble", CursorOver(Assert.Single(bubble.GetVisualDescendants().OfType<SelectableTextBlock>()))),
                    ("code", CursorOver(Assert.Single(Assert.Single(reply.GetVisualDescendants().OfType<CodeBlock>()).GetVisualDescendants().OfType<SelectableTextBlock>()))),
                };
                Assert.All(cursors, pair => Assert.True(pair.Item2 == "Ibeam", $"{pair.Item1}: {pair.Item2 ?? "arrow (no cursor)"}"));
            });
        }
        finally { if (window is not null) h.Main.OnMain(() => TimelineRowsPrSupport.Close(window)); }
    });
}
