using System.Collections.ObjectModel;

namespace Marrow.Core.Port;

// PORT-ONLY. Swift arrays, dictionaries and sets are VALUES: a copy is independent and == compares contents.
// Every collection inside a C# model record is one of these (ED-077), so the record's generated equality is
// Swift's synthesized Equatable. They supersede the internal MCPValues.List/Map in Models/MCP.cs.
// Source: draft C (proto-C SwiftValues.cs) with ValueSet and Mutating added.

/// <summary>Swift <c>[T]</c> inside a model: immutable, element-wise equality and hash.</summary>
public sealed class ValueList<T> : ReadOnlyCollection<T>, IEquatable<ValueList<T>>
{
    public static new ValueList<T> Empty { get; } = new([]);

    public ValueList(IEnumerable<T> items) : base(items.ToArray()) { }

    /// <summary><c>var copy = list; copy.append(x)</c>.</summary>
    public ValueList<T> Appending(T item) => new(this.Append(item));

    /// <summary><c>var copy = list; copy[i] = x</c>. Out of range traps.</summary>
    public ValueList<T> SetItem(int index, T item)
    {
        var items = this.ToArray();
        items[index] = item;
        return new(items);
    }

    /// <summary><c>var copy = list; ...mutations...</c>: any Swift mutation sequence on a copy.</summary>
    public ValueList<T> Mutating(Action<List<T>> change)
    {
        var items = new List<T>(this);
        change(items);
        return new(items);
    }

    public bool Equals(ValueList<T>? other) => other is not null && this.SequenceEqual(other);

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in this) hash.Add(item);
        return hash.ToHashCode();
    }

    public static implicit operator ValueList<T>(T[] items) => new(items);
}

/// <summary>
/// Swift <c>[K: V]</c> inside a model: an independent copy, == by content, a hash independent of insertion order.
/// A repeated key keeps the LAST value, as Swift dictionary literals and decoded JSON objects do. Iteration
/// order is NOT a spec (Swift's is randomly seeded): code that shows or writes a dictionary sorts it, as the
/// source does. String keys holding user or external text take SwiftStringComparer.Instance (R-STR-2).
/// </summary>
public sealed class ValueMap<TKey, TValue> : ReadOnlyDictionary<TKey, TValue>, IEquatable<ValueMap<TKey, TValue>>
    where TKey : notnull
{
    private readonly IEqualityComparer<TKey> comparer;

    public static new ValueMap<TKey, TValue> Empty { get; } = new([]);

    public ValueMap(IEnumerable<KeyValuePair<TKey, TValue>> pairs, IEqualityComparer<TKey>? comparer = null)
        : base(Build(pairs, comparer)) => this.comparer = comparer ?? EqualityComparer<TKey>.Default;

    /// <summary><c>var copy = map; ...mutations...</c>.</summary>
    public ValueMap<TKey, TValue> Mutating(Action<Dictionary<TKey, TValue>> change)
    {
        var map = new Dictionary<TKey, TValue>(this, comparer);
        change(map);
        return new(map, comparer);
    }

    private static Dictionary<TKey, TValue> Build(IEnumerable<KeyValuePair<TKey, TValue>> pairs, IEqualityComparer<TKey>? comparer)
    {
        var map = new Dictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }

    public bool Equals(ValueMap<TKey, TValue>? other) =>
        other is not null && Count == other.Count && this.All(pair =>
            other.TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(pair.Value, value));

    public override bool Equals(object? obj) => Equals(obj as ValueMap<TKey, TValue>);

    public override int GetHashCode()
    {
        var hash = Count;
        foreach (var pair in this) hash ^= HashCode.Combine(comparer.GetHashCode(pair.Key), pair.Value);
        return hash;
    }
}

/// <summary>Swift <c>Set&lt;T&gt;</c> inside a model: == by content, a hash independent of order, order not a spec.</summary>
public sealed class ValueSet<T> : IReadOnlyCollection<T>, IEquatable<ValueSet<T>>
{
    private readonly HashSet<T> items;

    public static ValueSet<T> Empty { get; } = new([]);

    public ValueSet(IEnumerable<T> items, IEqualityComparer<T>? comparer = null) => this.items = new HashSet<T>(items, comparer);

    public int Count => items.Count;

    public bool Contains(T item) => items.Contains(item);

    /// <summary><c>var copy = set; ...mutations...</c>.</summary>
    public ValueSet<T> Mutating(Action<HashSet<T>> change)
    {
        var copy = new HashSet<T>(items, items.Comparer);
        change(copy);
        return new(copy, items.Comparer);
    }

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ValueSet<T>? other) => other is not null && items.SetEquals(other.items);

    public override bool Equals(object? obj) => Equals(obj as ValueSet<T>);

    public override int GetHashCode()
    {
        var hash = Count;
        foreach (var item in items) hash ^= item is null ? 0 : items.Comparer.GetHashCode(item);
        return hash;
    }
}
