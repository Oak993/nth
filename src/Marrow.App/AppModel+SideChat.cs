using Marrow.App.Runtime;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

// PORT-ONLY: web surfaces W6, the side chat (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.9; ED-NEW-web-w6-1). No Swift counterpart.
// A side chat is a thread of its own under the main thread (ParentThreadID, IsInPanel, IsSideChat): its own ThreadRuntime and provider
// session, so it never blocks, queues behind or interrupts the main run, saved with the library like any thread, and listed nowhere
// (the sidebar, the palette and the subagent panel skip a panel child that is not a head or a subagent). What it knows of the main
// thread it is given at each turn, as a snapshot (SideChatSeed) that reads the main thread and writes nothing to it.
//
// Read-only is the default: plan mode, supervised, and a runtime that declines any request to run, change or build (the runtime's
// DeclinesInReadOnlySideChat). Can act gives the side chat the main thread's tools: build mode with the main thread's run mode.
// It ends when the pane closes, so a side chat never works where no one can see it.

namespace Marrow.App;

public sealed partial class AppModel
{
    /// <summary>The title of a side chat's thread record.</summary>
    public static string SideChatTitle => UiText.Get("sidechat.title");

    private readonly Dictionary<Guid, SideChatDigest> sideChatDigests = [];
    private readonly Dictionary<Guid, int> sideChatSnapshotCounts = [];

    /// <summary>The side chat of a thread, if it has one.</summary>
    public ChatThread? SideChat(Guid parentID) =>
        Children(parentID).FirstOrDefault(thread => thread.IsSideChat && !thread.IsArchived);

    /// <summary>
    /// The side chat of a thread, made now when it has none: the main thread's provider and model, plan mode (read-only) and supervised.
    /// Null for a thread that cannot have one (no such thread, a helper or a head, a side chat itself).
    /// </summary>
    public ChatThread? EnsureSideChat(Guid parentID)
    {
        if (SideChat(parentID) is { } existing) return existing;
        if (Thread(parentID) is not { IsHelper: false, IsInPanel: false } parent) return null;
        var side = new ChatThread(parent.ProjectID, parent.Provider, parent.Model, parent.Effort, RuntimeMode.Supervised, parent.FastMode)
        {
            ParentThreadID = parentID,
            IsInPanel = true,
            IsSideChat = true,
            Title = SideChatTitle,
            HasCustomTitle = true,
            InteractionMode = InteractionMode.Plan,
            HydraEnabled = false,
            HydraEnabledIsExplicit = true,
            WorktreePath = parent.WorktreePath,
            Branch = parent.Branch,
            ProviderAccountID = parent.ProviderAccountID,
            MissionID = parent.MissionID,
            AssistantID = parent.MissionID is null ? null : parent.AssistantID,
        };
        // ED-NEW-finalint-4: a mission's panel chat is one of its registered agents too.
        if (side.MissionID is not null && (!SealedProviderConfig.CanSeal(side.Provider) || !DextraHelperAdded(parent, side))) return null;
        InsertThread(side);
        return side;
    }

    /// <summary>Whether the side chat may act with the main thread's tools (the header's Can act). Off when there is no side chat.</summary>
    public bool SideChatCanAct(Guid parentID) => SideChat(parentID) is { InteractionMode: InteractionMode.Build };

    /// <summary>
    /// The Can act toggle. On: build mode with the main thread's run mode, from the next turn. Off: plan mode and supervised at once, and a
    /// turn that is running with the tools is stopped, so off means off.
    /// </summary>
    public void SetSideChatCanAct(Guid parentID, bool canAct)
    {
        if (EnsureSideChat(parentID) is not { } side || Thread(parentID) is not { } parent) return;
        if (!canAct && side.InteractionMode == InteractionMode.Build) ExistingRuntime(side.Id)?.StopSideChatTurn();
        UpdateThread(side.Id, current => current with
        {
            InteractionMode = canAct ? InteractionMode.Build : InteractionMode.Plan,
            RuntimeMode = canAct && parent.MissionID is null ? parent.RuntimeMode : RuntimeMode.Supervised,
        });
    }

    /// <summary>
    /// Sends the person's message as the side chat's next turn. The side chat's run mode follows the main thread's while it can act. False
    /// when it is busy or the message is empty.
    /// </summary>
    public bool SendSideChat(Guid parentID, string text)
    {
        if (EnsureSideChat(parentID) is not { } side || !CanStartSideChatTurn(side)) return false;
        if (side.InteractionMode == InteractionMode.Build && Thread(parentID) is { } parent)
        {
            var mode = parent.MissionID is null ? parent.RuntimeMode : SealedProviderConfig.CappedMode;
            if (side.RuntimeMode != mode) UpdateThread(side.Id, current => current with { RuntimeMode = mode });
        }
        return Runtime(side.Id).SendSideChat(text);
    }

    // A saved side conversation may outlive Stop or Restart; it cannot revive a finished mission's agent.
    internal bool CanStartSideChatTurn(ChatThread side)
    {
        if (side.ParentThreadID is not { } parentID || Thread(parentID) is not { } parent) return false;
        if (parent.MissionID is null && side.MissionID is null) return true;
        return parent.MissionID is { } mission && side.MissionID == mission
            && Dextra?.Runner.MissionOf(side.Id) is { } record && record.Id == mission
            && (MissionStates.IsRunning(record.Mission.State) || record.Mission.State == MissionState.NeedsYou);
    }

    /// <summary>
    /// Clears the side chat: its turn (if any) is stopped and its conversation is deleted with its record, and a new, empty one takes its
    /// place (the pane shows the new one). The Can act choice is kept.
    /// </summary>
    public void ClearSideChat(Guid parentID)
    {
        if (SideChat(parentID) is not { } side) return;
        var canAct = side.InteractionMode == InteractionMode.Build;
        sideChatSnapshotCounts.Remove(side.Id);
        ExistingRuntime(side.Id)?.StopSideChatTurn();
        Delete(side.Id);
        EnsureSideChat(parentID);
        if (canAct) SetSideChatCanAct(parentID, true);
    }

    /// <summary>Stops the side chat's running turn, if there is one.</summary>
    public void StopSideChat(Guid parentID)
    {
        if (SideChat(parentID) is { } side) ExistingRuntime(side.Id)?.StopSideChatTurn();
    }

    /// <summary>
    /// Side chat closed: what Can act allowed ends with it. A turn that was acting is stopped and the toggle is off the next time it opens.
    /// A read-only turn goes on, and its answer is there when the pane opens again.
    /// </summary>
    internal void SideChatClosed(Guid parentID)
    {
        if (SideChat(parentID) is not { InteractionMode: InteractionMode.Build } side) return;
        ExistingRuntime(side.Id)?.StopSideChatTurn();
        UpdateThread(side.Id, current => current with { InteractionMode = InteractionMode.Plan, RuntimeMode = RuntimeMode.Supervised });
    }

    /// <summary>
    /// Send to main: puts <paramref name="text"/> in the main thread's composer as a draft, after what is already typed there. It never
    /// sends anything. False when there is nothing to put.
    /// </summary>
    public bool SendSideChatToMain(Guid parentID, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || Thread(parentID) is null) return false;
        var main = Runtime(parentID);
        var typed = main.Draft.Text.TrimEnd();
        main.Draft = main.Draft with { Text = typed.Length == 0 ? text : typed + "\n\n" + text };
        return true;
    }

    /// <summary>
    /// What a side-chat turn is sent with (called by the side chat's runtime as the turn starts): the rules, the snapshot of the main
    /// thread as it is now, the side chat's own earlier exchanges and the question. Reads the main thread; writes nothing to it. Null for
    /// a thread that is not a side chat or whose main thread is gone.
    /// </summary>
    internal async Task<string?> SideChatTurnPrompt(Guid sideID, string question, Guid? currentTurn)
    {
        if (Thread(sideID) is not { IsSideChat: true, ParentThreadID: { } parentID } side || Thread(parentID) is not { } parent) return null;
        var main = Runtime(parentID);
        await main.EnsureLoaded();
        if (SwiftTask.IsCancelled || Thread(sideID) is not { IsSideChat: true } currentSide || currentSide.ParentThreadID != parentID
            || Thread(parentID) is not { } currentParent || !CanStartSideChatTurn(currentSide)) return null;
        side = currentSide;
        parent = currentParent;
        DextraHelperPrompted(parent, side);
        if (!sideChatDigests.TryGetValue(parentID, out var digest)) sideChatDigests[parentID] = digest = new SideChatDigest();
        var project = Project(parent.ProjectID);
        var snapshot = SideChatSeed.Build(new SideChatMain(
            parent.Title,
            main.Phase switch { RuntimePhase.Starting => SideChatMainPhase.Starting, RuntimePhase.Running => SideChatMainPhase.Running, _ => SideChatMainPhase.Idle },
            main.Entries.Select(entry => entry.Item).ToList(),
            main.Turns.ToList(),
            main.Approvals.Count + main.Questions.Count,
            main.IsWaitingForAnswer,
            project?.Name,
            parent.Branch,
            parent.WorktreePath ?? project?.Path), digest);
        var own = Runtime(sideID);
        var earlier = SideChatPrompt.Exchanges(own.Entries.Select(entry => entry.Item).ToList(), own.Turns.ToList(), currentTurn);
        sideChatSnapshotCounts[sideID] = sideChatSnapshotCounts.GetValueOrDefault(sideID) + 1;
        return SideChatPrompt.Build(question, snapshot, sideChatSnapshotCounts[sideID], earlier, side.InteractionMode == InteractionMode.Build);
    }

    // A side chat's turn ends quietly: no chime, no notification, no unread mark (it has no row), and its provider session is let go,
    // since the next turn starts a fresh one.
    private void SideChatTurnFinished(Guid id, TurnStatus status)
    {
        UpdateThread(id, current => current with { LastStatus = status, UpdatedAt = SwiftDate.Now });
        sessionsToRelease.Remove(id);
        ExistingRuntime(id)?.StopSession();
    }
}
