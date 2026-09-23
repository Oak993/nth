using System.Reflection;
using System.Security.Cryptography;
using Marrow.UI.Sound;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/FinishChime.swift (B2 1fbf81e), :1-37. Record PR-THEME-081.
//
// FinishChime is a caseless Swift enum (R-FILE-1/R-NAME): its four members port to private static
// members of a C# static class named FinishChime, `volume` -> Volume, `notes` -> Notes,
// `partials` -> Partials, `sound` -> Sound (R-NAME-2). Volume, Notes and Partials hold no platform
// object and can be read by reflection without touching the type's audio construction; the exact
// numbers below are copied from FinishChime.swift:9-10,25-36, independently of any C# implementation.
//
// The golden WAV bytes and their SHA-256 (FinishGoldenSha256) are the same values ToneSynthTests.cs
// uses for `ToneSynth.Wave(FinishNotes, FinishPartials, 0.7)`: a transcription of ToneSynth.swift in
// both Node.js and .NET agreeing byte for byte (see that file's header comment for the derivation and
// why no faithfully rounded libm can move a sample across a rounding boundary). Reusing it here checks
// that FinishChime's own Notes and Partials fields are wired to ToneSynth with duration 0.7, which
// FinishChime.swift does not store in a named member (it is the literal at :13), so it cannot be read
// by reflection and is instead proven by reproducing the golden output from the reflected fields.
//
// What this file cannot test, and why:
// - Actual audio output on the system device (side effect, not observable in an automated test).
// - "NSSound declines a play while one is in flight" (:18-21): the swallow-while-playing dedup rests
//   on the platform sound object's own in-flight state, which the Swift API exposes only through
//   playing again while it is not done, an observation that needs a running (and audible) audio
//   backend. No injectable seam exists in the Swift source: `sound?.play()` is called on the real
//   object directly.
// - "play() is a silent no-op" when the sound could not be created (:18-22, failure_cases): there is
//   no way to force platform audio construction to fail from a unit test.
// - "the tone is rendered on first use, not at launch" (`static let` is lazily computed on first
//   access to any member of the Swift enum): C# type initialization also runs on first touch of the
//   type, but the exact moment is a CLR implementation detail (`beforefieldinit`) not observable from
//   a test without instrumenting the type itself, which would go beyond the Swift twin.
public sealed class FinishChimeTests
{
    // ToneSynthTests.cs: FinishGoldenSha256, independently reproduced there in Node.js and .NET.
    private const string FinishGoldenSha256 = "51761373e4570017b1cdc2ea4702b69ede1a94ef91daf15808252fdc7c119ebd";

    [Fact]
    [Trait("Req", "PR-THEME-081")] // FinishChime.swift:9-10
    public void The_chime_plays_at_volume_0_35()
    {
        var volume = GetPrivateStatic<object>("Volume");

        Assert.Equal(0.35, Convert.ToDouble(volume, System.Globalization.CultureInfo.InvariantCulture), 10);
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // FinishChime.swift:25-28
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
    [Trait("Req", "PR-THEME-081")] // FinishChime.swift:32-36
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

    [Fact]
    [Trait("Req", "PR-THEME-081")] // FinishChime.swift:13, notes and partials rendered with duration 0.7
    public void The_notes_and_partials_render_through_ToneSynth_at_a_duration_of_0_7_seconds_to_the_golden_tone()
    {
        var notes = GetPrivateStatic<IReadOnlyList<ToneSynth.Note>>("Notes");
        var partials = GetPrivateStatic<IReadOnlyList<ToneSynth.Partial>>("Partials");

        var data = ToneSynth.Wave(notes, partials, 0.7);

        Assert.Equal(61782, data.Length); // 44 + 30869 * 2, the same golden length as ToneSynthTests
        Assert.Equal(FinishGoldenSha256, Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-081")] // FinishChime.swift:18-22, the only entry point
    public void Play_is_a_public_parameterless_static_method_returning_void()
    {
        var method = typeof(FinishChime).GetMethod(
            "Play", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
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
