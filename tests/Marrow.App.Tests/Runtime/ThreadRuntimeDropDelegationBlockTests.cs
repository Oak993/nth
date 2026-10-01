using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.dropDelegationBlock(for:status:) (App/Runtime/ThreadRuntime.swift:2801-2826), the branch with no delegation
// stream: a turn that was stopped or failed before its reply's delegation block could go out.
public sealed class ThreadRuntimeDropDelegationBlockTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Block = "```hydra\n[{\"task\": \"Rename the helper\"}]\n```";

    private static TimelineEntry Reply(Guid turn, string text, string id) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }), id));

    private static TimelineEntry Asked(Guid turn, string id) =>
        new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Fix the parser" }), id));

    private static void Drop(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries, Guid turn, TurnStatus status)
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));
        typeof(ThreadRuntime).GetMethod("DropDelegationBlock", Hidden)!.Invoke(runtime, [turn, status]);
    }

    private static string? DropNotice(ThreadRuntime runtime) =>
        ((HydraDelegationBudget)typeof(ThreadRuntime).GetField("hydraBudget", Hidden)!.GetValue(runtime)!).DropNotice;

    private static string Text(TimelineEntry entry) => entry.Item.TimelineItemContent switch
    {
        TimelineItem.Content.Assistant assistant => assistant.Value.Text,
        TimelineItem.Content.User user => user.Value.Text,
        _ => "",
    };

    [Fact]
    [Trait("Req", "PR-RUNTIME-211")] // :2816-2822 the LAST block-holding reply row of THIS turn loses its block
    [Trait("Req", "PR-RUNTIME-212")] // :2823 the note, stopped wording
    [Trait("Req", "PR-RUNTIME-260")] // :2825 the lead hears it on its next turn
    public void A_stopped_turn_strips_the_block_from_its_last_reply_holding_one_notes_it_and_tells_the_lead()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var (earlier, now) = (Guid.NewGuid(), Guid.NewGuid());
            var older = "Earlier.\n\n" + Block;
            var first = "Sending heads.\n\n" + Block;
            var second = "Sending more.\n\n" + Block;
            Drop(h.Runtime,
            [
                Reply(earlier, older, "e1"), // another turn's block is left alone
                Asked(now, "u1"),
                Reply(now, first, "r1"),
                Reply(now, second, "r2"), // the last row holding a block
                Reply(now, "No block here.", "r3"),
            ], now, TurnStatus.Interrupted);

            var entries = h.Runtime.Entries;
            Assert.Equal(older, Text(entries[0]));
            Assert.Equal(first, Text(entries[2]));
            Assert.Equal(HydraPrompts.WithoutDelegationBlock(second), Text(entries[3]));
            Assert.DoesNotContain("```hydra", Text(entries[3]), StringComparison.Ordinal);
            Assert.Equal("No block here.", Text(entries[4]));
            var note = Assert.IsType<TimelineItem.Content.User>(entries[^1].Item.TimelineItemContent).Value;
            Assert.Equal(6, entries.Count);
            Assert.Equal("Hydra sent no heads: the turn was stopped before they could go out. Ask again to send them.", note.Text);
            Assert.NotNull(note.HydraHeads); // a Hydra note, not a user prompt
            Assert.Equal(
                "[Hydra] No heads went out.\nYour reply's delegation block was dropped: the turn was stopped before the heads could go out, and none of that work was delegated. Do it yourself, or send the block again.\n\n---\n\n",
                DropNotice(h.Runtime));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-211")] // :2819 a reply that was only the block reads "Asked for heads."
    [Trait("Req", "PR-RUNTIME-212")] // :2823 the note, failed wording
    public void A_failed_turn_whose_reply_was_only_the_block_reads_asked_for_heads()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var now = Guid.NewGuid();
            Drop(h.Runtime, [Asked(now, "u1"), Reply(now, Block, "r1")], now, TurnStatus.Failed);
            var entries = h.Runtime.Entries;
            Assert.Equal("Asked for heads.", Text(entries[1]));
            Assert.Equal("Hydra sent no heads: the turn failed before they could go out. Ask again to send them.", Text(entries[^1]));
            Assert.Contains("the turn failed before the heads could go out", DropNotice(h.Runtime), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-211")] // :2816-2817 no fallback: a turn with no block-holding row is left as it is
    public void A_turn_with_no_block_changes_nothing_and_notes_nothing()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var now = Guid.NewGuid();
            Drop(h.Runtime, [Asked(now, "u1"), Reply(now, "Just an answer.", "r1")], now, TurnStatus.Interrupted);
            var entries = h.Runtime.Entries;
            Assert.Equal(2, entries.Count);
            Assert.Equal("Just an answer.", Text(entries[1]));
            Assert.Null(DropNotice(h.Runtime));
        });
    }
}
