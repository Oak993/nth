// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 4.1, 4.3, 10.4, 10.7). The engine's approval,
// confirm and show-window seams answered through the ask broker, so the pill's card and the chat answer the same entry.
// Each ask belongs to its step: when the engine stops waiting (the token it passes is cancelled) the ask is withdrawn,
// and a late click on it does nothing. The standing ("Always") grants stay with the app's grant store.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed class PcBrokerApprover : IPcApprover
{
    /// <summary>The reason a See-only answer gives a step that needed Use.</summary>
    public const string SeeOnly = "the user allowed Tactus only to see this app, not to click or type in it";

    private readonly PcAskBroker broker;
    private readonly Func<PcAppIdentity, PcCapability, PcApproval?> standing;
    private readonly PcGrants? grants;
    private readonly Lock gate = new();
    private readonly Dictionary<(string Thread, string App, PcCapability Capability), string> reasons = [];

    public PcBrokerApprover(PcAskBroker broker, Func<PcAppIdentity, PcCapability, PcApproval?> standing)
    {
        this.broker = broker;
        this.standing = standing;
    }

    /// <summary>With the grants store: "Always" grants are read from it, and every grant the card makes is written to it (ED-NEW-pcctl-25).</summary>
    public PcBrokerApprover(PcAskBroker broker, PcGrants grants)
        : this(broker, (grants ?? throw new ArgumentNullException(nameof(grants))).Standing)
    {
        this.grants = grants;
    }

    public PcApproval? Standing(PcAppIdentity app, PcCapability capability) => standing(app, capability);

    public string? Reason(string thread, PcAppIdentity app, PcCapability capability)
    {
        ArgumentNullException.ThrowIfNull(app);
        lock (gate) return reasons.Remove((thread, app.Key, capability), out var reason) ? reason : null;
    }

    public async Task<PcApproval> Ask(PcAskSource source, PcAppIdentity app, string appName, PcCapability capability,
        bool risky, bool allowAlways, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(app);
        // A chat grant made for another step covers this one (a See-only answer to a step that needed Use covers See).
        if (grants?.ForChat(source.Thread, app, capability) is { } granted) return granted;
        var ask = broker.Add(new PcAskDraft(PcAskKind.Approval, source.Thread, new PcApprovalAsk(capability, risky, allowAlways && source.Head is null))
        {
            App = app.Key,
            AppName = appName,
            Head = source.Head,
        }, cancellationToken);
        var outcome = await ask.Outcome.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Status != PcAskStatus.Answered) return PcApproval.TimedOut;
        var reply = outcome.Answer switch
        {
            PcApprovalReply r => r,
            PcApproval a => new PcApprovalReply(a, capability == PcCapability.Act),
            _ => null,
        };
        if (reply is null) return PcApproval.TimedOut;
        var approval = reply.Approval;
        if (approval is PcApproval.Chat or PcApproval.Always)
        {
            // A head's answer is never an "Always" grant (critique C14; its card never offers it).
            var scope = approval == PcApproval.Always && source.Head is null ? PcGrantScope.Always : PcGrantScope.Chat;
            grants?.Grant(source.Thread, app, appName, reply.Use, scope);
            if (capability == PcCapability.Act && !reply.Use)
            {
                Remember(source.Thread, app, capability, SeeOnly);
                return PcApproval.Denied;
            }
            return approval;
        }
        if (reply.Reason is { Length: > 0 } reason) Remember(source.Thread, app, capability, reason);
        return approval;
    }

    private void Remember(string thread, PcAppIdentity app, PcCapability capability, string reason)
    {
        lock (gate) reasons[(thread, app.Key, capability)] = reason;
    }
}

public sealed class PcBrokerConfirmer(PcAskBroker broker) : IPcConfirmer
{
    public async Task<bool> Confirm(PcAskSource source, string word, string elementName, string appName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        // The content rows come from the window's reader (DESIGN.md 10.4 table); until it is bound the card shows the action.
        var content = new PcConfirmContent(PcConfirmContent.CategoryOf(word), word, elementName, appName, []);
        var ask = broker.Add(new PcAskDraft(PcAskKind.Confirm, source.Thread, content) { AppName = appName, Head = source.Head }, cancellationToken);
        var outcome = await ask.Outcome.WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome is { Status: PcAskStatus.Answered, Answer: true };
    }

    public async Task<PcShowAnswer> Show(PcAskSource source, string appName, string windowTitle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var ask = broker.Add(new PcAskDraft(PcAskKind.ShowWindow, source.Thread, new PcShowAsk(windowTitle)) { AppName = appName, Head = source.Head }, cancellationToken);
        var outcome = await ask.Outcome.WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome is { Status: PcAskStatus.Answered, Answer: PcShowAnswer.Show } ? PcShowAnswer.Show : PcShowAnswer.NotNow;
    }
}
