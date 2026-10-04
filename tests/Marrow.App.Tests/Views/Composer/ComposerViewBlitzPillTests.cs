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
using Marrow.Services.Providers;
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

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :22-57 (the area), :143-189 (the pill), :625-646 (the attach button),
// DroppyCode/UI/Common/Components.swift :57-97 (ChipButtonStyle): the blitz-composerview unit's proofs for UI-COMPOSER-001, 002 and 011.
[Collection("Headless")]
public sealed class ComposerViewBlitzPillTests(HeadlessSession headless)
{
    // ---- UI-COMPOSER-001 (:22-57) ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:22-57
    [Trait("Rule", "R-TEST-4")] // The 820 / 20 / 14 / 12 / -14 numbers and the missing slot: The_area_caps_its_content_at_820_..., The_pill_overlaps_the_tab_by_14_points_..., B3_approval_cards_precede_the_pill_...
    public void The_cards_the_tab_and_the_pill_are_all_inside_one_glass_container_of_spacing_8_apart_from_the_stacks_12_and_minus_14()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = h.Runtime;
            GiveStats(h, runtime); // the changes tab
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("only"))));
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null));
            var window = h.Main.OnMain(() => Show(area, h.App, 700));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var container = Ancestors(Stack(area)).OfType<Control>().Single(control => SwiftGlass.GetContainerSpacing(control) is not null);
                Assert.Equal(8, SwiftGlass.GetContainerSpacing(container)); // GlassEffectContainer(spacing: 8)
                Assert.Equal(12, Stack(area).Spacing); // VStack(spacing: 12), a different number
                Assert.Equal(-ThreadChangesTab.Overlap, Group(area).Spacing); // inner VStack(spacing: -14), a different number
                var tab = One<ThreadChangesTab>(area);
                var pill = One<ComposerView>(area);
                var card = One<ApprovalCard>(area);
                Assert.Contains(container.GetLogicalDescendants(), descendant => ReferenceEquals(descendant, tab));
                Assert.Contains(container.GetLogicalDescendants(), descendant => ReferenceEquals(descendant, pill));
                Assert.Contains(container.GetLogicalDescendants(), descendant => ReferenceEquals(descendant, card));
                // Every piece of glass in the area merges in that one container: its nearest container is that one.
                var glass = LogicalQuery.SelfAndDescendants<Control>(area).Where(control => SwiftGlass.GetEffect(control) is not null).ToArray();
                Assert.NotEmpty(glass);
                Assert.All(glass, piece => Assert.Same(container, piece.GetVisualAncestors().OfType<Control>().First(ancestor => SwiftGlass.GetContainerSpacing(ancestor) is not null)));
                Assert.Contains(glass, piece => SwiftGlass.GetEffect(piece)!.Shape.Equals(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)) && pill.GetLogicalDescendants().Contains(piece)); // the pill's
                Assert.Contains(glass, piece => tab.GetLogicalDescendants().Contains(piece) || ReferenceEquals(piece, tab)); // and the tab's
            });
            Close(h, window);
        });
    }

    // ---- UI-COMPOSER-002 (:145-189) ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:157-158, :176-178; ComposerTextView.swift:360-376
    [Trait("Rule", "R-TEST-4")] // The paddings, the surface, the dots and the width: The_pill_pads_its_row_..., In_the_main_chat_the_pill_is_regular_glass_..., the working dots tests. The record's "44" is NOT proven: see the comment
    public void An_empty_pill_is_46_points_high_8_4_22_4_8_and_stays_so_with_the_keyboard()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            // SOURCE FACT (arithmetic): the text view's height is ceil(max(18, content) + 2 * 2) = 22 (ComposerTextView.swift:374, textContainerInset
            // (0, 2) at :249), which is above the 20 the column starts at (ComposerView.swift:552, :554), so the text column is 22 + 4 + 4 = 30 and
            // the pill is 30 + 8 + 8 = 46. The comment at :157 and the record say 44, which is the column before its first measure.
            // SOURCE FACT (VIDEO): the pill's outline is 46 points from rim to rim at C 0.5 (empty and idle) and at C 84.5 (one typed line), measured
            // on the extracted frames. A pill whose thread the model does not hold has no controls (:160); with them the 28 point send disc, bottom
            // aligned, is shorter than the text column and changes nothing.
            var (view, window, _, _) = OnScreen(h, Unresolved(h), null);
            var height = h.Main.OnMain(() => view.Bounds.Height);
            Assert.True(Math.Abs(height - 46) < 0.01, $"the empty pill is {height} high");
            Assert.Equal(22, h.Main.OnMain(() => One<ComposerTextView>(view).Bounds.Height), 3);
            h.Main.OnMain(() => One<ComposerNSTextView>(view).Focus());
            Pump(h, window);
            Assert.Equal(46, h.Main.OnMain(() => view.Bounds.Height), 3);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:143-145, :1293-1307 (the text view fills the pill's text area)
    [Trait("Rule", "R-TEST-4")] // The height and the paddings: the test above
    public void A_press_in_the_empty_part_of_the_text_area_focuses_the_field()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var (view, window, other, _) = OnScreen(h, Unresolved(h), null, takesFocusOnAppear: false);
            h.Main.OnMain(() => { other.Focus(); Settle(window); });
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.False(h.Main.OnMain(() => box.IsFocused));
            var frame = h.Main.OnMain(() => Frame(box, window));
            // Well to the right of where any text would be, inside the text area.
            var at = new Point(frame.Right - 20, frame.Center.Y);
            h.Main.OnMain(() =>
            {
                Assert.True(frame.Width > 100, $"the text area is wide: {frame}");
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                Settle(window);
            });
            Pump(h, window);
            Assert.True(h.Main.OnMain(() => box.IsFocused));
            Close(h, window);
        });
    }

    // ---- UI-COMPOSER-011 (:627-646; Components.swift:57-97) ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-011")] // App/Views/Composer/ComposerView.swift:634-638; UI/Common/Components.swift:57-97
    [Trait("Rule", "R-TEST-4")] // The padding, the active plate, the focus, the tooltip and the setting: The_attachment_chip_follows_external_..., B3_attach_action_uses_the_setting_...
    public void The_paperclip_reads_secondary_at_rest_and_tint_while_open_with_a_hover_plate_of_7_percent_over_0_12_s_and_pressed_plates_of_12_and_26_percent()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presenterSeam = Presentation.Presenter.Override(presenter);
            var recents = new ObservableBoolBox();
            var controls = h.Main.OnMain(() => new ComposerControls(h.Runtime, h.Thread, true, true, false, false, null,
                recents.AsBinding(), _ => { }, () => { }, () => { }));
            var window = h.Main.OnMain(() => Show(controls, h.App, 400));
            Pump(h, window);
            var button = h.Main.OnMain(() => Named(controls, "Attach files"));
            var plate = h.Main.OnMain(() => Assert.IsType<Border>(button.Content));
            Color Fill() => Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color;
            Color Ink() => Assert.IsAssignableFrom<ISolidColorBrush>((IBrush?)plate.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty)).Color;
            var theme = h.Main.OnMain(() => plate.ActualThemeVariant);
            h.Main.OnMain(() =>
            {
                Assert.Equal(Chrome.SecondaryText.Resolve(theme), Ink()); // secondary at rest
                Assert.Equal(Chrome.PrimaryText.Opacity(0).Resolve(theme), Fill());
                var glyph = One<SwiftImage>(button);
                Assert.Equal("paperclip", glyph.SymbolName);
                var font = glyph.GetValue(SwiftImage.FontProperty)!.Resolve();
                Assert.Equal(12, font.FontSize); // Chrome.iconFont: .callout, semibold
                Assert.Equal(FontWeight.SemiBold, font.FontWeight);
            });
            var centre = h.Main.OnMain(() => button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value);
            var frame = h.Main.OnMain(() => Frame(button, window));
            // The hover plate eases in over 0.12 s.
            h.Main.OnMain(() => { window.MouseMove(centre); Settle(window); });
            Assert.Equal(Chrome.PrimaryText.Opacity(0).Resolve(theme), h.Main.OnMain(Fill)); // not yet
            h.Main.Advance(TimeSpan.FromSeconds(0.06));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.06)));
            var half = h.Main.OnMain(Fill).A;
            var target = Chrome.PrimaryText.Opacity(0.07).Resolve(theme).A;
            var expected = target * Animation.EaseOut(0.12).Progress(0.06);
            Assert.True(half > 0 && half < target && Math.Abs(half - expected) <= 2, $"mid-hover plate {half}, expected {expected}, target {target}");
            h.Main.Advance(TimeSpan.FromSeconds(0.1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.1)));
            Assert.Equal(Chrome.PrimaryText.Opacity(0.07).Resolve(theme), h.Main.OnMain(Fill));
            // Pressed: primary at 0.12 (no animation); released over it: the click toggles the popover, so the chip is active.
            h.Main.OnMain(() => { window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton); Settle(window); });
            Assert.Equal(Chrome.PrimaryText.Opacity(0.12).Resolve(theme), h.Main.OnMain(Fill));
            h.Main.OnMain(() => { window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None); Settle(window); });
            Pump(h, window);
            Assert.True(h.Main.OnMain(() => recents.Value)); // the popover is open
            h.Main.Advance(TimeSpan.FromSeconds(0.2));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.2)));
            // Open: the tint, and a tint plate (hovering does not add to it).
            var tint = Chrome.Accent;
            Assert.Equal(tint.Resolve(theme), h.Main.OnMain(Ink));
            Assert.Equal(tint.Opacity(0.16).Resolve(theme), h.Main.OnMain(Fill));
            h.Main.OnMain(() => { window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton); Settle(window); });
            Assert.Equal(tint.Opacity(0.26).Resolve(theme), h.Main.OnMain(Fill)); // active and pressed
            h.Main.OnMain(() => { window.MouseMove(new Point(frame.X - 1, frame.Y - 1)); window.MouseUp(new Point(frame.X - 1, frame.Y - 1), MouseButton.Left, RawInputModifiers.None); Settle(window); });
            // contentShape(.capsule): the rectangle's corner is not the button.
            h.Main.OnMain(() =>
            {
                Assert.False(Reaches(window.InputHitTest(new Point(frame.X + 0.5, frame.Y + 0.5)) as Visual, button), "the corner outside the capsule");
                Assert.True(Reaches(window.InputHitTest(frame.Center) as Visual, button), "the middle");
            });
            Close(h, window);
        });
    }
}
