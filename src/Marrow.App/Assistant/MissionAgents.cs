// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.4 "How it spawns work"; unit AS-5; ED-NEW-dextra-as5-6). No Swift twin.
// The seam between the mission runner and nth's chats: the runner decides, this starts the chats and talks to them.
// AppMissionAgents is the real one over AppModel (NewThread/InsertThread with the MissionID, the capped mode and the
// workspace folder, then the brief through the chat's runtime); the tests drive the runner through fake provider chats.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A chat the runner starts for a mission: its lead, or its reviewer.</summary>
public sealed record AgentLaunch(Guid Mission, string AssistantId, string Title, string Brief, Guid ProjectId, string Folder, ProviderKind Provider)
{
    public string? Model { get; init; }

    /// <summary>The lead runs a Hydra team (Hydra on in its chat).</summary>
    public bool Team { get; init; }

    /// <summary>The reviewer: it only reads (the provider's plan mode, and every write it asks for is refused).</summary>
    public bool Reviewer { get; init; }

    /// <summary>A reader mission's lead (12.4 "Reader and actor split", AS-7): plan mode too, and it may only read.</summary>
    public bool Reader { get; init; }

    public string? Branch { get; init; }

    /// <summary>The account of the provider the chat runs on (the router's choice, AS-14); null: the provider's first enabled account.</summary>
    public Guid? AccountId { get; init; }
}

/// <summary>A head of a mission's lead, as Hydra reports it.</summary>
public sealed record AgentHead(Guid Thread, string Name, bool Finished, bool Stopped);

/// <summary>What the runner asks of nth's chats.</summary>
public interface IMissionAgents
{
    /// <summary>Runs work where the chats live (the app's main thread); at once when already there.</summary>
    void Post(Action work);

    /// <summary>Makes the chat (MissionID, capped mode, the workspace as its folder) and sends it the brief.</summary>
    Guid Start(AgentLaunch launch);

    /// <summary>Sends a message to the chat; false when it cannot take one now (a turn runs, or it is gone).</summary>
    bool Send(Guid thread, string text);

    bool TurnRunning(Guid thread);

    void Interrupt(Guid thread);

    /// <summary>Stops every Hydra head of the lead (StopAllHydraHeads).</summary>
    void StopHeads(Guid lead);

    /// <summary>Stops one Hydra head (StopHydraHead).</summary>
    void StopHead(Guid head);

    /// <summary>Ends the chat's provider session (its next message starts a new one).</summary>
    void StopSession(Guid thread);

    /// <summary>Answers a provider approval: the provider's once option, or its decline.</summary>
    void Resolve(Guid thread, ApprovalRequest request, bool approve);

    void Answer(Guid thread, QuestionRequest request, ValueMap<string, ValueList<string>> answers);

    /// <summary>The chat's last answer, for the reviewer and the results.</summary>
    string? FinalMessage(Guid thread);

    /// <summary>The lead's Hydra heads (HydraHeadInfo).</summary>
    IReadOnlyList<AgentHead> Heads(Guid lead);

    /// <summary>The models of a provider, for the reviewer's pick.</summary>
    IReadOnlyList<string> Models(ProviderKind provider);

    /// <summary>
    /// Shows a line in the chat (12.3 point 2: an approval nth answered for the owner is labelled where it was asked).
    /// A chat that is gone shows nothing.
    /// </summary>
    void Note(Guid thread, string text);

    /// <summary>
    /// Moves a live chat to another account of its provider (AS-14: the pause card's "Move the rest to ..."), bringing nth's own
    /// summary of it, at the chat's next safe point. False when it could not be moved; the chat then stays where it is.
    /// </summary>
    Task<bool> MoveAccount(Guid thread, Guid account);

    /// <summary>How many tokens of context the chat holds (what a cold start reads again at the uncached price); null when unknown.</summary>
    long? ContextTokens(Guid thread);

    /// <summary>What the chat is doing now in a few words ("generating the voiceover (3:42 of script)"); null when it says nothing.</summary>
    string? Activity(Guid thread);

    /// <summary>
    /// A head whose finish a Pause held (it paused at its turn boundary, 12.6b) is released: Hydra finishes it now, with
    /// the status its turn ended with, and tells the lead.
    /// </summary>
    void FinishHeld(Guid head, TurnStatus status);

    /// <summary>
    /// Starts a stopped head's chat again with <paramref name="text"/> (Resume on a head Stop cut): Hydra counts it as
    /// running again. False when the chat cannot take a message now.
    /// </summary>
    bool RestartHead(Guid head, string text);
}

/// <summary>The mission workspaces (12.6a), behind a seam so the runner's tests need no git.</summary>
public interface IMissionWorkspaces
{
    /// <summary>Makes the workspace; <paramref name="attempt"/> is 1, then one more for each Restart (a fresh branch and folder).</summary>
    Task<MissionWorkspace> Create(Guid mission, string title, string source, int attempt = 1);

    Task<MergeReview> Review(MissionWorkspace workspace);

    /// <summary>Merges exactly what <paramref name="reviewed"/> showed the owner (ED-NEW-dextra-as5-15).</summary>
    Task<MergeResult> Merge(MissionWorkspace workspace, MergeReview reviewed);

    Task<MergeResult> RebaseFirst(MissionWorkspace workspace);

    /// <summary>
    /// Removes the workspace. <paramref name="recordedSource"/> is the mission's own project folder as its record holds it; a workspace whose
    /// repository is not the repository of that folder is refused and nothing is deleted (ED-NEW-secfinal-26).
    /// </summary>
    Task Discard(MissionWorkspace workspace, string recordedSource);

    /// <summary>Revert changes (12.6b): the work comes out of the workspace and is kept, restorable, for 7 days.</summary>
    Task<RevertedWork> Revert(MissionWorkspace workspace, Guid mission, DateTimeOffset now, int changedFiles);

    /// <summary>
    /// What a Revert that nth's end interrupted already did (its hidden ref or set-aside copy), finished off; null when it did
    /// nothing yet. The crash guard of 12.6b: the next start completes the record instead of running the revert blind again.
    /// </summary>
    Task<RevertedWork?> FindReverted(MissionWorkspace workspace, Guid mission);

    /// <summary>Puts reverted work back where it was (the Activity log's restore, within 7 days).</summary>
    Task<MissionWorkspace> Restore(RevertedWork reverted, Guid mission);

    /// <summary>Forgets reverted work for good.</summary>
    Task DropReverted(RevertedWork reverted, Guid mission);
}

/// <summary>
/// The real workspaces, under the store's <c>worktrees/&lt;mission-id&gt;</c> (and <c>-2</c>, <c>-3</c> for a Restart's fresh
/// folder). <paramref name="folder"/> gives a mission's folder for an attempt. Revert, Restore and Discard act only on a
/// folder strictly under the worktrees folder, fully resolved, with no link on the way (the lean rules, item 10).
/// </summary>
public sealed class StoreMissionWorkspaces(Func<Guid, int, string> folder) : IMissionWorkspaces
{
    public Task<MissionWorkspace> Create(Guid mission, string title, string source, int attempt = 1) =>
        MissionWorkspaces.Create(mission, title, source, folder(mission, attempt), attempt: attempt);

    private string Root(Guid mission) => Path.GetDirectoryName(folder(mission, 1))!;

    private MissionWorkspace Checked(Guid mission, MissionWorkspace workspace)
    {
        if (!MissionWorkspaces.StrictlyUnder(Root(mission), workspace.Path))
            throw new MissionWorkspaceError("The mission's workspace is not under nth's own worktrees folder, so nth does not touch it.");
        return workspace;
    }

    public Task<RevertedWork> Revert(MissionWorkspace workspace, Guid mission, DateTimeOffset now, int changedFiles) =>
        MissionWorkspaces.Revert(Checked(mission, workspace), mission, now, changedFiles);

    public Task<RevertedWork?> FindReverted(MissionWorkspace workspace, Guid mission) =>
        MissionWorkspaces.FindReverted(Checked(mission, workspace), mission)!;

    public Task<MissionWorkspace> Restore(RevertedWork reverted, Guid mission) =>
        MissionWorkspaces.Restore(reverted with { Workspace = Checked(mission, reverted.Workspace) });

    public Task DropReverted(RevertedWork reverted, Guid mission)
    {
        Checked(mission, reverted.Workspace);
        // A set-aside copy is deleted from disk, so it too must lie under the worktrees folder.
        if (reverted.Workspace.Kind == MissionWorkspaceKind.Snapshot && !MissionWorkspaces.StrictlyUnder(Root(mission), reverted.Ref))
            throw new MissionWorkspaceError("The reverted copy is not under nth's own worktrees folder, so nth does not delete it.");
        return MissionWorkspaces.DropReverted(reverted);
    }

    public Task<MergeReview> Review(MissionWorkspace workspace) => MissionWorkspaces.Review(workspace);

    public Task<MergeResult> Merge(MissionWorkspace workspace, MergeReview reviewed) => MissionWorkspaces.Merge(workspace, reviewed);

    public Task<MergeResult> RebaseFirst(MissionWorkspace workspace) => MissionWorkspaces.RebaseFirst(workspace);

    public Task Discard(MissionWorkspace workspace, string recordedSource)
    {
        // ED-NEW-secfinal-22 (final security gate S4): Discard deletes a folder, so it too acts only on one strictly under the worktrees folder,
        // fully resolved, with no link on the way (Discard has no mission id; the worktrees folder is the same for every mission).
        if (!MissionWorkspaces.StrictlyUnder(Path.GetDirectoryName(folder(Guid.Empty, 1))!, workspace.Path))
            throw new MissionWorkspaceError("The mission's workspace is not under nth's own worktrees folder, so nth does not touch it.");
        return MissionWorkspaces.Discard(workspace, recordedSource);
    }
}

/// <summary>The real chats, over AppModel. Every call runs on the app's main thread.</summary>
public sealed class AppMissionAgents(AppModel app) : IMissionAgents
{
    public void Post(Action work)
    {
        if (MainActor.IsIsolated) work();
        else MainActor.Post(work);
    }

    public Guid Start(AgentLaunch launch)
    {
        var project = app.Project(launch.ProjectId) ?? throw new InvalidOperationException("The mission's project is gone.");
        var model = launch.Model ?? app.Providers.DefaultModel(launch.Provider)?.Id;
        var ownFolder = !string.Equals(Path.GetFullPath(launch.Folder).TrimEnd('\\', '/'), Path.GetFullPath(project.Path).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
        var thread = new ChatThread(project.Id, launch.Provider, model, null, SealedProviderConfig.CappedMode)
        {
            Title = launch.Title,
            HasCustomTitle = true,
            MissionID = launch.Mission,
            ProviderAccountID = launch.AccountId,
            AssistantID = Guid.TryParse(launch.AssistantId, out var assistant) ? assistant : null,
            WorktreePath = ownFolder ? launch.Folder : null,
            Branch = ownFolder ? launch.Branch : null,
            HydraEnabled = launch.Team,
            HydraEnabledIsExplicit = true,
            InteractionMode = launch.Reviewer || launch.Reader ? InteractionMode.Plan : InteractionMode.Build,
        };
        app.InsertThread(thread);
        app.Runtime(thread.Id).SendMissionMessage(launch.Brief);
        return thread.Id;
    }

    public bool Send(Guid thread, string text) => app.Thread(thread) is not null && app.Runtime(thread).SendMissionMessage(text);

    public bool TurnRunning(Guid thread) => app.ExistingRuntime(thread)?.IsRunning ?? false;

    public void Interrupt(Guid thread)
    {
        if (app.ExistingRuntime(thread) is { IsRunning: true } runtime) runtime.Interrupt();
    }

    public void StopHeads(Guid lead) => app.StopAllHydraHeads(lead);

    public void StopHead(Guid head) => app.StopHydraHead(head);

    public void StopSession(Guid thread) => app.ExistingRuntime(thread)?.StopSession();

    public void Resolve(Guid thread, ApprovalRequest request, bool approve)
    {
        var option = approve ? SealedProviderConfig.OnceOption(request) : SealedProviderConfig.DeclineOption(request);
        if (option is null || app.ExistingRuntime(thread) is not { } runtime) return;
        runtime.Resolve(request, option);
    }

    public void Answer(Guid thread, QuestionRequest request, ValueMap<string, ValueList<string>> answers) =>
        app.ExistingRuntime(thread)?.Answer(request, answers);

    public string? FinalMessage(Guid thread)
    {
        if (app.ExistingRuntime(thread) is not { } runtime) return null;
        return runtime.Entries.Select(entry => entry.Item.TimelineItemContent).Reverse()
            .OfType<TimelineItem.Content.Assistant>().FirstOrDefault()?.Value.Text;
    }

    public IReadOnlyList<AgentHead> Heads(Guid lead) =>
    [
        .. app.Threads.Where(t => t.ParentThreadID == lead && t.Hydra is not null)
            .Select(t => new AgentHead(t.Id, t.Hydra!.DisplayName, t.Hydra.IsFinished, t.Hydra.HydraHeadInfoStatus == HydraHeadInfo.Status.Stopped)),
    ];

    public IReadOnlyList<string> Models(ProviderKind provider) => [.. app.Providers.Models(provider).Select(m => m.Id)];

    public void Note(Guid thread, string text) => app.ExistingRuntime(thread)?.AppendDextraNote(text);

    public Task<bool> MoveAccount(Guid thread, Guid account) =>
        app.Thread(thread) is null ? Task.FromResult(false) : app.TryPinThreadAccountAsync(thread, account, Marrow.Core.Accounts.Bring.MarrowSummary);

    public long? ContextTokens(Guid thread) => app.ExistingRuntime(thread)?.Usage?.UsedTokens;

    public string? Activity(Guid thread) => app.ExistingRuntime(thread)?.HydraActivity is { Length: > 0 } activity ? activity : null;

    public void FinishHeld(Guid head, TurnStatus status)
    {
        if (app.Thread(head) is { } chat) app.DextraFinishHeldHead(chat, status);
    }

    public bool RestartHead(Guid head, string text) => app.Thread(head) is not null && app.DextraRestartHead(head, text);
}
