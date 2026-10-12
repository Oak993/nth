using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W10, "Show search suggestions" (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2, 9). The engine's suggested searches join the address
// field's list under the typed row, above the reader's tabs and history, and the list never grows past its limit.
[Trait("Decision", "ED-NEW-web-w10-7")]
public sealed class VisusSuggestionsRemoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<VisusSuggestion> Local(string typed, IEnumerable<VisusHistoryEntry>? history = null, IReadOnlyList<VisusOpenTab>? tabs = null) =>
        VisusSuggestions.For(typed, SearchEngine.DuckDuckGo, tabs ?? [], history ?? [], null, VisusTabKind.Browse, Now);

    private static VisusHistoryEntry Visit(string url, string title) => new(url, title, Now, 3, WebProfileKind.Browse);

    [Fact]
    public void Suggested_searches_go_under_the_typed_row_and_above_history()
    {
        var local = Local("hel", [Visit("https://hel.example.test/", "Hel page")]);
        var merged = VisusSuggestions.WithRemote(local, ["hello", "help"], SearchEngine.DuckDuckGo);
        Assert.Equal([VisusSuggestionKind.Search, VisusSuggestionKind.Search, VisusSuggestionKind.Search, VisusSuggestionKind.History], merged.Select(row => row.Kind));
        Assert.Equal(["hel", "hello", "help"], merged.Take(3).Select(row => row.Title));
        Assert.Equal("https://duckduckgo.com/?q=hello", merged[1].Url);
        Assert.Equal("Search DuckDuckGo", merged[1].Detail);
    }

    [Fact]
    public void At_most_three_are_added_and_the_list_keeps_its_limit()
    {
        var history = Enumerable.Range(0, 8).Select(n => Visit($"https://hel{n}.example.test/", "Hel " + n));
        var merged = VisusSuggestions.WithRemote(Local("hel", history), ["a1", "a2", "a3", "a4", "a5"], SearchEngine.DuckDuckGo);
        Assert.Equal(VisusSuggestions.Limit, merged.Count);
        Assert.Equal(3, merged.Count(row => row.Kind == VisusSuggestionKind.Search) - 1);
        Assert.Equal("hel", merged[0].Title);
    }

    [Fact]
    public void A_suggestion_equal_to_the_typed_row_or_repeated_is_not_added_twice()
    {
        var merged = VisusSuggestions.WithRemote(Local("hel"), ["HEL", "hello", "Hello", "help"], SearchEngine.Bing);
        Assert.Equal(["hel", "hello", "help"], merged.Select(row => row.Title));
    }

    [Fact]
    public void An_address_or_an_app_link_has_nothing_to_add_to()
    {
        var address = Local("example.com");
        Assert.Same(address, VisusSuggestions.WithRemote(address, ["example", "examples"], SearchEngine.DuckDuckGo));
        var empty = Local("");
        Assert.Same(empty, VisusSuggestions.WithRemote(empty, ["x"], SearchEngine.DuckDuckGo));
    }

    [Fact]
    public void No_suggestions_leave_the_list_as_it_was()
    {
        var local = Local("hel");
        Assert.Same(local, VisusSuggestions.WithRemote(local, [], SearchEngine.DuckDuckGo));
    }
}
