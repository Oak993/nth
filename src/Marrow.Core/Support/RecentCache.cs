using System.Diagnostics.CodeAnalysis;

namespace Marrow.Core.Support;

/// <summary>A two-generation cache; reads from the previous generation mutate it.</summary>
public sealed class RecentCache<TKey, TValue> where TKey : notnull
{
    private readonly record struct Entry(TValue Value, long Cost);
    private Dictionary<TKey, Entry> current;
    private Dictionary<TKey, Entry> previous = new();
    private long currentCost;
    private long previousCost;
    private readonly int limit;
    private readonly long costLimit;

    public RecentCache(int limit, long costLimit = long.MaxValue)
    {
        this.limit = Math.Max(1, limit);
        this.costLimit = Math.Max(0, costLimit);
        current = new Dictionary<TKey, Entry>(this.limit);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (current.TryGetValue(key, out var hit))
        {
            value = hit.Value;
            return true;
        }
        if (!previous.Remove(key, out hit))
        {
            value = default;
            return false;
        }
        previousCost -= hit.Cost;
        Insert(hit.Value, key, hit.Cost);
        value = hit.Value;
        return true;
    }

    public bool Contains(TKey key) => current.ContainsKey(key) || previous.ContainsKey(key);

    public bool RemoveValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var hasCurrent = current.Remove(key, out var currentEntry);
        var hasPrevious = previous.Remove(key, out var previousEntry);
        currentCost -= hasCurrent ? currentEntry.Cost : 0;
        previousCost -= hasPrevious ? previousEntry.Cost : 0;
        value = hasCurrent ? currentEntry.Value : hasPrevious ? previousEntry.Value : default;
        return hasCurrent || hasPrevious;
    }

    public void Insert(TValue value, TKey key, long cost = 1)
    {
        RemoveValue(key, out _);
        cost = Math.Max(0, cost);
        if (cost > costLimit) return;
        if (current.Count >= limit || currentCost > costLimit - cost)
        {
            previous = current;
            previousCost = currentCost;
            current = new Dictionary<TKey, Entry>(limit);
            currentCost = 0;
        }
        current[key] = new Entry(value, cost);
        currentCost += cost;
    }
}
