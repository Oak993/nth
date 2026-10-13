using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W6, a side chat's runtime (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.9; ED-NEW-web-w6-1). No Swift
// counterpart. A side chat is a thread with its own runtime and provider session, so nothing it does queues behind, interrupts or
// waits for the main run. What differs from a main thread is small and lives here, called from three places in StartTurn and one in the
// provider-event switch: its message is sent without the chat's slash commands, every turn starts in a fresh provider session carrying
// the seed of the moment (AppModel.SideChatTurnPrompt), a read-only side chat takes no git checkpoint, and a read-only side chat declines
// any request to run a command, change a file or take more access.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private TaskHandle? sideChatTurnTask;
    private bool sideChatTurnPending;
    public bool IsSideChatTurnPending { get => Read(sideChatTurnPending); private set => Set(ref sideChatTurnPending, value); }

    /// <summary>
    /// Sends the person's message to the model as the side chat's next turn. Unlike <see cref="Send"/> it takes no local command (a
    /// side chat has no /plan or /compact: Can act is its one mode switch) and no attachment. False when the side chat is busy or the
    /// message is empty; the composer then keeps the text.
    /// </summary>
    public bool SendSideChat(string text)
    {
        text = FoundationText.Trim(text ?? "", includeNewlines: true);
        if (text.Length == 0 || IsReverting || Phase != RuntimePhase.Idle || IsSideChatTurnPending || InstallerHoldsNewTurns) return false;
        if (Thread is { IsSideChat: true } side && App?.CanStartSideChatTurn(side) != true) return false;
        IsSideChatTurnPending = true;
        sideChatTurnTask = SpawnInstallerTurn(() => StartSideChatTurn(text));
        if (sideChatTurnTask is null) { IsSideChatTurnPending = false; return false; }
        Draft = new ComposerDraft();
        return true;
    }

    internal void StopSideChatTurn()
    {
        sideChatTurnTask?.Cancel();
        if (IsRunning) Interrupt();
    }

    private async Task StartSideChatTurn(string text)
    {
        try
        {
            if (SwiftTask.IsCancelled) return;
            if (Thread is { IsSideChat: true } side && App is { } app)
            {
                if (!app.CanStartSideChatTurn(side)) return;
                if (side.MissionID is not null)
                {
                    var dextra = app.Dextra;
                    await app.DextraAdmitHelper(side, SwiftTask.CancellationToken);
                    await EnsureLoaded();
                    if (SwiftTask.IsCancelled || app.Thread(ThreadID) is not { } current || !app.CanStartSideChatTurn(current))
                    {
                        dextra?.Gate.Leave(side.Id);
                        return;
                    }
                }
            }
            await StartTurn(text, Array.Empty<Attachment>());
        }
        finally { sideChatTurnTask = null; IsSideChatTurnPending = false; }
    }

    /// <summary>A read-only side chat (plan mode): nothing it does changes the project, so a turn needs no checkpoint to revert to.</summary>
    private static bool IsReadOnlySideChat(ChatThread thread) => thread is { IsSideChat: true, InteractionMode: InteractionMode.Plan };

    /// <summary>
    /// Every side-chat turn starts in a fresh provider session (DESIGN 3.9 "seeds fresh context each turn"): the session of the turn
    /// before is stopped and forgotten, so the old snapshots it was given never pile up. The turn carries its own conversation so far.
    /// </summary>
    private void BeginFreshSideChatSession(AppModel app)
    {
        ReleaseSession(stop: true);
        app.UpdateThread(ThreadID, current => current with { ProviderSessionID = null, ProviderResumeAt = null });
    }

    /// <summary>The text this side-chat turn is sent with: the rules, the snapshot of the main thread now, the conversation so far and the question.</summary>
    private async Task<string> SideChatTurnText(string question)
    {
        if (App is not { } app) return question;
        return await app.SideChatTurnPrompt(ThreadID, question, currentTurnID) ?? question;
    }

    /// <summary>
    /// A read-only side chat answers a request to run a command, change a file, or take more access with a refusal at once and says so in
    /// the thread; the person's own decision (Can act) is the only way those happen. A request to use a tool (read the web, an MCP tool) is
    /// the person's to answer, as anywhere. True when <paramref name="request"/> was refused here.
    /// </summary>
    private bool DeclinesInReadOnlySideChat(ApprovalRequest request)
    {
        if (Thread is not { } thread || !IsReadOnlySideChat(thread) || request.RequestKind == ApprovalRequest.Kind.Tool) return false;
        var refusal = request.Options.FirstOrDefault(option => option.OptionRole == ApprovalRequest.Option.Role.Decline)
            ?? request.Options.FirstOrDefault(option => option.OptionRole == ApprovalRequest.Option.Role.Cancel);
        if (refusal is null || session is null) return false;
        session.ResolveApproval(request.Id, refusal.Id);
        var declined = request.RequestKind switch
        {
            ApprovalRequest.Kind.Command => UiText.Get("sidechat.declined.command"),
            ApprovalRequest.Kind.FileChange => UiText.Get("sidechat.declined.file_change"),
            ApprovalRequest.Kind.Plan => UiText.Get("sidechat.declined.plan"),
            _ => UiText.Get("sidechat.declined.access"),
        };
        AppendNotice(Notice.Level.Info, declined);
        return true;
    }
}
