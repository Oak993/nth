using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
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

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :868-970 (the draft chips: the slash command chip, the reply
// quote chips, their remove buttons and the chip capsule): the blitz-composerview unit's proofs for UI-COMPOSER-005, 006, 007, 008.
[Collection("Headless")]
public sealed class ComposerViewBlitzChipTests(HeadlessSession headless)
{
    // The draft written while the transaction it is written in is watched: Swift's willSet, so the animation it was written under.
    internal static Animation? WrittenUnder(ThreadRuntimePart12Harness h, ThreadRuntime runtime, Action write)
    {
        Animation? seen = null;
        var wrote = false;
        h.Main.OnMain(() =>
        {
            ObservationTracking.WithTracking(() => runtime.Draft, () => { wrote = true; seen = Transaction.Current; });
            write();
        });
        Assert.True(wrote, "the draft was written");
        return seen;
    }

    internal static Color InkOf(Control control) => control switch
    {
        SwiftImage image => Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color,
        TextBlock text => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color,
        _ => throw new InvalidOperationException(control.GetType().Name),
    };

    // ---- UI-COMPOSER-005 (:882-907, :962-970): the slash command chip ----

    [Theory]
    [InlineData(true, "command")] // a built-in command
    [InlineData(false, "sparkles")] // a provider's command
    [Trait("Req", "UI-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:882-907
    [Trait("Rule", "R-TEST-4")] // Text, fonts, tooltip, capsule and removal: B3_command_chip_*, B3_chip_*; the row's arrival and departure: The_first_chip_grows_the_row_from_nothing_...
    public void The_command_chip_names_its_symbol_by_kind_draws_the_symbol_and_the_cross_in_secondary_text_and_sits_in_a_row_8_above_the_text_with_6_between_chips(bool builtIn, string symbol)
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var quote = new ReplyQuote { Text = "a quoted line" };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Command = new DraftCommand { Name = "review", Detail = "Review the diff", IsBuiltIn = builtIn }, Quotes = new ValueList<ReplyQuote>([quote]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 600));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var chipControls = ChipControls(chips).ToArray();
                Assert.Equal(2, chipControls.Length);
                var commandChip = chipControls[0];
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(commandChip).First();
                Assert.Equal(symbol, glyph.SymbolName);
                var cross = One<SwiftImage>(Named(commandChip, "Remove command"));
                var secondary = Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant);
                Assert.Equal(secondary, InkOf(glyph)); // Chrome.secondaryText on the symbol
                Assert.Equal(secondary, InkOf(cross)); // and on the x
                Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(glyph.ActualThemeVariant), InkOf(One<TextBlock>(commandChip))); // the label at 0.9
                // The quote chip's symbol and cross are the same ink (UI-COMPOSER-007, 008).
                var quoteChip = chipControls[1];
                Assert.Equal(secondary, InkOf(LogicalQuery.SelfAndDescendants<SwiftImage>(quoteChip).First()));
                Assert.Equal(secondary, InkOf(One<SwiftImage>(Named(quoteChip, "Remove quote"))));
                // HStack(spacing: 6) between the chips; the row's own .padding(.bottom, 8) under it.
                var gap = Frame(quoteChip, chips).X - Frame(commandChip, chips).Right;
                Assert.Equal(6, gap, 3);
                var scroller = One<ScrollViewer>(chips);
                Assert.Equal(new Thickness(0, 0, 0, 8), scroller.Margin);
                Assert.Equal(Frame(commandChip, chips).Bottom + 8, chips.Bounds.Height, 3);
            });
            Close(h, window);
        });
    }

    // ---- UI-COMPOSER-006 / 008 (:890-902, :919-931): the remove buttons ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-006")] // App/Views/Composer/ComposerView.swift:890-902
    [Trait("Req", "UI-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:891-893, :962-970
    [Trait("Rule", "R-TEST-4")] // Label, tooltip, focus and the font: B3_command_chip_*, B3_chip_fonts_and_remove_labels_*; the colour: the test above
    public void The_command_chips_cross_clears_only_the_command_inside_panelSlide_and_Backspace_at_the_start_does_the_same()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var quote = new ReplyQuote { Text = "kept" };
            ComposerDraft Draft() => new() { Text = "keep", Command = new DraftCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true }, Quotes = new ValueList<ReplyQuote>([quote]) };
            h.Main.OnMain(() => h.Runtime.Draft = Draft());
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 600));
            Pump(h, window);
            var cross = h.Main.OnMain(() => Named(chips, "Remove command"));
            var animation = WrittenUnder(h, h.Runtime, () => Click(cross));
            Assert.Equal(Chrome.PanelSlide, animation); // withAnimation(Chrome.panelSlide) { runtime.draft.command = nil }
            Assert.Null(h.Runtime.Draft.Command);
            Assert.Equal("keep", h.Runtime.Draft.Text);
            Assert.Equal([quote], h.Runtime.Draft.Quotes);
            Close(h, window);

            // Backspace with the caret at 0 (:357-366) takes the command inside the same animation.
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Command = Draft().Command });
            var view = Pill(h, h.Runtime);
            Assert.Equal(Chrome.PanelSlide, WrittenUnder(h, h.Runtime, () => Assert.True(Press(view, Key.Back))));
            Assert.Null(h.Runtime.Draft.Command);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-008")] // App/Views/Composer/ComposerView.swift:919-931
    [Trait("Req", "UI-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:919-922, :357-366
    [Trait("Rule", "R-TEST-4")] // Label, tooltip, focus, excerpt and font: B3_quote_chip_*, B3_chip_fonts_and_remove_labels_*; the colour: the first chip test of this file
    public void A_quotes_cross_removes_that_quote_by_id_inside_panelSlide_the_middle_of_three_leaves_the_other_two_in_order_and_Backspace_takes_the_last()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var (a, b, c) = (new ReplyQuote { Text = "alpha" }, new ReplyQuote { Text = "bravo" }, new ReplyQuote { Text = "charlie" });
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([a, b, c]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 600));
            Pump(h, window);
            var middle = h.Main.OnMain(() => ChipControls(chips).ElementAt(1));
            var animation = WrittenUnder(h, h.Runtime, () => Click(Named(middle, "Remove quote")));
            Assert.Equal(Chrome.PanelSlide, animation); // withAnimation(Chrome.panelSlide) { quotes.removeAll { $0.id == quote.id } }
            Assert.Equal([a.Id, c.Id], h.Runtime.Draft.Quotes.Select(quote => quote.Id)); // by id, in order
            h.Main.Advance(TimeSpan.FromSeconds(2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            Assert.Equal(["alpha", "charlie"], h.Main.OnMain(() => ChipControls(chips).Select(chip => LogicalQuery.SelfAndDescendants<TextBlock>(chip).First().Text!).ToArray()));
            Close(h, window);
            // Backspace at 0, with no command chip left, removes the last quote inside the same animation (:362-365).
            var view = Pill(h, h.Runtime);
            Assert.Equal(Chrome.PanelSlide, WrittenUnder(h, h.Runtime, () => Assert.True(Press(view, Key.Back))));
            Assert.Equal([a.Id], h.Runtime.Draft.Quotes.Select(quote => quote.Id));
        });
    }

    // ---- UI-COMPOSER-007 (:908-936): the reply quote chip ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:908-936
    [Trait("Rule", "R-TEST-4")] // The excerpt, its cap, fonts and the removal: B3_quote_chip_*, B3_chip_fonts_and_remove_labels_*, the chip tests of this file
    public void A_quote_chips_tooltip_is_the_whole_quote_and_several_quotes_scroll_the_row_sideways()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var long1 = "first line of the first quote\nsecond line that the excerpt leaves out " + new string('x', 300);
            var quotes = new[] { long1, new string('b', 400), new string('c', 400) }.Select(text => new ReplyQuote { Text = text }).ToArray();
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>(quotes) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 400));
            Pump(h, window);
            var scroller = h.Main.OnMain(() => One<ScrollViewer>(chips));
            h.Main.OnMain(() =>
            {
                var chipControls = ChipControls(chips).ToArray();
                for (var index = 0; index < quotes.Length; index++)
                    Assert.Equal(quotes[index].Text, ToolTip.GetTip(Assert.IsType<ChipSlot>(chipControls[index]).Chip)); // .help(quote.text): the whole quote, not its excerpt
                Assert.True(scroller.Extent.Width > scroller.Viewport.Width + 100, $"the chips overflow the row: {scroller.Extent} in {scroller.Viewport}");
                Assert.Equal(0, scroller.Offset.X);
                var at = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2), window)!.Value;
                window.MouseWheel(at, new Vector(-3, 0));
                Settle(window);
                Assert.Equal(0, scroller.Offset.X); // ED-NEW-softscroll-1: the row glides (OD-0071), nothing moves in the report's own frame
            });
            // The glide on the frame clock, the Swift clock with it: under way after one 60 Hz frame, at rest three notches (300 points at
            // the default 3 lines, ED-NEW-softscroll-2) on, or at the row's end.
            var most = h.Main.OnMain(() => scroller.Extent.Width - scroller.Viewport.Width);
            var trace = new List<double>();
            for (var frame = 0; frame < 60; frame++)
            {
                h.Main.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
                h.Main.OnMain(() => { frames.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)); Settle(window); trace.Add(scroller.Offset.X); });
            }
            Assert.InRange(trace[0], 0.01, Math.Min(300, most) - 0.01);
            Assert.Equal(Math.Min(300, most), trace[^1], 3);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:908-936, :941 .animation(Chrome.panelSlide, value: ChipKey)
    [Trait("Rule", "R-TEST-4")] // The excerpt, tooltip, scroll and the removal's writer: the tests above; the row's own collapse: The_first_chip_grows_the_row_from_nothing_...
    public void Removing_the_middle_quote_fades_it_where_it_stands_while_the_chip_after_it_follows_under_the_spring_and_nothing_jumps_at_the_end()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var (a, b, c) = (new ReplyQuote { Text = "alpha" }, new ReplyQuote { Text = "bravo bravo bravo" }, new ReplyQuote { Text = "charlie" });
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([a, b, c]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 600));
            Pump(h, window);
            var kept = h.Main.OnMain(() => ChipControls(chips).ToArray());
            var (first, middle, last) = (kept[0], kept[1], kept[2]);
            double X(Control control) => h.Main.OnMain(() => Frame(control, chips).X);
            var (x1, x2, x3) = (X(first), X(middle), X(last));
            var travel = x3 - x2; // the middle chip's width and the 6 point gap
            var widthOfMiddle = h.Main.OnMain(() => ((ChipSlot)middle).Chip.Bounds.Width);
            Assert.True(travel > 30, $"the middle chip takes room: {travel}");
            h.Main.OnMain(() => Click(Named(middle, "Remove quote")));
            Pump(h, window);
            Assert.Equal(x3, X(last), 3); // the first frame is where it was
            var previous = x3;
            foreach (var moment in new[] { 0.03, 0.03, 0.04, 0.05, 0.1 })
            {
                Elapse(h, frames, window, moment);
                var now = X(last);
                Assert.True(now <= previous + 0.01, $"the chip after it only moves left: {previous} then {now}");
                previous = now;
            }
            var elapsed = 0.25;
            var expected = x3 - travel * Chrome.PanelSlide.Progress(elapsed);
            Assert.InRange(Math.Abs(X(last) - expected), 0, 1.5); // the spring's curve, not a step
            Assert.InRange(X(last), x2 - 1, x3 - 1); // part-way
            Assert.Equal(x1, X(first), 3); // the chip before it stays
            Assert.Equal(widthOfMiddle, h.Main.OnMain(() => ((ChipSlot)middle).Chip.Bounds.Width), 3); // the leaving chip keeps its shape while its place closes
            Assert.True(Math.Abs(x2 - X(middle)) < 0.01, $"the leaving chip fades where it stands: {x2} then {X(middle)}; chip {h.Main.OnMain(() => Frame(((ChipSlot)middle).Chip, chips))}, slot {h.Main.OnMain(() => middle.Bounds)}");
            Elapse(h, frames, window, 3);
            Assert.Equal(x2, X(last), 1); // and lands exactly where the middle chip was, no step at the end
            Assert.Equal([a.Id, c.Id], h.Runtime.Draft.Quotes.Select(quote => quote.Id));
            Assert.Equal(2, h.Main.OnMain(() => ChipControls(chips).Count()));
            Close(h, window);
        });
    }
}
