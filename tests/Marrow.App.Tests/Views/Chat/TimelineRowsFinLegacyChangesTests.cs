using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Testing;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2707-2714 the finished turn's task: a turn recorded
// before per-file summaries existed has its changes worked out once and stored back (:2595 the card reads them).
[Collection("Headless")]
public sealed class TimelineRowsFinLegacyChangesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-107")] // TimelineRows.swift:2707-2714, :2595; ThreadRuntime.swift:3751-3757
    public void A_turn_without_per_file_changes_has_them_worked_out_once_stored_on_the_thread_and_into_its_document_and_a_turn_with_nothing_to_show_never_does() => headless.OnUiThread(() =>
    {
        Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var store = new AppModelHarness();
        var project = AppModelSamples.Project("Legacy", store.NewFolder("legacy turn"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        var withFiles = Guid.NewGuid();
        var withEdits = Guid.NewGuid();
        var withNone = Guid.NewGuid();
        var withChanges = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>(
                [
                    new TurnRecord { Id = withFiles, Index = 0, Status = TurnStatus.Completed },
                    new TurnRecord { Id = withEdits, Index = 1, Status = TurnStatus.Completed },
                    new TurnRecord { Id = withNone, Index = 2, Status = TurnStatus.Completed },
                    new TurnRecord { Id = withChanges, Index = 3, Status = TurnStatus.Completed },
                ]),
                Items = new ValueList<TimelineItem>(
                [
                    new TimelineItem(withFiles, new TimelineItem.Content.User(new UserMessage { Text = "first" })),
                    new TimelineItem(withEdits, new TimelineItem.Content.User(new UserMessage { Text = "second" })),
                    new TimelineItem(withNone, new TimelineItem.Content.User(new UserMessage { Text = "third" })),
                    new TimelineItem(withChanges, new TimelineItem.Content.User(new UserMessage { Text = "fourth" })),
                ]),
            },
            Storage.ThreadURL(thread.Id));
        var model = store.MakeModel();
        store.PrepareBootstrap(model);
        var runtime = store.Main.OnMain(() => model.Runtime(thread.Id));
        store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        FileChangeSummary? Stored(Guid turn)
        {
            FileChangeSummary? Read() => Assert.IsType<TimelineItem.Content.TurnEnd>(runtime.Entries.Single(entry => entry.TurnID == turn && entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd).Item.TimelineItemContent).Value.Changes;
            return MainActor.IsIsolated ? Read() : store.Main.OnMain(Read);
        }
        AvaloniaWindow window = null!;
        var known = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("kept.swift", 1, 0, false)]), 1, 0);
        store.Main.OnMain(() =>
        {
            Assert.Null(Stored(withFiles));
            var command = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"))]);
            var edits = new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit, ToolCall.Status.Completed, new ValueList<FileEdit>([new FileEdit("a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]))]);
            var files = new TurnFinishedBlock(runtime, withFiles, Summary(withFiles) with { FilesChanged = 2 }, ValueList<TimelineEntry>.Empty, command, null, canUndo: false, chronological: false);
            var edited = new TurnFinishedBlock(runtime, withEdits, Summary(withEdits), ValueList<TimelineEntry>.Empty, edits, null, canUndo: false, chronological: false);
            var none = new TurnFinishedBlock(runtime, withNone, Summary(withNone), ValueList<TimelineEntry>.Empty, command, null, canUndo: false, chronological: false);
            // A summary that already carries its per-file changes is never recomputed, whatever the file count says.
            var current = new TurnFinishedBlock(runtime, withChanges, Summary(withChanges) with { FilesChanged = 1, Changes = known }, ValueList<TimelineEntry>.Empty, command, null, canUndo: false, chronological: false);
            var panel = new StackPanel();
            foreach (var block in new Control[] { files, edited, none, current }) panel.Children.Add(block);
            window = Show(panel);
        });
        // :2708-2713 the diff is parsed once, off the main thread, and the summary written back to the turn's end.
        store.Main.Loop.RunUntil(() => Stored(withFiles) is not null && Stored(withEdits) is not null, AppModelHarness.Bound);
        store.Main.RunUntilIdle();
        store.Main.OnMain(() =>
        {
            // :2708 no files changed and no edits: nothing is worked out; a summary that has its changes keeps none stored back.
            Assert.Null(Stored(withNone));
            Assert.Null(Stored(withChanges));
            // :2713 the stored summary reaches the thread's document (what is saved), so a restart reads it instead of parsing again.
            var snapshot = runtime.SnapshotForPersistence();
            FileChangeSummary? Saved(Guid turn) => snapshot.Items.Select(item => item.TimelineItemContent).OfType<TimelineItem.Content.TurnEnd>().Select(end => end.Value)
                .FirstOrDefault(summary => summary.TurnID == turn)?.Changes;
            Assert.NotNull(Saved(withFiles));
            Assert.NotNull(Saved(withEdits));
            Assert.Null(Saved(withNone));
            Close(window);
            runtime.CancelPersistence();
        });
    });
}
