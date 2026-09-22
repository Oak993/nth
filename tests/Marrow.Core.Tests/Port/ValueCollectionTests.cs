using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// R-VAL-2 (IMPLEMENTING.md 3.9). Source: drafts A and C (value semantics), plus ValueSet and Mutating.
public sealed class ValueCollectionTests
{
    [Fact]
    [Trait("Rule", "R-VAL-2")]
    [Trait("Decision", "ED-077")]
    public void Collections_in_records_compare_by_content_like_Swift_values()
    {
        var a = new HolderSample(new(["x", "y"]), new([KeyValuePair.Create("k", 1)]), new(["t", "u"]));
        var b = new HolderSample(new(["x", "y"]), new([KeyValuePair.Create("k", 1)]), new(["u", "t"]));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Items = new(["y", "x"]) });
    }

    [Fact]
    [Trait("Rule", "R-VAL-2")]
    public void BCL_collections_in_records_compare_by_reference()
    {
        // The pitfall: a record holding List<T> or Dictionary compares the references, not the contents.
        var left = new List<string> { "x" };
        var right = new List<string> { "x" };
        Assert.False(new ListHolderSample(left).Equals(new ListHolderSample(right)));
        Assert.False(new Dictionary<string, int> { ["k"] = 1 }.Equals(new Dictionary<string, int> { ["k"] = 1 }));
    }

    [Fact]
    [Trait("Rule", "R-VAL-3")]
    public void Mutating_a_copy_never_changes_the_original()
    {
        var original = new ValueList<int>([1, 2, 3]);
        var copy = original.Mutating(list => { list.RemoveAt(0); list.Add(4); });
        Assert.Equal([1, 2, 3], original);
        Assert.Equal([2, 3, 4], copy);
        Assert.Equal([1, 9, 3], original.SetItem(1, 9));
        Assert.Equal([1, 2, 3, 4], original.Appending(4));
        var map = new ValueMap<string, int>([KeyValuePair.Create("a", 1)]);
        Assert.Equal(2, map.Mutating(m => m["a"] = 2)["a"]);
        Assert.Equal(1, map["a"]);
    }

    [Fact]
    [Trait("Rule", "R-VAL-2")]
    [Trait("Rule", "R-CODE-8")]
    public void A_repeated_key_keeps_the_last_value()
    {
        var map = new ValueMap<string, int>([KeyValuePair.Create("a", 1), KeyValuePair.Create("a", 2)]);
        Assert.Equal(2, map["a"]);
        Assert.Single(map);
    }

    [Fact]
    [Trait("Rule", "R-VAL-2")]
    [Trait("Rule", "R-STR-2")]
    public void A_map_of_external_text_takes_the_Swift_string_comparer()
    {
        var map = new ValueMap<string, int>([KeyValuePair.Create("\U000000E9", 1)], SwiftStringComparer.Instance);
        Assert.True(map.ContainsKey("e\U00000301"));
        Assert.False(new ValueMap<string, int>([KeyValuePair.Create("\U000000E9", 1)]).ContainsKey("e\U00000301"));
    }

    [Fact]
    [Trait("Rule", "R-COLL-1")]
    public void OrderBy_is_stable_and_List_Sort_is_not()
    {
        var items = Enumerable.Range(0, 200).Select(i => (Key: i % 3, Order: i)).ToList();
        var stable = items.OrderBy(item => item.Key).ToList();
        Assert.All(stable.GroupBy(item => item.Key), group => Assert.Equal(group.Select(g => g.Order).Order(), group.Select(g => g.Order)));
        var unstable = items.ToList();
        unstable.Sort((x, y) => x.Key.CompareTo(y.Key));
        Assert.False(unstable.SequenceEqual(stable)); // the pitfall: List.Sort reorders equal keys
    }
}
