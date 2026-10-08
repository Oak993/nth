using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using Chrome = Marrow.UI.Chrome.Chrome; // the Swift type Chrome, not this namespace
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10, V-T70 (voice-SPEC V-14.1; OD-0091 and OD-0086: the owner's ruling that the attachment button moves to the composer's left side): inside the composer
// 34 x 28 at left 8 with the text inset 48; outside, a 36 x 36 circle in the margin with the composer 44 narrower and the inset 14; switching eases (OD-0076);
// the recent-downloads popover and the chip's active state go with the visible button. The move does not read VoiceFeature.Enabled.
[Collection("Headless")]
public sealed class ComposerAttachmentPlaceTests(HeadlessSession headless)
{
    private static VoiceOrbSettings Outside => VoiceOrbSettings.Default with { Attachment = VoiceAttachment.Outside };

    private static int AttachButtons(Control root) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Count(button => AutomationProperties.GetName(button) == "Attach files");

    [Fact] // V-T70
    [Trait("Rule", "R-TEST-10")]
    public void Inside_the_button_is_34_by_28_at_left_8_on_the_controls_row_and_the_text_starts_at_48()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (view, window) = rig.Open(600);
            rig.Main.OnMain(() =>
            {
                var attach = Named(view, "Attach files");
                var frame = Frame(attach, view);
                Assert.Equal(34, frame.Width, 0.5);
                Assert.Equal(28, frame.Height, 0.5);
                Assert.Equal(8, frame.X, 0.5);
                var controls = One<ComposerControls>(view);
                Assert.Equal(Frame(controls, view).Bottom, frame.Bottom, 0.5); // aligned with the bottom controls row
                Assert.DoesNotContain(attach, LogicalQuery.SelfAndDescendants<Button>(controls)); // it moved out of ComposerControls
                Assert.Equal(48, Frame(One<ComposerTextColumn>(view), view).X, 0.5);
                Assert.Equal(600, view.Bounds.Width, 0.5);
                Assert.Equal(1, AttachButtons(view));
            });
            rig.Close(window);
        });
    }

    [Theory] // V-T70; OD-0132 replaces only the former height-22 single-line centre oracle.
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [Trait("Rule", "R-TEST-10")]
    public void Outside_it_is_a_36_circle_in_the_margin_the_composer_gives_it_44_and_the_text_starts_at_14(double scale)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(Outside);
            var (view, window) = rig.Open(600);
            rig.Main.OnMain(() => window.SetRenderScaling(scale));
            rig.PumpWindow(window);
            rig.Main.OnMain(() =>
            {
                var attach = Named(view, "Attach files");
                var frame = Frame(attach, view);
                Assert.Equal(36, frame.Width, 0.5);
                Assert.Equal(36, frame.Height, 0.5);
                Assert.Equal(-44, frame.X, 0.5); // the margin is the 44 the composer gave up: 36 and 8 to the pill
                Assert.Equal(view.Bounds.Height / 2, frame.Center.Y, 0.5 / scale); // owner OD-0132: within half a physical pixel of the unchanged outline centre
                Assert.Equal(44, Frame(view, Host(window)).X, 0.5);
                Assert.Equal(556, view.Bounds.Width, 0.5);
                Assert.Equal(14, Frame(One<ComposerTextColumn>(view), view).X, 0.5);
                Assert.Equal(1, AttachButtons(view));
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T70, OD-0076: nothing cuts
    [Trait("Rule", "R-VIEW-4")]
    public void Switching_the_place_eases_the_composers_width_the_text_inset_and_the_two_buttons()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (view, window) = rig.Open(600);
            double Left() => rig.Main.OnMain(() => Frame(view, Host(window)).X);
            double Inset() => rig.Main.OnMain(() => Frame(One<ComposerTextColumn>(view), view).X);
            Assert.Equal(0, Left(), 0.5);
            Assert.Equal(48, Inset(), 0.5);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Attachment = VoiceAttachment.Outside });
            rig.PumpWindow(window);
            var lefts = new List<double> { Left() };
            var insets = new List<double> { Inset() };
            var firstFrame = true;
            for (var frame = 0; frame < 40; frame++)
            {
                rig.Elapse(window, 1.0 / 60);
                lefts.Add(Left());
                insets.Add(Inset());
                if (!firstFrame) continue;
                firstFrame = false;
                Assert.Equal(2, rig.Main.OnMain(() => AttachButtons(view))); // both buttons are there while one fades out and the other in
            }
            Assert.Equal(44, lefts[^1], 0.5);
            Assert.Equal(14, insets[^1], 0.5);
            Assert.Equal(1, rig.Main.OnMain(() => AttachButtons(view))); // the one that left is gone
            Assert.Contains(lefts, left => left is > 1 and < 43); // it travelled: no single cut
            Assert.Contains(insets, inset => inset is > 15 and < 47);
            // the pill's width follows the design's ez (cubic-bezier .32 .72 0 1) over 0.42 s, the text's inset the same curve over 0.34 s (CZ-14.1); +1 px: layout rounds both
            var width = Animation.TimingCurve(0.32, 0.72, 0, 1, 0.42);
            var inset = Animation.TimingCurve(0.32, 0.72, 0, 1, 0.34);
            for (var i = 1; i < lefts.Count; i++)
            {
                Assert.True(lefts[i] >= lefts[i - 1] - 0.001, $"the width only grows: {lefts[i - 1]} then {lefts[i]}");
                Assert.True(insets[i] <= insets[i - 1] + 0.001, $"the inset only shrinks: {insets[i - 1]} then {insets[i]}");
                var t = i / 60.0;
                Assert.True(Math.Abs(lefts[i] - 44 * width.Progress(Math.Min(t, 0.42))) <= 1.01, $"the width at {t:F3} s is {lefts[i]}, the curve says {44 * width.Progress(Math.Min(t, 0.42)):F1}");
                Assert.True(Math.Abs(insets[i] - (14 + 34 * (1 - inset.Progress(Math.Min(t, 0.34))))) <= 1.01, $"the inset at {t:F3} s is {insets[i]}, the curve says {14 + 34 * (1 - inset.Progress(Math.Min(t, 0.34))):F1}");
            }
            rig.Close(window);
        });
    }

    [Fact] // V-T70, OD-0076: the two buttons' fade (0.28 s ease) and scale (0.6 to 1 over 0.34 s ez), every frame against the curve
    [Trait("Rule", "R-VIEW-4")]
    public void The_two_buttons_cross_over_on_the_fade_and_scale_curves_frame_by_frame()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (view, window) = rig.Open(600);
            var start = rig.Main.OnMain(() => SwiftClock.Now);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Attachment = VoiceAttachment.Outside });
            rig.PumpWindow(window);
            for (var frame = 0; frame < 20; frame++)
            {
                rig.Elapse(window, 1.0 / 60);
                var t = rig.Main.OnMain(() => (SwiftClock.Now - start).TotalSeconds); // the clock's own time: a frame is a whole number of 100 ns ticks
                var buttons = rig.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Select(b => (b.Place, b.Opacity, ((ScaleTransform)b.RenderTransform!).ScaleX)).ToList());
                if (buttons.Count != 2) break; // the one that left is gone
                var fade = CssCurves.At(CssCurves.Ease, t, 0.28);
                var grow = CssCurves.At(CssCurves.Ez, t, 0.34);
                var outside = buttons.Single(b => b.Place == ComposerAttachPlace.Outside);
                var inside = buttons.Single(b => b.Place == ComposerAttachPlace.Inside);
                Assert.True(Math.Abs(outside.Opacity - fade) < 1e-4, $"the arriving button's opacity at {t:F3} s is {outside.Opacity}, the curve says {fade}");
                Assert.True(Math.Abs(inside.Opacity - (1 - fade)) < 1e-4, $"the leaving button's opacity at {t:F3} s is {inside.Opacity}, the curve says {1 - fade}");
                Assert.True(Math.Abs(outside.ScaleX - (0.6 + 0.4 * grow)) < 1e-4, $"the arriving button's scale at {t:F3} s is {outside.ScaleX}, the curve says {0.6 + 0.4 * grow}");
                Assert.True(Math.Abs(inside.ScaleX - (0.6 + 0.4 * (1 - grow))) < 1e-4, $"the leaving button's scale at {t:F3} s is {inside.ScaleX}, the curve says {0.6 + 0.4 * (1 - grow)}");
            }
            rig.Close(window);
        });
    }

    [Fact] // V-T70: the popover and chip state move with it
    [Trait("Rule", "R-TEST-10")]
    public void The_recent_downloads_popover_and_the_active_chip_follow_the_visible_button()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var downloads = rig.Store.NewFolder("downloads-vt10");
            File.WriteAllText(Path.Combine(downloads, "report.txt"), "report");
            using var folder = RecentDownloads.DirectoryProvider.Override(() => downloads);
            var (view, window) = rig.Open(600);
            var inside = rig.Main.OnMain(() => Named(view, "Attach files"));
            rig.Main.OnMain(() => Click(inside));
            rig.PumpWindow(window);
            var (popup, presented) = rig.Presenter.Calls[^1];
            Assert.True(presented, "the click opened the popover");
            Assert.Same(inside, popup.PlacementTarget);
            Assert.Equal(PlacementMode.Bottom, popup.Placement);

            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Attachment = VoiceAttachment.Outside });
            rig.PumpWindow(window);
            rig.Elapse(window, 1.0);
            var outside = rig.Main.OnMain(() => Named(view, "Attach files"));
            Assert.NotSame(inside, outside);
            // the popover went over: the first button's closed, the one now showing opened, and it is the one that stands
            var calls = rig.Presenter.Calls.Select(call => (Inside: ReferenceEquals(call.Popup.PlacementTarget, inside), call.Presented)).ToList();
            Assert.False(calls.Last(call => call.Inside).Presented, "the first button's popover is closed");
            var outsideCalls = rig.Presenter.Calls.Where(call => ReferenceEquals(call.Popup.PlacementTarget, outside)).ToList();
            Assert.True(outsideCalls.Count > 0 && outsideCalls[^1].Presented, "still open, now from the visible button");
            var plate = rig.Main.OnMain(() => Assert.IsType<Border>(outside.Content));
            var accent = rig.Main.OnMain(() => Chrome.Accent.Opacity(0.16).Resolve(plate.ActualThemeVariant));
            Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color); // the chip reads active

            rig.Main.OnMain(() => Click(outside)); // a second click closes it
            rig.PumpWindow(window);
            Assert.False(rig.Presenter.Calls.Last(call => ReferenceEquals(call.Popup.PlacementTarget, outside)).Presented, "the second click closed it");
            rig.Close(window);
        });
    }

    [Fact] // V-T70 / V-T87: the owner-ruled move is not behind the feature flag
    [Trait("Rule", "R-TEST-10")]
    public void With_the_feature_off_the_attachment_still_sits_at_the_left_and_the_controls_do_not_hold_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            using var off = VoiceFeature.Switch.Override(new VoiceFeatureSwitch(false));
            var (view, window) = rig.Open(600);
            rig.Main.OnMain(() =>
            {
                var attach = Named(view, "Attach files");
                Assert.Equal(8, Frame(attach, view).X, 0.5);
                Assert.Equal(48, Frame(One<ComposerTextColumn>(view), view).X, 0.5);
                Assert.Equal(0, AttachButtons(One<ComposerControls>(view)));
            });
            rig.Close(window);
        });
    }
}
