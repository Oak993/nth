// PORT-ONLY: Tactus native probe NP-10, IME composition signals (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.7 "The gate",
// 5.4 item 11, critique C4). NATIVE: keeps the physical-input monitor (its hooks and the IME WinEvent thread) for 90 s
// while the owner composes words with the Hindi and then the Urdu input language in Notepad, and in Word, Chrome and
// Outlook when open, and records every change of the two signals the gate reads (an open composition window, and an
// IME-served input language in the foreground) with the foreground app. TextEditPattern events are not subscribed by the
// gate, so they are not probed here (ED-NEW-pcctl-43 item 4). Reading only. Runs only from the smoke script.
using System.Diagnostics;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcImeProbeNativeTests
{
    [Fact]
    public async Task NP10_The_composition_and_IME_language_signals_are_recorded_while_the_owner_composes()
    {
        using var rig = new PcSmokeRig("np10-ime");
        var input = (rig.Composition.Assembly ?? throw new InvalidOperationException("Tactus is off.")).Input;
        var notepad = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        using var keep = input.Keep();
        rig.Result("for 90 s: in Notepad (and Word, Chrome, Outlook if open) switch to Hindi, compose two words, then Urdu, two words");

        var watch = Stopwatch.StartNew();
        var last = (Composition: (nint)0, Ime: false, Foreground: (nint)0);
        var compositions = 0;
        var imeForegrounds = new HashSet<string>(StringComparer.Ordinal);
        while (watch.ElapsedMilliseconds < 90_000)
        {
            var snapshot = input.Snapshot();
            var foreground = PcSmokeRig.Snapshot().Foreground;
            var now = (snapshot.Composition, snapshot.ImeLanguage, foreground);
            if (now != last)
            {
                var exe = PcSmokeRig.ExeOf(foreground);
                if (snapshot.Composition != 0 && last.Composition == 0) compositions++;
                if (snapshot.ImeLanguage) imeForegrounds.Add(exe);
                rig.Note((watch.ElapsedMilliseconds / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " s: " + exe
                    + " composition " + (snapshot.Composition == 0 ? "none" : PcWindowRef.Format(snapshot.Composition)) + ", IME language " + snapshot.ImeLanguage
                    + ", last key " + snapshot.LastKeyMs);
                last = now;
            }
            await Task.Delay(50);
        }

        rig.Result(compositions + " compositions seen; IME language seen in: " + (imeForegrounds.Count == 0 ? "none" : string.Join(", ", imeForegrounds))
            + "; if the owner composed and both are empty, the gate falls back to 2 s of keyboard stillness for those languages");
        PcSmokeRig.Close(notepad);
    }
}
