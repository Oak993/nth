using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2340-2383 TodoListRow and :2385-2431 NoticeRow.
[Collection("Headless")]
public sealed class TimelineRowsFinNoticeTests(HeadlessSession headless)
{
    private static double FirstBaseline(TextBlock text, Visual root) => Top(text, root) + text.TextLayout.TextLines[0].Baseline;

    private static double GlyphBaseline(SwiftImage glyph, Visual root) => Top(glyph, root) + TextBlock.GetBaselineOffset(glyph);

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-033")] // TimelineRows.swift:2343-2382 the checklist card
    public void The_checklist_is_a_quaternary_card_with_a_counted_header_and_first_baseline_step_rows_in_their_status_colours() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var longStep = string.Join(" ", Enumerable.Repeat("a step that wraps", 12));
            var entry = new TimelineEntry(new TimelineItem(null, Steps(
                ("Read the code", TodoStep.Status.Done), ("Write the fix", TodoStep.Status.Active), ("Run tests", TodoStep.Status.Pending), (longStep, TodoStep.Status.Pending))));
            var row = new TodoListRow(entry);
            var empty = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps())));
            var panel = new StackPanel { Width = 500, Spacing = 30 };
            panel.Children.Add(row);
            panel.Children.Add(empty);
            var window = Show(panel);
            try
            {
                // :2344 no steps: nothing is rendered at all.
                Assert.Empty(Mounted<TextBlock>(empty));
                Assert.Equal(0, empty.Bounds.Height);
                // :2363-2364 padding 14 on a .quaternary card at 0.32, radius 18; :2345 inner spacing 7.
                var card = Mounted<Border>(row).First(border => border.Padding == new Thickness(14));
                Assert.Equal(new CornerRadius(18), card.CornerRadius);
                Paints(card.Background, SwiftColor.Quaternary.Opacity(0.32), card, "card fill");
                var column = Assert.IsType<StackPanel>(card.Child);
                Assert.Equal(7, column.Spacing);
                // :2368-2369 the row takes the width less 96 points of trailing room; the card is lead-aligned.
                Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                Assert.Equal(Left(row, window), Left(card, window), 0.5);
                Assert.True(card.Bounds.Width <= 500 - 96 + 0.5, "the card runs into the trailing room");
                // :2346-2351 the header: the checklist glyph and "N of M done", 6 apart, caption type in Chrome.secondaryText.
                var count = TextNamed(row, "1 of 4 done");
                Face(count, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(count.Foreground, ChromeTokens.SecondaryText, count, "header");
                var checklist = Mounted<SwiftImage>(row).First(image => image.SymbolName == "checklist");
                Assert.Equal(10, SwiftImage.GetFont(checklist)!.Resolve().FontSize);
                Paints(checklist.Foreground, ChromeTokens.SecondaryText, checklist, "header glyph");
                Assert.Equal(6, Left(count, window) - (Left(checklist, window) + checklist.Bounds.Width), 0.5);
                OnOneCentreLine(window, checklist, count);
                // :2353-2360 each step: a first-baseline row 8 points apart, callout type.
                var expected = new[]
                {
                    ("Read the code", "checkmark.circle.fill", TodoStep.Status.Done),
                    ("Write the fix", "circle.dotted.circle", TodoStep.Status.Active),
                    ("Run tests", "circle", TodoStep.Status.Pending),
                    (longStep, "circle", TodoStep.Status.Pending),
                };
                foreach (var (words, symbol, status) in expected)
                {
                    var text = TextNamed(row, words);
                    // The step's own glyph: the one of its symbol beside the text's first line (the callout's 17-point box puts the baseline, and so the glyph,
                    // half its extra leading, 1.2 pt, lower in the box than Inter's own box did; ED-290 (1)).
                    var glyph = Mounted<SwiftImage>(row).First(image => image.SymbolName == symbol && Top(image, window) <= Top(text, window) + 3 && Top(image, window) >= Top(text, window) - 3);
                    Face(text, 12, FontWeight.Normal, ".chat(.callout)");
                    Assert.Equal(12, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
                    Assert.Equal(8, Left(text, window) - (Left(glyph, window) + glyph.Bounds.Width), 0.5);
                    Assert.Equal(FirstBaseline(text, window), GlyphBaseline(glyph, window), 0.5);
                    // :2355 a pending glyph is .tertiary; active and done are drawn in the tint.
                    Paints(glyph.Foreground, status == TodoStep.Status.Pending ? SwiftColor.Tertiary : ChromeTokens.Accent, glyph, symbol);
                    // :2357-2358 done text is .secondary with a secondary strikethrough; every other step is .primary.
                    Paints(text.Foreground, status == TodoStep.Status.Done ? SwiftColor.Secondary : SwiftColor.Primary, text, words);
                    if (status == TodoStep.Status.Done)
                    {
                        var strike = Assert.Single(text.TextDecorations!);
                        Assert.Equal(TextDecorationLocation.Strikethrough, strike.Location);
                        Paints(strike.Stroke, SwiftColor.Secondary, text, "strikethrough");
                    }
                    else Assert.True(text.TextDecorations is null || text.TextDecorations.Count == 0);
                }
                // The long step wraps inside the card's width rather than widening it.
                Assert.True(TextNamed(row, longStep).TextLayout.TextLines.Count > 1);
                // The card hugs its steps: with short steps only, it is far narrower than the row.
                var shortRow = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps(("one", TodoStep.Status.Pending)))));
                panel.Children.Add(shortRow);
                Settle(window);
                Assert.True(Mounted<Border>(shortRow).First(border => border.Padding == new Thickness(14)).Bounds.Width < 200);
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData(Notice.Level.Info)]
    [InlineData(Notice.Level.Warning)]
    [InlineData(Notice.Level.Error)]
    [Trait("Req", "UI-CHAT-034")] // TimelineRows.swift:2385-2431 the notice row
    [Trait("Req", "UI-RUNTIME-001")] // TimelineRows.swift:2385-2404, :2406-2430 the info-level notice
    [Trait("Req", "UI-RUNTIME-002")] // the Warning case: TimelineRows.swift:2385-2431, padding 12/14/8, radius 18, full width, callout 12, Chrome.warning glyph and 0.1 fill, .primary text, selectable, first-baseline row
    [Trait("Req", "UI-RUNTIME-003")] // the Error case: the same geometry with Chrome.danger and exclamationmark.octagon
    // Every clause of UI-RUNTIME-002 (the Warning case) and UI-RUNTIME-003 (the Error case) is proven here from the Swift source (OD-0098). The corner of a notice two lines
    // tall, the one clause that was open (2-chat-use.mov shows one, C 10.6-12.0, the "API Error: 402 ..." banner, about 48 pt, but its end has no extractable edge: the red
    // tint at 0.1 over a blurred wallpaper, compression blocks of 4 luma, SPEC.md 2.3), is Apple's corner at the largest radius that fits under OD-0102 (owner, 2026-10-04,
    // "corner-band: A"): ContinuousCornerReproofTests.A_warning_or_error_notice_two_lines_tall_is_cut_to_Apples_corner_at_the_largest_radius_that_fits reads it off the
    // mounted two-line warning and error notices, and REVIEW_the_four_band_elements_render_... compares their rendered corners with the path fill at all four corners.
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:2394 a notice's message enables text selection
    public void A_notice_is_a_first_baseline_row_whose_pill_glyph_and_colours_follow_its_level(Notice.Level level) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var message = string.Join(" ", Enumerable.Repeat("The source notice stays within its row.", 8));
            var row = new NoticeRow(NoticeEntry(level, message));
            var host = new Border { Width = 360, Child = row };
            var panel = new StackPanel();
            panel.Children.Add(host);
            var window = Show(panel);
            try
            {
                var pill = Mounted<Border>(row).First();
                var glyph = Assert.Single(Mounted<SwiftImage>(row));
                var text = Assert.Single(Mounted<SelectableTextBlock>(row)); // :2394 .textSelection(.enabled)
                Assert.Equal(message, text.Text);
                var isInfo = level == Notice.Level.Info;
                // :2397 .font(.chat(.callout)) on the whole row: the message and the glyph are 12 points.
                Face(text, 12, FontWeight.Normal, ".chat(.callout)");
                Assert.Equal(12, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
                // :2408-2412 info.circle, exclamationmark.triangle, exclamationmark.octagon.
                Assert.Equal(level switch { Notice.Level.Info => "info.circle", Notice.Level.Warning => "exclamationmark.triangle", _ => "exclamationmark.octagon" }, glyph.SymbolName);
                // :2416-2430 the theme's own status colours; info is .secondary with no fill.
                var status = level switch { Notice.Level.Info => SwiftColor.Secondary, Notice.Level.Warning => ChromeTokens.Warning, _ => ChromeTokens.Danger };
                Paints(glyph.Foreground, status, glyph, "glyph");
                Paints(text.Foreground, isInfo ? SwiftColor.Secondary : SwiftColor.Primary, text, "message");
                if (isInfo) Assert.True(pill.Background is null || Resolve(SwiftColor.Clear, pill) == ((ISolidColorBrush)pill.Background).Color, "an info notice has a clear background");
                else Paints(pill.Background, status.Opacity(0.1), pill, "pill fill");
                // :2398-2402 info: no padding at all; warning and error: 12 leading, 14 trailing, 8 vertical, radius 18.
                Assert.Equal(isInfo ? default : new Thickness(12, 8, 14, 8), pill.Padding);
                if (!isInfo) Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                // :2402 `.background(..., in: .rect(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))` (ED-181 closed): a pill as tall as
                // this wrapped message (2 * 1.528665 * 18 = 55 pt fits) is cut to Apple's continuous corner; a one-line notice (33 pt) is the circular
                // arc (ED-275 (2): the reference video fits circular ends for the capsules and for a radius from half the height up). A notice two lines tall
                // (36 to 55 pt) is in the band of ED-275 (2), Apple's corner at the largest radius that fits under OD-0102 (ContinuousCornerReproofTests).
                if (!isInfo)
                {
                    Assert.True(pill.Bounds.Height >= 55, $"the wrapped notice is only {pill.Bounds.Height} pt tall");
                    ContinuousCornerChecks.Drawn(pill, expectApple: true);
                    var oneLine = new NoticeRow(NoticeEntry(level, "Short."));
                    var oneLineHost = new Border { Width = 360, Child = oneLine };
                    panel.Children.Add(oneLineHost);
                    Settle(window);
                    var flat = Mounted<Border>(oneLine).First();
                    Assert.True(flat.Bounds.Height < 55, $"the one-line notice is {flat.Bounds.Height} pt tall");
                    ContinuousCornerChecks.Drawn(flat, expectApple: false);
                }
                // :2401 full width, lead-aligned.
                Assert.Equal(360, pill.Bounds.Width, 0.5);
                Assert.Equal(Left(host, window), Left(pill, window), 0.5);
                // :2390 a first-baseline row 8 points apart: the glyph sits on the first line's baseline even when the text wraps.
                Assert.Equal(8, Left(text, window) - (Left(glyph, window) + glyph.Bounds.Width), 0.5);
                Assert.True(text.TextLayout.TextLines.Count > 1);
                Assert.Equal(FirstBaseline(text, window), GlyphBaseline(glyph, window), 0.5);
                Assert.True(Left(text, window) + text.Bounds.Width <= Left(host, window) + 360 - (isInfo ? 0 : 14) + 0.5, "the message runs past the pill's trailing inset");
                // :2394 .textSelection(.enabled): the message selects, and a selection is drawn in the system's selected-text colour (a
                // SelectableTextBlock has no SelectionBrush with no control theme loaded; ED-256 (5); review round 4).
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                text.SelectAll();
                Assert.Equal(message, text.SelectedText);
                Assert.True((text.SelectionBrush as ISolidColorBrush)?.Color == system,
                    $"the notice's message: the selection is drawn with {text.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
            }
            finally { Close(window); }
        });
    });
}
