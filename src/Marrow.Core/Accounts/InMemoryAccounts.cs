namespace Marrow.Core.Accounts;

/// <summary>The in-memory <see cref="IAccountStore"/> until MA-1's store is joined.</summary>
public sealed class InMemoryAccountStore(IEnumerable<AccountInfo> accounts) : IAccountStore
{
    private List<AccountInfo> list = [.. accounts];

    public IReadOnlyList<AccountInfo> Accounts => list;

    public void Set(IEnumerable<AccountInfo> accounts) => list = [.. accounts];

    public void Update(string id, Func<AccountInfo, AccountInfo> change) =>
        list = [.. list.Select(a => a.Id == id ? change(a) : a)];
}

/// <summary>The in-memory <see cref="IAccountLimits"/> until MA-2's limits are joined.</summary>
public sealed class InMemoryAccountLimits : IAccountLimits
{
    private readonly Dictionary<string, AccountUsage> usage = [];
    private readonly Dictionary<string, double> rates = [];
    private readonly Dictionary<string, double> costs = [];

    public void SetUsage(string accountId, AccountUsage value) => usage[accountId] = value;

    public void SetRate(string accountId, double percentPerMinute) => rates[accountId] = percentPerMinute;

    public void SetHandshakeCost(string accountId, double percent) => costs[accountId] = percent;

    public AccountUsage? Usage(string accountId) => usage.GetValueOrDefault(accountId);

    public double? SpendPercentPerMinute(string accountId, Guid threadId) => rates.TryGetValue(accountId, out var v) ? v : null;

    public double? HandshakeCostPercent(string accountId) => costs.TryGetValue(accountId, out var v) ? v : null;
}

/// <summary>The in-memory <see cref="IHandoffSummary"/> until MA-4's summary is joined. A gap is a key fact the read-back does not mention (ordinal, ignoring case).</summary>
public sealed class InMemoryHandoffSummary : IHandoffSummary
{
    public IReadOnlyList<string> KeyFacts { get; set; } = [];

    public int Builds { get; private set; }

    public Task<HandoffSummary> BuildAsync(Guid threadId, CancellationToken ct)
    {
        Builds++;
        return Task.FromResult(new HandoffSummary($"summary of {threadId}", $"handoff-{threadId}.json", KeyFacts));
    }

    public IReadOnlyList<string> FindGaps(HandoffSummary summary, string readBack) =>
        [.. summary.KeyFacts.Where(f => !readBack.Contains(f, StringComparison.OrdinalIgnoreCase))];
}
