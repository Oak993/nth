using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2463-2478 MutedRoundGlyph and :2718-2859 TurnFileCard.
[Collection("Headless")]
public sealed class TimelineRowsFinFileCardTests(HeadlessSession headless)
{
    private static FileChangeSummary Files(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static Button DiscWith(Control card, string symbol) =>
        Mounted<Button>(card).Single(button => Mounted<SwiftImage>(button).Any(image => image.SymbolName == symbol));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-037")] // TimelineRows.swift:2735-2827 the card
    [Trait("Req", "UI-GIT-046")] // TimelineRows.swift:2770-2776 the revert glyph button
    [Trait("Req", "UI-GIT-048")] // TimelineRows.swift:2800-2816 the review glyph button
    public void The_file_card_is_a_hugging_quaternary_pill_with_the_title_button_a_20_point_gap_and_the_two_round_buttons() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var one = new TurnFileCard(Files(1), canUndo: true, onRevert: () => { }, onReview: _ => { });
            var three = new TurnFileCard(Files(3), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var none = new TurnFileCard(new FileChangeSummary(ValueList<FileChangeSummary.File>.Empty, 0, 0), canUndo: true, onRevert: () => { }, onReview: _ => { });
            var panel = new StackPanel { Spacing = 20, Width = 700 };
            foreach (var card in new Control[] { one, three, none }) panel.Children.Add(card);
            var window = Show(panel);
            try
            {
                // :2740 the title is exactly Edited 1 file / Edited N files; the card's accessibility label is the same.
                Assert.Equal("Edited 1 file", AutomationProperties.GetName(one));
                Assert.Equal("Edited 3 files", AutomationProperties.GetName(three));
                var pill = Mounted<Border>(one).First(border => border.Padding == new Thickness(12, 6, 6, 6));
                // :2818-2822 padded 12 leading, 6 trailing and 6 vertical, radius 18, .quaternary at 0.32, hugging its content, leading.
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                // :2822 `.background(.quaternary.opacity(0.32), in: RoundedRectangle(cornerRadius: TimelineMetrics.pillRadius, style: .continuous))` (ED-181
                // closed): the card is 40 pt tall (28 + 12), under the 55 pt an 18 pt continuous corner needs, so its ends are the circular arc the
                // video shows for the Edited card (static.timeline-rows: radius 18).
                ContinuousCornerChecks.Drawn(pill, expectApple: false);
                Paints(pill.Background, SwiftColor.Quaternary.Opacity(0.32), pill, "card fill");
                Assert.Equal(HorizontalAlignment.Left, pill.HorizontalAlignment);
                Near("gap 1", Left(panel, window), Left(pill, window));
                Assert.True(pill.Bounds.Width < 450, "the card did not hug its content");
                Assert.Equal(28 + 12, pill.Bounds.Height, 0.5); // the 28-point discs and 6 points above and below
                // :2742-2760 the title button: badge, title, stats, chevron; :2763 its tooltip.
                var title = Assert.Single(Mounted<Button>(three), button => ToolTip.GetTip(button) as string == "Show the files this turn changed");
                var badge = Assert.Single(Mounted<SwiftImage>(title), image => image.Width == 18);
                Assert.Equal(18, badge.Height);
                Paints(badge.Foreground, ChromeTokens.SecondaryText, badge, "badge");
                var words = TextNamed(title, "Edited 3 files");
                Face(words, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Paints(words.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), words, "title");
                var stat = Assert.Single(Mounted<DiffStatLabel>(title));
                Assert.Equal((40, 7), (stat.Additions, stat.Deletions));
                var chevron = Assert.Single(Mounted<SwiftImage>(title), image => image.SymbolName == "chevron.right");
                var chevronFont = SwiftImage.GetFont(chevron)!.Resolve();
                Assert.Equal(10, chevronFont.FontSize);
                Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                // :2743 HStack(spacing: 8) between badge, title, stats and chevron: one centre line.
                Near("gap 2", 8, Left(words, window) - (Left(badge, window) + 18));
                Near("gap 3", 8, Left(stat, window) - (Left(words, window) + words.Bounds.Width));
                Near("gap 4", 8, Left(chevron, window) - (Left(stat, window) + stat.Bounds.Width));
                OnOneCentreLine(window, badge, words, stat, chevron);
                // No files: the title button is disabled and the chevron is absent.
                var emptyTitle = Assert.Single(Mounted<Button>(none), button => ToolTip.GetTip(button) as string == "Show the files this turn changed");
                Assert.False(emptyTitle.IsEnabled);
                Assert.DoesNotContain(Mounted<SwiftImage>(none), image => image.SymbolName == "chevron.right");
                // :2768-2816 the fixed gap and the discs: 8 + 20 + 8 after the title button, then 8 between the two discs.
                var revert = DiscWith(one, "arrow.uturn.backward");
                var review = DiscWith(one, "plusminus");
                var oneTitle = Assert.Single(Mounted<Button>(one), button => ToolTip.GetTip(button) as string == "Show the files this turn changed");
                Near("gap 5", 8 + 20 + 8, Left(revert, window) - (Left(oneTitle, window) + oneTitle.Bounds.Width));
                Near("gap 6", 8, Left(review, window) - (Left(revert, window) + revert.Bounds.Width));
                Near("gap 7", 6, Left(pill, window) + pill.Bounds.Width - (Left(review, window) + review.Bounds.Width));
                // The reference video's Edited card (catalogue static.timeline-rows, 13 files: pill 40 tall, radius 18, badge 18 at x + 13, discs 28
                // with centres 36 apart and the last centre 20 from the right edge) equals the Swift constants asserted here: 40 = 28 + 12; pitch
                // 28 + 8; 14 + 6 trailing padding; the badge at the 12 pt leading padding (its ink starts a pixel further in, the symbol's own inset).
                Near("disc pitch", 36, Left(review, window) - Left(revert, window));
                Near("last disc centre from the right edge", 20, Left(pill, window) + pill.Bounds.Width - (Left(review, window) + review.Bounds.Width / 2));
                Near("badge from the left edge", 12, Left(badge, window) - Left(pill, window));
                Assert.Equal("Revert this turn's files and conversation", ToolTip.GetTip(revert));
                Assert.Equal("Show this turn's changes", ToolTip.GetTip(review));
                Assert.False(revert.Focusable);
                Assert.False(review.Focusable);
                OnOneCentreLine(window, oneTitle, revert, review);
                // The revert disc is absent when the turn cannot be undone; the review disc is always there.
                Assert.DoesNotContain(Mounted<SwiftImage>(three), image => image.SymbolName == "arrow.uturn.backward");
                Assert.Single(Mounted<SwiftImage>(three), image => image.SymbolName == "plusminus");
                Assert.Single(Mounted<SwiftImage>(none), image => image.SymbolName == "plusminus");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-038")] // TimelineRows.swift:2463-2478 the muted round glyph, :2770-2799 the revert disc
    [Trait("Req", "UI-CHAT-040")] // TimelineRows.swift:2800-2816 the review disc is the same chrome
    public void A_round_button_is_a_28_point_flat_overlay_disc_that_brightens_over_a_tenth_of_a_second_under_the_pointer() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        AvaloniaWindow window = null!;
        MutedRoundGlyph disc = null!;
        runtime.OnMain(() =>
        {
            disc = new MutedRoundGlyph("arrow.uturn.backward");
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { disc } });
            var circle = Mounted<Border>(disc).Single(border => border.Width == 28);
            Assert.Equal(28, circle.Bounds.Height);
            Assert.Equal(new CornerRadius(14), circle.CornerRadius);
            // :2469 Chrome.iconFont is .system(.callout, weight: .semibold): 12 points.
            var glyph = Assert.Single(Mounted<SwiftImage>(disc));
            var font = SwiftImage.GetFont(glyph)!.Resolve();
            Assert.Equal(12, font.FontSize);
            Assert.Equal(FontWeight.SemiBold, font.FontWeight);
            Paints(glyph.Foreground, ChromeTokens.SecondaryText, glyph, "resting glyph");
            Paints(circle.Background, ChromeTokens.Overlay(0.1), circle, "resting fill");
            Hover(window, circle);
        });
        // :2475 withAnimation(Chrome.hover): an ease-out of 0.1 s; part way at 40 ms, there by 100 ms.
        frames.Advance(TimeSpan.Zero);
        runtime.AdvanceMilliseconds(40);
        frames.Advance(TimeSpan.FromMilliseconds(40));
        runtime.OnMain(() =>
        {
            var circle = Mounted<Border>(disc).Single(border => border.Width == 28);
            var partway = ((ISolidColorBrush)circle.Background!).Color;
            Assert.NotEqual(Resolve(ChromeTokens.Overlay(0.1), circle), partway);
            Assert.NotEqual(Resolve(ChromeTokens.Overlay(0.18), circle), partway);
        });
        runtime.AdvanceMilliseconds(200);
        frames.Advance(TimeSpan.FromMilliseconds(200));
        runtime.OnMain(() =>
        {
            try
            {
                var circle = Mounted<Border>(disc).Single(border => border.Width == 28);
                var glyph = Assert.Single(Mounted<SwiftImage>(disc));
                Paints(glyph.Foreground, ChromeTokens.PrimaryText, glyph, "hovered glyph");
                Paints(circle.Background, ChromeTokens.Overlay(0.18), circle, "hovered fill");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-GIT-044")] // TimelineRows.swift:2745-2757, :2829-2858 clicking the title shows the file list in a popover below it
    public void Clicking_the_title_opens_the_files_popover_below_it_with_twelve_rows_and_a_second_click_closes_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        TurnFileCard card = null!;
        AvaloniaWindow window = null!;
        Button title = null!;
        runtime.OnMain(() =>
        {
            card = new TurnFileCard(Files(13), canUndo: true, onRevert: () => { }, onReview: _ => { });
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { card } });
            title = Assert.Single(Mounted<Button>(card), button => ToolTip.GetTip(button) as string == "Show the files this turn changed");
            Assert.DoesNotContain(presenter.Calls, call => call.Presented);
            Click(window, title);
        });
        runtime.OnMain(() =>
        {
            try
            {
                var opened = Assert.Single(presenter.Calls, call => call.Presented).Popup; // :2757 .popover(isPresented:, arrowEdge: .bottom)
                Assert.Equal(PlacementMode.Bottom, opened.Placement);
                // :2829-2858 the first twelve files, then "and 1 more file" for the thirteenth.
                Assert.Equal(12, LogicalQuery.SelfAndDescendants<MiddleTruncatingTextBlock>(opened).Count());
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(opened), text => text.Text == "and 1 more file");
                Click(window, title); // the toggle: a second click dismisses it
            }
            catch { Close(window); throw; }
        });
        runtime.OnMain(() =>
        {
            try
            {
                var shown = presenter.Calls.Where(call => call.Presented).Select(call => call.Popup).Distinct().Single();
                Assert.False(presenter.Calls[^1].Presented);
                Assert.Same(shown, presenter.Calls[^1].Popup);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-040")] // TimelineRows.swift:2800-2816 its anchor is captured lazily on first hover
    [Trait("Req", "UI-GIT-048")] // TimelineRows.swift:2800-2816 hover arms the anchor capture; click opens the changes on this button
    public void The_review_button_captures_its_own_anchor_only_once_hovered_and_a_click_before_that_does_nothing() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var reviewed = new List<Control>();
        TurnFileCard card = null!;
        AvaloniaWindow window = null!;
        runtime.OnMain(() =>
        {
            card = new TurnFileCard(Files(2), canUndo: false, onRevert: () => { }, onReview: reviewed.Add);
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { card } });
            var review = DiscWith(card, "plusminus");
            Click(window, review);
            // :2807-2811 a click that never followed a hover leaves the anchor unset: the control silently does nothing.
            Assert.Empty(reviewed);
            Assert.Empty(Mounted<AttachmentAnchorCapture>(card));
        });
        runtime.OnMain(() =>
        {
            Settle(window);
            Hover(window, DiscWith(card, "plusminus"));
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                var anchor = Assert.Single(Mounted<AttachmentAnchorCapture>(card));
                Click(window, DiscWith(card, "plusminus"));
                // :2698 the changes open on the button itself: the anchor it captured is what the review closure receives.
                Assert.Same(anchor, Assert.Single(reviewed));
            }
            finally { Close(window); }
        });
    });
}
