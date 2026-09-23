using System.Reflection;
using Marrow.UI.Port;
using Marrow.UI.Sound;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/FinishChime.swift (B2 1fbf81e), :1-37. Record PR-THEME-081.
//
// FinishChime is a caseless Swift enum (R-FILE-1/R-NAME): its four members port to private static
// members of a C# static class named FinishChime, `volume` -> Volume, `notes` -> Notes,
// `partials` -> Partials, `sound` -> Sound (R-NAME-2). Volume is `Float` in Swift (:10) and must stay
// `float` in C#: `0.35` round-trips as a `double` but not through a `float` cast (0.35f as a double is
// 0.349999994...), so the test below checks the field's declared type, not just its value. Notes and
// Partials are checked against the literals at :25-36, independently of any C# implementation.
//
// ED-151 gives the seam this file previously stopped on: F.Sound's SwiftSound and SoundOutput, with
// FakeSoundOutput (tests/Marrow.UI.Tests/Fixtures/FakeSoundOutput.cs) recording every play. The four
// behavioural tests below run against the fake and reach into the private `Sound`/`SoundBuilt` fields
// with reflection (saving and restoring them) so each test starts from a known, unbuilt state
// regardless of test order and leaves the type's static state as it found it.
public sealed class FinishChimeTests
{
    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:9-10
    public void Volume_is_a_private_static_float_field_exactly_0_35()
    {
        var field = typeof(FinishChime).GetField("Volume", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(typeof(float), field!.FieldType);

        var volume = (float)field.GetValue(null)!;

        Assert.Equal(0.35f, volume);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:25-28
    public void The_first_note_is_587_33_hz_from_time_0_at_level_0_8_and_the_second_is_880_hz_at_0_14_seconds_at_level_1()
    {
        var notes = GetPrivateStatic<IReadOnlyList<ToneSynth.Note>>("Notes");

        Assert.Equal(2, notes.Count);
        Assert.Equal(587.33, notes[0].Frequency);
        Assert.Equal(0, notes[0].Onset);
        Assert.Equal(0.8, notes[0].Level);
        Assert.Equal(880, notes[1].Frequency);
        Assert.Equal(0.14, notes[1].Onset);
        Assert.Equal(1, notes[1].Level);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:32-36
    public void The_tone_has_three_partials_the_fundamental_the_octave_and_the_double_octave()
    {
        var partials = GetPrivateStatic<IReadOnlyList<ToneSynth.Partial>>("Partials");

        Assert.Equal(3, partials.Count);
        Assert.Equal(1, partials[0].Multiple);
        Assert.Equal(1, partials[0].Level);
        Assert.Equal(0.14, partials[0].Decay);
        Assert.Equal(2, partials[1].Multiple);
        Assert.Equal(0.22, partials[1].Level);
        Assert.Equal(0.06, partials[1].Decay);
        Assert.Equal(4, partials[2].Multiple);
        Assert.Equal(0.08, partials[2].Level);
        Assert.Equal(0.04, partials[2].Decay);
    }

    // Companion to the swallow-while-playing and failure-silent-no-op behaviours at :18-22, which are
    // the subject of the STOP filed for this part (no port-only voice seam exists to test them through).
    // Kept because it still pins Play's shape as the type's only entry point.
    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:18-22
    public void Play_is_a_public_parameterless_static_method_returning_void()
    {
        var method = typeof(FinishChime).GetMethod(
            "Play", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:12-16
    public void Play_builds_the_one_sound_from_ToneSynth_wave_over_notes_partials_duration_0_7_at_volume_0_35()
    {
        ResetSoundState();
        var fake = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(fake);

        FinishChime.Play();

        var play = Assert.Single(fake.Plays);
        var expectedBytes = ToneSynth.Wave(
            GetPrivateStatic<IReadOnlyList<ToneSynth.Note>>("Notes"),
            GetPrivateStatic<IReadOnlyList<ToneSynth.Partial>>("Partials"),
            0.7);
        Assert.Equal(expectedBytes, play.Sound.Data.ToArray());
        Assert.Equal(0.35f, play.Volume);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:12-16
    public void The_sound_is_built_on_first_play_and_not_before_or_again()
    {
        ResetSoundState();
        var fake = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(fake);

        Assert.Null(GetSoundField());

        FinishChime.Play();
        var built = GetSoundField();
        Assert.NotNull(built);

        fake.CompleteLast();
        FinishChime.Play();

        Assert.Same(built, GetSoundField());
        Assert.Equal(2, fake.Plays.Count);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:18-21
    public void A_play_while_the_chime_is_still_sounding_makes_no_second_chime()
    {
        ResetSoundState();
        var fake = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(fake);

        FinishChime.Play();
        FinishChime.Play();

        Assert.Single(fake.Plays);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // UI/Sound/FinishChime.swift:18-22 failure_cases
    public void Play_does_nothing_when_the_sound_could_not_be_created()
    {
        ResetSoundState();
        SetSoundField(built: true, sound: null);
        var fake = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(fake);

        var exception = Record.Exception(FinishChime.Play);

        Assert.Null(exception);
        Assert.Empty(fake.Plays);
    }

    /// <summary>Leaves FinishChime's static sound cache as if Play had never run, saving nothing back:
    /// each test that calls this starts clean and the real chime is rebuilt (harmlessly, no output
    /// attached) the next time any later test plays it.</summary>
    private static void ResetSoundState() => SetSoundField(built: false, sound: null);

    private static void SetSoundField(bool built, SwiftSound? sound)
    {
        var builtField = typeof(FinishChime).GetField("SoundBuilt", BindingFlags.NonPublic | BindingFlags.Static);
        var soundField = typeof(FinishChime).GetField("Sound", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(builtField);
        Assert.NotNull(soundField);
        builtField!.SetValue(null, built);
        soundField!.SetValue(null, sound);
    }

    private static SwiftSound? GetSoundField()
    {
        var soundField = typeof(FinishChime).GetField("Sound", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(soundField);
        return (SwiftSound?)soundField!.GetValue(null);
    }

    private static T GetPrivateStatic<T>(string name)
    {
        var field = typeof(FinishChime).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return (T)value!;
    }
}
