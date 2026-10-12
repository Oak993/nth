// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6, 12.6b "Model"; unit AS-6; ED-NEW-dextra-as6-3, -4, -8). No Swift twin.
// The one service every task list calls (12.6b: "One service, MissionControls, serves all of them"): the Board's Missions tab,
// the home's Result list, Background tasks, the Tactus pill and the tray. Pause, Resume, Stop, Revert, Restore and Restart for
// a mission; Pause, Resume and Stop for each of its heads; and the three global moves: Pause all (a graceful hold, and a
// latch for duties that fire meanwhile), Resume all (every paused task and every task Stop all stopped, naming what it left
// alone) and Stop all. The work itself lives in MissionRunner and StopAll; this is the surface and the sentences.
using Marrow.App.Safety;
using Marrow.App.Tray;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Core.Port;

namespace Marrow.App.Assistant;

/// <summary>What Pause all did: how many tasks it asked to pause, and its line.</summary>
public sealed record PauseAllResult(int Tasks, string Line);

/// <summary>What Resume all did, and its line.</summary>
public sealed record ResumeAllReport(ResumeAllResult Result, string Line);

public sealed class MissionControls
{
    /// <summary>The tray's wording for Stop all (the approved page: "Stop Assistant").</summary>
    public const string TrayStopLabel = "Stop Assistant";

    private readonly MissionRunner runner;
    private readonly StopAll stop;
    private readonly SchedulerHold hold;
    private readonly DecisionQueue queue;

    public MissionControls(MissionRunner runner, StopAll stopAll, SchedulerHold hold, DecisionQueue queue)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        stop = stopAll ?? throw new ArgumentNullException(nameof(stopAll));
        this.hold = hold ?? throw new ArgumentNullException(nameof(hold));
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public MissionRunner Runner => runner;

    public StopAll Stopper => stop;

    public SchedulerHold Hold => hold;

    // ---- one task ----

    public string? Pause(Guid mission) => runner.Pause(mission);

    public string? Resume(Guid mission) => runner.Resume(mission);

    /// <summary>Stop in any live state, including Pausing and Paused (12.6b).</summary>
    public string? Stop(Guid mission) => runner.Stop(mission);

    public Task<string?> Revert(Guid mission) => runner.Revert(mission);

    public Task<string?> RestoreReverted(Guid mission) => runner.RestoreReverted(mission);

    public string? Restart(Guid mission) => runner.Restart(mission);

    // ---- one head ----

    public string? PauseHead(Guid mission, Guid head) => runner.PauseHead(mission, head);

    public string? ResumeHead(Guid mission, Guid head) => runner.ResumeHead(mission, head);

    public string? StopHead(Guid mission, Guid head) => runner.StopHead(mission, head);

    // ---- everything ----

    /// <summary>
    /// Pause all (12.6b): every task that can pause pauses at its next turn boundary; nothing is cut. It is a latch: a duty that
    /// fires while it is on starts paused. Irreversible cards are held, as for any pause.
    /// </summary>
    public PauseAllResult PauseAll(string source = "Pause all")
    {
        hold.LatchPause();
        var count = 0;
        foreach (var record in runner.Missions.Where(r => MissionStates.CanPause(r.Mission.State)))
        {
            if (runner.Pause(record.Id, PauseCause.PauseAll) is null) count++;
        }
        return new PauseAllResult(count, ControlWords.PauseAllLine(source, count));
    }

    /// <summary>
    /// Resume all (12.6b, r4 B6): resumes every paused task and every task that Stop all stopped, and re-raises held decisions.
    /// It releases the scheduler and the latch. It never resumes a task the owner stopped by hand, a head he stopped on its
    /// own, a reverted task (it waits for Restart) or a task a reserve holds (its card does that), and it names what it left.
    /// </summary>
    public ResumeAllReport ResumeAll()
    {
        var afterStop = hold.ReleaseAll();
        stop.Clear();
        var picked = 0;
        var backToNeedsYou = new List<string>();
        var byHand = new List<string>();
        var headsByHand = new List<string>();
        var held = new List<string>();
        var stillStopping = new List<string>();
        var reverted = 0;
        foreach (var record in runner.Missions)
        {
            var m = record.Mission;
            if (MissionMachine.ResumedByResumeAll(m))
            {
                var outcome = runner.ResumeCore(record.Id, all: true);
                if (outcome.Refused is not null)
                {
                    // The stop key is taken, or the like: it stays as it was and is named.
                    byHand.Add(m.Title);
                    continue;
                }
                picked++;
                if (outcome.HeldWord is { } word) backToNeedsYou.Add(word);
                headsByHand.AddRange(outcome.HeadsLeftStopped);
                continue;
            }
            switch (m.State)
            {
                case MissionState.Stopping:
                    stillStopping.Add(m.Title);
                    break;
                case MissionState.Paused or MissionState.Stopped when m.PausedBy == PauseCause.Reserve:
                    held.Add(m.Title);
                    break;
                case MissionState.Stopped when m.Reverted:
                    reverted++;
                    break;
                case MissionState.Stopped when m.StoppedAfterFailure:
                    break;
                case MissionState.Stopped:
                    byHand.Add(m.Title);
                    break;
                case not (MissionState.Done or MissionState.Failed):
                    headsByHand.AddRange(m.Heads.Where(h => h.State == HeadState.Stopped && h.StoppedByHand).Select(h => $"{h.Name} in {ControlWords.Quote(m.Title)}"));
                    break;
            }
        }
        var result = new ResumeAllResult(afterStop)
        {
            PickedUp = picked,
            BackToNeedsYou = new ValueList<string>(backToNeedsYou),
            StoppedByHand = new ValueList<string>(byHand),
            HeadsStoppedByHand = new ValueList<string>(headsByHand),
            Reverted = reverted,
            HeldByReserve = new ValueList<string>(held),
            StillStopping = new ValueList<string>(stillStopping),
        };
        return new ResumeAllReport(result, ControlWords.ResumeAllLine(result));
    }

    /// <summary>Stop all (12.6): the one entry the header, the sidebar row, the tray and voice call. It never asks.</summary>
    public StopReport StopAll(StopSource from = StopSource.Header) => stop.Press(from);

    /// <summary>The header, tray and voice toggle: Pause all, or Resume all as soon as every live task is pausing or paused.</summary>
    public GlobalToggle Toggle => ControlWords.Toggle(runner.Missions.Select(r => r.Mission), hold.PauseLatched, hold.StopHeld);

    /// <summary>The owner's answer to a card. While its task is paused a reversible answer is kept and acts on Resume.</summary>
    public string? Answer(Guid card, DecisionAnswer answer) => queue.Answer(card, answer);

    // ---- the tray (7.4) ----

    /// <summary>What the Assistant puts in the tray menu: Stop, and Pause all or Resume all (the page's order).</summary>
    public IReadOnlyList<TrayItem> TrayItems()
    {
        var toggle = Toggle;
        return
        [
            new TrayItem("assistant.stop", TrayStopLabel, () => StopAll(StopSource.Tray)),
            toggle.Kind == GlobalToggleKind.ResumeAll
                ? new TrayItem("assistant.resume", ControlWords.ResumeAllLabel, () => ResumeAll())
                : new TrayItem("assistant.pause", ControlWords.PauseAllLabel, () => PauseAll("Tray: Pause all")),
        ];
    }
}
