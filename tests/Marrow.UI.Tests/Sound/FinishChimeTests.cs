using System.Reflection;
using Marrow.UI.Sound;

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
// What this file cannot test, and why (see the STOP filed for this part for the full reasoning):
// - Actual audio output on the system device (side effect, not observable in an automated test).
// - "NSSound declines a play while one is in flight" (:18-21), "play() is a silent no-op" when the
//   sound could not be created (:18-22, failure_cases), "the tone is rendered on first use, not at
//   launch, once" (:12-16, `static let` lazy init), and whether FinishChime hands ToneSynth the exact
//   Notes/Partials at duration 0.7 (:13): the Swift source has no seam for any of these (`sound?.play()`
//   runs straight to the real NSSound object, `sound` is a plain `static let`), and no port-only voice
//   abstraction FinishChime could be tested through exists anywhere in the tree yet (src/Marrow.UI/Port
//   holds only AvaloniaMainExecutor, ViewLifetime and ViewSupport). Inventing that abstraction's shape
//   here, as a test-only part barred from writing implementation code, would commit the not-yet-written
//   FinishChime.cs to an injection pattern no ED or work packet settles. Filed as a STOP rather than
//   guessed; see the report for this unit.
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

    private static T GetPrivateStatic<T>(string name)
    {
        var field = typeof(FinishChime).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return (T)value!;
    }
}
