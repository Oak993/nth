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
        (AppShortcut.ShowMainWindow, "showMainWindow", AppShortcutSection.Window, "Show nth", "Brings the main window to the front.", null, default, null, null),
        (AppShortcut.OpenSettings, "openSettings", AppShortcutSection.Window, "Settings", "Opens this window.", 0x33, Ctrl, "Ctrl+,", ","),
        // PORT-ONLY (web W3b, DESIGN 3.7, ED-NEW-web-w3b-1): the pane workspace's commands, after the source's own.
        (AppShortcut.ToggleBrowser, "toggleBrowser", AppShortcutSection.Window, "Toggle Visus", "Shows or hides the browser pane.", 0x30, Ctrl | Shift, "Ctrl+Shift+B", "b"),
        (AppShortcut.ToggleSideChat, "toggleSideChat", AppShortcutSection.Window, "Toggle side chat", "Shows or hides a side conversation about this thread.", 0x27, Ctrl, "Ctrl+;", ";"),
        (AppShortcut.ToggleArtifacts, "toggleArtifacts", AppShortcutSection.Window, "Toggle Tabula", "Shows or hides the artifacts pane.", 0x1E, Ctrl | Shift, "Ctrl+Shift+A", "a"),
        (AppShortcut.ToggleBackgroundTasks, "toggleBackgroundTasks", AppShortcutSection.Window, "Toggle background tasks", "Shows or hides the list of running jobs.", 0x22, Ctrl | Shift, "Ctrl+Shift+G", "g"),
        (AppShortcut.ClosePane, "closePane", AppShortcutSection.Window, "Close pane", "Closes the focused pane.", 0x2B, Ctrl, "Ctrl+\\", "\\"),
        (AppShortcut.ExpandPane, "expandPane", AppShortcutSection.Window, "Expand or restore pane", "Springs the focused pane over the workspace, or back.", 0x2B, Ctrl | Shift, "Ctrl+Shift+\\", "\\"),
        (AppShortcut.NextPane, "nextPane", AppShortcutSection.Window, "Next pane", "Moves focus to the next pane, then the dividers.", 0x40, default, "F6", "F6"),
        (AppShortcut.PreviousPane, "previousPane", AppShortcutSection.Window, "Previous pane", "Moves focus to the previous pane.", 0x40, Shift, "Shift+F6", "F6"),
        (AppShortcut.MovePaneEarlier, "movePaneEarlier", AppShortcutSection.Window, "Move pane earlier", "Moves the focused pane one place earlier in the layout.", 0xE04B, Ctrl | Alt, "Ctrl+Alt+\u2190", "leftArrow"),
        (AppShortcut.MovePaneLater, "movePaneLater", AppShortcutSection.Window, "Move pane later", "Moves the focused pane one place later in the layout.", 0xE04D, Ctrl | Alt, "Ctrl+Alt+\u2192", "rightArrow"),
        (AppShortcut.JoinPaneTabs, "joinPaneTabs", AppShortcutSection.Window, "Join or leave tabs", "Joins the focused pane to the one before it as a tab, or splits it out of its group.", 0x14, Ctrl | Alt, "Ctrl+Alt+T", "t"),
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
    private const string ReservedRefusal = "Windows or nth already uses this chord."; // :329 (OD-0008 B8, ED-099 (4))
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
        var stored = Stored(runtime);
        Assert.Contains(permutations, p => p.AsSpan().SequenceEqual(stored));
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
            Assert.Equal(Utf8("Show nth already uses this chord."), Utf8(store.Refusal(ctrl0, AppShortcut.NewThread)!));
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
        Assert.Equal(7 + 11, AppShortcutSection.Window.Shortcuts().Count()); // the source's seven and the eleven pane commands (W3b)
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
        Assert.Equal(Enumerable.Repeat(Key, 7 + 11), runtime.Defaults.Writes); // the source's seven and web W3b's eleven
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
                // F6 and Shift+F6 are the pane workspace's own defaults (web W3b): they are refused as taken by "Next
                // pane" and "Previous pane", never as typing keys.
                var nextPane = code == 0x40 ? "Next pane already uses this chord." : null;
                var previousPane = code == 0x40 ? "Previous pane already uses this chord." : null;
                Assert.Equal(nextPane, store.Refusal(new KeyChord(code, default), AppShortcut.NewThread));
                Assert.Equal(previousPane, store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread));
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
            Assert.Equal(Utf8("Show nth already uses this chord."), Utf8(store.Refusal(ctrlD, AppShortcut.NewThread)!));
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

    // ==================================================================
    // Part 2 of 3: PR-TERM-069, PR-WIN-116 to PR-WIN-138.
    //
    // Every expected value below is transcribed independently from the pinned Swift (B2 1fbf81e) and from
    // ED-099 / OD-0008 / OD-0010, never from Marrow.Core.Models.Shortcuts. Where a helper already defined
    // above (Table, Reserved, CharacterCodes, CaretCodes, ModifierKeyCodes, AssertShortcut, Load, OnMain,
    // Seed, Stored, AssertStoredChord, ChordJson, Utf8) already carries the same independently-derived
    // value, it is reused rather than retyped.
    // ==================================================================

    [Fact]
    [Trait("Req", "PR-TERM-069")] // Core/Models/Shortcuts.swift:124, :161-162, :139, :182, :206, :230 (ED-099, OD-0008 B7)
    public void Toggle_terminal_sits_in_Window_and_panels_titled_and_described_verbatim_and_ships_Ctrl_J()
    {
        AssertShortcut(AppShortcut.ToggleTerminal, AppShortcut.CommandPalette);

        // AssertShortcut only checks the section by enum (row.Section == shortcut.Section()); the record's
        // expected_result names the section by its title, "Window and panels". Check the title itself so a port
        // with the wrong section title (for example "Window & panels") cannot pass this record's test while
        // PR-WIN-122 (which does check titles, but only for its own two sections' Table rows) catches it late or
        // not at all for this shortcut specifically. :139 `case .window: "Window and panels"`; :161-162 places
        // .toggleTerminal in .window.
        Assert.Equal(Utf8("Window and panels"), Utf8(AppShortcut.ToggleTerminal.Section().Title()));
    }

    [Fact]
    [Trait("Req", "PR-WIN-116")] // Core/Models/Shortcuts.swift:5-18 (ED-099 (2), (3))
    public void A_chord_keeps_only_its_key_position_and_the_allowed_modifiers_masked_on_construction_and_again_on_read()
    {
        using var runtime = new TestRuntime();

        // Construction masks (:13): the Win bit (Avalonia Meta) and any bit outside Control, Alt, Shift never survive.
        Assert.Equal(Ctrl, new KeyChord(0x31, Ctrl | Win).ModifierFlags);
        Assert.Equal(2UL, (ulong)new KeyChord(0x31, Ctrl | Win).Modifiers);
        // A chord "recorded" with an extra bit equals the same chord recorded without it: only the masked set matters.
        Assert.Equal(new KeyChord(0x31, Ctrl), new KeyChord(0x31, Ctrl | Win));
        Assert.Equal(new KeyChord(0x31, ChordModifiers.None), new KeyChord(0x31, Win));

        // Masking is an INTERSECTION with the allowed set (:13 `modifiers.intersection(Self.allowedModifiers)`),
        // not merely "clear the Win bit": other disallowed bits (an arbitrary middle bit and the top bit) are
        // cleared too, leaving only Control.
        var noisy = new KeyChord(0x31, (ChordModifiers)0x10 | (ChordModifiers)0x80000000 | Ctrl);
        Assert.Equal(2UL, (ulong)noisy.Modifiers);
        Assert.Equal(new KeyChord(0x31, Ctrl), noisy);

        // The synthesized decoder assigns the raw stored bits directly, bypassing the masking init (:5, :8-9), so a
        // stored value can carry bits outside the allowed set; ModifierFlags masks again on every read (:16-18).
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":49,\"modifiers\":4294967298}}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            var chord = store.Chord(AppShortcut.ToggleTerminal)!;
            Assert.Equal(4294967298UL, (ulong)chord.Modifiers); // raw, unmasked
            Assert.Equal(Ctrl, chord.ModifierFlags); // masked again on read: only bit 2 (Control) is allowed

            // Equality is the synthesized Equatable of `struct KeyChord: Codable, Hashable` (:5), which compares
            // the stored raw modifiers (:9), not modifierFlags. The decoded chord carries raw modifiers
            // 4294967298 and does NOT equal a chord built through the masking init with the same key and the
            // same ModifierFlags: the "masked set" wording of the record's expected_result holds only for chords
            // built through the masking init (:11-14), not for one assigned directly by the decoder.
            Assert.NotEqual(new KeyChord(49, Ctrl), chord);
        });

        // :16-18: the two places that actually READ the masked flags back, not just the ModifierFlags property in
        // isolation. A disallowed bit inside the 32-bit range (Win, bit 8) must not survive into either
        // `keyboardShortcut` (:24 `let flags = modifierFlags`) or `description` (:33 `KeyNames.modifierSymbols(modifierFlags)`).
        // A port building KeyboardShortcut straight from `(ChordModifiers)Modifiers`, without going through
        // ModifierFlags, would truncate the 4294967298 case above (mod 2^32 leaves bit 1) but pass; this seeds a
        // value where the disallowed bit is inside 32 bits and only masking, not truncation, removes it.
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":{\"keyCode\":36,\"modifiers\":10}}}"); // Control (2) | Win (8)
        var store2 = Load(runtime);
        OnMain(runtime, store2, () =>
        {
            var chord = store2.Chord(AppShortcut.ToggleTerminal)!;
            Assert.Equal(10UL, (ulong)chord.Modifiers); // raw, unmasked: Control and Win both present
            Assert.Equal(Utf8("Ctrl+J"), Utf8(chord.Description)); // :32-34, only Control survives into the label
            Assert.Equal(
                new KeyboardShortcut(new KeyEquivalent("j"), Ctrl), // :21-30, only Control survives into the shortcut
                chord.KeyboardShortcut);
        });

        // :8 "Two chords are equal when their key position ... are equal": a negative case on the key position.
        Assert.NotEqual(new KeyChord(0x31, Ctrl), new KeyChord(0x30, Ctrl));
        // A negative case on the masked modifier set.
        Assert.NotEqual(new KeyChord(0x31, Ctrl), new KeyChord(0x31, Ctrl | Shift));
    }

    [Fact]
    [Trait("Req", "PR-WIN-117")] // Core/Models/Shortcuts.swift:32-34, :43-50 (OD-0008 B7, ED-099 (4))
    public void A_chords_description_is_Ctrl_Alt_Shift_in_that_fixed_order_each_followed_by_a_plus_then_the_label()
    {
        Assert.Equal(Utf8("Ctrl+Shift+N"), Utf8(new KeyChord(0x31, Shift | Ctrl).Description)); // Shift-Command-N
        Assert.Equal(Utf8("Ctrl+Alt+U"), Utf8(new KeyChord(0x16, Alt | Ctrl).Description)); // Option-Command-U
        Assert.Equal(Utf8("Ctrl+Tab"), Utf8(new KeyChord(0x0F, Ctrl).Description)); // Control-Tab
        // Every order of construction yields the same fixed rendering order (Ctrl, then Alt, then Shift).
        Assert.Equal(Utf8("Ctrl+Alt+Shift+N"), Utf8(new KeyChord(0x31, Shift | Alt | Ctrl).Description));
        Assert.Equal(Utf8("Ctrl+Alt+Shift+N"), Utf8(new KeyChord(0x31, Ctrl | Shift | Alt).Description));
        Assert.Equal(Utf8("Ctrl+Alt+Shift+N"), Utf8(new KeyChord(0x31, Alt | Ctrl | Shift).Description));
        // No modifiers: the label alone, no leading separator.
        Assert.Equal(Utf8("N"), Utf8(new KeyChord(0x31, ChordModifiers.None).Description));
    }

    [Fact]
    [Trait("Req", "PR-WIN-118")] // Core/Models/Shortcuts.swift:52-73 (ED-099 (2))
    public void Key_labels_come_from_the_character_table_then_the_special_table_then_a_Key_code_fallback()
    {
        using var runtime = new TestRuntime();

        // The character table (:52-58), lower-cased letters shown upper-case, in the Swift declaration's order,
        // mapped one for one onto scan codes (ED-099 (2)); CharacterCodes above carries the same 47 codes in order.
        string[] characterLabels =
        [
            "A", "S", "D", "F", "H", "G", "Z", "X", "C", "V", "B", "Q",
            "W", "E", "R", "Y", "T", "1", "2", "3", "4", "6", "5",
            "=", "9", "7", "-", "8", "0", "]", "O", "U", "[", "I",
            "P", "L", "J", "'", "K", ";", "\\", ",", "/", "N", "M",
            ".", "`",
        ];
        Assert.Equal(47, CharacterCodes.Length);
        Assert.Equal(characterLabels.Length, CharacterCodes.Length);
        for (var i = 0; i < CharacterCodes.Length; i++)
            Assert.Equal(Utf8(characterLabels[i]), Utf8(KeyNames.Label(CharacterCodes[i])));

        // The deliberate crossover (:53-54): scan 0x07 (macOS 22) labels "6"; scan 0x06 (macOS 23) labels "5".
        Assert.Equal("6", KeyNames.Label(0x07));
        Assert.Equal("5", KeyNames.Label(0x06));

        // The special table (:60-65), 26 names, mapped one for one onto scan codes (ED-099 (2)).
        (long Code, string Label)[] special =
        [
            (0x1C, "Return"), (0x0F, "Tab"), (0x39, "Space"), (0x0E, "Delete"), (0x01, "Esc"), (0xE053, "Del"),
            (0xE04B, "\U00002190"), // U+2190 LEFTWARDS ARROW
            (0xE04D, "\U00002192"), // U+2192 RIGHTWARDS ARROW
            (0xE050, "\U00002193"), // U+2193 DOWNWARDS ARROW
            (0xE048, "\U00002191"), // U+2191 UPWARDS ARROW
            (0xE047, "Home"), (0xE04F, "End"), (0xE049, "PgUp"), (0xE051, "PgDn"),
            (0x3B, "F1"), (0x3C, "F2"), (0x3D, "F3"), (0x3E, "F4"), (0x3F, "F5"), (0x40, "F6"), (0x41, "F7"),
            (0x42, "F8"), (0x43, "F9"), (0x44, "F10"), (0x57, "F11"), (0x58, "F12"),
        ];
        Assert.Equal(26, special.Length);
        foreach (var (code, label) in special)
            Assert.Equal(Utf8(label), Utf8(KeyNames.Label(code)));

        // The fallback (:72): an unmapped scan code, for example numpad * (0x37), reads "Key <decimal>", and the
        // failure_cases note says such a chord is not rejected: it is still stored and still fires.
        Assert.Equal("Key 55", KeyNames.Label(0x37));

        // Extended-flag and numpad aliasing negatives (ED-099 (2)): stripping the 0xE000 extended flag or
        // conflating a numpad code with its non-numpad neighbour must NOT land in the special table.
        Assert.Equal("Key 71", KeyNames.Label(0x47)); // numpad 7, not Home (0xE047)
        Assert.Equal("Key 57372", KeyNames.Label(0xE01C)); // numpad Enter, not Return (0x1C)
        Assert.Equal("Key 83", KeyNames.Label(0x53)); // numpad '.', not Del (0x53 has no entry; 117 macOS is 0xE053)

        // Exact-set sweep (:72 `characters[code] ?? specialLabels[code] ?? "Key \(code)"`): outside the 47
        // character codes and the 26 special codes, the fallback applies on EVERY other scan code, not just the
        // spot codes above. A wrong port mapping numpad digits (0x47-0x53) to characters, or F13-F24 (0x64-0x6E)
        // to function names, would still pass the spot checks but fail this sweep.
        var namedCodes = new HashSet<long>(CharacterCodes);
        foreach (var (code, _) in special) namedCodes.Add(code);
        Assert.Equal(73, namedCodes.Count); // 47 characters + 26 special
        for (long code = 0x00; code <= 0x7F; code++)
        {
            if (namedCodes.Contains(code)) continue;
            Assert.Equal("Key " + Decimal(code), KeyNames.Label(code));
        }
        for (long code = 0xE000; code <= 0xE07F; code++)
        {
            if (namedCodes.Contains(code)) continue;
            Assert.Equal("Key " + Decimal(code), KeyNames.Label(code));
        }

        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(new KeyChord(0x37, Ctrl), AppShortcut.ToggleTerminal);
            // The Shared ambient is only overridden for the duration of OnMain's scope, and Label reads
            // Shared.Current (:288-290): the assertion must run inside that scope, not after it is disposed,
            // or it reads the process-wide lazy default store instead of the store under test (R-TEST-5).
            Assert.Equal(Utf8("Ctrl+Key 55"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-119")] // Core/Models/Shortcuts.swift:80-101 (ED-099 (6))
    public void A_key_converts_to_its_menu_equivalent_through_three_tiers_or_to_nothing_at_all()
    {
        using var runtime = new TestRuntime();

        // Character keys convert to their lower-case character (:81); the lower-case letters below are transcribed
        // independently, in the same declaration order as CharacterCodes (:52-58, ED-099 (2)). The expected value
        // is the literal SwiftUI spelling (ED-099 (6)), never a port-side factory, so the test does not depend on
        // KeyEquivalent.Character existing or being implemented correctly.
        char[] characterEquivalents =
        [
            'a', 's', 'd', 'f', 'h', 'g', 'z', 'x', 'c', 'v', 'b', 'q',
            'w', 'e', 'r', 'y', 't', '1', '2', '3', '4', '6', '5',
            '=', '9', '7', '-', '8', '0', ']', 'o', 'u', '[', 'i',
            'p', 'l', 'j', '\'', 'k', ';', '\\', ',', '/', 'n', 'm',
            '.', '`',
        ];
        Assert.Equal(CharacterCodes.Length, characterEquivalents.Length);
        for (var i = 0; i < CharacterCodes.Length; i++)
            Assert.Equal(new KeyEquivalent(characterEquivalents[i].ToString()), KeyNames.KeyEquivalent(CharacterCodes[i]));

        // The fourteen named keys convert to their named equivalents (:83-96), literal SwiftUI spellings
        // (ED-099 (6), Shortcuts.swift:81-99): tab, space, escape, home, end, pageUp and pageDown are not pinned
        // by Port/Keyboard.cs's own KeyboardTests.cs, so they must be pinned here.
        (long Code, KeyEquivalent Expected)[] named =
        [
            (0x1C, new KeyEquivalent("return")), (0x0F, new KeyEquivalent("tab")), (0x39, new KeyEquivalent("space")),
            (0x0E, new KeyEquivalent("delete")), (0x01, new KeyEquivalent("escape")), (0xE053, new KeyEquivalent("deleteForward")),
            (0xE04B, new KeyEquivalent("leftArrow")), (0xE04D, new KeyEquivalent("rightArrow")), (0xE050, new KeyEquivalent("downArrow")),
            (0xE048, new KeyEquivalent("upArrow")), (0xE047, new KeyEquivalent("home")), (0xE04F, new KeyEquivalent("end")),
            (0xE049, new KeyEquivalent("pageUp")), (0xE051, new KeyEquivalent("pageDown")),
        ];
        Assert.Equal(14, named.Length);
        foreach (var (code, expected) in named)
            Assert.Equal(expected, KeyNames.KeyEquivalent(code));

        // Function keys convert through the private-use scalar convention, F1 to F12 (:97-99), literal spellings
        // "F1".."F12" (ED-099 (6)); F2..F11 are not pinned by KeyboardTests.cs, so they must be pinned here.
        (long Code, string Name)[] functions =
        [
            (0x3B, "F1"), (0x3C, "F2"), (0x3D, "F3"), (0x3E, "F4"), (0x3F, "F5"), (0x40, "F6"),
            (0x41, "F7"), (0x42, "F8"), (0x43, "F9"), (0x44, "F10"), (0x57, "F11"), (0x58, "F12"),
        ];
        foreach (var (code, name) in functions)
            Assert.Equal(new KeyEquivalent(name), KeyNames.KeyEquivalent(code));

        // Every other key code converts to nothing (:99): numpad * and Scroll Lock, both outside the three tables.
        Assert.Null(KeyNames.KeyEquivalent(0x37));
        Assert.Null(KeyNames.KeyEquivalent(0x46));
        // Extended-flag/numpad aliasing negative (ED-099 (2)): numpad Enter (0xE01C) is not Return (0x1C).
        Assert.Null(KeyNames.KeyEquivalent(0xE01C));

        // Exact-set sweep (:97-99 `guard let number = functionKeys[code] ... else { return nil }`): non-null
        // exactly on the 47 character codes, the 14 named codes and the 12 function codes (73 total), and null on
        // every other scan code. A port mapping numpad digits or F13-F24 to equivalents would fail this sweep even
        // though it passes the spot checks above.
        var convertibleCodes = new HashSet<long>(CharacterCodes);
        foreach (var (code, _) in named) convertibleCodes.Add(code);
        foreach (var (code, _) in functions) convertibleCodes.Add(code);
        Assert.Equal(73, convertibleCodes.Count);
        for (long code = 0x00; code <= 0x7F; code++)
            Assert.Equal(convertibleCodes.Contains(code), KeyNames.KeyEquivalent(code) is not null);
        for (long code = 0xE000; code <= 0xE07F; code++)
            Assert.Equal(convertibleCodes.Contains(code), KeyNames.KeyEquivalent(code) is not null);

        // The evidence at :22 (`guard let key = KeyNames.keyEquivalent(for: keyCode) else { return nil }`): a
        // chord on a key with no menu equivalent carries no keyboardShortcut either, whatever its modifiers.
        Assert.Null(new KeyChord(0x37, Ctrl).KeyboardShortcut);

        // Modifier pass-through (:23-29): control, option, shift and command (both read as Control, ED-099 (3))
        // each insert their own EventModifiers flag; F1 with all three carries all three through.
        Assert.Equal(
            new KeyboardShortcut(new KeyEquivalent("F1"), Ctrl | Alt | Shift),
            new KeyChord(0x3B, Ctrl | Alt | Shift).KeyboardShortcut);

        // A command overridden to a chord with no menu equivalent has a non-null Chord but a null
        // KeyboardShortcut (:281-283): the store "simply carries no menu shortcut" for it.
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(new KeyChord(0x37, Ctrl), AppShortcut.ToggleTerminal);
            Assert.NotNull(store.Chord(AppShortcut.ToggleTerminal));
            Assert.Null(store.KeyboardShortcut(AppShortcut.ToggleTerminal));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-120")] // Core/Models/Shortcuts.swift:39-41 (ED-099 (3))
    public void Nine_modifier_key_scan_codes_are_recognised_and_never_form_a_chord_on_their_own()
    {
        // LShift, RShift, LCtrl, RCtrl, LAlt, RAlt, LWin, RWin, CapsLock (ED-099 (3); macOS fn has no scan code).
        Assert.Equal(9, ModifierKeyCodes.Length);
        foreach (var code in ModifierKeyCodes)
            Assert.True(KeyNames.IsModifierKey(code));

        // A handful of ordinary keys are not modifier keys.
        foreach (var code in new long[] { 0x1E, 0x31, 0x1C, 0x3B, 0xE047 })
            Assert.False(KeyNames.IsModifierKey(code));

        // isModifierKey is an EXACT set (:40, the ten-member Contains), not a range test: sweep every scan code
        // and its extended form and require the predicate to agree with the exact set on every one of them, so an
        // extra key such as NumLock (0x45), Scroll Lock (0x46) or Apps (0xE05D) is caught if wrongly included.
        var modifierSet = ModifierKeyCodes.ToHashSet();
        for (long code = 0x00; code <= 0x7F; code++)
            Assert.Equal(modifierSet.Contains(code), KeyNames.IsModifierKey(code));
        for (long code = 0xE000; code <= 0xE07F; code++)
            Assert.Equal(modifierSet.Contains(code), KeyNames.IsModifierKey(code));

        // The record's expected_result also names the refusal: "A chord whose key is one of them is refused"
        // (:324 `if KeyNames.isModifierKey(chord.keyCode) { return "Add a key to the modifiers." }`). Prove it here
        // directly, not only through PR-WIN-129, so a Refusal that skipped this check cannot pass this record's
        // test.
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var code in ModifierKeyCodes)
                Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, ChordModifiers.None), AppShortcut.NewThread)!));

            // A non-modifier key with the same (absent) flags is not caught by this check: bare F5 is accepted.
            Assert.Null(store.Refusal(new KeyChord(0x3F, ChordModifiers.None), AppShortcut.NewThread));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-121")] // Core/Models/Shortcuts.swift:75-78 (ED-099 (2))
    public void Fifty_seven_typing_and_caret_key_positions_are_recognised_and_navigation_and_function_keys_are_not()
    {
        using var runtime = new TestRuntime();

        Assert.Equal(47, CharacterCodes.Length);
        Assert.Equal(10, CaretCodes.Length);
        foreach (var code in CharacterCodes.Concat(CaretCodes))
            Assert.True(KeyNames.TypesText(code));

        // Function keys, Home, End, Page Up and Page Down are not in this set (:77).
        long[] notTyping = [0xE047, 0xE04F, 0xE049, 0xE051, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58];
        foreach (var code in notTyping)
            Assert.False(KeyNames.TypesText(code));

        // Modifier keys are not typing keys either.
        foreach (var code in ModifierKeyCodes)
            Assert.False(KeyNames.TypesText(code));

        // typesText is the exact union of the character table and the ten caret codes (:77 `characters[code] !=
        // nil || [...].contains(code)`), not a range test: sweep every scan code and its extended form.
        var typingSet = CharacterCodes.Concat(CaretCodes).ToHashSet();
        for (long code = 0x00; code <= 0x7F; code++)
            Assert.Equal(typingSet.Contains(code), KeyNames.TypesText(code));
        for (long code = 0xE000; code <= 0xE07F; code++)
            Assert.Equal(typingSet.Contains(code), KeyNames.TypesText(code));

        // The record's expected_result: "F5 on its own is an acceptable chord" (:325-327), because function keys,
        // Home and Page Down are not typesText, so a bare or shift-only chord on them is never refused for typing.
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Null(store.Refusal(new KeyChord(0x3F, ChordModifiers.None), AppShortcut.NewThread)); // bare F5
            Assert.Null(store.Refusal(new KeyChord(0xE047, ChordModifiers.None), AppShortcut.NewThread)); // bare Home
            Assert.Null(store.Refusal(new KeyChord(0xE051, Shift), AppShortcut.NewThread)); // bare Shift+PgDn

            // The record's central rule (OD-0008 B7 / ED-099 (4), :325-327): such a chord is refused unless it
            // carries Ctrl, Alt or (upstream) Command; Shift alone is not enough. Prove it here directly, on a
            // typing key (0x1E, A) and a caret key (0xE04B, Left arrow), both bare and Shift-only.
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0x1E, ChordModifiers.None), AppShortcut.NewThread)!));
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0x1E, Shift), AppShortcut.NewThread)!));
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0xE04B, ChordModifiers.None), AppShortcut.NewThread)!));
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(0xE04B, Shift), AppShortcut.NewThread)!));

            // Ctrl or Alt qualifies the same keys; Ctrl+Shift also qualifies (flags.subtracting(.shift) leaves
            // Control, not empty). 0xE04B (Left arrow) is not reserved and not any command's default (Table,
            // Reserved above), so a bare-Ctrl Refusal call on it can only pass through the typing check.
            Assert.Null(store.Refusal(new KeyChord(0x1E, Alt), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0x39, Ctrl | Shift), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0xE04B, Ctrl), AppShortcut.NewThread));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-122")] // Core/Models/Shortcuts.swift:130-153 (OD-0010)
    public void Exactly_two_sections_hold_their_commands_in_declaration_order_under_their_own_symbol()
    {
        Assert.Equal(new[] { AppShortcutSection.Threads, AppShortcutSection.Window }, SwiftEnum<AppShortcutSection>.AllCases); // :131-132
        Assert.Equal(Utf8("Threads"), Utf8(AppShortcutSection.Threads.Title())); // :138
        Assert.Equal(Utf8("Window and panels"), Utf8(AppShortcutSection.Window.Title())); // :139
        // The SF Symbol names, kept verbatim as the Core-layer value (:145-146).
        Assert.Equal("bubble.left.and.bubble.right.fill", AppShortcutSection.Threads.Symbol()); // :145
        Assert.Equal("macwindow", AppShortcutSection.Window.Symbol()); // :146
        // DEVIATION OD-0010: the port draws these as the Lucide icons messages-square (for
        // bubble.left.and.bubble.right.fill) and app-window-mac (for macwindow) via porting/icons/sf-symbols-to-lucide.json.
        // A Marrow.Core test has no dependency on that porting-evidence file or on the UI icon renderer (R-FILE-2,
        // layer boundary); the Lucide half of this record is proven by the UI unit that reads AppShortcutSection.Symbol()
        // and draws the section header, App/Views/Settings/ShortcutsSettingsPage.swift's port (UI.Views.Settings.ShortcutsSettingsPage),
        // not here.

        AppShortcut[] expectedThreads =
        [
            AppShortcut.NewThread, AppShortcut.NewWorktreeThread, AppShortcut.AddProject, AppShortcut.StopTurn,
            AppShortcut.QueueChat, AppShortcut.TogglePlanMode, AppShortcut.PreviousThread, AppShortcut.NextThread,
            AppShortcut.RunScript, AppShortcut.FinishThread, AppShortcut.ArchiveThread, AppShortcut.SettleThread,
        ]; // :159-160, declaration order
        AppShortcut[] expectedWindow =
        [
            AppShortcut.CommandPalette, AppShortcut.ToggleSidebar, AppShortcut.ToggleTerminal, AppShortcut.ToggleChanges,
            AppShortcut.ToggleActivityView, AppShortcut.ShowMainWindow, AppShortcut.OpenSettings,
            // PORT-ONLY (web W3b): the pane workspace's commands follow the source's own.
            AppShortcut.ToggleBrowser, AppShortcut.ToggleSideChat, AppShortcut.ToggleArtifacts, AppShortcut.ToggleBackgroundTasks,
            AppShortcut.ClosePane, AppShortcut.ExpandPane, AppShortcut.NextPane, AppShortcut.PreviousPane,
            AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater, AppShortcut.JoinPaneTabs,
        ]; // :161-162, declaration order
        Assert.Equal(expectedThreads, AppShortcutSection.Threads.Shortcuts());
        Assert.Equal(expectedWindow, AppShortcutSection.Window.Shortcuts());
        foreach (var shortcut in expectedThreads) Assert.Equal(AppShortcutSection.Threads, shortcut.Section());
        foreach (var shortcut in expectedWindow) Assert.Equal(AppShortcutSection.Window, shortcut.Section());
    }

    [Fact]
    [Trait("Req", "PR-WIN-123")] // Core/Models/Shortcuts.swift:239, :245-249, :259-274 (ED-099 (5))
    public void Only_changed_chords_are_stored_under_keyboardShortcuts_as_the_swift_json_shape_with_three_states()
    {
        using var runtime = new TestRuntime();

        // Absent: nothing stored at all, so the default applies and loading writes nothing.
        var store = Load(runtime);
        OnMain(runtime, store, () => Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal)));
        Assert.Empty(runtime.Defaults.Writes);

        // Present with a chord: the whole dictionary is re-encoded and written, under the exact key
        // "keyboardShortcuts" (:249), keyed by the command's raw value, as {"<raw>":{"chord":{"keyCode":...,"modifiers":...}}}.
        OnMain(runtime, store, () => store.Set(new KeyChord(0x24, Ctrl | Shift), AppShortcut.ToggleTerminal));
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes);
        AssertStoredChord(runtime, "toggleTerminal", 0x24, 6);
        OnMain(runtime, store, () => Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal)));

        // Read back from what was actually persisted, not just from the in-memory store (R-TEST-4): a fresh store
        // loaded from the same UserDefaults instance decodes the chord written above, proving the "present with a
        // chord" state round-trips, not merely that Set updated memory.
        var reloaded = Load(runtime);
        OnMain(runtime, reloaded, () =>
        {
            Assert.True(reloaded.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(new KeyChord(0x24, Ctrl | Shift), reloaded.Chord(AppShortcut.ToggleTerminal));
        });

        // Present with a null chord: deliberately cleared, answers to no chord at all, distinct from absent.
        OnMain(runtime, store, () => store.Set(null, AppShortcut.ToggleTerminal));
        Assert.Equal(Utf8("{\"toggleTerminal\":{}}"), Stored(runtime));
        OnMain(runtime, store, () =>
        {
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
        });

        // Read back the cleared state too: loading a fresh store from the persisted {"chord":null} shape must
        // also read as customised-but-cleared, not as absent.
        var reloadedCleared = Load(runtime);
        OnMain(runtime, reloadedCleared, () =>
        {
            Assert.True(reloadedCleared.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Null(reloadedCleared.Chord(AppShortcut.ToggleTerminal));
        });

        // Stored data that does not decode falls back to empty: every customisation is lost silently and every
        // command reads at its default (:269-273 the decode failing, or absent, both yield [:]).
        Seed(runtime, "not valid json");
        var fallback = Load(runtime);
        OnMain(runtime, fallback, () =>
        {
            foreach (var row in Table)
            {
                Assert.False(fallback.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), fallback.Chord(row.Shortcut));
            }
        });
        Assert.Empty(runtime.Defaults.Writes); // loading never writes (:267-274)
    }

    [Fact]
    [Trait("Req", "PR-WIN-123")] // Core/Models/Shortcuts.swift:269
    public void A_blob_with_one_decodable_entry_and_one_that_is_not_falls_back_to_empty_in_full()
    {
        using var runtime = new TestRuntime();

        // A blob with one entry that decodes and one that does not still falls back to empty in full: the decode
        // is one `JSONDecoder().decode([String: Override].self, from: data)` for the WHOLE dictionary (:269), never
        // per entry, so the "valid" entry (toggleTerminal) is lost too, not merely the invalid one (newThread,
        // whose modifiers is -1, not a UInt).
        Seed(runtime,
            "{\"toggleTerminal\":{\"chord\":{\"keyCode\":36,\"modifiers\":2}},\"newThread\":{\"chord\":{\"keyCode\":49,\"modifiers\":-1}}}");
        var mixed = Load(runtime);
        OnMain(runtime, mixed, () =>
        {
            Assert.False(mixed.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(AppShortcut.ToggleTerminal.DefaultChord(), mixed.Chord(AppShortcut.ToggleTerminal));
            Assert.False(mixed.IsCustomized(AppShortcut.NewThread));
            Assert.Equal(AppShortcut.NewThread.DefaultChord(), mixed.Chord(AppShortcut.NewThread));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-123")] // Core/Models/Shortcuts.swift:246, :269
    public void A_stored_chord_of_null_reads_as_cleared_not_absent()
    {
        using var runtime = new TestRuntime();

        // The record's own title literally names the "chord": null form: it reads as cleared, not absent
        // (:246 `var chord: KeyChord?`, whose synthesized Decodable is decodeIfPresent, so null gives nil).
        Seed(runtime, "{\"toggleTerminal\":{\"chord\":null}}");
        var cleared = Load(runtime);
        OnMain(runtime, cleared, () =>
        {
            Assert.True(cleared.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Null(cleared.Chord(AppShortcut.ToggleTerminal));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-123")] // Core/Models/Shortcuts.swift:259-263
    public void Two_separate_mutations_each_write_once_and_leave_both_entries_stored()
    {
        using var runtime = new TestRuntime();

        // "Rewritten in full on every change" (:239, :261-263 the didSet re-encoding the whole `overrides`): two
        // separate mutations each write once, and the stored bytes end up holding BOTH entries in one of the two
        // Swift-correct dictionary orders (R-CODE-4 PlainEncoder has no fixed key order).
        var store = Load(runtime);
        OnMain(runtime, store, () => store.Set(null, AppShortcut.ToggleTerminal));
        Assert.Single(runtime.Defaults.Writes);
        OnMain(runtime, store, () => store.Set(null, AppShortcut.NewThread));
        Assert.Equal(2, runtime.Defaults.Writes.Count);
        byte[][] orders =
        [
            Utf8("{\"toggleTerminal\":{},\"newThread\":{}}"),
            Utf8("{\"newThread\":{},\"toggleTerminal\":{}}"),
        ];
        var finalStored = Stored(runtime);
        Assert.Contains(orders, o => o.AsSpan().SequenceEqual(finalStored));
    }

    [Fact]
    [Trait("Req", "PR-WIN-124")] // Core/Models/Shortcuts.swift:276-283 (ED-099 (6))
    public void A_commands_chord_is_its_override_when_one_exists_including_a_null_one_otherwise_its_default()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // No override: the default (:278).
            Assert.Equal(new KeyChord(0x24, Ctrl), store.Chord(AppShortcut.ToggleTerminal));
            // :81 characters[38]?.lowercased().first == "j" (the literal SwiftUI spelling, ED-099 (6)), never the
            // port-side factory KeyEquivalent.Character, so the test does not depend on that factory existing.
            Assert.Equal(new KeyboardShortcut(new KeyEquivalent("j"), Ctrl), store.KeyboardShortcut(AppShortcut.ToggleTerminal));

            // A null override wins outright: no chord and no menu shortcut, even though a default exists (:277).
            store.Set(null, AppShortcut.ToggleTerminal);
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
            Assert.Null(store.KeyboardShortcut(AppShortcut.ToggleTerminal));

            // A real override wins outright too, and every caller reads it live, so a remap corrects every label
            // at once (:279, :285-290).
            var remapped = new KeyChord(0x24, Ctrl | Alt);
            store.Set(remapped, AppShortcut.ToggleTerminal);
            Assert.Equal(remapped, store.Chord(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Ctrl+Alt+J"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-125")] // Core/Models/Shortcuts.swift:300-302
    public void Recording_a_commands_own_default_clears_its_override_instead_of_storing_it()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // A remap away from the default is stored.
            store.Set(new KeyChord(0x24, Ctrl | Shift), AppShortcut.ToggleTerminal);
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));

            // Setting it back to exactly the default removes the entry entirely (:301), built fresh so equality is
            // by value, not by reference.
            store.Set(new KeyChord(0x24, Ctrl), AppShortcut.ToggleTerminal);
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(new KeyChord(0x24, Ctrl), store.Chord(AppShortcut.ToggleTerminal));

            // Clearing a command whose own default is null (ShowMainWindow, OD-0012 G6) also removes any entry: a
            // recorded null equals the null default, so it is not stored as a customisation either.
            store.Set(null, AppShortcut.ShowMainWindow);
            Assert.False(store.IsCustomized(AppShortcut.ShowMainWindow));
        });
        Assert.Equal(Utf8("{}"), Stored(runtime));
    }

    [Fact]
    [Trait("Req", "PR-WIN-126")] // Core/Models/Shortcuts.swift:304-318
    public void A_command_or_a_whole_section_can_be_reset_and_customisation_can_be_queried_either_way()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
            Assert.False(store.HasCustomizations(AppShortcutSection.Window));

            // reset(_: AppShortcut) removes just that command's entry (:304-306).
            store.Set(new KeyChord(0x24, Ctrl | Alt), AppShortcut.ToggleTerminal);
            store.Set(new KeyChord(0x30, Ctrl | Alt), AppShortcut.ToggleSidebar);
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            store.Reset(AppShortcut.ToggleTerminal);
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.True(store.IsCustomized(AppShortcut.ToggleSidebar)); // untouched

            // reset(_: AppShortcutSection) removes every entry in THAT section only (:308-310 `for shortcut in
            // section.shortcuts`): a Threads command's customisation must survive a Window reset.
            store.Set(new KeyChord(0x31, Ctrl | Shift), AppShortcut.NewThread); // Threads command
            Assert.True(store.IsCustomized(AppShortcut.ToggleSidebar));
            Assert.True(store.HasCustomizations(AppShortcutSection.Window));
            var writesBeforeReset = runtime.Defaults.Writes.Count;
            store.Reset(AppShortcutSection.Window);
            Assert.False(store.IsCustomized(AppShortcut.ToggleSidebar));
            Assert.False(store.HasCustomizations(AppShortcutSection.Window));
            Assert.True(store.IsCustomized(AppShortcut.NewThread)); // untouched by the Window reset
            Assert.True(store.HasCustomizations(AppShortcutSection.Threads));
            // One write per command in the reset section (:309 fires the didSet on every assignment): the Window
            // section holds exactly 7 commands (:161-162) and the pane workspace's 11 (web W3b), so the reset grows
            // Writes by exactly 18, not one for the whole reset (no recorded ruling authorizes coalescing).
            Assert.Equal(7 + 11, AppShortcutSection.Window.Shortcuts().Count);
            Assert.Equal(writesBeforeReset + 7 + 11, runtime.Defaults.Writes.Count);
            store.Reset(AppShortcut.NewThread); // leave the store clean for the assertions below

            // A cleared (null) override still counts as customised (:313 `!= nil`).
            store.Set(null, AppShortcut.ToggleTerminal);
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.True(store.HasCustomizations(AppShortcutSection.Window));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-127")] // Core/Models/Shortcuts.swift:285-298 (OD-0008 B7)
    public void A_commands_chord_reads_into_help_text_as_a_parenthesised_suffix_and_as_nothing_once_cleared()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // The source's own worked example, rendered in the port's Windows notation (OD-0008 B7).
            Assert.Equal(Utf8("Ctrl+J"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));
            Assert.Equal(Utf8(" (Ctrl+J)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleTerminal)));
            Assert.Equal(Utf8("Hide terminal (Ctrl+J)"), Utf8("Hide terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal)));

            // Cleared: label is nil, and the hint collapses entirely, including its leading space.
            store.Set(null, AppShortcut.ToggleTerminal);
            Assert.Null(ShortcutStore.Label(AppShortcut.ToggleTerminal));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Hide terminal"), Utf8("Hide terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal)));

            // A command with no default and no override (ShowMainWindow) reads the same way: no chord ever shown.
            Assert.Null(ShortcutStore.Label(AppShortcut.ShowMainWindow));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ShowMainWindow));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-128")] // Core/Models/Shortcuts.swift:321-322
    public void Re_recording_a_commands_own_current_chord_is_accepted_before_any_of_the_five_checks_run()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // At the default, the default is always accepted for itself.
            Assert.Null(store.Refusal(new KeyChord(0x24, Ctrl), AppShortcut.ToggleTerminal));

            // Even a chord that would otherwise fail the modifier-key check, the typing check, the menu check or
            // the reserved check is accepted when it is already the command's own current chord.
            var bareShift = new KeyChord(0x2A, ChordModifiers.None); // a modifier key on its own
            store.Set(bareShift, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(bareShift, AppShortcut.ToggleSidebar));

            var reserved = new KeyChord(0x2E, Ctrl); // Ctrl+C, reserved
            store.Set(reserved, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(reserved, AppShortcut.ToggleSidebar));

            // The typing check (:325-327) is bypassed too: a bare typing key already held by the command.
            var typing = new KeyChord(0x31, ChordModifiers.None); // bare N, would otherwise fail the typing check
            store.Set(typing, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(typing, AppShortcut.ToggleSidebar));

            // The menu check (:328) is bypassed too: a key with no menu equivalent already held by the command.
            var noMenu = new KeyChord(0x37, Ctrl); // numpad *, chord.keyboardShortcut is nil
            store.Set(noMenu, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(noMenu, AppShortcut.ToggleSidebar));

            // The conflict check (:330-333) is bypassed too: without the early return, Ctrl+K would name
            // CommandPalette (its default holder), but it is being re-recorded for the command that now holds it.
            var ctrlK = new KeyChord(0x25, Ctrl); // Ctrl+K, CommandPalette's default
            store.Set(ctrlK, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(ctrlK, AppShortcut.ToggleSidebar));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-129")] // Core/Models/Shortcuts.swift:324
    public void Recording_only_modifier_keys_is_refused_with_Add_a_key_to_the_modifiers()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        Assert.Equal("Add a key to the modifiers.", ModifierRefusal); // the exact string, trailing full stop included
        OnMain(runtime, store, () =>
        {
            foreach (var code in ModifierKeyCodes)
                Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, ChordModifiers.None), AppShortcut.NewThread)!));

            // :324 `if KeyNames.isModifierKey(chord.keyCode) { return "Add a key to the modifiers." }` tests the
            // key CODE alone, whatever flags are held: a real recording of Ctrl or Shift arrives with its own
            // modifier bit set (LCtrl 0x1D with Control, LShift 0x2A with Shift), not with ChordModifiers.None.
            foreach (var code in ModifierKeyCodes)
            {
                Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, Ctrl), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(code, Ctrl | Alt | Shift), AppShortcut.NewThread)!));
            }

            // :322-324: the modifier-key check is FIRST, so it wins even over the fifth and last check (:330-333),
            // the conflict check, when another command already holds a modifier-key chord as its own current chord.
            var lShift = new KeyChord(0x2A, Ctrl);
            store.Set(lShift, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(lShift, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-130")] // Core/Models/Shortcuts.swift:325-327 (OD-0008 B7)
    public void Recording_a_bare_or_shift_only_typing_key_is_refused_with_the_Windows_wording()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        Assert.Equal("Add Ctrl or Alt so typing still works.", TypingRefusal);
        OnMain(runtime, store, () =>
        {
            foreach (var code in CharacterCodes.Concat(CaretCodes))
            {
                Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(code, ChordModifiers.None), AppShortcut.NewThread)!));
                Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(new KeyChord(code, Shift), AppShortcut.NewThread)!));
            }
            // With Ctrl or Alt the same keys are accepted (the qualifying modifiers under ED-099 (3)). Ctrl+Y
            // (0x15) is used, not Ctrl+N: Ctrl+N is NewThread's own default (:216), so a Refusal(Ctrl+N, NewThread)
            // call would return null at the early return (:322 `if chord == self.chord(for: shortcut) { return
            // nil }`) before the typing check at :325 ever runs, proving nothing about the typing check itself.
            // Ctrl+Y is not any command's default (Table above) and not reserved (Reserved above), so this
            // Refusal call can only pass through the typing check.
            Assert.Null(store.Refusal(new KeyChord(0x15, Ctrl), AppShortcut.NewThread)); // Ctrl+Y
            Assert.Null(store.Refusal(new KeyChord(0x31, Alt), AppShortcut.NewThread));
            Assert.Null(store.Refusal(new KeyChord(0x39, Alt | Shift), AppShortcut.NewThread));
            // Ctrl+Shift on a typing key also qualifies: flags.subtracting(.shift) leaves Control, not empty.
            Assert.Null(store.Refusal(new KeyChord(0x39, Ctrl | Shift), AppShortcut.NewThread)); // Ctrl+Shift+Space

            // The typing check (the second check) wins over the conflict check (the fifth, :330-333): with another
            // command already holding the bare chord, the typing refusal still comes back, not the conflict message.
            var bareN = new KeyChord(0x31, ChordModifiers.None);
            store.Set(bareN, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8(TypingRefusal), Utf8(store.Refusal(bareN, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-131")] // Core/Models/Shortcuts.swift:328
    public void Recording_a_key_with_no_menu_equivalent_is_refused_with_Menus_cannot_use_this_key()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        Assert.Equal("Menus cannot use this key.", MenuRefusal);
        OnMain(runtime, store, () =>
        {
            foreach (var code in new long[] { 0x37, 0x46 }) // numpad * and Scroll Lock: outside all three tables
                Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(new KeyChord(code, Ctrl), AppShortcut.NewThread)!));
            // The modifier-key check runs first, so its refusal wins over this one on a modifier key.
            Assert.Equal(Utf8(ModifierRefusal), Utf8(store.Refusal(new KeyChord(0x2A, ChordModifiers.None), AppShortcut.NewThread)!));

            // The menu check (the third check) wins over the conflict check (the fifth, :330-333): with another
            // command already holding a chord that has no menu equivalent, the menu refusal still comes back, not
            // the conflict message.
            var noMenu = new KeyChord(0x37, Ctrl);
            store.Set(noMenu, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8(MenuRefusal), Utf8(store.Refusal(noMenu, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-132")] // Core/Models/Shortcuts.swift:251-257, :329 (OD-0008 B8)
    public void Eighteen_reserved_chords_are_refused_with_the_Windows_wording()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        Assert.Equal("Windows or nth already uses this chord.", ReservedRefusal);
        Assert.Equal(18, Reserved.Length);
        OnMain(runtime, store, () =>
        {
            // Ctrl with Q, H, M, C, V, X, A, Z; Ctrl+Shift+Z; Ctrl with the nine digit positions 1 to 9 in order.
            Assert.Equal(new[] { "Ctrl+Q", "Ctrl+H", "Ctrl+M", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+A", "Ctrl+Z", "Ctrl+Shift+Z",
                "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5", "Ctrl+6", "Ctrl+7", "Ctrl+8", "Ctrl+9" },
                Reserved.Select(r => r.Rendered));
            // Each fixture row's Rendered string is checked against the port's own Description (:32-34, PR-WIN-117),
            // not against itself, so this exercises Description over the reserved table's codes and modifiers.
            foreach (var (code, mods, rendered) in Reserved)
                Assert.Equal(Utf8(rendered), Utf8(new KeyChord(code, mods).Description));

            foreach (var (code, mods, _) in Reserved)
                Assert.Equal(Utf8(ReservedRefusal), Utf8(store.Refusal(new KeyChord(code, mods), AppShortcut.NewThread)!));

            // Near misses: chords that resemble a reserved entry but are not one of the exact 18 (:329 `contains`
            // is exact-chord equality) are free. Ctrl+Y (0x15) proves ED-099 (4) withdraws ED-050 (b)'s Ctrl+Y
            // variant of the reserved list.
            (long Code, ChordModifiers Mods)[] free =
            [
                (0x15, Ctrl), // Ctrl+Y
                (0x0B, Ctrl), // Ctrl+0
                (0x10, Ctrl | Shift), // Ctrl+Shift+Q
                (0x02, Ctrl | Shift), // Ctrl+Shift+1
                (0x2C, Ctrl | Alt), // Ctrl+Alt+Z
                (0x10, Alt), // Alt+Q
            ];
            foreach (var (code, mods) in free)
                Assert.Null(store.Refusal(new KeyChord(code, mods), AppShortcut.NewThread));

            // The reserved check (the fourth check) wins over the conflict check (the fifth, :330-333): with
            // another command already holding a reserved chord, the reserved refusal still comes back, not the
            // conflict message.
            var ctrlC = new KeyChord(0x2E, Ctrl);
            store.Set(ctrlC, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8(ReservedRefusal), Utf8(store.Refusal(ctrlC, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-133")] // Core/Models/Shortcuts.swift:330-333
    public void A_chord_another_command_currently_holds_is_refused_with_that_commands_own_title_naming_the_first_holder()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var chord = new KeyChord(0x25, Ctrl); // Ctrl+K, CommandPalette's default
        OnMain(runtime, store, () =>
        {
            Assert.Equal(Utf8("Command palette already uses this chord."), Utf8(store.Refusal(chord, AppShortcut.NewThread)!));

            // Two holders: the earlier one in declaration order is named (NewThread before CommandPalette, :106, :122).
            store.Set(chord, AppShortcut.NewThread);
            Assert.Equal(Utf8("New thread already uses this chord."), Utf8(store.Refusal(chord, AppShortcut.OpenSettings)!));

            // The comparison is against live chords: once both holders are cleared, nothing blocks the chord.
            store.Set(null, AppShortcut.NewThread);
            store.Set(null, AppShortcut.CommandPalette);
            Assert.Null(store.Refusal(chord, AppShortcut.OpenSettings));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-134")] // Core/Models/Shortcuts.swift:261-262, :268 (dead_code.md entry 28)
    public void Shortcut_overrides_read_and_write_the_stores_key_directly_with_no_observable_capture_run_difference()
    {
        // CaptureRun is not ported (NOT_APPLICABLE_WITH_EVIDENCE, dead_code.md entry 28: CaptureRun.defaults is
        // nil outside a capture run, so every one of the nine redirected sites resolves to the standard store
        // anyway). There is therefore no automated, observable difference to assert beyond what PR-WIN-123 and
        // PR-SETTINGS-104 already prove: every read and write in this file goes straight to the one settings store
        // (runtime.Defaults / UserDefaults.Standard), which is what this record's windows_path calls for.
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () => store.Set(new KeyChord(0x24, Ctrl), AppShortcut.ToggleTerminal));
        Assert.Equal(new[] { Key }, runtime.Defaults.Writes); // the one store, the one key (:249)
    }

    [Fact]
    [Trait("Req", "PR-WIN-135")] // Core/Models/Shortcuts.swift:106, :159-160, :168, :192, :216 (ED-099)
    public void NewThread_titled_New_thread_stored_under_newThread_ships_Ctrl_N() =>
        AssertShortcut(AppShortcut.NewThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-136")] // Core/Models/Shortcuts.swift:107, :159-160, :169, :193, :217 (ED-099)
    public void NewWorktreeThread_titled_New_thread_in_worktree_stored_under_newWorktreeThread_ships_Ctrl_Shift_N() =>
        AssertShortcut(AppShortcut.NewWorktreeThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-137")] // Core/Models/Shortcuts.swift:108, :159-160, :170, :194, :218 (ED-099)
    public void AddProject_titled_Add_project_stored_under_addProject_ships_Ctrl_O() =>
        AssertShortcut(AppShortcut.AddProject, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-138")] // Core/Models/Shortcuts.swift:109, :159-160, :171, :195, :219 (ED-099)
    public void StopTurn_titled_Stop_the_running_turn_stored_under_stopTurn_ships_Ctrl_period() =>
        AssertShortcut(AppShortcut.StopTurn, AppShortcut.CommandPalette);

    // ------------------------------------------------------------------ part 3 of 3: PR-WIN-139 to PR-WIN-153, UI-TERM-034, UI-TERM-035.

    [Fact]
    [Trait("Req", "PR-WIN-139")] // Core/Models/Shortcuts.swift:110, :159-160, :172, :196, :220 (ED-099)
    public void QueueChat_titled_Queue_a_chat_stored_under_queueChat_ships_Ctrl_Return() =>
        AssertShortcut(AppShortcut.QueueChat, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-140")] // Core/Models/Shortcuts.swift:111, :159-160, :173, :197, :221 (ED-099)
    public void TogglePlanMode_titled_Plan_mode_stored_under_togglePlanMode_ships_Ctrl_Shift_P() =>
        AssertShortcut(AppShortcut.TogglePlanMode, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-141")] // Core/Models/Shortcuts.swift:112, :159-160, :174, :198, :222 (ED-099)
    public void PreviousThread_titled_Previous_thread_stored_under_previousThread_ships_Ctrl_Shift_Tab() =>
        AssertShortcut(AppShortcut.PreviousThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-142")] // Core/Models/Shortcuts.swift:113, :159-160, :175, :199, :223 (ED-099)
    public void NextThread_titled_Next_thread_stored_under_nextThread_ships_Ctrl_Tab() =>
        AssertShortcut(AppShortcut.NextThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-143")] // Core/Models/Shortcuts.swift:114, :159-160, :176, :200, :224 (ED-099)
    public void RunScript_titled_Run_top_script_stored_under_runScript_ships_Ctrl_R() =>
        AssertShortcut(AppShortcut.RunScript, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-144")] // Core/Models/Shortcuts.swift:115-117, :159-160, :177, :201, :225 (ED-099)
    public void FinishThread_titled_Finish_thread_stored_under_archiveThread_ships_Ctrl_Shift_Delete() =>
        AssertShortcut(AppShortcut.FinishThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-145")] // Core/Models/Shortcuts.swift:118-119, :159-160, :178, :202, :226 (ED-099)
    public void ArchiveThread_titled_Archive_thread_stored_under_archiveThreadPrompt_ships_Ctrl_W() =>
        AssertShortcut(AppShortcut.ArchiveThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-146")] // Core/Models/Shortcuts.swift:120-121, :159-160, :179, :203, :227 (ED-099)
    public void SettleThread_titled_Settle_thread_stored_under_settleThread_ships_Ctrl_S() =>
        AssertShortcut(AppShortcut.SettleThread, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-147")] // Core/Models/Shortcuts.swift:122, :161-162, :180, :204, :228 (ED-099)
    public void CommandPalette_titled_Command_palette_stored_under_commandPalette_ships_Ctrl_K() =>
        AssertShortcut(AppShortcut.CommandPalette, AppShortcut.ToggleSidebar);

    [Fact]
    [Trait("Req", "PR-WIN-148")] // Core/Models/Shortcuts.swift:123, :161-162, :181, :205, :229 (ED-099)
    public void ToggleSidebar_titled_Toggle_sidebar_stored_under_toggleSidebar_ships_Ctrl_B() =>
        AssertShortcut(AppShortcut.ToggleSidebar, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-149")] // Core/Models/Shortcuts.swift:124, :161-162, :182, :206, :230 (ED-099)
    public void ToggleTerminal_titled_Toggle_terminal_stored_under_toggleTerminal_ships_Ctrl_J() =>
        AssertShortcut(AppShortcut.ToggleTerminal, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-150")] // Core/Models/Shortcuts.swift:125, :161-162, :183, :207, :231 (ED-099)
    public void ToggleChanges_titled_Toggle_changes_stored_under_toggleChanges_ships_Ctrl_D() =>
        AssertShortcut(AppShortcut.ToggleChanges, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-151")] // Core/Models/Shortcuts.swift:126, :161-162, :184, :208, :232 (ED-099)
    public void ToggleActivityView_titled_Activity_view_stored_under_toggleActivityView_ships_Ctrl_Alt_U() =>
        AssertShortcut(AppShortcut.ToggleActivityView, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-152")] // Core/Models/Shortcuts.swift:127, :161-162, :185, :209, :233 (OD-0002, OD-0012 F1, OD-0012 G6)
    public void ShowMainWindow_titled_Show_Marrow_stored_under_showMainWindow_ships_with_no_chord() =>
        AssertShortcut(AppShortcut.ShowMainWindow, AppShortcut.CommandPalette);

    [Fact]
    [Trait("Req", "PR-WIN-153")] // Core/Models/Shortcuts.swift:128, :161-162, :186, :210, :234 (ED-099)
    public void OpenSettings_titled_Settings_stored_under_openSettings_ships_Ctrl_comma() =>
        AssertShortcut(AppShortcut.OpenSettings, AppShortcut.CommandPalette);

    // UI-TERM-034 and UI-TERM-035 describe a settings-page row; the row control itself belongs to a later UI
    // unit (none exists yet in this tree), so what is testable here at the model layer is the row's exact
    // text, its section placement, its default chord's rendering, and the "customized" and "cleared" states
    // ShortcutStore drives (:246, :277, :289, :296, :300-306, :312-314); the refusal rules those states rest
    // on are proven for every key by PR-WIN-118 to PR-WIN-133.

    [Fact]
    [Trait("Req", "UI-TERM-034")] // Core/Models/Shortcuts.swift:124, :161-162, :182, :206, :230 (OD-0008 B7, ED-099)
    public void Toggle_terminal_row_reads_its_title_and_detail_in_Window_and_panels_with_Ctrl_J_and_tracks_customized_and_cleared_states()
    {
        var row = Row(AppShortcut.ToggleTerminal);
        Assert.Equal(Utf8("Toggle terminal / Shows or hides the thread's terminal."), Utf8(row.Title + " / " + row.Detail));
        Assert.Equal(AppShortcutSection.Window, row.Section);
        Assert.Equal(Utf8("Window and panels"), Utf8(row.Section.Title()));

        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.False(store.IsCustomized(AppShortcut.ToggleTerminal)); // default, not yet customized (:312-314)
            Assert.Equal(Utf8("Ctrl+J"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));

            store.Set(new KeyChord(0x25, Ctrl), AppShortcut.ToggleTerminal); // customized (:300-302, :312-314)
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal));
            Assert.Equal(Utf8("Ctrl+K"), Utf8(ShortcutStore.Label(AppShortcut.ToggleTerminal)!));

            store.Set(null, AppShortcut.ToggleTerminal); // cleared: no chord, no label, an empty hint (:246, :277, :289, :296)
            Assert.Null(store.Chord(AppShortcut.ToggleTerminal));
            Assert.Null(ShortcutStore.Label(AppShortcut.ToggleTerminal));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleTerminal));
            Assert.True(store.IsCustomized(AppShortcut.ToggleTerminal)); // a stored empty chord still counts as customized
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-035")] // Core/Models/Shortcuts.swift:114, :159-160, :176, :200, :224 (OD-0008 B7, ED-099)
    public void Run_top_script_row_reads_its_title_and_detail_in_Threads_with_Ctrl_R_and_tracks_customized_and_cleared_states()
    {
        var row = Row(AppShortcut.RunScript);
        Assert.Equal(Utf8("Run top script / Runs the first project script in the selected thread's terminal."), Utf8(row.Title + " / " + row.Detail));
        Assert.Equal(AppShortcutSection.Threads, row.Section);
        Assert.Equal(Utf8("Threads"), Utf8(row.Section.Title()));

        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.False(store.IsCustomized(AppShortcut.RunScript)); // default, not yet customized (:312-314)
            Assert.Equal(Utf8("Ctrl+R"), Utf8(ShortcutStore.Label(AppShortcut.RunScript)!));

            store.Set(new KeyChord(0x25, Ctrl | Alt), AppShortcut.RunScript); // customized (:300-302, :312-314)
            Assert.True(store.IsCustomized(AppShortcut.RunScript));
            Assert.Equal(Utf8("Ctrl+Alt+K"), Utf8(ShortcutStore.Label(AppShortcut.RunScript)!));

            store.Set(null, AppShortcut.RunScript); // cleared: no chord, no label, an empty hint (:246, :277, :289, :296)
            Assert.Null(store.Chord(AppShortcut.RunScript));
            Assert.Null(ShortcutStore.Label(AppShortcut.RunScript));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.RunScript));
            Assert.True(store.IsCustomized(AppShortcut.RunScript)); // a stored empty chord still counts as customized
        });
    }
}
