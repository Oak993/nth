using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
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

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :64-229 UserMessageRow (the quote chips, the attachment strip, the
// pencil and its confirmation) and :1353-1390 AssistantMessageRow. Where a record's capture comparison asks for a frame the reference
// videos do not contain, the Swift source is the proof (OD-0098): the states the code names are mounted and rendered, and the values the
// code states are read back from the layout and from the rendered frame.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbUserTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static Border Bubble(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(14, 9, 14 + 4, 9));

    private static StackPanel HoverLine(Control row) => Mounted<StackPanel>(row).Single(stack => stack.Height == 22 && double.IsNaN(stack.Width));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static void Frames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        main.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    // ---- PR-CHAT-078: the quotes a message led with ----

    [Theory]
    [Trait("Req", "PR-CHAT-078")] // ThreadRuntime.swift:122-135 outgoingText and :71-99 peel: the composer's outgoing text read back is the quotes and the typed words
    [InlineData("one", "the words")]
    [InlineData("first line\nsecond line", "words\non two lines")]
    [InlineData("a\n\nb", "after a blank line")]
    [InlineData("quoted", "")]
    [InlineData("  padded  ", "x")]
    public void The_composers_outgoing_text_peels_back_to_exactly_the_quotes_and_the_words_it_was_built_from(string quote, string words)
    {
        var draft = new ComposerDraft
        {
            Text = words,
            Quotes = new ValueList<ReplyQuote>([new ReplyQuote { Text = quote }, new ReplyQuote { Text = "second " + quote }]),
        };
        var (quotes, body) = ReplyQuote.Peel(draft.OutgoingText);
        Assert.Equal([quote, "second " + quote], quotes.Select(item => item.Text));
        Assert.Equal(words, body);
        // With a slash command the lifted command is rejoined with one space (:97): command, a blank line, the quotes, the words.
        var withCommand = draft with { Command = new DraftCommand { Name = "plan", Detail = "", IsBuiltIn = true } };
        var (commandQuotes, commandBody) = ReplyQuote.Peel(withCommand.OutgoingText);
        Assert.Equal([quote, "second " + quote], commandQuotes.Select(item => item.Text));
        Assert.Equal(words.Length == 0 ? "/plan" : "/plan " + words, commandBody);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")] // TimelineRows.swift:92-117, ComposerView.swift:962-970 ChipChrome; OD-0098 (no chip frame): the chips drawn
    public void The_quote_chips_are_drawn_as_overlay_capsules_with_secondary_glyphs_and_primary_words_flush_right_above_the_bubble_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var row = new UserMessageRow(UserEntry("> short\n\n> a longer quoted line of words\n\nthe typed words", Guid.NewGuid()), h.Runtime, canRevert: false);
                var host = new Border { Padding = new Thickness(0, 20, 0, 0), Child = row };
                BlitzAbRender.Render(host, theme, backdrop, 640, 200, (window, frame) =>
                {
                    var chips = Mounted<Border>(row).Where(border => AutomationProperties.GetName(border)?.StartsWith("Quote: ", StringComparison.Ordinal) == true).ToList();
                    Assert.Equal(2, chips.Count);
                    var bubble = Bubble(row);
                    foreach (var chip in chips)
                    {
                        var box = BlitzAbRender.BoxIn(chip, window);
                        // The capsule's fill (:110 ChipChrome: Chrome.overlay(0.12)) a few points inside its left end, clear of the glyph.
                        var inside = frame.At((int)box.Left + 3, (int)(box.Top + box.Height / 2));
                        var expected = BlitzAbFrame.Over(ChromeTokens.Overlay(0.12).Resolve(theme), backdrop);
                        Assert.True(BlitzAbFrame.Distance(inside, expected) <= 6, $"{theme}: the chip fill is {inside}, the source gives {expected}");
                        // A capsule: its corner is not filled.
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left, (int)box.Top), backdrop) <= 6, $"{theme}: the chip's corner is filled");
                        var glyph = Assert.Single(Mounted<SwiftImage>(chip));
                        BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(glyph, window), expected, ChromeTokens.SecondaryText.Resolve(theme), $"{theme} quote glyph", most: 1.6);
                        var words = Assert.Single(Mounted<TextBlock>(chip));
                        BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(words, window), expected, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), $"{theme} quote words", closest: 110);
                        // Flush right, 4 points short of the bubble's tail edge (:116).
                        Assert.Equal(Left(bubble, window) + bubble.Bounds.Width - 4, box.Right, 0.5);
                    }
                    Assert.True(chips[0].Bounds.Width < chips[1].Bounds.Width);
                    Assert.Equal(4, Top(chips[1], window) - (Top(chips[0], window) + chips[0].Bounds.Height), 0.5);
                    Assert.True(Top(chips[1], window) + chips[1].Bounds.Height < Top(bubble, window), "the chips sit above the bubble");
                });
            }
        });
    });

    // ---- PR-CHAT-079: the attachment strip ----

    [Fact]
    [Trait("Req", "PR-CHAT-079")] // TimelineRows.swift:88-91, :1209-1233, :1235-1351; OD-0098: the strip over the bubble, laid out and drawn (the plate's continuous corner is ED-181's)
    public void The_strip_is_56_point_plates_6_apart_right_aligned_over_the_bubble_on_a_quaternary_plate_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var path = Path.Combine(h.Project.Path, "plate.png");
            // BGRA, one flat colour: the picture fills the plate (a 2 x 2 image drawn aspect-fill into a square).
            ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, [.. Enumerable.Repeat(new byte[] { 40, 40, 200, 255 }, 4).SelectMany(pixel => pixel)]), path, 56);
            var files = new ValueList<Attachment>([
                new Attachment { Name = "plate.png", Path = path, MimeType = "image/png" },
                new Attachment { Name = "notes.txt", Path = Path.Combine(h.Project.Path, "notes.txt"), MimeType = "text/plain" }]);
            foreach (var (theme, backdrop) in Themes)
            {
                var row = new UserMessageRow(UserEntry("see these", Guid.NewGuid(), files), h.Runtime, canRevert: false);
                var host = new Border { Padding = new Thickness(0, 20, 0, 0), Child = row };
                BlitzAbRender.Render(host, theme, backdrop, 640, 220, (window, frame) =>
                {
                    var strip = Assert.Single(Mounted<AttachmentStrip>(row));
                    var plates = Mounted<Border>(strip).Where(border => border.Width == 56 && border.Height == 56).ToList();
                    Assert.Equal(2, plates.Count);
                    var bubble = Bubble(row);
                    // 56 square, 6 apart (:1222, :1237), the strip hugging them and right-aligned over the bubble (:88, :1220-1221).
                    Assert.All(plates, plate => { Assert.Equal(56, plate.Bounds.Width); Assert.Equal(56, plate.Bounds.Height); });
                    Assert.Equal(6, Left(plates[1], window) - (Left(plates[0], window) + 56), 0.5);
                    Assert.Equal(112 + 6, strip.Bounds.Width, 0.5);
                    Assert.Equal(Left(bubble, window) + bubble.Bounds.Width, Left(strip, window) + strip.Bounds.Width, 0.5);
                    Assert.Equal(6, Top(bubble, window) - (Top(strip, window) + strip.Bounds.Height), 0.5);
                    // The picture fills its plate and the plate clips it to the 12-point corner (:1293-1304).
                    var picture = BlitzAbRender.BoxIn(plates[0], window);
                    var middle = frame.At((int)(picture.Left + 28), (int)(picture.Top + 28));
                    Assert.True(middle.R > 150 && middle.G < 90 && middle.B < 90, $"{theme}: the picture does not fill its plate ({middle})");
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)picture.Left, (int)picture.Top), backdrop) <= 6, $"{theme}: the plate's corner is not clipped");
                    // The file's plate (:1312-1314): .quaternary at 0.6 behind the shell icon, in the plate's own margin.
                    var file = BlitzAbRender.BoxIn(plates[1], window);
                    var fill = frame.At((int)(file.Left + 28), (int)(file.Top + 1));
                    var expected = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.6).Resolve(theme), backdrop);
                    Assert.True(BlitzAbFrame.Distance(fill, expected) <= 6, $"{theme}: the file plate is {fill}, the source gives {expected}");
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)file.Left, (int)file.Top), backdrop) <= 6, $"{theme}: the file plate's corner is not clipped");
                    // The icon is drawn inside the plate at size x 0.08 padding (4.48): the margin of the plate is bare plate.
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)(file.Left + 2), (int)(file.Top + 28)), expected) <= 6);
                    Assert.NotNull(frame.Ink(new Rect(file.Left + 6, file.Top + 6, 44, 44), expected, 30));
                });
            }
        });
    });

    // ---- PR-CHAT-080: the pencil ----

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:130-161, :179-183: the pencil is on a reserved line that fades in on hover and never moves the message
    public void The_pencil_sits_in_the_reserved_gap_below_the_message_and_fades_in_on_hover_without_moving_any_text_and_only_a_revertable_message_has_one() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        var turn = Guid.NewGuid();
        UserMessageRow row = null!, locked = null!, noTurn = null!;
        AvaloniaWindow window = null!;
        double bubbleTop = 0, bubbleLeft = 0, rowHeight = 0;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("same words", turn), h.Runtime, canRevert: true);
            locked = new UserMessageRow(UserEntry("same words", Guid.NewGuid()), h.Runtime, canRevert: false);
            noTurn = new UserMessageRow(UserEntry("same words", null), h.Runtime, canRevert: true);
            var panel = new StackPanel { Spacing = 60 };
            foreach (var item in new Control[] { row, locked, noTurn }) panel.Children.Add(item);
            window = Show(panel);
            Assert.Empty(Mounted<Button>(HoverLine(locked))); // :131 not revertable: no pencil
            Assert.Empty(Mounted<Button>(HoverLine(noTurn))); // :131 no turn id: no pencil
            var line = HoverLine(row);
            var pencil = Assert.Single(Mounted<Button>(line));
            Assert.Equal("Edit message", ToolTip.GetTip(pencil)); // :142
            Assert.Equal(22, line.Height); // :160
            var frame = Mounted<Border>(pencil).First(border => border.Width == 22); // :137 22 by 22
            Assert.Equal(22, frame.Bounds.Width);
            Assert.Equal(22, frame.Bounds.Height);
            Assert.Equal(0, line.Opacity); // :161 hidden unless hovered or editing
            // :179-183 the room is inside the row and taken back out of its height: the gap below the bubble holds the line.
            Assert.Equal(Bubble(row).Bounds.Height, row.Bounds.Height, 0.5);
            Assert.Equal(6, Top(line, window) - (Top(Bubble(row), window) + Bubble(row).Bounds.Height), 0.5); // :88 the stack's spacing
            Assert.Equal(Left(Bubble(row), window) + Bubble(row).Bounds.Width, Left(line, window) + line.Bounds.Width, 0.5);
            bubbleTop = Top(Bubble(row), window); bubbleLeft = Left(Bubble(row), window); rowHeight = row.Bounds.Height;
            Hover(window, Assert.Single(Mounted<InlineText>(row)));
        });
        Frames(h.Main, clock, 60); // the fade is an ease-out of 0.12 s (the reply row's own hover language, OD-0076): part way at 60 ms
        h.Main.OnMain(() => Assert.InRange(HoverLine(row).Opacity, 0.01, 0.99));
        Frames(h.Main, clock, 120);
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Equal(1, HoverLine(row).Opacity);
            Assert.Equal(0, HoverLine(locked).Opacity);
            // Showing it moved nothing.
            Assert.Equal(bubbleTop, Top(Bubble(row), window), 0.01);
            Assert.Equal(bubbleLeft, Left(Bubble(row), window), 0.01);
            Assert.Equal(rowHeight, row.Bounds.Height, 0.01);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:132-134, :145-155, :87, :141: the pencil toggles the editor on the runtime, anchored to itself, and settling on it starts the preview
    public void Settling_on_the_pencil_starts_the_preview_a_click_opens_the_editor_on_the_runtime_anchored_to_the_pencil_and_a_second_click_closes_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var turn = Guid.NewGuid();
        var file = new Attachment { Name = "a.txt", Path = Path.Combine(h.Project.Path, "a.txt"), MimeType = "text/plain" };
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound); // the thread's history lands first (ThreadRuntime.swift:172-182)
        UserMessageRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("edit me", turn, new ValueList<Attachment>([file])), h.Runtime, canRevert: true);
            window = Show(row);
            Assert.Null(h.Runtime.CachedRevertPreview(turn));
            Hover(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Assert.NotNull(h.Runtime.CachedRevertPreview(turn)); // :145-147 the pointer settling on the pencil starts the revert preview
            Assert.Null(h.Runtime.MessageEdit);
            Click(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Settle(window);
            var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit); // :134 PromptEdit(id:, text:, attachments:)
            Assert.Equal(turn, edit.Id);
            Assert.Equal("edit me", edit.Text);
            Assert.Equal([file], edit.Attachments);
            var shown = Assert.Single(presenter.Calls, call => call.Presented);
            Assert.IsType<MessageEditor>(PopoverHost.Content(shown.Popup));
            var pencil = Assert.Single(Mounted<Button>(HoverLine(row)));
            Assert.Same(Mounted<AttachmentAnchorCapture>((Visual)pencil.Parent!).Single(), shown.Popup.PlacementTarget); // :148-155
            Assert.Equal(1, HoverLine(row).Opacity); // :161 editing keeps the line up
            Paints(Assert.Single(Mounted<SwiftImage>(pencil)).Foreground, ChromeTokens.Accent, pencil, "the pencil while editing (:141)");
            Click(window, pencil); // :134 a second click closes it
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Null(h.Runtime.MessageEdit);
            Assert.False(presenter.Calls.Last(call => call.Popup == presenter.Calls.First(item => item.Presented).Popup).Presented);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // TimelineRows.swift:84-87, :148-155, :197-198, :208-212; the persistence line: runtime.messageEdit, not row state
    public void An_edit_open_on_one_row_survives_the_row_being_taken_out_and_put_back_and_the_editor_reopens_on_it_as_it_was_left() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var turn = Guid.NewGuid();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        UserMessageRow row = null!;
        Border host = null!;
        AvaloniaWindow window = null!;
        PromptEdit edit = null!;
        Popup? Shown() => presenter.Calls.Where(call => PopoverHost.Content(call.Popup) is MessageEditor).Select(call => call.Popup).LastOrDefault() is { } popup
            && presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented ? popup : null;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("edit me", turn), h.Runtime, canRevert: true);
            host = new Border { Child = row };
            window = Show(host);
            Click(window, Assert.Single(Mounted<Button>(HoverLine(row))));
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Settle(window);
            edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
            Assert.NotNull(Shown());
            host.Child = null; // the row leaves (a thread left, a row scrolled out of a virtualised list): :198 onDisappear closes the popover only
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Null(Shown());
            Assert.Same(edit, h.Runtime.MessageEdit); // the edit is the thread's: it survives
            host.Child = row; // the row comes back: its anchor lands in its window and the editor opens on the edit as it was left
        });
        Frames(h.Main, clock, 200);
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.IsType<MessageEditor>(PopoverHost.Content(Assert.IsType<Popup>(Shown())));
            Assert.Same(edit, h.Runtime.MessageEdit);
            Close(window);
        });
    });

    // ---- PR-CHAT-082: the confirmation ----

    [Theory]
    [Trait("Req", "PR-CHAT-082")] // TimelineRows.swift:202-206: the awaited revert's error is discarded (try?); a revert that cannot start leaves the thread as it was
    [InlineData("Revert and keep file changes")]
    [InlineData("Revert files too")]
    public void A_revert_that_cannot_start_because_a_turn_is_running_raises_nothing_more_and_leaves_the_thread_as_it_was(string pressed) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new RecordingConfirmationPresenter();
        using var seam = AlertPresentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        AvaloniaWindow window = null!;
        int turns = 0, entries = 0;
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt"); // a turn is running (ThreadRuntime.swift:1838 refuses to revert)
            var entry = h.Runtime.Entries.First(item => item.Item.TimelineItemContent is TimelineItem.Content.User);
            var row = new UserMessageRow(entry, h.Runtime, true);
            window = Show(row);
            turns = h.Runtime.Turns.Count; entries = h.Runtime.Entries.Count;
            ((MessageMenuRequests)Field(row, "menuRequests")!).ConfirmRevert = true;
            Settle(window);
        });
        h.Main.Loop.RunUntil(() => { Dispatcher.UIThread.RunJobs(); return presenter.Shown.Count == 1; }, AppModelHarness.Bound);
        h.Main.OnMain(() => presenter.Press(pressed));
        h.Main.Loop.RunUntilIdle();
        h.Main.OnMain(() =>
        {
            Assert.Single(presenter.Shown); // no second dialog, no thrown error: the failure is discarded here
            Assert.Equal(turns, h.Runtime.Turns.Count);
            Assert.Equal(entries, h.Runtime.Entries.Count);
            Close(window);
        });
    });

    // ---- PR-CHAT-088: the reply's hover line ----

    [Fact]
    [Trait("Req", "PR-CHAT-088")] // TimelineRows.swift:1363-1388; OD-0098: the hover line laid out, shown and drawn
    public void The_worked_for_label_fades_in_on_the_leading_edge_of_the_gap_below_the_reply_without_moving_any_text_and_is_drawn_in_secondary_ink_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var clockSeam = FrameClock.Source.Override(clock);
        foreach (var (theme, backdrop) in Themes)
        {
            AssistantMessageRow row = null!;
            AvaloniaWindow window = null!;
            Control markdown = null!;
            double textTop = 0, textLeft = 0, rowHeight = 0;
            h.Main.OnMain(() =>
            {
                row = new AssistantMessageRow(Reply("The reply words."), Summary(Guid.NewGuid()), h.Runtime);
                var host = new Border { Padding = new Thickness(24, 20, 24, 40), Child = row };
                window = BlitzAbRender.Open(host, theme, backdrop, 640, 200);
                BlitzAbRender.Capture(window);
                markdown = Assert.Single(Mounted<MarkdownView>(row));
                var line = HoverLine(row);
                // The line is the 22-point room 2 points under the markdown (:1363, :1377), on the leading edge (:1373 the spacer follows the label).
                Assert.Equal(2, Top(line, window) - (Top(markdown, window) + markdown.Bounds.Height), 0.5);
                var label = Assert.Single(Mounted<TextBlock>(line));
                Assert.Equal(Left(row, window), Left(label, window), 0.5);
                OnOneCentreLine(window, label);
                Assert.Equal(Top(line, window) + 11, CentreY(label, window), 0.5); // the HStack's centre alignment in the 22-point line
                Assert.Equal(0, line.Opacity);
                // :1384-1388 the room is not in the row's height: the row ends where its text does.
                Assert.Equal(markdown.Bounds.Height, row.DesiredSize.Height, 0.5);
                textTop = Top(markdown, window); textLeft = Left(markdown, window); rowHeight = row.DesiredSize.Height;
                Hover(window, markdown);
            });
            Frames(h.Main, clock, 60);
            h.Main.OnMain(() => Assert.InRange(HoverLine(row).Opacity, 0.01, 0.99)); // :1381 easeOut(duration: 0.12): part way at 60 ms
            Frames(h.Main, clock, 120);
            h.Main.OnMain(() =>
            {
                try
                {
                    var frame = BlitzAbRender.Capture(window);
                    var label = Assert.Single(Mounted<TextBlock>(HoverLine(row)));
                    Assert.Equal(1, HoverLine(row).Opacity);
                    Assert.Equal(textTop, Top(markdown, window), 0.01);
                    Assert.Equal(textLeft, Left(markdown, window), 0.01);
                    Assert.Equal(rowHeight, row.DesiredSize.Height, 0.01);
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(label, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the Worked for label in {theme}", closest: 130);
                }
                finally { BlitzAbRender.Shut(window); }
            });
        }
    });
}
