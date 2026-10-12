using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-perf-ui-thread-3: the meta the timeline reads (the last assistant row of a turn, the turns that have a reply, a turn's summary) is built in one pass over the entries, not by
// copying its maps once per assistant row, and the turn's phase flipping (at the send and at the turn's end) is a tail build of the blocks, not a rebuild of the whole thread.
// Allocation is counted on the calling thread with GC.GetAllocatedBytesForCurrentThread: it does not depend on the clock or the machine.
[Collection("Headless")]
public sealed class TimelineMetaBuildTests(HeadlessSession headless)
{
    private void OnMain(Action body) => headless.OnUiThread(() => { using var runtime = new TestRuntime(); runtime.OnMain(body); });

    // N finished turns of a question, three assistant rows (text, tool follow-up text, answer) and the turn's end marker.
    private static List<TimelineEntry> Thread(int turns, string prefix = "t")
    {
        var entries = new List<TimelineEntry>();
        for (var i = 0; i < turns; i++)
        {
            var turn = Guid.NewGuid();
            entries.Add(User(prefix + "-user-" + i, turn, "Question " + i));
            for (var row = 0; row < 3; row++) entries.Add(Reply(prefix + "-reply-" + i + "-" + row, turn, "Answer " + i + "." + row));
            entries.Add(End(prefix + "-end-" + i, turn));
        }
        return entries;
    }

    // The definition of the meta: one Add per entry, each Add copying the maps it writes (what Build did before the single pass).
    private static TimelineMeta FoldOfOneEntryAtATime(IEnumerable<TimelineEntry> entries)
    {
        var meta = new TimelineMeta();
        foreach (var entry in entries)
        {
            if (entry.TimelineEntryKind == TimelineEntry.Kind.Assistant && entry.TurnID is { } turn)
                meta = meta with { LastAssistantIDByTurn = meta.LastAssistantIDByTurn.Mutating(map => map[turn] = entry.Id), TurnsWithReply = meta.TurnsWithReply.Mutating(set => set.Add(turn)) };
            else if (entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd && entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd end)
                meta = meta with { SummaryByTurn = meta.SummaryByTurn.Mutating(map => map[end.Value.TurnID] = end.Value) };
        }
        return meta;
    }

    private static long BytesToBuild(int turns)
    {
        var entries = Thread(turns);
        TimelineMeta.Build(entries); // warm-up: JIT and the static caches
        var before = GC.GetAllocatedBytesForCurrentThread();
        var meta = TimelineMeta.Build(entries);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(turns, meta.SummaryByTurn.Count);
        return bytes;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the single pass is the fold of Add, value for value
    public void The_meta_of_one_pass_equals_the_meta_of_one_entry_at_a_time() => OnMain(() =>
    {
        var entries = Thread(40);
        // A turn whose last assistant row is replaced by a later one, a turn with a reply and no end, an end with no reply, and rows of no turn.
        var open = Guid.NewGuid(); var endedOnly = Guid.NewGuid();
        entries.Add(User("open-user", open)); entries.Add(Reply("open-reply-1", open)); entries.Add(Reply("open-reply-2", open));
        entries.Add(End("ended-only", endedOnly)); entries.Add(Reply("no-turn-reply")); entries.Add(User("no-turn-user"));

        var onePass = TimelineMeta.Build(entries);
        var folded = FoldOfOneEntryAtATime(entries);

        Assert.Equal(folded.LastAssistantIDByTurn, onePass.LastAssistantIDByTurn);
        Assert.Equal(folded.TurnsWithReply, onePass.TurnsWithReply);
        Assert.Equal(folded.SummaryByTurn, onePass.SummaryByTurn);
        Assert.Equal("open-reply-2", onePass.LastAssistantIDByTurn[open]);
        Assert.Contains(open, onePass.TurnsWithReply);
        Assert.DoesNotContain(endedOnly, onePass.TurnsWithReply);
        Assert.Equal(41, onePass.SummaryByTurn.Count);
        // Add stays what it was: one entry on a meta, the original untouched.
        var base1 = TimelineMeta.Build(entries.Take(10).ToList());
        var grown = base1.Add(entries[10]);
        Assert.Equal(FoldOfOneEntryAtATime(entries.Take(11)).LastAssistantIDByTurn, grown.LastAssistantIDByTurn);
        Assert.Equal(FoldOfOneEntryAtATime(entries.Take(10)).LastAssistantIDByTurn, base1.LastAssistantIDByTurn);
        // Entries that change nothing give back the same meta.
        Assert.Same(base1, base1.AddRange([User("u"), Reply("r")]));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the allocation of Build grows with the thread, not with the thread times its turns
    public void Building_the_meta_allocates_in_proportion_to_the_thread_not_to_its_square() => OnMain(() =>
    {
        var small = BytesToBuild(250);
        var large = BytesToBuild(1000);
        // 1,000 turns of three assistant rows: one copy of three small collections costs well under 2 MB; copying the maps per assistant row costs a hundred times that (the control below).
        Assert.True(large < 2_000_000, "a 1,000-turn meta allocated " + large + " bytes");
        // Four times the thread, about four times the bytes (with the maps' growth steps: under six); a square would be sixteen.
        Assert.True(large < small * 6, "250 turns " + small + " bytes, 1,000 turns " + large + " bytes");
        // The control: the fold that copies per entry (the old build) allocates far more than the single pass on the same thread, so the budget above is not slack.
        var entries = Thread(1000);
        var beforeFold = GC.GetAllocatedBytesForCurrentThread();
        FoldOfOneEntryAtATime(entries);
        var folded = GC.GetAllocatedBytesForCurrentThread() - beforeFold;
        Assert.True(folded > large * 20, "the fold of one entry at a time allocated " + folded + " bytes against " + large);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the entries appended to a thread land in the meta together
    public void The_appended_entries_of_a_tail_build_are_folded_in_together_and_the_blocks_are_those_of_a_rebuild() => OnMain(() =>
    {
        var entries = Thread(30);
        var cache = new TimelineBlockCache();
        var first = cache.Blocks(entries, false, false, null);
        var turn = Guid.NewGuid();
        var grown = entries.Concat([User("new-user", turn), Reply("new-a", turn), Reply("new-b", turn), End("new-end", turn)]).ToList();
        var tail = cache.Blocks(grown, false, false, null);
        Assert.Equal(DisplayBlock.Build(grown, TimelineMeta.Build(grown), false, false, null), tail);
        // The finished turns before the last run of the pass before are the very blocks of that pass (the last run is made again: the appended turn follows it).
        for (var index = 0; index < first.Count - 1; index++) Assert.Same(first[index], tail[index]);
        // The new turn has its summary and its last reply, as the single-pass meta says.
        var meta = TimelineMeta.Build(grown);
        Assert.Equal("new-b", meta.LastAssistantIDByTurn[turn]);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the send flips the phase to running and appends the prompt; the thread before it is not built again
    public void A_send_builds_only_the_tail_and_leaves_the_blocks_before_it_as_they_were() => OnMain(() =>
    {
        var entries = Thread(60);
        var cache = new TimelineBlockCache();
        var idle = cache.Blocks(entries, false, false, null);
        var turn = Guid.NewGuid();
        var sent = entries.Concat([User("sent", turn, "go on")]).ToList();
        var running = cache.Blocks(sent, false, true, null);
        Assert.Equal(DisplayBlock.Build(sent, TimelineMeta.Build(sent), false, true, null), running);
        for (var index = 0; index < idle.Count - 1; index++) Assert.Same(idle[index], running[index]);
        Assert.True(running[idle.Count] is DisplayBlock.Turn { ShowsWorking: true });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the turn's end appends its marker and flips the phase back; the blocks before the turn stay, and the turn is the finished one
    public void The_end_of_a_turn_builds_only_the_tail_and_gives_the_blocks_of_a_rebuild() => OnMain(() =>
    {
        var entries = Thread(60);
        var turn = Guid.NewGuid();
        var running = entries.Concat([User("sent", turn, "go on"), Reply("answer", turn, "done")]).ToList();
        var cache = new TimelineBlockCache();
        var before = cache.Blocks(running, false, true, null);
        var finished = running.Concat([End("sent-end", turn)]).ToList();
        var after = cache.Blocks(finished, false, false, null);
        Assert.Equal(DisplayBlock.Build(finished, TimelineMeta.Build(finished), false, false, null), after);
        for (var index = 0; index < 60; index++) Assert.Same(before[index], after[index]);
        Assert.True(after[60] is DisplayBlock.Turn { Summary: not null, ShowsWorking: false });
        Assert.DoesNotContain(after, block => block is DisplayBlock.Working);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: only the phase flipping, with no new entry, also leaves the blocks before the running turn alone
    public void The_phase_alone_flipping_gives_the_blocks_of_a_rebuild_and_keeps_the_stable_ones() => OnMain(() =>
    {
        var entries = Thread(25);
        var turn = Guid.NewGuid();
        var running = entries.Concat([User("sent", turn, "go on"), ToolEntry("tool", turn), Reply("partial", turn, "so far")]).ToList();
        var cache = new TimelineBlockCache();
        var on = cache.Blocks(running, true, true, null);
        var off = cache.Blocks(running, true, false, null);
        Assert.Equal(DisplayBlock.Build(running, TimelineMeta.Build(running), true, false, null), off);
        for (var index = 0; index < 25; index++) Assert.Same(on[index], off[index]);
        var back = cache.Blocks(running, true, true, null);
        Assert.Equal(DisplayBlock.Build(running, TimelineMeta.Build(running), true, true, null), back);
        Assert.Equal(on, back);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-3: the heads' pill that goes before the running turn is inserted into the stable part: that flip still rebuilds whole
    public void A_phase_flip_with_the_heads_pill_before_the_running_turn_still_gives_the_blocks_of_a_rebuild() => OnMain(() =>
    {
        var entries = Thread(10);
        var turn = Guid.NewGuid();
        var running = entries.Concat([User("sent", turn, "go on"), Reply("partial", turn, "so far")]).ToList();
        var pills = new HeadPills { Earlier = new([1, 2]) };
        var cache = new TimelineBlockCache();
        cache.Blocks(running, false, false, null, pills);
        var on = cache.Blocks(running, false, true, null, pills);
        Assert.Equal(DisplayBlock.Build(running, TimelineMeta.Build(running), false, true, null, pills), on);
        var off = cache.Blocks(running, false, false, null, pills);
        Assert.Equal(DisplayBlock.Build(running, TimelineMeta.Build(running), false, false, null, pills), off);
    });
}
