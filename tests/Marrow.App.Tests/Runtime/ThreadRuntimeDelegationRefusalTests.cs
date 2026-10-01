using System.Diagnostics;
using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.refuseBlock(_:dropped:) (App/Runtime/ThreadRuntime.swift:2966-2973) and holdSentDelegationBlock(_:)
// (:2940-2958): a block that sends no heads is answered in a turn of the lead's own, up to the refusal cap; a block whose
// heads went out stays in the reply for its hold, fades, then leaves.
public sealed class ThreadRuntimeDelegationRefusalTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Refusal = "[Hydra] Your delegation block could not be read, so no heads went out.";
    private const string Dropped = "That block could not be read either.";

    private static HydraDelegationBudget Budget(ThreadRuntime runtime) =>
        (HydraDelegationBudget)typeof(ThreadRuntime).GetField("hydraBudget", Hidden)!.GetValue(runtime)!;

    private static void SetBudget(ThreadRuntime runtime, HydraDelegationBudget budget) =>
        typeof(ThreadRuntime).GetField("hydraBudget", Hidden)!.SetValue(runtime, budget);

    private static string Refuse(ThreadRuntime runtime) =>
        typeof(ThreadRuntime).GetMethod("RefuseBlock", Hidden)!.Invoke(runtime, [Refusal, Dropped])!.ToString()!;

    private static IEnumerable<UserMessage> UserMessages(ThreadRuntime runtime) =>
        runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<TimelineItem.Content.User>().Select(user => user.Value);

    private static string ReplyText(ThreadRuntime runtime, string id) =>
        ((TimelineItem.Content.Assistant)runtime.Entries.Single(entry => entry.Id == id).Item.TimelineItemContent).Value.Text;

    [Fact]
    [Trait("Req", "PR-RUNTIME-231")] // :2967 refuse counts; :2971 a turn of its own with hydraHeads [] (not a user turn)
    public void Under_the_cap_the_lead_is_told_in_a_turn_of_its_own_that_does_not_reset_the_count()
    {
        using var h = new ThreadRuntimePart12Harness();
        var outcome = h.Main.OnMain(() => Refuse(h.Runtime));
        Assert.Equal("TurnStarted", outcome);
        h.Main.OnMain(() => Assert.Equal(1, Budget(h.Runtime).Refusals));
        h.Main.Loop.RunUntil(() => UserMessages(h.Runtime).Any(message => message.Text == Refusal), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var told = UserMessages(h.Runtime).Single(message => message.Text == Refusal);
            Assert.NotNull(told.HydraHeads); // the empty array, not nil
            Assert.Empty(told.HydraHeads!);
            Assert.Equal(1, Budget(h.Runtime).Refusals); // beginTurn(userAuthored: false) kept the count
            Assert.DoesNotContain(UserMessages(h.Runtime), message => message.Text.StartsWith("No heads went out", StringComparison.Ordinal));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-230")] // :2967-2969 past the cap: dropped with a note, the lead hears it next turn
    public void Past_the_cap_the_block_is_dropped_with_a_note_and_a_notice_for_the_next_turn()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var budget = new HydraDelegationBudget();
            for (var i = 0; i < HydraDelegationBudget.MaxRefusals; i++) budget = budget.Refuse("earlier").Budget;
            SetBudget(h.Runtime, budget);
            var before = h.Runtime.Entries.Count;

            Assert.Equal("None", Refuse(h.Runtime));
            Assert.Equal(before + 1, h.Runtime.Entries.Count);
            Assert.Equal("No heads went out\n" + Dropped + " The block was dropped from the reply.", UserMessages(h.Runtime).Last().Text);
            Assert.Equal("[Hydra] " + Refusal + "\n\n---\n\n", Budget(h.Runtime).DropNotice);
            Assert.Equal(HydraDelegationBudget.MaxRefusals, Budget(h.Runtime).Refusals);
        });
        h.Main.Loop.RunUntilIdle(); // nothing was spawned: no turn starts
        h.Main.OnMain(() => Assert.DoesNotContain(UserMessages(h.Runtime), message => message.Text == Refusal));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-228")] // :2942-2948 held 3.2 s in its done state, then marked leaving
    [Trait("Req", "PR-RUNTIME-229")] // :2949-2955 after 450 ms the block leaves the reply
    public void A_sent_block_holds_for_its_done_wave_then_fades_then_leaves_the_reply()
    {
        using var h = new ThreadRuntimePart12Harness();
        const string text = "Sending two heads.\n\n```hydra-sent\n[{\"task\": \"a\"}, {\"task\": \"b\"}]\n```";
        var clock = Stopwatch.StartNew();
        h.Main.OnMain(() =>
        {
            typeof(ThreadRuntime).GetMethod("Append", Hidden, [typeof(TimelineItem)])!.Invoke(h.Runtime,
                [new TimelineItem(Guid.NewGuid(), new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }), "reply")]);
            typeof(ThreadRuntime).GetMethod("HoldSentDelegationBlock", Hidden)!.Invoke(h.Runtime, ["reply"]);
        });

        h.Main.Loop.RunUntil(() => ReplyText(h.Runtime, "reply").Contains("```hydra-leaving\n", StringComparison.Ordinal), AppModelHarness.Bound);
        var leftAt = clock.Elapsed;
        Assert.True(leftAt >= TimeSpan.FromSeconds(3.1), "marked leaving after " + leftAt);
        h.Main.OnMain(() => Assert.Equal(HydraPrompts.MarkingDelegationBlockLeaving(text), ReplyText(h.Runtime, "reply")));

        h.Main.Loop.RunUntil(() => !HydraPrompts.HasSentDelegationBlock(ReplyText(h.Runtime, "reply")), AppModelHarness.Bound);
        Assert.True(clock.Elapsed - leftAt >= TimeSpan.FromMilliseconds(400), "left " + (clock.Elapsed - leftAt) + " after fading");
        h.Main.OnMain(() => Assert.Equal(HydraPrompts.WithoutDelegationBlock(text), ReplyText(h.Runtime, "reply")));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-229")] // :2953 a reply that was only the block reads "Sent out heads."
    public void A_reply_that_was_only_the_block_reads_sent_out_heads()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            typeof(ThreadRuntime).GetMethod("Append", Hidden, [typeof(TimelineItem)])!.Invoke(h.Runtime,
                [new TimelineItem(Guid.NewGuid(), new TimelineItem.Content.Assistant(new AssistantMessage { Text = "```hydra-sent\n[{\"task\": \"a\"}]\n```" }), "reply")]);
            typeof(ThreadRuntime).GetMethod("HoldSentDelegationBlock", Hidden)!.Invoke(h.Runtime, ["reply"]);
        });
        h.Main.Loop.RunUntil(() => ReplyText(h.Runtime, "reply") == "Sent out heads.", AppModelHarness.Bound);
    }
}
