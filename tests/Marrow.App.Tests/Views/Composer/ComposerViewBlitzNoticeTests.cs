using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
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

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :219-225, :511-524, :576-583 (the attachment notice): the
// blitz-composerview unit's proof for UI-COMPOSER-004's transitions; the owner's OD-0076 gives the pill's height the ease the
// transaction asks for (ED-282, the notice's row).
[Collection("Headless")]
public sealed class ComposerViewBlitzNoticeTests(HeadlessSession headless)
{
    internal const string CapNotice = "One file was left out: a message holds 8 attachments";

    [Fact]
    [Trait("Req", "UI-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:576-583, :522-524
    [Trait("Rule", "R-TEST-4")] // The removal: the next test; the strings and style: B3_attachment_notice_*
    public void The_notice_row_opens_under_panelSlide_so_the_pill_eases_to_its_new_height_while_the_line_comes_in_as_softAppear()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var view = Pill(h, Unresolved(h));
            var window = h.Main.OnMain(() => Show(view, h.App, 500));
            Pump(h, window);
            double Height() => h.Main.OnMain(() => view.Bounds.Height);
            var before = Height();
            h.Main.OnMain(() => InvokePrivate(view, "Note", CapNotice));
            Pump(h, window);
            Elapse(h, frames, window, 2);
            var total = Height() - before;
            Assert.True(total > 11, $"the pill grows by the line and its 6 point margin: {before} to {before + total}");
            Close(h, window);

            // The arrival, sampled.
            var view2 = Pill(h, Unresolved(h));
            var window2 = h.Main.OnMain(() => Show(view2, h.App, 500));
            Pump(h, window2);
            var start = h.Main.OnMain(() => view2.Bounds.Height);
            h.Main.OnMain(() => InvokePrivate(view2, "Note", CapNotice));
            Pump(h, window2);
            var line2 = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<TextBlock>(view2).Single(text => text.Text == CapNotice));
            Assert.Equal(start, h.Main.OnMain(() => view2.Bounds.Height), 3); // the pill has not moved at the first frame
            Assert.Equal(0, h.Main.OnMain(() => line2.Opacity)); // .softAppear: the line comes in from nothing ...
            Assert.Equal(4, h.Main.OnMain(() => Assert.IsType<TranslateTransform>(line2.RenderTransform).Y), 3); // ... 4 points low
            foreach (var moment in new[] { 0.05, 0.05, 0.1 }) Elapse(h, frames, window2, moment);
            // 0.2 s in: the height is the transaction's spring.
            var elapsed = 0.2;
            var grown = h.Main.OnMain(() => view2.Bounds.Height) - start;
            var expected = total * Chrome.PanelSlide.Progress(elapsed);
            Assert.InRange(Math.Abs(grown - expected), 0, 1.0);
            Assert.InRange(grown, 1, total - 1);
            // The line's own arrival is on the soft curve (smooth 0.32): opacity up, the rise down, together.
            var soft = Animation.SoftAppear.Progress(elapsed);
            Assert.InRange(soft, 0.2, 0.999);
            Assert.InRange(Math.Abs(h.Main.OnMain(() => line2.Opacity) - soft), 0, 0.02);
            Assert.InRange(Math.Abs(h.Main.OnMain(() => Assert.IsType<TranslateTransform>(line2.RenderTransform).Y) - 4 * (1 - soft)), 0, 0.1);
            Elapse(h, frames, window2, 2);
            Assert.Equal(1, h.Main.OnMain(() => line2.Opacity));
            Assert.Equal(0, h.Main.OnMain(() => (line2.RenderTransform as TranslateTransform)?.Y ?? 0), 3);
            Close(h, window2);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:219-225, :576-583
    [Trait("Rule", "R-TEST-4")] // The arrival: the test above; the 5 second key: B3_attachment_notice_uses_a_five_second_string_key_...
    public void After_5_s_the_notice_fades_out_over_0_12_s_while_the_pill_eases_back_and_nothing_is_left_behind()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var view = Pill(h, Unresolved(h));
            var window = h.Main.OnMain(() => Show(view, h.App, 500));
            Pump(h, window);
            double Height() => h.Main.OnMain(() => view.Bounds.Height);
            var before = Height();
            h.Main.OnMain(() => InvokePrivate(view, "Note", CapNotice));
            Pump(h, window);
            var line = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(text => text.Text == CapNotice));
            var column = h.Main.OnMain(() => One<ComposerTextColumn>(view));
            Elapse(h, frames, window, 2);
            var grown = Height();
            Assert.True(grown - before > 11);
            // Three seconds on, 5 s after the notice: the string is cleared inside withAnimation(Chrome.panelSlide).
            h.Main.Advance(TimeSpan.FromSeconds(3));
            Pump(h, window);
            Assert.Null(h.Main.OnMain(() => view.AttachmentNotice));
            Assert.Equal(1, h.Main.OnMain(() => line.Opacity)); // the first frame of the leaving is where it was
            Assert.Equal(grown, Height(), 3);
            Elapse(h, frames, window, 0.06);
            var fade = h.Main.OnMain(() => line.Opacity);
            Assert.InRange(Math.Abs(fade - (1 - Animation.EaseOut(duration: 0.12).Progress(0.06))), 0, 0.02); // 0.12 s, ease out
            var shrinking = Height();
            var expected = before + (grown - before) * (1 - Chrome.PanelSlide.Progress(0.06));
            Assert.InRange(Math.Abs(shrinking - expected), 0, 1.0);
            Assert.True(shrinking < grown && shrinking > before, $"between: {before} < {shrinking} < {grown}");
            Elapse(h, frames, window, 2);
            Assert.Equal(before, Height(), 3);
            h.Main.OnMain(() =>
            {
                var stack = Assert.IsType<StackPanel>(column.Child);
                Assert.Equal(3, stack.Children.Count); // the chips, the strip and the text view: the notice's row is gone
                Assert.Empty(LogicalQuery.SelfAndDescendants<NoticeSlot>(column));
            });
            Close(h, window);
        });
    }
}
