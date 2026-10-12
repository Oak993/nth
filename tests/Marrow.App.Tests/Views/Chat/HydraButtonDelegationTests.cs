using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The lead's delegation under way, read off the badge (App/Views/Chat/HydraButton.swift:35, :113-174): with Hydra on, no
/// head running and the lead's turn running, a running agent tool or a delegation block in a reply of the LAST turn makes
/// the badge appear with the sending pulse (the mini spinner) before any head has a status.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonDelegationTests(HeadlessSession headless)
{
    private const string Block = "On it.\n```hydra\n[]\n```\n";

    private void Run(Action<HydraButtonRig> body, bool hydraOn = true) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var rig = new HydraButtonRig(hydraOn);
        body(rig);
    });

    // Settled first: a badge that is leaving stays under its parent until its transition ends (SwiftTransition.Remove).
    private static bool Pulses(HydraButtonRig rig)
    {
        rig.Tick(0.6);
        return rig.Badge is { } badge && LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(badge).Any();
    }

    private static bool Absent(HydraButtonRig rig)
    {
        rig.Tick(0.6);
        return rig.Badge is null;
    }

    // A row id no other test has used: the finished-reply cache is a process-wide static keyed by row id (:144).
    private static string Row() => "row-" + Guid.NewGuid().ToString("N");

    [Fact]
    [Trait("Req", "PR-HYDRA-159")] // HydraButton.swift:35 isLeadDelegating gate; :121-123 a running agent tool row
    public void A_running_agent_tool_in_the_last_turn_makes_the_badge_pulse()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.User(Row(), turn, "go"), HydraButtonRig.Tool(Row(), turn, ToolCall.Kind.Agent, ToolCall.Status.Running));
            Assert.True(Pulses(rig));

            // :122 the status must be running, and the kind must be agent.
            rig.SetEntries(HydraButtonRig.User(Row(), turn, "go"), HydraButtonRig.Tool(Row(), turn, ToolCall.Kind.Agent, ToolCall.Status.Completed));
            Assert.True(Absent(rig));
            rig.SetEntries(HydraButtonRig.User(Row(), turn, "go"), HydraButtonRig.Tool(Row(), turn, ToolCall.Kind.Command, ToolCall.Status.Running));
            Assert.True(Absent(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-159")] // HydraButton.swift:124-130 an assistant message holding a delegation block, finished or streaming
    public void A_delegation_block_in_a_finished_or_a_streaming_reply_makes_the_badge_pulse()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.Reply(Row(), turn, "Nothing to hand out."));
            Assert.True(Absent(rig));
            rig.SetEntries(HydraButtonRig.Reply(Row(), turn, Block));
            Assert.True(Pulses(rig));
            rig.SetEntries(HydraButtonRig.Reply(Row(), turn, Block, streaming: true));
            Assert.True(Pulses(rig));
            rig.SetEntries(HydraButtonRig.Reply(Row(), turn, "```hydra-sent\n[]\n```\n"));
            Assert.True(Pulses(rig)); // the three dialects are one test (HydraPrompts.hasDelegationBlock)
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-159")] // HydraButton.swift:115-120 only the LAST turn's entries are examined
    public void Only_the_last_turn_is_examined_and_the_walk_stops_at_the_first_entry_of_an_earlier_turn()
    {
        Run(rig =>
        {
            var older = Guid.NewGuid();
            var last = Guid.NewGuid();
            rig.SetRunning(true);

            // An older turn's running agent tool and block do not count once a newer turn is the last one.
            rig.SetEntries(
                HydraButtonRig.Tool(Row(), older, ToolCall.Kind.Agent, ToolCall.Status.Running),
                HydraButtonRig.Reply(Row(), older, Block),
                HydraButtonRig.User(Row(), last, "next question"));
            Assert.True(Absent(rig));

            // Anywhere in the last turn counts, however many other rows follow it.
            rig.SetEntries(
                HydraButtonRig.User(Row(), older, "old"),
                HydraButtonRig.Tool(Row(), last, ToolCall.Kind.Agent, ToolCall.Status.Running),
                HydraButtonRig.Tool(Row(), last, ToolCall.Kind.Command, ToolCall.Status.Completed),
                HydraButtonRig.Reply(Row(), last, "plain"));
            Assert.True(Pulses(rig));
            rig.SetEntries(
                HydraButtonRig.Reply(Row(), last, Block),
                HydraButtonRig.Reply(Row(), last, "plain"));
            Assert.True(Pulses(rig));

            // The walk stops at the first entry of another turn: rows of the last turn beyond it are never reached.
            rig.SetEntries(
                HydraButtonRig.Tool(Row(), last, ToolCall.Kind.Agent, ToolCall.Status.Running),
                HydraButtonRig.Reply(Row(), older, "plain"),
                HydraButtonRig.Reply(Row(), last, "plain"));
            Assert.True(Absent(rig));

            // The loop breaks at the first earlier-turn entry: a turn-less last row is its own turn.
            rig.SetEntries(
                HydraButtonRig.Tool(Row(), last, ToolCall.Kind.Agent, ToolCall.Status.Running),
                HydraButtonRig.Reply(Row(), null, "plain"));
            Assert.True(Absent(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-159")] // HydraButton.swift:35 isOn && running == 0 && runtime.isRunning
    public void The_pulse_needs_hydra_on_for_the_chat_and_the_lead_running()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetEntries(HydraButtonRig.Tool(Row(), turn, ToolCall.Kind.Agent, ToolCall.Status.Running));
            Assert.True(Absent(rig)); // the lead is idle

            rig.SetRunning(true);
            Assert.True(Pulses(rig));
            rig.SetRunning(false);
            Assert.True(Absent(rig));
        });

        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.Tool(Row(), turn, ToolCall.Kind.Agent, ToolCall.Status.Running));
            Assert.True(Absent(rig)); // Hydra is off for this chat
        }, hydraOn: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-159")] // HydraButton.swift:35 running == 0: a head already at work shows its count, not the pulse
    public void With_a_head_running_the_badge_shows_the_count_and_not_the_delegation_pulse()
    {
        Run(rig =>
        {
            rig.SetRunning(true);
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1); // past the sending pulse the new head starts
            rig.SetEntries(HydraButtonRig.Tool(Row(), Guid.NewGuid(), ToolCall.Kind.Agent, ToolCall.Status.Running));
            Assert.NotNull(rig.Badge);
            Assert.False(Pulses(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:144-152 a finished reply's answer is remembered by its row id, not its text
    public void A_finished_replys_answer_is_remembered_by_its_row_id_not_its_text()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var row = Row();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.Reply(row, turn, "No block here."));
            Assert.True(Absent(rig));

            // The same row, now holding a block: the remembered answer stands, as a finished reply's text never changes.
            rig.SetEntries(HydraButtonRig.Reply(row, turn, Block));
            Assert.True(Absent(rig));

            // Another row with the same text is read afresh.
            rig.SetEntries(HydraButtonRig.Reply(Row(), turn, Block));
            Assert.True(Pulses(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:144 RecentCache(limit: 32): a two-generation cache
    public void The_finished_reply_cache_holds_thirty_two_rows_and_lets_go_of_the_oldest_after_sixty_four_more()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var row = Row();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.Reply(row, turn, "No block here."));

            // Thirty-two other rows later the row is still remembered (RecentCache.swift:43-56 rolls over on the 33rd insert).
            rig.SetEntries(Enumerable.Range(0, 32).Select(_ => HydraButtonRig.Reply(Row(), turn, "plain")).ToArray());
            rig.SetEntries(HydraButtonRig.Reply(row, turn, Block));
            Assert.True(Absent(rig));

            // Sixty-four more and it is gone: read afresh, the row's block is found.
            rig.SetEntries(Enumerable.Range(0, 64).Select(_ => HydraButtonRig.Reply(Row(), turn, "plain")).ToArray());
            rig.SetEntries(HydraButtonRig.Reply(row, turn, Block));
            Assert.True(Pulses(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:162-174 a streaming reply is scanned from where the last scan ended
    public void A_fence_split_across_two_flushes_is_found_once_its_second_half_arrives()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var entry = HydraButtonRig.Reply(Row(), turn, "Plan:\n```hy", streaming: true);
            rig.SetRunning(true);
            rig.SetEntries(entry);
            Assert.True(Absent(rig));

            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Plan:\n```hydra\n", IsStreaming = true }));
            Assert.True(Pulses(rig)); // every streamed token re-renders the button (:139-142)
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:168 max(0, scanned - 32): thirty-two bytes of overlap, no more
    public void The_overlap_of_a_rescan_is_exactly_thirty_two_bytes()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetRunning(true);

            // An opener cut off 32 bytes before the end of the first flush starts exactly at the rescan's first byte.
            var within = "```" + new string(' ', 24) + "hydra";
            Assert.Equal(32, within.Length);
            var entry = HydraButtonRig.Reply(Row(), turn, within, streaming: true);
            rig.SetEntries(entry);
            Assert.True(Absent(rig));
            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = within + "\n", IsStreaming = true }));
            Assert.True(Pulses(rig));

            // One byte further back it begins before the rescan does: not found, as the source reads it.
            var beyond = "```" + new string(' ', 25) + "hydra";
            Assert.Equal(33, beyond.Length);
            var other = HydraButtonRig.Reply(Row(), turn, beyond, streaming: true);
            rig.SetEntries(other);
            Assert.True(Absent(rig));
            rig.ReplaceItem(other, new TimelineItem.Content.Assistant(new AssistantMessage { Text = beyond + "\n", IsStreaming = true }));
            Assert.True(Absent(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:163 a row found to hold a block stays found
    public void A_streaming_row_found_to_hold_a_block_stays_found()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var entry = HydraButtonRig.Reply(Row(), turn, Block, streaming: true);
            rig.SetRunning(true);
            rig.SetEntries(entry);
            Assert.True(Pulses(rig));

            // Rewritten with no block and a different length, still streaming: the row stays found.
            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "plain", IsStreaming = true }));
            Assert.True(Pulses(rig));

            // Finished, it is read by the finished-reply path, which has no block to find.
            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "plain", IsStreaming = false }));
            Assert.True(Absent(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:166 an unchanged length is not scanned again
    public void A_streaming_reply_whose_length_has_not_changed_is_not_scanned_again()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var entry = HydraButtonRig.Reply(Row(), turn, new string('a', 52), streaming: true);
            rig.SetRunning(true);
            rig.SetEntries(entry);
            Assert.True(Absent(rig));

            // Same number of bytes, different text, now holding a block in its last 32 bytes: count == scanned answers
            // false at once (:166), though a rescan from byte 20 would find it.
            var text = new string('a', 43) + "```hydra\n";
            Assert.Equal(52, text.Length);
            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = true }));
            Assert.True(Absent(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:167-168 a reply rewritten shorter is read from the start again
    public void A_streaming_reply_rewritten_shorter_is_read_from_the_start()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            var entry = HydraButtonRig.Reply(Row(), turn, new string('a', 100), streaming: true);
            rig.SetRunning(true);
            rig.SetEntries(entry);
            Assert.True(Absent(rig));

            rig.ReplaceItem(entry, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "```hydra\n", IsStreaming = true }));
            Assert.True(Pulses(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-160")] // HydraButton.swift:171 the scan map is dropped wholesale when it holds more than 64 rows
    public void The_scan_map_is_dropped_wholesale_when_more_than_sixty_four_rows_are_tracked()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            // The split opener of the previous test that a retained scan state cannot find (33 bytes back).
            var beyond = "```" + new string(' ', 25) + "hydra";
            var tracked = HydraButtonRig.Reply(Row(), turn, beyond, streaming: true);
            rig.SetRunning(true);
            rig.SetEntries(tracked);
            Assert.True(Absent(rig));

            // One walk over 66 streaming rows (the tracked one first, as the walk runs from the end): the 66th insert finds
            // the map at 65 and drops it, the tracked row's state with it.
            var fillers = Enumerable.Range(0, 65).Select(_ => HydraButtonRig.Reply(Row(), turn, "filler text", streaming: true));
            rig.SetEntries(fillers.Prepend(tracked).Reverse().ToArray());
            Assert.True(Absent(rig));

            // With no state left the row is scanned from byte 0, and the opener completed by this token is found.
            rig.ReplaceItem(tracked, new TimelineItem.Content.Assistant(new AssistantMessage { Text = beyond + "\n", IsStreaming = true }));
            Assert.True(Pulses(rig));
        });
    }
}
