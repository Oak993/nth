using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Fixtures;

public sealed class ObservedApiCredits : IApiKeyCreditObservations
{
    private readonly Dictionary<string, (double? Usd, DateTimeOffset? At, bool? Usable)> values = [];
    public void Set(string id, double? usd, DateTimeOffset? at, bool? usable = true) => values[id] = (usd, at, usable);
    public double? CreditsLeft(string id) => values.GetValueOrDefault(id).Usd;
    public DateTimeOffset? ObservedAt(string id) => values.GetValueOrDefault(id).At;
    public bool? IsUsable(string id) => values.GetValueOrDefault(id).Usable;
}
