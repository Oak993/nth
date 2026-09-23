using System.Diagnostics.CodeAnalysis;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/RecentCache.swift (B2 1fbf81e), :1-57.

/// <summary>
/// :3 <c>struct RecentCache&lt;Key: Hashable, Value&gt;</c>: a two-generation cache, not an LRU (PR-STORE-053,
/// port_impacts 117). A read that hits the previous generation promotes the entry and can itself roll the
/// current generation over (PR-STORE-054), so every read mutates.
/// </summary>
/// <remarks>
/// ENGINEERING DECISION: a sealed class, not an immutable record (R-VAL-1 covers model values). The Swift struct
/// is a mutable container whose <c>mutating</c> methods change it in place, and every one of the 17 Swift construction
/// sites holds it in a stored <c>var</c> and mutates it through that variable without taking a copy
/// (ThreadRuntime.swift:1960-1961, :1988-1996, :198 reassigns a fresh cache; Storage.swift:254, :270 inside a
/// Mutex-guarded State mutated in place; the static caches in ThumbnailCache.swift:16, :70, MarkdownView.swift:107,
/// :563, :1347, and the rest). With no copy anywhere, a class instance behaves identically.
/// Swift <c>Int</c> is <c>long</c> (R-NUM-4) for the limit and the costs; checked arithmetic
/// (CheckForOverflowUnderflow) traps where Swift's <c>+=</c> and <c>-=</c> trap.
/// </remarks>
public sealed class RecentCache<TKey, TValue> where TKey : notnull
{
    // :4-7
    private readonly record struct Entry(TValue Value, long Cost);

    // :9-14
    private Dictionary<TKey, Entry> current;
    private Dictionary<TKey, Entry> previous;
    private long currentCost;
    // :12 previousCost is written at :25, :39 and :49 and read nowhere in the pinned tree (PR-STORE-058); it is
    // kept, as the source keeps it, and has no observable effect.
    private long previousCost;
    private readonly long limit;
    private readonly long costLimit;

    // :3 Key: Hashable. A String key hashes and compares by Swift String equality, which is Unicode canonical
    // equivalence (R-STR-2): U+00E9 and U+0065 U+0301 spell one key, and U+212A KELVIN SIGN equals "K".
    // No Swift site passes a comparer, so a string-keyed cache takes SwiftStringComparer.Instance; every other key
    // type (UUID as Guid, Date) uses its own equality, as its Hashable conformance does.
    private static readonly IEqualityComparer<TKey>? KeyComparer =
        typeof(TKey) == typeof(string) ? (IEqualityComparer<TKey>)(object)SwiftStringComparer.Instance : null;

    // :16-20 init(limit:costLimit:), costLimit defaults to Int.max.
    public RecentCache(long limit, long costLimit = long.MaxValue)
    {
        this.limit = Math.Max(1, limit); // :17
        this.costLimit = Math.Max(0, costLimit); // :18
        current = Reserved(this.limit); // :19 current.reserveCapacity(self.limit)
        previous = new Dictionary<TKey, Entry>(KeyComparer); // :10
    }

    // :22-28 mutating func value(for key: Key) -> Value? (Swift: value(for:)). A miss is false.
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        // :23
        if (current.TryGetValue(key, out var hit))
        {
            value = hit.Value;
            return true;
        }
        // :24
        if (!previous.Remove(key, out hit))
        {
            value = default;
            return false;
        }
        previousCost -= hit.Cost; // :25
        Insert(hit.Value, key, hit.Cost); // :26 re-inserted at its original cost; can roll the generation over
        value = hit.Value; // :27
        return true;
    }

    // :30-32 func contains(_ key: Key) -> Bool; reads both generations and promotes nothing.
    public bool Contains(TKey key) => current.ContainsKey(key) || previous.ContainsKey(key);

    // :34-41 @discardableResult mutating func removeValue(for key: Key) -> Value?. False when neither generation
    // holds the key. The generations never share a key (PR-STORE-058), so :40's order only picks the one that has it.
    public bool RemoveValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var hasCurrent = current.Remove(key, out var currentEntry); // :36
        var hasPrevious = previous.Remove(key, out var previousEntry); // :37
        currentCost -= hasCurrent ? currentEntry.Cost : 0; // :38
        previousCost -= hasPrevious ? previousEntry.Cost : 0; // :39
        // :40 currentEntry?.value ?? previousEntry?.value
        if (hasCurrent)
        {
            value = currentEntry.Value;
            return true;
        }
        if (hasPrevious)
        {
            value = previousEntry.Value;
            return true;
        }
        value = default;
        return false;
    }

    // :43-56 mutating func insert(_ value: Value, for key: Key, cost: Int = 1)
    public void Insert(TValue value, TKey key, long cost = 1)
    {
        // :44 UPSTREAM DEFECT 43, reproduced: the existing entry is removed before the guard at :46, so an
        // over-budget value leaves the key absent altogether (PR-STORE-056).
        RemoveValue(key, out _);
        cost = Math.Max(0, cost); // :45
        if (!(cost <= costLimit)) return; // :46 guard cost <= costLimit else { return }
        // :47 compared by subtraction: costLimit - cost cannot overflow, as costLimit >= 0 and cost >= 0.
        if (current.Count >= limit || currentCost > costLimit - cost)
        {
            // :48-52 UPSTREAM DEFECT 44, reproduced: the old previous generation is discarded in this assignment.
            previous = current;
            previousCost = currentCost;
            current = Reserved(limit); // :50-51 current = [:]; current.reserveCapacity(limit)
            currentCost = 0;
        }
        current[key] = new Entry(value, cost); // :54
        currentCost += cost; // :55
    }

    // :19, :51 reserveCapacity(limit). ENGINEERING DECISION: a .NET Dictionary capacity is an int, so a limit beyond
    // int.MaxValue traps on the checked cast. No Swift site passes one: the largest limit is 1200
    // (MarkdownView.swift:563, :1347).
    private static Dictionary<TKey, Entry> Reserved(long limit) => new(checked((int)limit), KeyComparer);
}
