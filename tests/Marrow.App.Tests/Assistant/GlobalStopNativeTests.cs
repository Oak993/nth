using System.Runtime.InteropServices;
using Marrow.App.Safety;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it alone, with focus, on a temp setup and no other typing):
// DESIGN.md rev 5, 16.1 "Native gate", GlobalStop. The chord is registered with Windows only while work holds a lease and
// released when none does; a real key press of it reaches the subscribers on the chord's own thread; and a chord another
// registration holds blocks a mission from starting (fail closed) until it is let go. The chord used is one nobody presses
// (Ctrl+Alt+Shift+F11), so the owner's own Ctrl+Alt+Esc is never taken.
[Trait("Decision", "ED-NEW-dextra-as6-2")]
[Trait("Feature", "AS-6")]
public sealed class GlobalStopNativeTests
{
    private const string Chord = "Ctrl+Alt+Shift+F11";
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12; // Alt
    private const byte VkShift = 0x10;
    private const byte VkF11 = 0x7A;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    /// <summary>The owner pressing the chord: the modifiers down, F11 down and up, the modifiers up.</summary>
    private static void PressChord()
    {
        foreach (var key in new[] { VkControl, VkMenu, VkShift, VkF11 }) keybd_event(key, 0, 0, 0);
        foreach (var key in new[] { VkF11, VkShift, VkMenu, VkControl }) keybd_event(key, 0, KeyUp, 0);
    }

    [Fact]
    public void The_chord_registers_when_work_starts_reaches_the_subscribers_on_a_real_press_and_releases_when_it_ends()
    {
        using var host = new StopChordThread();
        using var stop = new GlobalStop(host, () => Chord);
        using var pressed = new ManualResetEventSlim();
        var onThread = 0;
        stop.Subscribe("native", _ =>
        {
            onThread = Environment.CurrentManagedThreadId;
            pressed.Set();
        });

        PressChord(); // nothing holds a lease: the chord is not registered, so nothing hears it
        Assert.False(pressed.Wait(TimeSpan.FromMilliseconds(500)));

        Assert.True(stop.TryAcquire("mission:native", out var lease));
        PressChord();
        Assert.True(pressed.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, onThread); // the chord's own thread, never the test's or the UI's

        lease!.Dispose();
        pressed.Reset();
        PressChord(); // released again
        Assert.False(pressed.Wait(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void A_chord_that_is_held_elsewhere_blocks_a_mission_from_starting_until_it_is_let_go()
    {
        Assert.True(PcHotkey.TryParse(Chord, out var hotkey));
        using var other = new StopChordThread();
        Assert.True(other.Register(77, hotkey)); // another registration holds the chord
        using var host = new StopChordThread();
        using var stop = new GlobalStop(host, () => Chord);

        Assert.False(stop.TryAcquire("mission:native", out _));
        Assert.True(stop.Status.Taken);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Shift+F11 is taken. Change it in Settings", stop.Status.Unavailable);

        other.Unregister(77);
        Assert.True(stop.TryAcquire("mission:native", out var lease));
        Assert.True(stop.Status.Works);
        lease!.Dispose();
    }
}
