using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.App.Port.Voice;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pickerpill-1")]
public sealed class ComposerVoicePillT46Tests(HeadlessSession headless)
{
    [Theory]
    [InlineData(600, 26)]
    [InlineData(600, 32)]
    [InlineData(900, 26)]
    [InlineData(900, 32)]
    public void Word_sets_language_and_preparation_keep_the_inline_pill_inside_the_composer(double width, int diameter) => headless.OnUiThread(() =>
    {
        using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { OrbSize = diameter });
        var (view, window) = rig.Open(width);
        try
        {
            var pill = Pill(view);
            rig.Main.OnMain(() => pill.Open = true);
            foreach (var words in Enum.GetValues<VoicePillWords>())
                foreach (var state in Enum.GetValues<VoiceLabelState>())
                {
                    rig.Main.OnMain(() => { pill.Words = words; pill.State = state; pill.Language = "Portuguese (Brazil)"; });
                    Run(rig, window, .8);
                    var rect = rig.Main.OnMain(() => PillRect(view));
                    Assert.True(rect.Left >= 0 && rect.Right <= view.Bounds.Width, rect + " in width " + view.Bounds.Width);
                    Assert.True(rect.Top >= 0 && rect.Bottom <= view.Bounds.Height, rect + " in height " + view.Bounds.Height);
                }
            rig.Main.OnMain(() => pill.ShowPreparation(true, new VoicePrepItem(VoicePreparation.DownloadId, "Getting voice ready", .5, VoicePrepState.Running) { ModelId = "tiny-q5_1" }));
            Run(rig, window, .8);
            var preparation = rig.Main.OnMain(() => PillRect(view));
            Assert.True(preparation.Left >= 0 && preparation.Right <= view.Bounds.Width);
            Assert.True(preparation.Top >= 0 && preparation.Bottom <= view.Bounds.Height);
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void Inline_pill_is_centred_inside_the_composer_on_every_open_and_close_frame() => headless.OnUiThread(() =>
    {
        using var rig = ComposerVoicePillGeometryTests.Quiet();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var opening = Frames(rig, window, 40, () => (Rect: PillRect(view), Height: view.Bounds.Height));
            rig.Main.OnMain(() => view.Voice!.Cancel());
            var closing = Frames(rig, window, 40, () => (Rect: PillRect(view), Height: view.Bounds.Height));
            foreach (var frame in opening.Concat(closing))
            {
                Assert.True(frame.Rect.Top >= 3 - 0.1, frame.Rect.ToString());
                Assert.True(frame.Rect.Bottom <= frame.Height - 3 + 0.1, frame.Rect + " in " + frame.Height);
                Assert.True(frame.Rect.Left >= 0);
                Assert.True(frame.Rect.Right <= view.Bounds.Width);
            }
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void Changing_the_saved_style_during_a_session_does_not_jump_the_anchor_on_cancel() => headless.OnUiThread(() =>
    {
        using var rig = ComposerVoicePillGeometryTests.Quiet();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); Run(rig, window, .8);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { ListeningStyle = VoiceListeningStyle.Float });
            Run(rig, window, .2);
            var before = rig.Main.OnMain(() => OrbCentre(view).Y);
            rig.Main.OnMain(() => view.Voice!.Cancel());
            var seen = Frames(rig, window, 50, () => OrbCentre(view).Y);
            Assert.InRange(Math.Abs(seen[0] - before), 0, .2);
            foreach (var step in seen.Zip(seen.Skip(1), (a, b) => Math.Abs(b - a))) Assert.InRange(step, 0, .2);
        }
        finally { rig.Close(window); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delayed_paint_keeps_the_text_inset_and_pill_width_on_the_same_curve(bool paintFirst) => headless.OnUiThread(() =>
    {
        using var rig = ComposerVoicePillGeometryTests.Quiet();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            if (paintFirst) rig.Elapse(window, FrameTime);
            rig.Main.Advance(TimeSpan.FromMilliseconds(500));
            var samples = Frames(rig, window, 40, () =>
            {
                var pill = Pill(view);
                var column = LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single();
                var widthProgress = (pill.ShownWidth - 40) / (pill.OpenWidth - 40);
                var target = VoicePillGeometry.TextRightInset(VoiceListeningStyle.Pill, true, Slot(view).Diameter, pill.OpenWidth);
                return (Width: widthProgress, Inset: column.Margin.Right / target);
            });
            foreach (var sample in samples) Assert.Equal(sample.Width, sample.Inset, .002);
            rig.Main.OnMain(() => view.Voice!.Cancel());
            if (paintFirst) rig.Elapse(window, FrameTime);
            rig.Main.Advance(TimeSpan.FromMilliseconds(500));
            var closing = Frames(rig, window, 40, () =>
            {
                var pill = Pill(view);
                var column = LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single();
                var target = VoicePillGeometry.TextRightInset(VoiceListeningStyle.Pill, true, Slot(view).Diameter, pill.OpenWidth);
                return (Width: (pill.ShownWidth - 40) / (pill.OpenWidth - 40), Inset: column.Margin.Right / target);
            });
            foreach (var sample in closing) Assert.Equal(sample.Width, sample.Inset, .002);
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void A_delayed_later_frame_does_not_skip_the_open_or_close_curve() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (pill, window) = Standalone(rig);
        try
        {
            rig.Main.OnMain(() => pill.Open = true);
            rig.Elapse(window, FrameTime);
            var first = pill.ShownWidth;
            rig.Main.Advance(TimeSpan.FromMilliseconds(500));
            rig.Elapse(window, FrameTime);
            Assert.InRange(pill.ShownWidth, first + .01, pill.OpenWidth - 10);
            Run(rig, window, .6);
            rig.Main.OnMain(() => pill.Open = false);
            rig.Elapse(window, FrameTime);
            var closing = pill.ShownWidth;
            rig.Main.Advance(TimeSpan.FromMilliseconds(500));
            rig.Elapse(window, FrameTime);
            Assert.InRange(pill.ShownWidth, 50, closing - .01);
            Assert.True(pill.ShownOpacity > 0);
            Run(rig, window, .6);
            Assert.Equal(40, pill.ShownWidth);
            Assert.Equal(0, pill.ShownOpacity);
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void A_delayed_first_frame_does_not_consume_the_open_or_close_animation() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (pill, window) = Standalone(rig);
        try
        {
            rig.Main.OnMain(() => pill.Open = true);
            rig.Main.Advance(TimeSpan.FromMilliseconds(500)); // preparation stalled main; no frame has been drawn
            rig.Elapse(window, FrameTime);
            Assert.InRange(pill.ShownWidth, 40.01, pill.OpenWidth - 1);
            Run(rig, window, .6);
            var full = pill.ShownWidth;
            rig.Main.OnMain(() => pill.Open = false);
            rig.Main.Advance(TimeSpan.FromMilliseconds(500));
            rig.Elapse(window, FrameTime);
            Assert.InRange(pill.ShownWidth, 40.01, full - .01);
            Assert.True(pill.ShownOpacity > 0);
            Run(rig, window, .6);
            Assert.Equal(40, pill.ShownWidth);
            Assert.Equal(0, pill.ShownOpacity);
        }
        finally { rig.Close(window); }
    });
}
