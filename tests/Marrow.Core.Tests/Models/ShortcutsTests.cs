using System.Globalization;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Shortcuts.swift (B2 1fbf81e), unit Core.Models.Shortcuts,
// part 1 of 3. Every expected value is transcribed from the Swift lines cited on each test, or from the
// record's "deviation" field (OD-0008 B7 Windows notation, OD-0008 B8 reserved wording, OD-0002 and
// OD-0012 F1 product name, OD-0012 G6 no default chord for showMainWindow), or from ED-099, never from the
// C# under test.
//
// ED-099 fixes the chord format: KeyCode is the Windows Set-1 scan code of the key position, extended keys as
// 0xE000 | code, with the US-position table of Shortcuts.swift:52-69 mapped one for one (A 0x1E, N 0x31,
// Return 0x1C, Tab 0x0F, Delete 0x0E, Home 0xE047, F1 0x3B, and the rest); ChordModifiers Alt = 1,
// Control = 2, Shift = 4, with Command read as Control and Win (Avalonia Meta, 8) masked out like fn;
// "Ctrl+", "Alt+", "Shift+" in that order before the label; the defaults key "keyboardShortcuts" keeps the
// Swift JSON shape (keyCode, modifiers) holding these values. ED-099 (5) supersedes the "discard a macOS
// blob" verification of PR-SETTINGS-104 and the "new format / new key" wording of port_impacts.md entries
// 110 and 119: nothing is discarded because no macOS profile can reach Windows.
//
// API shape assumed (R-NAME-1 to R-NAME-6, R-ENUM-1, R-LIFE-2, R-VAL-1, R-NUM-4): `enum AppShortcut` and
// `enum AppShortcutSection` (a C# enum cannot nest the Swift `AppShortcut.Section`) with [RawValue], their
// computed members as extension methods (`Id()`, `Title()`, `Detail()`, `Section()`, `DefaultChord()`,
// `Shortcuts()`); `KeyChord` a sealed value type with a `(keyCode, ChordModifiers modifiers)` constructor that
// masks (:11-14), `KeyCode` (Swift Int), `Modifiers` (the stored raw bits, Swift UInt), `ModifierFlags`
// (ChordModifiers, :16-18), `KeyboardShortcut` (:21-30) and `Description` (:32-34); and `ShortcutStore` with
// `Shared` as an Ambient<ShortcutStore>. The store's Swift `private init` is reached by reflection so the port
// can keep it private.
public sealed class ShortcutsTests
{
    private const string Key = "keyboardShortcuts"; // :249

    private const ChordModifiers Ctrl = ChordModifiers.Control;
    private const ChordModifiers Alt = ChordModifiers.Alt;
    private const ChordModifiers Shift = ChordModifiers.Shift;

    // Avalonia KeyModifiers.Meta, the Win key: never kept in a chord (ED-099 (3)).
    private const ChordModifiers Win = (ChordModifiers)8;

    // AppShortcut in declaration order (:106-128), with raw value (:105-128, :117, :119), section (:157-164),
    // title (:166-188, :185 renamed by OD-0002 / OD-0012 F1), detail (:190-212), the default chord (:214-236)
    // as ED-099 maps it (scan code and ChordModifiers; showMainWindow has none, OD-0012 G6), its rendering
    // (OD-0008 B7) and its menu key equivalent (:80-101, ED-099 (6)).
    private static readonly (AppShortcut Shortcut, string Raw, AppShortcutSection Section, string Title, string Detail, long? Code, ChordModifiers Mods, string? Rendered, string? Equivalent)[] Table =
    [
        (AppShortcut.NewThread, "newThread", AppShortcutSection.Threads, "New thread", "Starts a thread in the current project.", 0x31, Ctrl, "Ctrl+N", "n"),
        (AppShortcut.NewWorktreeThread, "newWorktreeThread", AppShortcutSection.Threads, "New thread in worktree", "Starts a thread in its own git worktree.", 0x31, Ctrl | Shift, "Ctrl+Shift+N", "n"),
        (AppShortcut.AddProject, "addProject", AppShortcutSection.Threads, "Add project", "Opens a folder as a project.", 0x18, Ctrl, "Ctrl+O", "o"),
        (AppShortcut.StopTurn, "stopTurn", AppShortcutSection.Threads, "Stop the running turn", "Interrupts the agent while it works.", 0x34, Ctrl, "Ctrl+.", "."),
        (AppShortcut.QueueChat, "queueChat", AppShortcutSection.Threads, "Queue a chat", "While a turn runs, lines the draft up behind it.", 0x1C, Ctrl, "Ctrl+Return", "return"),
        (AppShortcut.TogglePlanMode, "togglePlanMode", AppShortcutSection.Threads, "Plan mode", "Plans before building.", 0x19, Ctrl | Shift, "Ctrl+Shift+P", "p"),
        (AppShortcut.PreviousThread, "previousThread", AppShortcutSection.Threads, "Previous thread", "Selects the thread above in the sidebar.", 0x0F, Ctrl | Shift, "Ctrl+Shift+Tab", "tab"),
        (AppShortcut.NextThread, "nextThread", AppShortcutSection.Threads, "Next thread", "Selects the thread below in the sidebar.", 0x0F, Ctrl, "Ctrl+Tab", "tab"),
        (AppShortcut.RunScript, "runScript", AppShortcutSection.Threads, "Run top script", "Runs the first project script in the selected thread's terminal.", 0x13, Ctrl, "Ctrl+R", "r"),
        (AppShortcut.FinishThread, "archiveThread", AppShortcutSection.Threads, "Finish thread", "Settles the selected thread, or archives it, whichever General chose. Reopens a settled one.", 0x0E, Ctrl | Shift, "Ctrl+Shift+Delete", "delete"),
        (AppShortcut.ArchiveThread, "archiveThreadPrompt", AppShortcutSection.Threads, "Archive thread", "Asks to archive the selected thread; Return archives it, and Delete is a click away.", 0x11, Ctrl, "Ctrl+W", "w"),
        (AppShortcut.SettleThread, "settleThread", AppShortcutSection.Threads, "Settle thread", "Settles the selected thread. Reopens one that is settled.", 0x1F, Ctrl, "Ctrl+S", "s"),
        (AppShortcut.CommandPalette, "commandPalette", AppShortcutSection.Window, "Command palette", "Searches threads, projects and actions.", 0x25, Ctrl, "Ctrl+K", "k"),
        (AppShortcut.ToggleSidebar, "toggleSidebar", AppShortcutSection.Window, "Toggle sidebar", "Shows or hides the sidebar.", 0x30, Ctrl, "Ctrl+B", "b"),
        (AppShortcut.ToggleTerminal, "toggleTerminal", AppShortcutSection.Window, "Toggle terminal", "Shows or hides the thread's terminal.", 0x24, Ctrl, "Ctrl+J", "j"),
        (AppShortcut.ToggleChanges, "toggleChanges", AppShortcutSection.Window, "Toggle changes", "Shows or hides the changes panel.", 0x20, Ctrl, "Ctrl+D", "d"),
        (AppShortcut.ToggleActivityView, "toggleActivityView", AppShortcutSection.Window, "Activity view", "Lists every thread by when it was last active.", 0x16, Ctrl | Alt, "Ctrl+Alt+U", "u"),
        (AppShortcut.ShowMainWindow, "showMainWindow", AppShortcutSection.Window, "Show Marrow", "Brings the main window to the front.", null, default, null, null),
        (AppShortcut.OpenSettings, "openSettings", AppShortcutSection.Window, "Settings", "Opens this window.", 0x33, Ctrl, "Ctrl+,", ","),
    ];

    // The 18 reserved chords (:251-257) under ED-099: Ctrl with Q, H, M, C, V, X, A, Z; Ctrl+Shift+Z; Ctrl with
    // the digits 1 to 9 (macOS codes 18, 19, 20, 21, 23, 22, 26, 28, 25).
    private static readonly (long Code, ChordModifiers Mods, string Rendered)[] Reserved =
    [
        (0x10, Ctrl, "Ctrl+Q"), (0x23, Ctrl, "Ctrl+H"), (0x32, Ctrl, "Ctrl+M"), (0x2E, Ctrl, "Ctrl+C"),
        (0x2F, Ctrl, "Ctrl+V"), (0x2D, Ctrl, "Ctrl+X"), (0x1E, Ctrl, "Ctrl+A"), (0x2C, Ctrl, "Ctrl+Z"),
        (0x2C, Ctrl | Shift, "Ctrl+Shift+Z"),
        (0x02, Ctrl, "Ctrl+1"), (0x03, Ctrl, "Ctrl+2"), (0x04, Ctrl, "Ctrl+3"), (0x05, Ctrl, "Ctrl+4"),
        (0x06, Ctrl, "Ctrl+5"), (0x07, Ctrl, "Ctrl+6"), (0x08, Ctrl, "Ctrl+7"), (0x09, Ctrl, "Ctrl+8"),
        (0x0A, Ctrl, "Ctrl+9"),
    ];

    // The 47 character positions of :52-58 as ED-099 (2) maps them, in the Swift table's order.
    private static readonly long[] CharacterCodes =
    [
        0x1E, 0x1F, 0x20, 0x21, 0x23, 0x22, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x10,
        0x11, 0x12, 0x13, 0x15, 0x14, 0x02, 0x03, 0x04, 0x05, 0x07, 0x06,
        0x0D, 0x0A, 0x08, 0x0C, 0x09, 0x0B, 0x1B, 0x18, 0x16, 0x1A, 0x17,
        0x19, 0x26, 0x24, 0x28, 0x25, 0x27, 0x2B, 0x33, 0x35, 0x31, 0x32,
        0x34, 0x29,
    ];

    // The typing and caret keys of :77 besides the characters: Return, Tab, Space, Delete, Esc, Del, left,
    // right, down, up (macOS 36, 48, 49, 51, 53, 117, 123, 124, 125, 126; ED-099 (2)).
    private static readonly long[] CaretCodes = [0x1C, 0x0F, 0x39, 0x0E, 0x01, 0xE053, 0xE04B, 0xE04D, 0xE050, 0xE048];

    // The modifier keys of :40 under ED-099 (2): LShift, RShift, LCtrl, RCtrl, LAlt, RAlt, LWin, RWin, CapsLock.
    private static readonly long[] ModifierKeyCodes = [0x2A, 0x36, 0x1D, 0xE01D, 0x38, 0xE038, 0xE05B, 0xE05C, 0x3A];

    private const string TypingRefusal = "Add Ctrl or Alt so typing still works."; // :326 (OD-0008 B7, ED-099 (4))
    private const string ReservedRefusal = "Windows or Marrow already uses this chord."; // :329 (OD-0008 B8, ED-099 (4))
    private const string ModifierRefusal = "Add a key to the modifiers."; // :324
    private const string MenuRefusal = "Menus cannot use this key."; // :328

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);

    // A synthesized KeyChord encoding (:5-9) holding the stored masked bits (:13), used only as INPUT to the
    // decoder, which reads the keys in any order.
    private static string ChordJson(long code, ChordModifiers mods) =>
        "{\"keyCode\":" + Decimal(code) + ",\"modifiers\":" + Decimal((uint)mods) + "}";

    // The writer is `JSONEncoder().encode(overrides)` with no .sortedKeys (:261): the unsorted PlainEncoder, whose
    // key order Swift does not fix (R-CODE-4, R-TEST-4). A single-entry dictionary holding one chord therefore has
    // exactly two correct byte sequences, the two orders of the chord object's keys; each is compared byte for byte.
    private static void AssertStoredChord(TestRuntime runtime, string raw, long keyCode, ulong modifiers)
    {
        var code = "\"keyCode\":" + Decimal(keyCode);
        var mods = "\"modifiers\":" + modifiers.ToString(CultureInfo.InvariantCulture);
        var prefix = "{\"" + raw + "\":{\"chord\":{";
        byte[][] permutations =
        [
            Utf8(prefix + code + "," + mods + "}}}"),
            Utf8(prefix + mods + "," + code + "}}}"),
        ];
        Assert.Contains(Stored(runtime), permutations, ByteSequence.Instance);
    }

    private sealed class ByteSequence : IEqualityComparer<byte[]>
    {
        public static readonly ByteSequence Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            x is not null && y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length;
    }

    private static (AppShortcut Shortcut, string Raw, AppShortcutSection Section, string Title, string Detail, long? Code, ChordModifiers Mods, string? Rendered, string? Equivalent) Row(AppShortcut shortcut) =>
        Array.Find(Table, r => r.Shortcut == shortcut);

    private static KeyChord Default(AppShortcut shortcut) => shortcut.DefaultChord()!;

    // `private init()` (:267-274) runs on main (@MainActor, :240) and reads UserDefaults.standard once.
    private static ShortcutStore Load(TestRuntime runtime) =>
        runtime.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);

    // `ShortcutStore.shared` (:243) is what the static label(for:) and hint(for:) read (:288-298).
    private static void OnMain(TestRuntime runtime, ShortcutStore store, Action body)
    {
        using var shared = ShortcutStore.Shared.Override(store);
        runtime.OnMain(body);
    }

    private static void Seed(TestRuntime runtime, string json)
    {
        runtime.Defaults.Set(Key, Utf8(json));
        runtime.Defaults.Writes.Clear();
    }

    private static byte[] Stored(TestRuntime runtime) => Assert.IsType<byte[]>(runtime.Defaults.Object(Key));

    // One row of the shortcut table and its store behaviour: the checks every per-shortcut requirement names.
    private static void AssertShortcut(AppShortcut shortcut, AppShortcut donor)
    {
        using var runtime = new TestRuntime();
        var row = Row(shortcut);
        var donorRow = Row(donor);
        Assert.Equal(row.Raw, SwiftEnum<AppShortcut>.RawValue(shortcut));
        Assert.Equal(row.Raw, shortcut.Id()); // :155
        Assert.Equal(shortcut, SwiftEnum<AppShortcut>.FromRawValue(row.Raw));
        Assert.Equal(Utf8(row.Title), Utf8(shortcut.Title()));
        Assert.Equal(Utf8(row.Detail), Utf8(shortcut.Detail()));
        Assert.Equal(row.Section, shortcut.Section());
        Assert.Contains(shortcut, row.Section.Shortcuts());
        if (row.Code is null)
        {
            Assert.Null(shortcut.DefaultChord());
        }
        else
        {
            var chord = Default(shortcut);
            Assert.Equal(row.Code.Value, chord.KeyCode);
            Assert.Equal(row.Mods, chord.ModifierFlags);
            Assert.Equal((ulong)row.Mods, (ulong)chord.Modifiers);
            Assert.Equal(new KeyChord(row.Code.Value, row.Mods), chord);
            Assert.Equal(Utf8(row.Rendered!), Utf8(chord.Description));
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent(row.Equivalent!), row.Mods), chord.KeyboardShortcut);
        }

        var store = Load(runtime);
        var donorCode = donorRow.Code!.Value;
        var recorded = new KeyChord(donorCode, donorRow.Mods);
        OnMain(runtime, store, () =>
        {
            // At its default: the shipped chord, and a hint that names it (:276-279, :288-298).
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));
            Assert.Equal(row.Rendered, ShortcutStore.Label(shortcut));
            Assert.Equal(row.Rendered is null ? "" : " (" + row.Rendered + ")", ShortcutStore.Hint(shortcut));

            // Re-recorded to a chord: the store answers with it and stores it under the raw value (:300-302, :259-265).
            store.Set(recorded, shortcut);
            Assert.True(store.IsCustomized(shortcut));
            Assert.Equal(recorded, store.Chord(shortcut));
            Assert.Equal(Utf8(donorRow.Rendered!), Utf8(ShortcutStore.Label(shortcut)!));
            Assert.Equal(Utf8(" (" + donorRow.Rendered + ")"), Utf8(ShortcutStore.Hint(shortcut)));
        });
        AssertStoredChord(runtime, row.Raw, donorCode, (uint)donorRow.Mods);

        OnMain(runtime, store, () =>
        {
            // A chord equal to the default removes the override (:301); built fresh, so equality is by value.
            var sameAsDefault = row.Code is null ? null : new KeyChord(row.Code.Value, row.Mods);
            store.Set(sameAsDefault, shortcut);
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));
        });
        Assert.Equal(Utf8("{}"), Stored(runtime));

        OnMain(runtime, store, () =>
        {
            // Cleared: no chord, no label, an empty hint (:246, :277, :289, :296).
            store.Set(null, shortcut);
            Assert.Null(store.Chord(shortcut));
            Assert.Null(store.KeyboardShortcut(shortcut));
            Assert.Null(ShortcutStore.Label(shortcut));
            Assert.Equal("", ShortcutStore.Hint(shortcut));
            Assert.Equal(row.Code is not null, store.IsCustomized(shortcut));
        });
        Assert.Equal(Utf8(row.Code is null ? "{}" : "{\"" + row.Raw + "\":{}}"), Stored(runtime));

        OnMain(runtime, store, () =>
        {
            store.Reset(shortcut); // :304-306
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));
        });

        // The override is read back under the raw value, cleared or recorded (:268-270, :277).
        Seed(runtime, "{\"" + row.Raw + "\":{}}");
        var reloaded = Load(runtime);
        OnMain(runtime, reloaded, () =>
        {
            Assert.True(reloaded.IsCustomized(shortcut));
            Assert.Null(reloaded.Chord(shortcut));
            Assert.Equal("", ShortcutStore.Hint(shortcut));
            foreach (var other in Table.Where(r => r.Shortcut != shortcut))
            {
                Assert.False(reloaded.IsCustomized(other.Shortcut));
                Assert.Equal(other.Shortcut.DefaultChord(), reloaded.Chord(other.Shortcut));
            }
        });

        Seed(runtime, "{\"" + row.Raw + "\":{\"chord\":" + ChordJson(donorCode, donorRow.Mods) + "}}");
        var relaunched = Load(runtime);
        OnMain(runtime, relaunched, () =>
        {
            Assert.True(relaunched.IsCustomized(shortcut));
            Assert.Equal(recorded, relaunched.Chord(shortcut));
            Assert.Equal(Utf8(donorRow.Rendered!), Utf8(ShortcutStore.Label(shortcut)!));
        });
    }

    // ------------------------------------------------------------------ the table, per requirement

    [Fact]
    [Trait("Req", "PR-SETTINGS-193")] // Core/Models/Shortcuts.swift:106, :159-160, :168, :192, :216 (ED-099)
    public void New_thread_sits_in_Threads_with_its_verbatim_title_and_detail_and_ships_Ctrl_N() =>
        AssertShortcut(AppShortcut.NewThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-194")] // Core/Models/Shortcuts.swift:107, :159-160, :169, :193, :217 (ED-099)
    public void New_thread_in_worktree_sits_in_Threads_and_ships_Ctrl_Shift_N() =>
        AssertShortcut(AppShortcut.NewWorktreeThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-195")] // Core/Models/Shortcuts.swift:108, :159-160, :170, :194, :218 (ED-099)
    public void Add_project_sits_in_Threads_and_ships_Ctrl_O() =>
        AssertShortcut(AppShortcut.AddProject, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-196")] // Core/Models/Shortcuts.swift:109, :159-160, :171, :195, :219 (ED-099)
    public void Stop_the_running_turn_sits_in_Threads_and_ships_Ctrl_period() =>
        AssertShortcut(AppShortcut.StopTurn, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-197")] // Core/Models/Shortcuts.swift:110, :159-160, :172, :196, :220 (ED-099)
    public void Queue_a_chat_sits_in_Threads_and_ships_Ctrl_Return() =>
        AssertShortcut(AppShortcut.QueueChat, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-198")] // Core/Models/Shortcuts.swift:111, :159-160, :173, :197, :221 (ED-099)
    public void Plan_mode_sits_in_Threads_and_ships_Ctrl_Shift_P() =>
        AssertShortcut(AppShortcut.TogglePlanMode, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-199")] // Core/Models/Shortcuts.swift:112, :159-160, :174, :198, :222 (ED-099)
    public void Previous_thread_sits_in_Threads_and_ships_Ctrl_Shift_Tab() =>
        AssertShortcut(AppShortcut.PreviousThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-200")] // Core/Models/Shortcuts.swift:113, :159-160, :175, :199, :223 (ED-099)
    public void Next_thread_sits_in_Threads_and_ships_Ctrl_Tab() =>
        AssertShortcut(AppShortcut.NextThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-201")] // Core/Models/Shortcuts.swift:114, :159-160, :176, :200, :224 (ED-099)
    public void Run_top_script_sits_in_Threads_and_ships_Ctrl_R() =>
        AssertShortcut(AppShortcut.RunScript, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-202")] // Core/Models/Shortcuts.swift:115-117, :159-160, :177, :201, :225 (ED-099)
    public void Finish_thread_is_stored_as_archiveThread_sits_in_Threads_and_ships_Ctrl_Shift_Delete() =>
        AssertShortcut(AppShortcut.FinishThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-203")] // Core/Models/Shortcuts.swift:118-119, :159-160, :178, :202, :226 (ED-099)
    public void Archive_thread_is_stored_as_archiveThreadPrompt_sits_in_Threads_and_ships_Ctrl_W() =>
        AssertShortcut(AppShortcut.ArchiveThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-202")] // Core/Models/Shortcuts.swift:115-119, :268-270, :277
    [Trait("Req", "PR-SETTINGS-203")] // Core/Models/Shortcuts.swift:115-119, :268-270, :277
    public void A_chord_stored_under_the_old_archiveThread_name_applies_to_Finish_thread_not_Archive_thread()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"archiveThread\":{\"chord\":" + ChordJson(0x25, Ctrl | Alt) + "}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.True(store.IsCustomized(AppShortcut.FinishThread));
            Assert.Equal(new KeyChord(0x25, Ctrl | Alt), store.Chord(AppShortcut.FinishThread));
            Assert.Equal(Utf8("Ctrl+Alt+K"), Utf8(ShortcutStore.Label(AppShortcut.FinishThread)!));
            Assert.False(store.IsCustomized(AppShortcut.ArchiveThread));
            Assert.Equal(Utf8("Ctrl+W"), Utf8(ShortcutStore.Label(AppShortcut.ArchiveThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-204")] // Core/Models/Shortcuts.swift:120-121, :159-160, :179, :203, :227 (ED-099)
    public void Settle_thread_sits_in_Threads_and_ships_Ctrl_S() =>
        AssertShortcut(AppShortcut.SettleThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-205")] // Core/Models/Shortcuts.swift:122, :161-162, :180, :204, :228 (ED-099)
    public void Command_palette_sits_in_Window_and_panels_and_ships_Ctrl_K() =>
        AssertShortcut(AppShortcut.CommandPalette, AppShortcut.ToggleSidebar);

    [Fact]
    [Trait("Req", "PR-SETTINGS-206")] // Core/Models/Shortcuts.swift:123, :161-162, :181, :205, :229 (ED-099)
    public void Toggle_sidebar_sits_in_Window_and_panels_and_ships_Ctrl_B() =>
        AssertShortcut(AppShortcut.ToggleSidebar, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-207")] // Core/Models/Shortcuts.swift:124, :161-162, :182, :206, :230 (ED-099)
    public void Toggle_terminal_sits_in_Window_and_panels_and_ships_Ctrl_J() =>
        AssertShortcut(AppShortcut.ToggleTerminal, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-208")] // Core/Models/Shortcuts.swift:125, :161-162, :183, :207, :231 (ED-099)
    public void Toggle_changes_sits_in_Window_and_panels_and_ships_Ctrl_D() =>
        AssertShortcut(AppShortcut.ToggleChanges, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-209")] // Core/Models/Shortcuts.swift:126, :161-162, :184, :208, :232 (ED-099)
    public void Activity_view_sits_in_Window_and_panels_and_ships_Ctrl_Alt_U() =>
        AssertShortcut(AppShortcut.ToggleActivityView, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:127, :161-162, :185, :209, :233 (OD-0002, OD-0012 F1, OD-0012 G6)
    public void Show_Marrow_sits_in_Window_and_panels_and_ships_with_no_chord() =>
        AssertShortcut(AppShortcut.ShowMainWindow, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:233, :301, :312-314 (OD-0012 G6)
    public void Clearing_Show_Marrow_equals_its_missing_default_so_it_stores_no_override()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.ShowMainWindow);
            Assert.False(store.IsCustomized(AppShortcut.ShowMainWindow));
            Assert.Null(ShortcutStore.Label(AppShortcut.ShowMainWindow));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ShowMainWindow));
        });
        Assert.Equal(Utf8("{}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:300-302, :321-333 (OD-0012 G6)
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:321-333 (failure case: Ctrl+0 is not guarded)
    public void Ctrl_0_is_free_by_default_and_once_Show_Marrow_holds_it_the_owner_check_names_Show_Marrow()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var ctrl0 = new KeyChord(0x0B, Ctrl); // 0 is macOS 29, scan 0x0B (ED-099 (2))
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
                Assert.Null(store.Refusal(ctrl0, row.Shortcut));

            store.Set(ctrl0, AppShortcut.ShowMainWindow);
            Assert.True(store.IsCustomized(AppShortcut.ShowMainWindow));
            Assert.Equal(Utf8("Ctrl+0"), Utf8(ShortcutStore.Label(AppShortcut.ShowMainWindow)!));
            Assert.Equal(Utf8(" (Ctrl+0)"), Utf8(ShortcutStore.Hint(AppShortcut.ShowMainWindow)));
            Assert.Equal(Utf8("Show Marrow already uses this chord."), Utf8(store.Refusal(ctrl0, AppShortcut.NewThread)!));
            Assert.Null(store.Refusal(ctrl0, AppShortcut.ShowMainWindow));
        });
        AssertStoredChord(runtime, "showMainWindow", 11, 2);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-211")] // Core/Models/Shortcuts.swift:128, :161-162, :186, :210, :234 (ED-099)
    public void Settings_sits_in_Window_and_panels_and_ships_Ctrl_comma() =>
        AssertShortcut(AppShortcut.OpenSettings, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-SETTINGS-193")] // Core/Models/Shortcuts.swift:105-128, :130-153; App/Views/Settings/ShortcutsSettingsPage.swift:54-61
    [Trait("Req", "PR-SETTINGS-194")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-195")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-196")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-197")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-198")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-199")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-200")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-201")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-202")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-203")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-204")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-205")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-206")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-207")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-208")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-209")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:150-152
    [Trait("Req", "PR-SETTINGS-211")] // Core/Models/Shortcuts.swift:150-152
    public void Sections_list_their_shortcuts_in_declaration_order_under_their_verbatim_titles()
    {
        Assert.Equal(Table.Select(r => r.Shortcut), SwiftEnum<AppShortcut>.AllCases);
        Assert.Equal(Table.Select(r => r.Raw), SwiftEnum<AppShortcut>.AllCases.Select(s => SwiftEnum<AppShortcut>.RawValue(s)));

        Assert.Equal(new[] { AppShortcutSection.Threads, AppShortcutSection.Window }, SwiftEnum<AppShortcutSection>.AllCases); // :130-132
        Assert.Equal("threads", SwiftEnum<AppShortcutSection>.RawValue(AppShortcutSection.Threads));
        Assert.Equal("window", SwiftEnum<AppShortcutSection>.RawValue(AppShortcutSection.Window));
        Assert.Equal("threads", AppShortcutSection.Threads.Id()); // :134
        Assert.Equal("window", AppShortcutSection.Window.Id());
        Assert.Equal(Utf8("Threads"), Utf8(AppShortcutSection.Threads.Title())); // :138
        Assert.Equal(Utf8("Window and panels"), Utf8(AppShortcutSection.Window.Title())); // :139

        Assert.Equal(Table.Where(r => r.Section == AppShortcutSection.Threads).Select(r => r.Shortcut), AppShortcutSection.Threads.Shortcuts());
        Assert.Equal(Table.Where(r => r.Section == AppShortcutSection.Window).Select(r => r.Shortcut), AppShortcutSection.Window.Shortcuts());
        Assert.Equal(12, AppShortcutSection.Threads.Shortcuts().Count());
        Assert.Equal(7, AppShortcutSection.Window.Shortcuts().Count());
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-193")] // Core/Models/Shortcuts.swift:32-34, :43-50 (OD-0008 B7, ED-099 (4))
    [Trait("Req", "PR-SETTINGS-209")] // Core/Models/Shortcuts.swift:32-34, :232 (OD-0008 B7, ED-099 (4))
    public void A_chord_reads_its_modifiers_as_Ctrl_Alt_Shift_in_that_order_before_the_US_position_key_label()
    {
        Assert.Equal(Utf8("Ctrl+Alt+N"), Utf8(new KeyChord(0x31, Alt | Ctrl).Description));
        Assert.Equal(Utf8("Ctrl+Shift+N"), Utf8(new KeyChord(0x31, Shift | Ctrl).Description));
        Assert.Equal(Utf8("Alt+Shift+N"), Utf8(new KeyChord(0x31, Shift | Alt).Description));
        Assert.Equal(Utf8("Ctrl+Alt+Shift+N"), Utf8(new KeyChord(0x31, Shift | Alt | Ctrl).Description));
        Assert.Equal(Utf8("Alt+Tab"), Utf8(new KeyChord(0x0F, Alt).Description));
        Assert.Equal(Utf8("Shift+Return"), Utf8(new KeyChord(0x1C, Shift).Description));
        // No modifiers: the key label alone, with no separator (:33, and "" from :44-49).
        Assert.Equal(Utf8("N"), Utf8(new KeyChord(0x31, default).Description));
        Assert.Equal(Utf8("Delete"), Utf8(new KeyChord(0x0E, default).Description)); // :61 macOS 51
        Assert.Equal(Utf8("Return"), Utf8(new KeyChord(0x1C, default).Description)); // :61 macOS 36
        // The Win key is masked out when the chord is made (:13, ED-099 (3)).
        Assert.Equal(Utf8("Ctrl+N"), Utf8(new KeyChord(0x31, Ctrl | Win).Description));
        Assert.Equal(new KeyChord(0x31, Ctrl), new KeyChord(0x31, Ctrl | Win));
        Assert.Equal(2UL, (ulong)new KeyChord(0x31, Ctrl | Win).Modifiers);
    }

    // ------------------------------------------------------------------ PR-GIT-141, PR-TERM-068

    [Fact]
    [Trait("Req", "PR-GIT-141")] // Core/Models/Shortcuts.swift:125, :183, :207, :231, :281-298
    public void Toggle_changes_ships_Ctrl_D_and_its_hint_follows_the_current_binding()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Utf8("Toggle changes"), Utf8(AppShortcut.ToggleChanges.Title()));
        Assert.Equal(Utf8("Shows or hides the changes panel."), Utf8(AppShortcut.ToggleChanges.Detail()));
        Assert.Equal("toggleChanges", SwiftEnum<AppShortcut>.RawValue(AppShortcut.ToggleChanges));
        Assert.Equal(new KeyChord(0x20, Ctrl), AppShortcut.ToggleChanges.DefaultChord()); // D is macOS 2, scan 0x20
        Assert.Equal(Utf8("Ctrl+D"), Utf8(Default(AppShortcut.ToggleChanges).Description));

        var store = Load(runtime);
        var rebound = new KeyChord(0x20, Ctrl | Alt);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("d"), Ctrl), store.KeyboardShortcut(AppShortcut.ToggleChanges));
            Assert.Equal(Utf8(" (Ctrl+D)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleChanges)));
            store.Set(rebound, AppShortcut.ToggleChanges);
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("d"), Ctrl | Alt), store.KeyboardShortcut(AppShortcut.ToggleChanges));
            Assert.Equal(Utf8(" (Ctrl+Alt+D)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleChanges)));
        });

        var relaunched = Load(runtime); // the rebinding persists with the store (:259-274)
        OnMain(runtime, relaunched, () =>
        {
            Assert.Equal(Utf8(" (Ctrl+Alt+D)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleChanges)));
            relaunched.Set(null, AppShortcut.ToggleChanges);
            Assert.Null(relaunched.KeyboardShortcut(AppShortcut.ToggleChanges));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleChanges));
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-068")] // Core/Models/Shortcuts.swift:114, :137-138, :159-160, :176, :200, :224
    public void Run_top_script_is_listed_under_Threads_and_a_remap_changes_every_label_that_shows_it()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(AppShortcutSection.Threads, AppShortcut.RunScript.Section());
        Assert.Equal(Utf8("Threads"), Utf8(AppShortcut.RunScript.Section().Title()));
        Assert.Equal(Utf8("Run top script"), Utf8(AppShortcut.RunScript.Title()));
        Assert.Equal(Utf8("Runs the first project script in the selected thread's terminal."), Utf8(AppShortcut.RunScript.Detail()));
        Assert.Equal(new KeyChord(0x13, Ctrl), AppShortcut.RunScript.DefaultChord()); // R is macOS 15, scan 0x13
        Assert.Equal(Utf8("Ctrl+R"), Utf8(Default(AppShortcut.RunScript).Description));

        var store = Load(runtime);
        var remapped = new KeyChord(0x13, Ctrl | Shift);
        OnMain(runtime, store, () =>
        {
            Assert.Equal("Ctrl+R", ShortcutStore.Label(AppShortcut.RunScript));
            store.Set(remapped, AppShortcut.RunScript);
            Assert.True(store.IsCustomized(AppShortcut.RunScript));
            Assert.Equal(Utf8("Ctrl+Shift+R"), Utf8(ShortcutStore.Label(AppShortcut.RunScript)!));
            Assert.Equal(Utf8(" (Ctrl+Shift+R)"), Utf8(ShortcutStore.Hint(AppShortcut.RunScript)));
        });
        AssertStoredChord(runtime, "runScript", 19, 6);
    }

    // ------------------------------------------------------------------ PR-SETTINGS-104 persistence

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:267-279
    public void With_nothing_stored_every_shortcut_is_at_its_default_and_loading_writes_nothing()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
            {
                Assert.False(store.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), store.Chord(row.Shortcut));
            }
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
            Assert.False(store.HasCustomizations(AppShortcutSection.Window));
        });
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:239, :245-247, :259-265, :300-302
    public void Clearing_stores_an_override_with_no_chord_as_the_plain_encoders_bytes_in_one_write()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () => store.Set(null, AppShortcut.ToggleTerminal));
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes);
        // JSONEncoder omits a nil optional (encodeIfPresent), so the cleared override is an empty object.
        Assert.Equal(Utf8("{\"toggleTerminal\":{}}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:5-14, :245-249, :259-265, :300-302 (ED-099 (1), (3), (5))
    public void A_recorded_chord_is_stored_as_keyCode_and_modifiers_holding_the_scan_code_and_the_masked_bits()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);

        OnMain(runtime, store, () => store.Set(new KeyChord(0x31, Shift | Alt | Ctrl), AppShortcut.ToggleTerminal));
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes);
        AssertStoredChord(runtime, "toggleTerminal", 49, 7);

        // The Win key never reaches storage (:13, ED-099 (3)).
        OnMain(runtime, store, () => store.Set(new KeyChord(0x31, Ctrl | Win), AppShortcut.ToggleTerminal));
        AssertStoredChord(runtime, "toggleTerminal", 49, 2);

        // An extended key is 0xE000 | code, written as a decimal integer: Home is 0xE047 (ED-099 (1), (2)).
        OnMain(runtime, store, () => store.Set(new KeyChord(0xE047, Ctrl), AppShortcut.ToggleTerminal));
        AssertStoredChord(runtime, "toggleTerminal", 57415, 2);

        // No modifiers at all: modifiers is 0, not omitted (a non-optional UInt, :9).
        OnMain(runtime, store, () => store.Set(new KeyChord(0x3F, default), AppShortcut.ToggleTerminal));
        AssertStoredChord(runtime, "toggleTerminal", 63, 0);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:8-9, :16-18, :72, :268-270 (R-NUM-4: Swift Int and UInt are 64-bit)
    public void A_stored_chord_keeps_a_negative_key_code_and_a_modifier_value_wider_than_32_bits()
    {
        // keyCode is a Swift Int and modifiers a Swift UInt (:8-9), both 64 bits, so the synthesized decoder
        // accepts -1 and 4294967298 (2^32 + 2) and stores them raw: it does not run the masking init (:11-14).
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":-1,\"modifiers\":4294967298}}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            var chord = store.Chord(AppShortcut.ToggleTerminal)!;
            Assert.Equal(-1L, chord.KeyCode);
            Assert.Equal(4294967298UL, (ulong)chord.Modifiers);
            // modifierFlags intersects with the allowed set (:16-18): only bit 2, Control, survives.
            Assert.Equal(Ctrl, chord.ModifierFlags);
            // -1 is in neither label table, so it reads "Key -1" (:72, ED-099 (2)).
            Assert.Equal(Utf8("Ctrl+Key -1"), Utf8(chord.Description));
            Assert.Equal(Utf8("Ctrl+Key -1"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
        });
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:245-249, :259-270, :304-306, :316-318
    public void An_override_for_no_current_command_survives_the_next_write()
    {
        // `overrides` is keyed by the raw string (:245) and didSet re-encodes the whole dictionary (:259-265),
        // so an entry no AppShortcut names is carried through every write.
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"notAShortcut\":{}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // The stray key belongs to no section (:316-318).
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
            Assert.False(store.HasCustomizations(AppShortcutSection.Window));
            store.Reset(AppShortcut.ToggleTerminal); // one assignment, one write (:304-306)
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
            Assert.False(store.HasCustomizations(AppShortcutSection.Window));
        });
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes);
        Assert.Equal(Utf8("{\"notAShortcut\":{}}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:259-265, :300-302
    public void Recording_the_default_removes_the_override_and_rewrites_the_whole_dictionary()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.ToggleTerminal);
            store.Set(null, AppShortcut.NewThread);
            store.Set(new KeyChord(0x31, Ctrl), AppShortcut.NewThread);
            Assert.False(store.IsCustomized(AppShortcut.NewThread));
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
        });
        Assert.Equal(new[] { Key, Key, Key }, runtime.Defaults.Writes);
        Assert.Equal(Utf8("{\"toggleTerminal\":{}}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:259-265, :304-306
    public void Every_assignment_writes_even_a_reset_of_a_shortcut_that_was_never_changed()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () => store.Reset(AppShortcut.ToggleTerminal));
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes);
        Assert.Equal(Utf8("{}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:259-265, :308-310, :316-318
    public void Resetting_a_section_writes_once_per_shortcut_in_it_and_keeps_the_other_sections_overrides()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.NewThread);
            store.Set(null, AppShortcut.SettleThread);
            store.Set(null, AppShortcut.ToggleTerminal);
        });
        runtime.Defaults.Writes.Clear();
        OnMain(runtime, store, () =>
        {
            Assert.True(store.HasCustomizations(AppShortcutSection.Threads));
            store.Reset(AppShortcutSection.Threads);
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
            Assert.True(store.HasCustomizations(AppShortcutSection.Window));
            Assert.Equal(Default(AppShortcut.NewThread), store.Chord(AppShortcut.NewThread));
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
        });
        Assert.Equal(Enumerable.Repeat(Key, 12), runtime.Defaults.Writes);
        Assert.Equal(Utf8("{\"toggleTerminal\":{}}"), Stored(runtime));

        runtime.Defaults.Writes.Clear();
        OnMain(runtime, store, () => store.Reset(AppShortcutSection.Window));
        Assert.Equal(Enumerable.Repeat(Key, 7), runtime.Defaults.Writes);
        Assert.Equal(Utf8("{}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:259-274
    public void A_relaunched_store_reads_back_recorded_and_cleared_shortcuts()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var recorded = new KeyChord(0x25, Ctrl | Alt);
        OnMain(runtime, store, () =>
        {
            store.Set(recorded, AppShortcut.NewThread);
            store.Set(null, AppShortcut.ToggleTerminal);
        });

        var relaunched = Load(runtime);
        OnMain(runtime, relaunched, () =>
        {
            Assert.Equal(recorded, relaunched.Chord(AppShortcut.NewThread));
            Assert.True(relaunched.IsCustomized(AppShortcut.NewThread));
            Assert.Null(relaunched.Chord(AppShortcut.ToggleTerminal));
            Assert.True(relaunched.IsCustomized(AppShortcut.ToggleTerminal));
            foreach (var row in Table.Where(r => r.Shortcut is not AppShortcut.NewThread and not AppShortcut.ToggleTerminal))
            {
                Assert.False(relaunched.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), relaunched.Chord(row.Shortcut));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:5-9, :268-270 (ED-099 (5): the Swift JSON shape)
    public void A_stored_chord_in_the_Swift_shape_is_read_back_by_its_scan_code_and_modifier_bits()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":49,\"modifiers\":7}},\"newThread\":{\"chord\":{\"modifiers\":2,\"keyCode\":57415}}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(new KeyChord(0x31, Ctrl | Alt | Shift), store.Chord(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Ctrl+Alt+Shift+N"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
            Assert.Equal(new KeyChord(0xE047, Ctrl), store.Chord(AppShortcut.NewThread)); // key order does not matter
            Assert.Equal(Utf8("Ctrl+Home"), Utf8(ShortcutStore.Label(AppShortcut.NewThread)!));
        });
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:5-9, :16-18, :268-270 (synthesized Decodable skips the masking init)
    public void A_stored_modifier_value_is_kept_raw_while_the_chord_reads_only_the_allowed_bits()
    {
        using var runtime = new TestRuntime();
        // 10 is Control (2) with bit 8 (the Win key) set: the synthesized decoder stores it as is (:9), and
        // modifierFlags intersects it with the allowed set (:17).
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":36,\"modifiers\":10}}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            var chord = store.Chord(AppShortcut.ToggleTerminal)!;
            Assert.Equal(0x24L, chord.KeyCode);
            Assert.Equal(10UL, (ulong)chord.Modifiers);
            Assert.Equal(Ctrl, chord.ModifierFlags);
            Assert.Equal(Utf8("Ctrl+J"), Utf8(chord.Description));
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("j"), Ctrl), chord.KeyboardShortcut);
            // Synthesized Hashable compares the stored bits, so it is not the default Ctrl+J (:5).
            Assert.NotEqual(Default(AppShortcut.ToggleTerminal), chord);
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
        });
    }

    [Theory]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:268-273 (a [String: Override] decode fails as a whole)
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"toggleTerminal\":[]}")]
    [InlineData("{\"toggleTerminal\":null}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":5}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":5}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"modifiers\":2}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":null,\"modifiers\":2}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":null}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":\"49\",\"modifiers\":2}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":\"2\"}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49.5,\"modifiers\":2}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":-1}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":18446744073709551616}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":9223372036854775808,\"modifiers\":2}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":true,\"modifiers\":2}}}")]
    public void Stored_data_that_fails_to_decode_starts_empty_and_the_next_change_overwrites_it(string stored)
    {
        // keyCode (Int) and modifiers (UInt) are non-optional synthesized fields (:8-9): missing, null, a string,
        // a fraction, a Bool, a negative UInt, a UInt past UInt.max (2^64) or an Int past Int.max (2^63) throws,
        // and with it the whole dictionary (try?, :269).
        using var runtime = new TestRuntime();
        Seed(runtime, stored);
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
            {
                Assert.False(store.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), store.Chord(row.Shortcut));
            }
        });
        Assert.Empty(runtime.Defaults.Writes);
        Assert.Equal(Utf8(stored), Stored(runtime));

        OnMain(runtime, store, () => store.Set(null, AppShortcut.StopTurn));
        Assert.Equal(Utf8("{\"stopTurn\":{}}"), Stored(runtime));
    }

    [Theory]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:268 (data(forKey:) is nil for a value that is not Data)
    [InlineData("{\"toggleTerminal\":{}}")]
    [InlineData(true)]
    [InlineData(1L)]
    [InlineData(1.5)]
    public void A_stored_value_that_is_not_data_is_ignored(object stored)
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set(Key, stored);
        runtime.Defaults.Writes.Clear();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(Default(AppShortcut.ToggleTerminal), store.Chord(AppShortcut.ToggleTerminal));
        });
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Theory]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:245-247, :268-270 (synthesized Decodable: decodeIfPresent, unknown keys ignored)
    [InlineData("{\"toggleTerminal\":{}}")]
    [InlineData("{\"toggleTerminal\":{\"chord\":null}}")]
    [InlineData("{\"toggleTerminal\":{\"unknown\":1}}")]
    [InlineData("{\"notAShortcut\":{},\"toggleTerminal\":{}}")]
    [InlineData("{\"notAShortcut\":{\"chord\":{\"keyCode\":49,\"modifiers\":2}},\"toggleTerminal\":{}}")]
    public void A_stored_override_with_no_chord_reads_as_cleared_whatever_else_the_blob_holds(string stored)
    {
        using var runtime = new TestRuntime();
        Seed(runtime, stored);
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
            foreach (var row in Table.Where(r => r.Shortcut != AppShortcut.ToggleTerminal))
            {
                Assert.False(store.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), store.Chord(row.Shortcut));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:5-9, :268-270 (synthesized Decodable ignores unknown keys)
    public void A_stored_chord_with_extra_keys_still_decodes()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":48,\"modifiers\":3,\"extra\":\"x\"},\"more\":[]}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(new KeyChord(0x30, Ctrl | Alt), store.Chord(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Ctrl+Alt+B"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:268 (keys are compared exactly)
    public void An_override_under_a_differently_cased_name_belongs_to_no_shortcut()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"ToggleTerminal\":{}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(Default(AppShortcut.ToggleTerminal), store.Chord(AppShortcut.ToggleTerminal));
        });
    }

    // ------------------------------------------------------------------ PR-SETTINGS-223 resolution

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:276-279, :312-314
    public void An_override_wins_outright_absent_means_default_and_a_stored_nil_means_cleared()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var recorded = new KeyChord(0x30, Ctrl | Shift);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(new KeyChord(0x24, Ctrl), store.Chord(AppShortcut.ToggleTerminal));
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal));

            store.Set(recorded, AppShortcut.ToggleTerminal);
            Assert.Equal(recorded, store.Chord(AppShortcut.ToggleTerminal));
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));

            store.Set(null, AppShortcut.ToggleTerminal);
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
        });

        var relaunched = Load(runtime); // a cleared shortcut stays cleared across relaunches
        OnMain(runtime, relaunched, () =>
        {
            Assert.Null(relaunched.Chord(AppShortcut.ToggleTerminal));
            Assert.True(relaunched.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Null(relaunched.KeyboardShortcut(AppShortcut.ToggleTerminal));
            Assert.Null(ShortcutStore.Label(AppShortcut.ToggleTerminal));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleTerminal));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:312-318
    public void A_command_is_customised_by_the_presence_of_an_override_even_when_it_resolves_to_its_default()
    {
        using var runtime = new TestRuntime();
        // nil, which is also showMainWindow's default (OD-0012 G6); and newThread's own default Ctrl+N stored.
        Seed(runtime, "{\"showMainWindow\":{},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":2}}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Null(store.Chord(AppShortcut.ShowMainWindow));
            Assert.Null(AppShortcut.ShowMainWindow.DefaultChord());
            Assert.True(store.IsCustomized(AppShortcut.ShowMainWindow));
            Assert.Equal(Default(AppShortcut.NewThread), store.Chord(AppShortcut.NewThread));
            Assert.True(store.IsCustomized(AppShortcut.NewThread));
            Assert.True(store.HasCustomizations(AppShortcutSection.Window));
            Assert.True(store.HasCustomizations(AppShortcutSection.Threads));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:21-30, :80-101, :281-283 (ED-099 (6))
    public void The_menu_shortcut_follows_the_resolved_chord()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
            {
                var expected = row.Code is null ? (KeyboardShortcut?)null : new KeyboardShortcut(new KeyEquivalent(row.Equivalent!), row.Mods);
                Assert.Equal(expected, store.KeyboardShortcut(row.Shortcut));
            }

            // A function key maps to its F-number equivalent (:67-69, :98); a bare key keeps no modifiers.
            store.Set(new KeyChord(0x3F, Shift), AppShortcut.ToggleTerminal); // F5
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("F5"), Shift), store.KeyboardShortcut(AppShortcut.ToggleTerminal));
            store.Set(new KeyChord(0x58, default), AppShortcut.ToggleTerminal); // F12
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("F12"), ChordModifiers.None), store.KeyboardShortcut(AppShortcut.ToggleTerminal));
            store.Set(new KeyChord(0xE049, Ctrl | Alt), AppShortcut.ToggleTerminal); // PgUp
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("pageUp"), Ctrl | Alt), store.KeyboardShortcut(AppShortcut.ToggleTerminal));

            // A key a menu cannot hold gives no menu shortcut (:22), though the chord itself is kept.
            store.Set(new KeyChord(0x4E, Ctrl), AppShortcut.ToggleTerminal);
            Assert.Equal(new KeyChord(0x4E, Ctrl), store.Chord(AppShortcut.ToggleTerminal));
            Assert.Null(store.KeyboardShortcut(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Ctrl+Key 78"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!)); // :72, ED-099 (2)
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:285-298
    public void Label_and_hint_read_through_the_shared_store_and_vanish_when_the_chord_is_cleared()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var remapped = new KeyChord(0x24, Ctrl | Alt);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
            {
                Assert.Equal(row.Rendered, ShortcutStore.Label(row.Shortcut));
                Assert.Equal(row.Rendered is null ? "" : " (" + row.Rendered + ")", ShortcutStore.Hint(row.Shortcut));
            }

            Assert.Equal(Utf8("Hide terminal (Ctrl+J)"), Utf8("Hide terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal))); // :292-293
            store.Set(remapped, AppShortcut.ToggleTerminal);
            Assert.Equal(Utf8("Ctrl+Alt+J"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
            Assert.Equal(Utf8(" (Ctrl+Alt+J)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleTerminal)));
            store.Set(null, AppShortcut.ToggleTerminal);
            Assert.Null(ShortcutStore.Label(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Hide terminal"), Utf8("Hide terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal)));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:243, :288-298 (the static helpers read `shared`, not another store)
    public void Label_and_hint_read_the_shared_store_only()
    {
        using var runtime = new TestRuntime();
        var shared = Load(runtime);
        var other = Load(runtime);
        OnMain(runtime, shared, () =>
        {
            other.Set(null, AppShortcut.ToggleTerminal);
            Assert.Equal("Ctrl+J", ShortcutStore.Label(AppShortcut.ToggleTerminal));
            shared.Set(new KeyChord(0x24, Ctrl | Shift), AppShortcut.ToggleTerminal);
            Assert.Equal(Utf8(" (Ctrl+Shift+J)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleTerminal)));
        });
    }

    // ------------------------------------------------------------------ PR-SETTINGS-219 refusals

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:322
    public void Re_recording_a_shortcuts_own_chord_is_never_refused()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table.Where(r => r.Code is not null))
                Assert.Null(store.Refusal(new KeyChord(row.Code!.Value, row.Mods), row.Shortcut));

            // The self check runs first: even a chord every other check would refuse is allowed for its holder.
            var bareN = new KeyChord(0x31, default);
            store.Set(bareN, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(bareN, AppShortcut.ToggleSidebar));
            var bareShift = new KeyChord(0x2A, default);
            store.Set(bareShift, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(bareShift, AppShortcut.ToggleSidebar));
            var ctrlC = new KeyChord(0x2E, Ctrl);
            store.Set(ctrlC, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(ctrlC, AppShortcut.ToggleSidebar));
            var numpadPlus = new KeyChord(0x4E, Ctrl);
            store.Set(numpadPlus, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(numpadPlus, AppShortcut.ToggleSidebar));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:39-41, :324 (ED-099 (2))
    public void A_bare_modifier_key_is_refused_with_Add_a_key_to_the_modifiers_whatever_is_held_with_it()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var code in ModifierKeyCodes)
                foreach (var mods in new[] { ChordModifiers.None, Ctrl, Shift, Alt, Ctrl | Alt | Shift })
                    Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, mods), AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:75-78, :325-327 (OD-0008 B7, ED-099 (2), (4))
    public void A_key_that_types_or_moves_the_caret_pressed_bare_or_with_Shift_only_is_refused_with_the_Windows_wording()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(47, CharacterCodes.Length);
            foreach (var code in CharacterCodes.Concat(CaretCodes))
            {
                Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(code, default), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread)!));
                // The Win key is masked out, so Win alone is still bare (ED-099 (3)).
                Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(code, Win | Shift), AppShortcut.NewThread)!));
            }

            // With Alt the same keys pass the typing check (:325 subtracts only Shift).
            Assert.Null(store.Refusal(new KeyChord(0x31, Alt), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0x39, Alt | Shift), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0xE048, Alt), AppShortcut.NewThread));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:60-65, :77, :80-101, :325-328 (ED-099 (2))
    public void Navigation_and_function_keys_do_not_type_so_a_bare_one_is_allowed()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        // Home, End, PgUp, PgDn and F1 to F12 have labels and menu equivalents but are not in the typing list (:77).
        long[] codes = [0xE047, 0xE04F, 0xE049, 0xE051, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58];
        OnMain(runtime, store, () =>
        {
            foreach (var code in codes)
            {
                Assert.Null(store.Refusal(new KeyChord(code, default), AppShortcut.NewThread));
                Assert.Null(store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:22, :80-101, :328 (ED-099 (2))
    public void A_key_with_no_menu_equivalent_is_refused_with_Menus_cannot_use_this_key()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        // Keys outside the US-position table: numpad *, Scroll Lock, numpad +, the ISO key beside left Shift.
        long[] codes = [0x37, 0x46, 0x4E, 0x56];
        OnMain(runtime, store, () =>
        {
            foreach (var code in codes)
            {
                foreach (var mods in new[] { Ctrl, Alt, Ctrl | Shift })
                    Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, mods), AppShortcut.NewThread)!));
                // Bare, such a key does not type (:77), so the menu check is the one that fires.
                Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, default), AppShortcut.NewThread)!));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:22, :72, :77, :80-101, :325-328 (ED-099 (1), (2))
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:72, :276-279, :281-289 (ED-099 (2))
    public void An_extended_key_and_its_non_extended_twin_are_different_positions()
    {
        // ED-099 (1): extended keys are 0xE000 | code, and :72, :77 and :80-101 are exact dictionary lookups. So
        // numpad 7 (0x47) is not Home (0xE047), numpad Enter (0xE01C) is not Return (0x1C), and numpad slash
        // (0xE035) is not '/' (0x35): none of the three is in the table.
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        (long Code, string Bare, string WithCtrl)[] outside =
        [
            (0x47, "Key 71", "Ctrl+Key 71"),
            (0xE01C, "Key 57372", "Ctrl+Key 57372"),
            (0xE035, "Key 57397", "Ctrl+Key 57397"),
        ];
        OnMain(runtime, store, () =>
        {
            foreach (var (code, bare, withCtrl) in outside)
            {
                // Not a typing key (:77), so bare or with Shift the menu check fires, not the typing refusal (:325-328).
                Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, default), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, Ctrl), AppShortcut.NewThread)!));
                Assert.Null(new KeyChord(code, Ctrl).KeyboardShortcut); // :22
                Assert.Equal(Utf8(bare), Utf8(new KeyChord(code, default).Description)); // :72
                Assert.Equal(Utf8(withCtrl), Utf8(new KeyChord(code, Ctrl).Description));

                // Through the store: the chord is kept, has no menu shortcut, and labels as "Key <decimal>".
                store.Set(new KeyChord(code, Ctrl), AppShortcut.ToggleTerminal);
                Assert.Null(store.KeyboardShortcut(AppShortcut.ToggleTerminal));
                Assert.Equal(Utf8(withCtrl), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
                Assert.Equal(Utf8(" (" + withCtrl + ")"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleTerminal)));
            }

            // Their twins in the table behave as the table says.
            Assert.Null(store.Refusal(new KeyChord(0xE047, default), AppShortcut.NewThread)); // Home does not type
            Assert.Equal(Utf8("Home"), Utf8(new KeyChord(0xE047, default).Description));
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0x1C, default), AppShortcut.NewThread)!)); // Return
            Assert.Equal(Utf8("Return"), Utf8(new KeyChord(0x1C, default).Description));
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0x35, default), AppShortcut.NewThread)!)); // '/'
            Assert.Equal(Utf8("/"), Utf8(new KeyChord(0x35, default).Description));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:251-257, :329 (OD-0008 B8, ED-099 (2), (4))
    public void The_18_reserved_chords_are_refused_with_the_Windows_wording_and_only_with_exactly_their_modifiers()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(18, Reserved.Length);
            foreach (var (code, mods, rendered) in Reserved)
            {
                var chord = new KeyChord(code, mods);
                Assert.Equal(Utf8(rendered), Utf8(chord.Description));
                foreach (var row in Table)
                    Assert.Equal(Utf8(ReservedRefusal), Utf8(store.Refusal(chord, row.Shortcut)!));
                // The Win key is masked out, so it changes nothing (ED-099 (3)).
                Assert.Equal(Utf8(ReservedRefusal), Utf8(store.Refusal(new KeyChord(code, mods | Win), AppShortcut.NewThread)!));
                // Equality is exact: adding Alt makes a chord the list does not hold (:329 `contains`).
                Assert.Null(store.Refusal(new KeyChord(code, mods | Alt), AppShortcut.NewThread));
            }

            // Ctrl+Shift with the other reserved letters is not reserved; only Z is listed with Shift (:254).
            Assert.Null(store.Refusal(new KeyChord(0x2E, Ctrl | Shift), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0x10, Ctrl | Shift), AppShortcut.NewThread));
            // Ctrl+0 and Ctrl+Y are not in the list (the digits run 1 to 9; ED-099 withdraws ED-050 (b)'s Ctrl+Y).
            Assert.Null(store.Refusal(new KeyChord(0x0B, Ctrl), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0x15, Ctrl), AppShortcut.NewThread));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:251-257, :321-333; App/AppCommands.swift:53-62 (failure case: zoom chords unguarded)
    public void The_hard_coded_zoom_chords_are_neither_reserved_nor_owned_so_they_are_accepted()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // Ctrl+- (scan 0x0C), Ctrl+= (0x0D) and Ctrl+0 (0x0B).
            foreach (var code in new long[] { 0x0C, 0x0D, 0x0B })
                foreach (var row in Table)
                    Assert.Null(store.Refusal(new KeyChord(code, Ctrl), row.Shortcut));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:330-332
    public void A_chord_another_command_holds_is_refused_with_that_commands_title()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(Utf8("Command palette already uses this chord."), Utf8(store.Refusal(new KeyChord(0x25, Ctrl), AppShortcut.NewThread)!));
            foreach (var holder in Table.Where(r => r.Code is not null))
                foreach (var other in Table.Where(r => r.Shortcut != holder.Shortcut))
                    Assert.Equal(Utf8(holder.Title + " already uses this chord."), Utf8(store.Refusal(new KeyChord(holder.Code!.Value, holder.Mods), other.Shortcut)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:330-333
    public void The_owner_check_reads_current_bindings_and_names_the_first_holder_in_declaration_order()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var ctrlK = new KeyChord(0x25, Ctrl);
        OnMain(runtime, store, () =>
        {
            // Two holders: newThread is declared before commandPalette (:106, :122).
            store.Set(ctrlK, AppShortcut.NewThread);
            Assert.Equal(Utf8("New thread already uses this chord."), Utf8(store.Refusal(ctrlK, AppShortcut.OpenSettings)!));
            Assert.Null(store.Refusal(ctrlK, AppShortcut.CommandPalette)); // its own chord (:322)

            // Freed by clearing both holders: nothing refuses it (:333).
            store.Set(null, AppShortcut.NewThread);
            store.Set(null, AppShortcut.CommandPalette);
            Assert.Null(store.Refusal(ctrlK, AppShortcut.OpenSettings));
            Assert.Null(store.Refusal(ctrlK, AppShortcut.CommandPalette));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:330-332
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:185 (OD-0002, OD-0012 F1)
    public void A_chord_held_by_Show_Marrow_is_refused_with_the_renamed_title()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var ctrlD = new KeyChord(0x20, Ctrl);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.ToggleChanges);
            store.Set(ctrlD, AppShortcut.ShowMainWindow);
            Assert.Equal(Utf8("Show Marrow already uses this chord."), Utf8(store.Refusal(ctrlD, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:322-333 (the order of the checks)
    public void The_checks_run_in_source_order_and_the_first_that_fires_wins()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // Modifier key before the menu check: right Ctrl has no menu equivalent either (:324 before :328).
            Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(0xE01D, default), AppShortcut.NewThread)!));

            // Typing before the owner check: a bare key another command holds (:325 before :330).
            var bareN = new KeyChord(0x31, default);
            store.Set(bareN, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(bareN, AppShortcut.NewThread)!));

            // Menu before the owner check: a numpad chord another command holds (:328 before :330).
            var numpad = new KeyChord(0x4E, Ctrl);
            store.Set(numpad, AppShortcut.ToggleTerminal);
            Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(numpad, AppShortcut.NewThread)!));

            // Reserved before the owner check: Ctrl+C held by a command still reads as reserved (:329 before :330).
            var ctrlC = new KeyChord(0x2E, Ctrl);
            store.Set(ctrlC, AppShortcut.ToggleChanges);
            Assert.Equal(Utf8(ReservedRefusal), Utf8(store.Refusal(ctrlC, AppShortcut.NewThread)!));

            // A modifier key held by a command is still the modifier refusal for another (:324 before :330).
            var bareShift = new KeyChord(0x2A, default);
            store.Set(bareShift, AppShortcut.OpenSettings);
            Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(bareShift, AppShortcut.NewThread)!));

            // A free chord with Ctrl passes every check (:333).
            Assert.Null(store.Refusal(new KeyChord(0x31, Ctrl | Alt), AppShortcut.NewThread));
        });
    }
}
