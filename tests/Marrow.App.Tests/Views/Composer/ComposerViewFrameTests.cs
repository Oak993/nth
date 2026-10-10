using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Runtime;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// The composer's trailing controls against the reference frame (2-chat-use.mov C 19.700, 1 px = 1 pt, chat composer of a Claude thread with the context ring: the pill
// spans 1004 to 1824). MEASURED ON THE FRAME: the 28 point send disc 8 in from the pill's right edge (its pixel columns 1788 to 1816, edge 1824); the ring 17.5 across
// (the ring's pixel columns 1760 to 1778) with its centre 55 from the edge, which is the disc, the 4 of the send button's leading padding (:842), the 2 of the row's
// spacing (:626) and the ring button's 15 + 2 x 6 wide frame (:726-729): 8 + 28 + 4 + 2 + 13.5 = 55.5. Source: DroppyCode/App/Views/Composer/ComposerView.swift:625-656, :726-731, :842.
[Collection("Headless")]
public sealed class ComposerViewFrameTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-090")] // App/Views/Composer/ComposerView.swift:625-656
    public void The_trailing_controls_are_attach_model_ring_and_send_two_apart_fixed_size_with_no_ring_in_a_compact_composer_and_on_the_frames_places()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            // The chat composer of the frame: a resolved thread, a context ring, 820 wide.
            h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
            var view = Pill(h, h.Runtime, null);
            var window = h.Main.OnMain(() => Show(view, h.App, 820));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var controls = One<ComposerControls>(view);
                var row = Assert.IsType<StackPanel>(controls.Child);
                // :626 HStack(spacing: 2): the model chip, the ring, send, in that order, 2 apart. The attach button, first in the source, is the pill's left (V-14.1, owner-ruled).
                Assert.Equal(2, row.Spacing);
                Assert.Equal(new[] { "ModelEffortSlot", "ContextMeter", "SendButton" }, row.Children.Select(child => child.GetType().Name).ToArray());
                for (var index = 1; index < row.Children.Count; index++)
                    Assert.Equal(2, Frame(row.Children[index], row).X - Frame(row.Children[index - 1], row).Right, 0.5);
                // On the frame: the disc 8 from the pill's right edge, 28 across; the ring 17.5 across with its centre 55.5 from the edge.
                // OD-0136 amendment 2, step 9: the disc is the 32 px nth send face (the page's .sb.new, 32 across, the pill staying 46). It grows to the left, the 8 from the edge kept, so the
                // ring's centre is 8 + 32 + 4 + 2 + 13.5 = 59.5 from the edge.
                var pill = Frame(view, window);
                Assert.Equal(46, pill.Height, 0.5); // the pill with its controls is 46 from rim to rim on the frame (rows 1298-1343), not the record's 44
                var send = Frame(row.Children[2], window);
                var disc = Frame(LogicalQuery.SelfAndDescendants<Button>(row.Children[2]).First(button => button.Bounds.Width >= 31), window);
                Assert.Equal(pill.Right - 8, disc.Right, 1);
                Assert.Equal(32, disc.Width, 0.5);
                Assert.Equal(32, disc.Height, 0.5);
                Assert.Equal(4, disc.X - send.X, 0.5); // the send button's leading padding
                var meter = Frame(row.Children[1], window);
                Assert.Equal(15 + 12, meter.Width, 0.5); // the ring button: 15 across and 6 either side
                Assert.Equal(pill.Right - 59.5, meter.X + meter.Width / 2, 1.5);
            });
            // :655 .fixedSize(): never compressed. Given less room than they ask for, the row still wants its full width.
            var wide = h.Main.OnMain(() => One<ComposerControls>(view).Child!.DesiredSize.Width);
            h.Main.OnMain(() =>
            {
                var controls = One<ComposerControls>(view);
                controls.Measure(new Size(40, 100));
                Assert.Equal(wide, controls.Child!.DesiredSize.Width, 0.5); // Avalonia clamps the control's own DesiredSize to the room offered; the row inside is measured unconstrained, as .fixedSize() proposes
            });
            Close(h, window);
            // A compact (floating panel) composer leaves the ring out entirely and keeps the other three in order, 2 apart.
            foreach (var compact in new[] { true })
            {
                var controls = h.Main.OnMain(() => new ComposerControls(h.Runtime, h.Thread, compact, false, false, null, () => { }));
                var compactWindow = h.Main.OnMain(() => Show(controls, h.App, 600));
                Pump(h, compactWindow);
                h.Main.OnMain(() =>
                {
                    var row = Assert.IsType<StackPanel>(controls.Child);
                    Assert.Equal(new[] { "ModelEffortSlot", "SendButton" }, row.Children.Select(child => child.GetType().Name).ToArray());
                    Assert.Empty(LogicalQuery.SelfAndDescendants<ContextMeter>(controls));
                    for (var index = 1; index < row.Children.Count; index++)
                        Assert.Equal(2, Frame(row.Children[index], row).X - Frame(row.Children[index - 1], row).Right, 0.5);
                    controls.Measure(new Size(40, 100));
                    Assert.True(controls.Child!.DesiredSize.Width > 40);
                });
                Close(h, compactWindow);
            }
        });
    }

    // PR-COMPOSER-088 whole (ComposerView.swift:880-959; no reference frame shows a chip row: OD-0098, the Swift is the proof): the chips sit in a horizontal scroller
    // with no visible indicators, 6 points apart and 8 above the text; the row opens from nothing (zero height, zero opacity, clipped from the bottom: the content stays
    // at the top) as the first chip arrives and closes to nothing as the last one leaves, on Chrome.panelSlide; a chip joining or leaving a row that stays moves under the
    // same spring while the scroller and its offset are the same ones; the animation is keyed on the quote ids and the command name, so a text edit asks for no frame.
    [Fact]
    [Trait("Req", "PR-COMPOSER-088")] // App/Views/Composer/ComposerView.swift:879-881, :939-941, :950-959
    public void The_chip_row_is_a_horizontal_scroller_that_opens_from_and_closes_to_nothing_clipped_from_the_bottom_and_animates_only_on_the_ids_and_the_command()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime));
            var window = h.Main.OnMain(() => Show(chips, h.App, 400));
            Pump(h, window);
            void Advance(double seconds) { h.Main.Advance(TimeSpan.FromSeconds(seconds)); h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(seconds))); Pump(h, window); }
            Assert.Equal(0, h.Main.OnMain(() => chips.Bounds.Height)); // :879 nothing to show takes no room

            // The first chip: the row grows from zero height and fades in, clipped from the bottom (its content keeps to the top).
            var first = new ReplyQuote { Text = "first quote" };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "t", Quotes = new ValueList<ReplyQuote>([first]) });
            Pump(h, window);
            var row = h.Main.OnMain(() => Assert.IsType<Border>(ChipRows(chips).Children.Single()));
            Assert.True(h.Main.OnMain(() => row.ClipToBounds)); // :957 .clipped()
            Assert.Equal(VerticalAlignment.Top, h.Main.OnMain(() => ((Control)row.Child!).VerticalAlignment)); // :956 alignment: .top
            Assert.Equal(0, h.Main.OnMain(() => row.Height)); // :955 frame(height: 0)
            Assert.Equal(0, h.Main.OnMain(() => row.Opacity)); // :958 opacity 0
            Advance(0.03);
            var progress = Chrome.PanelSlide.Progress(0.03);
            Assert.InRange(progress, 0.01, 0.99);
            Assert.Equal(progress, h.Main.OnMain(() => row.Opacity), 3);
            Advance(2);
            var full = h.Main.OnMain(() => row.Bounds.Height);
            Assert.True(full > 8);
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity));
            // :880 the scroller: horizontal, no indicators; :939 8 below the chips.
            var scroller = h.Main.OnMain(() => One<ScrollViewer>(chips));
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);
            Assert.Equal(8, h.Main.OnMain(() => scroller.Margin.Bottom));

            // Two more chips: each arrives alone (the row, the scroller and the chips before it do not flicker) and the chips are 6 apart.
            var quotes = new[] { first, new ReplyQuote { Text = new string('b', 240) }, new ReplyQuote { Text = new string('c', 240) } };
            var firstChip = h.Main.OnMain(() => ChipControls(chips).Single());
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Quotes = new ValueList<ReplyQuote>(quotes) });
            Pump(h, window);
            var drawn = h.Main.OnMain(() => ChipControls(chips).ToArray());
            h.Main.OnMain(() =>
            {
                Assert.Same(scroller, One<ScrollViewer>(chips));
                Assert.Same(row, ChipRows(chips).Children.Single());
                Assert.Same(firstChip, drawn[0]);
                Assert.Equal(1, row.Opacity);
                Assert.Equal(1, firstChip.Opacity);
                Assert.Equal(0, drawn[1].Opacity); // the arrivals fade in from nothing
                Assert.Equal(0, drawn[2].Opacity);
            });
            Advance(2);
            h.Main.OnMain(() =>
            {
                Assert.Equal(1, drawn[1].Opacity);
                var chip = Frame(((Decorator)drawn[0]).Child!, scroller);
                var next = Frame(((Decorator)drawn[1]).Child!, scroller);
                Assert.Equal(6, next.X - chip.Right, 0.5); // :881 HStack(spacing: 6)
            });
            // Scrolled along, a text edit asks for no frame and moves nothing (:941 the key is the ids and the command's name).
            h.Main.OnMain(() => scroller.Offset = new Vector(60, 0));
            Pump(h, window);
            var pending = frames.Pending;
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Text = "t and more" });
            Pump(h, window);
            Assert.Equal(pending, frames.Pending);
            Assert.Equal(drawn, h.Main.OnMain(() => ChipControls(chips).ToArray()));
            Assert.Equal(60, h.Main.OnMain(() => scroller.Offset.X));
            // The key holds the command's name too (:941 ChipKey(quoteIDs:commandName:)): a command joining a row of quotes arrives from nothing on the spring (the key moved only by
            // the name), the scroller and the quotes' chips staying as they were, and the command leaving again fades where it stands. Without the name in the key neither would move.
            var command = new DraftCommand { Name = "review", Detail = "Review the changes", IsBuiltIn = false };
            h.Main.OnMain(() => h.Runtime.Draft = h.Runtime.Draft with { Command = command });
            Pump(h, window);
            var commandChip = h.Main.OnMain(() => ChipControls(chips).First());
            h.Main.OnMain(() =>
            {
                Assert.Same(scroller, One<ScrollViewer>(chips));
                Assert.Equal(4, ChipControls(chips).Count());
                Assert.Equal(drawn, ChipControls(chips).Skip(1).ToArray()); // the quotes' chips are the same ones, after the command's (:882 first)
                Assert.Equal(0, commandChip.Opacity); // arriving from nothing
                Assert.Equal(1, drawn[0].Opacity);
            });
            Advance(0.03);
            h.Main.OnMain(() => Assert.Equal(progress, commandChip.Opacity, 3));
            Advance(2);
            h.Main.OnMain(() => Assert.Equal(1, commandChip.Opacity));
            h.Main.OnMain(() => Click(Named(commandChip, "Remove command")));
            Pump(h, window);
            Advance(0.03);
            h.Main.OnMain(() =>
            {
                Assert.Equal(1 - progress, commandChip.Opacity, 3); // leaving: it fades where it stands
                Assert.Equal(1, drawn[0].Opacity);
                Assert.Same(scroller, One<ScrollViewer>(chips));
            });
            Advance(2);
            Assert.Equal(drawn, h.Main.OnMain(() => ChipControls(chips).ToArray()));
            Assert.Equal(60, h.Main.OnMain(() => scroller.Offset.X));
            // A chip leaving a row that stays fades where it stands; the scroller and its offset are the same ones.
            h.Main.OnMain(() => Click(Named(drawn[0], "Remove quote")));
            Pump(h, window);
            Advance(0.03);
            h.Main.OnMain(() =>
            {
                Assert.Same(scroller, One<ScrollViewer>(chips));
                Assert.Equal(1 - progress, drawn[0].Opacity, 3);
                Assert.Equal(1, drawn[1].Opacity);
                Assert.Equal(1, ChipRows(chips).Children.Single().Opacity); // the row itself stays whole
                Assert.Equal(60, scroller.Offset.X);
            });
            Advance(2);
            // The last chips leave: the row collapses to nothing (opacity and height on the curve) and is gone.
            h.Main.OnMain(() => Click(Named(drawn[1], "Remove quote")));
            Advance(2);
            h.Main.OnMain(() => Click(Named(drawn[2], "Remove quote")));
            Pump(h, window);
            Assert.Equal(1, h.Main.OnMain(() => row.Opacity)); // still full at the first frame
            Advance(0.03);
            Assert.Equal(1 - progress, h.Main.OnMain(() => row.Opacity), 3);
            Assert.Equal(full * (1 - progress), h.Main.OnMain(() => row.Height), 1.5);
            Advance(2);
            Assert.Empty(h.Main.OnMain(() => ChipRows(chips).Children.ToArray()));
            Close(h, window);
        });
    }
}
