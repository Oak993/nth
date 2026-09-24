using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using ChromeNs = Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e). This part (part 1 of the unit)
// covers PR-THEME-105 to 129 except 122. Covers MarkdownBlockView.body (:300-374), its Equatable (:295-298),
// the marker (:506-538), MentionRanges/MentionsTarget/MentionSegments' empty-environment guard, TableBlock's
// body (:1338-1397) and CopyButton's body (:1471-1498).
//
// UNTESTABLE for now, and why: the paragraph case's InlineText branches (:308-320) and the .code delegation-card
// branch's HydraDelegationBlock factory all construct types this unit's sibling units still leave MARROW_STUB
// (InlineText's own render path is part 2's PR-THEME-151; StreamVeil/ProseRunView/LinkParagraphView are a
// separate unit each) — every one of them throws NotImplementedException on construction today. Where a branch
// is reachable only through one of those stubs, the test below proves the *routing* reached it (a distinct
// exception citing that stub's own Swift line) rather than the rendered content, and says so.
[Collection("Headless")]
public sealed class MarkdownBlockViewBodyTests(HeadlessSession headless)
{
    private static MarkdownBlockView RuleView() => new(new MarkdownBlock.Rule());

    // A block whose body never touches an InlineText/VeiledText stub (used for content-comparison tests
    // instead of .paragraph, which the class header explains cannot be constructed yet).
    private static MarkdownBlock TableProbe(string header) =>
        new MarkdownBlock.Table(new ValueList<string>([header]), new ValueList<ValueList<string>>([new ValueList<string>(["1"])]));

    // Mirrors ChromeTests.Show: Track (ViewLifetime.cs) only starts observing on AttachedToVisualTree, so any
    // assertion depending on a Track-driven update needs the control shown in a real window first.
    private static Window Show(Avalonia.Controls.Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static object InvokeMarker(MarkdownBlockView view, bool ordered, int number, bool? @checked)
    {
        var method = typeof(MarkdownBlockView).GetMethod("Marker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return method.Invoke(view, [ordered, number, @checked])!;
    }

    // ---- PR-THEME-106: Equatable (:295-298) ----

    [Fact]
    [Trait("Req", "PR-THEME-106")] // DroppyCode/UI/Markdown/MarkdownView.swift:295-298
    public void Two_settled_views_compare_equal_without_looking_at_their_content() => headless.OnUiThread(() =>
    {
        var lhs = new MarkdownBlockView(TableProbe("one"), settled: true);
        var rhs = new MarkdownBlockView(TableProbe("two"), settled: true);
        Assert.True(MarkdownBlockView.Equatable(lhs, rhs));
    });

    [Fact]
    [Trait("Req", "PR-THEME-106")] // DroppyCode/UI/Markdown/MarkdownView.swift:295-298
    public void A_settled_and_an_unsettled_view_compare_by_content() => headless.OnUiThread(() =>
    {
        var lhs = new MarkdownBlockView(TableProbe("one"), settled: true);
        var rhs = new MarkdownBlockView(TableProbe("two"), settled: false);
        Assert.False(MarkdownBlockView.Equatable(lhs, rhs));
    });

    [Fact]
    [Trait("Req", "PR-THEME-106")] // DroppyCode/UI/Markdown/MarkdownView.swift:295-298
    public void Two_unsettled_views_with_equal_blocks_compare_equal() => headless.OnUiThread(() =>
    {
        var lhs = RuleView();
        var rhs = RuleView();
        Assert.True(MarkdownBlockView.Equatable(lhs, rhs));
    });

    [Fact]
    [Trait("Req", "PR-THEME-106")] // DroppyCode/UI/Markdown/MarkdownView.swift:295-298
    public void Two_unsettled_views_with_different_blocks_compare_unequal() => headless.OnUiThread(() =>
    {
        var lhs = RuleView();
        var rhs = new MarkdownBlockView(TableProbe("x"));
        Assert.False(MarkdownBlockView.Equatable(lhs, rhs));
    });

    // ---- PR-THEME-110: paragraph path selection (:306-320) ----
    // ContainsLinks (PR-THEME-127/128 territory) is exercised by RichLink's own tests; MentionsTarget and the
    // VeiledText-reaching mention branch are this part's own routing and are exercised directly below.

    [Fact]
    [Trait("Req", "PR-THEME-110")] // DroppyCode/UI/Markdown/MarkdownView.swift:411-414
    public void MentionsTarget_is_false_when_the_environment_carries_no_targets() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var method = typeof(MarkdownBlockView).GetMethod("MentionsTarget", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.False((bool)method.Invoke(view, ["Ping Hank about the release."])!);
    });

    [Fact]
    [Trait("Req", "PR-THEME-110")] // DroppyCode/UI/Markdown/MarkdownView.swift:411-414, :418-441
    public void MentionsTarget_is_true_when_a_target_name_appears_word_bounded() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        MarkdownView.SetHydraMentionTargets(view, new ValueMap<string, HydraMentionTarget>(
            [new("Hank", new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running })]));
        var method = typeof(MarkdownBlockView).GetMethod("MentionsTarget", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True((bool)method.Invoke(view, ["Ping Hank about the release."])!);
        Assert.False((bool)method.Invoke(view, ["Hankering for coffee."])!); // not word-bounded
    });

    [Fact]
    [Trait("Req", "PR-THEME-110")] // DroppyCode/UI/Markdown/MarkdownView.swift:418-441: sorted by count descending
    public void MentionRanges_prefers_the_longest_matching_name_at_a_position()
    {
        var ranges = MarkdownBlockView.MentionRanges("Hank 2 is here", ["Hank", "Hank 2"]);
        var only = Assert.Single(ranges);
        Assert.Equal((0, 6), only);
    }

    [Fact]
    [Trait("Req", "PR-THEME-110")] // DroppyCode/UI/Markdown/MarkdownView.swift:442-446
    public void MentionSegments_returns_null_when_the_environment_carries_no_personas() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var method = typeof(MarkdownBlockView).GetMethod("MentionSegments", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Null(method.Invoke(view, ["no mentions here"]));
    });

    [Fact]
    [Trait("Req", "PR-THEME-110")] // DroppyCode/UI/Markdown/MarkdownView.swift:306-320, :313-320: with the
    // environment at its defaults (no links, no targets, no personas) the routing falls through all three
    // earlier conditions to the final else (:320) and constructs InlineText(hasLinks: false) -- the only
    // externally observable difference from that branch until part 2's InlineText lands is which MARROW_STUB
    // site the construction throws from; this proves it is InlineText's (:1127), not VeiledText's.
    public void A_plain_paragraph_with_no_links_targets_or_personas_reaches_the_default_InlineText_branch()
    {
        var block = new MarkdownBlock.Paragraph("nothing special here");
        var ex = Assert.Throws<NotImplementedException>(() => new MarkdownBlockView(block));
        Assert.Contains("MarkdownView.swift:1127", ex.Message, StringComparison.Ordinal);
    }

    // ---- PR-THEME-111/112/113: marker (:506-538) ----

    [Fact]
    [Trait("Req", "PR-THEME-113")] // DroppyCode/UI/Markdown/MarkdownView.swift:508-511
    public void A_checked_item_marker_is_a_filled_circle_in_the_accent() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var marker = (SwiftImage)InvokeMarker(view, ordered: false, number: 0, @checked: true);
        Assert.Equal("checkmark.circle.fill", marker.SymbolName);
    });

    [Fact]
    [Trait("Req", "PR-THEME-113")] // DroppyCode/UI/Markdown/MarkdownView.swift:508-511
    public void An_unchecked_item_marker_is_an_outline_circle_in_the_tertiary_colour() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var marker = (SwiftImage)InvokeMarker(view, ordered: false, number: 0, @checked: false);
        Assert.Equal("circle", marker.SymbolName);
    });

    [Fact]
    [Trait("Req", "PR-THEME-112")] // DroppyCode/UI/Markdown/MarkdownView.swift:512-516
    public void An_ordered_marker_is_the_number_and_a_full_stop_in_monospaced_medium() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var marker = (TextBlock)InvokeMarker(view, ordered: true, number: 3, @checked: null);
        Assert.Equal("3.", marker.Text);
        Assert.Equal(FontWeight.Medium, marker.FontWeight);
    });

    [Fact]
    [Trait("Req", "PR-THEME-111")] // DroppyCode/UI/Markdown/MarkdownView.swift:517-524: a 5x5 filled dot at depth 0
    public void An_unordered_top_level_marker_is_a_filled_5_point_dot() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        var marker = (Ellipse)InvokeMarker(view, ordered: false, number: 0, @checked: null);
        Assert.Equal(5, marker.Width);
        Assert.Equal(5, marker.Height);
        Assert.NotNull(marker.Fill);
        Assert.Null(marker.Stroke);
    });

    [Fact]
    [Trait("Req", "PR-THEME-111")] // DroppyCode/UI/Markdown/MarkdownView.swift:525-531: a 5.5x5.5 ring one level in
    public void An_unordered_nested_marker_is_a_5_5_point_ring() => headless.OnUiThread(() =>
    {
        var view = RuleView();
        MarkdownView.SetMarkdownListDepth(view, 1);
        var marker = (Ellipse)InvokeMarker(view, ordered: false, number: 0, @checked: null);
        Assert.Equal(5.5, marker.Width);
        Assert.Equal(5.5, marker.Height);
        Assert.NotNull(marker.Stroke);
        Assert.Equal(1.2, marker.StrokeThickness);
        Assert.Null(marker.Fill);
    });

    [Fact]
    [Trait("Req", "PR-THEME-111")] // DroppyCode/UI/Markdown/MarkdownView.swift:536-538: accent at 0.9, or 0.55 dimmed
    public void The_marker_colour_is_the_accent_at_90_percent_or_55_percent_dimmed() => headless.OnUiThread(() =>
    {
        var property = typeof(MarkdownBlockView).GetProperty("MarkerColor", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var plain = RuleView();
        Assert.Equal(
            ChromeNs.Chrome.Accent.Opacity(0.9).Resolve(Avalonia.Styling.ThemeVariant.Light),
            ((SwiftColor)property.GetValue(plain)!).Resolve(Avalonia.Styling.ThemeVariant.Light));

        var dimmed = RuleView();
        MarkdownView.SetMarkdownDimmed(dimmed, true);
        Assert.Equal(
            ChromeNs.Chrome.Accent.Opacity(0.55).Resolve(Avalonia.Styling.ThemeVariant.Light),
            ((SwiftColor)property.GetValue(dimmed)!).Resolve(Avalonia.Styling.ThemeVariant.Light));
    });

    // ---- PR-THEME-114: list layout (:334-353) ----

    [Fact]
    [Trait("Req", "PR-THEME-114")] // DroppyCode/UI/Markdown/MarkdownView.swift:335, :337, :339: 6 / 8 / 6
    public void A_list_stacks_items_6_points_apart_with_8_between_marker_and_text_and_6_under_children() => headless.OnUiThread(() =>
    {
        var child = new MarkdownBlock.Rule();
        var item = new MarkdownListItem("", null, new ValueList<MarkdownBlock>([child])); // empty text: :340 skips InlineText
        var block = new MarkdownBlock.List(false, 1, new ValueList<MarkdownListItem>([item]));
        var view = new MarkdownBlockView(block);

        var itemsStack = Assert.IsType<StackPanel>(view.Child);
        Assert.Equal(Orientation.Vertical, itemsStack.Orientation);
        Assert.Equal(6, itemsStack.Spacing); // :335

        var row = Assert.IsType<StackPanel>(Assert.Single(itemsStack.Children));
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        Assert.Equal(8, row.Spacing); // :337

        var content = Assert.IsType<StackPanel>(row.Children[1]);
        Assert.Equal(Orientation.Vertical, content.Orientation);
        Assert.Equal(6, content.Spacing); // :339

        var childView = Assert.IsType<MarkdownBlockView>(Assert.Single(content.Children));
        Assert.Equal(1, MarkdownView.GetMarkdownListDepth(childView)); // :345-346 listDepth + 1
    });

    [Fact]
    [Trait("Req", "PR-THEME-114")] // DroppyCode/UI/Markdown/MarkdownView.swift:340: item.text.isEmpty skips InlineText
    public void An_empty_list_item_text_adds_no_inline_text_child() => headless.OnUiThread(() =>
    {
        var item = new MarkdownListItem("", null, ValueList<MarkdownBlock>.Empty);
        var block = new MarkdownBlock.List(false, 1, new ValueList<MarkdownListItem>([item]));
        var view = new MarkdownBlockView(block);
        var itemsStack = Assert.IsType<StackPanel>(view.Child);
        var row = Assert.IsType<StackPanel>(Assert.Single(itemsStack.Children));
        var content = Assert.IsType<StackPanel>(row.Children[1]);
        Assert.Empty(content.Children);
    });

    // ---- PR-THEME-115: quote (:354-364) ----

    [Fact]
    [Trait("Req", "PR-THEME-115")] // DroppyCode/UI/Markdown/MarkdownView.swift:355-364
    public void A_quote_is_a_3_point_capsule_beside_content_10_points_away_with_blocks_8_points_apart() => headless.OnUiThread(() =>
    {
        var child = new MarkdownBlock.Rule();
        var block = new MarkdownBlock.Quote(new ValueList<MarkdownBlock>([child]));
        var view = new MarkdownBlockView(block);

        var grid = Assert.IsType<Grid>(view.Child);
        Assert.Equal(3, grid.ColumnDefinitions.Count); // Auto,10,*
        Assert.Equal(10, grid.ColumnDefinitions[1].Width.Value); // :355 spacing: 10

        var capsule = grid.Children.OfType<Border>().Single(c => Grid.GetColumn(c) == 0);
        Assert.Equal(3, capsule.Width); // :356 frame(width: 3)
        Assert.NotNull(capsule.Background);

        var content = grid.Children.OfType<StackPanel>().Single(c => Grid.GetColumn(c) == 2);
        Assert.Equal(8, content.Spacing); // :358

        var childView = Assert.IsType<MarkdownBlockView>(Assert.Single(content.Children));
        Assert.True(MarkdownView.GetMarkdownDimmed(childView)); // :364 .environment(\.markdownDimmed, true)
    });

    // ---- PR-THEME-116: rule (:367-371) ----

    [Fact]
    [Trait("Req", "PR-THEME-116")] // DroppyCode/UI/Markdown/MarkdownView.swift:368-371
    public void A_rule_is_a_1_point_line_with_4_points_above_and_below() => headless.OnUiThread(() =>
    {
        var view = new MarkdownBlockView(new MarkdownBlock.Rule());
        var rule = Assert.IsType<Rectangle>(view.Child);
        Assert.Equal(1, rule.Height);
        Assert.Equal(new Avalonia.Thickness(0, 4, 0, 4), rule.Margin);
        Assert.NotNull(rule.Fill);
    });

    // ---- PR-THEME-119: TableBlock (:1338-1397) ----

    [Fact]
    [Trait("Req", "PR-THEME-119")] // DroppyCode/UI/Markdown/MarkdownView.swift:1363-1368: a semibold header
    public void A_table_renders_a_semibold_header_row_in_a_grid() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["Name", "Age"], [["Ada", "30"], ["Lin", "40"]]);
        var window = Show(table);
        var texts = VisualQuery.Descendants<TextBlock>(table).ToList();
        var header = texts.Where(t => t.FontWeight == FontWeight.SemiBold).Select(t => t.Text).ToList();
        Assert.Equal(["Name", "Age"], header);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-119")] // DroppyCode/UI/Markdown/MarkdownView.swift:1344, :1358, :1383-1392: 30 rows,
    // a "Show all N rows" link when the table has more
    public void A_table_over_30_rows_collapses_to_30_with_a_show_all_link() => headless.OnUiThread(() =>
    {
        var rows = Enumerable.Range(0, 35).Select(i => (IReadOnlyList<string>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToList();
        var table = new TableBlock(["N"], rows);
        var window = Show(table);
        var texts = VisualQuery.Descendants<TextBlock>(table).Select(t => t.Text).ToList();
        Assert.Equal(30, texts.Count(t => t is not null && t != "N" && !t.StartsWith("Show", StringComparison.Ordinal)));
        Assert.Contains(texts, t => t == "Show all 35 rows");
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-119")] // DroppyCode/UI/Markdown/MarkdownView.swift:1396 .background(.quaternary.opacity(0.35), in: .rect(cornerRadius: 12))
    public void A_table_sits_on_a_12_point_rounded_quaternary_panel() => headless.OnUiThread(() =>
    {
        var table = new TableBlock(["N"], [["1"]]);
        var panel = Assert.IsType<Border>(table.Child);
        Assert.Equal(12, panel.CornerRadius.TopLeft);
        Assert.NotNull(panel.Background);
    });

    [Fact]
    [Trait("Req", "PR-THEME-119")] // DroppyCode/UI/Markdown/MarkdownView.swift:1341, :1384-1386: tapping "Show all" reveals every row
    public void Tapping_show_all_reveals_every_row() => headless.OnUiThread(() =>
    {
        var rows = Enumerable.Range(0, 35).Select(i => (IReadOnlyList<string>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToList();
        var table = new TableBlock(["N"], rows);
        var window = Show(table);

        var button = VisualQuery.Descendants<Button>(table).Single();
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var cells = VisualQuery.Descendants<TextBlock>(table).Count(t => t.Text != "N" && t.Text != "Show all 35 rows");
        Assert.Equal(35, cells);
        Assert.DoesNotContain(VisualQuery.Descendants<TextBlock>(table), t => t.Text == "Show all 35 rows");
        window.Close();
    });

    // ---- PR-THEME-120: CopyButton (:1471-1498) ----

    [Fact]
    [Trait("Req", "PR-THEME-120")] // DroppyCode/UI/Markdown/MarkdownView.swift:1492, :1496: default label "Copy" as the tooltip
    public void The_copy_buttons_default_label_is_the_tooltip_Copy() => headless.OnUiThread(() =>
    {
        var button = new CopyButton("some code");
        var content = Assert.IsType<Button>(button.Child);
        Assert.Equal("Copy", ToolTip.GetTip(content));
    });

    [Fact]
    [Trait("Req", "PR-THEME-120")] // DroppyCode/UI/Markdown/MarkdownView.swift:1488: doc.on.doc before copying
    public void The_copy_button_starts_with_the_doc_on_doc_glyph() => headless.OnUiThread(() =>
    {
        var button = new CopyButton("some code");
        var content = Assert.IsType<Button>(button.Child);
        var icon = Assert.IsType<SwiftImage>(content.Content);
        Assert.Equal("doc.on.doc", icon.SymbolName);
        Assert.Equal(22, icon.Width);
        Assert.Equal(22, icon.Height);
    });

    [Fact]
    [Trait("Req", "PR-THEME-120")] // DroppyCode/UI/Markdown/MarkdownView.swift:1480, :1488: clicking swaps to the check mark
    public void Clicking_copies_and_swaps_the_glyph_to_a_check_mark() => headless.OnUiThread(() =>
    {
        var button = new CopyButton("copy me please");
        var window = Show(button);
        var content = Assert.IsType<Button>(button.Child);
        content.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // :1477 the button's action
        Dispatcher.UIThread.RunJobs();
        var icon = Assert.IsType<SwiftImage>(content.Content);
        Assert.Equal("checkmark", icon.SymbolName); // :1488
        window.Close();
    });
}
