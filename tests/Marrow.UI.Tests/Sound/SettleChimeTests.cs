using System.Reflection;
using Marrow.UI.Sound;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/SettleChime.swift (B2 1fbf81e), :1-37. Record PR-THEME-082.
//
// SettleChime declares `notes`, `partials` and `volume` as file-private (`private static let`), which
// maps to C# `private` verbatim (R-NAME-6: private/fileprivate -> private). Reflection is therefore the
// only way to observe their exact values without inventing new public surface on the twin, so this file
// reads them by the names R-NAME-2 gives the ported members (Notes, Partials, Volume). Notes, Partials
// and Volume are transcribed byte for byte from SettleChime.swift:24-27,31-35,9-10 (R-TEST-4),
// independently of any C# implementation. A missing field throws at run time (MissingFieldException),
// an expected red result before SettleChime.cs exists.
//
// Revised after review. Three changes from the previous version of this file:
// - No "Duration" field test. Swift has no `duration` member: 0.32 is a literal argument at
//   SettleChime.swift:12, not a stored value. Reading it by reflection would force SettleChime.cs to add
//   a private const the source does not have (copy, never invent; R-FILE-1 twin shape).
// - No combined "renders the golden wav" test. With Duration gone there is nothing left to assemble
//   Notes/Partials/duration into a `ToneSynth.Wave` call independently of the implementation, and
//   tests/Marrow.UI.Tests/Sound/ToneSynthTests.cs already proves this exact fixture (SettleChime's
//   notes, partials and the 0.32 literal) hashes to 403fad7b... against the Swift wave formula. A test
//   here that just called ToneSynth.Wave on the same reflected fields would restate that result, not add
//   SettleChime evidence, since it would not observe what SettleChime itself builds the voice from.
// - The previous "Play does not throw when called more than once" test is gone. It could not fail on a
//   wrong port (a no-op Play, a play-without-stop, a stop-after-play, or a port that creates a new
//   overlapping voice on every call all pass it), so it was not evidence for PR-THEME-082's cancellation
//   policy ("stop-then-play on one shared voice, not a mix of two overlapping voices",
//   SettleChime.swift:17-21) and it would have driven the real Windows audio device whenever the
//   implementation under test actually plays sound (R-TEST-10 forbids that). It is replaced below by a
//   shape-only test that pins Play as the type's public entry point without calling it, matching the
//   precedent in tests/Marrow.UI.Tests/Sound/FinishChimeTests.cs for the sibling PR-THEME-081 unit.
//
// What this file cannot test, and why (STOP filed for this part; see the unit's report):
// - The voice construction at SettleChime.swift:12-13 (`NSSound(data: ToneSynth.wave(...))`,
//   `sound?.volume = volume`): that the bytes handed to the voice are exactly the wave SettleChime's own
//   Notes/Partials/duration produce, and that Volume actually reaches the voice.
// - The stop-then-play cancellation policy at :17-21 (`sound?.stop(); sound?.play()`) as an exact call
//   sequence (Stop, Play, Stop, Play) on one shared voice, and that exactly one voice is created across
//   two Play() calls and none before the first.
// - The failure case at :11-15/:19-20 (NSSound(data:) is failable; when it returns nil, play() is a
//   silent no-op through optional chaining).
// - That reading Notes/Partials/Volume by reflection does not itself create the voice (Swift's
//   `static let sound` is lazy, :11; a static initializer that runs eagerly would create it at type
//   load).
// These all need a way to observe what SettleChime hands to and calls on its voice: a recording NSSound
// port seam. No such seam exists anywhere in this tree (grep for NSSound, SoundPlayer, ISound in src and
// tests: no hits) and ED-095, which lists the AppKit port types this port has (NSView, NSImage,
// NSPopover, ...), does not list one for NSSound. Adding that abstraction's shape here, in a part barred
// from writing implementation code, would commit the not-yet-written SettleChime.cs (and FinishChime.cs)
// to an injection pattern no ED or work packet settles. Filed as a STOP (missing dependency,
// SettleChime.swift:11-21) rather than guessed or shipped as a weaker test with a Req trait it cannot
// support.
public sealed class SettleChimeTests
{
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
        // value, the same reasoning FinishChimeTests uses for its own Volume field.
        var field = typeof(SettleChime).GetField("Volume", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException("Marrow.UI.Sound.SettleChime.Volume (private static) not found; SettleChime.cs is not written yet or does not expose it under this name.");
        Assert.Equal(typeof(float), field.FieldType);

        var volume = (float)field.GetValue(null)!;

        Assert.Equal(0.18f, volume);
    }

    // Companion to the stop-then-play cancellation policy and the failure-silent-no-op behaviour at
    // :17-21, which are the subject of the STOP filed for this part (no port-only voice seam exists to
    // test them through, and calling Play() here would drive the real Windows audio device). Kept
    // because it still pins Play's shape as the type's only entry point, matching FinishChimeTests.
    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:17-21
    public void Play_is_a_public_parameterless_static_method_returning_void()
    {
        var method = typeof(SettleChime).GetMethod(
            "Play", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }
}
