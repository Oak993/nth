// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; work/ignis/PLAN.md unit U3). No Swift twin. One chat's runtime as
// Ignis sees it (IIgnisRuntimePort): whether it is between tasks, the hidden keep-warm ping turn, the silent keep-warm
// request of API sessions, and the silent tidy-up. ThreadRuntime.cs holds only the hooks (the ignisPingTurn flag beside
// skillsContinuationTurn, the CacheUsage and UsageLimit forwarding, the silent Compact origin); everything else is here.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime : IIgnisRuntimePort, IIgnisRuntimeTimeline
{
    // Ping turns of this runtime by turn id (the user item's id is "ignis:ping:" + the turn id, so the view can hide
    // the whole turn by TurnID and the mark survives restarts without a store of its own).
    private readonly HashSet<Guid> ignisPingTurns = [];
    private IIgnisController? ignisAttachedTo;
    private DateTimeOffset? ignisLastRequestAt;
    private bool ignisSilentCompact;

    Guid IIgnisRuntimePort.ThreadID => ThreadID;

    ProviderKind IIgnisRuntimePort.Provider => Thread?.Provider ?? ProviderKind.Claude;

    string? IIgnisRuntimePort.Model => Thread?.Model;

    DateTimeOffset? IIgnisRuntimePort.LastRequestAt => ignisLastRequestAt;

    bool IIgnisRuntimePort.CanKeepWarmSilently => session is { } live && IgnisSessionHooks.KeepWarm.Current(live) is not null;

    /// <summary>
    /// Between tasks: idle, nothing queued or waiting to send, no running heads or head work in flight, no open approval
    /// or question, not reverting, not archived, and not itself a Droppy-run Hydra head (a head's turns belong to its lead).
    /// </summary>
    internal bool IgnisIsBetweenTasks
    {
        get
        {
            if (Phase != RuntimePhase.Idle || IsReverting || ignisSilentCompact) return false;
            if (FollowUps.Count != 0 || pendingSend is not null) return false;
            if (Approvals.Count != 0 || Questions.Count != 0) return false;
            if (hydraPendingReports.Count != 0 || hydraBatches.Count != 0 || hydraWaiting.Count != 0) return false;
            if (App is not { } app || app.Thread(ThreadID) is not { } thread) return false;
            if (thread.IsArchived || thread.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy) return false;
            return !app.HydraTeam(ThreadID).Any(head => head.Hydra is { IsFinished: false });
        }
    }

    bool IIgnisRuntimePort.IsBetweenTasks => IgnisIsBetweenTasks;

    /// <summary>The turn being finished (App.TurnFinished runs inside FinishTurn) is a hidden keep-warm ping.</summary>
    internal bool IgnisFinishingPing => finishingTurnID is { } turn && ignisPingTurns.Contains(turn);

    /// <summary>A ping turn is starting or running now.</summary>
    internal bool IgnisPingRunning => currentTurnID is { } turn && ignisPingTurns.Contains(turn);

    /// <summary>Whether a turn of this runtime was a hidden ping.</summary>
    internal bool IgnisIsPingTurn(Guid turnID) => ignisPingTurns.Contains(turnID);

    bool IIgnisRuntimePort.SendHiddenPing()
    {
        if (!IgnisIsBetweenTasks || session is null) return false;
        ignisLastRequestAt = SwiftDate.Now;
        _ = MainActor.Spawn(StartIgnisPingTurn);
        return true;
    }

    private async Task StartIgnisPingTurn()
    {
        var previous = ignisPingTurn.Value;
        try
        {
            ignisPingTurn.Value = true;
            await EnsureLoaded();
            if (!IgnisIsBetweenTasks) return; // the user started something since the controller asked
            await StartTurn(IgnisWords.PingText, Array.Empty<Attachment>());
        }
        finally { ignisPingTurn.Value = previous; }
    }

    bool IIgnisRuntimePort.KeepWarmSilently()
    {
        if (!IgnisIsBetweenTasks || session is not { } live || IgnisSessionHooks.KeepWarm.Current(live) is not { } keepWarm) return false;
        ignisLastRequestAt = SwiftDate.Now;
        _ = MainActor.Spawn(async () =>
        {
            try { await keepWarm(); }
            catch (Exception) { /* a failed keep-warm is a cold cache later; the controller sees it as no usage */ }
            await MainActor.Run(() => IgnisController?.TurnFinished(ThreadID));
        });
        return true;
    }

    bool IIgnisRuntimePort.Tidy(int keepTurns)
    {
        if (!IgnisIsBetweenTasks || session is not { IsRunning: true }) return false;
        Compact(ignisKeepTurns: Math.Max(1, keepTurns));
        return ignisSilentCompact;
    }

    bool IIgnisRuntimeTimeline.IsTidying => ignisSilentCompact;

    void IIgnisRuntimeTimeline.AppendEvent(IgnisEvent @event)
    {
        if (entryIndex.ContainsKey(@event.TimelineItemID)) return;
        Append(new TimelineItem(TurnIDForNewRows, new TimelineItem.Content.Notice(new Notice
        {
            NoticeLevel = Notice.Level.Info,
            Message = IgnisWords.EventLine(@event),
        }), id: @event.TimelineItemID, date: @event.At));
        saveRevision++;
        ScheduleSave();
    }

    // ---- hooks called from ThreadRuntime.cs ----

    /// <summary>The ping turn's user item: an info notice whose id is "ignis:ping:" + the turn id.</summary>
    private TimelineItem IgnisPingItem(TimelineItem userItem, Guid turnID)
    {
        ignisPingTurns.Add(turnID);
        return userItem with
        {
            Id = IgnisEvent.PingPrefix + turnID.ToString("D"),
            TimelineItemContent = new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = IgnisWords.PingText }),
        };
    }

    private IIgnisController? IgnisController => IgnisSeams.Controller.Current();

    /// <summary>Attaches this runtime to the running controller once (again after the controller changed).</summary>
    internal IIgnisController? IgnisAttach()
    {
        if (IgnisController is not { } controller) return null;
        if (!ReferenceEquals(ignisAttachedTo, controller))
        {
            ignisAttachedTo = controller;
            controller.Attach(this);
        }
        return controller;
    }

    private void IgnisObserve(IgnisTurnUsage usage)
    {
        if (usage.AgentID is null) ignisLastRequestAt = SwiftDate.Now;
        IgnisAttach()?.Observe(ThreadID, usage);
    }

    private void IgnisUsageLimit(DateTimeOffset? resetsAt) => IgnisAttach()?.UsageLimitHit(ThreadID, resetsAt);

    /// <summary>How long a silent tidy-up waits for the provider's "Context compacted." before it counts as done.</summary>
    internal static readonly TimeSpan IgnisTidyNoticeWait = TimeSpan.FromMinutes(5);

    private long ignisTidyEpoch;

    private void IgnisTidyStarted()
    {
        ignisSilentCompact = true;
        ignisTidyEpoch++;
    }

    // The compact request returned. Claude and Codex compact after it returns and say so with "Context compacted." (the
    // swallowed notice ends the tidy-up); the API path says it before. A failure, or no notice in time, ends it too.
    private void IgnisTidyFinished(bool succeeded)
    {
        if (!ignisSilentCompact) return;
        if (!succeeded)
        {
            IgnisTidyEnded();
            return;
        }
        var epoch = ignisTidyEpoch;
        _ = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(IgnisTidyNoticeWait);
            if (ignisSilentCompact && epoch == ignisTidyEpoch) IgnisTidyEnded();
        });
    }

    private void IgnisTidyEnded()
    {
        ignisSilentCompact = false;
        ignisLastRequestAt = SwiftDate.Now;
        IgnisAttach()?.TurnFinished(ThreadID);
    }

    /// <summary>A silent tidy-up's provider notices are not shown; its "Context compacted." ends the tidy-up.</summary>
    private bool IgnisSwallowsNotice(Notice notice)
    {
        if (!ignisSilentCompact || notice.NoticeLevel != Notice.Level.Info) return false;
        if (notice.Message == "Compacting context.") return true;
        if (notice.Message != "Context compacted.") return false;
        IgnisTidyEnded();
        return true;
    }

    private static Task IgnisCompact(ProviderSession live, int keepTurns) => IgnisSessionHooks.Tidy.Current(live, keepTurns);
}
