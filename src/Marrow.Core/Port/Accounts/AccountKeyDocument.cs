using Marrow.Core.Accounts.Keys;
using System.Text.Json.Serialization;

namespace Marrow.Core.Port.Accounts;

internal sealed record AccountKeyDocument
{
    public int Version { get; init; } = 1;
    public Dictionary<string, KeyOrderMode> Orders { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, double> Caps { get; init; } = [];
    public AccountKeySpendEntry[] Spend { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountKeyReservationEntry[]? Reservations { get; init; }
}

internal sealed record AccountKeySpendEntry([property: JsonRequired] Guid Key, [property: JsonRequired] double Usd, [property: JsonRequired] DateTimeOffset At);

internal sealed record AccountKeyReservationEntry([property: JsonRequired] Guid Request, [property: JsonRequired] Guid Key,
    [property: JsonRequired] double? Usd, [property: JsonRequired] DateTimeOffset At);
