using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6, 12.6b; ED-NEW-dextra-as6-1): the words of Stop all, Pause all and Resume all, and the rule
// that flips the header, tray and voice toggle. The sentences follow the approved page, so the app and the page read alike.
public sealed class ControlWordsTests
{
    private static Mission Of(MissionState state, PauseCause? pausedBy = null, bool reverted = false) =>
        new(Guid.NewGuid(), "Weekly stats", "dextra", MissionOrigin.Owner, state) { PausedBy = pausedBy, Reverted = reverted };

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-1")]
    public void The_stop_strip_keeps_the_time_it_was_sent_apart_from_the_time_the_agents_confirmed()
    {
        Assert.Equal(
            "Stop sent in 4 ms (Ctrl+Alt+Esc). 11 of 11 agents confirmed stopped in 0.9 s; process trees closed. Nothing was undone. "
            + "Open decisions are held. Each mission's changes wait in its own branch. Duties are held until Resume all.",
            ControlWords.StopStrip(3.6, "Ctrl+Alt+Esc", 11, 11, TimeSpan.FromSeconds(0.9)));
        Assert.StartsWith("Stop sent in 12 ms (Stop all button). Confirming agents: 3 of 11 stopped… Nothing was undone.",
            ControlWords.StopStrip(12, "Stop all button", 11, 3, null), StringComparison.Ordinal);
        // Never "0 ms": the press itself took some time.
        Assert.StartsWith("Stop sent in 1 ms ", ControlWords.StopStrip(0.2, "Tray", 0, 0, null), StringComparison.Ordinal);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Esc is taken. Change it in Settings", ControlWords.StopKeyUnavailable("Ctrl+Alt+Esc"));
        Assert.Equal("Still pausing. Waiting for Voice to finish: generating the voiceover (3:42 of script). Pause never cuts a turn; Stop cuts it now.",
            ControlWords.StillPausing(["Voice"], "generating the voiceover (3:42 of script)"));
        Assert.Equal("Still pausing. Waiting for A and B to finish. Pause never cuts a turn; Stop cuts it now.", ControlWords.StillPausing(["A", "B"], "  "));
        Assert.Equal("Resuming with a cold cache: about 60k tokens read again.", ControlWords.ColdCache(60_400));
        Assert.Equal("Resuming with a cold cache: about 1k tokens read again.", ControlWords.ColdCache(1_000));
        Assert.Equal("Resuming with a cold cache: about 640 tokens read again.", ControlWords.ColdCache(640));
        Assert.Equal("Resuming with a cold cache: the context is read again at the uncached price.", ControlWords.ColdCache(null));
        Assert.Equal("Resuming with a cold cache: the context is read again at the uncached price.", ControlWords.ColdCache(0));
    }

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-1")]
    public void Resume_all_names_what_it_picked_up_and_what_it_left_alone()
    {
        var result = new ResumeAllResult(AfterStop: true)
        {
            PickedUp = 4,
            BackToNeedsYou = new ValueList<string>(["send"]),
            StoppedByHand = new ValueList<string>(["Weekly stats for both channels"]),
            HeadsStoppedByHand = new ValueList<string>(["Writer in “Espresso machines”"]),
            Reverted = 2,
        };
        Assert.Equal(
            "Resumed all after the stop: 4 tasks picked up where they left off. 1 went back to Needs you: a send card never resumes on its own. "
            + "“Weekly stats for both channels” stays stopped, because you stopped it. "
            + "Writer in “Espresso machines” stays stopped, because you stopped it on your own. 2 reverted tasks wait for Restart.",
            ControlWords.ResumeAllLine(result));
        Assert.Equal("Resumed all: 1 task picked up where it left off.", ControlWords.ResumeAllLine(new ResumeAllResult(false) { PickedUp = 1 }));
        var several = ControlWords.ResumeAllLine(new ResumeAllResult(false)
        {
            PickedUp = 0,
            StoppedByHand = new ValueList<string>(["A", "B", "C"]),
            HeldByReserve = new ValueList<string>(["R"]),
            StillStopping = new ValueList<string>(["S"]),
        });
        Assert.Contains("“A”, “B” and “C” stay stopped, because you stopped them.", several, StringComparison.Ordinal);
        Assert.Contains("“R” is held by your reserve; its card resumes it.", several, StringComparison.Ordinal);
        Assert.Contains("“S” is still stopping; Resume all again once it has stopped.", several, StringComparison.Ordinal);
        Assert.Equal("Pause all (Tray: Pause all): 3 tasks pause at their next turn boundary; nothing is cut; new duties start paused",
            ControlWords.PauseAllLine("Tray: Pause all", 3));
    }

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-1")]
    public void The_toggle_shows_Pause_all_while_anything_can_pause_and_Resume_all_as_soon_as_every_live_task_is_pausing_or_paused()
    {
        // Any task that can still pause keeps Pause all.
        var mixed = ControlWords.Toggle([Of(MissionState.Working), Of(MissionState.Paused)], pauseLatched: false, stopHeld: false);
        Assert.Equal(GlobalToggleKind.PauseAll, mixed.Kind);
        Assert.True(mixed.CanPauseAll);
        Assert.True(mixed.CanResumeAll);
        // Every live task pausing or paused: Resume all (Resume also cancels a Pausing), before any turn has ended.
        var held = ControlWords.Toggle([Of(MissionState.Pausing), Of(MissionState.Paused), Of(MissionState.Done)], false, false);
        Assert.Equal(GlobalToggleKind.ResumeAll, held.Kind);
        Assert.False(held.CanPauseAll);
        Assert.True(held.CanResumeAll);
        // Needs you and Blocked can still pause.
        Assert.Equal(GlobalToggleKind.PauseAll, ControlWords.Toggle([Of(MissionState.NeedsYou), Of(MissionState.Paused)], false, false).Kind);
        Assert.Equal(GlobalToggleKind.PauseAll, ControlWords.Toggle([Of(MissionState.Blocked)], false, false).Kind);
        // Tasks Stop all stopped, a latch or a held scheduler also offer Resume all.
        Assert.Equal(GlobalToggleKind.ResumeAll, ControlWords.Toggle([Of(MissionState.Stopped), Of(MissionState.Stopping)], false, false).Kind);
        Assert.Equal(GlobalToggleKind.ResumeAll, ControlWords.Toggle([], pauseLatched: true, stopHeld: false).Kind);
        Assert.Equal(GlobalToggleKind.ResumeAll, ControlWords.Toggle([Of(MissionState.Done)], pauseLatched: false, stopHeld: true).Kind);
        // Nothing to do either way: Pause all, both buttons disabled.
        var idle = ControlWords.Toggle([Of(MissionState.Done), Of(MissionState.Failed)], false, false);
        Assert.Equal((GlobalToggleKind.PauseAll, false, false), (idle.Kind, idle.CanPauseAll, idle.CanResumeAll));
        // A reverted task waits for Restart, and a reserve's task for its card: neither is something Resume all would do.
        Assert.False(ControlWords.Toggle([Of(MissionState.Stopped, reverted: true)], false, false).CanResumeAll);
        Assert.False(ControlWords.Toggle([Of(MissionState.Paused, PauseCause.Reserve)], false, false).CanResumeAll);
    }

    [Fact]
    [Trait("Feature", "AS-6")]
    [Trait("Decision", "ED-NEW-dextra-as6-1")]
    public void The_card_words_and_lists_read_the_way_the_page_does()
    {
        Assert.Equal("send", ControlWords.HeldCardWord(ActionClass.Outward, DecisionKind.Approval));
        Assert.Equal("pay", ControlWords.HeldCardWord(ActionClass.Money, DecisionKind.Approval));
        Assert.Equal("delete", ControlWords.HeldCardWord(ActionClass.Destructive, DecisionKind.ToolCall));
        Assert.Equal("install", ControlWords.HeldCardWord(ActionClass.Install, DecisionKind.Approval));
        Assert.Equal("commit", ControlWords.HeldCardWord(ActionClass.Commitment, DecisionKind.Approval));
        Assert.Equal("merge", ControlWords.HeldCardWord(null, DecisionKind.Merge));
        Assert.Equal("", ControlWords.AndList([]));
        Assert.Equal("A", ControlWords.AndList(["A"]));
        Assert.Equal("A and B", ControlWords.AndList(["A", "B"]));
        Assert.Equal("A, B and C", ControlWords.AndList(["A", "B", "C"]));
        Assert.Equal("1 task", ControlWords.Plural(1, "task"));
        Assert.Equal("0 tasks", ControlWords.Plural(0, "task"));
    }
}
