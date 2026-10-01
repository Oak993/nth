using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Avalonia.Media;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), FaviconCache.image(for:) :896-901, the
// store step: `if memory.count >= 300 { memory.removeAll(keepingCapacity: true) }; memory[key] = image`, or
// `failed.insert(key)` when there is no icon. PR-THEME-139 (upstream defect 49). The step is private and runs
// only after a real fetch, so it is driven through the port's own method for it (FaviconCache.Remember) with no
// network; the cache's two sets are read back by reflection, the same way FaviconCacheImagePipelineTests reaches
// the private members of the pipeline.
// The cache is process-wide, so every test starts by emptying both sets and uses host names no other test does.
public sealed class FaviconCacheFlushTests
{
    private static readonly MethodInfo RememberMethod = typeof(FaviconCache).GetMethod("Remember", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static IDictionary Memory() => (IDictionary)typeof(FaviconCache).GetField("memory", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static HashSet<string> Failed() => (HashSet<string>)typeof(FaviconCache).GetField("failed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static void Reset()
    {
        Memory().Clear();
        Failed().Clear();
    }

    private static void Remember(string host, IImage? image) => RememberMethod.Invoke(null, [host, image]);

    private static string Host(int index) => "flush-" + index.ToString(CultureInfo.InvariantCulture) + ".example";

    // :933 `>= 300` flushes before the store, so the 300th icon still fits and the 301st empties the map.
    [Fact]
    [Trait("Req", "PR-THEME-139")]
    public void The_301st_icon_discards_all_300_and_becomes_the_only_entry()
    {
        Reset();
        for (var index = 0; index < 300; index++) Remember(Host(index), new StandInImage());
        Assert.Equal(300, Memory().Count);
        Assert.NotNull(FaviconCache.Cached(Host(0)));
        Assert.NotNull(FaviconCache.Cached(Host(299)));

        Remember(Host(300), new StandInImage());

        Assert.Single(Memory());
        Assert.NotNull(FaviconCache.Cached(Host(300)));
        for (var index = 0; index < 300; index++) Assert.Null(FaviconCache.Cached(Host(index)));
        Reset();
    }

    // :933-934 a wholesale flush, not an LRU eviction: it starts over, and the cache fills towards 300 again.
    [Fact]
    [Trait("Req", "PR-THEME-139")]
    public void After_a_flush_the_cache_fills_again_and_flushes_again_at_the_next_300()
    {
        Reset();
        for (var index = 0; index <= 300; index++) Remember(Host(index), new StandInImage());
        for (var index = 301; index < 600; index++) Remember(Host(index), new StandInImage());
        Assert.Equal(300, Memory().Count); // the 301st started a new map; 299 more brought it to 300
        Assert.NotNull(FaviconCache.Cached(Host(300)));

        Remember(Host(600), new StandInImage());

        Assert.Single(Memory());
        Assert.Null(FaviconCache.Cached(Host(300)));
        Assert.NotNull(FaviconCache.Cached(Host(600)));
        Reset();
    }

    // :935-936 a host with no icon goes to the failed set instead, and takes nothing out of the icon map.
    [Fact]
    [Trait("Req", "PR-THEME-139")]
    public void A_host_with_no_icon_is_recorded_as_failed_and_the_icon_map_is_left_alone()
    {
        Reset();
        Remember(Host(1), new StandInImage());

        Remember(Host(2), null);

        Assert.Contains(Host(2), Failed());
        Assert.Null(FaviconCache.Cached(Host(2)));
        Assert.Single(Memory());
        Reset();
    }

    // :936 nothing ever clears the failed set: not the flush of the icon map, and not storing more icons.
    [Fact]
    [Trait("Req", "PR-THEME-139")]
    public void The_failed_set_survives_the_flush_and_is_never_cleared()
    {
        Reset();
        Remember(Host(1000), null);
        for (var index = 0; index <= 300; index++) Remember(Host(index), new StandInImage()); // the last one flushes

        Assert.Single(Memory());
        Assert.Contains(Host(1000), Failed());
        Reset();
    }
}
