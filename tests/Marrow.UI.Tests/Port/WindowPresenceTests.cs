using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-6: the "window can be seen" signal itself. The hang watchdog's quiet (counts and the hung-window escape), a live
// presence fed by a hub of the test's own (window state, cloak, cover, the covering processes' location changes, pointer
// recovery), the session and display messages, and the covering arithmetic in physical pixels. Headless; the looks, the hooks
// and the hung check are fakes, so nothing here touches the desktop.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class WindowPresenceTests(HeadlessSession headless)
{
    private const uint LocationChange = 0x800B;
    private static readonly Guid DisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [Fact]
    public void Quiet_needs_an_open_window_none_seen_and_none_hung()
    {
        var hung = WindowPresence.Hung;
        WindowPresence.Hung = handle => handle == 0x42;
        try
        {
            Assert.False(WindowPresence.IsQuiet(0, 0, []), "no tracked window: never quiet");
            Assert.False(WindowPresence.IsQuiet(2, 1, [0x11, 0x12]), "one window seen");
            Assert.True(WindowPresence.IsQuiet(2, 0, [0x11, 0x12]), "two windows, neither seen");
            Assert.True(WindowPresence.IsQuiet(1, 0, []), "an unseen window without a handle");
            Assert.False(WindowPresence.IsQuiet(2, 0, [0x11, 0x42]), "a window has stopped answering: the watchdog watches again");
        }
        finally { WindowPresence.Hung = hung; }
    }

    [Fact]
    public void Each_window_is_counted_once_and_a_closed_window_leaves_the_counts() => headless.OnUiThread(() =>
    {
        const nint FirstHandle = 0x7A01, SecondHandle = 0x7A02;
        var hung = WindowPresence.Hung;
        var first = new Window { Content = new Border() };
        var second = new Window { Content = new Border() };
        WindowPresence.TestFactory = w => new WindowPresence(w, true, w == first ? FirstHandle : SecondHandle);
        try
        {
            var before = WindowPresence.Counts;
            first.Show(); second.Show();
            var a = WindowPresence.For((Visual)first.Content!)!;
            var b = WindowPresence.For((Visual)second.Content!)!;
            Assert.Same(a, WindowPresence.For(first));
            Assert.Equal((before.Open + 2, before.Seen + 2), WindowPresence.Counts);
            Assert.True(WindowPresence.Watches(FirstHandle) && WindowPresence.Watches(SecondHandle));

            var changes = 0;
            a.Changed += () => changes++;
            a.SetSeen(false);
            a.SetSeen(false);
            Assert.Equal(1, changes);
            Assert.Equal((before.Open + 2, before.Seen + 1), WindowPresence.Counts);
            b.SetSeen(false);
            Assert.Equal((before.Open + 2, before.Seen), WindowPresence.Counts);

            // Both unseen, and the second window's thread has stopped answering: never quiet, whatever else is open.
            WindowPresence.Hung = handle => handle == SecondHandle;
            Assert.False(WindowPresence.Quiet);
            WindowPresence.Hung = hung;

            a.SetSeen(true);
            Assert.Equal((before.Open + 2, before.Seen + 1), WindowPresence.Counts);
            first.Close();
            Assert.Equal((before.Open + 1, before.Seen), WindowPresence.Counts);
            Assert.False(WindowPresence.Watches(FirstHandle));
            second.Close();
            Assert.Equal(before, WindowPresence.Counts);
            Assert.False(WindowPresence.Watches(SecondHandle));
            b.SetSeen(true);
            Assert.Equal(before, WindowPresence.Counts);
        }
        finally
        {
            WindowPresence.Hung = hung; first.Close(); second.Close(); Dispatcher.UIThread.RunJobs();
            WindowPresence.TestFactory = null;
        }
    });

    [Fact]
    public void A_live_presence_follows_the_window_the_look_and_the_covering_processes() => headless.OnUiThread(() =>
    {
        const nint Handle = 0x7B01, CoveringWindow = 0x7B90, CoveringChild = 0x7B91;
        var installed = new List<(uint First, uint Last, uint Process)>();
        var removed = new List<nint>();
        var hub = new AmbientWindowState.EventHub(
            (first, last, process, _) => { installed.Add((first, last, process)); return installed.Count; }, removed.Add,
            handle => handle != CoveringChild, _ => throw new InvalidOperationException("the presence takes its own look"));
        var clear = new AmbientWindowState.WindowLook(false, false, []);
        var look = clear;
        var looks = 0;
        var window = new Window { Width = 80, Height = 80, Background = Brushes.Black, Content = new Border { Background = Brushes.Black } };
        WindowPresence.TestFactory = w => new WindowPresence(w, hub, Handle, () => { looks++; return look; });
        void Raise(uint kind, nint handle, int objectId = 0) => hub.OnEvent(1, kind, handle, objectId, 0, 0, 0);
        try
        {
            window.Show();
            var presence = WindowPresence.For(window)!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(presence.Seen);
            Assert.True(hub.Hooked);
            Assert.Empty(hub.Watched);

            // A batch of events is one flush: one look, however many events.
            look = new(true, false, []); looks = 0;
            Raise(0x8017, Handle); Raise(0x8003, CoveringWindow); Raise(0x000B, CoveringWindow);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, looks);
            Assert.False(presence.Seen, "cloaked");
            Assert.Empty(hub.Watched);

            // Fully covered: the covering processes' location changes are heard (one hook each, for that process only).
            look = new(false, true, [77, 78]);
            Raise(0x8004, CoveringWindow); Dispatcher.UIThread.RunJobs();
            Assert.False(presence.Seen, "fully covered");
            Assert.Equal(new uint[] { 77, 78 }, hub.Watched.Order());
            Assert.Equal(new (uint, uint, uint)[] { (LocationChange, LocationChange, 77), (LocationChange, LocationChange, 78) },
                installed.Where(range => range.Process != 0).OrderBy(range => range.Process));

            // The covering window restored from maximised with the keyboard raises only its location change: seen again,
            // and the location hooks go with the cover.
            look = clear;
            Raise(LocationChange, CoveringWindow); Dispatcher.UIThread.RunJobs();
            Assert.True(presence.Seen, "a covering window's location change is heard");
            Assert.Empty(hub.Watched);
            Assert.Equal(2, removed.Count);

            // A child window's location change, or an event of a part of a window, is not a window change.
            look = new(false, true, [77]);
            Raise(0x000B, CoveringWindow); Dispatcher.UIThread.RunJobs();
            Assert.False(presence.Seen);
            look = clear; looks = 0;
            Raise(LocationChange, CoveringChild); Raise(0x8004, CoveringWindow, objectId: -4); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, looks);
            Assert.False(presence.Seen);

            // Pointer input reaches only a window that can be seen: an unseen one that hears it looks again.
            window.MouseMove(new Point(20, 20));
            Assert.True(presence.Seen, "pointer recovery");
            Assert.Empty(hub.Watched);

            // Minimised: unseen without a look at the desktop, and no covering process is heard.
            look = new(false, true, [79]);
            Raise(0x8004, CoveringWindow); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new uint[] { 79 }, hub.Watched);
            looks = 0;
            window.WindowState = WindowState.Minimized;
            Assert.False(presence.Seen, "minimised");
            Assert.Equal(0, looks);
            Assert.Empty(hub.Watched);
            look = clear;
            window.WindowState = WindowState.Normal;
            Assert.True(presence.Seen, "restored");

            window.Hide();
            Assert.False(presence.Seen, "hidden");
            window.Show();
            Assert.True(presence.Seen, "shown");

            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(hub.Hooked, "the presence's close removes its listener and the hub's hooks");
            Assert.Equal(installed.Count, removed.Count);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null; }
    });

    [Fact]
    public void Session_lock_and_display_off_pause_the_presence_until_both_end() => headless.OnUiThread(() =>
    {
        const nint Handle = 0x7C01;
        var hub = new AmbientWindowState.EventHub((_, _, _, _) => 0, _ => { }, _ => true, _ => default);
        var window = new Window { Content = new Border() };
        WindowPresence.TestFactory = w => new WindowPresence(w, hub, Handle, () => new(false, false, []));
        var setting = Marshal.AllocHGlobal(24);
        WindowPresence? presence = null;
        nint Send(uint message, nint wParam, nint lParam)
        {
            var handled = false;
            var result = presence!.OnMessage(Handle, message, wParam, lParam, ref handled);
            Assert.False(handled, "observed only, never handled");
            return result;
        }
        nint Power(Guid guid, int length, int data)
        {
            Marshal.Copy(guid.ToByteArray(), 0, setting, 16);
            Marshal.WriteInt32(setting, 16, length);
            Marshal.WriteInt32(setting, 20, data);
            return setting;
        }
        try
        {
            window.Show();
            presence = WindowPresence.For(window)!;
            Assert.True(presence.Seen);

            Assert.Equal(0, Send(0x02B1, 7, 0)); // WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK
            Assert.False(presence.Seen, "locked");
            Send(0x02B1, 5, 0); // WTS_SESSION_LOGON: not a lock change
            Assert.False(presence.Seen);
            Send(0x02B1, 8, 0); // WTS_SESSION_UNLOCK
            Assert.True(presence.Seen, "unlocked");

            Assert.Equal(0, Send(0x0218, 0x8013, Power(DisplayState, 4, 0))); // WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE: off
            Assert.False(presence.Seen, "display off");
            Send(0x0218, 0x8013, Power(DisplayState, 4, 2));
            Assert.True(presence.Seen, "dimmed counts as on");
            Send(0x0218, 0x8013, Power(DisplayState, 4, 0));
            Assert.False(presence.Seen);
            Send(0x0218, 0x8013, Power(DisplayState, 4, 1));
            Assert.True(presence.Seen, "display on");

            // Not the display's setting, too short a payload, another power event, or no payload: nothing changes.
            Send(0x0218, 0x8013, Power(Guid.NewGuid(), 4, 0));
            Send(0x0218, 0x8013, Power(DisplayState, 0, 0));
            Send(0x0218, 0x000A, Power(DisplayState, 4, 0));
            Send(0x0218, 0x8013, 0);
            Assert.True(presence.Seen);

            // Locked with the display off: seen again only once both have ended.
            Send(0x02B1, 7, 0);
            Send(0x0218, 0x8013, Power(DisplayState, 4, 0));
            Send(0x02B1, 8, 0);
            Assert.False(presence.Seen, "unlocked, the display still off");
            Send(0x0218, 0x8013, Power(DisplayState, 4, 1));
            Assert.True(presence.Seen);
        }
        finally
        {
            if (presence is not null) { Send(0x02B1, 8, 0); Send(0x0218, 0x8013, Power(DisplayState, 4, 1)); }
            Marshal.FreeHGlobal(setting);
            window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null;
        }
    });

    // GetWindowRect gives physical pixels for the window and the windows above alike, so the cover is decided with no scale:
    // a window at 150 % (1800 x 1350 physical) is covered by a window of the same physical bounds, not by its logical size.
    [Fact]
    public void Covering_is_decided_in_physical_pixels_and_names_the_covering_windows()
    {
        var bounds = new Rect(300, 150, 1800, 1350);
        var heard = new List<nint>();
        Assert.True(AmbientWindowState.Covers(bounds, [(1, bounds)], heard.Add));
        Assert.Equal(new nint[] { 1 }, heard);

        heard.Clear();
        Assert.False(AmbientWindowState.Covers(bounds, [(1, new Rect(300, 150, 1200, 900))], heard.Add), "the logical size leaves it showing");
        Assert.Equal(new nint[] { 1 }, heard);

        heard.Clear();
        Assert.True(AmbientWindowState.Covers(bounds,
            [(1, new Rect(0, 0, 3840, 10)), (2, new Rect(300, 150, 900, 1350)), (3, new Rect(1200, 100, 900, 1400))], heard.Add));
        Assert.Equal(new nint[] { 2, 3 }, heard);

        heard.Clear();
        Assert.False(AmbientWindowState.Covers(bounds,
            [(2, new Rect(300, 150, 900, 1350)), (3, new Rect(1200, 150, 900, 1349))], heard.Add), "a one-pixel strip still shows");
        Assert.Equal(new nint[] { 2, 3 }, heard);

        Assert.True(AmbientWindowState.Covers(bounds, StopsAfterTheCover(bounds), null));
    }

    private static IEnumerable<(nint, Rect)> StopsAfterTheCover(Rect bounds)
    {
        yield return (1, bounds);
        throw new InvalidOperationException("asked for a window above after nothing was left showing");
    }
}
