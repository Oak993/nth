// PORT-ONLY: T11 step 2. What the Pills page and the pill editor draw: plain data.
using Marrow.Core.Accounts;
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Settings;

public enum PillRenameResult
{
    Ok,
    Empty,
    Taken,
    TooLong,
}

/// <summary>One account mark of a pill drawn small: the step's account, its service and whether the account is off.</summary>
public sealed record StepMark(string AccountName, ProviderKind? Service, bool Skipped, bool NewService);

public sealed record PillRowView(Guid Id, string Name, string Summary, string Tooltip, IReadOnlyList<StepMark> Steps);

public sealed record StepView(
    int Index,
    int Number,
    string AccountId,
    string AccountName,
    ProviderKind? Service,
    string ServiceText,
    bool Skipped,
    bool NewService,
    bool SameAsAccount,
    string Model,
    string? ModelId,
    string? Effort,
    bool Fast,
    Marrow.Core.Accounts.HandshakePoint HandshakeAt,
    bool HandshakeLocked,
    bool NeverRunsOut);

public sealed record AvailableStep(string AccountId, string Label, ProviderKind Service);
