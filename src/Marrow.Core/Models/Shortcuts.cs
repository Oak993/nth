using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Shortcuts.swift (B2 1fbf81e), :1-335.
//
// ED-099 fixes the chord format, and every chord value in this file follows it:
// (1) KeyChord.KeyCode is the Windows Set-1 scan code of the physical key, extended keys as 0xE000 | code, in
//     place of the macOS virtual key code (:4, :37). The US-position tables of :52-69 map one for one onto those
//     codes (ED-099 (2)); labels, typesText and the menu key equivalents keep the macOS lists, keyed by these codes.
// (2) Modifiers are ChordModifiers (Alt 1, Control 2, Shift 4; Port/Keyboard.cs): Command and Control both become
//     Control, Option becomes Alt, and the Win key is masked out as fn is (ED-099 (3), ED-050 (a)).
// (3) A chord reads "Ctrl+", "Alt+", "Shift+" in that order before the key label (OD-0008 B7, ED-099 (4)).
// (4) The defaults key stays "keyboardShortcuts" with the Swift JSON shape, keyCode and modifiers (ED-099 (5)).
// (5) SwiftUI's KeyboardShortcut, KeyEquivalent and EventModifiers are the port values of Port/Keyboard.cs (ED-099 (6)).

/// <summary>:4-5 A physical key and the modifiers held with it (<c>struct KeyChord: Codable, Hashable, Sendable</c>).</summary>
/// <remarks>
/// A Swift struct is an immutable sealed record (R-VAL-1): its generated equality compares KeyCode and the stored
/// Modifiers bits, as the synthesized Hashable does (:5). Swift <c>Int</c> is <c>long</c> and <c>UInt</c> is
/// <c>ulong</c> (R-NUM-4). The synthesized Decodable assigns both stored properties without running the masking
/// init (:11-14), so a decoded chord can hold bits outside the allowed set; <see cref="ModifierFlags"/> reads
/// only the allowed ones (:16-18).
/// </remarks>
public sealed record KeyChord
{
    // :6 static let allowedModifiers: [.control, .option, .shift, .command]; ED-099 (3): Control | Alt | Shift.
    public static ChordModifiers AllowedModifiers { get; } = ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift;

    // :8 var keyCode: Int (ED-099 (1): the scan code)
    public long KeyCode { get; init; }

    // :9 var modifiers: UInt (the raw bits of ChordModifiers)
    public ulong Modifiers { get; init; }

    // :11-14 init(keyCode:modifiers:)
    public KeyChord(long keyCode, ChordModifiers modifiers)
    {
        KeyCode = keyCode; // :12
        Modifiers = (ulong)(modifiers & AllowedModifiers); // :13
    }

    // The synthesized init(from:) (:5) sets the stored properties directly.
    private KeyChord()
    {
    }

    // :16-18 var modifierFlags
    public ChordModifiers ModifierFlags => (ChordModifiers)(Modifiers & (ulong)AllowedModifiers);

    // :20-30 The chord as SwiftUI menus take it, or nil for a key a menu cannot hold.
    public KeyboardShortcut? KeyboardShortcut
    {
        get
        {
            if (KeyNames.KeyEquivalent(KeyCode) is not { } key) return null; // :22
            var events = ChordModifiers.None; // :23
            var flags = ModifierFlags; // :24
            // :25-28 control and command both insert Control (ED-099 (3)); option inserts Alt; shift inserts Shift.
            if (flags.HasFlag(ChordModifiers.Control)) events |= ChordModifiers.Control;
            if (flags.HasFlag(ChordModifiers.Alt)) events |= ChordModifiers.Alt;
            if (flags.HasFlag(ChordModifiers.Shift)) events |= ChordModifiers.Shift;
            return new Port.KeyboardShortcut(key, events); // :29
        }
    }

    // :32-34 var description
    public string Description => KeyNames.ModifierSymbols(ModifierFlags) + KeyNames.Label(KeyCode);

    /// <summary>The synthesized <c>init(from:)</c> (:5, :8-9): both keys required, strict types (R-CODE-1).</summary>
    public static KeyChord Decode(JsonElement element) => new()
    {
        KeyCode = SwiftDecoding.Decode(element, "keyCode", SwiftDecoding.ReadInt), // :8
        Modifiers = SwiftDecoding.Decode(element, "modifiers", SwiftDecoding.ReadUInt), // :9
    };

    /// <summary>The synthesized <c>encode(to:)</c> (:5, :8-9): the stored properties in declaration order (R-CODE-3).</summary>
    public static void Encode(Utf8JsonWriter writer, KeyChord chord)
    {
        writer.WriteStartObject();
        writer.WriteNumber("keyCode", chord.KeyCode); // :8
        writer.WriteNumber("modifiers", chord.Modifiers); // :9
        writer.WriteEndObject();
    }
}

/// <summary>:37-38 Key labels and menu key equivalents for the US key positions (a caseless enum).</summary>
public static class KeyNames
{
    // :39-41 static func isModifierKey(_:). macOS 54-63 (right Command, left Command, left Shift, Caps Lock, left
    // Option, left Control, right Shift, right Option, right Control, fn) are, by ED-099 (2), LShift 0x2A,
    // RShift 0x36, LCtrl 0x1D, RCtrl 0xE01D, LAlt 0x38, RAlt 0xE038, LWin 0xE05B, RWin 0xE05C and CapsLock 0x3A;
    // fn has no Windows scan code.
    private static readonly long[] ModifierKeys = [0x2A, 0x36, 0x1D, 0xE01D, 0x38, 0xE038, 0xE05B, 0xE05C, 0x3A];

    public static bool IsModifierKey(long code) => ModifierKeys.Contains(code);

    // :43-50 static func modifierSymbols(_:). DEVIATION OD-0008 B7 (ED-099 (4)): upstream appends the glyphs
    // U+2303, U+2325, U+21E7 and U+2318 for control, option, shift and command with no separator; the port writes
    // "Ctrl+", "Alt+" and "Shift+" in that order (command and control are both Control, ED-099 (3)).
    public static string ModifierSymbols(ChordModifiers flags)
    {
        var text = ""; // :44
        if (flags.HasFlag(ChordModifiers.Control)) text += "Ctrl+"; // :45, :48
        if (flags.HasFlag(ChordModifiers.Alt)) text += "Alt+"; // :46
        if (flags.HasFlag(ChordModifiers.Shift)) text += "Shift+"; // :47
        return text; // :49
    }

    // :52-58 private static let characters, keyed by the scan codes of ED-099 (2); the macOS code is noted on each.
    private static readonly Dictionary<long, string> Characters = new()
    {
        [0x1E] = "A", // 0
        [0x1F] = "S", // 1
        [0x20] = "D", // 2
        [0x21] = "F", // 3
        [0x23] = "H", // 4
        [0x22] = "G", // 5
        [0x2C] = "Z", // 6
        [0x2D] = "X", // 7
        [0x2E] = "C", // 8
        [0x2F] = "V", // 9
        [0x30] = "B", // 11
        [0x10] = "Q", // 12
        [0x11] = "W", // 13
        [0x12] = "E", // 14
        [0x13] = "R", // 15
        [0x15] = "Y", // 16
        [0x14] = "T", // 17
        [0x02] = "1", // 18
        [0x03] = "2", // 19
        [0x04] = "3", // 20
        [0x05] = "4", // 21
        [0x07] = "6", // 22
        [0x06] = "5", // 23
        [0x0D] = "=", // 24
        [0x0A] = "9", // 25
        [0x08] = "7", // 26
        [0x0C] = "-", // 27
        [0x09] = "8", // 28
        [0x0B] = "0", // 29
        [0x1B] = "]", // 30
        [0x18] = "O", // 31
        [0x16] = "U", // 32
        [0x1A] = "[", // 33
        [0x17] = "I", // 34
        [0x19] = "P", // 35
        [0x26] = "L", // 37
        [0x24] = "J", // 38
        [0x28] = "'", // 39
        [0x25] = "K", // 40
        [0x27] = ";", // 41
        [0x2B] = "\\", // 42
        [0x33] = ",", // 43
        [0x35] = "/", // 44
        [0x31] = "N", // 45
        [0x32] = "M", // 46
        [0x34] = ".", // 47
        [0x29] = "`", // 50
    };

    // :60-65 private static let specialLabels, keyed by the scan codes of ED-099 (2).
    private static readonly Dictionary<long, string> SpecialLabels = new()
    {
        [0x1C] = "Return", // 36
        [0x0F] = "Tab", // 48
        [0x39] = "Space", // 49
        [0x0E] = "Delete", // 51 (backspace)
        [0x01] = "Esc", // 53
        [0xE053] = "Del", // 117 (forward delete)
        [0xE04B] = "\U00002190", // 123, U+2190 LEFTWARDS ARROW
        [0xE04D] = "\U00002192", // 124, U+2192 RIGHTWARDS ARROW
        [0xE050] = "\U00002193", // 125, U+2193 DOWNWARDS ARROW
        [0xE048] = "\U00002191", // 126, U+2191 UPWARDS ARROW
        [0xE047] = "Home", // 115
        [0xE04F] = "End", // 119
        [0xE049] = "PgUp", // 116
        [0xE051] = "PgDn", // 121
        [0x3B] = "F1", // 122
        [0x3C] = "F2", // 120
        [0x3D] = "F3", // 99
        [0x3E] = "F4", // 118
        [0x3F] = "F5", // 96
        [0x40] = "F6", // 97
        [0x41] = "F7", // 98
        [0x42] = "F8", // 100
        [0x43] = "F9", // 101
        [0x44] = "F10", // 109
        [0x57] = "F11", // 103
        [0x58] = "F12", // 111
    };

    // :67-69 private static let functionKeys, keyed by the scan codes of ED-099 (2).
    private static readonly Dictionary<long, int> FunctionKeys = new()
    {
        [0x3B] = 1, // 122
        [0x3C] = 2, // 120
        [0x3D] = 3, // 99
        [0x3E] = 4, // 118
        [0x3F] = 5, // 96
        [0x40] = 6, // 97
        [0x41] = 7, // 98
        [0x42] = 8, // 100
        [0x43] = 9, // 101
        [0x44] = 10, // 109
        [0x57] = 11, // 103
        [0x58] = 12, // 111
    };

    // :71-73 static func label(for:). An unknown key reads "Key <scan code in decimal>" (ED-099 (2), R-NUM-2).
    public static string Label(long code) =>
        Characters.TryGetValue(code, out var character) ? character
        : SpecialLabels.TryGetValue(code, out var special) ? special
        : "Key " + code.ToString(CultureInfo.InvariantCulture);

    // :76 Return, Tab, Space, Delete, Esc, Del, left, right, down, up (macOS 36, 48, 49, 51, 53, 117, 123-126).
    private static readonly long[] TextKeys = [0x1C, 0x0F, 0x39, 0x0E, 0x01, 0xE053, 0xE04B, 0xE04D, 0xE050, 0xE048];

    // :75-78 Keys that type or move the caret, which a chord without Command, Control or Option would steal from text.
    public static bool TypesText(long code) => Characters.ContainsKey(code) || TextKeys.Contains(code);

    // :80-101 static func keyEquivalent(for:) (ED-099 (6): KeyEquivalent.Name is the SwiftUI spelling).
    public static KeyEquivalent? KeyEquivalent(long code)
    {
        // :81 characters[code]?.lowercased().first
        if (Characters.TryGetValue(code, out var label) &&
            SwiftString.Characters(SwiftString.Lowercased(label)).FirstOrDefault() is { } character)
            return new Port.KeyEquivalent(character);
        switch (code) // :82
        {
            case 0x1C: return Port.KeyEquivalent.Return; // :83 (36)
            case 0x0F: return Port.KeyEquivalent.Tab; // :84 (48)
            case 0x39: return Port.KeyEquivalent.Space; // :85 (49)
            case 0x0E: return Port.KeyEquivalent.Delete; // :86 (51)
            case 0x01: return Port.KeyEquivalent.Escape; // :87 (53)
            case 0xE053: return Port.KeyEquivalent.DeleteForward; // :88 (117)
            case 0xE04B: return Port.KeyEquivalent.LeftArrow; // :89 (123)
            case 0xE04D: return Port.KeyEquivalent.RightArrow; // :90 (124)
            case 0xE050: return Port.KeyEquivalent.DownArrow; // :91 (125)
            case 0xE048: return Port.KeyEquivalent.UpArrow; // :92 (126)
            case 0xE047: return Port.KeyEquivalent.Home; // :93 (115)
            case 0xE04F: return Port.KeyEquivalent.End; // :94 (119)
            case 0xE049: return Port.KeyEquivalent.PageUp; // :95 (116)
            case 0xE051: return Port.KeyEquivalent.PageDown; // :96 (121)
            default: // :97-99 NSF1FunctionKey + number - 1, which KeyEquivalent.Function stands for (ED-099 (6))
                if (!FunctionKeys.TryGetValue(code, out var number)) return null;
                return Port.KeyEquivalent.Function(number);
        }
    }
}

/// <summary>:104-105 Every command a shortcut can be recorded for, with the chord it ships with.</summary>
/// <remarks>R-ENUM-1: <c>enum AppShortcut: String, CaseIterable, Identifiable</c>, raw values written out.</remarks>
public enum AppShortcut
{
    [RawValue("newThread")] NewThread, // :106
    [RawValue("newWorktreeThread")] NewWorktreeThread, // :107
    [RawValue("addProject")] AddProject, // :108
    [RawValue("stopTurn")] StopTurn, // :109
    [RawValue("queueChat")] QueueChat, // :110
    [RawValue("togglePlanMode")] TogglePlanMode, // :111
    [RawValue("previousThread")] PreviousThread, // :112
    [RawValue("nextThread")] NextThread, // :113
    [RawValue("runScript")] RunScript, // :114

    // :115-117 Settles or archives the selected thread, as Settings chose. Stored under its old name, so a chord
    // recorded for "Archive thread" still applies.
    [RawValue("archiveThread")] FinishThread,

    // :118-119 Asks whether to archive the selected thread or delete it.
    [RawValue("archiveThreadPrompt")] ArchiveThread,

    // :120-121 Settles the selected thread, or reopens one that is settled.
    [RawValue("settleThread")] SettleThread,
    [RawValue("commandPalette")] CommandPalette, // :122
    [RawValue("toggleSidebar")] ToggleSidebar, // :123
    [RawValue("toggleTerminal")] ToggleTerminal, // :124
    [RawValue("toggleChanges")] ToggleChanges, // :125
    [RawValue("toggleActivityView")] ToggleActivityView, // :126
    [RawValue("showMainWindow")] ShowMainWindow, // :127
    [RawValue("openSettings")] OpenSettings, // :128
}

/// <summary>
/// :130 <c>enum Section: String, CaseIterable, Identifiable</c>, nested in AppShortcut in Swift; a C# enum cannot
/// nest a type, so it is the top-level <c>AppShortcutSection</c>.
/// </summary>
public enum AppShortcutSection
{
    [RawValue("threads")] Threads, // :131
    [RawValue("window")] Window, // :132
}

/// <summary>The computed members of <see cref="AppShortcutSection"/> (:134-152, R-NAME-4).</summary>
public static class AppShortcutSectionExtensions
{
    // :134 var id: String { rawValue }
    public static string Id(this AppShortcutSection section) => SwiftEnum<AppShortcutSection>.RawValue(section);

    // :136-141 var title
    public static string Title(this AppShortcutSection section) => section switch
    {
        AppShortcutSection.Threads => "Threads", // :138
        AppShortcutSection.Window => "Window and panels", // :139
    };

    // :143-148 var symbol: the SF Symbol name, which the UI maps to its Lucide icon (OD-0010).
    public static string Symbol(this AppShortcutSection section) => section switch
    {
        AppShortcutSection.Threads => "bubble.left.and.bubble.right.fill", // :145
        AppShortcutSection.Window => "macwindow", // :146
    };

    // :150-152 var shortcuts: the commands of this section in declaration order.
    public static IReadOnlyList<AppShortcut> Shortcuts(this AppShortcutSection section) =>
        SwiftEnum<AppShortcut>.AllCases.Where(shortcut => shortcut.Section() == section).ToArray();
}

/// <summary>The computed members of <see cref="AppShortcut"/> (:155-236, R-NAME-4).</summary>
public static class AppShortcutExtensions
{
    // :155 var id: String { rawValue }
    public static string Id(this AppShortcut shortcut) => SwiftEnum<AppShortcut>.RawValue(shortcut);

    // :157-164 var section
    public static AppShortcutSection Section(this AppShortcut shortcut) => shortcut switch
    {
        // :159-160
        AppShortcut.NewThread or AppShortcut.NewWorktreeThread or AppShortcut.AddProject or AppShortcut.StopTurn or
            AppShortcut.QueueChat or AppShortcut.TogglePlanMode or AppShortcut.PreviousThread or AppShortcut.NextThread or
            AppShortcut.RunScript or AppShortcut.FinishThread or AppShortcut.ArchiveThread or AppShortcut.SettleThread =>
            AppShortcutSection.Threads,
        // :161-162
        AppShortcut.CommandPalette or AppShortcut.ToggleSidebar or AppShortcut.ToggleTerminal or AppShortcut.ToggleChanges or
            AppShortcut.ToggleActivityView or AppShortcut.ShowMainWindow or AppShortcut.OpenSettings =>
            AppShortcutSection.Window,
    };

    // :166-188 var title
    public static string Title(this AppShortcut shortcut) => shortcut switch
    {
        AppShortcut.NewThread => "New thread", // :168
        AppShortcut.NewWorktreeThread => "New thread in worktree", // :169
        AppShortcut.AddProject => "Add project", // :170
        AppShortcut.StopTurn => "Stop the running turn", // :171
        AppShortcut.QueueChat => "Queue a chat", // :172
        AppShortcut.TogglePlanMode => "Plan mode", // :173
        AppShortcut.PreviousThread => "Previous thread", // :174
        AppShortcut.NextThread => "Next thread", // :175
        AppShortcut.RunScript => "Run top script", // :176
        AppShortcut.FinishThread => "Finish thread", // :177
        AppShortcut.ArchiveThread => "Archive thread", // :178
        AppShortcut.SettleThread => "Settle thread", // :179
        AppShortcut.CommandPalette => "Command palette", // :180
        AppShortcut.ToggleSidebar => "Toggle sidebar", // :181
        AppShortcut.ToggleTerminal => "Toggle terminal", // :182
        AppShortcut.ToggleChanges => "Toggle changes", // :183
        AppShortcut.ToggleActivityView => "Activity view", // :184
        // :185 DEVIATION OD-0002, OD-0012 F1 (PR-SETTINGS-210): upstream reads "Show Droppy Code".
        AppShortcut.ShowMainWindow => "Show Nth",
        AppShortcut.OpenSettings => "Settings", // :186
    };

    // :190-212 var detail
    public static string Detail(this AppShortcut shortcut) => shortcut switch
    {
        AppShortcut.NewThread => "Starts a thread in the current project.", // :192
        AppShortcut.NewWorktreeThread => "Starts a thread in its own git worktree.", // :193
        AppShortcut.AddProject => "Opens a folder as a project.", // :194
        AppShortcut.StopTurn => "Interrupts the agent while it works.", // :195
        AppShortcut.QueueChat => "While a turn runs, lines the draft up behind it.", // :196
        AppShortcut.TogglePlanMode => "Plans before building.", // :197
        AppShortcut.PreviousThread => "Selects the thread above in the sidebar.", // :198
        AppShortcut.NextThread => "Selects the thread below in the sidebar.", // :199
        AppShortcut.RunScript => "Runs the first project script in the selected thread's terminal.", // :200
        AppShortcut.FinishThread => "Settles the selected thread, or archives it, whichever General chose. Reopens a settled one.", // :201
        AppShortcut.ArchiveThread => "Asks to archive the selected thread; Return archives it, and Delete is a click away.", // :202
        AppShortcut.SettleThread => "Settles the selected thread. Reopens one that is settled.", // :203
        AppShortcut.CommandPalette => "Searches threads, projects and actions.", // :204
        AppShortcut.ToggleSidebar => "Shows or hides the sidebar.", // :205
        AppShortcut.ToggleTerminal => "Shows or hides the thread's terminal.", // :206
        AppShortcut.ToggleChanges => "Shows or hides the changes panel.", // :207
        AppShortcut.ToggleActivityView => "Lists every thread by when it was last active.", // :208
        AppShortcut.ShowMainWindow => "Brings the main window to the front.", // :209
        AppShortcut.OpenSettings => "Opens this window.", // :210
    };

    // :214-236 var defaultChord, a fresh value on every read. Each macOS key code is replaced by its scan code and
    // each modifier by ChordModifiers (ED-099 (2), (3); ED-050 (a)); the macOS code is noted on each line.
    public static KeyChord? DefaultChord(this AppShortcut shortcut) => shortcut switch
    {
        AppShortcut.NewThread => new KeyChord(0x31, ChordModifiers.Control), // :216 (45 N, .command)
        AppShortcut.NewWorktreeThread => new KeyChord(0x31, ChordModifiers.Shift | ChordModifiers.Control), // :217 (45 N, [.shift, .command])
        AppShortcut.AddProject => new KeyChord(0x18, ChordModifiers.Control), // :218 (31 O, .command)
        AppShortcut.StopTurn => new KeyChord(0x34, ChordModifiers.Control), // :219 (47 ., .command)
        AppShortcut.QueueChat => new KeyChord(0x1C, ChordModifiers.Control), // :220 (36 Return, .command)
        AppShortcut.TogglePlanMode => new KeyChord(0x19, ChordModifiers.Shift | ChordModifiers.Control), // :221 (35 P, [.shift, .command])
        AppShortcut.PreviousThread => new KeyChord(0x0F, ChordModifiers.Control | ChordModifiers.Shift), // :222 (48 Tab, [.control, .shift])
        AppShortcut.NextThread => new KeyChord(0x0F, ChordModifiers.Control), // :223 (48 Tab, .control)
        AppShortcut.RunScript => new KeyChord(0x13, ChordModifiers.Control), // :224 (15 R, .command)
        AppShortcut.FinishThread => new KeyChord(0x0E, ChordModifiers.Shift | ChordModifiers.Control), // :225 (51 Delete, [.shift, .command])
        AppShortcut.ArchiveThread => new KeyChord(0x11, ChordModifiers.Control), // :226 (13 W, .command)
        AppShortcut.SettleThread => new KeyChord(0x1F, ChordModifiers.Control), // :227 (1 S, .command)
        AppShortcut.CommandPalette => new KeyChord(0x25, ChordModifiers.Control), // :228 (40 K, .command)
        AppShortcut.ToggleSidebar => new KeyChord(0x30, ChordModifiers.Control), // :229 (11 B, .command)
        AppShortcut.ToggleTerminal => new KeyChord(0x24, ChordModifiers.Control), // :230 (38 J, .command)
        AppShortcut.ToggleChanges => new KeyChord(0x20, ChordModifiers.Control), // :231 (2 D, .command)
        AppShortcut.ToggleActivityView => new KeyChord(0x16, ChordModifiers.Alt | ChordModifiers.Control), // :232 (32 U, [.option, .command])
        // :233 DEVIATION OD-0012 G6 (PR-SETTINGS-210): upstream ships KeyChord(keyCode: 29, modifiers: .command), the
        // same chord as the hard-coded Actual Size command (AppCommands.swift:57-58); in Marrow Ctrl+0 is Actual Size
        // and "Show Marrow" has no default chord.
        AppShortcut.ShowMainWindow => null,
        AppShortcut.OpenSettings => new KeyChord(0x33, ChordModifiers.Control), // :234 (43 ",", .command)
    };
}

/// <summary>
/// :239-242 The user's shortcuts. Only changed chords are stored; a stored empty chord means the shortcut was
/// cleared. <c>@MainActor @Observable final class ShortcutStore</c> is a <see cref="MainActorModel"/> (R-OBS-6).
/// </summary>
public sealed class ShortcutStore : MainActorModel
{
    // :243 static let shared = ShortcutStore(); an Ambient so tests can replace it (R-LIFE-2). Its initializer reads
    // UserDefaults, so it runs lazily, once, on first access.
    public static Ambient<ShortcutStore> Shared { get; } = new(() => new ShortcutStore());

    // :245-247 private struct Override: Codable, Hashable { var chord: KeyChord? }
    private sealed record Override(KeyChord? Chord)
    {
        // Synthesized init(from:): chord is decodeIfPresent (missing or null is nil); unknown keys are ignored.
        public static Override Decode(JsonElement element) =>
            new(SwiftDecoding.DecodeIfPresent(element, "chord", KeyChord.Decode));

        // Synthesized encode(to:): a nil chord is omitted (encodeIfPresent), so a cleared override is {}.
        public static void Encode(Utf8JsonWriter writer, Override value)
        {
            writer.WriteStartObject();
            if (value.Chord is { } chord)
            {
                writer.WritePropertyName("chord");
                KeyChord.Encode(writer, chord);
            }
            writer.WriteEndObject();
        }
    }

    // :249
    private const string DefaultsKey = "keyboardShortcuts";

    // :251-257 Chords macOS and the app already answer to, which no command may take. The same 18 chords under
    // ED-099 (OD-0008 B8): Ctrl with Q, H, M, C, V, X, A, Z; Ctrl+Shift+Z; Ctrl with 1 to 9. The macOS codes are noted.
    private static readonly KeyChord[] Reserved = BuildReserved();

    private static KeyChord[] BuildReserved()
    {
        // :253 [12, 4, 46, 8, 9, 7, 0, 6] with .command
        var chords = new long[] { 0x10, 0x23, 0x32, 0x2E, 0x2F, 0x2D, 0x1E, 0x2C }
            .Select(code => new KeyChord(code, ChordModifiers.Control)).ToList();
        chords.Add(new KeyChord(0x2C, ChordModifiers.Shift | ChordModifiers.Control)); // :254 (6 with [.shift, .command])
        // :255 [18, 19, 20, 21, 23, 22, 26, 28, 25] with .command
        chords.AddRange(new long[] { 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A }
            .Select(code => new KeyChord(code, ChordModifiers.Control)));
        return chords.ToArray(); // :256
    }

    // :259-265 private var overrides: [String: Override], with a didSet that re-encodes the whole dictionary with a
    // plain JSONEncoder (R-CODE-4 PlainEncoder) and writes it to UserDefaults.standard on every assignment. The keys
    // are stored text, compared as Swift compares strings (R-VAL-2).
    private ValueMap<string, Override> overrides;

    private ValueMap<string, Override> Overrides
    {
        get => Read(overrides);
        set => Set(ref overrides, value, didSet: _ =>
        {
            // :261-263 if let data = try? JSONEncoder().encode(overrides) { UserDefaults.standard.set(data, forKey:) }
            if (SwiftTry.Optional(() => EncodeOverrides(overrides)) is { } data)
                UserDefaults.Standard.Current.Set(DefaultsKey, data);
        });
    }

    // :267-274 private init(). A backing-field assignment: Swift runs no didSet in init, so loading writes nothing.
    private ShortcutStore()
    {
        if (UserDefaults.Standard.Current.Data(DefaultsKey) is { } data && // :268
            SwiftTry.Optional(() => DecodeOverrides(data)) is { } stored) // :269
            overrides = stored; // :270
        else
            overrides = new ValueMap<string, Override>([], SwiftStringComparer.Instance); // :272
    }

    // :269 JSONDecoder().decode([String: Override].self, from: data)
    private static ValueMap<string, Override> DecodeOverrides(byte[] data)
    {
        using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
        return new ValueMap<string, Override>(SwiftDecoding.ReadMap(document.RootElement, Override.Decode), SwiftStringComparer.Instance);
    }

    // :261 JSONEncoder().encode(overrides)
    private static byte[] EncodeOverrides(ValueMap<string, Override> map)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in map)
            {
                writer.WritePropertyName(key);
                Override.Encode(writer, value);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory, SwiftJson.DocumentOptions);
        return SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder);
    }

    // :276-279 func chord(for:): an override wins outright, even when its chord is nil.
    public KeyChord? Chord(AppShortcut shortcut)
    {
        if (Overrides.TryGetValue(SwiftEnum<AppShortcut>.RawValue(shortcut), out var @override)) return @override.Chord; // :277
        return shortcut.DefaultChord(); // :278
    }

    // :281-283 func keyboardShortcut(for:)
    public KeyboardShortcut? KeyboardShortcut(AppShortcut shortcut) => Chord(shortcut)?.KeyboardShortcut;

    // :285-290 A command's chord as it reads in a label, or nil when the command has none. Read through the shared
    // store, so a remap corrects every label that shows it.
    public static string? Label(AppShortcut shortcut) => Shared.Current.Chord(shortcut)?.Description;

    // :292-298 The same chord as a tail for a help string: "Hide terminal" + Hint(ToggleTerminal) reads
    // "Hide terminal (Ctrl+J)", and an empty string once the chord is cleared.
    public static string Hint(AppShortcut shortcut)
    {
        if (Label(shortcut) is not { } label) return ""; // :296
        return " (" + label + ")"; // :297
    }

    // :300-302 func set(_:for:): a chord equal to the default removes the override.
    public void Set(KeyChord? chord, AppShortcut shortcut)
    {
        var key = SwiftEnum<AppShortcut>.RawValue(shortcut);
        var @override = chord == shortcut.DefaultChord() ? null : new Override(chord); // :301
        Overrides = Overrides.Mutating(map =>
        {
            if (@override is null) map.Remove(key);
            else map[key] = @override;
        });
    }

    // :304-306 func reset(_: AppShortcut): one assignment, so one write, even when nothing was stored.
    public void Reset(AppShortcut shortcut)
    {
        var key = SwiftEnum<AppShortcut>.RawValue(shortcut);
        Overrides = Overrides.Mutating(map => map.Remove(key)); // :305
    }

    // :308-310 func reset(_: AppShortcut.Section): one assignment, so one write, per shortcut in the section.
    public void Reset(AppShortcutSection section)
    {
        foreach (var shortcut in section.Shortcuts())
        {
            var key = SwiftEnum<AppShortcut>.RawValue(shortcut);
            Overrides = Overrides.Mutating(map => map.Remove(key)); // :309
        }
    }

    // :312-314 func isCustomized(_:): the presence of an override, whatever it holds.
    public bool IsCustomized(AppShortcut shortcut) => Overrides.ContainsKey(SwiftEnum<AppShortcut>.RawValue(shortcut));

    // :316-318 func hasCustomizations(in:)
    public bool HasCustomizations(AppShortcutSection section) => section.Shortcuts().Any(IsCustomized);

    // :320-334 Why a chord cannot be taken by a command, or nil when it can. The checks run in source order.
    public string? Refusal(KeyChord chord, AppShortcut shortcut)
    {
        if (chord == Chord(shortcut)) return null; // :322
        var flags = chord.ModifierFlags; // :323
        if (KeyNames.IsModifierKey(chord.KeyCode)) return "Add a key to the modifiers."; // :324
        if ((flags & ~ChordModifiers.Shift) == ChordModifiers.None && KeyNames.TypesText(chord.KeyCode)) // :325
            // :326 DEVIATION OD-0008 B7 (ED-099 (4)): upstream reads "Add U+2318, U+2303 or U+2325 so typing still works."
            return "Add Ctrl or Alt so typing still works.";
        if (chord.KeyboardShortcut is null) return "Menus cannot use this key."; // :328
        // :329 DEVIATION OD-0008 B8 (ED-099 (4)): upstream reads "macOS or Droppy Code already uses this chord."
        if (Reserved.Contains(chord)) return "Windows or Nth already uses this chord.";
        // :330-332 the first other command, in declaration order, whose current chord is this one
        foreach (var owner in SwiftEnum<AppShortcut>.AllCases)
        {
            if (owner != shortcut && Chord(owner) == chord)
                return owner.Title() + " already uses this chord.";
        }
        return null; // :333
    }
}
