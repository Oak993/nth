// PORT-ONLY: OD-0114; ED-403. Streaming checkpoints are read-only unless the provider owns a paused step.
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Store;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    public bool IsAccountSwitchSafe => !isStartingSession && IsAccountWorkSettled;
    private bool IsAccountWorkSettled => (Phase == RuntimePhase.Idle || OwnsAccountStepPause) && !IsLoadingHistory && !IsReverting && pendingSend is null
        && closingTurnID is null && finishingTurnID is null
        && Approvals.Count == 0 && Questions.Count == 0 && !HasWorkingHeads
        && hydraPendingReports.Count == 0 && hydraBatches.Count == 0 && hydraWaiting.Count == 0
        && !hydraFlushScheduled && HydraLanding is null && !IsHydraMerging
        && commandTrees.Count == 0 && commandSettles.Count == 0 && hydraCommandTrees.Count == 0
        && !Entries.Any(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool && tool.Value.ToolCallStatus == ToolCall.Status.Running);

    public HandoffSummary SaveHandshakeCheckpoint()
    {
        FlushHandoffJournal(explicitCheckpoint: true);
        var journal = ReadHandoffJournal();
        var history = HandoffJournal.Restore(SnapshotForPersistence(), journal);
        var diff = string.Join("\n", history.Turns.Where(turn => turn.ProviderDiff is not null).Select(turn => turn.ProviderDiff)
            .Append(currentProviderDiff).Where(patch => !string.IsNullOrWhiteSpace(patch)));
        return HandoffStore.Save(new HandoffContext(history, Thread?.Title ?? "", diff)
        {
            EventJournal = journal,
            IsQuiescent = IsAccountSwitchSafe,
        }, HandoffFolder);
    }
}
