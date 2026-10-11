using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Models;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Window;

// Source: DroppyCode/App/Window/ThreadWindow.swift (B2 1fbf81e), :1-65.
//
// Platform mapping (PA-0002 windows, ED-099 chords, port_impacts.md 188):
// - The base class DCDisplayCycleGuardedWindow is a macOS-only exception net and is not re-implemented
//   (port_impacts.md 188, X6); both windows derive from the Avalonia Window.
// - AppKit's event path becomes one tunnelling KeyDown class handler on each window class. The window is
//   the root of the tunnel route, so the handler runs before any control in the window and before the framework's
//   bubbling handlers (Tab focus navigation, key bindings, menus), which is where AppKit's local monitor, sendEvent
//   and performKeyEquivalent sit relative to the first responder and the menu. Consuming the event is Handled = true.
// - NSEvent.keyCode is the Set-1 scan code (ED-099 (1)), read from the Avalonia physical key; the macOS codes of the
//   source are noted where they are replaced. Command is Control and the Windows key is masked out like fn
//   (ED-099 (3)); KeyModifiers carries ED-099's ChordModifiers values, so it casts directly.
// - NSWindow.attachedSheet is AttachedSheet on each window; whatever presents a sheet over the window (ChatView.swift
//   :1456, :1489; WallpaperCard.swift:177) sets it while the sheet is up.
// - NSText as first responder is a focused TextBox, the Avalonia text input.
// - performClose(nil) is Close(), which raises Closing (windowShouldClose) before closing, as performClose does.

/// <summary>:3 The main window: it takes the thread-switching chords before the menu sees them.</summary>
public sealed class ThreadWindow : AvaloniaWindow
{
    private Action<long>? selectThreadNumber;

    // :20 private var digitMonitor: Any?
    private Func<KeyEventArgs, bool>? digitMonitor;

    // :23 The number row, by key code, for Command-1 to Command-9. macOS 18, 19, 20, 21, 23, 22, 26, 28, 25 are the
    // scan codes 0x02 to 0x0A by ED-099 (2); the crossover of macOS 23 (5) and 22 (6) does not exist in scan codes.
    private static readonly Dictionary<long, long> DigitKeyCodes = new()
    {
        [0x02] = 1, // 18
        [0x03] = 2, // 19
        [0x04] = 3, // 20
        [0x05] = 4, // 21
        [0x06] = 5, // 23
        [0x07] = 6, // 22
        [0x08] = 7, // 26
        [0x09] = 8, // 28
        [0x0A] = 9, // 25
    };

    // A class handler: Avalonia runs it at the window before any instance handler, and the window is the first stop
    // of the tunnel route.
    static ThreadWindow()
    {
        KeyDownEvent.AddClassHandler<ThreadWindow>((window, e) => window.SendEvent(e), RoutingStrategies.Tunnel);
        // PORT-ONLY (ED-NEW-session-hotkeys): hold-Ctrl watches every key and press in the window, handled or not, from the
        // window's end of the tunnel, so no control under it (the composer, the list, a menu) can keep a Ctrl release from it.
        KeyDownEvent.AddClassHandler<ThreadWindow>((window, e) => window.NoteKeyDown(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyUpEvent.AddClassHandler<ThreadWindow>((window, e) => window.NoteKeyUp(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerPressedEvent.AddClassHandler<ThreadWindow>((window, _) => window.EndHold(taint: true), RoutingStrategies.Tunnel, handledEventsToo: true);
        // A wheel turn (the sidebar scrolled under the numbers, Ctrl+wheel zoom) is a use of the hand like a press: the numbers go.
        PointerWheelChangedEvent.AddClassHandler<ThreadWindow>((window, _) => window.EndHold(taint: true), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public ThreadWindow() => Deactivated += (_, _) => EndHold(taint: false);

    // :4 var selectThread: ((Int) -> Void)?
    public Action<long>? SelectThread { get; set; }

    /// <summary>
    /// :5-19 Command-1 to Command-9, taken before the event is dispatched. The first assignment, nil included,
    /// installs the monitor once (:10); it reads the closure live, so a later assignment needs no second monitor.
    /// </summary>
    public Action<long>? SelectThreadNumber
    {
        get => selectThreadNumber;
        set
        {
            selectThreadNumber = value;
            // :9-18 didSet. The monitor lives as long as the window: it is part of this window's own event path, not
            // a process-wide hook, so nothing outlives the window and nothing needs removing (:1-39 has no removal).
            if (digitMonitor is not null) return; // :10
            digitMonitor = DigitMonitor; // :11
        }
    }

    /// <summary>NSWindow.attachedSheet: the sheet over this window, or null.</summary>
    public Control? AttachedSheet { get; set; }

    /// <summary>
    /// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11): Ctrl held alone for <see cref="HoldDelay"/> shows the sidebar's
    /// session numbers (true); releasing Ctrl, any other key, a press, or the window going inactive hides them (false).
    /// </summary>
    public Action<bool>? ShowSessionNumbers { get; set; }

    /// <summary>How long Ctrl is held alone before the numbers show.</summary>
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How often the safety net reads Ctrl's real state while a hold is pending or the numbers show.</summary>
    public static readonly TimeSpan HoldWatchInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The keyboard's real Ctrl state: true down, false up, null unknown. The safety net behind the key-up: a release the window
    /// never sees (it went to another window, an IME, a focus change) still hides the numbers within one watch interval.
    /// Windows: GetAsyncKeyState(VK_CONTROL). Tests stand in their own.
    /// </summary>
    internal static Ambient<Func<bool?>> ControlKeyState { get; } = new(Win32ControlKeyState);

    private bool controlHeld; // Ctrl went down alone and is still down
    private bool holdSpent;   // another key or a press came while Ctrl was down: no numbers until Ctrl comes up
    private bool numbersShown;
    private TaskHandle? holdWait;
    private TaskHandle? holdWatch;
    private long holdWatchTurn; // the watcher's turn: a cancelled loop's tail never clears the handle of the one after it

    /// <summary>Whether the session numbers show now (tests).</summary>
    internal bool AreSessionNumbersShown => numbersShown;

    /// <summary>Whether the safety net holds a watcher now, and how many of its loops are alive (tests).</summary>
    internal (bool Held, int Loops) HoldWatchState => (holdWatch is not null, holdWatchLoops);

    private int holdWatchLoops;

    private void NoteKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            // Ctrl alone (the Windows key is masked out like fn, ED-099 (3)); Ctrl with Shift or Alt (AltGr) is another chord.
            var alone = ((ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers) == ChordModifiers.Control;
            if (!alone)
            {
                EndHold(taint: true);
                return;
            }
            if (controlHeld || holdSpent) return; // the key's own auto-repeat
            controlHeld = true;
            StartHold();
            return;
        }
        // Any other key: a chord (Ctrl+C, Ctrl+1) or typing. The numbers go at once and the hold is spent until Ctrl is up.
        EndHold(taint: true);
    }

    private void NoteKeyUp(KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl)) return;
        controlHeld = false;
        holdSpent = false;
        EndHold(taint: false); // the fade-out starts on this same key-up
    }

    private void StartHold()
    {
        holdWait?.Cancel();
        var weak = new WeakReference<ThreadWindow>(this);
        holdWait = MainActor.Spawn(async () =>
        {
            if (!await SwiftTask.TrySleep(HoldDelay) || !weak.TryGetTarget(out var window)) return;
            if (!window.controlHeld || window.holdSpent || !window.IsVisible || window.AttachedSheet is not null) return;
            window.numbersShown = true;
            window.ShowSessionNumbers?.Invoke(true);
        });
        Watch();
    }

    // The safety net: while a hold is pending or the numbers show, Ctrl's real state is read every interval.
    private void Watch()
    {
        if (holdWatch is not null) return;
        var weak = new WeakReference<ThreadWindow>(this);
        var turn = ++holdWatchTurn;
        holdWatch = MainActor.Spawn(async () =>
        {
            if (weak.TryGetTarget(out var started)) started.holdWatchLoops++;
            while (await SwiftTask.TrySleep(HoldWatchInterval) && weak.TryGetTarget(out var window) && window.holdWatchTurn == turn)
            {
                if (!window.controlHeld && !window.numbersShown) break;
                if (ControlKeyState.Current() == false)
                {
                    window.controlHeld = false;
                    window.holdSpent = false;
                    window.EndHold(taint: false);
                    break;
                }
            }
            if (!weak.TryGetTarget(out var owner)) return;
            owner.holdWatchLoops--;
            if (owner.holdWatchTurn == turn) owner.holdWatch = null;
        });
    }

    /// <summary>
    /// Ends a pending hold and hides the numbers if they show. <paramref name="taint"/>: Ctrl may still be down but this hold is
    /// spent (another key, a press); false: Ctrl is up (its key-up, the safety net, the window going inactive).
    /// </summary>
    private void EndHold(bool taint)
    {
        holdWait?.Cancel();
        holdWait = null;
        if (taint && controlHeld) holdSpent = true;
        if (!taint)
        {
            controlHeld = false;
            holdSpent = false;
        }
        var wasShown = numbersShown;
        numbersShown = false;
        if (!controlHeld)
        {
            holdWatch?.Cancel();
            holdWatch = null;
            holdWatchTurn++; // the cancelled loop's tail is not this handle's any more
        }
        if (wasShown) ShowSessionNumbers?.Invoke(false);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private static bool? Win32ControlKeyState()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return (GetAsyncKeyState(0x11) & 0x8000) != 0; } // VK_CONTROL
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    // :11-17 the local monitor's body: true consumes the event (`return nil`), false returns it (`return event`).
    private bool DigitMonitor(KeyEventArgs e)
    {
        // :12-14
        if (!ReferenceEquals(TopLevel.GetTopLevel(e.Source as Visual), this) || AttachedSheet is not null ||
            ((ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers) != ChordModifiers.Control ||
            DigitNumber(e) is not { } number || selectThreadNumber is not { } select) return false;
        select(number); // :15
        return true; // :16
    }

    // :25-38 override func performKeyEquivalent(with:)
    private bool PerformKeyEquivalent(KeyEventArgs e)
    {
        // :26 macOS 48 is Tab, scan code 0x0F (ED-099 (2)).
        if (KeyCode(e) == 0x0F && AttachedSheet is null)
        {
            var chord = new KeyChord(0x0F, (ChordModifiers)e.KeyModifiers); // :27
            if (chord == ShortcutStore.Shared.Current.Chord(AppShortcut.NextThread)) // :28
            {
                SelectThread?.Invoke(1); // :29
                return true; // :30
            }
            if (chord == ShortcutStore.Shared.Current.Chord(AppShortcut.PreviousThread)) // :32
            {
                SelectThread?.Invoke(-1); // :33
                return true; // :34
            }
        }
        return false; // :37 super.performKeyEquivalent: the event goes on its normal route.
    }

    // The event path: the local monitor (:11) sees a key-down first, then the window's performKeyEquivalent (:25).
    private void SendEvent(KeyEventArgs e)
    {
        if (e.Handled) return;
        if ((digitMonitor is { } monitor && monitor(e)) || PerformKeyEquivalent(e)) e.Handled = true;
    }

    // :14 the digit the chord names: the number row by scan code (:23), or, PORT-ONLY (ED-NEW-session-hotkeys; owner
    // 2026-10-11 "with the numpad working as well"), the number pad's 1 to 9. The pad is matched by the key it types, which is
    // NumPad1..9 only with Num Lock on: with it off the same keys type End, Down, PageDown, Left, Clear, Right, Home, Up and
    // PageUp, which keep their own Ctrl chords. NumPad0 is not a thread number (as the row's 0 is not).
    private static long? DigitNumber(KeyEventArgs e)
    {
        if (KeyCode(e) is { } keyCode && DigitKeyCodes.TryGetValue(keyCode, out var number)) return number;
        return e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 + 1 : null;
    }

    // NSEvent.keyCode as a scan code (ED-099 (1)) for the keys this file names: the digits 1 to 9 (:23) and Tab (:26).
    // No other key can satisfy a test in this file, so any other key reads as null and is returned unchanged.
    private static long? KeyCode(KeyEventArgs e) => e.PhysicalKey switch
    {
        PhysicalKey.Digit1 => 0x02,
        PhysicalKey.Digit2 => 0x03,
        PhysicalKey.Digit3 => 0x04,
        PhysicalKey.Digit4 => 0x05,
        PhysicalKey.Digit5 => 0x06,
        PhysicalKey.Digit6 => 0x07,
        PhysicalKey.Digit7 => 0x08,
        PhysicalKey.Digit8 => 0x09,
        PhysicalKey.Digit9 => 0x0A,
        PhysicalKey.Tab => 0x0F,
        _ => null,
    };
}

/// <summary>
/// :41-44 A window beside the main one, Settings say, where Command-W closes it as it does in any Mac app. The chord
/// is the archive shortcut in the main window and, left alone, would reach the Thread menu from here too.
/// </summary>
public sealed class SecondaryWindow : AvaloniaWindow
{
    // A class handler, as ThreadWindow's: it runs at the window before any instance handler.
    static SecondaryWindow()
    {
        // ED-NEW-T45 owner override: Settings can maximize without introducing a manual resize border.
        CanMaximizeProperty.OverrideMetadata<SecondaryWindow>(new StyledPropertyMetadata<bool>(
            coerce: (owner, value) => value && (owner is SecondaryWindow window && (window.CanResize || window.AllowsCaptionMaximize))));
        KeyDownEvent.AddClassHandler<SecondaryWindow>((window, e) => window.Dispatch(e), RoutingStrategies.Tunnel);
    }

    internal bool AllowsCaptionMaximize { get; init; }

    // ED-NEW-T45: an owned, nonmodal window stays above this app's main window
    // and follows its native minimise/restore, without becoming globally topmost.
    internal void SetMainOwner(AvaloniaWindow? owner) => Owner = owner;

    /// <summary>NSWindow.attachedSheet: the sheet over this window, or null.</summary>
    public Control? AttachedSheet { get; set; }

    // :45-56 override func sendEvent(_:). Escape closes it as well, like a sheet. Taken here rather than in keyDown,
    // which the hosting view never passes on. A field being typed in keeps its Escape.
    private bool SendEvent(KeyEventArgs e)
    {
        // :49-51 keyDown (this handler sees only key-downs), macOS 53 is Esc, scan code 0x01 (ED-099 (2)).
        if (KeyCode(e) == 0x01 &&
            ((ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers) == ChordModifiers.None &&
            FocusManager?.GetFocusedElement() is not TextBox)
        {
            // PORT-ONLY (OD-0088, ED-253): a popover or menu open over the window is a window of its own on the Mac, which
            // takes this Escape and closes; the port draws it over this window, so the newest one closes and this stays.
            if (Marrow.UI.Port.WindowTransients.DismissNewest(this)) return true;
            Close(); // :52 performClose(nil)
            return true; // :53
        }
        return false; // :55 super.sendEvent
    }

    // :58-64 override func performKeyEquivalent(with:). macOS 13 with Command is W (0x11) with Control (ED-099).
    private bool PerformKeyEquivalent(KeyEventArgs e)
    {
        if (AttachedSheet is null && KeyCode(e) is { } keyCode &&
            new KeyChord(keyCode, (ChordModifiers)e.KeyModifiers) == new KeyChord(0x11, ChordModifiers.Control)) // :59
        {
            Close(); // :60 performClose(nil)
            return true; // :61
        }
        return false; // :63 super.performKeyEquivalent
    }

    // sendEvent (:48) comes first; it hands the key equivalent on to performKeyEquivalent (:58) through super.
    private void Dispatch(KeyEventArgs e)
    {
        if (e.Handled) return;
        if (SendEvent(e) || PerformKeyEquivalent(e)) e.Handled = true;
    }

    // NSEvent.keyCode as a scan code (ED-099 (1)) for the keys this file names: Esc (:49) and W (:59). No other key
    // can satisfy a test in this file, so any other key reads as null and is returned unchanged.
    private static long? KeyCode(KeyEventArgs e) => e.PhysicalKey switch
    {
        PhysicalKey.Escape => 0x01,
        PhysicalKey.W => 0x11,
        _ => null,
    };
}
