using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;

namespace Marrow.App.Port;

// PORT-ONLY: the menu bar's key equivalents without a menu bar. Upstream installs AppCommands (AppCommands.swift:11-114)
// as the macOS menu bar (DroppyCodeApp.swift:21-25), and AppKit hands every key equivalent pressed in any window of the
// app to that menu, which runs the matching enabled item. OD-0008 C3: the port draws no menu bar; every command keeps
// its chord, dispatched by the port itself with the same enablement rules. This file is that dispatch:
// - MenuCommand is one Button of a Commands body with its .keyboardShortcut and .disabled modifiers. Nothing draws it;
//   the title is kept as the source spells it so the command stays on record.
// - Attach puts the dispatch on one window as a tunnelling KeyDown handler on the window itself. Instance handlers run
//   after the window class's own handlers (ThreadWindow's digit monitor and Tab chords, SecondaryWindow's Escape and
//   Ctrl+W), which is where the menu sits relative to a window's local monitor, sendEvent and performKeyEquivalent;
//   and a tunnel handler at the window runs before any control inside it, as a menu key equivalent is taken before the
//   first responder's keyDown ("a menu command always wins over a button's shortcut", AppCommands.swift:69-73).
// - A key event becomes a SwiftUI KeyboardShortcut by ED-099: the physical key's Set-1 scan code (1), the menu key
//   equivalent Shortcuts.swift:80-101 gives that code (6), and the modifiers held, Win masked out (3). AltGr reaches
//   Avalonia as Ctrl+Alt and reads as those two here; no decision says otherwise yet: STOP App.AppCommands #7 in
//   tests/Marrow.App.Tests/AppCommandsTests.cs.
// - Except the four zoom items, whose literal key equivalents a menu matches by the character typed (ED-270): those are
//   read by Typed, the character the key types on the active layout (Port/KeyboardLayoutText.cs), Ctrl and the rest of
//   the held modifiers matched as the item states them.
// - Its call sites replace DroppyCodeApp.swift:24, the menu bar's install, on each window of the app that can be key:
//   WindowManager.MakeWindow (the main and Settings windows) and TourWindowController.Present (the tour window). The
//   floating guide panels never take activation (FloatingGuidePanel.cs, ED-063), so a key pressed while one is up goes
//   to the app window that is active and meets the dispatch there. STOP App.AppCommands #6 (resolved by fix/shortcuts).

/// <summary>
/// One item of a SwiftUI <c>Commands</c> body: <c>Button(Title) { Action() }.keyboardShortcut(Shortcut)
/// .disabled(IsDisabled)</c>. A null <see cref="Shortcut"/> is an item with no key equivalent.
/// <see cref="MatchesTypedCharacter"/> marks an item whose key equivalent is a literal character, which a menu matches
/// by what the pressed key types, not by where it sits (ED-270): Zoom In "+", Zoom Out "-", Actual Size "0", the hidden
/// Zoom In "=" (AppCommands.swift:53-62).
/// </summary>
public sealed record MenuCommand(string Title, KeyboardShortcut? Shortcut, bool IsDisabled, Action Action,
    bool MatchesTypedCharacter = false, bool WorksInsideContent = false);

/// <summary>The port's own dispatch of menu key equivalents (OD-0008 C3).</summary>
public static class MenuKeyEquivalents
{
    /// <summary>
    /// Runs the first enabled command, in body order, whose key equivalent is <paramref name="pressed"/>, and reports
    /// that the chord was taken. A disabled command does not claim the chord: the event goes on its way, so an approval
    /// card's button receives Ctrl+Return while "Queue a chat" is off (AppCommands.swift:69-77, PR-WIN-105).
    /// </summary>
    public static bool Perform(IReadOnlyList<MenuCommand> commands, KeyboardShortcut pressed) =>
        Perform(commands, pressed, () => pressed);

    /// <summary>
    /// The same, for a key-down read two ways (ED-270): <paramref name="positional"/> is the chord by key position
    /// (<see cref="Shortcut"/>), matched against ordinary items, and <paramref name="typed"/> the chord by the character
    /// typed (<see cref="Typed"/>, read only when an item that matches by character is reached), matched against the
    /// literal-character items. One pass in body order, as a menu takes the first enabled item holding the chord.
    /// </summary>
    public static bool Perform(IReadOnlyList<MenuCommand> commands, KeyboardShortcut? positional, Func<KeyboardShortcut?> typed,
        ShortcutScope scope = ShortcutScope.Chrome)
    {
        var typedChord = default(KeyboardShortcut?);
        var typedRead = false;
        foreach (var command in commands)
        {
            if (command.Shortcut is not { } held || command.IsDisabled) continue;
            // PORT-ONLY (web W3b, DESIGN 3.7): a web view or the terminal gets every key first; only the commands marked as
            // working inside content are taken from there.
            if (scope == ShortcutScope.Content && !command.WorksInsideContent) continue;
            KeyboardShortcut? pressed;
            if (command.MatchesTypedCharacter)
            {
                if (!typedRead) { typedChord = typed(); typedRead = true; }
                pressed = typedChord;
            }
            else pressed = positional;
            if (pressed != held) continue;
            command.Action();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The key equivalent a key-down event presses (ED-099): its physical key as a scan code, that code's menu key
    /// equivalent (KeyNames.KeyEquivalent, Shortcuts.swift:80-101), and the Ctrl, Alt and Shift held with it. Null for
    /// a key no menu item can hold.
    /// </summary>
    public static KeyboardShortcut? Shortcut(KeyEventArgs e)
    {
        if (ScanCode(e.PhysicalKey) is not { } code || KeyNames.KeyEquivalent(code) is not { } key) return null;
        return new KeyboardShortcut(key, (ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers);
    }

    /// <summary>
    /// ED-270 (supersedes ED-230's position reading of "+"): the key equivalent a key-down presses by the character it
    /// types. A literal-character item (Zoom In "+", Zoom Out "-", Actual Size "0", the hidden "=" item; AppCommands.swift:53-62)
    /// is matched by what the key types on the active layout, as a menu does, so on a German keyboard the key that types
    /// "+" zooms in and the key at the US "-" position, which types "ß", does not. The numeric keypad's + and - type those
    /// characters on every layout (Shift or not). Shift is dropped from the chord when it is part of typing the character
    /// (Shift with the US "=" key types "+"; with the French "0" key, "0"), and kept when it is not (Shift with the US "0"
    /// key types ")", which no item holds). Null for a key that types no character.
    /// </summary>
    public static KeyboardShortcut? Typed(KeyEventArgs e)
    {
        var modifiers = (ChordModifiers)e.KeyModifiers & KeyChord.AllowedModifiers;
        if (e.PhysicalKey == PhysicalKey.NumPadAdd || e.Key == Key.Add)
            return new KeyboardShortcut(new KeyEquivalent("+"), modifiers & ~ChordModifiers.Shift);
        if (e.PhysicalKey == PhysicalKey.NumPadSubtract || e.Key == Key.Subtract)
            return new KeyboardShortcut(new KeyEquivalent("-"), modifiers & ~ChordModifiers.Shift);
        var layout = KeyboardLayoutText.Current.Current;
        var shift = modifiers.HasFlag(ChordModifiers.Shift);
        if (layout.Typed(e.PhysicalKey, shift) is not { Length: 1 } typed) return null;
        if (shift && layout.Typed(e.PhysicalKey, false) != typed) modifiers &= ~ChordModifiers.Shift;
        return new KeyboardShortcut(new KeyEquivalent(typed), modifiers);
    }

    /// <summary>
    /// Puts the dispatch on <paramref name="window"/> until the result is disposed: every key-down the window's own
    /// handlers left unconsumed is offered to <paramref name="perform"/>, and consumed when it returns true.
    /// </summary>
    public static IDisposable Attach(TopLevel window, Func<KeyboardShortcut, bool> perform) =>
        Attach(window, (positional, typed) =>
            (positional is { } position && perform(position)) || (typed() is { } character && perform(character)));

    /// <summary>
    /// The dispatch the app's windows carry (ED-270): <paramref name="perform"/> is given the key-down's chord by key
    /// position and, read only when asked, by the character typed (<see cref="Perform(IReadOnlyList{MenuCommand}, KeyboardShortcut?, Func{KeyboardShortcut?})"/>).
    /// </summary>
    public static IDisposable Attach(TopLevel window, Func<KeyboardShortcut?, Func<KeyboardShortcut?>, bool> perform) =>
        Attach(window, (positional, typed, _) => perform(positional, typed));

    /// <summary>
    /// The same, told the scope of the control holding focus (web W3b, DESIGN 3.7): <see cref="ShortcutScope.Content"/> in a web
    /// view or the terminal, where the content gets every key first.
    /// </summary>
    public static IDisposable Attach(TopLevel window, Func<KeyboardShortcut?, Func<KeyboardShortcut?>, ShortcutScope, bool> perform)
    {
        EventHandler<KeyEventArgs> onKeyDown = (_, e) =>
        {
            if (e.Handled) return;
            if (ShortcutScopeHost.FocusClaims(window, e)) return; // W8a: the focused pane answers this key itself (Visus's browser keys)
            if (perform(Shortcut(e), () => Typed(e), ShortcutScopeHost.ScopeOfFocus(window))) e.Handled = true;
        };
        window.AddHandler(InputElement.KeyDownEvent, onKeyDown, RoutingStrategies.Tunnel);
        return new Detachment(() => window.RemoveHandler(InputElement.KeyDownEvent, onKeyDown));
    }

    /// <summary>The Set-1 scan code of a key, as ED-099 (2) reads it; null for a key outside the table.</summary>
    internal static long? ScanCodeOf(PhysicalKey key) => ScanCode(key);

    // ED-099 (2): the Set-1 scan code of each key the US-position tables of Shortcuts.swift:52-69 name, which are the
    // only keys a menu key equivalent can be on (KeyNames.KeyEquivalent is nil for every other code). Any other
    // physical key reads as null.
    private static long? ScanCode(PhysicalKey key) => key switch
    {
        PhysicalKey.A => 0x1E,
        PhysicalKey.S => 0x1F,
        PhysicalKey.D => 0x20,
        PhysicalKey.F => 0x21,
        PhysicalKey.H => 0x23,
        PhysicalKey.G => 0x22,
        PhysicalKey.Z => 0x2C,
        PhysicalKey.X => 0x2D,
        PhysicalKey.C => 0x2E,
        PhysicalKey.V => 0x2F,
        PhysicalKey.B => 0x30,
        PhysicalKey.Q => 0x10,
        PhysicalKey.W => 0x11,
        PhysicalKey.E => 0x12,
        PhysicalKey.R => 0x13,
        PhysicalKey.Y => 0x15,
        PhysicalKey.T => 0x14,
        PhysicalKey.Digit1 => 0x02,
        PhysicalKey.Digit2 => 0x03,
        PhysicalKey.Digit3 => 0x04,
        PhysicalKey.Digit4 => 0x05,
        PhysicalKey.Digit6 => 0x07,
        PhysicalKey.Digit5 => 0x06,
        PhysicalKey.Equal => 0x0D,
        PhysicalKey.Digit9 => 0x0A,
        PhysicalKey.Digit7 => 0x08,
        PhysicalKey.Minus => 0x0C,
        PhysicalKey.Digit8 => 0x09,
        PhysicalKey.Digit0 => 0x0B,
        PhysicalKey.BracketRight => 0x1B,
        PhysicalKey.O => 0x18,
        PhysicalKey.U => 0x16,
        PhysicalKey.BracketLeft => 0x1A,
        PhysicalKey.I => 0x17,
        PhysicalKey.P => 0x19,
        PhysicalKey.L => 0x26,
        PhysicalKey.J => 0x24,
        PhysicalKey.Quote => 0x28,
        PhysicalKey.K => 0x25,
        PhysicalKey.Semicolon => 0x27,
        PhysicalKey.Backslash => 0x2B,
        PhysicalKey.Comma => 0x33,
        PhysicalKey.Slash => 0x35,
        PhysicalKey.N => 0x31,
        PhysicalKey.M => 0x32,
        PhysicalKey.Period => 0x34,
        PhysicalKey.Backquote => 0x29,
        PhysicalKey.Enter => 0x1C, // Return
        PhysicalKey.Tab => 0x0F,
        PhysicalKey.Space => 0x39,
        PhysicalKey.Backspace => 0x0E, // macOS Delete (51)
        PhysicalKey.Escape => 0x01,
        PhysicalKey.Delete => 0xE053, // macOS forward delete (117)
        PhysicalKey.ArrowLeft => 0xE04B,
        PhysicalKey.ArrowRight => 0xE04D,
        PhysicalKey.ArrowDown => 0xE050,
        PhysicalKey.ArrowUp => 0xE048,
        PhysicalKey.Home => 0xE047,
        PhysicalKey.End => 0xE04F,
        PhysicalKey.PageUp => 0xE049,
        PhysicalKey.PageDown => 0xE051,
        PhysicalKey.F1 => 0x3B,
        PhysicalKey.F2 => 0x3C,
        PhysicalKey.F3 => 0x3D,
        PhysicalKey.F4 => 0x3E,
        PhysicalKey.F5 => 0x3F,
        PhysicalKey.F6 => 0x40,
        PhysicalKey.F7 => 0x41,
        PhysicalKey.F8 => 0x42,
        PhysicalKey.F9 => 0x43,
        PhysicalKey.F10 => 0x44,
        PhysicalKey.F11 => 0x57,
        PhysicalKey.F12 => 0x58,
        _ => null,
    };

    private sealed class Detachment(Action detach) : IDisposable
    {
        private Action? pending = detach;

        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
