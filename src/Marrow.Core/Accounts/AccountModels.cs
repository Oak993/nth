namespace Marrow.Core.Accounts;

// PORT-ONLY (OD-0114, MA-5): multi-account foundation. Source for every rule here is
// porting/first-release/multi-account-NOTES.md (owner, 2026-10-05); no Swift twin exists.

/// <summary>The plan size, which sets the Smart Handshake default when there is no measured data. NOTES "Handshake defaults": $20 plans 95%, 5x/10x/20x plans 98%.</summary>
public enum PlanTier
{
    Unknown,
    Standard,
    Large,
}

/// <summary>
/// The Handshake start point: Smart, or a percentage from 80 to 99 (NOTES "Start the Handshake: Smart (recommended) / At [slider 80-99%]").
/// The default value is Smart.
/// </summary>
public readonly record struct HandshakePoint
{
    public const int MinPercent = 80;
    public const int MaxPercent = 99;

    public int? Percent { get; }

    private HandshakePoint(int percent) => Percent = percent;

    public static HandshakePoint Smart => default;

    public bool IsSmart => Percent is null;

    public static HandshakePoint At(int percent) =>
        percent is < MinPercent or > MaxPercent
            ? throw new ArgumentOutOfRangeException(nameof(percent), percent, "The Handshake point is Smart or 80 to 99 percent.")
            : new HandshakePoint(percent);
}

/// <summary>One connected account, as MA-1's store will give it. <see cref="Id"/> is the store's stable id; <see cref="Service"/> groups accounts of one service (Claude, Codex, OpenRouter...).</summary>
public sealed record AccountInfo(
    string Id,
    string Service,
    string Name,
    bool Enabled = true,
    PlanTier Plan = PlanTier.Unknown,
    HandshakePoint? HandshakeAt = null);

/// <summary>What MA-2 reads for an account's binding window. <see cref="PercentUsed"/> is 0 to 100.</summary>
public sealed record AccountUsage(double PercentUsed, DateTimeOffset? ResetsAt, bool IsOut = false)
{
    /// <summary>Out, and not yet past the reset time it reported.</summary>
    public bool IsExhaustedAt(DateTimeOffset now) =>
        (IsOut || PercentUsed >= 100) && !(ResetsAt is { } reset && now >= reset);
}

/// <summary>Model, effort and fast mode a step or account carries (NOTES "Effort and fast mode"). Null means "same as the account".</summary>
public sealed record StepSettings(string? Model = null, string? Effort = null, bool? Fast = null);

/// <summary>One step of a pill (NOTES "Pills", "Effort and fast mode", "Handshake point everywhere"). A null model, effort, fast mode or point is "same as the account". <see cref="KeyId"/> names an API key of the service when the step is a key rather than the account's own sign-in (MA-7).</summary>
public sealed record PillStep(
    string AccountId,
    string? Model = null,
    string? Effort = null,
    bool? Fast = null,
    HandshakePoint? HandshakeAt = null,
    string? KeyId = null)
{
    public StepSettings Settings => new(Model, Effort, Fast);
    public string AccountIdentity => KeyId ?? AccountId;
}

/// <summary>A named sequence across accounts and services. <see cref="EarlierAccount"/> is the pill's own "when an earlier account is back" value (a <see cref="DecisionValues"/> value), or null to follow Settings.</summary>
public sealed record Pill(
    string Name,
    IReadOnlyList<PillStep> Steps,
    string? EarlierAccount = null,
    Guid Id = default,
    PillHandshakeMode HandshakeMode = PillHandshakeMode.PerStep,
    HandshakePoint? SameHandshake = null)
{
    /// <summary>The step as the Handshake sees it: in "same for every step" mode the pill's one point replaces the step's own (MA-7).</summary>
    public PillStep Effective(int index) =>
        HandshakeMode == PillHandshakeMode.SameForEvery ? Steps[index] with { HandshakeAt = SameHandshake ?? HandshakePoint.Smart } : Steps[index];
}

/// <summary>NOTES "Handshake point everywhere" and the MA-7 brief: each step has its own point, or one point serves every step.</summary>
public enum PillHandshakeMode
{
    PerStep,
    SameForEvery,
}

/// <summary>Where a thread goes next: the account, and the pill step when a pill drives the move.</summary>
public sealed record SwitchTarget(AccountInfo Account, int? StepIndex = null, PillStep? Step = null)
{
    public StepSettings Settings => Step?.Settings ?? new StepSettings();
}
