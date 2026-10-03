using System.Reflection;
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

// REVIEW (adversarial round 3 of the TimelineRows parity pass, 2026-10-03). Round 2 found that an entry arriving below an open
// message editor closes it and builds a new MessageEditor (its @State restoresFiles, error and isSending reset; MessageEditor.swift
// :26-29), where SwiftUI keeps the row and its editor (TimelineRows.swift:198 .onDisappear fires only when the row leaves the
// hierarchy; ThreadTimeline.swift:384-391 eager VStack). The fix posts UserMessageRow's close until after the redraw pass, and the
// round-2 REVIEW test now passes, but on the RECORDING presenter, which never opens a Popup. The editor's popover is a real Popup
// whose PlacementTarget is the row's own AttachmentAnchorCapture (PromptEditorPopover.Show, target = the anchor view), and the unit
// itself measured that a real Popup closes itself inside the Remove call that takes its placement target out of the tree (STOP #11,
// TimelineRowsTests; the "Same defect class" note of the round-2 fix report says a deferred check "would not show on a real
// timeline" for a popover anchored to a view in the row). ED-NEW-parity-timelinerows (1) names only the row menu as affected and
// lists the editor's survival under the overlay presenter as owed on the native app; the overlay presenter runs headless here
// (TimelineRowsPrPopupPresenter, as TimelineRowsParityScenarioTests use it). Measured with this harness by the reviewer
// (2026-10-03, a probe not committed): a Hydra note's report, whose popover hangs from the window, stays open with the same content
// while the same notice arrives; the message editor's popover closes once and opens again on a new MessageEditor.
[Collection("Headless")]
public sealed class TimelineRowsParityReview3Tests(HeadlessSession headless)
{
    [ThreadStatic] private static Marrow.Testing.ManualTimeProvider? clock;

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void SetTurns(ThreadRuntime runtime, IEnumerable<TurnRecord> turns) =>
        typeof(ThreadRuntime).GetProperty("Turns")!.SetValue(runtime, new ValueList<TurnRecord>(turns));

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

    // The real timeline (as TimelineRowsParityReview2Tests) in a window with the overlay layers, every popover opened for real in it.
    private void WithOverlayTimeline(Action<ThreadTimeline, ThreadRuntime, Avalonia.Controls.Window, ManualFrameSource, TimelineRowsPrPopupPresenter> check) => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var presenter = new TimelineRowsPrPopupPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var h = new AppModelHarness();
        clock = h.Main.Time;
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = new AppModel();
        var id = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
        DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult();
        var runtime = new ThreadRuntime(id, model);
        var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, true, 480);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var layers = new VisualLayerManager { EnableOverlayLayer = true, Child = host };
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(layers, true);
        var window = new Avalonia.Controls.Window { Content = layers, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show();
            Settle(window, frames);
            check(view, runtime, window, frames, presenter);
        }
        finally
        {
            clock = null;
            runtime.CancelPersistence();
            window.Content = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    // PR-CHAT-080 (cancellation: "The editor closes when the row disappears"): the same scenario as the round-2 REVIEW test, with the
    // editor's popover opened for real. The thread is idle, the reader opens the editor on the second message, and a notice the
    // thread is told while it waits arrives below; the edited turn's rows are kept (the same UserMessageRow), so the open editor and
    // its MessageEditor must stay as they are.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (its cancellation clause on the real timeline): TimelineRows.swift:148-155, :197-198; MessageEditor.swift:26-29; ThreadTimeline.swift:384-391
    public void REVIEW_An_open_message_editor_popover_stays_open_with_its_own_state_on_the_overlay_presenter_while_an_entry_arrives_below_it() => WithOverlayTimeline((view, runtime, window, frames, presenter) =>
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
        SetTurns(runtime, turns);
        SetEntries(runtime, entries);
        Settle(window, frames);
        UserMessageRow Row() => view.GetVisualDescendants().OfType<UserMessageRow>()
            .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(block => (block.Text ?? block.Inlines?.Text ?? "").Contains("Question number 1", StringComparison.Ordinal)));
        var row = Row();
        var pencil = row.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), "Edit message"));
        var point = pencil.TranslatePoint(new Point(pencil.Bounds.Width / 2, pencil.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Settle(window, frames);
        var edit = Assert.IsType<PromptEdit>(runtime.MessageEdit);
        var editor = presenter.Calls.Last(call => call.Presented && call.Popup.Child is MessageEditor).Popup;
        Assert.True(editor.IsOpen);
        var content = Assert.IsType<MessageEditor>(editor.Child);
        var closings = 0;
        editor.Closed += (_, _) => closings++;

        // A notice the thread is told while it waits: a block of its own below the turns, the edited turn untouched.
        SetEntries(runtime, runtime.Entries.Append(new TimelineEntry(new TimelineItem(null,
            new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "A head finished its work." }), "notice-1"))));
        Settle(window, frames);
        Assert.Same(row, Row()); // the row was kept: it never disappeared
        Assert.Same(edit, runtime.MessageEdit);
        Assert.True(closings == 0, $"the editor's popover closed {closings} time(s) when an entry arrived below its row");
        Assert.True(ReferenceEquals(content, editor.Child), "a new MessageEditor was built: its @State (restoresFiles, error, isSending) started over");
    });
}
