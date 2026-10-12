// PORT-ONLY: T11 step 2. What Settings > Providers draws for an account (or an API key) and for a service: plain data. IsKey marks a key's row (its line is the masked key);
// Keys and CanAddKey mark a service kept by API keys and whether it takes another key (OD-0149).
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
    bool NoFiveHour,
    bool IsKey = false);

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
    string? ActionLabel = null,
    bool Keys = false,
    bool CanAddKey = false);
