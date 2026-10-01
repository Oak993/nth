using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using ChromeNs = Marrow.UI.Chrome;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/ProseRunView.swift (B3 52bc792), :17 and :123-199, the finished-reply
// ("prose run") path that the notes of UI-THEME-028, 029, 031, 032 and 033 describe next to the block path. The
// per-block numbers of this path are also asserted under PR-THEME-149 by ProseRunViewTests; this file adds what
// those records state in their own words: sizes from an unrounded environment size, the heading colour inside a
// quote, markers as text characters in the text colour, nested quote indents and the rule that is dropped.
[Collection("Headless")]
public sealed class MarkdownProsePathTests(HeadlessSession headless)
{
    private const string Bullet = "\U00002022"; // ProseRunView.swift:171
    private const string HollowBullet = "\U000025E6"; // ProseRunView.swift:171
    private const string Checked = "\U00002611"; // ProseRunView.swift:167
    private const string Unchecked = "\U00002610"; // ProseRunView.swift:167

    private static ValueList<MarkdownBlock> Blocks(params MarkdownBlock[] blocks) => new(blocks);

    private static MarkdownBlock P(string text) => new MarkdownBlock.Paragraph(text);

    private static MarkdownBlock H(int level, string text) => new MarkdownBlock.Heading(level, text);

    private static MarkdownListItem Item(string text, bool? @checked = null, params MarkdownBlock[] children) =>
        new(text, @checked, new ValueList<MarkdownBlock>(children));

    private static MarkdownBlock List(bool ordered, int start, params MarkdownListItem[] items) =>
        new MarkdownBlock.List(ordered, start, new ValueList<MarkdownListItem>(items));

    private static MarkdownBlock Quote(params MarkdownBlock[] blocks) => new MarkdownBlock.Quote(new ValueList<MarkdownBlock>(blocks));

    private static ParagraphText Attributed(ValueList<MarkdownBlock> blocks, double pointSize = 13) =>
        ProseRunRepresentable.Attributed(blocks, pointSize, false, []);

    private static void AssertColour(SwiftColor expected, SwiftColor? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual!.Resolve(ThemeVariant.Light));
        Assert.Equal(expected.Resolve(ThemeVariant.Dark), actual.Resolve(ThemeVariant.Dark));
    }

    private static int IndexOf(ParagraphText text, string value) => text.String.IndexOf(value, System.StringComparison.Ordinal);

    // :17 the run's size is the environment's markdown size to the nearest half point; :139-145 a heading is that
    // times 1.35, 1.2 or 1.08, each rounded, and levels 4 to 6 keep it. 14.2 -> 14, so 18.9, 16.8 and 15.12 round to 19, 17 and 15; 17 stays 17.
    [Theory]
    [Trait("Req", "UI-THEME-028")]
    [InlineData(14.2, 1, 19)]
    [InlineData(14.2, 2, 17)]
    [InlineData(14.2, 3, 15)]
    [InlineData(14.2, 4, 14)]
    [InlineData(14.2, 5, 14)]
    [InlineData(14.2, 6, 14)]
    [InlineData(17, 1, 23)] // 22.95
    [InlineData(17, 2, 20)] // 20.4
    [InlineData(17, 3, 18)] // 18.36
    public void A_finished_heading_is_the_half_point_rounded_size_times_the_level_multiplier_rounded(double environment, int level, double expected) => headless.OnUiThread(() =>
    {
        var host = new Decorator();
        MarkdownView.SetMarkdownPointSize(host, environment);
        var run = new ProseRunView(Blocks(H(level, "Title")));
        host.Child = run;
        var window = MarkdownVisualProbe.Show(host);

        var view = Assert.IsType<LinkTextView>(Assert.IsType<ProseRunRepresentable>(run.Child).Child);
        Assert.Equal(SwiftFont.SystemFont(expected, SwiftFont.Weight.Semibold), view.TextStorageForTests!.RunAt(0).Font);
        window.Close();
    });

    // UI-THEME-028 colours: NSColor.labelColor.withAlphaComponent(0.7) for a heading inside a quote (:126, :194).
    [Fact]
    [Trait("Req", "UI-THEME-028")]
    [Trait("Req", "UI-THEME-032")]
    public void A_finished_heading_inside_a_quote_is_the_label_colour_at_70_percent_and_one_outside_it_is_not() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(H(2, "Plain"), Quote(H(2, "Quoted"))));

        AssertColour(SwiftColor.LabelColor, text.RunAt(IndexOf(text, "Plain")).ForegroundColor);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(IndexOf(text, "Quoted")).ForegroundColor);
    });

    // UI-THEME-029 / 031 notes: in a finished reply the markers are the characters "•", "◦", "☑" and "☐" in the
    // text colour (:126, :173-176), not an accent dot, and a quote dims them to 70 percent.
    [Fact]
    [Trait("Req", "UI-THEME-029")]
    [Trait("Req", "UI-THEME-031")]
    public void A_finished_lists_markers_are_text_characters_in_the_text_colour_not_the_accent() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(
            List(false, 1, Item("top", null, List(false, 1, Item("nested"))), Item("done", true), Item("todo", false)),
            Quote(List(false, 1, Item("quoted")))));

        Assert.Contains(Bullet + "	top", text.String, System.StringComparison.Ordinal);
        Assert.Contains(HollowBullet + "	nested", text.String, System.StringComparison.Ordinal);
        Assert.Contains(Checked + "	done", text.String, System.StringComparison.Ordinal);
        Assert.Contains(Unchecked + "	todo", text.String, System.StringComparison.Ordinal);
        foreach (var marker in new[] { Bullet, HollowBullet, Checked, Unchecked })
        {
            var at = IndexOf(text, marker);
            Assert.True(at >= 0, marker);
            AssertColour(SwiftColor.LabelColor, text.RunAt(at).ForegroundColor);
            Assert.NotEqual(
                ChromeNs.Chrome.Accent.Resolve(ThemeVariant.Light),
                text.RunAt(at).ForegroundColor!.Resolve(ThemeVariant.Light));
        }
        var quotedBullet = text.String.LastIndexOf(Bullet, System.StringComparison.Ordinal);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(quotedBullet).ForegroundColor);
    });

    // UI-THEME-032 notes: no bar, a 14 point extra indent with dimmed text, so a quote in a quote is 28 (:194).
    [Fact]
    [Trait("Req", "UI-THEME-032")]
    public void A_finished_nested_quote_adds_14_points_of_indent_for_each_level_and_no_bar_character() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(Quote(P("one"), Quote(P("two")))));

        Assert.Equal("one\ntwo", text.String);
        Assert.Equal(14, text.RunAt(IndexOf(text, "one")).Style!.FirstLineHeadIndent);
        Assert.Equal(28, text.RunAt(IndexOf(text, "two")).Style!.FirstLineHeadIndent);
        Assert.Equal(28, text.RunAt(IndexOf(text, "two")).Style!.HeadIndent);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(IndexOf(text, "two")).ForegroundColor);
    });

    // UI-THEME-033 notes: a rule inside a prose run is dropped entirely (:196-197), and from copied plain text.
    [Fact]
    [Trait("Req", "UI-THEME-033")]
    public void A_rule_inside_a_finished_prose_run_adds_nothing_to_the_text_or_the_copied_text() => headless.OnUiThread(() =>
    {
        var blocks = Blocks(Quote(P("above"), new MarkdownBlock.Rule(), P("below")));

        Assert.Equal("above\nbelow", Attributed(blocks).String);
        Assert.Equal("above\nbelow", ProseRunRepresentable.JoinedSources(blocks));
    });

    // :25-45 a rule at the top level closes the prose run and is drawn as its own block, between two runs.
    [Fact]
    [Trait("Req", "UI-THEME-033")]
    public void A_top_level_rule_in_a_finished_reply_is_a_line_between_two_prose_runs() => headless.OnUiThread(() =>
    {
        var view = new MarkdownView("above\n\n---\n\nbelow");
        var window = MarkdownVisualProbe.Show(view);

        var runs = VisualQuery.Descendants<ProseRunView>(view).ToList();
        Assert.Equal(2, runs.Count);
        var line = Assert.Single(VisualQuery.Descendants<Rectangle>(view));
        Assert.Equal(1, line.Bounds.Height, 3);
        Assert.True(runs[0].Bounds.Bottom <= MarkdownVisualProbe.Origin(line, view).Y);
        Assert.True(MarkdownVisualProbe.Origin(line, view).Y + line.Bounds.Height <= runs[1].Bounds.Top);
        window.Close();
    });
}
