using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Shortcuts.swift (B2 1fbf81e), unit Core.Models.Shortcuts,
// part 1 of 3. Every expected value is transcribed from the Swift lines cited on each test, or from the
// record's "deviation" field (OD-0008 B7 Windows notation, OD-0008 B8 reserved wording, OD-0002 and
// OD-0012 F1 product name, OD-0012 G6 no default chord for showMainWindow), never from the C# under test.
//
// The chord FORMAT on Windows (which key code space, which modifier bits, the persisted chord JSON) is not
// settled by any recorded decision, so no test here names a key code or a modifier bit: every chord is taken
// from a shipped default, recombined from two defaults' key code and modifier flags, or built with the empty
// modifier set (`default`). The refusal reasons that need any other chord (a bare modifier key, a key menus
// cannot hold, the 18 reserved chords) are reported as a STOP, not guessed.
//
// API shape assumed (R-NAME-1 to R-NAME-6, R-ENUM-1, R-LIFE-2): `enum AppShortcut` and `enum
// AppShortcutSection` (a C# enum cannot nest the Swift `AppShortcut.Section`) with [RawValue], their computed
// members as extension methods (`Id()`, `Title()`, `Detail()`, `Section()`, `DefaultChord()`, `Shortcuts()`),
// `KeyChord` with a `(keyCode, modifiers)` constructor and `KeyCode`, `ModifierFlags`, `Description`, and
// `ShortcutStore` with `Shared` as an Ambient<ShortcutStore>. The store's Swift `private init` is reached by
// reflection so the port can keep it private.
public sealed class ShortcutsTests
{
    private const string Key = "keyboardShortcuts"; // :249

    // AppShortcut in declaration order (:106-128), with raw value (:105-128, :117, :119), section (:157-164),
    // title (:166-188, :185 renamed by OD-0002 / OD-0012 F1), detail (:190-212) and the default chord as the
    // port renders it (record deviations, OD-0008 B7; showMainWindow has none, OD-0012 G6).
    private static readonly (AppShortcut Shortcut, string Raw, AppShortcutSection Section, string Title, string Detail, string? Rendered)[] Table =
    [
        (AppShortcut.NewThread, "newThread", AppShortcutSection.Threads, "New thread", "Starts a thread in the current project.", "Ctrl+N"),
        (AppShortcut.NewWorktreeThread, "newWorktreeThread", AppShortcutSection.Threads, "New thread in worktree", "Starts a thread in its own git worktree.", "Ctrl+Shift+N"),
        (AppShortcut.AddProject, "addProject", AppShortcutSection.Threads, "Add project", "Opens a folder as a project.", "Ctrl+O"),
        (AppShortcut.StopTurn, "stopTurn", AppShortcutSection.Threads, "Stop the running turn", "Interrupts the agent while it works.", "Ctrl+."),
        (AppShortcut.QueueChat, "queueChat", AppShortcutSection.Threads, "Queue a chat", "While a turn runs, lines the draft up behind it.", "Ctrl+Return"),
        (AppShortcut.TogglePlanMode, "togglePlanMode", AppShortcutSection.Threads, "Plan mode", "Plans before building.", "Ctrl+Shift+P"),
        (AppShortcut.PreviousThread, "previousThread", AppShortcutSection.Threads, "Previous thread", "Selects the thread above in the sidebar.", "Ctrl+Shift+Tab"),
        (AppShortcut.NextThread, "nextThread", AppShortcutSection.Threads, "Next thread", "Selects the thread below in the sidebar.", "Ctrl+Tab"),
        (AppShortcut.RunScript, "runScript", AppShortcutSection.Threads, "Run top script", "Runs the first project script in the selected thread's terminal.", "Ctrl+R"),
        (AppShortcut.FinishThread, "archiveThread", AppShortcutSection.Threads, "Finish thread", "Settles the selected thread, or archives it, whichever General chose. Reopens a settled one.", "Ctrl+Shift+Delete"),
        (AppShortcut.ArchiveThread, "archiveThreadPrompt", AppShortcutSection.Threads, "Archive thread", "Asks to archive the selected thread; Return archives it, and Delete is a click away.", "Ctrl+W"),
        (AppShortcut.SettleThread, "settleThread", AppShortcutSection.Threads, "Settle thread", "Settles the selected thread. Reopens one that is settled.", "Ctrl+S"),
        (AppShortcut.CommandPalette, "commandPalette", AppShortcutSection.Window, "Command palette", "Searches threads, projects and actions.", "Ctrl+K"),
        (AppShortcut.ToggleSidebar, "toggleSidebar", AppShortcutSection.Window, "Toggle sidebar", "Shows or hides the sidebar.", "Ctrl+B"),
        (AppShortcut.ToggleTerminal, "toggleTerminal", AppShortcutSection.Window, "Toggle terminal", "Shows or hides the thread's terminal.", "Ctrl+J"),
        (AppShortcut.ToggleChanges, "toggleChanges", AppShortcutSection.Window, "Toggle changes", "Shows or hides the changes panel.", "Ctrl+D"),
        (AppShortcut.ToggleActivityView, "toggleActivityView", AppShortcutSection.Window, "Activity view", "Lists every thread by when it was last active.", "Ctrl+Alt+U"),
        (AppShortcut.ShowMainWindow, "showMainWindow", AppShortcutSection.Window, "Show Marrow", "Brings the main window to the front.", null),
        (AppShortcut.OpenSettings, "openSettings", AppShortcutSection.Window, "Settings", "Opens this window.", "Ctrl+,"),
    ];

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

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

    // One row of the shortcut table and its store behaviour: the checks every per-shortcut record names.
    private static void AssertShortcut(AppShortcut shortcut, AppShortcut donor, string donorRendered)
    {
        using var runtime = new TestRuntime();
        var row = Array.Find(Table, r => r.Shortcut == shortcut);
        Assert.Equal(row.Raw, SwiftEnum<AppShortcut>.RawValue(shortcut));
        Assert.Equal(row.Raw, shortcut.Id()); // :155
        Assert.Equal(shortcut, SwiftEnum<AppShortcut>.FromRawValue(row.Raw));
        Assert.Equal(Utf8(row.Title), Utf8(shortcut.Title()));
        Assert.Equal(Utf8(row.Detail), Utf8(shortcut.Detail()));
        Assert.Equal(row.Section, shortcut.Section());
        Assert.Contains(shortcut, row.Section.Shortcuts());
        if (row.Rendered is null) Assert.Null(shortcut.DefaultChord());
        else Assert.Equal(Utf8(row.Rendered), Utf8(Default(shortcut).Description));

        var store = Load(runtime);
        var recorded = Default(donor);
        OnMain(runtime, store, () =>
        {
            // At its default: the shipped chord, and a hint that names it (:276-279, :288-298).
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));
            Assert.Equal(row.Rendered, ShortcutStore.Label(shortcut));
            Assert.Equal(row.Rendered is null ? "" : " (" + row.Rendered + ")", ShortcutStore.Hint(shortcut));

            // Re-recorded to a chord: the store answers with it (:300-302, :276-279).
            store.Set(recorded, shortcut);
            Assert.True(store.IsCustomized(shortcut));
            Assert.Equal(recorded, store.Chord(shortcut));
            Assert.Equal(Utf8(donorRendered), Utf8(ShortcutStore.Label(shortcut)!));

            // A chord equal to the default removes the override (:301); built fresh, so equality is by value.
            var sameAsDefault = row.Rendered is null ? null : new KeyChord(Default(shortcut).KeyCode, Default(shortcut).ModifierFlags);
            store.Set(sameAsDefault, shortcut);
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));

            // Cleared: no chord, no label, an empty hint (:246, :277, :289, :296).
            store.Set(null, shortcut);
            Assert.Null(store.Chord(shortcut));
            Assert.Null(ShortcutStore.Label(shortcut));
            Assert.Equal("", ShortcutStore.Hint(shortcut));
            Assert.Equal(row.Rendered is not null, store.IsCustomized(shortcut));

            store.Reset(shortcut); // :304-306
            Assert.False(store.IsCustomized(shortcut));
            Assert.Equal(shortcut.DefaultChord(), store.Chord(shortcut));
        });

        // The override is stored under the raw value: a stored cleared override for it is read back (:268-270, :277).
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
    }

    // ------------------------------------------------------------------ the table, per record

    [Fact]
    [Trait("Req", "PR-SETTINGS-193")] // Core/Models/Shortcuts.swift:106, :159-160, :168, :192, :216
    public void New_thread_sits_in_Threads_with_its_verbatim_title_and_detail_and_ships_Ctrl_N() =>
        AssertShortcut(AppShortcut.NewThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-194")] // Core/Models/Shortcuts.swift:107, :159-160, :169, :193, :217
    public void New_thread_in_worktree_sits_in_Threads_and_ships_Ctrl_Shift_N() =>
        AssertShortcut(AppShortcut.NewWorktreeThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-195")] // Core/Models/Shortcuts.swift:108, :159-160, :170, :194, :218
    public void Add_project_sits_in_Threads_and_ships_Ctrl_O() =>
        AssertShortcut(AppShortcut.AddProject, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-196")] // Core/Models/Shortcuts.swift:109, :159-160, :171, :195, :219
    public void Stop_the_running_turn_sits_in_Threads_and_ships_Ctrl_period() =>
        AssertShortcut(AppShortcut.StopTurn, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-197")] // Core/Models/Shortcuts.swift:110, :159-160, :172, :196, :220
    public void Queue_a_chat_sits_in_Threads_and_ships_Ctrl_Return() =>
        AssertShortcut(AppShortcut.QueueChat, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-198")] // Core/Models/Shortcuts.swift:111, :159-160, :173, :197, :221
    public void Plan_mode_sits_in_Threads_and_ships_Ctrl_Shift_P() =>
        AssertShortcut(AppShortcut.TogglePlanMode, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-199")] // Core/Models/Shortcuts.swift:112, :159-160, :174, :198, :222
    public void Previous_thread_sits_in_Threads_and_ships_Ctrl_Shift_Tab() =>
        AssertShortcut(AppShortcut.PreviousThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-200")] // Core/Models/Shortcuts.swift:113, :159-160, :175, :199, :223
    public void Next_thread_sits_in_Threads_and_ships_Ctrl_Tab() =>
        AssertShortcut(AppShortcut.NextThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-201")] // Core/Models/Shortcuts.swift:114, :159-160, :176, :200, :224
    public void Run_top_script_sits_in_Threads_and_ships_Ctrl_R() =>
        AssertShortcut(AppShortcut.RunScript, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-202")] // Core/Models/Shortcuts.swift:115-117, :159-160, :177, :201, :225
    public void Finish_thread_is_stored_as_archiveThread_sits_in_Threads_and_ships_Ctrl_Shift_Delete() =>
        AssertShortcut(AppShortcut.FinishThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-203")] // Core/Models/Shortcuts.swift:118-119, :159-160, :178, :202, :226
    public void Archive_thread_is_stored_as_archiveThreadPrompt_sits_in_Threads_and_ships_Ctrl_W() =>
        AssertShortcut(AppShortcut.ArchiveThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-202")] // Core/Models/Shortcuts.swift:115-119, :268-270, :277
    [Trait("Req", "PR-SETTINGS-203")] // Core/Models/Shortcuts.swift:115-119, :268-270, :277
    public void A_chord_stored_under_the_old_archiveThread_name_applies_to_Finish_thread_not_Archive_thread()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"archiveThread\":{}}");
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.True(store.IsCustomized(AppShortcut.FinishThread));
            Assert.Null(store.Chord(AppShortcut.FinishThread));
            Assert.False(store.IsCustomized(AppShortcut.ArchiveThread));
            Assert.Equal(Utf8("Ctrl+W"), Utf8(ShortcutStore.Label(AppShortcut.ArchiveThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-204")] // Core/Models/Shortcuts.swift:120-121, :159-160, :179, :203, :227
    public void Settle_thread_sits_in_Threads_and_ships_Ctrl_S() =>
        AssertShortcut(AppShortcut.SettleThread, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-205")] // Core/Models/Shortcuts.swift:122, :161-162, :180, :204, :228
    public void Command_palette_sits_in_Window_and_panels_and_ships_Ctrl_K() =>
        AssertShortcut(AppShortcut.CommandPalette, AppShortcut.ToggleSidebar, "Ctrl+B");

    [Fact]
    [Trait("Req", "PR-SETTINGS-206")] // Core/Models/Shortcuts.swift:123, :161-162, :181, :205, :229
    public void Toggle_sidebar_sits_in_Window_and_panels_and_ships_Ctrl_B() =>
        AssertShortcut(AppShortcut.ToggleSidebar, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-207")] // Core/Models/Shortcuts.swift:124, :161-162, :182, :206, :230
    public void Toggle_terminal_sits_in_Window_and_panels_and_ships_Ctrl_J() =>
        AssertShortcut(AppShortcut.ToggleTerminal, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-208")] // Core/Models/Shortcuts.swift:125, :161-162, :183, :207, :231
    public void Toggle_changes_sits_in_Window_and_panels_and_ships_Ctrl_D() =>
        AssertShortcut(AppShortcut.ToggleChanges, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-209")] // Core/Models/Shortcuts.swift:126, :161-162, :184, :208, :232
    public void Activity_view_sits_in_Window_and_panels_and_ships_Ctrl_Alt_U() =>
        AssertShortcut(AppShortcut.ToggleActivityView, AppShortcut.CommandPalette, "Ctrl+K");

    [Fact]
    [Trait("Req", "PR-SETTINGS-210")] // Core/Models/Shortcuts.swift:127, :161-162, :185, :209, :233 (OD-0002, OD-0012 F1, OD-0012 G6)
    public void Show_Marrow_sits_in_Window_and_panels_and_ships_with_no_chord() =>
        AssertShortcut(AppShortcut.ShowMainWindow, AppShortcut.CommandPalette, "Ctrl+K");

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
    [Trait("Req", "PR-SETTINGS-211")] // Core/Models/Shortcuts.swift:128, :161-162, :186, :210, :234
    public void Settings_sits_in_Window_and_panels_and_ships_Ctrl_comma() =>
        AssertShortcut(AppShortcut.OpenSettings, AppShortcut.CommandPalette, "Ctrl+K");

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
    [Trait("Req", "PR-SETTINGS-193")] // Core/Models/Shortcuts.swift:32-34, :43-50 (OD-0008 B7)
    [Trait("Req", "PR-SETTINGS-209")] // Core/Models/Shortcuts.swift:32-34, :232 (OD-0008 B7)
    public void A_chord_reads_its_modifiers_in_Windows_notation_before_the_US_position_key_label()
    {
        var n = Default(AppShortcut.NewThread);
        var ctrlAlt = Default(AppShortcut.ToggleActivityView).ModifierFlags;
        var ctrlShift = Default(AppShortcut.NewWorktreeThread).ModifierFlags;
        Assert.Equal(Utf8("Ctrl+Alt+N"), Utf8(new KeyChord(n.KeyCode, ctrlAlt).Description));
        Assert.Equal(Utf8("Ctrl+Shift+N"), Utf8(new KeyChord(n.KeyCode, ctrlShift).Description));
        Assert.Equal(Utf8("Ctrl+Alt+Tab"), Utf8(new KeyChord(Default(AppShortcut.NextThread).KeyCode, ctrlAlt).Description));
        // No modifiers: the key label alone, with no separator (:33, and "" from :44-49).
        Assert.Equal(Utf8("N"), Utf8(new KeyChord(n.KeyCode, default).Description));
        Assert.Equal(Utf8("Delete"), Utf8(new KeyChord(Default(AppShortcut.FinishThread).KeyCode, default).Description)); // :61 code 51
        Assert.Equal(Utf8("Return"), Utf8(new KeyChord(Default(AppShortcut.QueueChat).KeyCode, default).Description)); // :61 code 36
    }

    // ------------------------------------------------------------------ PR-GIT-141, PR-TERM-068

    [Fact]
    [Trait("Req", "PR-GIT-141")] // Core/Models/Shortcuts.swift:125, :183, :207, :231, :288-298
    public void Toggle_changes_ships_Ctrl_D_and_its_hint_follows_the_current_binding()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Utf8("Toggle changes"), Utf8(AppShortcut.ToggleChanges.Title()));
        Assert.Equal(Utf8("Shows or hides the changes panel."), Utf8(AppShortcut.ToggleChanges.Detail()));
        Assert.Equal("toggleChanges", SwiftEnum<AppShortcut>.RawValue(AppShortcut.ToggleChanges));
        Assert.Equal(Utf8("Ctrl+D"), Utf8(Default(AppShortcut.ToggleChanges).Description));

        var store = Load(runtime);
        var rebound = new KeyChord(Default(AppShortcut.ToggleChanges).KeyCode, Default(AppShortcut.ToggleActivityView).ModifierFlags);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(Utf8(" (Ctrl+D)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleChanges)));
            store.Set(rebound, AppShortcut.ToggleChanges);
            Assert.Equal(Utf8(" (Ctrl+Alt+D)"), Utf8(ShortcutStore.Hint(AppShortcut.ToggleChanges)));
            store.Set(null, AppShortcut.ToggleChanges);
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleChanges));
        });

        var relaunched = Load(runtime); // the rebinding persists with the store (:259-274)
        OnMain(runtime, relaunched, () => Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleChanges)));
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
        Assert.Equal(Utf8("Ctrl+R"), Utf8(Default(AppShortcut.RunScript).Description));

        var store = Load(runtime);
        var remapped = new KeyChord(Default(AppShortcut.RunScript).KeyCode, Default(AppShortcut.NewWorktreeThread).ModifierFlags);
        OnMain(runtime, store, () =>
        {
            Assert.Equal("Ctrl+R", ShortcutStore.Label(AppShortcut.RunScript));
            store.Set(remapped, AppShortcut.RunScript);
            Assert.True(store.IsCustomized(AppShortcut.RunScript));
            Assert.Equal(Utf8("Ctrl+Shift+R"), Utf8(ShortcutStore.Label(AppShortcut.RunScript)!));
            Assert.Equal(Utf8(" (Ctrl+Shift+R)"), Utf8(ShortcutStore.Hint(AppShortcut.RunScript)));
        });
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
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:259-265, :300-302
    public void Recording_the_default_removes_the_override_and_rewrites_the_whole_dictionary()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.ToggleTerminal);
            store.Set(null, AppShortcut.NewThread);
            store.Set(Default(AppShortcut.NewThread), AppShortcut.NewThread);
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
        var recorded = Default(AppShortcut.CommandPalette);
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

    [Theory]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:268-273 (a [String: Override] decode fails as a whole)
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"toggleTerminal\":[]}")]
    [InlineData("{\"toggleTerminal\":null}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":5}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":5}}")] // a chord is an object (ED-050 (c))
    public void Stored_data_that_fails_to_decode_starts_empty_and_the_next_change_overwrites_it(string stored)
    {
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

    [Theory]
    [Trait("Req", "PR-SETTINGS-104")] // Core/Models/Shortcuts.swift:5-14, :268-273; port_impacts.md:228 entry 110, :246 entry 119
    [InlineData("{\"newThread\":{\"chord\":{\"keyCode\":45,\"modifiers\":1048576}}}")]
    [InlineData("{\"toggleTerminal\":{},\"newThread\":{\"chord\":{\"keyCode\":45,\"modifiers\":1179648}}}")]
    [InlineData("{\"toggleChanges\":{\"chord\":{\"keyCode\":2,\"modifiers\":1572864}}}")]
    public void A_blob_written_by_the_macOS_app_is_discarded_rather_than_misread(string macBlob)
    {
        // keyCode 45 is N, 2 is D (Shortcuts.swift:53-56); 1048576 is NSEvent.ModifierFlags.command,
        // 1179648 is shift and command, 1572864 is option and command: the raw bits :13 stores.
        using var runtime = new TestRuntime();
        Seed(runtime, macBlob);
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table)
            {
                Assert.False(store.IsCustomized(row.Shortcut));
                Assert.Equal(row.Shortcut.DefaultChord(), store.Chord(row.Shortcut));
            }
        });
    }

    // ------------------------------------------------------------------ PR-SETTINGS-223 resolution

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:276-279, :312-314
    public void An_override_wins_outright_absent_means_default_and_a_stored_nil_means_cleared()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var recorded = Default(AppShortcut.ToggleSidebar);
        OnMain(runtime, store, () =>
        {
            Assert.Equal(Default(AppShortcut.ToggleTerminal), store.Chord(AppShortcut.ToggleTerminal));
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
            Assert.Null(ShortcutStore.Label(AppShortcut.ToggleTerminal));
            Assert.Equal("", ShortcutStore.Hint(AppShortcut.ToggleTerminal));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:312-318
    public void A_command_is_customised_by_the_presence_of_an_override_even_when_it_resolves_to_its_default()
    {
        using var runtime = new TestRuntime();
        Seed(runtime, "{\"showMainWindow\":{}}"); // nil, which is also showMainWindow's default (OD-0012 G6)
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            Assert.Null(store.Chord(AppShortcut.ShowMainWindow));
            Assert.Null(AppShortcut.ShowMainWindow.DefaultChord());
            Assert.True(store.IsCustomized(AppShortcut.ShowMainWindow));
            Assert.True(store.HasCustomizations(AppShortcutSection.Window));
            Assert.False(store.HasCustomizations(AppShortcutSection.Threads));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-223")] // Core/Models/Shortcuts.swift:285-298
    public void Label_and_hint_read_through_the_shared_store_and_vanish_when_the_chord_is_cleared()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var remapped = new KeyChord(Default(AppShortcut.ToggleTerminal).KeyCode, Default(AppShortcut.ToggleActivityView).ModifierFlags);
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

    // ------------------------------------------------------------------ PR-SETTINGS-219 refusals (partial)

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:322
    public void Re_recording_a_shortcuts_own_chord_is_never_refused()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var bareN = new KeyChord(Default(AppShortcut.NewThread).KeyCode, default);
        OnMain(runtime, store, () =>
        {
            foreach (var row in Table.Where(r => r.Rendered is not null))
                Assert.Null(store.Refusal(store.Chord(row.Shortcut)!, row.Shortcut));

            // The self check runs first: even a chord the typing check would refuse is allowed for its holder.
            store.Set(bareN, AppShortcut.ToggleSidebar);
            Assert.Null(store.Refusal(bareN, AppShortcut.ToggleSidebar));
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
            Assert.Equal(Utf8("Command palette already uses this chord."), Utf8(store.Refusal(Default(AppShortcut.CommandPalette), AppShortcut.NewThread)!));
            foreach (var holder in Table.Where(r => r.Rendered is not null))
                foreach (var other in Table.Where(r => r.Shortcut != holder.Shortcut))
                    Assert.Equal(Utf8(holder.Title + " already uses this chord."), Utf8(store.Refusal(Default(holder.Shortcut), other.Shortcut)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:330-333
    public void The_owner_check_reads_current_bindings_and_names_the_first_holder_in_declaration_order()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var ctrlK = Default(AppShortcut.CommandPalette);
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
        var ctrlD = Default(AppShortcut.ToggleChanges);
        OnMain(runtime, store, () =>
        {
            store.Set(null, AppShortcut.ToggleChanges);
            store.Set(ctrlD, AppShortcut.ShowMainWindow);
            Assert.Equal(Utf8("Show Marrow already uses this chord."), Utf8(store.Refusal(ctrlD, AppShortcut.NewThread)!));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:325-327, :75-78 (OD-0008 B7)
    public void A_key_that_types_pressed_without_Ctrl_or_Alt_is_refused_with_the_Windows_wording()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        OnMain(runtime, store, () =>
        {
            // Every shipped key types or moves the caret: letters, ".", ",", Return 36, Tab 48, Delete 51 (:52-58, :77).
            foreach (var holder in Table.Where(r => r.Rendered is not null))
            {
                var bare = new KeyChord(Default(holder.Shortcut).KeyCode, default);
                var replacing = holder.Shortcut == AppShortcut.NewThread ? AppShortcut.OpenSettings : AppShortcut.NewThread;
                Assert.Equal(Utf8("Add Ctrl or Alt so typing still works."), Utf8(store.Refusal(bare, replacing)!));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-219")] // Core/Models/Shortcuts.swift:322-333 (the order of the checks)
    public void The_typing_check_fires_before_the_owner_check_and_a_free_chord_with_Ctrl_is_allowed()
    {
        using var runtime = new TestRuntime();
        var store = Load(runtime);
        var bareN = new KeyChord(Default(AppShortcut.NewThread).KeyCode, default);
        var ctrlAltN = new KeyChord(Default(AppShortcut.NewThread).KeyCode, Default(AppShortcut.ToggleActivityView).ModifierFlags);
        OnMain(runtime, store, () =>
        {
            store.Set(bareN, AppShortcut.ToggleSidebar);
            Assert.Equal(Utf8("Add Ctrl or Alt so typing still works."), Utf8(store.Refusal(bareN, AppShortcut.NewThread)!));
            Assert.Null(store.Refusal(ctrlAltN, AppShortcut.NewThread));
        });
    }
}
