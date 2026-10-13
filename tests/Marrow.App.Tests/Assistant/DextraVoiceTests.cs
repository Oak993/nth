using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 the global chord brings nth to the front, 12.6 Stop all cuts a voice reply, 16.2 "AS-18: the global chord brings nth to the front"): Dextra's voice
// as the host wires it. It starts with the controller and ends with it; its chord is registered while Dextra runs and released when it stops; a press of the chord brings the
// window forward and toggles the conversation; Stop all reaches the conversation through its scope. Every part is a double: no window, no microphone, no sound.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-9")]
public sealed class DextraVoiceTests
{
    [Fact]
    public void The_voice_starts_with_the_controller_registers_its_chord_and_releases_it_when_Dextra_stops()
    {
        using var rig = new DextraVoiceRig();
        var host = rig.Host;
        var setup = rig;
        var h = rig.Harness;

        h.Main.OnMain(() =>
        {
            Assert.Null(host.Voice);
            host.EnsureStarted();
            Assert.NotNull(host.Voice);
            Assert.Equal(["register Ctrl+Alt+Enter"], setup.Chord.Calls);
            Assert.True(host.Voice.Chord.Status.Works);

            host.Settings.Update(p => p with { On = false });

            Assert.Null(host.Voice);
            Assert.Equal(["register Ctrl+Alt+Enter", "unregister"], setup.Chord.Calls);
            Assert.Null(setup.Chord.Registered);
        });
    }

    [Fact]
    public void A_press_of_the_chord_brings_the_window_forward_and_starts_talking_and_another_press_ends_the_listening()
    {
        using var rig = new DextraVoiceRig();
        var host = rig.Host;
        var setup = rig;
        var h = rig.Harness;

        h.Main.OnMain(() =>
        {
            host.EnsureStarted();
            var conversation = host.Voice!.Conversation;

            setup.Chord.Press(VoiceChord.ChordId);

            Assert.Equal(["window forward", "dictation ended"], setup.Log);
            Assert.Equal(VoicePhase.Listening, conversation.Phase);
            Assert.True(setup.Capture.IsRunning);

            setup.Chord.Press(VoiceChord.ChordId); // nothing was said: the second press closes the microphone

            Assert.Equal(VoicePhase.Idle, conversation.Phase);
            Assert.False(setup.Capture.IsRunning);
            Assert.Equal(["window forward", "dictation ended", "window forward"], setup.Log);
        });
    }

    [Fact]
    public void Stop_all_reaches_the_conversation_through_the_scope_the_controller_was_given()
    {
        using var rig = new DextraVoiceRig();
        var host = rig.Host;
        var setup = rig;
        var h = rig.Harness;

        h.Main.OnMain(() =>
        {
            host.EnsureStarted();
            var conversation = host.Voice!.Conversation;
            conversation.Start(VoiceStart.Toggle);
            Assert.Equal(VoicePhase.Listening, conversation.Phase);

            var report = host.Controller!.Controls.StopAll(StopSource.Header);

            Assert.NotNull(report);
            Assert.Equal(VoicePhase.Idle, conversation.Phase);
            Assert.False(setup.Capture.IsRunning);
        });
    }

    [Fact]
    public void A_chord_another_app_holds_still_leaves_the_voice_working_and_says_so_in_the_status()
    {
        using var rig = new DextraVoiceRig();
        var host = rig.Host;
        var setup = rig;
        var h = rig.Harness;
        setup.Chord.Taken.Add(new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, PcHotkey.Return));

        h.Main.OnMain(() =>
        {
            host.EnsureStarted();

            Assert.False(host.Voice!.Chord.Status.Works);
            Assert.Equal("Ctrl+Alt+Enter is taken by another app. Change it.", host.Voice.Chord.Status.Line);
            host.Voice.Conversation.Start(VoiceStart.Toggle); // the header's Talk and the talk button still work
            Assert.Equal(VoicePhase.Listening, host.Voice.Conversation.Phase);
        });
    }

    [Fact]
    public void A_changed_talk_chord_is_registered_at_once_and_the_stop_chord_cannot_be_taken_for_it()
    {
        var defaults = new InMemorySettingsStore();
        using var rig = new DextraVoiceRig(defaults);
        var host = rig.Host;
        var setup = rig;
        var h = rig.Harness;

        h.Main.OnMain(() =>
        {
            host.EnsureStarted();

            host.Settings.Update(p => p with { TalkChord = "Ctrl+Alt+Home" });
            Assert.Equal(["register Ctrl+Alt+Enter", "unregister", "register Ctrl+Alt+Home"], setup.Chord.Calls);
            Assert.Equal("Ctrl+Alt+Home", host.Voice!.Chord.Status.Chord);

            host.Settings.Update(p => p with { TalkChord = "Ctrl+Alt+Esc" }); // the stop chord
            Assert.False(host.Voice.Chord.Status.Works);
            Assert.Equal(PcChordProblem.Clash, host.Voice.Chord.Status.Problem);
        });
    }

    [Fact]
    public void The_production_parts_are_local_and_bind_the_communications_microphone_and_the_PCs_own_voices()
    {
        var parts = new DextraVoiceParts();
        // nothing is opened here: the defaults are only made, never started
        Assert.IsType<WinRtSpeechSynth>(parts.Synth());
        Assert.IsType<WasapiSpeechPlayer>(parts.Player());
        Assert.IsType<Marrow.Services.Port.Voice.Capture.WasapiCapture>(parts.Capture());
        Assert.IsNotType<Marrow.Services.Port.Voice.Cloud.OpenAiTranscriber>(parts.Engine());
        Assert.Equal(new[] { PcHotkey.PauseDefault }, parts.OtherChords);
    }
}
