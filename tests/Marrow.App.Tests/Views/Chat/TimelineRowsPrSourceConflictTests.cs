using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrSourceConflictTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_sixty_limit_recent_cache_keeps_more_than_sixty_entries_across_its_two_generations()
    {
        // STOP opencode-App.Views.Chat.TimelineRows #2 PR-CHAT-102.
        // TimelineRows.swift:2135-2138 uses RecentCache(limit:60); Core/Support/RecentCache.swift:43-55
        // rotates two generations, contradicting the record's "At most sixty parsed files" total limit.
        var cache = new RecentCache<string, string>(60);
        for (var i = 0; i < 61; i++) cache.Insert(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "diff" + i);
        Assert.All(Enumerable.Range(0, 61), i => Assert.True(cache.Contains("diff" + i)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_idle_loaded_legacy_summary_is_updated_in_memory_but_the_pinned_revision_guard_drops_it_on_restart()
    {
        // STOP opencode-App.Views.Chat.TimelineRows #3 PR-CHAT-107 (the same conflict as STOP #7 in TimelineRowsTests).
        // ThreadRuntime.swift:3797-3802 calls scheduleSave without incrementing saveRevision;
        // :3650-3664 saveNow rejects an unchanged revision. A ruling must resolve persistence vs pinned source.
        // PR-GIT-151 is no longer under this STOP (adversarial review 2026-10-03: it was left under it by mistake when STOP #11
        // was rewritten). Its record asks for the one-off parse of an older turn's diff and for the result "stored on the runtime
        // as that turn's changes"; its persistence line is only "TurnSummary.changes carries the summary for turns recorded with
        // it", which these assertions do not touch. The in-memory store the record names is asserted just below and in
        // TimelineRowsPrRecordsTests; that the stored summary does not survive a restart is PR-CHAT-107's "survives into the
        // thread document" line, and that record alone stays blocked on the ruling.
        headless.OnUiThread(() =>
        {
            Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var store = new AppModelHarness();
            var project = AppModelSamples.Project("Legacy restart", store.NewFolder("legacy-restart"));
            var thread = AppModelSamples.Thread(project);
            var turn = Guid.NewGuid();
            store.WriteLibrary([project], [thread]);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(thread.Id)
            {
                Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turn, Index = 0, Status = TurnStatus.Completed }]),
                Items = new ValueList<TimelineItem>([new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "legacy" }))]),
            }, Storage.ThreadURL(thread.Id));
            var model = store.MakeModel(); store.PrepareBootstrap(model);
            var runtime = store.Main.OnMain(() => model.Runtime(thread.Id));
            store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            store.Main.OnMain(runtime.SaveNow); // Settle the load-created turn-end repair before testing an idle revision.
            DiskWriter.Shared.WaitForQueuedWrites();
            var changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("a.txt", 1, 0, false)]), 1, 0);
            store.Main.OnMain(() =>
            {
                runtime.StoreLegacyChanges(changes, turn);
                var end = runtime.Entries.Single(entry => entry.TurnID == turn && entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd);
                Assert.Equal(changes, Assert.IsType<TimelineItem.Content.TurnEnd>(end.Item.TimelineItemContent).Value.Changes);
                runtime.SaveNow(); runtime.CancelPersistence();
            });
            DiskWriter.Shared.WaitForQueuedWrites();
            var restartedModel = store.MakeModel(); store.PrepareBootstrap(restartedModel);
            var restarted = store.Main.OnMain(() => restartedModel.Runtime(thread.Id));
            store.Main.Loop.Run(() => restarted.EnsureLoaded(), AppModelHarness.Bound);
            store.Main.OnMain(() =>
            {
                var end = restarted.Entries.Single(entry => entry.TurnID == turn && entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd);
                Assert.Null(Assert.IsType<TimelineItem.Content.TurnEnd>(end.Item.TimelineItemContent).Value.Changes);
                restarted.CancelPersistence();
            });
        });
    }
}
