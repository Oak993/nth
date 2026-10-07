namespace Marrow.Core.Accounts;

/// <summary>
/// PORT-ONLY: OD-0116, lead decision 2026-10-06. A destination that cannot be primed in a verified text-only standby (every CLI except a Claude that passed its gate)
/// gets no standby: the Handshake waits for the source reply to end and the context goes with the destination's first real turn. No read-back, no gap fill, no resumed reply.
/// </summary>
public interface IDeferredDeliveryHost
{
    bool DeliversDeferred(SwitchTarget target);

    /// <summary>
    /// PORT-ONLY: ED-439. The same answer, after the host has checked what the destination CLI can do at run time (a cached, offline check), so the engine does not ask
    /// "finish the reply?" of a destination that will not continue it.
    /// </summary>
    ValueTask<bool> ResolveDeliversDeferredAsync(SwitchTarget target, CancellationToken ct) => ValueTask.FromResult(DeliversDeferred(target));
}
