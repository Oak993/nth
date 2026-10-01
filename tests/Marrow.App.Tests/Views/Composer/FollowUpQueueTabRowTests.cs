using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.FollowUpQueueTabRig;

namespace Marrow.App.Tests.Views.Composer;

// The rows of the queue tab: numbers, links, layout, labels, buttons, tooltips, the editor.
// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class FollowUpQueueTabRowTests(HeadlessSession headless)
{
    private static FollowUpPrompt[] Plain(params string[] texts) => texts.Select(text => Prompt(text)).ToArray();

    [Fact]
    [Trait("Req", "PR-COMPOSER-157")] // FollowUpQueueTab.swift:212-226,346-377
    [Trait("Req", "UI-COMPOSER-050")]
    public void A_bundle_shares_one_number_shown_on_its_first_row_in_the_accent_colour()
    {
        var b1 = Guid.NewGuid();
        var b2 = Guid.NewGuid();
        RunTab(headless,
        [
            Prompt("a"), Prompt("b", b1), Prompt("c", b1), Prompt("d"), Prompt("e", b2), Prompt("f", b2), Prompt("g", b2),
        ], run =>
        {
            var rows = run.Rows;
            Assert.Equal(7, rows.Count);

            // a=1, the bundle b+c takes 2 (shown on b), d=3, the bundle e+f+g takes 4 (shown on e).
            string?[] numbers = ["1", "2", null, "3", "4", null, null];
            for (var index = 0; index < rows.Count; index++)
            {
                var leading = Leading(rows[index]);
                if (numbers[index] is null)
                {
                    // The later rows of a bundle carry the unlink button in the number's place.
                    Assert.IsType<Button>(leading);
                    continue;
                }
                var number = Assert.IsType<TextBlock>(leading);
                Assert.Equal(numbers[index], number.Text);
                // The head of a pair: accent; everything else: the secondary colour.
                var head = index is 1 or 4;
                Assert.Equal(Effective((head ? Chrome.Accent : Chrome.SecondaryText).Brush(run.Tab)), Effective(number.Foreground));
                // :380 the grip follows: accent at 0.85 for the head of a pair, secondary at 0.7 otherwise.
                Assert.Equal(Effective((head ? Chrome.Accent.Opacity(0.85) : Chrome.SecondaryText.Opacity(0.7)).Brush(run.Tab)),
                    Effective(Grip(rows[index]).Foreground));
                // .subheadline at medium (11 point), monospaced digits, a 14 point trailing-aligned column, hidden from screen readers.
                Assert.Equal(11, number.FontSize);
                Assert.Equal(FontWeight.Medium, number.FontWeight);
                Assert.Contains(number.FontFeatures!, feature => feature.Tag == "tnum" && feature.Value == 1);
                Assert.Equal(14, number.Bounds.Width);
                Assert.Equal(TextAlignment.Right, number.TextAlignment);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(number));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-157")] // FollowUpQueueTab.swift:217-224 the hadPrevious guard: the first prompt always starts at 1
    public void The_first_prompt_starts_at_one_even_when_it_carries_a_bundle()
    {
        var bundle = Guid.NewGuid();
        RunTab(headless, [Prompt("a", bundle), Prompt("b", bundle), Prompt("c"), Prompt("d")], run =>
        {
            var rows = run.Rows;
            Assert.Equal("1", Assert.IsType<TextBlock>(Leading(rows[0])).Text);
            Assert.IsType<Button>(Leading(rows[1]));
            Assert.Equal("2", Assert.IsType<TextBlock>(Leading(rows[2])).Text);
            Assert.Equal("3", Assert.IsType<TextBlock>(Leading(rows[3])).Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-158")] // FollowUpQueueTab.swift:347-363
    [Trait("Req", "UI-COMPOSER-051")]
    public void A_row_joined_to_the_one_above_shows_an_unlink_glyph_that_unlinks_it_on_a_spring()
    {
        var bundle = Guid.NewGuid();
        RunTab(headless, [Prompt("a", bundle), Prompt("b", bundle), Prompt("c")], run =>
        {
            var rows = run.Rows;
            var unlink = Assert.IsType<Button>(Leading(rows[1]));
            Assert.IsType<TextBlock>(Leading(rows[0])); // the head of the pair has its number, not a link
            Assert.IsType<TextBlock>(Leading(rows[2]));

            Assert.Equal("Unlink from the follow-up above", ToolTip.GetTip(unlink));
            Assert.Equal("Unlink", AutomationProperties.GetName(unlink));
            Assert.False(unlink.Focusable);
            Assert.Equal(14, unlink.Bounds.Width);
            var glyph = Assert.IsType<SwiftImage>(unlink.Content);
            Assert.Equal("link", glyph.SymbolName);
            Assert.Equal(Effective(Chrome.Accent.Brush(run.Tab)), Effective(glyph.Foreground));
            var font = Assert.IsType<SwiftFont>(SwiftImage.GetFont(glyph));
            Assert.Equal(10, font.PointSize);
            Assert.Equal(FontWeight.SemiBold, font.Resolve().FontWeight);

            var seen = new List<Animation?>();
            Tracked<ValueList<FollowUpPrompt>>? probe = null;
            run.Step(() => probe = new Tracked<ValueList<FollowUpPrompt>>(() => run.Runtime.FollowUps, _ => seen.Add(Transaction.Current)));
            run.Click(unlink);

            // That prompt leaves the bundle (a lone head is released with it) and goes as its own message again.
            Assert.All(run.Runtime.FollowUps, prompt => Assert.Null(prompt.BundleID));
            Assert.Equal(["a", "b", "c"], Texts(run.Runtime));
            Assert.Equal(Animation.Spring(response: 0.3, dampingFraction: 0.8), seen.Last());
            Assert.All(run.Rows, row => Assert.IsType<TextBlock>(Leading(row)));
            run.Step(() => probe!.Dispose());
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-049")] // FollowUpQueueTab.swift:343,457-460,88-124
    [Trait("Req", "PR-COMPOSER-165")]
    public void A_one_line_row_is_32_tall_with_its_items_8_apart_on_one_centre_line_and_the_buttons_at_the_trailing_edge()
    {
        RunTab(headless, [Prompt("one"), Prompt("two", null, RealImage("a.png"), RealImage("b.png")), Prompt("three")], run =>
        {
            var rows = run.Rows;
            var window = run.Window;
            Assert.All(rows, row => Assert.Equal(32, row.Bounds.Height));

            foreach (var row in rows)
            {
                var line = RectIn(RowLine(row), window);
                var rowRect = RectIn(row, window);
                Assert.Equal(22, line.Height);
                Assert.Equal(5, line.Top - rowRect.Top); // .padding(.vertical, 5)
                Assert.Equal(5, rowRect.Bottom - line.Bottom);

                var leading = RectIn(Leading(row), window);
                var grip = RectIn(Grip(row), window);
                Assert.Equal(14, leading.Width);
                Assert.Equal(28, grip.Width);
                Assert.Equal(22, grip.Height);
                Assert.Equal(8, grip.Left - leading.Right);
                // The grip's whole 28 by 22 cell is the gesture's (:382 .contentShape(.rect)), with its help and label.
                var host = (Control)Grip(row).GetVisualParent()!;
                Assert.Equal(28, host.Bounds.Width);
                Assert.Equal(22, host.Bounds.Height);
                Assert.Equal("Drag above or below to reorder · drop onto the middle of another to send them together", ToolTip.GetTip(host));
                Assert.Equal("Drag to reorder", AutomationProperties.GetName(host));

                var buttons = Buttons(row).Select(button => RectIn(button, window)).ToList();
                Assert.Equal(3, buttons.Count);
                Assert.Equal(rowRect.Right, buttons.Last().Right, 0.5); // the buttons sit at the trailing edge
                var text = RectIn(ProseOf(row), window);
                Assert.True(text.Right <= buttons.First().Left, "the text ends before the buttons");

                // One centre line: the number, grip, buttons and the text's box all centre on it.
                var centre = line.Center.Y;
                Assert.Equal(centre, leading.Center.Y, 1.5);
                Assert.Equal(centre, grip.Center.Y, 0.5);
                Assert.All(buttons, button => Assert.Equal(centre, button.Center.Y, 0.5));
                Assert.Equal(centre, text.Center.Y, 1.5);
            }

            // No attachments: the text follows the grip by 8. With two: 22 point thumbnails 4 apart, 8 either side.
            Assert.Equal(8, RectIn(ProseOf(rows[0]), window).Left - RectIn(Grip(rows[0]), window).Right);
            var thumbnails = rows[1].GetVisualDescendants().OfType<Marrow.App.Views.Chat.AttachmentThumbnail>().ToList();
            Assert.Equal(2, thumbnails.Count);
            var first = RectIn(thumbnails[0], window);
            var second = RectIn(thumbnails[1], window);
            Assert.Equal(22, first.Width);
            Assert.Equal(22, first.Height);
            Assert.Equal(22, second.Width);
            Assert.Equal(4, second.Left - first.Right);
            Assert.Equal(8, first.Left - RectIn(Grip(rows[1]), window).Right);
            Assert.Equal(8, RectIn(ProseOf(rows[1]), window).Left - second.Right);
            Assert.Equal(RectIn(RowLine(rows[1]), window).Center.Y, first.Center.Y, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-049")] // FollowUpQueueTab.swift:88-124,457-460: the rule under each row, the stack's padding
    public void The_rule_under_a_row_is_off_for_the_last_row_and_a_row_whose_next_shares_its_bundle()
    {
        var bundle = Guid.NewGuid();
        RunTab(headless, [Prompt("a"), Prompt("b", bundle), Prompt("c", bundle), Prompt("d")], run =>
        {
            var rows = run.Rows;
            Assert.Equal([true, false, true, false], rows.Select(row => RuleOf(row).IsVisible).ToArray());
            var expected = Effective(Chrome.SecondaryText.Opacity(0.35).Brush(run.Tab));
            foreach (var row in rows.Where(row => RuleOf(row).IsVisible))
            {
                Assert.Equal(expected, Effective(RuleOf(row).Background));
                // Along the bottom edge, drawn over the row: it adds no height.
                var rule = RectIn(RuleOf(row), run.Window);
                Assert.Equal(RectIn(row, run.Window).Bottom, rule.Bottom, 0.5);
                Assert.Equal(32, row.Bounds.Height);
            }

            // Under the list's own 0.5 opacity rule: padded 6 at the top, 4 above the first row, rows touching.
            var topRule = run.Tab.GetVisualDescendants().OfType<Border>().First(border => border.Height == 1 && border.Opacity == 0.5);
            var list = run.Tab.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(6, RectIn(topRule, run.Window).Top - RectIn(list, run.Window).Top);
            Assert.Equal(4, RectIn(rows[0], run.Window).Top - RectIn(topRule, run.Window).Bottom);
            for (var index = 1; index < rows.Count; index++)
                Assert.Equal(RectIn(rows[index - 1], run.Window).Bottom, RectIn(rows[index], run.Window).Top, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-165")] // FollowUpQueueTab.swift:329-334,408-429
    [Trait("Req", "UI-COMPOSER-049")]
    public void The_prose_is_lifted_by_half_the_gap_between_cap_height_and_x_height_and_stops_at_two_lines()
    {
        var longText = string.Join(" ", Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 12));
        RunTab(headless, [Prompt("short"), Prompt(longText), Prompt("   ", null, RealImage("c.png"))], run =>
        {
            var rows = run.Rows;
            var oneLine = ProseOf(rows[0]);
            var many = ProseOf(rows[1]);

            // .callout: 12 point, primary at 0.9.
            Assert.Equal(12, oneLine.FontSize);
            Assert.Equal(Effective(Chrome.PrimaryText.Opacity(0.9).Brush(run.Tab)), Effective(oneLine.Foreground));

            // Lifted: Inter's cap height 1490 and x-height 1118 of 2048 units make 12 * (1490 - 1118) / 2048 = 2.18 points,
            // which rounds to 2, and half of that is 1: the prose sits one point above the centre line (up is negative).
            var lift = Assert.IsType<TranslateTransform>(oneLine.RenderTransform);
            Assert.Equal(0, lift.X);
            Assert.Equal(-1, lift.Y);

            // Two lines, tail truncated: the long prompt is two lines tall and no more, with the row growing to fit them.
            Assert.Equal(TextTrimming.CharacterEllipsis, many.TextTrimming);
            Assert.Equal(TextWrapping.Wrap, many.TextWrapping);
            Assert.Equal(2, many.Bounds.Height / oneLine.Bounds.Height, 0.1);
            Assert.Equal(many.Bounds.Height + 10, rows[1].Bounds.Height, 0.5);
            Assert.True(rows[1].Bounds.Height > 32);
        });
    }

    [Theory]
    [InlineData("1 image", 1, 0)]
    [InlineData("2 images", 2, 0)]
    [InlineData("1 file", 0, 1)]
    [InlineData("3 files", 0, 3)]
    [InlineData("1 image, 1 file", 1, 1)]
    [InlineData("2 images, 1 file", 2, 1)]
    [InlineData("1 image, 2 files", 1, 2)]
    [InlineData("Empty follow-up", 0, 0)]
    [Trait("Req", "PR-COMPOSER-166")] // FollowUpQueueTab.swift:408-415,501-509
    public void A_follow_up_with_no_text_names_its_attachments_images_first_or_reads_Empty_follow_up(string expected, int images, int files)
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = Make();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                // Files first in the list, to show the label does not follow the list's order.
                var attachments = Enumerable.Range(0, files).Select(i => File($"f{i}.txt"))
                    .Concat(Enumerable.Range(0, images).Select(i => Image($"i{i}.png"))).ToArray();
                var row = RowOf(Prompt(" \n\t ", null, attachments), runtime);
                var window = Show(row, 600, 80);

                var label = ProseOf(row);
                Assert.Equal(expected, label.Text);
                // In the secondary colour, not the prompt text's primary 0.9.
                Assert.Equal(Effective(Chrome.SecondaryText.Brush(row)), Effective(label.Foreground));
                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-166")] // FollowUpQueueTab.swift:409-424 text that is not blank is shown as it is
    public void A_follow_up_with_text_shows_the_text_and_not_the_attachment_summary()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = Make();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var row = RowOf(Prompt("fix the build", null, Image("a.png")), runtime);
                var window = Show(row, 600, 80);
                Assert.Equal("fix the build", ProseOf(row).Text);
                Assert.Equal(Effective(Chrome.PrimaryText.Opacity(0.9).Brush(row)), Effective(ProseOf(row).Foreground));
                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-167")] // FollowUpQueueTab.swift:431-453,512-532
    public void Each_row_ends_with_send_edit_and_delete_buttons_22_square_with_no_gap_in_the_secondary_colour()
    {
        RunTab(headless, [Prompt("one")], run =>
        {
            var row = run.Rows.Single();
            var buttons = Buttons(row);
            Assert.Equal(3, buttons.Count);
            string[] symbols = ["paperplane", "pencil", "trash"];
            string?[] names = [null, "Edit follow-up", "Delete follow-up"];

            for (var index = 0; index < 3; index++)
            {
                var button = Assert.IsType<Button>(buttons[index].Child);
                var icon = Assert.IsType<SwiftImage>(button.Content);
                Assert.Equal(symbols[index], icon.SymbolName);
                Assert.Equal(22, buttons[index].Bounds.Width);
                Assert.Equal(22, buttons[index].Bounds.Height);
                Assert.Equal(22 * index, buttons[index].Bounds.X, 0.5); // no spacing between them
                Assert.Equal(Effective(Chrome.SecondaryText.Brush(row)), Effective(icon.Foreground));
                Assert.Equal(11, SwiftImage.GetFont(icon)!.PointSize);
                Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(icon)!.Resolve().FontWeight);
                if (names[index] is { } name)
                {
                    Assert.Equal(name, ToolTip.GetTip(button));
                    Assert.Equal(name, AutomationProperties.GetName(button));
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-167")] // FollowUpQueueTab.swift:450-452 the trash removes this follow-up
    public void The_trash_button_deletes_its_own_follow_up()
    {
        RunTab(headless, Plain("one", "two", "three"), run =>
        {
            var second = run.Rows[1];
            run.Click(Buttons(second)[2]);
            Assert.Equal(["one", "three"], Texts(run.Runtime));
            run.Settle();
            Assert.Equal(2, run.Rows.Count);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-168")] // FollowUpQueueTab.swift:434-439
    public void The_send_tooltip_says_the_running_turn_is_stopped_when_there_are_no_native_heads()
    {
        RunTab(headless, Plain("one"), run =>
        {
            var send = Buttons(run.Rows.Single())[0];
            var button = (Button)send.Child!;
            Assert.False(run.Runtime.HasWorkingNativeHeads);
            Assert.Equal("Send now", ToolTip.GetTip(button));

            run.Step(() => SetPhase(run.Runtime, RuntimePhase.Running));
            run.Settle();
            Assert.Equal("Send now (stops the running turn)", ToolTip.GetTip(button));
            Assert.Equal("Send now (stops the running turn)", AutomationProperties.GetName(button));

            // And back: the tooltip follows the turn.
            run.Step(() => SetPhase(run.Runtime, RuntimePhase.Idle));
            run.Settle();
            Assert.Equal("Send now", ToolTip.GetTip(button));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-168")] // FollowUpQueueTab.swift:434-436 native heads at work during a running turn
    public void The_send_tooltip_says_it_waits_for_the_heads_only_while_native_heads_work_during_a_running_turn()
    {
        RunTab(headless, Plain("one"), run =>
        {
            Assert.True(run.Runtime.HasWorkingNativeHeads);
            var button = (Button)Buttons(run.Rows.Single())[0].Child!;

            // Heads at work but the lead idle: the plain label.
            Assert.Equal("Send now", ToolTip.GetTip(button));

            run.Step(() => SetPhase(run.Runtime, RuntimePhase.Running));
            run.Settle();
            Assert.Equal("Send as soon as the heads report", ToolTip.GetTip(button));
            Assert.Equal("Send as soon as the heads report", AutomationProperties.GetName(button));

            run.Step(() => SetPhase(run.Runtime, RuntimePhase.Idle));
            run.Settle();
            Assert.Equal("Send now", ToolTip.GetTip(button));
        }, nativeHead: true);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-168")] // FollowUpQueueTab.swift:437-439 the paper plane sends this follow-up now
    public void The_paper_plane_sends_its_own_follow_up_now_which_leaves_the_queue()
    {
        RunTab(headless, [Prompt("one"), Prompt("/plan"), Prompt("three")], run =>
        {
            // "/plan" is a local command: sent while idle it flips the thread's mode and starts no turn.
            var before = run.App.Thread(run.Runtime.ThreadID)!.InteractionMode;
            run.Click(Buttons(run.Rows[1])[0]);
            Assert.Equal(["one", "three"], Texts(run.Runtime));
            Assert.NotEqual(before, run.App.Thread(run.Runtime.ThreadID)!.InteractionMode);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-173")] // FollowUpQueueTab.swift:440-449,495-499
    public void The_pencil_toggles_an_editor_popover_anchored_by_rect_that_carries_the_prompt()
    {
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        var image = Image("a.png");
        RunTab(headless, [Prompt("one"), Prompt("edit me", null, image)], run =>
        {
            var pencil = Buttons(run.Rows[1])[1];
            var prompt = run.Runtime.FollowUps[1];
            Assert.Null(run.Runtime.FollowUpEdit);

            run.Click(pencil);
            run.Settle();

            // The edit belongs to the thread and carries the prompt's text and attachments.
            var edit = run.Runtime.FollowUpEdit!;
            Assert.Equal(prompt.Id, edit.Id);
            Assert.Equal("edit me", edit.Text);
            Assert.Equal([image], edit.Attachments.ToArray());

            // The popover hangs from the pencil's rect on the window, not from a view mounted in the tab.
            var (popup, shown) = recorder.Calls.Last();
            Assert.True(shown);
            Assert.Same(run.Window, popup.PlacementTarget);
            var rect = RectIn(pencil, run.Window);
            Assert.Equal(rect.X, popup.PlacementRect!.Value.X, 1);
            Assert.Equal(rect.Y, popup.PlacementRect!.Value.Y, 1);
            Assert.Equal(rect.Width, popup.PlacementRect!.Value.Width, 1);
            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<FollowUpEditor>(popup.Child!));

            // A second tap closes it.
            run.Click(pencil);
            run.Settle();
            Assert.Null(run.Runtime.FollowUpEdit);
            Assert.False(recorder.Calls.Last().Presented);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-173")] // FollowUpQueueTab.swift:337-340,489-492 leaving the thread closes only the popover
    public void Leaving_the_thread_closes_only_the_popover_and_coming_back_reopens_it_on_the_edit_as_it_was_left()
    {
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        RunTab(headless, Plain("one", "two"), run =>
        {
            var pencil = Buttons(run.Rows[0])[1];
            run.Click(pencil);
            run.Settle();
            var edit = run.Runtime.FollowUpEdit!;
            Assert.True(recorder.Calls.Last().Presented);
            run.Step(() => edit.Text = "half typed");

            // The tab leaves the screen: the popover closes, the edit stays on the thread.
            run.Step(() =>
            {
                run.Window.Content = null;
                Settle(run.Window);
            });
            Assert.False(recorder.Calls.Last().Presented);
            Assert.Same(edit, run.Runtime.FollowUpEdit);

            // The reader clicks elsewhere in the window (the sidebar, to switch threads): the window is the current event's.
            run.Click(new Point(5, 495));

            // The tab comes back: the popover reopens on the same edit.
            var shownBefore = recorder.Calls.Count(call => call.Presented);
            run.Step(() =>
            {
                run.Tab = new FollowUpQueueTab(run.Runtime);
                run.Window.Content = run.Tab;
                Settle(run.Window);
            });
            run.Settle();
            run.Settle();
            Assert.Equal(shownBefore + 1, recorder.Calls.Count(call => call.Presented));
            Assert.True(recorder.Calls.Last().Presented);
            Assert.Same(edit, run.Runtime.FollowUpEdit);
            Assert.Equal("half typed", run.Runtime.FollowUpEdit!.Text);
            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<FollowUpEditor>(recorder.Calls.Last().Popup.Child!));
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-053")] // FollowUpQueueTab.swift:396-407
    public void The_thumbnails_share_one_preview_panel_anchored_by_the_strips_window_rect_and_mount_no_anchor_view()
    {
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        RunTab(headless, [Prompt("pictures", null, RealImage("p1.png"), RealImage("p2.png"), RealImage("p3.png"))], run =>
        {
            var row = run.Rows.Single();
            var thumbnails = row.GetVisualDescendants().OfType<Marrow.App.Views.Chat.AttachmentThumbnail>().ToList();
            Assert.Equal(3, thumbnails.Count);
            var strip = thumbnails[0].GetVisualParent()!;
            Assert.Equal(4, RectIn(thumbnails[1], run.Window).Left - RectIn(thumbnails[0], run.Window).Right);

            // Hovering a thumbnail mounts no anchor view under it (anchorless).
            run.Step(() => run.Window.MouseMove(Center(thumbnails[0], run.Window), RawInputModifiers.None));
            run.Settle();
            Assert.Empty(row.GetVisualDescendants().OfType<AttachmentAnchorCapture>());

            // A click opens the panel; a click on another thumbnail drives the very same panel.
            run.Click(thumbnails[0]);
            var (first, firstShown) = recorder.Calls.Last();
            Assert.True(firstShown);
            Assert.Same(run.Window, first.PlacementTarget);
            var stripRect = RectIn((Visual)strip, run.Window);
            Assert.InRange(first.PlacementRect!.Value.Center.X, stripRect.Left, stripRect.Right);
            Assert.Equal(stripRect.Top, first.PlacementRect!.Value.Top, 1);
            Assert.Equal(stripRect.Height, first.PlacementRect!.Value.Height, 1);

            run.Click(thumbnails[2]);
            var (second, secondShown) = recorder.Calls.Last();
            Assert.True(secondShown);
            Assert.Same(first, second);
        });
    }
}
