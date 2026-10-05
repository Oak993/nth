using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T85 (voice-SPEC V-13.4; OD-0097 B1 to B3): the composer light goes to voiceOrb.lightVoice's level during Listening, Processing and Landing in a real composer (a ComposerArea, which carries the light's slot),
// for each of away (0), dim (0.35) and stay (1), and returns to 1 after Landing. The light's own follower (VT-9) eases to the level it is given; what the composer owes it is the level, at once, on every change of state
// or setting. And V-13.3: the voice glow in the same composer is active while the session listens or finishes, processing only in Processing, and the raw level is what the session hears.
[Collection("Headless")]
public sealed class ComposerVoiceLightTests(HeadlessSession headless)
{
    private static (ComposerArea Area, ComposerView View, ComposerLightSlot Slot, Avalonia.Controls.Window Window) Open(VoiceComposerRig rig, double width = 900)
    {
        var area = rig.Main.OnMain(() =>
        {
            var made = new ComposerArea(rig.Runtime, workingDirectory: null);
            AppEnvironment.SetAppModel(made, rig.App);
            return made;
        });
        var window = rig.Main.OnMain(() => Show(area, rig.App, width));
        rig.PumpWindow(window);
        return (area, One<ComposerView>(area), One<ComposerLightSlot>(area), window);
    }

    private static VoiceScript Said() => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8),
        new ScriptedWord("please.", 2.0, 2.5),
    ]);

    [Theory] // V-T85
    [InlineData(VoiceLightVoice.Away, 0.0)]
    [InlineData(VoiceLightVoice.Dim, 0.35)]
    [InlineData(VoiceLightVoice.Stay, 1.0)]
    [Trait("Decision", "ED-292")]
    public void The_light_goes_to_the_settings_level_while_the_voice_is_on_and_back_to_one_after_landing(VoiceLightVoice setting, double level)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { LightVoice = setting });
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            var script = Said();
            rig.Speak(script);
            rig.Engine = script.CreateEngine(TimeSpan.FromSeconds(1.5)); // the final pass takes a while, so Processing can be seen
            var (_, view, slot, window) = Open(rig);
            Assert.Equal(1, rig.Main.OnMain(() => slot.Light.VoiceLight)); // at rest
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var seen = new HashSet<VoiceSessionState>();
            void Check()
            {
                var state = rig.Main.OnMain(() => view.Voice!.State);
                var voiceOn = state is VoiceSessionState.Listening or VoiceSessionState.Processing or VoiceSessionState.Landing;
                seen.Add(state);
                Assert.Equal(voiceOn ? level : 1, rig.Main.OnMain(() => slot.Light.VoiceLight), 1e-9);
            }
            for (var frame = 0; frame < 120; frame++)
            {
                rig.Elapse(window, FrameTime);
                if (frame > 2) Check();
            }
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the second click: Processing
            for (var frame = 0; frame < 600 && rig.Main.OnMain(() => view.Voice!.State) != VoiceSessionState.Rest; frame++)
            {
                rig.Elapse(window, FrameTime);
                if (frame > 2) Check();
            }
            Run(rig, window, 0.2);
            Check();
            Assert.Contains(VoiceSessionState.Listening, seen);
            Assert.Contains(VoiceSessionState.Processing, seen);
            Assert.Contains(VoiceSessionState.Landing, seen);
            Assert.Equal(1, rig.Main.OnMain(() => slot.Light.VoiceLight)); // returned
            rig.Close(window);
        });
    }

    [Fact] // V-T85, V-13.4: a change of the setting during a session retargets at once
    [Trait("Decision", "ED-292")]
    public void A_change_of_the_setting_during_a_session_retargets_the_light_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { LightVoice = VoiceLightVoice.Away });
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            rig.QuietRoom();
            var (_, view, slot, window) = Open(rig);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 0.3);
            Assert.Equal(0, rig.Main.OnMain(() => slot.Light.VoiceLight));
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LightVoice = VoiceLightVoice.Dim });
            rig.PumpWindow(window);
            Assert.Equal(0.35, rig.Main.OnMain(() => slot.Light.VoiceLight), 1e-9);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { LightVoice = VoiceLightVoice.Stay });
            rig.PumpWindow(window);
            Assert.Equal(1, rig.Main.OnMain(() => slot.Light.VoiceLight), 1e-9);
            rig.Close(window);
        });
    }

    [Fact] // V-T85, V-13.3: the glow in a real composer, over the glass and under the text and controls
    [Trait("Decision", "ED-292")]
    public void The_glow_is_active_while_listening_and_finishing_and_sweeps_in_processing_only_under_the_content()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
            var script = Said();
            rig.Speak(script);
            rig.Engine = script.CreateEngine(TimeSpan.FromSeconds(1.5));
            var (_, view, _, window) = Open(rig);
            var glow = rig.Main.OnMain(() => Glow(view));
            rig.Main.OnMain(() =>
            {
                Assert.False(glow.Glow.Active);
                // it stands in the composer's content layers, above the working dots, under the pill's layer and the text and controls
                var layers = (Grid)glow.Parent!;
                Assert.True(layers.Children.IndexOf(glow) < layers.Children.IndexOf(layers.Children.OfType<Marrow.App.Port.Voice.VoicePillSlot>().Single()));
                Assert.True(layers.Children.IndexOf(layers.Children.OfType<Marrow.App.Port.Voice.VoicePillSlot>().Single()) < layers.Children.Count - 1); // the padded content is the last
                Assert.Equal(22, glow.Glow.Radius);
            });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1.5);
            rig.Main.OnMain(() =>
            {
                Assert.True(glow.Glow.Active);
                Assert.False(glow.Glow.Processing);
                Assert.True(glow.Glow.Level.Value > 0); // the scripted capture's tone is heard
            });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 0.3);
            rig.Main.OnMain(() =>
            {
                Assert.True(glow.Glow.Active);
                Assert.True(glow.Glow.Processing);
                Assert.Equal(0, glow.Glow.Level.Value); // the level source is 0 after the capture has stopped
            });
            Run(rig, window, 4);
            rig.Main.OnMain(() =>
            {
                Assert.False(glow.Glow.Active);
                Assert.False(glow.Glow.Processing);
            });
            rig.Close(window);
        });
    }
}
