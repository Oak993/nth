// PORT-ONLY: web surfaces W14, Tabula (DESIGN.md 6.3 "Ask to change ... Sending posts a turn in the main thread with the
// artifact attached as context", "Ask to fix, which posts a turn with the error and stack"; 6.5 "nth.send(text) puts text
// in the main composer as a draft. The user sends"; ED-NEW-web-w14-1). No Swift counterpart. The two ways the Tabula pane
// reaches its chat: a turn it posts (sent now when the chat is idle, queued as a follow-up while a turn runs, as the
// composer's own queue does), and a draft an artifact writes into the composer, which only the user sends.
using Marrow.Core.Port;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    /// <summary>Posts <paramref name="text"/> as the user's next turn: now when idle, else as a queued follow-up.</summary>
    public void SendArtifactTurn(string text)
    {
        text = FoundationText.Trim(text ?? "", includeNewlines: true);
        if (text.Length == 0 || IsReverting) return;
        if (CanQueue)
        {
            EnqueueFollowUp(text, []);
            return;
        }

        if (Phase != RuntimePhase.Idle) return;
        _ = MainActor.Spawn(async () =>
        {
            await EnsureLoaded();
            await StartTurn(text, []);
        });
    }

    /// <summary>An artifact's nth.send: the text joins the composer's draft (after a blank line when it has words); nothing is sent.</summary>
    public void DraftFromArtifact(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var current = Draft.Text;
        Draft = Draft with { Text = FoundationText.Trim(current, includeNewlines: true).Length == 0 ? text : current.TrimEnd() + "\n\n" + text };
    }
}
