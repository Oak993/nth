// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 4.1, 4.3, 10.4, 10.7). The engine's approval,
// confirm and show-window seams answered through the ask broker, so the pill's card and the chat answer the same entry.
// Each ask belongs to its step: when the engine stops waiting (the token it passes is cancelled) the ask is withdrawn,
// and a late click on it does nothing. The standing ("Always") grants stay with the app's grant store.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed class PcBrokerApprover(PcAskBroker broker, Func<PcAppIdentity, PcCapability, PcApproval?> standing) : IPcApprover
{
    public PcApproval? Standing(PcAppIdentity app, PcCapability capability) => standing(app, capability);

    public async Task<PcApproval> Ask(PcAskSource source, PcAppIdentity app, string appName, PcCapability capability,
        bool risky, bool allowAlways, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(app);
        var ask = broker.Add(new PcAskDraft(PcAskKind.Approval, source.Thread, new PcApprovalAsk(capability, risky, allowAlways && source.Head is null))
        {
            App = app.Key,
            AppName = appName,
            Head = source.Head,
        }, cancellationToken);
        var outcome = await ask.Outcome.WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome is { Status: PcAskStatus.Answered, Answer: PcApproval answer } ? answer : PcApproval.TimedOut;
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
