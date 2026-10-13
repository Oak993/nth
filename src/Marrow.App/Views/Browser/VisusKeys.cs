using Avalonia.Input;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7 "the browser keys (Ctrl+T, Ctrl+L, Ctrl+F, F5,
// Ctrl+Shift+N, Alt+Left, Alt+Right, Ctrl+plus, Ctrl+minus, Ctrl+0) reach the page or the shell. Visus's own browser keys are
// handled by Visus, not the page, as a browser does"; the canvas's "Ctrl+T opens a new tab · Ctrl+Shift+N a private one";
// ED-NEW-web-w8a-2). No Swift counterpart. The keys Visus answers as a browser does, from inside its page (the engine's
// accelerator event) and from its own chrome (the tab strip, the toolbar, the address field). They are the standard browser
// keys, fixed like a browser's; none is a reserved chord, so F6, Stop, Expand, Move and Ctrl+Shift+B still reach nth first.

namespace Marrow.App.Views.Browser;

/// <summary>What a browser key does in Visus.</summary>
public enum VisusCommand
{
    NewTab,
    NewPrivateTab,
    CloseTab,
    ReopenClosedTab,
    FocusAddress,
    Reload,
    HardReload,
    Back,
    Forward,
    NextTab,
    PreviousTab,
    SelectTab1,
    SelectTab2,
    SelectTab3,
    SelectTab4,
    SelectTab5,
    SelectTab6,
    SelectTab7,
    SelectTab8,
    SelectLastTab,
    Find,
    ZoomIn,
    ZoomOut,
    ResetZoom,
    ShowZoom,
    Print,
    SavePdf,
    PickElement,
}

/// <summary>DESIGN 3.7: Visus's browser keys.</summary>
public static class VisusKeys
{
    private const ChordModifiers Ctrl = ChordModifiers.Control;
    private const ChordModifiers Shift = ChordModifiers.Shift;
    private const ChordModifiers Alt = ChordModifiers.Alt;

    // ED-099 scan codes (Set 1; extended keys as 0xE000 | code).
    private static readonly Dictionary<KeyChord, VisusCommand> Table = new()
    {
        [new KeyChord(0x21, Ctrl)] = VisusCommand.Find,
        [new KeyChord(0x19, Ctrl)] = VisusCommand.Print,
        [new KeyChord(0x0D, Ctrl)] = VisusCommand.ZoomIn,
        [new KeyChord(0x0D, Ctrl | Shift)] = VisusCommand.ZoomIn,
        [new KeyChord(0x4E, Ctrl)] = VisusCommand.ZoomIn,
        [new KeyChord(0x0C, Ctrl)] = VisusCommand.ZoomOut,
        [new KeyChord(0x4A, Ctrl)] = VisusCommand.ZoomOut,
        [new KeyChord(0x0B, Ctrl)] = VisusCommand.ResetZoom,
        [new KeyChord(0x12, Ctrl | Shift)] = VisusCommand.PickElement,
        [new KeyChord(0x14, Ctrl)] = VisusCommand.NewTab, // Ctrl+T
        [new KeyChord(0x31, Ctrl | Shift)] = VisusCommand.NewPrivateTab, // Ctrl+Shift+N
        [new KeyChord(0x11, Ctrl)] = VisusCommand.CloseTab, // Ctrl+W
        [new KeyChord(0x3E, Ctrl)] = VisusCommand.CloseTab, // Ctrl+F4
        [new KeyChord(0x14, Ctrl | Shift)] = VisusCommand.ReopenClosedTab, // Ctrl+Shift+T
        [new KeyChord(0x26, Ctrl)] = VisusCommand.FocusAddress, // Ctrl+L
        [new KeyChord(0x20, Alt)] = VisusCommand.FocusAddress, // Alt+D
        [new KeyChord(0x3F, ChordModifiers.None)] = VisusCommand.Reload, // F5
        [new KeyChord(0x13, Ctrl)] = VisusCommand.Reload, // Ctrl+R
        [new KeyChord(0x3F, Shift)] = VisusCommand.HardReload, // Shift+F5
        [new KeyChord(0x3F, Ctrl)] = VisusCommand.HardReload, // Ctrl+F5
        [new KeyChord(0x13, Ctrl | Shift)] = VisusCommand.HardReload, // Ctrl+Shift+R
        [new KeyChord(0xE04B, Alt)] = VisusCommand.Back, // Alt+Left
        [new KeyChord(0xE04D, Alt)] = VisusCommand.Forward, // Alt+Right
        [new KeyChord(0x0F, Ctrl)] = VisusCommand.NextTab, // Ctrl+Tab
        [new KeyChord(0xE051, Ctrl)] = VisusCommand.NextTab, // Ctrl+PageDown
        [new KeyChord(0x0F, Ctrl | Shift)] = VisusCommand.PreviousTab, // Ctrl+Shift+Tab
        [new KeyChord(0xE049, Ctrl)] = VisusCommand.PreviousTab, // Ctrl+PageUp
        [new KeyChord(0x02, Ctrl)] = VisusCommand.SelectTab1,
        [new KeyChord(0x03, Ctrl)] = VisusCommand.SelectTab2,
        [new KeyChord(0x04, Ctrl)] = VisusCommand.SelectTab3,
        [new KeyChord(0x05, Ctrl)] = VisusCommand.SelectTab4,
        [new KeyChord(0x06, Ctrl)] = VisusCommand.SelectTab5,
        [new KeyChord(0x07, Ctrl)] = VisusCommand.SelectTab6,
        [new KeyChord(0x08, Ctrl)] = VisusCommand.SelectTab7,
        [new KeyChord(0x09, Ctrl)] = VisusCommand.SelectTab8,
        [new KeyChord(0x0A, Ctrl)] = VisusCommand.SelectLastTab, // Ctrl+9
    };

    /// <summary>Every chord Visus answers, for the shortcut list.</summary>
    public static IReadOnlyCollection<KeyChord> Chords => Table.Keys;

    /// <summary>The command a chord runs in Visus, or null.</summary>
    public static VisusCommand? CommandFor(KeyChord chord) => Table.TryGetValue(chord, out var command) ? command : null;

    /// <summary>The command a key-down in Visus's own chrome runs, or null.</summary>
    public static VisusCommand? CommandFor(KeyEventArgs e) => PaneShortcuts.ChordOf(e) is { } chord ? CommandFor(chord) : null;

    /// <summary>The text a menu shows for a command's first chord ("Ctrl+T").</summary>
    public static string? Label(VisusCommand command) => command switch
    {
        VisusCommand.Find => "Ctrl+F",
        VisusCommand.Print => "Ctrl+P",
        VisusCommand.ZoomIn => "Ctrl++",
        VisusCommand.ZoomOut => "Ctrl+-",
        VisusCommand.ResetZoom => "Ctrl+0",
        VisusCommand.NewTab => "Ctrl+T",
        VisusCommand.PickElement => "Ctrl+Shift+E",
        VisusCommand.NewPrivateTab => "Ctrl+Shift+N",
        VisusCommand.CloseTab => "Ctrl+W",
        VisusCommand.ReopenClosedTab => "Ctrl+Shift+T",
        VisusCommand.FocusAddress => "Ctrl+L",
        VisusCommand.Reload => "F5",
        VisusCommand.HardReload => "Shift+F5",
        VisusCommand.Back => "Alt+Left",
        VisusCommand.Forward => "Alt+Right",
        VisusCommand.NextTab => "Ctrl+Tab",
        VisusCommand.PreviousTab => "Ctrl+Shift+Tab",
        _ => null,
    };
}
