using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3b, the shortcut scope rule (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7 "Scope rule,
// three scopes", 10.1 "F6 (a reserved chord, so it works from inside a page or the terminal)", 13.1 "Shortcut scope tests";
// ED-NEW-web-w3b-1). No Swift counterpart. A pure function of the user's bindings: which chords nth takes from where.
// - nth chrome (the chat, the native panes, the sidebar): every nth chord applies.
// - A content scope (a web view, the terminal): the content gets every key first. nth takes only the reserved chords, the
//   ones that are rare in pages and shells and that every pane must be able to answer: F6 and Shift+F6, the Tactus Stop
//   chord, Ctrl+Shift+Backslash, Ctrl+Alt+Arrow and Ctrl+Shift+B. The list follows the user's bindings: a re-bound command
//   keeps its "works inside pages" mark and its new chord is the reserved one.

namespace Marrow.Core.Models;

/// <summary>Where the keyboard focus is, as far as shortcuts care (DESIGN 3.7).</summary>
public enum ShortcutScope
{
    /// <summary>nth's own controls: every nth chord applies.</summary>
    Chrome,

    /// <summary>A web view or the terminal: the content gets every key first; nth takes only the reserved chords.</summary>
    Content,
}

/// <summary>The three-scope rule of DESIGN 3.7 over the user's shortcut bindings.</summary>
public static class ShortcutScopes
{
    private const long Escape = 0x01;
    private const long Left = 0xE04B;
    private const long Right = 0xE04D;
    private const long Up = 0xE048;
    private const long Down = 0xE050;

    /// <summary>The Tactus Stop chord, Ctrl+Alt+Esc (DESIGN 3.7 and 8): fixed, not a re-bindable command in this tree.</summary>
    public static KeyChord TactusStop { get; } = new(Escape, ChordModifiers.Control | ChordModifiers.Alt);

    /// <summary>The commands nth takes from a content scope: the "works inside pages" mark in Settings.</summary>
    public static IReadOnlyList<AppShortcut> ReservedCommands { get; } =
    [
        AppShortcut.NextPane, AppShortcut.PreviousPane, AppShortcut.ExpandPane,
        AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater, AppShortcut.ToggleBrowser,
    ];

    /// <summary>Whether the command's chord reaches nth from inside a page or the terminal.</summary>
    public static bool WorksInsidePages(this AppShortcut shortcut) => ReservedCommands.Contains(shortcut);

    /// <summary>
    /// The chords nth takes from a content scope under the store's bindings: each reserved command's current chord (a
    /// cleared one has none), the Up and Down arrows that move a pane like Left and Right do, and the Tactus Stop chord.
    /// </summary>
    public static IReadOnlyList<KeyChord> ReservedChords(ShortcutStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var chords = new List<KeyChord>();
        foreach (var command in ReservedCommands)
        {
            if (store.Chord(command) is { } chord) chords.Add(chord);
            if (AliasOf(store, command) is { } alias) chords.Add(alias);
        }
        chords.Add(TactusStop);
        return chords;
    }

    /// <summary>Whether <paramref name="chord"/> is one nth takes from a content scope.</summary>
    public static bool IsReserved(ShortcutStore store, KeyChord chord) => ReservedChords(store).Contains(chord);

    /// <summary>
    /// The command a chord runs under the store's bindings, or null: the command bound to it, else the move command whose
    /// Left or Right arrow it is the Up or Down twin of (Ctrl+Alt+Up and Down move a pane like Left and Right, 3.6).
    /// </summary>
    public static AppShortcut? Resolve(ShortcutStore store, KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var command in SwiftEnum<AppShortcut>.AllCases)
            if (store.Chord(command) == chord) return command;
        return AliasOwner(store, chord);
    }

    /// <summary>The move command whose arrow chord <paramref name="chord"/> is the vertical twin of, or null.</summary>
    public static AppShortcut? AliasOwner(ShortcutStore store, KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var command in new[] { AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater })
            if (AliasOf(store, command) == chord) return command;
        return null;
    }

    /// <summary>
    /// Whether the scope lets nth run <paramref name="chord"/>: always in nth chrome; from a content scope only the
    /// reserved chords. A chord nth does not take reaches the page or the shell as typed.
    /// </summary>
    public static bool Takes(ShortcutScope scope, ShortcutStore store, KeyChord chord) =>
        scope == ShortcutScope.Chrome || IsReserved(store, chord);

    /// <summary>The Up twin of a Left chord and the Down twin of a Right chord (same modifiers); null for any other binding.</summary>
    private static KeyChord? AliasOf(ShortcutStore store, AppShortcut command)
    {
        if (command is not (AppShortcut.MovePaneEarlier or AppShortcut.MovePaneLater) || store.Chord(command) is not { } bound) return null;
        return (command, bound.KeyCode) switch
        {
            (AppShortcut.MovePaneEarlier, Left) => new KeyChord(Up, bound.ModifierFlags),
            (AppShortcut.MovePaneLater, Right) => new KeyChord(Down, bound.ModifierFlags),
            _ => null,
        };
    }
}
