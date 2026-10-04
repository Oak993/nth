using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2463-2478 MutedRoundGlyph and :2718-2858 TurnFileCard with its files
// popover and its revert confirmation. The popovers are opened by real clicks and their content is read through PopoverHost.Content
// (OD-0095), then hosted and measured; where a record's capture comparison asks for a frame the reference videos do not contain, the
// Swift source is the proof (OD-0098). The card itself is in a reference frame (2-chat-use.mov 9.0 s, "Edited 13 files"), drawn in a
// .continuous rounded rectangle, which ED-181 still draws with circular arcs: its corner is the one thing the renders below do not claim.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbFileCardTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static FileChangeSummary Changes(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static Button WithTip(Control root, string tip) => Mounted<Button>(root).Single(button => Equals(ToolTip.GetTip(button), tip));

    // The popup the last click presented, its content read through PopoverHost.Content and taken out to be hosted in a window of its own.
    private static Control TakeContent(RecordingPresenter presenter, out Popup popup)
    {
        popup = presenter.Calls.Last(call => call.Presented).Popup;
        var content = PopoverHost.Content(popup) ?? throw new Xunit.Sdk.XunitException("the popover has no content");
        switch (popup.Child)
        {
            case PopoverCard card: card.Child = null; break;
            default: popup.Child = null; break;
        }
        return content;
    }

    // ---- PR-CHAT-109: the files popover at 12, 13 and 20 files ----

    [Theory]
    [Trait("Req", "PR-CHAT-109")] // TimelineRows.swift:2764-2766, :2829-2858; OD-0098, OD-0095
    [InlineData(1, 1, null)]
    [InlineData(12, 12, null)]
    [InlineData(13, 12, "and 1 more file")]
    [InlineData(14, 12, "and 2 more files")]
    [InlineData(20, 12, "and 8 more files")]
    public void The_files_popover_opened_by_a_click_lists_at_most_twelve_files_and_then_how_many_more_in_secondary_callout_text(int total, int listed, string? overflow) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        TurnFileCard card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            card = new TurnFileCard(Changes(total), canUndo: false, onRevert: () => { }, onReview: _ => { });
            window = Show(card);
            Click(window, WithTip(card, "Show the files this turn changed"));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var content = TakeContent(presenter, out var popup);
                Assert.Equal(PlacementMode.Bottom, popup.Placement); // :2764 arrowEdge: .bottom
                foreach (var (theme, backdrop) in Themes)
                {
                    var host = new Border { Child = content, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
                    BlitzAbRender.Render(host, theme, backdrop, 440, 40 * (listed + 2), (shown, frame) =>
                    {
                        var paths = Mounted<MiddleTruncatingTextBlock>(content).ToList();
                        Assert.Equal(Enumerable.Range(1, listed).Select(i => "src/file" + i + ".swift"), paths.Select(path => path.FullText));
                        Assert.Equal(listed, Mounted<DiffStatLabel>(content).Count());
                        // :2847-2848 each row padded 12 horizontally and 7 vertically: the path starts 12 in, rows are the text's height + 14 apart.
                        Assert.All(paths, path => Assert.Equal(Left(content, shown) + 12, Left(path, shown), 0.5));
                        for (var i = 1; i < paths.Count; i++)
                            Assert.Equal(paths[i - 1].Bounds.Height + 14, CentreY(paths[i], shown) - CentreY(paths[i - 1], shown), 0.75);
                        foreach (var path in paths) Paints(path.Foreground, SwiftColor.Primary, path, "file path (:2835 no foreground style)");
                        if (overflow is null) Assert.DoesNotContain(Texts(content), text => text.StartsWith("and ", StringComparison.Ordinal));
                        else
                        {
                            var more = TextNamed(content, overflow);
                            Paints(more.Foreground, ChromeTokens.SecondaryText, more, "the overflow line (:2853)");
                            Assert.Equal(Left(content, shown) + 12, Left(more, shown), 0.5);
                            Assert.Equal(paths[^1].Bounds.Height + 14, CentreY(more, shown) - CentreY(paths[^1], shown), 2.5);
                            BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(more, shown), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the overflow line in {theme}", closest: 130);
                        }
                    });
                    host.Child = null;
                }
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-109")] // TimelineRows.swift:2833-2845: a binary file reads Binary in caption secondary; a long path is cut in the middle and the stat stays beside it
    public void A_binary_file_reads_Binary_and_a_long_path_is_cut_in_the_middle_with_its_stat_kept_a_gap_of_at_least_eight_clear() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        TurnFileCard card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            var longPath = "src/" + string.Concat(Enumerable.Repeat("deeply/nested/", 12)) + "file.swift";
            var summary = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("logo.png", 0, 0, true), new(longPath, 120, 30, false)]), 120, 30);
            card = new TurnFileCard(summary, canUndo: false, onRevert: () => { }, onReview: _ => { });
            window = Show(card);
            Click(window, WithTip(card, "Show the files this turn changed"));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var content = TakeContent(presenter, out _);
                var host = new Border { Child = content, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
                var shown = BlitzAbRender.Open(host, ThemeVariant.Dark, Color.FromRgb(24, 24, 27), 320, 120);
                try
                {
                    BlitzAbRender.Capture(shown);
                    var binary = TextNamed(content, "Binary");
                    Face(binary, 10, FontWeight.Normal, "Binary (:2841 caption)");
                    Paints(binary.Foreground, ChromeTokens.SecondaryText, binary, "Binary");
                    Assert.DoesNotContain(Mounted<DiffStatLabel>(content), stat => stat.Additions == 0 && stat.Deletions == 0); // a binary file has no stat
                    var paths = Mounted<MiddleTruncatingTextBlock>(content).ToList();
                    var cut = paths[1];
                    var stat = Assert.Single(Mounted<DiffStatLabel>(content));
                    Assert.True(cut.Bounds.Width < 300 - 24 - stat.Bounds.Width, "the long path is not cut to the room the stat leaves");
                    Assert.True(Left(stat, shown) - (Left(cut, shown) + cut.Bounds.Width) >= 24 - 0.5, "the stat is nearer than the spacing, spacer and spacing");
                    Assert.Equal(1, cut.LineLimit);
                    Assert.Equal(Left(content, shown) + 300 - 12, Left(binary, shown) + binary.Bounds.Width, 0.5); // the trailing item ends 12 in
                }
                finally { BlitzAbRender.Shut(shown); }
            }
            finally { Close(window); }
        });
    });

    // ---- PR-CHAT-110: the revert confirmation ----

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2770-2798, :2695; OD-0098, OD-0095
    public void The_revert_disc_opens_a_300_point_popover_whose_text_is_verbatim_with_Revert_the_default_action_and_Cancel_doing_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var reverts = 0;
        TurnFileCard card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            card = new TurnFileCard(Changes(2), canUndo: true, onRevert: () => reverts++, onReview: _ => { });
            window = Show(card);
            Click(window, WithTip(card, "Revert this turn's files and conversation"));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var content = TakeContent(presenter, out var popup);
                Assert.Equal(PlacementMode.Bottom, popup.Placement); // :2777 arrowEdge: .bottom
                var shown = BlitzAbRender.Open(content, ThemeVariant.Dark, Color.FromRgb(24, 24, 27), 340, 200);
                try
                {
                    BlitzAbRender.Capture(shown);
                    var heading = TextNamed(content, "Revert up to this point?");
                    Assert.Equal(14, heading.FontSize); // :2780 .system(size: 14, weight: .semibold)
                    Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
                    var message = TextNamed(content, "Files go back to how they were before this turn, and the turn leaves the conversation.");
                    Assert.Equal(12, message.FontSize); // :2782 .system(.callout)
                    Paints(message.Foreground, ChromeTokens.SecondaryText, message, "the message");
                    // :2795-2796 padded 16 and fixed at 300 wide; :2778 spacing 10 between the heading, the message and the buttons.
                    var sheet = Mounted<Border>(content).Append((Border)content).First(border => border.Width == 300);
                    Assert.Equal(300, sheet.Bounds.Width);
                    Assert.Equal(Left(sheet, shown) + 16, Left(heading, shown), 0.5);
                    Assert.Equal(Top(heading, shown) , Top(sheet, shown) + 16, 0.5);
                    Assert.Equal(10, Top(message, shown) - (Top(heading, shown) + heading.Bounds.Height), 0.5);
                    var cancel = Mounted<Button>(content).Single(button => Texts(button).Contains("Cancel"));
                    var go = Mounted<Button>(content).Single(button => Texts(button).Contains("Revert"));
                    Assert.Equal(10, Top(cancel, shown) - (Top(message, shown) + message.Bounds.Height), 0.5);
                    // :2785-2786 the pair trails the line (Spacer first), Cancel then Revert, 8 apart.
                    Assert.Equal(Left(sheet, shown) + 300 - 16, Left(go, shown) + go.Bounds.Width, 0.5);
                    Assert.Equal(8, Left(go, shown) - (Left(cancel, shown) + cancel.Bounds.Width), 0.5);
                    Assert.True(go.IsDefault); // :2791 .keyboardShortcut(.defaultAction)
                    Assert.False(cancel.IsDefault);
                    // Cancel closes and does nothing ...
                    Click(shown, cancel);
                    Assert.Equal(0, reverts);
                    Assert.Empty(h.Runtime.Turns);
                    // ... and Revert (the default action, so Return too: IsDefault above) asks for the revert (files restored, :2695).
                    Click(shown, go);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(1, reverts);
                }
                finally { BlitzAbRender.Shut(shown); }
            }
            finally { Close(window); }
        });
    });

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    // ---- PR-CHAT-108: the card, drawn ----

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827, :2463-2478; OD-0098 (every value but the pill's continuous corner, ED-181)
    public void The_file_card_is_drawn_as_a_quaternary_pill_with_secondary_title_ink_and_two_28_point_overlay_discs_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var card = new TurnFileCard(Changes(13), canUndo: true, onRevert: () => { }, onReview: _ => { });
                var host = new Border { Padding = new Thickness(24), Child = card };
                BlitzAbRender.Render(host, theme, backdrop, 520, 100, (window, frame) =>
                {
                    var pill = Mounted<Border>(card).First(border => border.Padding == new Thickness(12, 6, 6, 6));
                    var box = BlitzAbRender.BoxIn(pill, window);
                    var expectedFill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), backdrop);
                    // The fill, in the pill's own margin (12 leading, 6 vertical): clear of every control.
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), expectedFill) <= 6, $"{theme}: the card's fill");
                    // The pill hugs its content: it ends 6 points past the last disc (:2820, :2823).
                    var discs = Mounted<Border>(card).Where(border => border.Width == 28 && border.Height == 28).ToList();
                    Assert.Equal(2, discs.Count);
                    Assert.Equal(box.Right, Left(discs[^1], window) + 28 + 6, 0.5);
                    Assert.Equal(box.Top + 6, Top(discs[0], window), 0.5);
                    // The discs are Chrome.overlay(0.1) over the pill (:2472); the glyphs Chrome.secondaryText (:2470).
                    var discFill = BlitzAbFrame.Over(ChromeTokens.Overlay(0.1).Resolve(theme), expectedFill);
                    foreach (var disc in discs)
                    {
                        var area = BlitzAbRender.BoxIn(disc, window);
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)area.Left + 2, (int)(area.Top + 14)), discFill) <= 6, $"{theme}: a disc's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)area.Left, (int)area.Top), expectedFill) <= 6, $"{theme}: a disc's corner is filled (a circle)");
                        BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(Assert.Single(Mounted<SwiftImage>(disc)), window), discFill, ChromeTokens.SecondaryText.Resolve(theme), $"{theme} disc glyph", most: 1.6);
                    }
                    // The title in Chrome.primaryText at 0.9, medium callout (:2750-2751), on the pill.
                    var title = TextNamed(card, "Edited 13 files");
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(title, window), expectedFill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), $"the title in {theme}", closest: 110);
                    // The 18-point badge at the pill's leading padding (:2744-2748), a 20-point gap before the first disc (:2769).
                    var badge = Mounted<SwiftImage>(card).First(image => image.Width == 18);
                    Assert.Equal(box.Left + 12, Left(badge, window), 0.5);
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(badge, window), expectedFill, ChromeTokens.SecondaryText.Resolve(theme), $"{theme} badge", most: 1.6);
                    var chevron = Mounted<SwiftImage>(card).First(image => image.SymbolName == "chevron.right");
                    var gap = Left(discs[0], window) - (Left(chevron, window) + chevron.Bounds.Width);
                    Assert.Equal(8 + 20 + 8, gap, 0.75); // the title button's slot, the spacing, the 20-point gap and the spacing
                    Assert.Equal(8, Left(discs[1], window) - (Left(discs[0], window) + 28), 0.5);
                });
            }
        });
    });
}
