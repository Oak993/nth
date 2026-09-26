using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/ProseRunView.swift (B3 52bc792), :1-209. A run of prose blocks is ONE
// NSAttributedString in ONE NSTextView (LinkTextView, LinkParagraphView.swift:275-550); the port's representation
// is the ED-210 decision (ProseRunView unit): ParagraphText (the NSMutableAttributedString subset) laid out by
// ParagraphTextPresenter inside LinkTextView. Expected values are the Swift's numbers and characters.
// Non-ASCII expectations are written as eight-digit escapes (R-STR-9).
[Collection("Headless")]
public sealed class ProseRunViewTests(HeadlessSession headless)
{
    private const string Bullet = "\U00002022"; // U+2022 BULLET, ProseRunView.swift:171
    private const string HollowBullet = "\U000025E6"; // U+25E6 WHITE BULLET, ProseRunView.swift:171
    private const string Checked = "\U00002611"; // U+2611 BALLOT BOX WITH CHECK, ProseRunView.swift:167
    private const string Unchecked = "\U00002610"; // U+2610 BALLOT BOX, ProseRunView.swift:167

    private static readonly IReadOnlyDictionary<string, HydraMentionTarget> NoTargets = new Dictionary<string, HydraMentionTarget>();

    private static ValueList<MarkdownBlock> Blocks(params MarkdownBlock[] blocks) => new(blocks);

    private static MarkdownBlock P(string text) => new MarkdownBlock.Paragraph(text);

    private static MarkdownBlock H(int level, string text) => new MarkdownBlock.Heading(level, text);

    private static MarkdownListItem Item(string text, bool? isChecked = null, params MarkdownBlock[] children) =>
        new(text, isChecked, new ValueList<MarkdownBlock>(children));

    private static MarkdownBlock List(bool ordered, int start, params MarkdownListItem[] items) =>
        new MarkdownBlock.List(ordered, start, new ValueList<MarkdownListItem>(items));

    private static MarkdownBlock Quote(params MarkdownBlock[] blocks) => new MarkdownBlock.Quote(new ValueList<MarkdownBlock>(blocks));

    private static MarkdownBlock CodeBlock() => new MarkdownBlock.Code("swift", "let x = 1");

    private static MarkdownBlock TableBlock() =>
        new MarkdownBlock.Table(new ValueList<string>(["a"]), new ValueList<ValueList<string>>([new ValueList<string>(["1"])]));

    private static ParagraphText Attributed(ValueList<MarkdownBlock> blocks, double pointSize = 13, bool dimmed = false) =>
        ProseRunRepresentable.Attributed(blocks, pointSize, dimmed, []);

    private static void AssertColour(SwiftColor expected, SwiftColor? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual!.Resolve(ThemeVariant.Light));
        Assert.Equal(expected.Resolve(ThemeVariant.Dark), actual.Resolve(ThemeVariant.Dark));
    }

    private static ParagraphStyle Style(ParagraphText text, int index) =>
        text.RunAt(index).Style ?? throw new InvalidOperationException("no paragraph style at " + index);

    private static int IndexOf(ParagraphText text, string value) => text.String.IndexOf(value, StringComparison.Ordinal);

    private static ParagraphTextPresenter Laid(ParagraphText text, double width)
    {
        var presenter = new ParagraphTextPresenter();
        presenter.SetText(text);
        presenter.Layout(width);
        return presenter;
    }

    private static Rect CharacterRect(ParagraphTextPresenter presenter, int index)
    {
        var rects = presenter.RangeRects(index, 1);
        Assert.NotEmpty(rects);
        return rects[0];
    }

    // A real layout pass needs a hosting window (as LinkHoverTests.Show). The windows are left open, as there: closing
    // one detaches the run, whose hover end (LinkHover.swift:41-43) reaches the process-wide HydraHeadHoverPopover,
    // a Popup owned by whichever headless dispatcher thread first touched it.
    private static Window Show(Control content, double width = 400)
    {
        var window = new Window { Width = width, Height = 600, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // ---- joinedSources (PR-THEME-149) ----

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:90-112 (B3)
    public void Joined_sources_gather_heading_paragraph_list_item_and_quote_texts_and_drop_code_tables_and_rules()
    {
        var blocks = Blocks(
            H(1, "Title"),
            P("Para"),
            CodeBlock(),
            List(false, 1, Item("a", null, P("child")), Item("b")),
            Quote(P("q")),
            TableBlock(),
            new MarkdownBlock.Rule());

        Assert.Equal("Title\nPara\na\nchild\nb\nq", ProseRunRepresentable.JoinedSources(blocks));
    }

    // ---- attributed(blocks:) (PR-THEME-149) ----

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:114-121, :201-208 (B3)
    public void Blocks_become_one_string_separated_by_a_newline_in_the_base_font_and_colour() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("one"), P("two")));

        Assert.Equal("one\ntwo", text.String);
        var separator = text.RunAt(3);
        Assert.Equal(SwiftFont.SystemFont(13), separator.Font);
        AssertColour(SwiftColor.LabelColor, separator.ForegroundColor);
        Assert.Null(separator.Style);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:118, :128-136 (B3)
    public void A_top_level_paragraph_is_not_indented_and_carries_twelve_points_after() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("one")));

        var style = Style(text, 0);
        Assert.Equal(0, style.FirstLineHeadIndent);
        Assert.Equal(0, style.HeadIndent);
        Assert.Equal(12, style.ParagraphSpacing);
        Assert.Equal(0, style.ParagraphSpacingBefore);
        AssertColour(SwiftColor.LabelColor, text.RunAt(0).ForegroundColor);
        Assert.Equal(SwiftFont.SystemFont(13), text.RunAt(0).Font);
    });

    [Theory]
    [InlineData(1, 18, 6)] // (13.5 * 1.35).rounded() = 18.225 -> 18
    [InlineData(2, 16, 6)] // (13.5 * 1.2).rounded() = 16.2 -> 16
    [InlineData(3, 15, 2)] // (13.5 * 1.08).rounded() = 14.58 -> 15
    [InlineData(4, 13.5, 2)]
    [InlineData(5, 13.5, 2)]
    [InlineData(6, 13.5, 2)]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:137-161 (B3)
    public void A_heading_is_sized_by_level_set_semibold_and_spaced_before_by_level_and_twelve_after(int level, double size, double before) =>
        headless.OnUiThread(() =>
        {
            var text = Attributed(Blocks(H(level, "Title")), pointSize: 13.5);

            Assert.Equal("Title", text.String);
            Assert.Equal(SwiftFont.SystemFont(size, SwiftFont.Weight.Semibold), text.RunAt(0).Font);
            var style = Style(text, 0);
            Assert.Equal(before, style.ParagraphSpacingBefore);
            Assert.Equal(12, style.ParagraphSpacing);
            Assert.Equal(0, style.FirstLineHeadIndent);
            Assert.Equal(0, style.HeadIndent);
        });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:146-154 (B3), LinkParagraphView.swift:113-133
    public void A_heading_keeps_its_bold_and_monospaced_runs_and_turns_every_other_run_semibold() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(H(1, "a **b** *c* `d`")));

        Assert.Equal("a b c d", text.String);
        Assert.Equal(SwiftFont.SystemFont(18, SwiftFont.Weight.Semibold), text.RunAt(0).Font); // "a", (13 * 1.35).rounded()
        Assert.Equal(SwiftFont.BoldSystemFont(18), text.RunAt(2).Font); // "b": bold trait kept
        Assert.Equal(SwiftFont.SystemFont(18, SwiftFont.Weight.Semibold), text.RunAt(4).Font); // "c": italic has neither trait
        Assert.Equal(SwiftFont.MonospacedSystemFont(17, SwiftFont.Weight.Regular), text.RunAt(6).Font); // "d": max(9, 18 - 1)
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:162-176 (B3)
    public void List_markers_are_ballot_boxes_numbers_bullets_and_hollow_bullets_each_followed_by_a_tab() => headless.OnUiThread(() =>
    {
        Assert.Equal(
            Bullet + "\ta\n" + Checked + "\tb\n" + Unchecked + "\tc",
            Attributed(Blocks(List(false, 1, Item("a"), Item("b", true), Item("c", false)))).String);
        Assert.Equal("3.\tx\n4.\ty", Attributed(Blocks(List(true, 3, Item("x"), Item("y")))).String);
        Assert.Equal(Checked + "\tt", Attributed(Blocks(List(true, 1, Item("t", true)))).String);
        Assert.Equal(
            Bullet + "\ta\n" + HollowBullet + "\tb",
            Attributed(Blocks(List(false, 1, Item("a", null, List(false, 1, Item("b")))))).String);

        var text = Attributed(Blocks(List(false, 1, Item("a"))));
        Assert.Equal(SwiftFont.SystemFont(13), text.RunAt(0).Font);
        AssertColour(SwiftColor.LabelColor, text.RunAt(0).ForegroundColor);
        Assert.Equal(SwiftFont.SystemFont(13), text.RunAt(1).Font);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:177-179 (B3)
    public void An_empty_list_item_is_its_marker_and_tab_alone() => headless.OnUiThread(() =>
    {
        Assert.Equal(Bullet + "\t", Attributed(Blocks(List(false, 1, Item("")))).String);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:180-186 (B3)
    public void A_list_item_hangs_at_eighteen_points_past_its_indent_with_one_tab_stop_there_and_the_last_top_level_item_spaces_twelve() =>
        headless.OnUiThread(() =>
        {
            var text = Attributed(Blocks(List(false, 1, Item("a"), Item("b", null, List(false, 1, Item("c"), Item("d"))))));

            var first = Style(text, IndexOf(text, "a"));
            Assert.Equal(0, first.FirstLineHeadIndent);
            Assert.Equal(18, first.HeadIndent);
            Assert.Equal(new ValueList<double>([18]), first.TabStops);
            Assert.Equal(18, first.DefaultTabInterval);
            Assert.Equal(6, first.ParagraphSpacing);
            Assert.Equal(0, Style(text, 0).FirstLineHeadIndent); // the marker carries the same style

            Assert.Equal(12, Style(text, IndexOf(text, "b")).ParagraphSpacing); // last item, depth 0

            var nested = Style(text, IndexOf(text, "c"));
            Assert.Equal(18, nested.FirstLineHeadIndent);
            Assert.Equal(36, nested.HeadIndent);
            Assert.Equal(new ValueList<double>([36]), nested.TabStops);
            Assert.Equal(18, nested.DefaultTabInterval);
            Assert.Equal(6, nested.ParagraphSpacing);
            Assert.Equal(6, Style(text, IndexOf(text, "d")).ParagraphSpacing); // last item, but depth 1
        });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:188-190, :128-136 (B3)
    public void A_list_items_children_are_appended_eighteen_points_in_with_six_points_after() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(List(false, 1, Item("a", null, P("child")))));

        Assert.Equal(Bullet + "\ta\nchild", text.String);
        var child = Style(text, IndexOf(text, "child"));
        Assert.Equal(18, child.FirstLineHeadIndent);
        Assert.Equal(18, child.HeadIndent);
        Assert.Equal(6, child.ParagraphSpacing);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:126, :192-195, :201-208 (B3)
    public void Quoted_content_is_indented_fourteen_more_dimmed_to_seven_tenths_and_drawn_with_no_bar() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("a"), Quote(P("q"))));

        Assert.Equal("a\nq", text.String); // no bar character, no extra run
        var quoted = Style(text, 2);
        Assert.Equal(14, quoted.FirstLineHeadIndent);
        Assert.Equal(14, quoted.HeadIndent);
        Assert.Equal(12, quoted.ParagraphSpacing); // the quote passes its caller's spacing through
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(2).ForegroundColor);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(1).ForegroundColor); // its separator is dimmed too
        AssertColour(SwiftColor.LabelColor, text.RunAt(0).ForegroundColor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:189, :194 (B3)
    public void A_quote_inside_a_list_item_passes_on_the_items_six_points() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(List(false, 1, Item("a", null, Quote(P("q"))))));

        var quoted = Style(text, IndexOf(text, "q"));
        Assert.Equal(32, quoted.FirstLineHeadIndent); // 0 + 18 + 14
        Assert.Equal(32, quoted.HeadIndent);
        Assert.Equal(6, quoted.ParagraphSpacing);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:196-197, :202-203 (B3)
    public void Code_tables_and_rules_add_nothing_not_even_a_separator() => headless.OnUiThread(() =>
    {
        Assert.Equal(
            "a\nb",
            Attributed(Blocks(P("a"), CodeBlock(), TableBlock(), new MarkdownBlock.Rule(), P("b"))).String);
        Assert.Equal("", Attributed(Blocks(CodeBlock())).String);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:118, :126 (B3)
    public void A_dimmed_run_draws_every_block_in_the_label_colour_at_seven_tenths() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("a"), List(false, 1, Item("b"))), dimmed: true);

        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(0).ForegroundColor);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(1).ForegroundColor);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), text.RunAt(2).ForegroundColor); // the marker
    });

    // ---- the view: one text object at the rounded point size (PR-THEME-149) ----

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:17, :22 (B3)
    public void The_run_takes_the_markdown_point_size_rounded_to_the_nearest_half_point() => headless.OnUiThread(() =>
    {
        foreach (var (environment, expected) in new[] { (13.3, 13.5), (13.2, 13.0), (13.75, 14.0) })
        {
            var host = new Decorator();
            MarkdownView.SetMarkdownPointSize(host, environment);
            var run = new ProseRunView(Blocks(P("one")));
            host.Child = run;
            _ = Show(host);

            var representable = Assert.IsType<ProseRunRepresentable>(run.Child);
            Assert.Equal(expected, representable.PointSize);
            var view = Assert.IsType<LinkTextView>(representable.Child);
            Assert.Equal(SwiftFont.SystemFont(expected), view.TextStorageForTests!.RunAt(0).Font);
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:7, :22 (B3)
    public void The_run_reads_the_dimmed_environment() => headless.OnUiThread(() =>
    {
        var host = new Decorator();
        MarkdownView.SetMarkdownDimmed(host, true);
        var run = new ProseRunView(Blocks(P("one")));
        host.Child = run;
        _ = Show(host);

        var view = Assert.IsType<LinkTextView>(Assert.IsType<ProseRunRepresentable>(run.Child).Child);
        AssertColour(SwiftColor.LabelColor.Opacity(0.7), view.TextStorageForTests!.RunAt(0).ForegroundColor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:47-54, :71 (B3), LinkParagraphView.swift:481-496
    public void The_whole_run_is_one_text_view_holding_the_one_string() => headless.OnUiThread(() =>
    {
        var blocks = Blocks(H(2, "Head"), P("one"), List(false, 1, Item("a")), Quote(P("q")));
        var representable = new ProseRunRepresentable(blocks);

        var view = Assert.IsType<LinkTextView>(representable.Child);
        var presenter = Assert.IsType<ParagraphTextPresenter>(view.Child);
        Assert.Equal("Head\none\n" + Bullet + "\ta\nq", view.TextStorageForTests!.String);
        Assert.Same(view.TextStorageForTests, presenter.Text);
        Assert.True(view.TextStorageForTests.IsEqual(ProseRunRepresentable.Attributed(blocks, 13, false, [])));
    });

    // ---- layout of the one string (PR-THEME-149) ----

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:132-133, :194 (B3)
    public void Quoted_text_starts_fourteen_points_in_on_its_first_and_later_lines() => headless.OnUiThread(() =>
    {
        var quote = "quoted words that go on for long enough to wrap onto a second line";
        var text = Attributed(Blocks(P("a"), Quote(P(quote))));
        var presenter = Laid(text, 160);

        Assert.Equal(0, CharacterRect(presenter, 0).X, 3);
        var start = IndexOf(text, "quoted");
        var first = CharacterRect(presenter, start);
        Assert.Equal(14, first.X, 3);
        var later = Enumerable.Range(start, quote.Length).First(index => CharacterRect(presenter, index).Y > first.Y);
        Assert.Equal(14, CharacterRect(presenter, later).X, 3);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:173, :181-184 (B3)
    public void A_list_marker_sits_at_the_indent_and_its_text_starts_and_wraps_at_the_tab_stop() => headless.OnUiThread(() =>
    {
        var words = "item words that go on for long enough to wrap onto another line";
        var text = Attributed(Blocks(List(false, 1, Item(words, null, List(false, 1, Item("b"))))));
        var presenter = Laid(text, 160);

        Assert.Equal(0, CharacterRect(presenter, 0).X, 3); // the bullet
        var first = CharacterRect(presenter, 2);
        Assert.Equal(18, first.X, 3); // after the tab: the stop at indent + 18
        var later = Enumerable.Range(2, words.Length).First(index => CharacterRect(presenter, index).Y > first.Y);
        Assert.Equal(18, CharacterRect(presenter, later).X, 3); // headIndent = indent + 18

        var hollow = IndexOf(text, HollowBullet);
        Assert.Equal(18, CharacterRect(presenter, hollow).X, 3); // nested: firstLineHeadIndent = 18
        Assert.Equal(36, CharacterRect(presenter, hollow + 2).X, 3); // its stop at 18 + 18
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:134, :158-159, :185 (B3)
    public void Paragraphs_are_separated_by_the_space_after_the_first_plus_the_space_before_the_next() => headless.OnUiThread(() =>
    {
        static double Gap(ParagraphText text, string above, string below)
        {
            var presenter = Laid(text, 300);
            return CharacterRect(presenter, IndexOf(text, below)).Top - CharacterRect(presenter, IndexOf(text, above)).Bottom;
        }

        Assert.Equal(12, Gap(Attributed(Blocks(P("a"), P("b"))), "a", "b"), 3);
        Assert.Equal(18, Gap(Attributed(Blocks(P("a"), H(2, "h"))), "a", "h"), 3); // 12 after + 6 before
        Assert.Equal(14, Gap(Attributed(Blocks(P("a"), H(3, "h"))), "a", "h"), 3); // 12 after + 2 before
        Assert.Equal(6, Gap(Attributed(Blocks(List(false, 1, Item("a"), Item("b")))), "a", "b"), 3);
        Assert.Equal(12, Gap(Attributed(Blocks(List(false, 1, Item("a")), P("b"))), "a", "b"), 3); // last top-level item
    });

    // R-TEST-6 (review finding, ProseRunView unit): both assertions below rest on an open item, not a settled
    // requirement. ParagraphTextPresenter.cs:22-24 already says whether TextKit applies paragraphSpacingBefore
    // above a text container's first paragraph, and counts a last paragraph's paragraphSpacing in usedRect, is
    // UNVERIFIED under OD-0001 (porting/IMPLEMENTING.md's oracle table, O-71); ProseRunView.swift:158-159 gives
    // every paragraph the same literal style with no first/last exception written, so the port carries both edges
    // through unconditionally, exactly like every interior paragraph boundary, but nothing here proves that is
    // what NSLayoutManager itself does at a container's own edges. PR-THEME-149's between-paragraph gaps are the
    // settled requirement and stay `Req`, in Paragraphs_are_separated_by_the_space_after_the_first_plus_the_space_
    // before_the_next above, which starts every measurement mid-run and never touches either edge.
    [Fact]
    [Trait("Oracle", "O-71")] // DroppyCode/UI/Markdown/ProseRunView.swift:158-159 (B3), LinkParagraphView.swift:498-507
    public void The_height_holds_the_heading_spacing_and_is_rounded_up() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(H(1, "h")));
        var presenter = Laid(text, 300);

        var rect = CharacterRect(presenter, 0);
        Assert.Equal(6, rect.Top, 3); // six points before a level-1 heading: O-71, first-paragraph edge
        Assert.Equal(Math.Ceiling(rect.Bottom + 12), presenter.HeightForWidth(300)); // twelve after: O-71, last-paragraph edge
    });

    // ---- one selection and one hit-test space (PR-THEME-149) ----

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:50, :71 (B3), LinkParagraphView.swift:303-306
    public void One_drag_selects_across_the_paragraphs_of_the_run() => headless.OnUiThread(() =>
    {
        var representable = new ProseRunRepresentable(Blocks(P("first words"), P("second words")))
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 300,
        };
        var window = Show(representable);
        var view = Assert.IsType<LinkTextView>(representable.Child);
        var presenter = Assert.IsType<ParagraphTextPresenter>(view.Child);
        var text = view.TextStorageForTests!;

        // One point into each character: the caret goes before it.
        var fromRect = CharacterRect(presenter, IndexOf(text, "words"));
        var toRect = CharacterRect(presenter, IndexOf(text, "second") + 3);
        var from = new Point(fromRect.X + 1, fromRect.Center.Y);
        var to = new Point(toRect.X + 1, toRect.Center.Y);
        window.MouseDown(presenter.TranslatePoint(from, window)!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(presenter.TranslatePoint(to, window)!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(presenter.TranslatePoint(to, window)!.Value, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal("words\nsec", presenter.SelectedText);
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // DroppyCode/UI/Markdown/ProseRunView.swift:130 (B3), LinkParagraphView.swift:355-362, :415-428
    public void A_link_in_a_later_paragraph_is_found_under_the_pointer_and_prose_is_not() => headless.OnUiThread(() =>
    {
        var representable = new ProseRunRepresentable(Blocks(P("plain words"), P("see [link](https://prose-run.invalid/x) here")))
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 300,
        };
        _ = Show(representable);
        var view = Assert.IsType<LinkTextView>(representable.Child);
        var presenter = Assert.IsType<ParagraphTextPresenter>(view.Child);
        var text = view.TextStorageForTests!;

        var linkPoint = presenter.TranslatePoint(CharacterRect(presenter, IndexOf(text, "link") + 1).Center, view)!.Value;
        var prosePoint = presenter.TranslatePoint(CharacterRect(presenter, IndexOf(text, "plain") + 1).Center, view)!.Value;
        Assert.True(view.HasLink(linkPoint));
        Assert.Equal(SwiftURL.Create("https://prose-run.invalid/x"), view.Link(linkPoint));
        Assert.False(view.HasLink(prosePoint));
    });

    // ---- review fixes: selection paint, Narrator exposure, the prose right-click menu ----

    private static byte[] FramePixels(ParagraphTextPresenter presenter, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.White, new Rect(0, 0, width, height));
            presenter.Render(context);
        }
        var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, bitmap.Format ?? Avalonia.Platform.PixelFormat.Rgba8888,
            Avalonia.Platform.AlphaFormat.Premul);
        var bytes = new byte[copy.PixelSize.Width * copy.PixelSize.Height * 4];
        using (var buffer = copy.Lock())
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
            Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        }
        copy.Dispose();
        return bytes;
    }

    [Fact]
    [Trait("Req", "PR-THEME-149")] // review finding (HIGH): LinkParagraphView.swift:312-313 sets neither isEditable
    // nor selectedTextAttributes, so AppKit's own default (NSColor.selectedTextBackgroundColor) always paints a
    // drag selection. SelectableTextBlock.SelectionBrushProperty's AddOwner default is null in Avalonia 12.1.2
    // with no Fluent/Simple theme to override it, so with nothing set the presenter's own
    // `SelectionBrush is { } highlight` guard (Render) always failed: Select/SelectAll and Ctrl+C worked, but
    // nothing ever appeared on screen. Probe, matching the finding's own method (a frame hash before and after
    // SelectAll): render the same laid-out text unselected and fully selected and diff every pixel.
    public void A_selection_paints_something_the_unselected_frame_does_not() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("select this text please")));
        var unselected = Laid(text, 300);
        var selected = Laid(text, 300);
        selected.SelectAll();
        var height = (int)Math.Ceiling(selected.HeightForWidth(300));

        Assert.NotNull(selected.SelectionBrush); // the default this fix adds
        Assert.NotEqual(FramePixels(unselected, 300, height), FramePixels(selected, 300, height));
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // review finding (MEDIUM): LinkParagraphView.swift:275-332 is a real NSTextView,
    // exposed to accessibility like any other; ControlAutomationPeer.CreatePeerForElement on a bare
    // ParagraphTextPresenter returned NoneAutomationPeer (Control.OnCreateAutomationPeer's own default), name "",
    // IsControlElement/IsContentElement both false, so Narrator never read a finished message's prose. The port's
    // own paragraph path (SelectableTextBlock) gets Avalonia's TextBlockAutomationPeer for free.
    public void The_automation_peer_exposes_the_runs_text_as_a_text_element() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("hello"), P("second paragraph")));
        var presenter = new ParagraphTextPresenter();
        presenter.SetText(text);

        var peer = ControlAutomationPeer.CreatePeerForElement(presenter);
        Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
        Assert.True(peer.IsControlElement());
        Assert.True(peer.IsContentElement());
        Assert.Equal(text.String, peer.GetName());
    });

    [Fact]
    [Trait("Req", "PR-THEME-149")] // review finding (MEDIUM): LinkParagraphView.swift:453-461 `rightMouseDown`/
    // `menu(for:)` both fall through to AppKit's own text menu (Copy and the rest) when the click misses a link
    // (:432-435 "Prose keeps the system's menu"). The presenter had no ContextMenu or ContextFlyout at all (probe:
    // both null), so a right-click on prose in a finished message did nothing. This is its Windows counterpart:
    // Copy and Select All over the presenter's own selection, opened the way every other Avalonia ContextMenu
    // opens (Control.OnPointerReleased's own unhandled-right-button check; LinkParagraphView.cs's release-time
    // veto keeps a link's right-click at LinkPopover only, never both, the same either-or :453-456 itself keeps).
    // UNTESTABLE for now, and why: actually opening the menu goes through a real Avalonia Popup, and this suite's
    // own headless session opens no Fluent/Simple theme, so any real Popup.Open throws ("Unable to create
    // IPopupImpl and no overlay layer is found for the target control") — confirmed empirically already, by
    // LinkPopoverTests.cs's identical constraint on LinkPopover's own Popup (same file header). So this asserts
    // the menu's wiring and its two rows' actions directly, the way PopoversTests.A_context_menu_row_runs_its_
    // action_synchronously_inside_the_click_callback already tests RowActionMenuButtons without opening a Popup
    // either; which points count as "a link" for the release-time veto is PR-THEME-149's own
    // A_link_in_a_later_paragraph_is_found_under_the_pointer_and_prose_is_not, just above.
    public void The_presenters_context_menu_has_copy_and_select_all_over_its_own_selection() => headless.OnUiThread(() =>
    {
        var text = Attributed(Blocks(P("copy this text")));
        var presenter = new ParagraphTextPresenter();
        presenter.SetText(text);

        var menu = presenter.ContextMenu;
        Assert.NotNull(menu);
        var items = menu.Items.OfType<MenuItem>().ToList();
        var copy = Assert.Single(items, item => (item.Header as string) == "Copy");
        var selectAll = Assert.Single(items, item => (item.Header as string) == "Select All");

        presenter.Select(2, 6); // "py t"
        var expected = presenter.SelectedText;
        copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        // No headless clipboard readback exists in this suite (LinkPopoverTests.cs notes the same gap for Copy
        // Link); Copy()'s own guard not throwing on a non-empty selection is what there is to observe here.
        Assert.Equal(expected, presenter.SelectedText); // Click did not itself change the selection

        selectAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(0, presenter.SelectionStart);
        Assert.Equal(text.Length, presenter.SelectionEnd);
    });

    // ---- makeNSView / updateNSView / dismantleNSView (PR-THEME-150) ----

    [Fact]
    [Trait("Req", "PR-THEME-150")] // DroppyCode/UI/Markdown/ProseRunView.swift:43-54 (B3)
    public void Making_the_view_hands_it_to_the_host_with_the_coordinator_as_delegate_and_prose_taking_the_mouse() =>
        headless.OnUiThread(() =>
        {
            LinkTextView? hosted = null;
            var representable = new ProseRunRepresentable(Blocks(P("one")), onHost: view => hosted = view);

            var view = Assert.IsType<LinkTextView>(representable.Child);
            Assert.Same(view, hosted);
            Assert.Same(representable.Coordinator, view.Delegate);
            Assert.False(view.PassesProseThrough);
        });

    [Fact]
    [Trait("Req", "PR-THEME-150")] // DroppyCode/UI/Markdown/ProseRunView.swift:56-72 (B3)
    public void The_string_is_rebuilt_only_when_the_source_size_dimming_revision_mention_names_or_head_urls_change() =>
        headless.OnUiThread(() =>
        {
            var persona = HydraRoster.Personas[0];
            var other = HydraRoster.Personas[1];
            var thread = Guid.NewGuid();
            var target = new HydraMentionTarget { ThreadID = thread, Task = "t", Status = HydraHeadInfo.Status.Running };
            var sameUrl = new HydraMentionTarget { ThreadID = thread, Task = "changed", Status = HydraHeadInfo.Status.Completed };
            var newUrl = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
            var blocks = Blocks(P("plain words"));
            var representable = new ProseRunRepresentable(blocks);
            var view = Assert.IsType<LinkTextView>(representable.Child);
            Assert.Equal(1, view.RenderCountForTests);

            representable.Update(Blocks(P("plain words")));
            Assert.Equal(1, view.RenderCountForTests); // equal source, nothing drawn

            representable.Update(Blocks(P("other words")));
            Assert.Equal(2, view.RenderCountForTests);
            representable.Update(Blocks(P("other words")), pointSize: 14);
            Assert.Equal(3, view.RenderCountForTests);
            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true);
            Assert.Equal(4, view.RenderCountForTests);
            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1);
            Assert.Equal(5, view.RenderCountForTests);
            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1, mentions: [persona]);
            Assert.Equal(6, view.RenderCountForTests);
            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1, mentions: [persona],
                targets: new Dictionary<string, HydraMentionTarget> { [persona.Name] = target });
            Assert.Equal(7, view.RenderCountForTests);

            // The same head URL with a changed task and status: the check compares URLs only (:61, :64).
            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1, mentions: [persona],
                targets: new Dictionary<string, HydraMentionTarget> { [persona.Name] = sameUrl });
            Assert.Equal(7, view.RenderCountForTests);

            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1, mentions: [persona],
                targets: new Dictionary<string, HydraMentionTarget> { [persona.Name] = newUrl });
            Assert.Equal(8, view.RenderCountForTests);

            representable.Update(Blocks(P("other words")), pointSize: 14, dimmed: true, revision: 1, mentions: [other],
                targets: new Dictionary<string, HydraMentionTarget> { [persona.Name] = newUrl });
            Assert.Equal(9, view.RenderCountForTests); // mention names changed
        });

    [Fact]
    [Trait("Req", "PR-THEME-150")] // DroppyCode/UI/Markdown/ProseRunView.swift:73-76 (B3)
    public void The_head_handler_and_the_merge_target_land_on_every_update_even_one_that_draws_nothing() =>
        headless.OnUiThread(() =>
        {
            var blocks = Blocks(P("plain words"));
            var representable = new ProseRunRepresentable(blocks);
            var view = Assert.IsType<LinkTextView>(representable.Child);
            Action<SwiftURL> handler = _ => { };

            representable.Update(blocks, onOpenHead: handler);
            Assert.Equal(1, view.RenderCountForTests);
            Assert.Same(handler, view.OnOpenHead);

            var target = new MergeRequestTarget(Guid.NewGuid(), _ => { });
            LinkParagraphView.SetMergeRequestTarget(representable, target);
            Assert.Equal(1, view.RenderCountForTests);
            Assert.Same(target, view.MergeTarget);

            representable.Update(blocks);
            Assert.Null(view.OnOpenHead); // a new (nil) handler lands too
            Assert.Same(target, view.MergeTarget);
        });

    [Fact]
    [Trait("Req", "PR-THEME-150")] // DroppyCode/UI/Markdown/ProseRunView.swift:84-88 (B3); upstream defect 55
    public void Dismantling_clears_the_delegate_the_head_handler_and_the_merge_target() => headless.OnUiThread(() =>
    {
        var representable = new ProseRunRepresentable(Blocks(P("one")), onOpenHead: _ => { });
        var view = Assert.IsType<LinkTextView>(representable.Child);
        view.MergeTarget = new MergeRequestTarget(Guid.NewGuid(), _ => { });

        ProseRunRepresentable.DismantleNSView(view, representable.Coordinator);

        Assert.Null(view.Delegate);
        Assert.Null(view.OnOpenHead);
        Assert.Null(view.MergeTarget);
    });
}
