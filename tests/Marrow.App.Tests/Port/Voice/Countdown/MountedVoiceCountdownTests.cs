using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Voice;
using Marrow.App.Port.Voice.Countdown;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Port.Voice.Countdown;

// PORT-ONLY: OD-0106 and locked vn-page.css .drn, read from the mounted control.
[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoiceCountdownTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void The_actual_footer_starts_at_270_seconds_and_uses_the_locked_inside_geometry(bool dark, bool reduced)
    {
        headless.OnUiThread(() =>
        {
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduced });
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            try
            {
                rig.Main.OnMain(() => window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light);
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 269.99);
                var pill = Pill(view);
                var line = Footer(pill);
                Assert.Equal(0, line.Opacity);
                var words = pill.Labels.Select(label => label.Words).ToArray();
                rig.Elapse(window, .01);
                Assert.Equal(0, line.Opacity); // Locked .drn opacity .3s ease: arrival begins from zero.
                rig.Elapse(window, .3);
                var frame = line.TranslatePoint(default, pill)!.Value;
                Assert.Equal(36, frame.X, 6);
                Assert.Equal(pill.PillHeight - 7, frame.Y, 6);
                Assert.Equal(2, line.Bounds.Height, 6);
                Assert.Equal((pill.Bounds.Width - 36 - 42) * (reduced ? 1 : 29.7 / 30), line.Bounds.Width, 6);
                Assert.Equal(new CornerRadius(1), line.CornerRadius);
                Assert.Equal(1, line.Opacity, 6);
                Assert.Equal(words, pill.Labels.Select(label => label.Words));
                Assert.Empty(LogicalQuery.SelfAndDescendants<TextBlock>(line)); // No countdown digits.
                var ink = Assert.IsAssignableFrom<ISolidColorBrush>(line.Background).Color;
                var palette = dark ? CountdownPalette.Dark : CountdownPalette.Light;
                Assert.Equal(ToByte(palette.Neutral.A), ink.A);
                Assert.Equal(ToByte(palette.Neutral.R), ink.R);
                Assert.Equal(ToByte(palette.Neutral.G), ink.G);
                Assert.Equal(ToByte(palette.Neutral.B), ink.B);
            }
            finally { rig.Close(window); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void The_mounted_colour_matches_the_independent_60_Hz_curve_through_yellow_and_red(bool dark, bool reduced)
    {
        headless.OnUiThread(() =>
        {
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduced });
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            try
            {
                rig.Main.OnMain(() => window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light);
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 284.9);
                var line = Footer(Pill(view));
                var palette = dark ? CountdownPalette.Dark : CountdownPalette.Light;
                for (var sample = 0; sample <= 696; sample++)
                {
                    var remaining = view.Voice!.Session.CapCountdownRemaining!.Value.TotalSeconds;
                    var expected = CountdownReference.Expected(remaining, reduced, palette).Colour;
                    var actual = Assert.IsAssignableFrom<ISolidColorBrush>(line.Background).Color;
                    Assert.Equal(ToByte(expected.A), actual.A);
                    Assert.Equal(ToByte(expected.R), actual.R);
                    Assert.Equal(ToByte(expected.G), actual.G);
                    Assert.Equal(ToByte(expected.B), actual.B);
                    var fraction = (reduced ? Math.Ceiling(remaining) : remaining) / 30;
                    Assert.Equal((Pill(view).Bounds.Width - 78) * fraction, line.Bounds.Width, 5);
                    rig.Elapse(window, 1.0 / 60);
                }
            }
            finally { rig.Close(window); }
        });
    }

    [Theory]
    [InlineData(VoiceListeningStyle.Pill)]
    [InlineData(VoiceListeningStyle.Float)]
    [InlineData(VoiceListeningStyle.Cross)]
    public void The_actual_polite_region_announces_once_at_30_and_10_then_the_cap_finishes(VoiceListeningStyle style)
    {
        headless.OnUiThread(() =>
        {
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = style });
            var (view, window) = rig.Open(900);
            try
            {
                var announcer = LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single();
                var heard = new List<string>();
                announcer.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) heard.Add(announcer.Text ?? ""); };
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 269.99);
                Assert.DoesNotContain("30 seconds left.", heard);
                rig.Elapse(window, .01);
                Assert.Single(heard, text => text == "30 seconds left.");
                rig.Elapse(window, 19.99);
                Assert.DoesNotContain("10 seconds left.", heard);
                // Advance exact ticks to the session's 10s boundary; decimal double→TimeSpan conversion truncates19.99 by one tick.
                var untilTen = view.Voice!.Session.CapCountdownRemaining!.Value - TimeSpan.FromSeconds(10);
                rig.Main.Advance(untilTen);
                rig.Main.OnMain(() => rig.Frames.Advance(untilTen));
                rig.PumpWindow(window);
                Assert.Equal(TimeSpan.FromSeconds(10), view.Voice.Session.CapCountdownRemaining);
                Assert.Single(heard, text => text == "10 seconds left.");
                rig.Elapse(window, 10.1);
                Assert.Equal(VoiceStopReason.Cap, view.Voice!.Session.LastStopReason);
                Assert.NotEqual(VoiceSessionState.Listening, view.Voice.State);
                Assert.Single(heard, text => text == "30 seconds left.");
                Assert.Single(heard, text => text == "10 seconds left.");
                // V-15's empty/no-speech cap follows the same Rest path as a click, without a decoding pass.
                Assert.Equal(VoiceSessionState.Rest, view.Voice.State);
                Assert.Equal("noSpeech", view.Voice.Note!.Id);
            }
            finally { rig.Close(window); }
        });
    }

    [Fact]
    public void Cancel_before_ten_seconds_suppresses_the_late_announcement_and_a_new_session_starts_cleanly()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Marrow.App.Tests.Views.Composer.ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            try
            {
                var announcer = LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view).Single();
                var heard = new List<string>();
                announcer.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) heard.Add(announcer.Text ?? ""); };
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 280);
                rig.Main.OnMain(() => view.Voice!.Cancel());
                rig.Elapse(window, 25);
                Assert.DoesNotContain("10 seconds left.", heard);
                Assert.Equal(0, Footer(Pill(view)).Opacity);
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Elapse(window, 270.4);
                Assert.Equal(2, heard.Count(text => text == "30 seconds left."));
                Assert.InRange(Footer(Pill(view)).Bounds.Width / (Pill(view).Bounds.Width - 78), .98, 1);
            }
            finally { rig.Close(window); }
        });
    }

    private static VoiceCountdownFooter Footer(Control pill) => LogicalQuery.SelfAndDescendants<VoiceCountdownFooter>(pill).Single(control => control.Name == "VoiceCountdown");
    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
