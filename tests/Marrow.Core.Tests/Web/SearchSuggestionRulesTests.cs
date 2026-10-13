using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W10, "Show search suggestions" (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2, 9). What may be sent to a search engine at all,
// where it goes, and how the answer is read.
[Trait("Decision", "ED-NEW-web-w10-7")]
public sealed class SearchSuggestionRulesTests
{
    [Theory]
    [InlineData("weather in oslo", true)]
    [InlineData("  weather in oslo  ", true)]
    [InlineData("ab", true)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("example.com", false)]
    [InlineData("https://example.com/page", false)]
    [InlineData("http://localhost:5173", false)]
    [InlineData("localhost:5173", false)]
    [InlineData("192.168.0.4", false)]
    [InlineData("C:\\Users\\fake\\notes.txt", false)]
    [InlineData("file:///C:/fake.txt", false)]
    [InlineData("mailto:fake@example.test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("user@example.test", false)]
    [InlineData("call me at dana@work", false)]
    [InlineData("4111 1111 1111 1111", false)]
    [InlineData("account 123456789", false)]
    [InlineData("iphone 15 pro max 256gb", true)]
    [InlineData("rfc 9110 section 15", true)]
    public void Only_a_search_phrase_may_leave_the_pc(string? typed, bool allowed) =>
        Assert.Equal(allowed, SearchSuggestionRules.MaySend(typed, SearchEngine.DuckDuckGo));

    [Fact]
    public void A_text_over_the_length_limit_or_with_control_characters_is_not_sent()
    {
        Assert.True(SearchSuggestionRules.MaySend(new string('a', SearchSuggestionRules.MaxQueryLength), SearchEngine.Bing));
        Assert.False(SearchSuggestionRules.MaySend(new string('a', SearchSuggestionRules.MaxQueryLength + 1), SearchEngine.Bing));
        Assert.False(SearchSuggestionRules.MaySend("two\nlines", SearchEngine.Bing));
        Assert.False(SearchSuggestionRules.MaySend("tab\there", SearchEngine.Bing));
    }

    [Theory]
    [InlineData(SearchEngine.DuckDuckGo, "https://duckduckgo.com/ac/?type=list&q=hello%20world")]
    [InlineData(SearchEngine.Bing, "https://api.bing.com/osjson.aspx?query=hello%20world")]
    [InlineData(SearchEngine.Google, "https://suggestqueries.google.com/complete/search?client=firefox&q=hello%20world")]
    [InlineData(SearchEngine.Brave, "https://search.brave.com/api/suggest?q=hello%20world")]
    [InlineData(SearchEngine.Startpage, "https://www.startpage.com/suggestions?format=opensearch&q=hello%20world")]
    public void Each_engine_has_one_fixed_https_address_and_the_text_is_escaped(SearchEngine engine, string expected) =>
        Assert.Equal(expected, SearchSuggestionRules.RequestUrl(engine, "hello world"));

    [Fact]
    public void The_text_cannot_change_the_host_or_add_parameters()
    {
        var url = SearchSuggestionRules.RequestUrl(SearchEngine.Google, "a&client=evil#frag?x=//evil.test/")!;
        var uri = new Uri(url);
        Assert.Equal("suggestqueries.google.com", uri.Host);
        Assert.Equal(string.Empty, uri.Fragment);
        Assert.Equal("?client=firefox&q=a%26client%3Devil%23frag%3Fx%3D%2F%2Fevil.test%2F", uri.Query);
    }

    [Fact]
    public void A_text_that_may_not_be_sent_has_no_address() => Assert.Null(SearchSuggestionRules.RequestUrl(SearchEngine.Bing, "https://example.com/private?token=abc"));

    [Fact]
    public void The_open_search_answer_every_engine_uses_is_read_and_the_typed_text_is_left_out()
    {
        var read = SearchSuggestionRules.Parse("[\"hel\",[\"hello\",\"help\",\"HEL\",\"hello\",\"helium\",\"helmet\",\"helsinki\"]]", "hel");
        Assert.Equal(["hello", "help", "helium", "helmet", "helsinki"], read);
    }

    [Fact]
    public void An_object_with_a_suggestions_list_is_read_too()
    {
        Assert.Equal(["a b", "c d"], SearchSuggestionRules.Parse("{\"suggestions\":[{\"text\":\"a b\"},{\"phrase\":\"c d\"},{\"other\":1},7]}", "x"));
        Assert.Equal(["a", "b"], SearchSuggestionRules.Parse("{\"suggestions\":[\"a\",\"b\"]}", "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("[\"q\"]")]
    [InlineData("[\"q\",\"not a list\"]")]
    [InlineData("{\"suggestions\":\"nope\"}")]
    [InlineData("<html>blocked</html>")]
    [InlineData("42")]
    public void Anything_else_is_no_suggestions(string? body) => Assert.Empty(SearchSuggestionRules.Parse(body, "q"));

    [Fact]
    public void A_suggestion_that_is_empty_long_or_has_control_characters_is_dropped()
    {
        var long_ = new string('x', SearchSuggestionRules.MaxQueryLength + 1);
        var read = SearchSuggestionRules.Parse("[\"q\",[\"\",\"  \",\"good one\",\"bad\\nline\",\"" + long_ + "\",\"fine\"]]", "q");
        Assert.Equal(["good one", "fine"], read);
    }
}
