using System.Text;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3 fallback: fenced nth-ui and nth-html blocks, parsed while the answer
// streams; ED-NEW-web-w24-2). Every split of a stream must find the same text and the same blocks, a block's text must arrive as it
// is written (never held back except a line that can still be the closing fence), and a fence of the answer's own code is code.
[Trait("Decision", "ED-NEW-web-w24-2")]
public sealed class NthUiFenceTests
{
    private const string Sample =
        "At 400 a month you reach it in 30 months.\n"
        + "```nth-ui id=savings\n"
        + "card \"Savings goal\"\n"
        + "  state goal = 12000\n"
        + "  stat \"Goal\" = goal\n"
        + "```\n"
        + "Try the slider.";

    /// <summary>Feeds the text cut at <paramref name="cuts"/> and returns the parts, with neighbouring ones joined as the chat would append them.</summary>
    private static List<LiveFencePart> Stream(string text, IEnumerable<int> cuts)
    {
        var parser = new LiveFenceParser();
        var all = new List<LiveFencePart>();
        var at = 0;
        foreach (var cut in cuts.Append(text.Length))
        {
            all.AddRange(parser.Feed(text[at..cut]));
            at = cut;
        }
        all.AddRange(parser.Finish());
        return Join(all);
    }

    private static List<LiveFencePart> Join(IEnumerable<LiveFencePart> parts)
    {
        var joined = new List<LiveFencePart>();
        foreach (var part in parts)
        {
            if (part is LiveFencePart.Text text && joined.Count > 0 && joined[^1] is LiveFencePart.Text before) joined[^1] = new LiveFencePart.Text(before.Value + text.Value);
            else if (part is LiveFencePart.Content content && joined.Count > 0 && joined[^1] is LiveFencePart.Content earlier && earlier.Index == content.Index)
                joined[^1] = new LiveFencePart.Content(content.Index, earlier.Value + content.Value);
            else joined.Add(part);
        }
        return joined;
    }

    private static string Describe(IEnumerable<LiveFencePart> parts) => string.Join(" | ", parts.Select(part => part switch
    {
        LiveFencePart.Text text => "T:" + text.Value.Replace("\n", "\\n", StringComparison.Ordinal),
        LiveFencePart.Opened opened => $"O{opened.Index}:{opened.Header.Kind}:{opened.Header.Id}:{opened.Header.Title}:{opened.Header.Height}:{opened.Header.Lite}",
        LiveFencePart.Content content => $"C{content.Index}:" + content.Value.Replace("\n", "\\n", StringComparison.Ordinal),
        LiveFencePart.Closed closed => $"X{closed.Index}:{closed.Complete}",
        _ => "?",
    }));

    [Fact]
    public void A_block_is_found_with_its_text_and_the_answer_text_around_it()
    {
        var parts = LiveFenceParser.Parse(Sample);
        Assert.Equal(
            "T:At 400 a month you reach it in 30 months.\\n"
            + " | O0:Ui:savings:::False"
            + " | C0:card \"Savings goal\"\\n  state goal = 12000\\n  stat \"Goal\" = goal\\n"
            + " | X0:True"
            + " | T:Try the slider.",
            Describe(parts));
    }

    [Fact]
    public void Every_way_of_cutting_the_stream_finds_the_same_text_and_blocks()
    {
        var whole = Describe(Stream(Sample, []));
        for (var cut = 1; cut < Sample.Length; cut++)
            Assert.Equal(whole, Describe(Stream(Sample, [cut])));
        foreach (var piece in new[] { 1, 2, 3, 5, 8, 13 })
            Assert.Equal(whole, Describe(Stream(Sample, Enumerable.Range(1, Sample.Length / piece).Select(n => n * piece).Where(n => n < Sample.Length))));
    }

    [Fact]
    public void A_block_streams_as_it_is_written_and_only_a_line_that_could_close_it_waits()
    {
        var parser = new LiveFenceParser();
        parser.Feed("Sure.\n```nth-ui\n");
        Assert.Equal(0, parser.OpenIndex);

        // A text line arriving is handed over at once, in the middle of the line.
        Assert.Equal("text \"Hel", Assert.IsType<LiveFencePart.Content>(Assert.Single(parser.Feed("text \"Hel"))).Value);
        Assert.Equal("lo", Assert.IsType<LiveFencePart.Content>(Assert.Single(parser.Feed("lo"))).Value);

        // A line of only backticks waits: it may be the closing fence.
        Assert.Equal("\"\n", Assert.IsType<LiveFencePart.Content>(Assert.Single(parser.Feed("\"\n``"))).Value);
        var closed = parser.Feed("`\n");
        Assert.Equal([new LiveFencePart.Closed(0, true)], closed);
        Assert.Null(parser.OpenIndex);

        // The same pair of backticks, followed by more, was content after all.
        var other = new LiveFenceParser();
        other.Feed("```nth-ui\n");
        Assert.Empty(other.Feed("``"));
        Assert.Equal("``x", Assert.IsType<LiveFencePart.Content>(Assert.Single(other.Feed("x"))).Value);
    }

    [Fact]
    public void A_line_that_could_open_a_block_waits_and_any_other_text_is_shown_at_once()
    {
        var parser = new LiveFenceParser();
        Assert.Empty(parser.Feed("``"));
        Assert.Empty(parser.Feed("`nth-"));
        Assert.Equal([new LiveFencePart.Text("```nth-x\n")], parser.Feed("x\n"));

        var plain = new LiveFenceParser();
        Assert.Equal([new LiveFencePart.Text("Hello wor")], plain.Feed("Hello wor"));
        Assert.Equal([new LiveFencePart.Text("ld")], plain.Feed("ld"));
    }

    [Fact]
    public void A_fence_of_the_answers_own_code_is_code_and_a_view_inside_it_is_shown_not_parsed()
    {
        const string text = "Write it like this:\n```markdown\n```nth-ui\ncard \"Not me\"\n```\nThen:\n```nth-ui\ntext \"Me\"\n```\n";
        var parts = LiveFenceParser.Parse(text);
        var opened = parts.OfType<LiveFencePart.Opened>().ToList();
        Assert.Single(opened);
        Assert.Equal("text \"Me\"\n", Assert.IsType<LiveFencePart.Content>(Assert.Single(parts, p => p is LiveFencePart.Content)).Value);
        var shown = string.Concat(parts.OfType<LiveFencePart.Text>().Select(t => t.Value));
        Assert.Contains("```nth-ui\ncard \"Not me\"\n```\n", shown, StringComparison.Ordinal);
        Assert.StartsWith("Write it like this:\n```markdown\n", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void Tilde_fences_a_longer_closer_and_an_indent_of_up_to_three_spaces_work_and_four_spaces_is_not_a_fence()
    {
        var parts = LiveFenceParser.Parse("~~~nth-ui\ntext \"a\"\n~~~~\n   ```nth-html height=200\n<p>hi</p>\n   ```\n    ```nth-ui\nnot a block\n");
        Assert.Equal(["Ui", "Html"], parts.OfType<LiveFencePart.Opened>().Select(o => o.Header.Kind.ToString()));
        Assert.Equal(200, parts.OfType<LiveFencePart.Opened>().Last().Header.Height);
        Assert.Contains("    ```nth-ui\nnot a block\n", string.Concat(parts.OfType<LiveFencePart.Text>().Select(t => t.Value)), StringComparison.Ordinal);
        Assert.All(parts.OfType<LiveFencePart.Closed>(), closed => Assert.True(closed.Complete));
    }

    [Fact]
    public void An_answer_that_ends_inside_a_block_closes_it_as_cut_off_and_keeps_its_last_line()
    {
        var parts = LiveFenceParser.Parse("Here:\n```nth-ui\ncard \"Half\"\n  text \"still arri");
        Assert.Equal(new LiveFencePart.Closed(0, false), parts[^1]);
        Assert.Equal("card \"Half\"\n  text \"still arri", string.Concat(parts.OfType<LiveFencePart.Content>().Select(c => c.Value)));

        // A closing fence with no line break after it still closes the block.
        var tidy = LiveFenceParser.Parse("```nth-ui\ntext \"a\"\n```");
        Assert.Equal(new LiveFencePart.Closed(0, true), tidy[^1]);
    }

    [Fact]
    public void The_opener_reads_id_title_height_and_lite_and_only_the_two_tags_open_a_block()
    {
        var header = LiveFenceHeader.Parse("nth-ui lite id=\"Hosting Plan\" title='A \"plan\"'")!;
        Assert.Equal((LiveFenceKind.Ui, "hosting-plan", "A \"plan\"", null, true), (header.Kind, header.Id, header.Title, header.Height, header.Lite));
        var html = LiveFenceHeader.Parse("NTH-HTML height=360 name=Pong")!;
        Assert.Equal((LiveFenceKind.Html, "Pong", 360, false), (html.Kind, html.Title, html.Height, html.Lite));

        Assert.Null(LiveFenceHeader.Parse("nth-uix"));
        Assert.Null(LiveFenceHeader.Parse("nth-artifact id=x"));
        Assert.Null(LiveFenceHeader.Parse("python"));
        Assert.Empty(LiveFenceParser.Parse("```nth-uix\ntext\n```\n").OfType<LiveFencePart.Opened>());
    }

    [Fact]
    public void Two_blocks_in_one_answer_get_their_own_indexes_and_windows_line_ends_are_kept_in_the_content()
    {
        var parts = LiveFenceParser.Parse("One\r\n```nth-ui\r\ntext \"a\"\r\n```\r\nTwo\r\n```nth-html\r\n<b>x</b>\r\n```\r\n");
        Assert.Equal([0, 1], parts.OfType<LiveFencePart.Opened>().Select(o => o.Index));
        Assert.Equal("text \"a\"\r\n", parts.OfType<LiveFencePart.Content>().First().Value);
        Assert.Equal("One\r\n", ((LiveFencePart.Text)parts[0]).Value);
    }

    [Fact]
    public void View_ids_are_lower_case_letters_digits_and_hyphens()
    {
        Assert.Equal("hosting-plan-2", LiveViewIds.Normalize("  Hosting_Plan 2 "));
        Assert.Null(LiveViewIds.Normalize("!!!"));
        Assert.Null(LiveViewIds.Normalize(null));
        Assert.True(LiveViewIds.Normalize(new string('a', 200))!.Length <= LiveViewIds.MaxLength);
        Assert.Equal("a-b", LiveViewIds.Normalize("a--b"));
    }

    [Fact]
    public void Random_text_never_throws_and_every_split_agrees_with_the_whole()
    {
        var random = new Random(24);
        const string alphabet = "`~ \n\rnth-uihtml=\"'abc{}#";
        for (var round = 0; round < 400; round++)
        {
            var builder = new StringBuilder();
            for (var i = random.Next(1, 120); i > 0; i--) builder.Append(alphabet[random.Next(alphabet.Length)]);
            if (random.Next(3) == 0) builder.Insert(random.Next(builder.Length + 1), "\n```nth-ui id=r\n");
            var text = builder.ToString();
            var whole = Describe(Stream(text, []));
            var cut = random.Next(1, Math.Max(2, text.Length));
            Assert.Equal(whole, Describe(Stream(text, [cut])));
            // Whatever the cuts, the text shown and the block text together hold every character of the input except fence lines.
            var parts = Stream(text, [cut]);
            Assert.True(string.Concat(parts.OfType<LiveFencePart.Text>().Select(t => t.Value)).Length + string.Concat(parts.OfType<LiveFencePart.Content>().Select(c => c.Value)).Length <= text.Length);
        }
    }
}
