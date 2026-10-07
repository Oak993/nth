using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Countdown;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Port.Voice.Countdown;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class RenderedVoiceCountdownTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(VoiceListeningStyle.Pill, 32, false, 1)]
    [InlineData(VoiceListeningStyle.Pill, 26, true, 1.25)]
    [InlineData(VoiceListeningStyle.Pill, 32, true, 1.5)]
    [InlineData(VoiceListeningStyle.Pill, 26, false, 2)]
    [InlineData(VoiceListeningStyle.Float, 26, false, 1)]
    [InlineData(VoiceListeningStyle.Float, 32, true, 1.25)]
    [InlineData(VoiceListeningStyle.Float, 26, true, 1.5)]
    [InlineData(VoiceListeningStyle.Float, 32, false, 2)]
    public void The_mounted_footer_paints_only_inside_its_locked_rectangle_and_follows_the_shown_width(VoiceListeningStyle style, int size, bool dark, double scale)
    {
        headless.OnUiThread(() =>
        {
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = style, OrbSize = size });
            var (view, window) = rig.Open(1000);
            try
            {
                rig.Main.OnMain(() => { window.SetRenderScaling(scale); window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; view.Voice!.Activate(); });
                rig.PumpWindow(window);
                rig.Elapse(window, 275);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                rig.PumpWindow(window);
                var pill = Pill(view);
                var footer = LogicalQuery.SelfAndDescendants<VoiceCountdownFooter>(pill).Single();
                var origin = pill.TranslatePoint(default, window)!.Value;
                var box = new Rect((origin.X + 36) * scale, (origin.Y + pill.PillHeight - 7) * scale, footer.Bounds.Width * scale, 2 * scale);
                var frame = new Rect(Math.Floor(box.X) - 2, Math.Floor(box.Y) - 2, Math.Ceiling(box.Width) + 5, Math.Ceiling(box.Height) + 5);
                var on = PreviewPixels.Region(window, frame);
                VoiceEvidence.Capture(window, style + "-" + size + "-" + dark + "-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-on");
                rig.Main.OnMain(() => footer.Opacity = 0);
                var off = PreviewPixels.Region(window, frame);
                VoiceEvidence.Capture(window, style + "-" + size + "-" + dark + "-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-off");
                var differences = on.Zip(off, (a, b) => (byte)Math.Abs(a - b)).ToArray();
                var bounds = PreviewPixels.InkBounds(differences, (int)frame.Width, 2, 0);
                Assert.True(bounds.Right > bounds.Left + 10 * scale, "The actual footer paints a substantial line.");
                Assert.InRange(frame.X + bounds.Left, box.X - 1, box.X + 2);
                Assert.InRange(frame.X + bounds.Right, box.Right - 3, box.Right + 1);
                Assert.InRange(frame.Y + bounds.Top, box.Y - 1, box.Y + 1);
                Assert.InRange(frame.Y + bounds.Bottom, box.Bottom - 2, box.Bottom + 1);
                rig.Main.OnMain(() => { footer.Opacity = 1; rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Labels = VoicePillWords.L4 }; });
                rig.PumpWindow(window);
                for (var index = 0; index < 30; index++)
                {
                    rig.Elapse(window, 1.0 / 60);
                    Assert.Equal((pill.ShownWidth - 78) * view.Voice!.Session.CapCountdownRemaining!.Value.TotalSeconds / 30, footer.Bounds.Width, 5);
                    Assert.Equal(36, footer.TranslatePoint(default, pill)!.Value.X, 6);
                }
                rig.Main.OnMain(() => window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark);
                rig.Elapse(window, 1.0 / 60);
                var palette = dark ? CountdownPalette.Light : CountdownPalette.Dark;
                Assert.Equal((byte)Math.Round(palette.Neutral.R * 255), footer.Background!.Color.R);
                rig.Main.OnMain(() => view.Voice!.Cancel());
                rig.Elapse(window, .1);
                Assert.Equal(1 - CssCurves.Ease(1.0 / 3), footer.Opacity, .002);
                rig.Elapse(window, .3);
                Assert.Equal(0, footer.Opacity);
            }
            finally { rig.Close(window); }
        });
    }

    [Fact]
    public void Detach_cancels_the_old_announcements_and_reattach_has_one_fresh_countdown()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            try
            {
                var heard = new List<string>();
                var announcer = LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single();
                announcer.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) heard.Add(announcer.Text ?? ""); };
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 280);
                var host = Marrow.App.Tests.Views.Composer.ComposerViewTests.Host(window);
                rig.Main.OnMain(() => host.Child = null);
                rig.Elapse(window, 25);
                Assert.DoesNotContain("10 seconds left.", heard);
                rig.Main.OnMain(() => host.Child = view);
                rig.PumpWindow(window);
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 270.4);
                Assert.Equal(2, heard.Count(word => word == "30 seconds left."));
            }
            finally { rig.Close(window); }
        });
    }
}
