// PORT-ONLY: OD-0114; ED-403. Capture raw output in memory; multi-account threads flush by batch.
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using System.Text;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    // Created on first use, so a runtime built without its field initializers (parity fixtures) still buffers.
    private List<string>? pendingHandoffEventList;
    private List<string> pendingHandoffEvents => pendingHandoffEventList ??= [];
    private long pendingHandoffBytes;
    private Dictionary<string, long>? rawHandoffToolBytesMap;
    private Dictionary<string, long> rawHandoffToolBytes => rawHandoffToolBytesMap ??= new(StringComparer.Ordinal);
    private bool handoffJournalInitialized;
    private bool handoffMessageBoundary;
    private bool requestedHandoffJournal;

    private bool UsesHandoffJournal => requestedHandoffJournal || Thread?.PillID is not null
        || Thread is { } thread && App?.Settings.Accounts.ForService(thread.Provider).Count(account => account.Enabled) >= 2;

    // A snapshot of a running reply can be saved between messages. It does not pause the provider or authorize adoption.
    private bool IsHandshakeCheckpointSafe => handoffMessageBoundary && Phase == RuntimePhase.Running && !IsLoadingHistory && !IsReverting
        && pendingSend is null && closingTurnID is null && finishingTurnID is null && !isStartingSession
        && Approvals.Count == 0 && Questions.Count == 0 && !HasWorkingHeads
        && hydraPendingReports.Count == 0 && hydraBatches.Count == 0 && hydraWaiting.Count == 0
        && !hydraFlushScheduled && HydraLanding is null && !IsHydraMerging
        && commandTrees.Count == 0 && commandSettles.Count == 0 && hydraCommandTrees.Count == 0
        && !Entries.Any(entry => entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool && tool.Value.ToolCallStatus == ToolCall.Status.Running);

    private void CaptureHandoff(ProviderEvent value)
    {
        var preserve = value is ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.RequestResolved;
        switch (value)
        {
            case ProviderEvent.ToolStarted tool:
                rawHandoffToolBytes[tool.Id] = Encoding.UTF8.GetByteCount(tool.Call.Output);
                preserve |= rawHandoffToolBytes[tool.Id] > ToolCall.OutputLimit;
                break;
            case ProviderEvent.ToolOutput output:
                rawHandoffToolBytes[output.Id] = rawHandoffToolBytes.GetValueOrDefault(output.Id) + Encoding.UTF8.GetByteCount(output.Text);
                preserve |= rawHandoffToolBytes[output.Id] > ToolCall.OutputLimit;
                break;
            case ProviderEvent.ToolUpdated { Update.Output: { } text } tool:
                rawHandoffToolBytes[tool.Id] = Encoding.UTF8.GetByteCount(text);
                preserve |= rawHandoffToolBytes[tool.Id] > ToolCall.OutputLimit;
                break;
        }
        BufferHandoff(HandoffJournal.Encode(value), preserve);
        if (value is ProviderEvent.TurnCompleted or ProviderEvent.Exited) rawHandoffToolBytes.Clear();
    }

    private void BufferHandoff(string? line, bool preserve = false)
    {
        if (line is null) return;
        // Keep a sticky spool once a second account, an approval, display cleanup or clipping makes raw history necessary.
        requestedHandoffJournal |= preserve || UsesHandoffJournal || line.Contains('—', StringComparison.Ordinal);
        pendingHandoffEvents.Add(line);
        pendingHandoffBytes += Encoding.UTF8.GetByteCount(line) + 1L;
        if (pendingHandoffBytes >= 262_144)
        {
            requestedHandoffJournal = true;
            FlushHandoffJournal();
        }
    }

    // An earlier journal on disk keeps the spool sticky. Only a flush or a read needs to know, so a thread that never spools never touches the store.
    private void ProbeHandoffJournal()
    {
        if (handoffJournalInitialized) return;
        handoffJournalInitialized = true;
        requestedHandoffJournal |= File.Exists(HandoffJournal.PathIn(HandoffFolder));
    }

    private void FlushHandoffJournal(bool explicitCheckpoint = false)
    {
        requestedHandoffJournal |= explicitCheckpoint;
        if (pendingHandoffEvents.Count == 0) return;
        ProbeHandoffJournal();
        if (!UsesHandoffJournal) return;
        HandoffJournalWriter.Append(HandoffFolder, pendingHandoffEvents); // ED-NEW-perf-ui-thread-5: handed to the journal's own queue (it takes a copy); the disk is not the UI thread's
        pendingHandoffEvents.Clear();
        pendingHandoffBytes = 0;
    }

    private string[] ReadHandoffJournal()
    {
        ProbeHandoffJournal();
        return [.. HandoffJournal.Read(HandoffFolder), .. pendingHandoffEvents];
    }
}
