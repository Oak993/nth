using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The classification of the timeline's blocks (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792): what each kind of block is,
// read off the blocks a real build makes (DisplayBlock.Build) and off what the rail makes of them (TimelineMinimap).
[Collection("Headless")]
public sealed class ThreadTimelineBlocksTests(HeadlessSession headless)
{
    private void OnMain(Action body) => headless.OnUiThread(() => { using var runtime = new TestRuntime(); runtime.OnMain(body); });

    private static TimelineEntry NoticeEntry(string id, Guid? turn = null) =>
        Entry(id, new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Info, Message = "a notice" }), turn);

    [Fact]
    [Trait("Req", "PR-CHAT-039")] // ThreadTimeline.swift:1520-1526 (hasUserMessage); TimelineMinimap.swift:21-35 (entry(for:), one tick per block from its first sent message)
    public void Only_a_turn_with_a_sent_message_and_a_plain_single_user_group_carry_a_tick_and_a_turn_gives_one_from_its_first_message() => OnMain(() =>
    {
        var one = Guid.NewGuid(); var two = Guid.NewGuid();
        var entries = new[]
        {
            // Entries of no turn: a plain group each, and a run of tools and a head.
            User("plain-user", null, "a message sent outside any turn"), Reply("plain-reply"), NoticeEntry("plain-notice"),
            Entry("plain-plan", new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "plan", ProposedPlanState = ProposedPlan.State.Proposed })),
            Entry("plain-todos", new TimelineItem.Content.Todos(new([new TodoStep { Text = "step", TodoStepStatus = TodoStep.Status.Pending }]))),
            ToolEntry("plain-tool"), ToolEntry("plain-head", head: true),
            // A turn with two sent messages (a prompt and a steer): one block, one tick, from the first of them.
            User("one-prompt", one, "first prompt of the turn"), Reply("one-reply", one), User("one-steer", one, "a steer sent mid-turn"), End("one-end", one),
            // A turn with no sent message at all.
            Reply("two-reply", two), ToolEntry("two-tool", two),
        };
        var blocks = DisplayBlock.Build(entries, TimelineMeta.Build(entries), false, true, "merging", new HeadPills { Later = new([1]) });
        // Every kind of block the build makes, in order.
        Assert.Equal(new[] { "Group:Single", "Group:Single", "Group:Single", "Group:Single", "Group:Single", "Group:Work", "Group:Heads", "Turn", "Turn", "HeadsWorking", "Merging" },
            blocks.Select(block => block is DisplayBlock.Group group ? "Group:" + group.Value.GetType().Name : block.GetType().Name));
        Assert.Equal(new[] { true, false, false, false, false, false, false, true, false, false, false }, blocks.Select(block => block.HasUserMessage));
        // The working line on its own never has one either.
        Assert.False(new DisplayBlock.Working(null, new([])).HasUserMessage);
        Assert.False(Assert.Single(DisplayBlock.Build([], TimelineMeta.Build([]), false, true, null)).HasUserMessage);
        // The rail takes the same blocks: one tick for the plain message and one for the turn, titled from its first message and not the steer.
        var ticks = TimelineMinimap.Entries(blocks);
        Assert.Equal(new[] { "plain-user", blocks[7].Id }, ticks.Select(tick => tick.Id));
        Assert.Equal("a message sent outside any turn", ticks[0].Title);
        Assert.Equal("first prompt of the turn", ticks[1].Title);
        Assert.Equal(blocks.Count(block => block.HasUserMessage), ticks.Count);
    });
}
