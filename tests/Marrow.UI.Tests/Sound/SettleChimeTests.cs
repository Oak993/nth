using System.Reflection;
using System.Security.Cryptography;
using Marrow.Testing;
using Marrow.UI.Sound;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/SettleChime.swift (B2 1fbf81e), :1-37. Record PR-THEME-082.
//
// SettleChime declares `notes`, `partials`, `volume` and `sound` as file-private (`private static let`),
// which maps to C# `private` verbatim (R-NAME-6: private/fileprivate -> private). Reflection is therefore
// the only way to observe their exact values without inventing new public surface on the twin, so this file
// reads them by the names R-NAME-2 gives the ported members (Notes, Partials, Volume) plus one named constant,
// Duration, for the literal `duration: 0.32` argument at SettleChime.swift:12 (not itself a Swift member, but
// the natural, idiomatic place for a magic number here, matching ToneSynth's own private consts SampleRate,
// Attack and Release). A missing field throws at run time (MissingFieldException), which is an expected red
// result before SettleChime.cs exists (the type reference itself will not compile either).
//
// How the expected values were obtained (R-TEST-4), independently of any C# implementation:
// - Notes, Partials and the 0.32 duration are transcribed byte for byte from SettleChime.swift:24-27,31-35,12.
// - The golden SHA-256 (403fad7b...) is the one tests/Marrow.UI.Tests/Sound/ToneSynthTests.cs already
//   established for the identical fixture (SettleChime.swift:12,24-35, R-TEST-8) and cites its own derivation
//   there (an independent Node.js and .NET transcription of ToneSynth.swift:27-68, verified byte for byte, with
//   a rounding-margin analysis ruling out any faithfully-rounded libm difference). Reusing that literal here
//   proves SettleChime's own Notes/Partials/Duration combine to the exact same audio ToneSynthTests already
//   verified against the Swift formula, without re-deriving the digest.
// - Volume 0.18 is SettleChime.swift:9 `private static let volume: Float = 0.18`.
//
// Not automatically testable: the device-level "restarts from the top" behavior (SettleChime.swift:17-21,
// `sound?.stop(); sound?.play()`) and the "sound cannot be created -> play() is a silent no-op" failure case
// both describe real OS audio-device behavior. Nothing in this suite has a fake or loopback for the Windows
// sound backend (none exists yet; R-TEST-10 rules out touching real hardware from a test), so those two facts
// are covered only as a "Play does not throw" smoke test below, not as a positive assertion that a second
// play() actually restarts the same voice. This is called out in Play_does_not_throw_when_called_more_than_once_in_a_row.
public sealed class SettleChimeTests
{
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
    public void Volume_is_0_18_barely_there_under_the_sidebars_motion()
    {
        Assert.Equal(0.18f, GetPrivateStatic<float>("Volume"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:12
    public void Duration_is_0_32_seconds()
    {
        Assert.Equal(0.32, GetPrivateStatic<double>("Duration"));
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:9-16
    public void Notes_Partials_and_Duration_together_render_the_same_golden_wav_ToneSynthTests_verified_against_the_Swift_formula()
    {
        var notes = GetPrivateStatic<IReadOnlyList<ToneSynth.Note>>("Notes");
        var partials = GetPrivateStatic<IReadOnlyList<ToneSynth.Partial>>("Partials");
        var duration = GetPrivateStatic<double>("Duration");

        var data = ToneSynth.Wave(notes, partials, duration);

        Assert.Equal(44 + 14112 * 2, data.Length); // 0.32 * 44100 = 14112 samples, plus the 44-byte header.
        Assert.Equal(SettleGoldenSha256, Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-082")] // DroppyCode/UI/Sound/SettleChime.swift:17-21
    public void Play_does_not_throw_when_called_more_than_once_in_a_row()
    {
        // Proves only that play() tolerates a second call without throwing (the "silent no-op" failure case
        // and the "stop-then-replay on one shared voice" cancellation policy are device-level OS audio
        // behavior with no fake or loopback in this tree; see the file header for why they are not otherwise
        // asserted here).
        using var runtime = new TestRuntime();

        runtime.OnMain(() =>
        {
            SettleChime.Play();
            SettleChime.Play();
        });
    }
}
