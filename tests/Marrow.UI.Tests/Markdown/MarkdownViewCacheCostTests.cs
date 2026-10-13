using System.Reflection;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-perf-memory-4: the markdown caches are keyed by the whole text, so a count limit alone let a few very long messages (and tables)
// fill them for the whole run. Each entry now costs its text length and a generation holds a character budget; short text caches as before.
public sealed class MarkdownViewCacheCostTests
{
    private static T Cache<T>(Type owner, string field) =>
        (T)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static string Text(int index, int length) => ("prose-cost-probe-" + index + "-" + Guid.NewGuid() + " ").PadRight(length, 'x');

    [Fact]
    [Trait("Req", "PR-THEME-123")] // MarkdownView.swift:107 (blockCache); the budget is port-only
    public void Many_long_texts_do_not_all_stay_in_the_block_cache()
    {
        var cache = Cache<RecentCache<string, ValueList<MarkdownBlock>>>(typeof(MarkdownView), "blockCache");
        var texts = Enumerable.Range(0, 200).Select(index => Text(index, 50_000)).ToArray(); // 10 million characters in all

        foreach (var text in texts) MarkdownView.Blocks(text);

        var kept = texts.Count(cache.Contains);
        Assert.InRange(kept, 1, (int)(2 * MarkdownView.BlockCacheCostLimit / 50_000)); // two generations of the budget at most
        Assert.True(cache.Contains(texts[^1])); // the newest is always kept
        Assert.False(cache.Contains(texts[0]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-123")] // MarkdownView.swift:107 (blockCache): short messages cache exactly as before
    public void Short_texts_all_stay_in_the_block_cache_up_to_the_count_limit()
    {
        var cache = Cache<RecentCache<string, ValueList<MarkdownBlock>>>(typeof(MarkdownView), "blockCache");
        var texts = Enumerable.Range(0, 300).Select(index => Text(index, 200)).ToArray();

        foreach (var text in texts) MarkdownView.Blocks(text);

        Assert.All(texts, text => Assert.True(cache.Contains(text)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-123")] // MarkdownView.swift:107 (blockCache): one text over the whole budget is still cached
    public void One_text_larger_than_the_budget_is_still_cached_and_is_not_parsed_twice()
    {
        var cache = Cache<RecentCache<string, ValueList<MarkdownBlock>>>(typeof(MarkdownView), "blockCache");
        var text = Text(0, (int)MarkdownView.BlockCacheCostLimit + 10_000);

        var first = MarkdownView.Blocks(text);

        Assert.True(cache.Contains(text));
        Assert.Same(first, MarkdownView.Blocks(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-123")] // MarkdownView.swift:563 (prettyCache): the same budget rule
    public void Many_long_inline_sources_do_not_all_stay_in_the_pretty_cache()
    {
        var cache = Cache<RecentCache<string, AttributedText>>(typeof(RichLink), "prettyCache");
        var sources = Enumerable.Range(0, 100).Select(index => Text(index, 40_000)).ToArray(); // 4 million characters in all

        foreach (var source in sources) RichLink.PrettyAttributed(source);

        Assert.InRange(sources.Count(cache.Contains), 1, (int)(2 * MarkdownView.PrettyCacheCostLimit / 40_000));
        Assert.True(cache.Contains(sources[^1]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-123")] // MarkdownView.swift:1347 (cellCache): the same budget rule
    public void Many_long_table_cells_do_not_all_stay_in_the_cell_cache()
    {
        var cache = Cache<RecentCache<string, AttributedText>>(typeof(TableBlock), "cellCache");
        var cell = typeof(TableBlock).GetMethod("Cell", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sources = Enumerable.Range(0, 100).Select(index => Text(index, 20_000)).ToArray(); // 2 million characters in all

        foreach (var source in sources) cell.Invoke(null, [source]);

        Assert.InRange(sources.Count(cache.Contains), 1, (int)(2 * MarkdownView.CellCacheCostLimit / 20_000));
        Assert.True(cache.Contains(sources[^1]));
    }
}
