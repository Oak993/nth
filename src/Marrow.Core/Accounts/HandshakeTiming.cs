namespace Marrow.Core.Accounts;

/// <summary>Smart-start inputs the owner left to the engine: how long one step takes and the margin kept. ENGINEERING DECISION (MA-5 ED).</summary>
public sealed record HandshakeTimingOptions(double StepMinutes = 1.0, double MarginPercent = 1.0);

public sealed record HandshakeOptions(bool Enabled = true, HandshakePoint General = default, HandshakeTimingOptions? Timing = null)
{
    public HandshakeTimingOptions TimingOrDefault => Timing ?? new HandshakeTimingOptions();
}

public static class HandshakeTiming
{
    /// <summary>The usage percent at which the Handshake starts: step, else account, else general; a Smart point is measured, else plan-aware.</summary>
    public static double StartPercent(
        AccountInfo account, PillStep? step, HandshakeOptions options, IAccountLimits limits, Guid threadId) => throw new NotImplementedException();
}
