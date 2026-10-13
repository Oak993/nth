using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-perf-ui-thread-4: a pass of the timeline that only adds a row to the running turn (a tool, a reply, the turn's end) leaves the minimap rail as it is: the same column, the same
// ticks, the hover and the scrub that go on. The rail is made once from its ticks, not again on attach. The ticks of a thread with more user messages than the id cache holds are made
// once per block.
[Collection("Headless")]
public sealed class TimelineMinimapKeptRailTests(HeadlessSession headless)
{
    private static readonly Guid Running = Guid.Parse("5a1f3c52-8d3e-4b6e-9b1a-0d7c2e9f4a11");

    // N finished turns, then the running turn's prompt.
    private static List<TimelineEntry> Thread(int finished)
    {
        var entries = Turns(finished, "old-").ToList();
        entries.Add(User("prompt", Running, "Please make the build faster"));
        return entries;
    }

    private static TimelineMinimapColumn RailOf(TimelineMount mount) => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(mount.View).Single();

    private static List<Button> TicksOf(TimelineMinimapColumn rail) => LogicalQuery.SelfAndDescendants<Button>(rail).ToList();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-4: rows added to the running turn keep the rail and every tick
    public void Rows_added_to_the_running_turn_keep_the_rail_and_its_ticks() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Thread(8);
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var rail = RailOf(mount);
        var ticks = TicksOf(rail);
        Assert.Equal(9, ticks.Count);

        entries = [.. entries, ToolEntry("tool-1", Running)]; mount.SetEntries(entries); mount.Settle();
        entries = [.. entries, ToolEntry("tool-2", Running), Reply("answer", Running, "Done.")]; mount.SetEntries(entries); mount.Settle();
        entries = [.. entries, End("end", Running)]; mount.SetEntries(entries); mount.SetPhase(RuntimePhase.Idle); mount.Settle();

        Assert.Same(rail, RailOf(mount));
        var after = TicksOf(RailOf(mount));
        Assert.Equal(ticks.Count, after.Count);
        for (var index = 0; index < ticks.Count; index++) Assert.Same(ticks[index], after[index]);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-4: a prompt adds a tick, so the rail is made again with it
    public void A_new_prompt_adds_a_tick_and_the_rail_shows_it() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Thread(8);
        mount.SetEntries(entries); mount.Settle();
        Assert.Equal(9, TicksOf(RailOf(mount)).Count);

        var next = Guid.NewGuid();
        entries = [.. entries, End("end", Running), User("second-prompt", next, "And the tests too")];
        mount.SetEntries(entries); mount.Settle();

        Assert.Equal(10, TicksOf(RailOf(mount)).Count);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-4: the rail made by the constructor is the rail on show after attach
    public void A_rail_is_made_once_and_not_again_when_it_comes_to_the_screen() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var entries = Turns(5).ToList();
            var blocks = DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, false, null);
            var column = new TimelineMinimapColumn(blocks, new TimelineScrollTracking(), 300, (_, _) => { });
            var made = column.Child;
            Assert.NotNull(made);

            var window = new Avalonia.Controls.Window { Content = column, Width = 100, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(made, column.Child);
            Assert.Equal(5, TicksOf(column).Count);
            window.Close();
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-4: the ticks of a long thread are made once per block, not again on each look
    public void The_ticks_of_a_thread_with_more_prompts_than_the_cache_holds_are_made_once_per_block() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var entries = Turns(1000).ToList();
            var blocks = DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, false, null);

            var first = TimelineMinimap.Entries(blocks);
            var second = TimelineMinimap.Entries(blocks);

            Assert.Equal(1000, first.Count);
            for (var index = 0; index < first.Count; index++) Assert.Same(first[index], second[index]);
            // Blocks made again from the same entries give the same ticks by value.
            var again = TimelineMinimap.Entries(DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, false, null));
            Assert.Equal(first, again);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-4: the allocation of an appended entry with the rail on no longer grows with the thread
    public void An_appended_entry_with_the_rail_on_costs_about_the_same_at_two_thousand_entries_as_at_two_hundred()
    {
        var small = ThreadTimelineLeanBudgetTests.BytesPerAppendedEntry(headless, 200);
        var large = ThreadTimelineLeanBudgetTests.BytesPerAppendedEntry(headless, 2000);
        Assert.True(large <= 2 * small, $"an appended entry with the rail costs {large} bytes at 2,000 entries, {small} at 200");
        Assert.True(large <= 1_000_000, $"an appended entry with the rail costs {large} bytes at 2,000 entries (limit 1,000,000; it was 36,757,355)");
    }
}
