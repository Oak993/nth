using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W19, what a chat holds for its MCP Apps (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.6
// "ui/message is attributed, never the user", "ui/update-model-context is visible, not silent"; ED-NEW-web-w19-2). No Swift
// counterpart. An app's message starts a turn of its own: the row says who sent it (UserMessage.AppSource), the model reads it
// wrapped as untrusted app content, it never runs a slash command and never counts as the user's turn for Hydra's budget.
// The apps' context chips ride along with the next message, wrapped the same way, and are then spent. The cards an app asks
// for (its domains, its messages, a tool, the camera) are the thread's approval cards: the same stack, the same buttons, and
// the answer goes back to the app instead of the provider.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private readonly Dictionary<string, Action<ApprovalRequest.Option?>> appPrompts = new(StringComparer.Ordinal);
    private readonly AsyncLocal<string?> appMessageTurn = new();
    private int appPromptCount;

    /// <summary>What the chat's apps asked the model to know (the chips above the composer).</summary>
    public McpAppModelContext AppContexts { get; } = new();

    /// <summary>What a chat busy with a turn tells an app that posts a message.</summary>
    public const string AppMessageBusy = "The chat is busy. Post again when the answer has finished.";

    /// <summary>True when tool calls in this chat ask first (anything but full access).</summary>
    public bool AppToolsNeedApproval => App?.Thread(ThreadID)?.RuntimeMode is not RuntimeMode.FullAccess;

    /// <summary>
    /// Starts a turn with an app's message (DESIGN 7.6): the row is the app's, the model reads it as untrusted. Null when it
    /// started; else why not (the chat is busy, archived or reverting).
    /// </summary>
    public string? PostAppMessage(string server, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        if (string.IsNullOrWhiteSpace(text)) return "The message is empty.";
        if (Phase != RuntimePhase.Idle || IsReverting || followUps.Count > 0) return AppMessageBusy;
        if (App?.Thread(ThreadID) is not { IsArchived: false }) return "This chat cannot take messages now.";
        _ = MainActor.Spawn(() => StartAppTurn(server, text));
        return null;
    }

    private async Task StartAppTurn(string server, string text)
    {
        var previous = appMessageTurn.Value;
        try
        {
            appMessageTurn.Value = server;
            await StartTurn(text, Array.Empty<Attachment>());
        }
        finally { appMessageTurn.Value = previous; }
    }

    /// <summary>The prompt of a turn: an app's message wrapped as untrusted, and the apps' context chips before it (spent).</summary>
    private string AppPrompt(string? appSource, string prompt, bool command)
    {
        if (appSource is not null) prompt = McpAppUntrusted.WrapMessage(appSource, prompt);
        if (command) return prompt;
        var contexts = AppContexts.Take();
        return contexts.Count == 0 ? prompt : McpAppUntrusted.WrapContexts(contexts) + "\n\n" + prompt;
    }

    /// <summary>Shows an app's card in the approval stack; the answer is the option chosen (null when the card went away).</summary>
    internal (string Id, Task<ApprovalRequest.Option?> Answer) AskForApp(ApprovalRequest.Kind kind, string title, IReadOnlyList<ApprovalRequest.Option> options, string? detail = null, string? reason = null)
    {
        var id = "mcp-app-" + Interlocked.Increment(ref appPromptCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var answer = new TaskCompletionSource<ApprovalRequest.Option?>(TaskCreationOptions.RunContinuationsAsynchronously);
        appPrompts[id] = option => answer.TrySetResult(option);
        Approvals = Approvals.Appending(new ApprovalRequest(id, kind, title, new ValueList<ApprovalRequest.Option>(options), detail, reason));
        return (id, answer.Task);
    }

    /// <summary>Withdraws every card an app is waiting on (its page went away): each answers as dismissed.</summary>
    internal void WithdrawAppPrompts(IEnumerable<string> ids)
    {
        foreach (var id in ids.ToArray())
        {
            if (!appPrompts.Remove(id, out var answer)) continue;
            Approvals = Approvals.Mutating(items => items.RemoveAll(item => item.Id == id));
            answer(null);
        }
    }

    // Resolve's first step: an app's card answers the app, never the provider (and leaves no handoff record).
    private bool ResolveAppPrompt(ApprovalRequest request, ApprovalRequest.Option option)
    {
        // The table is null in a runtime built without its initialisers (tests that skip the constructor); no app asked for anything there.
        if (appPrompts is null || !appPrompts.Remove(request.Id, out var answer)) return false;
        Approvals = Approvals.Mutating(items => items.RemoveAll(item => item.Id == request.Id));
        answer(option);
        return true;
    }

    // A turn's end clears the provider's cards; an app's cards stay until the user answers them.
    private ValueList<ApprovalRequest> AppPromptsOnly() =>
        appPrompts is null || appPrompts.Count == 0 ? ValueList<ApprovalRequest>.Empty : new ValueList<ApprovalRequest>(Approvals.Where(item => appPrompts.ContainsKey(item.Id)));
}
