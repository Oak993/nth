using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class RecentCacheTests
{
    [Fact]
    [Trait("Req", "PR-STORE-053")]
    [Trait("Req", "PR-STORE-055")]
    public void Two_generations_remain_readable_until_the_next_wholesale_rollover()
    {
        var cache = new RecentCache<string, string>(2);
        cache.Insert("A", "a"); cache.Insert("B", "b");
        cache.Insert("C", "c"); cache.Insert("D", "d");
        Assert.All(new[] { "a", "b", "c", "d" }, key => Assert.True(cache.Contains(key)));
        cache.Insert("E", "e");
        Assert.False(cache.Contains("a")); Assert.False(cache.Contains("b"));
        Assert.All(new[] { "c", "d", "e" }, key => Assert.True(cache.Contains(key)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-054")]
    public void Reading_previous_promotes_and_can_itself_discard_the_old_generation()
    {
        var cache = new RecentCache<string, string>(2);
        foreach (var key in new[] { "a", "b", "c", "d" }) cache.Insert(key.ToUpperInvariant(), key);
        Assert.True(cache.TryGetValue("a", out var value)); Assert.Equal("A", value);
        Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("c")); Assert.True(cache.Contains("d")); Assert.True(cache.Contains("a"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-053")]
    [Trait("Req", "PR-STORE-054")]
    public void Contains_does_not_promote_and_current_hits_do_not_refresh_the_generation()
    {
        var cache = new RecentCache<string, int>(1);
        cache.Insert(1, "a"); cache.Insert(2, "b");
        Assert.True(cache.Contains("a"));
        Assert.True(cache.TryGetValue("b", out var value)); Assert.Equal(2, value);
        cache.Insert(3, "c");
        Assert.False(cache.Contains("a")); Assert.True(cache.Contains("b"));
        Assert.False(cache.TryGetValue("missing", out _));
    }

    [Fact]
    [Trait("Req", "PR-STORE-054")]
    [Trait("Req", "PR-STORE-055")]
    public void Promotion_preserves_original_cost_and_exact_budget_equality_does_not_roll()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(1, "a", 3); cache.Insert(2, "b", 2);
        cache.Insert(3, "c", 1); // a,b become previous
        Assert.True(cache.TryGetValue("a", out _)); // current cost is 4, not 2
        cache.Insert(4, "d", 1); // exactly 5: b still remains previous
        Assert.True(cache.Contains("b"));
        cache.Insert(5, "e", 1); // rolls and discards b
        Assert.False(cache.Contains("b")); Assert.True(cache.Contains("a"));
    }

    [Theory]
    [Trait("Req", "PR-STORE-056")]
    [InlineData(false)]
    [InlineData(true)]
    public void Over_budget_replacement_removes_existing_value_even_from_previous(bool previous)
    {
        var cache = new RecentCache<string, int>(1, 5);
        cache.Insert(1, "a", 2);
        if (previous) cache.Insert(2, "b", 1);
        cache.Insert(99, "a", 6);
        Assert.False(cache.Contains("a")); Assert.False(cache.TryGetValue("a", out _));
        if (previous) Assert.True(cache.Contains("b"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-057")]
    public void Nonpositive_limits_and_negative_costs_follow_source_clamps()
    {
        var cache = new RecentCache<string, int>(-2, -10);
        cache.Insert(1, "default"); Assert.False(cache.Contains("default"));
        cache.Insert(2, "a", -9); cache.Insert(3, "b", 0);
        Assert.True(cache.Contains("a")); Assert.True(cache.Contains("b"));
        cache.Insert(4, "c", 0);
        Assert.False(cache.Contains("a")); Assert.True(cache.Contains("b"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-058")]
    public void Removal_returns_current_or_previous_and_refunds_current_cost()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(10, "old", 5); cache.Insert(20, "a", 3);
        Assert.True(cache.RemoveValue("old", out var old)); Assert.Equal(10, old);
        Assert.False(cache.Contains("old"));
        cache.Insert(30, "b", 2);
        Assert.True(cache.RemoveValue("a", out var a)); Assert.Equal(20, a);
        cache.Insert(40, "c", 3); // refund allows b,c in current
        cache.Insert(50, "d", 1); // previous b,c
        cache.Insert(60, "e", 4); // no rollover yet
        Assert.True(cache.Contains("b")); Assert.True(cache.Contains("c"));
        Assert.False(cache.RemoveValue("a", out _));
    }

    [Fact]
    [Trait("Req", "PR-STORE-055")]
    [Trait("Req", "PR-STORE-058")]
    public void Replacing_a_key_removes_it_before_count_and_cost_checks()
    {
        var cache = new RecentCache<string, int>(2, 5);
        cache.Insert(1, "old", 5); cache.Insert(2, "a", 3); cache.Insert(3, "b", 2);
        cache.Insert(4, "a", 1);
        Assert.True(cache.Contains("old")); // replacement did not roll a full generation
        Assert.True(cache.TryGetValue("a", out var a)); Assert.Equal(4, a);
    }

    [Fact]
    [Trait("Req", "PR-STORE-055")]
    [Trait("Req", "PR-STORE-057")]
    public void Maximum_64_bit_cost_is_supported_without_addition_overflow()
    {
        var cache = new RecentCache<string, int>(2);
        cache.Insert(1, "max", long.MaxValue); cache.Insert(2, "zero", 0);
        Assert.True(cache.Contains("max"));
        cache.Insert(3, "next");
        Assert.True(cache.Contains("max")); Assert.True(cache.Contains("next"));
    }
}
