using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Every expected value below is a hand trace of DroppyCode/Core/Support/RecentCache.swift (Droppy Code 1.6.2,
// commit 1fbf81e), written out step by step next to the call that produces it. None comes from running the port.
public sealed class RecentCacheTests
{
    [Fact]
    [Trait("Req", "PR-STORE-053")] // DroppyCode/Core/Support/RecentCache.swift:9-10, :30-32
    [Trait("Req", "PR-STORE-055")] // DroppyCode/Core/Support/RecentCache.swift:47-55
    public void Two_generations_remain_readable_until_the_next_wholesale_rollover()
    {
        var cache = new RecentCache<string, string>(2);
        cache.Insert("A", "a"); cache.Insert("B", "b"); // current {a,b}
        cache.Insert("C", "c"); // count 2 >= 2: previous {a,b}, current {c}
        cache.Insert("D", "d"); // current {c,d}
        Assert.All(new[] { "a", "b", "c", "d" }, key => Assert.True(cache.Contains(key)));
        cache.Insert("E", "e"); // count 2 >= 2: previous {c,d}, current {e}; {a,b} discarded in the same assignment
        Assert.False(cache.Contains("a")); Assert.False(cache.Contains("b"));
        Assert.False(cache.TryGetValue("a", out _)); Assert.False(cache.TryGetValue("b", out _));
        Assert.All(new[] { "c", "d", "e" }, key => Assert.True(cache.Contains(key)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-054")] // DroppyCode/Core/Support/RecentCache.swift:22-28
    public void Reading_previous_promotes_and_can_itself_discard_the_old_generation()
    {
        var cache = new RecentCache<string, string>(2);
        foreach (var key in new[] { "a", "b", "c", "d" }) cache.Insert(key.ToUpperInvariant(), key); // previous {a,b}, current {c,d}
        // value(for: "a"): removed from previous, re-inserted; count 2 >= 2 rolls: previous {c,d}, current {a}; b discarded.
        Assert.True(cache.TryGetValue("a", out var value)); Assert.Equal("A", value);
        Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("c")); Assert.True(cache.Contains("d")); Assert.True(cache.Contains("a"));
        // The promoted key is in the fresh current: the next insert fills it without a rollover, so c and d survive.
        cache.Insert("E", "e"); // current {a,e}
        Assert.True(cache.Contains("c")); Assert.True(cache.Contains("d"));
        cache.Insert("F", "f"); // count 2 >= 2: previous {a,e}, current {f}
        Assert.True(cache.Contains("a")); Assert.False(cache.Contains("c")); Assert.False(cache.Contains("d"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-053")] // DroppyCode/Core/Support/RecentCache.swift:23, :30-32
    [Trait("Req", "PR-STORE-054")] // DroppyCode/Core/Support/RecentCache.swift:22-28
    public void Contains_does_not_promote_and_current_hits_do_not_refresh_the_generation()
    {
        var cache = new RecentCache<string, int>(1);
        cache.Insert(1, "a"); cache.Insert(2, "b"); // previous {a}, current {b}
        Assert.True(cache.Contains("a")); // contains reads both maps and moves nothing
        cache.Insert(3, "c"); // count 1 >= 1: previous {b}, current {c}; a discarded
        Assert.False(cache.Contains("a")); Assert.True(cache.Contains("b"));
        // Had contains promoted a, the state before c would be previous {b}, current {a}, and c would keep a and drop b.
        Assert.True(cache.TryGetValue("c", out var value)); Assert.Equal(3, value); // current hit returns at :23, no insert
        cache.Insert(4, "d"); // count 1 >= 1: previous {c}, current {d}
        Assert.True(cache.Contains("c")); Assert.False(cache.Contains("b"));
        Assert.False(cache.TryGetValue("missing", out _)); // :24 returns nil
    }

    [Fact]
    [Trait("Req", "PR-STORE-054")] // DroppyCode/Core/Support/RecentCache.swift:24-26
    [Trait("Req", "PR-STORE-055")] // DroppyCode/Core/Support/RecentCache.swift:47
    public void Promotion_preserves_original_cost_and_exact_budget_equality_does_not_roll()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(1, "a", 3); // 0 > 5 - 3 is false: current {a}, currentCost 3
        cache.Insert(2, "b", 2); // 3 > 5 - 2 is false: current {a,b}, currentCost 5
        cache.Insert(3, "c", 1); // 5 > 4: previous {a,b}, current {c}, currentCost 1
        Assert.True(cache.TryGetValue("a", out _)); // re-inserted at cost 3: 1 > 2 is false, currentCost 4
        cache.Insert(4, "d", 1); // 4 > 4 is false: exactly at the budget, no rollover, currentCost 5
        Assert.True(cache.Contains("b"));
        cache.Insert(5, "e", 1); // 5 > 4: previous {c,a,d}, current {e}; b discarded
        Assert.False(cache.Contains("b")); Assert.True(cache.Contains("a"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-055")] // DroppyCode/Core/Support/RecentCache.swift:47-55
    public void Cost_arm_rolls_by_itself_when_the_count_is_far_below_the_limit()
    {
        var cache = new RecentCache<string, int>(100, 10);
        cache.Insert(1, "a", 4); // current {a}, currentCost 4
        cache.Insert(2, "b", 6); // 4 > 4 is false: current {a,b}, currentCost 10
        cache.Insert(3, "c", 0); // 10 > 10 is false: a zero cost fits a full budget, current {a,b,c}
        cache.Insert(4, "d", 1); // 10 > 9: previous {a,b,c}, current {d}, currentCost 1
        Assert.All(new[] { "a", "b", "c", "d" }, key => Assert.True(cache.Contains(key)));
        cache.Insert(5, "e", 10); // 1 > 0: previous {d}, current {e}
        Assert.False(cache.Contains("a")); Assert.False(cache.Contains("b")); Assert.False(cache.Contains("c"));
        Assert.True(cache.Contains("d")); Assert.True(cache.Contains("e"));
    }

    [Theory]
    [Trait("Req", "PR-STORE-056")] // DroppyCode/Core/Support/RecentCache.swift:43-46
    [InlineData(false)]
    [InlineData(true)]
    public void Over_budget_replacement_removes_existing_value_even_from_previous(bool previous)
    {
        var cache = new RecentCache<string, int>(1, 5);
        cache.Insert(1, "a", 2); // current {a}
        if (previous) cache.Insert(2, "b", 1); // previous {a}, current {b}
        cache.Insert(99, "a", 6); // removeValue(a) first, then guard 6 <= 5 fails and returns
        Assert.False(cache.Contains("a")); Assert.False(cache.TryGetValue("a", out _));
        Assert.False(cache.RemoveValue("a", out _));
        if (previous) Assert.True(cache.Contains("b"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-056")] // DroppyCode/Core/Support/RecentCache.swift:46-47
    public void A_rejected_insert_returns_before_the_rollover_and_a_cost_equal_to_the_limit_is_kept()
    {
        var cache = new RecentCache<string, int>(1, 5);
        cache.Insert(1, "p", 1); cache.Insert(2, "a", 1); // previous {p}, current {a}
        cache.Insert(3, "z", 6); // guard 6 <= 5 fails before :47, so no rollover: p stays in previous
        Assert.True(cache.Contains("p")); Assert.True(cache.Contains("a")); Assert.False(cache.Contains("z"));
        cache.Insert(4, "full", 5); // guard 5 <= 5 passes; count 1 >= 1: previous {a}, current {full}
        Assert.True(cache.TryGetValue("full", out var full)); Assert.Equal(4, full);
        Assert.False(cache.Contains("p"));
    }

    [Theory]
    [Trait("Req", "PR-STORE-057")] // DroppyCode/Core/Support/RecentCache.swift:17
    [InlineData(int.MinValue)]
    [InlineData(-2)]
    [InlineData(0)]
    [InlineData(1)]
    public void A_limit_below_one_becomes_one(int limit)
    {
        var cache = new RecentCache<string, int>(limit);
        cache.Insert(1, "a", 0); // current {a}
        cache.Insert(2, "b", 0); // count 1 >= 1: previous {a}, current {b}
        Assert.True(cache.RemoveValue("b", out var b)); Assert.Equal(2, b); // current is now empty
        // count 0 >= 1 is false and 0 > max - 0 is false: no rollover, so previous {a} survives.
        // A limit of 0 would roll here (0 >= 0) and discard a.
        cache.Insert(3, "c", 0);
        Assert.True(cache.Contains("a")); Assert.True(cache.Contains("c"));
        cache.Insert(4, "d", 0); // count 1 >= 1: previous {c}, current {d}
        Assert.False(cache.Contains("a")); Assert.True(cache.Contains("c"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-057")] // DroppyCode/Core/Support/RecentCache.swift:18, :45-46
    public void Nonpositive_cost_limits_become_zero_and_a_zero_cost_limit_rejects_every_nonzero_cost()
    {
        var cache = new RecentCache<string, int>(10, -10); // costLimit max(0, -10) = 0
        cache.Insert(1, "default"); Assert.False(cache.Contains("default")); // default cost 1: 1 <= 0 fails
        cache.Insert(2, "one", 1); Assert.False(cache.Contains("one"));
        cache.Insert(3, "a", -9); // cost max(0, -9) = 0: 0 <= 0 passes; 0 > 0 - 0 is false: current {a}
        cache.Insert(4, "b", 0); // 0 > 0 is false: current {a,b}
        Assert.True(cache.Contains("a")); Assert.True(cache.Contains("b"));
        var minimum = new RecentCache<string, int>(10, long.MinValue); // also clamped to 0
        minimum.Insert(5, "zero", 0); Assert.True(minimum.Contains("zero"));
        minimum.Insert(6, "one", 1); Assert.False(minimum.Contains("one"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-057")] // DroppyCode/Core/Support/RecentCache.swift:45, :47, :55
    public void A_negative_insert_cost_is_charged_as_zero()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(1, "a", -9); // cost 0: current {a}, currentCost 0
        cache.Insert(2, "b", 5); // 0 > 0 is false: current {a,b}, currentCost 5
        cache.Insert(3, "c", 1); // 5 > 4: previous {a,b}, current {c}, currentCost 1
        cache.Insert(4, "d", 5); // 1 > 0: previous {c}, current {d}; a discarded
        Assert.False(cache.Contains("a")); Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("c")); Assert.True(cache.Contains("d"));
        // Charging -9 would leave currentCost at -9, -4, -3, 2 and never roll, keeping a.
    }

    [Fact]
    [Trait("Req", "PR-STORE-057")] // DroppyCode/Core/Support/RecentCache.swift:43, :47
    public void An_insert_without_a_cost_charges_exactly_one()
    {
        var cache = new RecentCache<string, int>(10, 3);
        cache.Insert(1, "a"); cache.Insert(2, "b"); cache.Insert(3, "c"); // 0 > 2, 1 > 2, 2 > 2 all false: currentCost 3
        cache.Insert(4, "d"); // 3 > 2: previous {a,b,c}, current {d}, currentCost 1
        Assert.All(new[] { "a", "b", "c", "d" }, key => Assert.True(cache.Contains(key)));
        cache.Insert(5, "e"); cache.Insert(6, "f"); // current {d,e,f}, currentCost 3
        Assert.True(cache.Contains("a"));
        cache.Insert(7, "g"); // 3 > 2: previous {d,e,f}, current {g}
        Assert.False(cache.Contains("a")); Assert.True(cache.Contains("d")); Assert.True(cache.Contains("g"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-058")] // DroppyCode/Core/Support/RecentCache.swift:34-41
    public void Removal_returns_current_or_previous_and_refunds_current_cost()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(10, "old", 5); // current {old}, currentCost 5
        cache.Insert(20, "a", 3); // 5 > 2: previous {old}, current {a}, currentCost 3
        Assert.True(cache.RemoveValue("old", out var old)); Assert.Equal(10, old); // the previous generation's value
        Assert.False(cache.Contains("old")); Assert.False(cache.TryGetValue("old", out _));
        cache.Insert(30, "b", 2); // 3 > 3 is false: current {a,b}, currentCost 5
        Assert.True(cache.RemoveValue("a", out var a)); Assert.Equal(20, a); // the current generation's value; currentCost 2
        cache.Insert(40, "c", 3); // 2 > 2 is false thanks to the refund: current {b,c}, currentCost 5
        cache.Insert(50, "d", 1); // 5 > 4: previous {b,c}, current {d}, currentCost 1
        cache.Insert(60, "e", 4); // 1 > 1 is false: current {d,e}
        Assert.True(cache.Contains("b")); Assert.True(cache.Contains("c"));
        // Without the refund currentCost stays 5 after removing a: c rolls b out, then e rolls {c,d} over, discarding b.
        Assert.False(cache.RemoveValue("a", out _)); // :40 returns nil when neither map holds the key
    }

    [Fact]
    [Trait("Req", "PR-STORE-055")] // DroppyCode/Core/Support/RecentCache.swift:44, :47
    [Trait("Req", "PR-STORE-058")] // DroppyCode/Core/Support/RecentCache.swift:34-41
    public void Replacing_a_key_removes_it_before_count_and_cost_checks()
    {
        var cache = new RecentCache<string, int>(2, 5);
        cache.Insert(1, "old", 5); // current {old}, currentCost 5
        cache.Insert(2, "a", 3); // 5 > 2: previous {old}, current {a}, currentCost 3
        cache.Insert(3, "b", 2); // 1 >= 2 false, 3 > 3 false: current {a,b}, currentCost 5
        cache.Insert(4, "a", 1); // removeValue(a) first: current {b}, currentCost 2; 1 >= 2 false, 2 > 4 false
        Assert.True(cache.Contains("old")); // replacement did not roll a full generation
        Assert.True(cache.TryGetValue("a", out var a)); Assert.Equal(4, a);
        Assert.True(cache.TryGetValue("b", out var b)); Assert.Equal(3, b);
    }

    [Fact]
    [Trait("Req", "PR-STORE-055")] // DroppyCode/Core/Support/RecentCache.swift:47, :55
    [Trait("Req", "PR-STORE-057")] // DroppyCode/Core/Support/RecentCache.swift:16, :18
    public void Maximum_64_bit_cost_is_compared_by_subtraction_without_addition_overflow()
    {
        // The limit of 10 is never reached, so only the cost arm currentCost > costLimit - cost can roll.
        var cache = new RecentCache<string, int>(10); // costLimit defaults to Int.max
        cache.Insert(1, "max", long.MaxValue); // 0 > max - max = 0 is false: current {max}, currentCost max
        cache.Insert(2, "zero", 0); // max > max - 0 is false: current {max,zero}
        Assert.True(cache.Contains("max")); Assert.True(cache.Contains("zero"));
        cache.Insert(3, "x", 1); // max > max - 1: previous {max,zero}, current {x}, currentCost 1
        cache.Insert(4, "y", 1); // 1 > max - 1 is false: current {x,y}, currentCost 2
        Assert.True(cache.Contains("max"));
        cache.Insert(5, "z", long.MaxValue); // 2 > max - max = 0: previous {x,y}, current {z}
        Assert.False(cache.Contains("max")); Assert.False(cache.Contains("zero"));
        Assert.True(cache.Contains("x")); Assert.True(cache.Contains("y")); Assert.True(cache.Contains("z"));
        // Writing the arm as currentCost + cost > costLimit wraps 2 + max to a negative number, never rolls, keeps max.
    }

    [Fact]
    [Trait("Req", "PR-STORE-053")] // DroppyCode/Core/Support/RecentCache.swift:3; String keys at ThumbnailCache.swift:16, MarkdownView.swift:107
    [Trait("Rule", "R-STR-2")]
    public void String_keys_compare_by_canonical_equivalence_like_Swift_String_hashing()
    {
        const string composed = "café";
        const string decomposed = "café";
        // Negative control: the default .NET string key compares ordinally and keeps the two forms apart.
        Assert.False(new Dictionary<string, int> { [composed] = 1 }.ContainsKey(decomposed));

        // No Swift call site passes a comparer; RecentCache<String, _> hashes by Swift String equality.
        var cache = new RecentCache<string, int>(2);
        cache.Insert(1, composed);
        Assert.True(cache.Contains(decomposed));
        Assert.True(cache.TryGetValue(decomposed, out var hit)); Assert.Equal(1, hit);
        cache.Insert(2, decomposed); // the same key: removeValue first, so current still holds one entry
        cache.Insert(3, "x"); // count 1 >= 2 is false: current {cafe, x}
        cache.Insert(4, "y"); // count 2 >= 2: previous {cafe, x}, current {y}
        cache.Insert(5, "z"); // current {y, z}; with two separate keys the rollover would have come one insert
        // earlier and this insert would have discarded both forms.
        Assert.True(cache.TryGetValue(composed, out var replaced)); Assert.Equal(2, replaced);
        Assert.True(cache.RemoveValue(decomposed, out var removed)); Assert.Equal(2, removed);
        Assert.False(cache.Contains(composed)); Assert.False(cache.Contains(decomposed));

        // U+212A KELVIN SIGN is canonically equivalent to U+004B.
        var kelvin = new RecentCache<string, int>(10);
        kelvin.Insert(7, "K");
        Assert.True(kelvin.Contains("K"));
        Assert.True(kelvin.TryGetValue("K", out var k)); Assert.Equal(7, k);
    }

    [Fact]
    [Trait("Req", "PR-STORE-053")] // DroppyCode/Core/Support/RecentCache.swift:3; UUID keys at ThreadRuntime.swift:1960, Storage.swift:254
    public void Non_string_keys_use_their_own_equality()
    {
        Guid first = SwiftUUID.Parse("6f2c1b52-6a0f-4d52-9a55-0d4f0b7b0001")!.Value;
        Guid second = SwiftUUID.Parse("6f2c1b52-6a0f-4d52-9a55-0d4f0b7b0002")!.Value;
        var cache = new RecentCache<Guid, string>(1);
        cache.Insert("one", first); cache.Insert("two", second); // previous {first}, current {second}
        Assert.True(cache.Contains(SwiftUUID.Parse("6F2C1B52-6A0F-4D52-9A55-0D4F0B7B0001")!.Value));
        Assert.True(cache.TryGetValue(first, out var one)); Assert.Equal("one", one); // promoted: previous {second}, current {first}
        Assert.True(cache.Contains(second));
    }
}
