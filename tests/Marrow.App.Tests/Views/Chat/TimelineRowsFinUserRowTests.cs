using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :64-229 UserMessageRow and :1167-1207 UserBubble.
[Collection("Headless")]
public sealed class TimelineRowsFinUserRowTests(HeadlessSession headless)
{
    private static Border Bubble(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(14, 9, 14 + 4, 9));

    private static StackPanel HoverLine(Control row) => Mounted<StackPanel>(row).Single(stack => stack.Height == 22 && double.IsNaN(stack.Width));

    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:88-129, :163-164, :1167-1207
    public void A_sent_message_is_a_trailing_bubble_hugging_its_text_tinted_at_14_percent_with_the_exact_tail_path() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var short_ = new UserMessageRow(UserEntry("hello there", Guid.NewGuid()), h.Runtime, canRevert: false);
            var long_ = new UserMessageRow(UserEntry(string.Join(" ", Enumerable.Repeat("a long line of words", 40)), Guid.NewGuid()), h.Runtime, canRevert: false);
            var panel = new StackPanel { Spacing = 60 };
            panel.Children.Add(short_);
            panel.Children.Add(long_);
            var window = Show(panel);
            try
            {
                var bubble = Bubble(short_);
                // :124-127 14 leading, 14 + the 4-point tail trailing, 9 vertical.
                Assert.Equal(new Thickness(14, 9, 18, 9), bubble.Padding);
                // :163 .frame(maxWidth: .infinity, alignment: .trailing): the bubble hugs its text and sits on the trailing edge.
                var text = Assert.Single(Mounted<InlineText>(short_));
                Assert.True(text.HugsContent);
                Assert.True(bubble.Bounds.Width < 200, "the bubble is wider than its short text");
                Assert.Equal(Left(panel, window) + panel.Bounds.Width, Left(bubble, window) + bubble.Bounds.Width, 0.5);
                // :164 .padding(.leading, 96): a long message wraps inside the row less 96 points of leading padding.
                var wide = Bubble(long_);
                Assert.InRange(wide.Bounds.Width, panel.Bounds.Width - 96 - 150, panel.Bounds.Width - 96 + 0.5);
                Assert.True(Left(wide, window) >= Left(panel, window) + 96 - 0.5);
                Assert.Equal(Left(panel, window) + panel.Bounds.Width, Left(wide, window) + wide.Bounds.Width, 0.5);
                // :128 .background(.tint.opacity(0.14), in: UserBubble()); the tint follows the theme accent.
                var shape = Assert.Single(Mounted<Avalonia.Controls.Shapes.Path>(short_));
                Paints(shape.Fill, ChromeTokens.Accent.Opacity(0.14), shape, "bubble fill");
                // :1167-1206 the path, from the control points the Swift gives (corner radius 18, tail 4).
                Assert.Equal(18, UserBubble.CornerRadius);
                Assert.Equal(4, UserBubble.Tail);
                var (w, hgt) = (bubble.Bounds.Width, bubble.Bounds.Height);
                var geometry = Assert.IsType<PathGeometry>(shape.Data);
                var figure = Assert.Single(geometry.Figures!);
                Assert.Equal(new Point(18, 0), figure.StartPoint);
                var s = figure.Segments!.ToList();
                Assert.Equal(10, s.Count);
                Assert.Equal(new Point(w - 4 - 18, 0), Assert.IsType<LineSegment>(s[0]).Point);
                var topRight = Assert.IsType<ArcSegment>(s[1]);
                Assert.Equal(new Point(w - 4, 18), topRight.Point);
                Assert.Equal(new Size(18, 18), topRight.Size);
                Assert.Equal(SweepDirection.Clockwise, topRight.SweepDirection);
                Assert.Equal(new Point(w - 4, hgt - 11), Assert.IsType<LineSegment>(s[2]).Point);
                var tip = Assert.IsType<BezierSegment>(s[3]);
                Assert.Equal((new Point(w - 4, hgt - 1), new Point(w, hgt), new Point(w, hgt)), (tip.Point1, tip.Point2, tip.Point3));
                var curl = Assert.IsType<BezierSegment>(s[4]);
                Assert.Equal((new Point(w - 4, hgt + 0.5), new Point(w - 8, hgt - 1), new Point(w - 11, hgt - 4)), (curl.Point1, curl.Point2, curl.Point3));
                var ease = Assert.IsType<BezierSegment>(s[5]);
                Assert.Equal((new Point(w - 16, hgt), new Point(w - 20, hgt), new Point(w - 25, hgt)), (ease.Point1, ease.Point2, ease.Point3));
                Assert.Equal(new Point(18, hgt), Assert.IsType<LineSegment>(s[6]).Point);
                Assert.Equal(new Point(0, hgt - 18), Assert.IsType<ArcSegment>(s[7]).Point);
                Assert.Equal(new Point(0, 18), Assert.IsType<LineSegment>(s[8]).Point);
                Assert.Equal(new Point(18, 0), Assert.IsType<ArcSegment>(s[9]).Point);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:88-96, :118-129, :130-161 (the row stack spacing is 6)
    public void The_sent_rows_stack_keeps_attachments_quotes_bubble_and_hover_line_six_points_apart() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            var row = new UserMessageRow(UserEntry("> quoted\n\nthe words", Guid.NewGuid(), new ValueList<Attachment>([file])), h.Runtime, canRevert: true);
            var window = Show(row);
            try
            {
                var strip = Assert.Single(Mounted<AttachmentStrip>(row));
                var quotes = Mounted<StackPanel>(row).Single(stack => stack.Spacing == 4);
                var bubble = Bubble(row);
                var line = HoverLine(row);
                Assert.Equal(6, Assert.IsType<StackPanel>(quotes.Parent).Spacing);
                double Top(Control c) => c.TranslatePoint(default, window)!.Value.Y;
                Assert.Equal(6, Top(quotes) - (Top(strip) + strip.Bounds.Height), 0.5);
                Assert.Equal(6, Top(bubble) - (Top(quotes) + quotes.Bounds.Height), 0.5);
                Assert.Equal(6, Top(line) - (Top(bubble) + bubble.Bounds.Height), 0.5);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-008")] // TimelineRows.swift:92-117; ComposerView.swift:962-970 ChipChrome; ThreadRuntime.swift:58-63 excerpt
    public void A_leading_quote_is_a_capsule_chip_with_a_footnote_glyph_and_a_capped_subheadline_excerpt() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var longQuote = new string('x', 30) + " " + new string('y', 60);
            var row = new UserMessageRow(UserEntry("> Hello    big   world\n> second line\n\n> " + longQuote + "\n\nthe typed words", Guid.NewGuid()), h.Runtime, false);
            var plain = new UserMessageRow(UserEntry("no quote here", Guid.NewGuid()), h.Runtime, false);
            var panel = new StackPanel { Spacing = 40 };
            panel.Children.Add(row);
            panel.Children.Add(plain);
            var window = Show(panel);
            try
            {
                var chips = Mounted<Border>(row).Where(border => AutomationProperties.GetName(border)?.StartsWith("Quote: ", StringComparison.Ordinal) == true).ToList();
                Assert.Equal(2, chips.Count);
                // :113 the accessibility label is Quote: followed by the excerpt; the excerpt is the first non-empty line,
                // whitespace collapsed, cut at 72 characters with an ellipsis.
                var excerpt2 = longQuote[..72] + "\u2026";
                Assert.Equal("Quote: Hello big world", AutomationProperties.GetName(chips[0]));
                Assert.Equal("Quote: " + excerpt2, AutomationProperties.GetName(chips[1]));
                // :111 .help(quote.text): the whole quote text.
                Assert.Equal("Hello    big   world\nsecond line", ToolTip.GetTip(chips[0]));
                Assert.Equal(longQuote, ToolTip.GetTip(chips[1]));
                foreach (var chip in chips)
                {
                    // ChipChrome: 9 leading, 7 trailing, 5 vertical; a continuous capsule filled with Chrome.overlay(0.12).
                    Assert.Equal(new Thickness(9, 5, 7, 5), chip.Padding);
                    Assert.Equal(chip.Bounds.Height / 2, chip.CornerRadius.TopLeft, 0.5);
                    Paints(chip.Background, ChromeTokens.Overlay(0.12), chip, "capsule fill");
                    var glyph = Assert.Single(Mounted<SwiftImage>(chip));
                    Assert.Equal("text.quote", glyph.SymbolName);
                    var glyphFont = SwiftImage.GetFont(glyph)!.Resolve();
                    Assert.Equal(10, glyphFont.FontSize); // .system(.footnote, weight: .semibold) at the macOS 10 pt footnote
                    Assert.Equal(FontWeight.SemiBold, glyphFont.FontWeight);
                    Paints(glyph.Foreground, ChromeTokens.SecondaryText, glyph, "glyph");
                    var excerpt = Assert.Single(Mounted<TextBlock>(chip));
                    Face(excerpt, 11, FontWeight.Normal, ".system(.subheadline)");
                    Paints(excerpt.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), excerpt, "excerpt");
                    Assert.Equal(1, excerpt.MaxLines);
                    Assert.Equal(TextTrimming.CharacterEllipsis, excerpt.TextTrimming);
                    Assert.True(excerpt.Bounds.Width <= 260 + 0.01);
                    // :98 HStack(spacing: 5), centred: the glyph and the words share a centre line.
                    var run = Assert.IsAssignableFrom<StackPanel>(excerpt.Parent);
                    Assert.Equal(5, run.Spacing);
                    Assert.Equal(5, Left(excerpt, window) - (Left(glyph, window) + glyph.Bounds.Width), 0.5);
                    OnOneCentreLine(window, glyph, excerpt);
                }
                // :96, :116 the strip is trailing-aligned, 4 points between chips, and cleared 4 points from the bubble's tail.
                var strip = Assert.IsType<StackPanel>(chips[0].Parent);
                Assert.Equal(4, strip.Spacing);
                Assert.Equal(HorizontalAlignment.Right, strip.HorizontalAlignment);
                Assert.Equal(4, strip.Margin.Right);
                Assert.Equal(4, Top(chips[1]) - (Top(chips[0]) + chips[0].Bounds.Height), 0.5);
                var bubble = Bubble(row);
                Assert.Equal(Left(bubble, window) + bubble.Bounds.Width - 4, Left(chips[0], window) + chips[0].Bounds.Width, 0.5);
                Assert.Equal(Left(bubble, window) + bubble.Bounds.Width - 4, Left(chips[1], window) + chips[1].Bounds.Width, 0.5);
                // The bubble keeps only the typed words; a message that led with no quote shows none.
                Assert.Equal("the typed words", Assert.Single(Mounted<InlineText>(row)).Source);
                Assert.DoesNotContain(Mounted<SwiftImage>(plain), image => image.SymbolName == "text.quote");

                double Top(Control c) => c.TranslatePoint(default, window)!.Value.Y;
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-008")] // TimelineRows.swift:96 VStack(alignment: .trailing, spacing: 4): each chip at its own width, flush right
    public void Quote_chips_keep_their_own_widths_and_meet_at_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(UserEntry("> short\n\n> " + new string('x', 30) + " " + new string('y', 30) + "\n\nthe typed words", Guid.NewGuid()), h.Runtime, false);
            var window = Show(row);
            try
            {
                var chips = Mounted<Border>(row).Where(border => AutomationProperties.GetName(border)?.StartsWith("Quote: ", StringComparison.Ordinal) == true).ToList();
                Assert.Equal(2, chips.Count);
                // A trailing-aligned VStack leaves each child at its own width; the StackPanel stretched the short chip to the long one's,
                // so its capsule drew wider than its words.
                foreach (var chip in chips) Assert.Equal(chip.DesiredSize.Width, chip.Bounds.Width, 0.5);
                Assert.True(chips[0].Bounds.Width < chips[1].Bounds.Width);
                double Right(Control c) => c.TranslatePoint(new Point(c.Bounds.Width, 0), window)!.Value.X;
                Assert.Equal(Right(chips[1]), Right(chips[0]), 0.5);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-010")] // TimelineRows.swift:130-161, :179-183
    [Trait("Req", "UI-GIT-049")] // TimelineRows.swift:130-156
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (STOP #11): TimelineRows.swift:130-161 (the pencil and its hover line), :132-135 (toggles the editor), :145-147 (hover prefetch), :179-183 (the reserved room)
    public void The_pencil_lives_on_a_reserved_hover_line_that_shows_on_hover_and_never_moves_the_message() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var pencilClock = new ManualFrameSource(); using var pencilFrames = FrameClock.Source.Override(pencilClock); // OD-0076: the pencil line fades on the frame clock
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var turn = Guid.NewGuid();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        UserMessageRow row = null!, locked = null!, noTurn = null!;
        AvaloniaWindow window = null!;
        double bubbleTop = 0, bubbleLeft = 0, rowHeight = 0;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("same words", turn), h.Runtime, canRevert: true);
            locked = new UserMessageRow(UserEntry("same words", Guid.NewGuid()), h.Runtime, canRevert: false);
            noTurn = new UserMessageRow(UserEntry("same words", null), h.Runtime, canRevert: true);
            var panel = new StackPanel { Spacing = 60 };
            foreach (var r in new Control[] { row, locked, noTurn }) panel.Children.Add(r);
            window = Show(panel);
            // :131 absent entirely when the turn is not revertable (or has no turn).
            Assert.Empty(Mounted<Button>(HoverLine(locked)));
            Assert.Empty(Mounted<Button>(HoverLine(noTurn)));
            var line = HoverLine(row);
            var pencil = Assert.Single(Mounted<Button>(line));
            // :160 the line is pinned to the hover line's room; :137 the hit area is 22 by 22; :142 the tooltip.
            Assert.Equal(22, line.Height);
            Assert.Equal("Edit message", ToolTip.GetTip(pencil));
            var frame = Mounted<Border>(pencil).First(border => border.Width == 22);
            Assert.Equal(22, frame.Bounds.Width);
            Assert.Equal(22, frame.Bounds.Height);
            var glyph = Assert.Single(Mounted<SwiftImage>(pencil));
            Assert.Equal("pencil", glyph.SymbolName);
            Paints(glyph.Foreground, SwiftColor.Secondary, glyph, "pencil outside editing (:141)");
            // :161 hidden (opacity 0) unless the row is hovered or the editor is open.
            Assert.True(line.Opacity == 0, "line hidden before hover");
            // :179-183 the room is reserved inside the row and taken back from its height: the row is as tall as the bubble
            // whether or not the controls exist, and showing the line moves nothing.
            Assert.Equal(Bubble(row).Bounds.Height, row.Bounds.Height, 0.5);
            Assert.Equal(row.Bounds.Height, locked.Bounds.Height, 0.5);
            bubbleTop = Bubble(row).TranslatePoint(default, window)!.Value.Y;
            bubbleLeft = Bubble(row).TranslatePoint(default, window)!.Value.X;
            rowHeight = row.Bounds.Height;
            Hover(window, Assert.Single(Mounted<InlineText>(row)));
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            var line = HoverLine(row);
            Assert.True(line.Opacity == 1, "line shown on hover");
            Assert.True(HoverLine(locked).Opacity == 0, "locked row's line stays hidden");
            Assert.Equal(bubbleTop, Bubble(row).TranslatePoint(default, window)!.Value.Y, 0.01);
            Assert.Equal(bubbleLeft, Bubble(row).TranslatePoint(default, window)!.Value.X, 0.01);
            Assert.Equal(rowHeight, row.Bounds.Height, 0.01);
            window.MouseMove(new Point(450, 680), RawInputModifiers.None);
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.True(HoverLine(row).Opacity == 0, "line hidden again once the pointer left");
            // :143-147 the pointer settling on the pencil starts the revert preview for this turn.
            Assert.Null(h.Runtime.CachedRevertPreview(turn));
            Hover(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Assert.NotNull(h.Runtime.CachedRevertPreview(turn));
            // :132-135 a click opens the editor on the turn's message; :148-155 anchored to the pencil.
            Click(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
            Assert.Equal(turn, edit.Id);
            Assert.Equal("same words", edit.Text);
            var shown = Assert.Single(presenter.Calls, call => call.Presented);
            Assert.IsType<MessageEditor>(shown.Popup.Child);
            var pencil = Assert.Single(Mounted<Button>(HoverLine(row)));
            Assert.Same(Mounted<AttachmentAnchorCapture>((Visual)pencil.Parent!).Single(), shown.Popup.PlacementTarget);
            // :141 tinted while the editor is open; :161 the line stays visible even without the pointer.
            Paints(Assert.Single(Mounted<SwiftImage>(pencil)).Foreground, ChromeTokens.Accent, pencil, "editing tint");
            window.MouseMove(new Point(450, 680), RawInputModifiers.None);
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.True(HoverLine(row).Opacity == 1, "line stays up while editing");
            Assert.Equal(bubbleTop, Bubble(row).TranslatePoint(default, window)!.Value.Y, 0.01);
            // :134 a second click while open closes the editor.
            Click(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Null(h.Runtime.MessageEdit);
            window.MouseMove(new Point(450, 680), RawInputModifiers.None);
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.True(HoverLine(row).Opacity == 0, "line hidden again after the editor closed and the pointer left");
            Paints(Assert.Single(Mounted<SwiftImage>(HoverLine(row))).Foreground, SwiftColor.Secondary, row, "pencil after editing");
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "UI-GIT-050")] // TimelineRows.swift:216-228 messageActions, :172-190 the intent and the confirmation
    public void Edit_from_here_is_the_third_item_in_its_own_group_only_when_rewindable_and_raises_the_row_dialog() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var popovers = new RecordingPresenter();
        using var popoverSeam = Presentation.Presenter.Override(popovers);
        var dialogs = new RecordingConfirmationPresenter();
        using var dialogSeam = AlertPresentation.Presenter.Override(dialogs);
        AvaloniaWindow window = null!, menu = null!;
        UserMessageRow row = null!;
        h.Main.OnMain(() =>
        {
            var plain = new UserMessageRow(UserEntry("hello", Guid.NewGuid()), h.Runtime, canRevert: false);
            row = new UserMessageRow(UserEntry("hello again", Guid.NewGuid()), h.Runtime, canRevert: true);
            var panel = new StackPanel { Spacing = 80 };
            panel.Children.Add(plain);
            panel.Children.Add(row);
            window = Show(panel);
            var (_, first) = RightClick(popovers, window, Assert.Single(Mounted<InlineText>(plain)));
            Assert.Equal(["Copy message", "Copy as markdown"], ItemTitles(first));
            var (popup, second) = RightClick(popovers, window, Assert.Single(Mounted<InlineText>(row)));
            // :218-226 the two copies, then Edit from here as the third item, starting a new group.
            Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], ItemTitles(second));
            Assert.Equal("arrow.uturn.backward", ItemSymbols(second)[2]);
            var stack = Assert.IsType<StackPanel>(second.Child);
            Assert.IsType<PopoverDivider>(stack.Children[2]);
            Assert.Single(stack.Children.OfType<PopoverDivider>());
            // :223-225 the click only posts an intent; the row takes it up and confirms on itself (:184-196).
            menu = HostMenu(popup);
            var edit = LogicalQuery.SelfAndDescendants<PopoverItem>(second).Single(item => Title(item) == "Edit from here");
            Assert.Empty(dialogs.Shown);
            ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(edit)));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return dialogs.Shown.Count == 1; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var dialog = Assert.Single(dialogs.Shown);
            Assert.Equal("Edit from this message?", dialog.Title);
            Assert.Same(window, dialog.Owner);
            dialogs.Cancel();
        });
        h.Main.OnMain(() => { Close(menu); Close(window); });
    });
}
