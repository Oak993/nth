// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3, 14 W25; ED-NEW-web-w25-1). Program.Main's one Bind line: from here every chat
// session is offered the ask_user tool (the nth-ui server for CLI providers, a native tool for API providers). A call finds its chat's
// runtime on the main actor when it asks; a chat with no runtime (it was closed) cannot show the card, and the call is detached.
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;

namespace Marrow.App.Port.AskUser;

public static class AskUserStartup
{
    public static void Bind() =>
        AskUserDirectory.Shared.SetProcessDefault(new AskUserDirectory(thread => new ChatAskHost(thread)));

    /// <summary>One chat's host: its runtime is looked up when a call asks, since a tool call arrives on a worker thread.</summary>
    private sealed class ChatAskHost(Guid thread) : IAskUserHost
    {
        public Task<AskOutcome> Ask(AskCard card, CancellationToken cancellation) =>
            MainActor.Invoke(() =>
                Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(thread) is { } runtime
                    ? runtime.AskUser(card, cancellation)
                    : Task.FromResult(new AskOutcome(AskOutcomeKind.Detached, card)));
    }
}
