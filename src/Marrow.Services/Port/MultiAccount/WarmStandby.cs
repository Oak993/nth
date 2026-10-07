// PORT-ONLY: OD-0116, ED-439. A warm start: the destination's process and session open early and receive nothing. No prompt exists, so nothing can act;
// the recorded context goes as the first real turn at the switch.
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed class WarmStandby
{
    private readonly ProviderSession session;
    private bool owned = true;
    private bool taken;

    private WarmStandby(ProviderSession session, SessionConfiguration configuration)
    {
        this.session = session;
        Configuration = configuration;
        session.OnEvent = Observe;
    }

    public SessionConfiguration Configuration { get; }
    public string SessionId { get; private set; } = "";

    /// <summary>The first thing the waiting session did on its own (an event name), or null. Any of it means the destination is not idle: it is stopped and the context goes the deferred way.</summary>
    public string? FaultReason { get; private set; }

    public bool IsHealthy => owned && FaultReason is null && session.IsRunning;

    public static async Task<WarmStandby> StartAsync(ProviderSession session, SessionConfiguration configuration, CancellationToken ct)
    {
        var warm = new WarmStandby(session, configuration);
        Task<string>? start = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            start = session.Start();
            warm.SessionId = await start.WaitAsync(ct);
            if (string.IsNullOrWhiteSpace(warm.SessionId)) throw new InvalidOperationException("The standby did not report its session identity.");
            if (warm.FaultReason is not null || !session.IsRunning) throw new InvalidOperationException("The standby exited while starting.");
            return warm;
        }
        catch
        {
            warm.Stop();
            if (start is { IsCompleted: false }) _ = StopLateStartAsync(start, session);
            throw;
        }
    }

    private static async Task StopLateStartAsync(Task<string> start, ProviderSession session)
    {
        try { await start; }
        catch { /* A failed start still releases whatever it opened. */ }
        finally { session.OnEvent = null; session.Stop(); }
    }

    /// <summary>Hands the live, idle session to the conversation. Only a healthy one can be taken.</summary>
    public ProviderSession Take()
    {
        if (!IsHealthy || taken) throw new InvalidOperationException("The warm session is not available.");
        taken = true;
        owned = false;
        session.OnEvent = null;
        return session;
    }

    /// <summary>Releases a session that was never taken. Safe to repeat; a taken session is left alone.</summary>
    public void Stop()
    {
        if (!owned) return;
        owned = false;
        session.OnEvent = null;
        session.Stop();
    }

    private void Observe(ProviderEvent value)
    {
        if (!owned) return;
        switch (value)
        {
            case ProviderEvent.Exited exit:
                Fault("exited: " + (exit.Error ?? "the session exited"));
                break;
            case ProviderEvent.TurnStarted or ProviderEvent.MessageDelta or ProviderEvent.MessageCompleted or ProviderEvent.ReasoningDelta
                or ProviderEvent.ReasoningCompleted or ProviderEvent.ToolStarted or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated
                or ProviderEvent.PlanDelta or ProviderEvent.PlanCompleted or ProviderEvent.Approval or ProviderEvent.Question
                or ProviderEvent.AgentStarted or ProviderEvent.AgentEvent:
                Fault(value.GetType().Name);
                break;
        }
    }

    private void Fault(string reason)
    {
        FaultReason ??= reason;
        Stop();
    }
}
