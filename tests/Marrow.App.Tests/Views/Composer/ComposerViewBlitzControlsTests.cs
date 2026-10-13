using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzListTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzRingTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzUsageTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :487-501, :552-565, :599-604, :625-656, :868-959 (the
// blitz-composerview unit): the pick's one undo step, the text view's growth and its live-resize rule, the attach button, the
// control row and the chip row.
[Collection("Headless")]
public sealed class ComposerViewBlitzControlsTests(HeadlessSession headless)
{
    internal static IEnumerable<Control> ChipControls(DraftChips chips) =>
        Assert.IsType<StackPanel>(One<ScrollViewer>(chips).Content).Children.OfType<Control>();

    internal static Panel ChipRows(DraftChips chips) => Assert.IsAssignableFrom<Panel>(chips.Child);

    // ---- PR-COMPOSER-049 (:487-501; ComposerTextView.swift:43-49) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerView.swift:487-490, :499-500; ComposerTextView.swift:43-49
    [Trait("Rule", "R-TEST-4")] // The pick's result and the caret: Picking_a_file_replaces_the_typed_at_word_...; the chip: the next two tests
    public void Picking_a_file_is_one_edit_for_the_undo_stack_that_brings_the_typed_word_back()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("undo-file");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var runtime = Unresolved(h);
            var (view, window, _, _) = OnScreen(h, runtime, directory);
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            // The words as a user types them, so the box's undo history holds them.
            h.Main.OnMain(() =>
            {
                box.Focus();
                window.KeyTextInput("see @mai and");
                Drain();
                box.SelectionStart = box.SelectionEnd = 8;
            });
            Until(h, window, () => view.Suggestions.IsVisible && view.Suggestions.Range == (4, 4));
            Assert.True(h.Main.OnMain(() => Press(view, Key.Tab)));
            Until(h, window, () => runtime.Draft.Text == "see @main.swift  and");
            Assert.True(h.Main.OnMain(() => box.CanUndo));
            h.Main.OnMain(() =>
            {
                box.Undo();
                Drain();
            });
            Until(h, window, () => runtime.Draft.Text == "see @mai and"); // one step: the pick is undone whole
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerView.swift:487-497; ComposerTextView.swift:43-49
    [Trait("Rule", "R-TEST-4")] // The command's removal of the typed run and its chip: Picking_a_command_deletes_the_typed_slash_word_...
    public void Picking_a_command_is_one_edit_for_the_undo_stack_that_brings_the_typed_word_back_and_the_chip_stays()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var resolved = h.Runtime; // a resolved thread offers the commands
            var (pill, pillWindow, _, _) = OnScreen(h, resolved, workingDirectory: null);
            var textBox = h.Main.OnMain(() => One<ComposerNSTextView>(pill));
            h.Main.OnMain(() =>
            {
                textBox.Focus();
                pillWindow.KeyTextInput("/pla");
                Drain();
            });
            UntilBecause("the list opened", h, pillWindow, () => pill.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(pill, Key.Enter)));
            UntilBecause("the pick left a chip and no text", h, pillWindow, () => resolved.Draft.Text.Length == 0 && resolved.Draft.Command is not null);
            Assert.True(h.Main.OnMain(() => textBox.CanUndo));
            h.Main.OnMain(() =>
            {
                textBox.Undo();
                Drain();
            });
            UntilBecause("the undo brought the word back", h, pillWindow, () => resolved.Draft.Text == "/pla");
            Assert.Equal("plan", h.Main.OnMain(() => resolved.Draft.Command!.Name)); // undo is the text's: the chip stays until its own x
            Pump(h, pillWindow);
            Close(h, pillWindow);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerTextView.swift:43-49 guard NSMaxRange(range) <= storage.length
    [Trait("Rule", "R-TEST-4")] // The picks: the tests above
    public void A_range_that_no_longer_fits_the_text_replaces_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var (view, window, _, _) = OnScreen(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "abc"));
            Until(h, window, () => runtime.Draft.Text == "abc");
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            h.Main.OnMain(() =>
            {
                view.Controller.ReplaceCharacters(2, 5, "x"); // runs past the end
                view.Controller.ReplaceCharacters(-1, 1, "x"); // starts before it
                Drain();
            });
            Pump(h, window);
            Assert.Equal("abc", box.Text);
            Assert.Equal("abc", h.Main.OnMain(() => runtime.Draft.Text));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerView.swift:493-495 withAnimation(Chrome.panelSlide) { draft.command = ... }
    [Trait("Rule", "R-TEST-4")] // The picks and the undo step: the tests above
    public void The_picked_commands_chip_arrives_under_panelSlide()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var runtime = h.Runtime;
            var (view, window, _, _) = OnScreen(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/pla"));
            Until(h, window, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Pump(h, window);
            var chips = h.Main.OnMain(() => One<DraftChips>(view));
            var arrival = h.Main.OnMain(() => ChipRows(chips).Children.Single());
            Assert.Equal(0, h.Main.OnMain(() => arrival.Opacity)); // the row grows from nothing and fades in
            Assert.Equal(0, h.Main.OnMain(() => arrival.Height));
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            var progress = Chrome.PanelSlide.Progress(0.03);
            Assert.InRange(progress, 0.01, 0.99);
            Assert.Equal(progress, h.Main.OnMain(() => arrival.Opacity), 3);
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, h.Main.OnMain(() => arrival.Opacity));
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-088 (:880-959) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-088")] // App/Views/Composer/ComposerView.swift:879-881, :939-941, :950-959
    [Trait("Rule", "R-TEST-4")] // The row's bottom clip and the 6 point gap: B3_chip_row_scrolls_without_indicators_and_collapse_clips_from_the_top; capture comparison mid-collapse: CAPTURE-ONLY
    public void The_first_chip_grows_the_row_from_nothing_with_a_fade_and_the_last_one_to_leave_collapses_it_back_to_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 400));
            Pump(h, window);
            Assert.Equal(0, h.Main.OnMain(() => chips.Bounds.Height)); // nothing to show takes no room
            var quote = new ReplyQuote { Text = "a quoted line" };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([quote]) });
            Pump(h, window);
            var row = h.Main.OnMain(() => Assert.IsType<Border>(ChipRows(chips).Children.Single()));
            Assert.True(h.Main.OnMain(() => row.ClipToBounds)); // the collapse clips
            Assert.Equal(VerticalAlignment.Top, h.Main.OnMain(() => ((Control)row.Child!).VerticalAlignment)); // from the top: clipped from the bottom
            Assert.Equal(0, h.Main.OnMain(() => row.Opacity));
            Assert.Equal(0, h.Main.OnMain(() => row.Height));
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            var full = h.Main.OnMain(() => row.Bounds.Height);
            Assert.True(full > 8);
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity));

            // The same row shrinks back, and its first half is the curve's.
            h.Main.OnMain(() => Click(Named(chips, "Remove quote")));
            Pump(h, window);
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity)); // still full at the first frame
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            var progress = Chrome.PanelSlide.Progress(0.03);
            Assert.InRange(progress, 0.01, 0.99);
            Assert.Equal(1 - progress, h.Main.OnMain(() => row.Opacity), 3);
            Assert.Equal(full * (1 - progress), h.Main.OnMain(() => row.Height), 1);
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            Assert.Empty(h.Main.OnMain(() => ChipRows(chips).Children.ToArray())); // and is gone
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-088")] // App/Views/Composer/ComposerView.swift:880-881, :941
    [Trait("Rule", "R-TEST-4")] // The row's arrival and departure: the test above; the survivors' glide on removal is the foundation's ForEach (ED-218) and is not a clause of this record
    public void A_chip_joining_a_row_that_stays_fades_in_alone_and_the_row_the_scroller_and_the_chips_before_it_do_not_flicker()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var first = new ReplyQuote { Text = "first quote" };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([first]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 400));
            Pump(h, window);
            var (scroller, row, firstChip) = h.Main.OnMain(() => (One<ScrollViewer>(chips), ChipRows(chips).Children.Single(), ChipControls(chips).Single()));
            var before = h.Main.OnMain(() => Frame(firstChip, scroller));
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity));
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));

            var second = new ReplyQuote { Text = "second quote" };
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Quotes = new ValueList<ReplyQuote>([first, second]) });
            Pump(h, window);
            var added = h.Main.OnMain(() => ChipControls(chips).Last());
            h.Main.OnMain(() =>
            {
                Assert.Same(scroller, One<ScrollViewer>(chips)); // the same scroller: a kept offset, no new row
                Assert.Same(row, ChipRows(chips).Children.Single());
                Assert.Same(firstChip, ChipControls(chips).First());
                Assert.Equal(1, row.Opacity);
                Assert.Equal(1, firstChip.Opacity);
                Assert.Equal(0, added.Opacity); // the arrival alone is from nothing
            });
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            var progress = Chrome.PanelSlide.Progress(0.03);
            Assert.InRange(progress, 0.01, 0.99);
            h.Main.OnMain(() =>
            {
                Assert.Equal(progress, added.Opacity, 3);
                Assert.Equal(1, row.Opacity);
                Assert.Equal(1, firstChip.Opacity);
                Assert.Equal(before, Frame(firstChip, scroller)); // the first chip stays where it was
            });
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, h.Main.OnMain(() => added.Opacity));
            Assert.Equal(2, h.Main.OnMain(() => ChipControls(chips).Count()));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-088")] // App/Views/Composer/ComposerView.swift:941 .animation(Chrome.panelSlide, value: ChipKey(quoteIDs:commandName:))
    [Trait("Rule", "R-TEST-4")] // The arrivals: the tests above
    public void A_chip_leaving_fades_out_while_the_scroller_keeps_its_offset_and_a_text_edit_asks_for_no_frame()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var quotes = Enumerable.Range(1, 3).Select(n => new ReplyQuote { Text = new string((char)('a' + n), 240) }).ToArray();
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "t", Quotes = new ValueList<ReplyQuote>(quotes) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 400));
            Pump(h, window);
            var scroller = h.Main.OnMain(() => One<ScrollViewer>(chips));
            h.Main.OnMain(() => scroller.Offset = new Vector(60, 0));
            Pump(h, window);
            Assert.Equal(60, h.Main.OnMain(() => scroller.Offset.X));
            var kept = h.Main.OnMain(() => ChipControls(chips).ToArray());
            Assert.Equal(3, kept.Length);

            // The draft's text changes: the key is the ids and the command's name alone, so nothing moves and no frame is asked for.
            var pending = frames.Pending;
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Text = "t and more" });
            Pump(h, window);
            Assert.Equal(pending, frames.Pending);
            Assert.Equal(kept, h.Main.OnMain(() => ChipControls(chips).ToArray()));

            // The first chip leaves: it fades where it stands; the others and the scroll are the same ones.
            h.Main.OnMain(() => Click(Named(kept[0], "Remove quote")));
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(0.03));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.03)));
            var progress = Chrome.PanelSlide.Progress(0.03);
            Assert.InRange(progress, 0.01, 0.99);
            h.Main.OnMain(() =>
            {
                Assert.Same(scroller, One<ScrollViewer>(chips));
                Assert.Equal(1 - progress, kept[0].Opacity, 3);
                Assert.Equal(1, kept[1].Opacity);
                Assert.Equal(1, kept[2].Opacity);
                Assert.Equal(1, ChipRows(chips).Children.Single().Opacity); // the row itself stays whole
                Assert.Equal(60, scroller.Offset.X);
            });
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal([kept[1], kept[2]], ChipControls(chips).ToArray());
                Assert.Same(scroller, One<ScrollViewer>(chips));
            });
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-015 (:552-565, :599-602) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:552-565, :599-602; ComposerTextView.swift:360-376
    [Trait("Rule", "R-TEST-4")] // The scroll past the ceiling: Past_five_lines_the_text_view_scrolls_inside_the_fixed_height
    public void Five_lines_fill_the_ceiling_exactly_more_hold_it_and_each_line_before_it_adds_one_line_height()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var column = h.Main.OnMain(() => Column(h.Runtime, notice: null));
            var window = h.Main.OnMain(() => Show(column, h.App, width: 400));
            Pump(h, window);
            var heights = new List<double>();
            foreach (var lines in new[] { 1, 2, 3, 4, 5, 6, 8, 30 })
            {
                h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = string.Join("\n", Enumerable.Range(1, lines).Select(n => "line " + n.ToString(System.Globalization.CultureInfo.InvariantCulture))) });
                Pump(h, window);
                var (clip, inner) = h.Main.OnMain(() => (((Border)One<ComposerTextView>(column).Parent!).Height, One<ComposerTextView>(column).Height));
                Assert.Equal(clip, inner); // the text view always takes the clip's height
                heights.Add(clip);
            }
            var ceiling = FiveLineCeiling();
            Assert.Equal(ceiling, heights[4]); // five lines of the 14 point face plus the 4 point inset, to the point
            Assert.All(heights.Skip(4), height => Assert.Equal(ceiling, height));
            var line = (ceiling - 4) / 5;
            for (var index = 1; index < 5; index++) Assert.InRange(heights[index] - heights[index - 1], line - 1, line + 1);
            Assert.True(heights[0] >= 20);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerTextView.swift:361 guard textView.bounds.width > 24 else { return }
    [Trait("Rule", "R-TEST-4")] // The growth itself: the test above
    public void A_text_view_narrower_than_24_points_reports_no_height_so_the_pill_stays_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var column = h.Main.OnMain(() => Column(h.Runtime, notice: null));
            var window = h.Main.OnMain(() => Show(column, h.App, width: 20));
            Pump(h, window);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "one\ntwo\nthree\nfour" });
            Pump(h, window);
            Assert.Equal(20, h.Main.OnMain(() => ((Border)One<ComposerTextView>(column).Parent!).Height));
            Assert.Equal(20, h.Main.OnMain(() => One<ComposerTextView>(column).Height));
            Close(h, window);
        });
    }

    // ED-NEW-perf-motion-11 (the lead's ruling on finding 5): .smooth(duration: 0.28) is a spring, which keeps its velocity through a
    // retarget. A line typed while the clip glides sets the new glide out at the speed the clip had; before, it set out from rest.
    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void A_line_typed_while_the_clip_glides_carries_the_glide_on_at_its_speed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var column = h.Main.OnMain(() => Column(h.Runtime, null));
            var window = h.Main.OnMain(() => Show(column, h.App, 400));
            var resize = h.Main.OnMain(() => new WindowLiveResize(window));
            h.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(column, resize));
            Pump(h, window);
            var text = One<ComposerTextView>(column);
            var clip = (Border)text.Parent!;
            string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(n => "line " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            const double frame = 1 / 60.0;
            void Step()
            {
                h.Main.Advance(TimeSpan.FromSeconds(frame));
                h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(frame)));
            }
            var smooth = Animation.Smooth(0.28);
            var first = h.Main.OnMain(() => clip.Height);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(2) });
            Pump(h, window);
            var goal = h.Main.OnMain(() => text.Height);
            Assert.True(goal > first + 5, $"two lines are taller ({first} to {goal})");
            for (var i = 0; i < 4; i++) Step();
            var reached = h.Main.OnMain(() => clip.Height);
            Assert.Equal(first + (goal - first) * smooth.Progress(4 * frame), reached, 3);
            var speed = (goal - first) * smooth.Speed(4 * frame);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(4) });
            Pump(h, window);
            var next = h.Main.OnMain(() => text.Height);
            Assert.Equal(reached, h.Main.OnMain(() => clip.Height), 6); // no jump at the new line
            Step();
            // From the height reached, along the curve, plus the speed it had dying out by the curve's end.
            var carried = reached + (next - reached) * smooth.Progress(frame) + speed * smooth.Impulse(frame);
            var fromRest = reached + (next - reached) * smooth.Progress(frame);
            Assert.True(carried - fromRest > 0.5, $"the speed carried counts here ({carried} against {fromRest})");
            Assert.Equal(carried, h.Main.OnMain(() => clip.Height), 3);
            for (var i = 0; i < 60; i++) Step();
            Assert.Equal(next, h.Main.OnMain(() => clip.Height), 6);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-016 (:599-604) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-016")] // App/Views/Composer/ComposerView.swift:604; App/Window/WindowManager.swift:119-139
    [Trait("Rule", "R-TEST-4")] // A pane drag: B3_clip_grows_on_smooth_028_while_text_is_immediate_and_live_resize_is_immediate; a real window drag reaching the hook: NATIVE-ONLY
    public void A_window_size_drag_makes_the_clip_follow_the_rewrap_at_once_and_a_move_drag_or_the_drag_ending_leaves_the_smooth_curve()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var column = h.Main.OnMain(() => Column(h.Runtime, null));
            var window = h.Main.OnMain(() => Show(column, h.App, 400));
            var resize = h.Main.OnMain(() => new WindowLiveResize(window));
            h.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(column, resize));
            Pump(h, window);
            var text = One<ComposerTextView>(column);
            var clip = (Border)text.Parent!;
            void Message(uint message)
            {
                var handled = false;
                h.Main.OnMain(() => resize.WndProc(0, message, 0, 0, ref handled));
            }
            string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(n => "line " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            const uint enter = 0x0231, sizing = 0x0214, exit = 0x0232;

            // The window's move drag is not a live resize: the growth is smooth.
            Message(enter);
            Assert.False(h.Main.OnMain(() => resize.IsReshaping));
            var before = h.Main.OnMain(() => clip.Height);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(3) });
            Pump(h, window);
            Assert.Equal(before, h.Main.OnMain(() => clip.Height));
            Assert.True(h.Main.OnMain(() => text.Height) > before);
            h.Main.Advance(TimeSpan.FromSeconds(0.14));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.14)));
            Assert.Equal(before + (text.Height - before) * Animation.Smooth(0.28).Progress(0.14), h.Main.OnMain(() => clip.Height), 5);
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            Message(exit);

            // The size drag: the first WM_SIZING makes it live, and the clip takes the rewrapped height in the same turn.
            Message(enter);
            Message(sizing);
            Assert.True(h.Main.OnMain(() => resize.IsReshaping));
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(5) });
            Pump(h, window);
            Assert.Equal(h.Main.OnMain(() => text.Height), h.Main.OnMain(() => clip.Height));
            Assert.Equal(FiveLineCeiling(), h.Main.OnMain(() => clip.Height));
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(2) });
            Pump(h, window);
            Assert.Equal(h.Main.OnMain(() => text.Height), h.Main.OnMain(() => clip.Height)); // shrinking follows as well

            // The drag ends: the next change is smooth again.
            Message(exit);
            Assert.False(h.Main.OnMain(() => resize.IsReshaping));
            var settled = h.Main.OnMain(() => clip.Height);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = Lines(4) });
            Pump(h, window);
            Assert.Equal(settled, h.Main.OnMain(() => clip.Height));
            Assert.True(h.Main.OnMain(() => text.Height) > settled);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-077 (:627-646) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-077")] // App/Views/Composer/ComposerView.swift:627-646; App/AppSettings.swift:148, :736
    [Trait("Rule", "R-TEST-4")] // Where the popover opens against a window's foot (the platform's flip) is NATIVE-ONLY; the setting's persistence: AppSettingsTests, SettingsStoreTests
    public void The_attach_button_opens_the_downloads_popover_below_it_with_the_chip_active_and_the_file_dialog_when_the_setting_is_off_live()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var dialog = new ComposerDialogProbe { Result = null };
            using var dialogScope = ComposerFileDialog.Current.Override(dialog);
            var downloads = h.Store.NewFolder("downloads-077");
            File.WriteAllText(Path.Combine(downloads, "report.txt"), "report");
            using var folder = RecentDownloads.DirectoryProvider.Override(() => downloads);
            Assert.True(h.App.Settings.RecentDownloadsPicker); // the default
            var runtime = h.Runtime;
            var (view, window, _, _) = OnScreen(h, runtime, workingDirectory: null);
            Button Attach() => Named(view, "Attach files");
            static Color Plate(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Border>(button.Content).Background).Color;

            // On: the click presents the popover below the button, and the chip reads as active while it is open.
            h.Main.OnMain(() => Click(Attach()));
            Pump(h, window);
            var (popup, presented) = presenter.Calls[^1];
            Assert.True(presented);
            Assert.Equal(PlacementMode.Bottom, popup.Placement);
            Assert.Same(h.Main.OnMain(Attach), popup.PlacementTarget);
            var content = Assert.IsType<DownloadsPopover>(PopoverHost.Content(popup));
            Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(h.Main.OnMain(() => Attach().ActualThemeVariant)), h.Main.OnMain(() => Plate(Attach())));
            Assert.Null(dialog.Options);

            // A pick from it attaches that file and closes it.
            h.Main.OnMain(() =>
            {
                if (popup.Child is PopoverCard card) card.Child = null;
                popup.Child = null;
            });
            var pickWindow = h.Main.OnMain(() => Show(content, h.App, 400));
            Until(h, pickWindow, () => LogicalQuery.SelfAndDescendants<Button>(content).Any(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "report.txt")));
            h.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(content).First(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "report.txt"))));
            Until(h, window, () => runtime.Draft.Attachments.Count == 1 && !presenter.Calls[^1].Presented);
            Assert.Equal("report.txt", h.Main.OnMain(() => runtime.Draft.Attachments[0].Name));
            Assert.Equal(Chrome.PrimaryText.Opacity(0).Resolve(h.Main.OnMain(() => Attach().ActualThemeVariant)), h.Main.OnMain(() => Plate(Attach())));
            Close(h, pickWindow);

            // Off, flipped while the pill is on screen: the same click now runs the file dialog and presents nothing.
            var calls = presenter.Calls.Count;
            h.Main.OnMain(() => h.App.Settings.RecentDownloadsPicker = false);
            Pump(h, window);
            h.Main.OnMain(() => Click(Attach()));
            Until(h, window, () => dialog.Options is not null);
            Assert.Equal((true, false, "Attach"), dialog.Options);
            Assert.Equal(calls, presenter.Calls.Count);

            // On again, flipped live: the popover once more.
            h.Main.OnMain(() => h.App.Settings.RecentDownloadsPicker = true);
            Pump(h, window);
            h.Main.OnMain(() => Click(Attach()));
            Pump(h, window);
            Assert.True(presenter.Calls[^1].Presented);
            Assert.Equal(calls + 1, presenter.Calls.Count);
            Close(h, window);
        });
    }

    // ---- PR-COMPOSER-090 (:625-656) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-090")] // App/Views/Composer/ComposerView.swift:625-656
    [Trait("Rule", "R-TEST-4")] // The record's verification is a capture comparison of the chat composer and a head panel's, which the video does not give a frame for at pixel level (CAPTURE-ONLY); the order and the compact omission: B3_controls_are_attach_model_context_send_two_apart_and_compact_omits_context
    public void The_controls_sit_two_points_apart_and_keep_their_own_width_however_little_room_they_are_given()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            foreach (var compact in new[] { false, true })
            {
                ComposerControls Make() => new(h.Runtime, h.Thread, compact, false, false, null, () => { });
                var wide = h.Main.OnMain(Make);
                var wideWindow = h.Main.OnMain(() => Show(wide, h.App, 600));
                Pump(h, wideWindow);
                var narrow = h.Main.OnMain(Make);
                var narrowWindow = h.Main.OnMain(() => Show(narrow, h.App, 40));
                Pump(h, narrowWindow);
                h.Main.OnMain(() =>
                {
                    var wideRow = Assert.IsType<StackPanel>(wide.Child);
                    var narrowRow = Assert.IsType<StackPanel>(narrow.Child);
                    Assert.Equal(compact ? 2 : 3, wideRow.Children.Count); // without the attach button, which is the pill's left now (V-14.1, owner-ruled)
                    for (var index = 1; index < wideRow.Children.Count; index++)
                        Assert.Equal(2, Frame(wideRow.Children[index], wideRow).X - Frame(wideRow.Children[index - 1], wideRow).Right, 5); // 2 points apart
                    // Avalonia clamps a control's own DesiredSize to what it was offered; the row inside it is measured unconstrained,
                    // which is what .fixedSize() proposes (:655), and is what the Auto column of the pill is given.
                    Assert.Equal(wide.DesiredSize.Width, narrow.Child!.DesiredSize.Width, 5);
                    Assert.Equal(wide.DesiredSize.Height, narrow.Child!.DesiredSize.Height, 5);
                    for (var index = 0; index < wideRow.Children.Count; index++)
                    {
                        Assert.True(Math.Abs(wideRow.Children[index].Bounds.Width - narrowRow.Children[index].Bounds.Width) < 0.001, "child " + index + " " + wideRow.Children[index].GetType().Name + " " + wideRow.Children[index].Bounds + " vs " + narrowRow.Children[index].Bounds); // none compressed
                        Assert.Equal(wideRow.Children[index].Bounds.Height, narrowRow.Children[index].Bounds.Height, 5);
                    }
                    Assert.True(wide.DesiredSize.Width > 40);
                });
                Close(h, narrowWindow);
                Close(h, wideWindow);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:599-602; ComposerTextView.swift:234-238, :273
    [Trait("Rule", "R-TEST-4")] // The heights: Five_lines_fill_the_ceiling_exactly_...
    public void The_text_view_draws_its_text_and_past_five_lines_it_scrolls_inside_the_fixed_height_and_keeps_the_caret_in_view()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var column = h.Main.OnMain(() => Column(h.Runtime, notice: null));
            var window = h.Main.OnMain(() => Show(column, h.App, width: 400));
            Pump(h, window);
            var eight = string.Join("\n", Enumerable.Range(1, 8).Select(n => "line " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = eight });
            Pump(h, window);
            var (box, scroll) = h.Main.OnMain(() => (One<ComposerNSTextView>(column), One<ScrollViewer>(column)));
            h.Main.OnMain(() =>
            {
                // A TextBox with no template draws nothing: the typed text needs its presenter.
                var presenter = Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<Avalonia.Controls.Presenters.TextPresenter>());
                Assert.Equal(eight, presenter.Text);
                Assert.Equal(FiveLineCeiling(), scroll.Viewport.Height); // five lines and the inset
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 10, "eight lines are taller than the fixed height"); // so it scrolls
                box.Focus();
                box.CaretIndex = eight.Length;
            });
            Pump(h, window);
            h.Main.OnMain(() => Assert.True(scroll.Offset.Y > 0, "the caret at the last line is scrolled into view"));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerTextView.swift:234-274 (the NSTextView draws its own text and insertion point)
    [Trait("Rule", "R-TEST-4")] // The text view's drawing; the growth and the scroll: the two tests above
    public void A_text_view_with_the_keyboard_draws_its_insertion_point_and_one_without_it_draws_nothing_while_empty()
    {
        headless.OnUiThread(() =>
        {
            var idle = new ComposerTextViewHarness(placeholder: "", takesFocusOnAppear: false);
            idle.Layout();
            var area = new Rect(0, 0, idle.View.Bounds.Width, ComposerTextViewHarness.ComposerHeight);
            Assert.Null(Ink.Bounds(idle.Window, area));
            var focused = new ComposerTextViewHarness(placeholder: "", takesFocusOnAppear: true);
            focused.Layout();
            focused.Focus();
            ComposerTextViewHarness.Pump();
            var caret = Ink.Bounds(focused.Window, area);
            Assert.NotNull(caret);
            Assert.InRange(caret!.Value.Width, 1, 3); // the one point bar, not a line of text
            Assert.True(caret.Value.Height >= 14);
        });
    }
}
