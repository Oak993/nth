using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Every expected value below is a hand trace of DroppyCode/Core/Support/RecentCache.swift (Droppy Code 1.6.2,
// commit 1fbf81e), written out step by step next to the call that produces it. None comes from running the port.
public sealed class RecentCacheRefundTests
{
    [Fact]
    [Trait("Req", "PR-STORE-058")] // DroppyCode/Core/Support/RecentCache.swift:38-39, :47
    public void Removing_a_previous_generation_key_refunds_previous_cost_and_leaves_current_cost_unchanged()
    {
        var cache = new RecentCache<string, int>(10, 5);
        cache.Insert(1, "old", 5); // 0 >= 10 false, 0 > 0 false: current {old}, currentCost 5
        cache.Insert(2, "a", 3); // 5 > 2: previous {old}, current {a}, currentCost 3
        Assert.True(cache.RemoveValue("old", out var old)); Assert.Equal(1, old); // :38 currentEntry is nil: currentCost stays 3; :39 refunds only previousCost
        cache.Insert(3, "b", 3); // 3 > 2: previous {a}, current {b}, currentCost 3
        Assert.True(cache.Contains("a")); Assert.True(cache.Contains("b"));
        cache.Insert(4, "c", 5); // 3 > 0: previous {b}, current {c}; {a} discarded
        Assert.False(cache.Contains("a"));
        Assert.True(cache.Contains("b")); Assert.True(cache.Contains("c"));
        // Charging old's cost 5 to currentCost as well would leave it at -2: b joins {a,b} (-2 > 2 false), and c
        // rolls {a,b} into previous, so a would still be found.
    }
}
