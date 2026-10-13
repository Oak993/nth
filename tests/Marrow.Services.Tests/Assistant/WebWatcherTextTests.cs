using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "Web page watch"; ED-NEW-dextra-as10-7): a page's HTML as the text a person reads (scripts,
// styles and comments gone, blocks on their own lines, entities decoded), the first price in a region, and the diff the card shows.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-7")]
public sealed class WebWatcherTextTests
{
    [Fact]
    public void Scripts_styles_comments_and_the_head_are_gone_and_blocks_become_lines()
    {
        const string html = "<html><head><title>Shop</title><style>p{color:red}</style></head><body><!-- ad --><h1>Barista &amp; Co</h1>"
            + "<script>alert('x')</script><p>Price:&nbsp;<b>$689</b></p><ul><li>One</li><li>Two</li></ul><br>Last&#33;</body></html>";
        Assert.Equal("Barista & Co\nPrice: $689\nOne\nTwo\nLast!", HtmlText.ToText(html));
    }

    [Fact]
    public void Runs_of_spaces_and_blank_lines_collapse()
    {
        Assert.Equal("a b\nc", HtmlText.ToText("<p>a    b</p>\n\n\n<p>  c  </p>"));
    }

    [Theory]
    [InlineData("<div id=\"root\"></div><script src=\"a.js\"></script>", "", true)]
    [InlineData("<div id=app></div><script>boot()</script><p>Loading</p>", "Loading", true)]
    [InlineData("<body><noscript>You need to enable JavaScript to run this app.</noscript></body>", "You need to enable JavaScript to run this app.", true)]
    [InlineData("<script>track()</script><p>A short page with a few real lines of its own to read here.</p>", "A short page with a few real lines of its own to read here.", false)]
    [InlineData("<p>Plain page with no script at all but not much text</p>", "Plain", false)]
    public void A_page_that_builds_itself_in_a_browser_is_told_from_one_that_does_not(string html, string text, bool needsBrowser) =>
        Assert.Equal(needsBrowser, HtmlText.NeedsBrowser(html, text));

    [Theory]
    [InlineData("Price: $689", 689)]
    [InlineData("Now only $1,299.00 was $1,499.00", 1299)]
    [InlineData("€42 each", 42)]
    [InlineData("£3.50", 3.5)]
    [InlineData("PKR 12,500 only", 12500)]
    [InlineData("Rs. 500", 500)]
    [InlineData("USD 19.99", 19.99)]
    [InlineData("total 1,234.56 due", 1234.56)]
    [InlineData("$ 7", 7)]
    public void The_first_price_in_a_text_is_found(string text, double price) => Assert.Equal((decimal)price, PriceFinder.First(text));

    [Theory]
    [InlineData("")]
    [InlineData("call us for a quote")]
    [InlineData("in stock: 3")]
    [InlineData("version 12")]
    public void A_text_with_no_price_gives_none(string text) => Assert.Null(PriceFinder.First(text));

    [Fact]
    public void The_diff_shows_what_went_and_what_came_in_order()
    {
        Assert.Equal("- Price: $689\n+ Price: $642", PageDiff.Make("Price: $689\nIn stock", "Price: $642\nIn stock"));
        Assert.Equal("+ New line", PageDiff.Make("a\nb", "a\nNew line\nb"));
        Assert.Equal("- b", PageDiff.Make("a\nb\nc", "a\nc"));
    }

    [Fact]
    public void A_large_diff_is_cut_to_what_a_card_can_hold()
    {
        var before = string.Join("\n", Enumerable.Range(0, 400).Select(i => "line " + i));
        var after = string.Join("\n", Enumerable.Range(0, 400).Select(i => "line " + (i + 1000)));
        var diff = PageDiff.Make(before, after);
        Assert.True(diff.Length <= PageDiff.CharLimit + 2);
        Assert.StartsWith("- line 0", diff, StringComparison.Ordinal);
    }
}
