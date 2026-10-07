// PORT-ONLY: T11 step 2. What the Models page draws for an account and for a service: plain data.
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed record AccountRowView(
    Guid Id,
    int Number,
    string Name,
    string PlanText,
    string Subtitle,
    bool Enabled,
    string HandshakeLabel,
    bool UsesDefaultHandshake,
    double? WeekFraction,
    string UsageText,
    bool Hot,
    bool NoFiveHour);

public sealed record ServiceBlockView(
    ProviderKind Service,
    string Title,
    string Summary,
    bool Several,
    bool CanAdd,
    IReadOnlyList<AccountRowView> Rows,
    double? MiniFraction,
    string MiniText,
    bool ServerBox = false,
    string? ActionLabel = null);
