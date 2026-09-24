using System.Reflection;
using System.Security.Cryptography;
using Marrow.UI.Port;
using Marrow.UI.Sound;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/SettleChime.swift (B2 1fbf81e), :1-36. Record PR-THEME-082.
//
// SettleChime declares `notes`, `partials`, `volume` and `sound` as file-private (`private static let`),
// which maps to C# `private` verbatim (R-NAME-6). Reflection is therefore the only way to observe them
// without inventing new public surface on the twin, so this file reads them by the names R-NAME-2 gives
// the ported members (Notes, Partials, Volume, Sound).
//
// One test in this file (Building_the_lazy_voice...) is the only test here that forces the process-wide
// `Sound` lazy: proving it is uncreated before the first Play() needs to be the first thing in the whole
// process that touches it, which is only safe to assert inside one self-contained test (xUnit does not
// guarantee method order across [Fact]s in a class). No other test below calls SettleChime.Play() or
// reads Sound, so this is not a race against the rest of the file.
//
// What this file cannot test, and why:
// - The exact bytes ToneSynth.Wave(Notes, Partials, 0.32) produces are proved once, independently of
//   SettleChime, by tests/Marrow.UI.Tests/Sound/ToneSynthTests.cs's SettleGoldenSha256 fixture. This file
//   reuses that same golden hash (transcribed here, not read from ToneSynthTests) to check that the voice
//   SettleChime itself builds carries those bytes, rather than re-deriving the wave formula.
// - SettleChime's own wave bytes always parse as a valid WAV (ToneSynth.Wave's fixed header shape), so
//   SwiftSound.Create never actually returns null for SettleChime's real Notes/Partials/duration: there is
//   no way to drive SettleChime.Play() through its own null branch without inventing a test-only seam on
//   SettleChime itself, which the source does not have. Play_is_a_silent_no_op_when_Create_fails instead
//   proves the general mechanism the optional chain at :19-20 relies on (SwiftSound.Create returning null,
//   and C#'s `?.` on a null value doing nothing and throwing nothing), the same guarantee SettleChime's
//   `sound?.stop(); sound?.play();` depends on; it does not call SettleChime.Play().
// - waveOut playback on the real output device is F.Sound's concern (SwiftSoundTests.cs); every test here
//   that plays anything uses FakeSoundOutput (R-TEST-10: no test drives the real Windows audio device).
public sealed class SettleChimeTests
{
    // Transcribed from tests/Marrow.UI.Tests/Sound/ToneSynthTests.cs's SettleGoldenSha256, which proves
    // ToneSynth.Wave(SettleChime's notes, SettleChime's partials, 0.32) hashes to this against the Swift
    // wave formula (SettleChime.swift:12,24-35).
    private const string SettleGoldenSha256 = "403fad7b4b016bc5ead15b57a1e4ef350f1afbfda7ab403933747195688e5b5f";

    private static T GetPrivateStatic<T>(string name)
    {
        var field = typeof(SettleChime).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException($"Marrow.UI.Sound.SettleChime.{name} (private static) not found; SettleChime.cs is not written yet or does not expose it under this name.");
        return (T)field.GetValue(null)!;
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:24-27
    public void Notes_step_from_E5_at_level_0_7_to_C5_at_level_1_starting_0_075_seconds_later()
    {
        var notes = GetPrivateStatic<IReadOnlyList<ToneSynth.Note>>("Notes");

        Assert.Equal(2, notes.Count);
        Assert.Equal(new ToneSynth.Note(659.26, 0, 0.7), notes[0]);
        Assert.Equal(new ToneSynth.Note(523.25, 0.075, 1), notes[1]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:31-35
    public void Partials_are_the_fundamental_then_the_third_harmonic_then_the_octave_in_that_declared_order()
    {
        var partials = GetPrivateStatic<IReadOnlyList<ToneSynth.Partial>>("Partials");

        Assert.Equal(3, partials.Count);
        Assert.Equal(new ToneSynth.Partial(1, 1, 0.075), partials[0]);
        Assert.Equal(new ToneSynth.Partial(3, 0.16, 0.02), partials[1]);
        Assert.Equal(new ToneSynth.Partial(2, 0.08, 0.03), partials[2]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:9-10
    public void Volume_is_a_private_static_float_field_exactly_0_18_barely_there_under_the_sidebars_motion()
    {
        // Volume is `Float` in Swift (:9), not `Double`: 0.18 does not round-trip through a float cast
        // (0.18f as a double is 0.180000007...), so this checks the field's declared type, not just its
        // value.
        var field = typeof(SettleChime).GetField("Volume", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("Marrow.UI.Sound.SettleChime.Volume (private static) not found; SettleChime.cs is not written yet or does not expose it under this name.");
        Assert.Equal(typeof(float), field.FieldType);

        var volume = (float)field.GetValue(null)!;

        Assert.Equal(0.18f, volume);
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:11-21
    public void Building_the_lazy_voice_once_at_volume_0_18_and_replaying_it_from_the_top_on_a_second_Play()
    {
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);

        // Swift's `static let sound` initializes on first access (:11), and that first access is inside
        // play() (:19-20): nothing before the first Play() call may have created it.
        var soundField = typeof(SettleChime).GetField("Sound", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("Marrow.UI.Sound.SettleChime.Sound (private static) not found; SettleChime.cs is not written yet or does not expose it under this name.");
        var lazy = (Lazy<SwiftSound?>)soundField.GetValue(null)!;
        Assert.False(lazy.IsValueCreated);

        SettleChime.Play();

        Assert.True(lazy.IsValueCreated);
        var voice = lazy.Value ?? throw new InvalidOperationException(
            "SettleChime.Sound.Value is null: SwiftSound.Create rejected SettleChime's own wave bytes.");
        Assert.Equal(SettleGoldenSha256, Convert.ToHexStringLower(SHA256.HashData(voice.Data.ToArray())));
        Assert.Equal(0.18f, voice.Volume);
        Assert.Single(output.Plays);
        Assert.Equal(["play"], output.Log);

        SettleChime.Play();

        // Exactly one voice across both calls: the lazy `static let` is the same SwiftSound both times,
        // never a fresh one per call.
        Assert.Equal(2, output.Plays.Count);
        Assert.Same(output.Plays[0].Sound, output.Plays[1].Sound);

        // sound?.stop() before the first play() was a no-op on an idle voice (nothing reached the output
        // yet, checked above: the log after the first Play() is just ["play"]), so the full log across
        // both calls is play, stop, play: the second Play() actually stops the first playback in flight
        // before starting a fresh one (SwiftSoundTests.cs proves the same SwiftSound-level shape: calling
        // Stop() then Play() then Stop() then Play() on one voice also logs exactly ["play", "stop",
        // "play"] on the output, never four entries, since a Stop() with nothing in flight reaches
        // nothing).
        //
        // This fails on: a no-op Play (Plays.Count stays 1); play-without-stop, i.e. FinishChime's
        // decline-while-playing policy (the second Play() call is declined while the first is still in
        // flight, so Plays.Count stays 1); play-then-stop, i.e. the two calls in SettleChime.swift:19-20
        // reversed (the log ends play, stop, play, stop, an extra trailing stop); and a new voice per
        // call (Plays[0].Sound would not be the same reference as Plays[1].Sound, and the first Stop()
        // of the second call would find nothing in flight, so the log would be play, play instead).
        Assert.Equal(["play", "stop", "play"], output.Log);
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:11-15,19-20
    public void Play_is_a_silent_no_op_when_Create_fails()
    {
        // NSSound(data:) is failable (:12); sound?.stop() and sound?.play() are optional-chained (:19-20),
        // so when Create returns null both calls do nothing and raise nothing. This does not call
        // SettleChime.Play(): SettleChime's own wave bytes always parse (see the file header), so its
        // Sound is never actually null. It instead proves the mechanism SettleChime's optional chain
        // relies on: SwiftSound.Create rejects bytes it cannot read as PCM WAV, and the null-conditional
        // operator on a null SwiftSound? neither throws nor calls the output.
        var missing = SwiftSound.Create([]);
        Assert.Null(missing);

        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);

        var exception = Record.Exception(() =>
        {
            missing?.Stop();
            missing?.Play();
            missing?.Stop();
            missing?.Play();
        });

        Assert.Null(exception);
        Assert.Empty(output.Log);
    }

    // This is a Rule test, not a Req test: it proves R-NAME-2 (the member name upper-cases from `play()`
    // to `Play()`) and R-NAME-6 (the App-layer callers such as MCPSettingsPage.swift:68 reach it as a
    // public member). The behavioural claims of PR-THEME-082 are proved above, against FakeSoundOutput.
    [Fact]
    [Trait("Rule", "R-NAME-2")] // DroppyCode/UI/Sound/SettleChime.swift:17
    public void Play_is_a_public_parameterless_static_method_returning_void()
    {
        var method = typeof(SettleChime).GetMethod(
            "Play", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }
}
