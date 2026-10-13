using System.Globalization;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792), :1-115.
// DEVIATION OD-0008 C3: the port draws no menu bar. `var body: some Commands` (:11-114) becomes Body, the same commands
// as a table in the source's order, each with its action, its enablement rule and its chord; Port/MenuKeyEquivalents.cs
// dispatches the chords with no menu present. The groups, the "Thread" menu and the dividers (:24, :32, :52, :84, :99)
// are drawn by nothing, so they have no entries. Chords are named in Windows notation (OD-0008 B7, ED-050, ED-099).
// Sweep B: :12-15 and :21-25 now call the landed WindowManager (STOP #1 resolved).
// :17-19 replaces the save placement with nothing; secondary windows close themselves on Ctrl+W.
// Outside this file the runtime install (DroppyCodeApp.swift:24) is WindowManager.MakeWindow's and
// TourWindowController.Present's attach of the dispatch to each window they build (STOP #6, resolved by fix/shortcuts).
// AltGr handling is still unsettled (STOP #7); the Welcome tour palette row is OD-0053's (STOP #4, resolved).
// The zoom items (:53-62) carry literal key equivalents, "+", "-", "0" and "=", which a menu matches by the character the
// key types (ED-270, superseding ED-230's position reading of "+"). They are marked MatchesTypedCharacter and matched by
// Port/MenuKeyEquivalents.Typed, the character the key types on the active layout, while every other item keeps the
// US-position reading of ED-099 (STOP App.AppCommands #2, resolved).

namespace Marrow.App;

/// <summary>:3-115 <c>struct AppCommands: Commands</c>: the app's commands, as a table (OD-0008 C3).</summary>
public sealed class AppCommands
{
    // :5 private let shortcuts = ShortcutStore.shared
    private readonly ShortcutStore shortcuts = ShortcutStore.Shared.Current;

    // The struct's memberwise init(model:).
    public AppCommands(AppModel model) => Model = model;

    // :4 let model: AppModel
    public AppModel Model { get; }

    // :7-9 private var runtime: ThreadRuntime?. Reading it creates the selected thread's runtime (AppModel.runtime(for:)).
    private ThreadRuntime? Runtime => Model.SelectedThreadID is { } id ? Model.Runtime(id) : null;

    /// <summary>
    /// :11-114 <c>var body: some Commands</c>, read afresh each time as SwiftUI re-reads it, so every title and every
    /// enablement rule reflects the model now.
    /// </summary>
    public IReadOnlyList<MenuCommand> Body
    {
        get
        {
            var body = new List<MenuCommand>
            {
                // :12-15 CommandGroup(replacing: .appSettings)
                new("Settings\U00002026", // :13
                    shortcuts.KeyboardShortcut(AppShortcut.OpenSettings), // :14
                    false,
                    () => WindowManager.Shared.ToggleSettings()), // :13; OD-0151: the shortcut closes the Settings panel when it is open
                // :21-25 CommandGroup(before: .windowArrangement)
                new(AppInfo.Current.Name, // :22; OD-0012 F1: Marrow
                    shortcuts.KeyboardShortcut(AppShortcut.ShowMainWindow), // :23; OD-0012 G6: no default
                    false,
                    () => WindowManager.Shared.ShowMain()), // :22
                // :27-35 CommandGroup(replacing: .newItem)
                new("New thread", // :28
                    shortcuts.KeyboardShortcut(AppShortcut.NewThread), // :29
                    false,
                    () => Model.NewThread()), // :28
                new("New thread in worktree", // :30
                    shortcuts.KeyboardShortcut(AppShortcut.NewWorktreeThread), // :31
                    false,
                    () => Model.NewThread(workspace: WorkspaceMode.Worktree)), // :30
                new("Add project\U00002026", // :33, U+2026 HORIZONTAL ELLIPSIS
                    shortcuts.KeyboardShortcut(AppShortcut.AddProject), // :34
                    false,
                    Model.ChooseProjectFolder), // :33

                // :37-51 CommandGroup(after: .sidebar)
                new("Toggle sidebar", // :38
                    shortcuts.KeyboardShortcut(AppShortcut.ToggleSidebar), // :39
                    // DEVIATION (ED-NEW-sidebarmodes-3): disabled in Panel only, which has no column. On the Mac the item stays live there
                    // and flips a column state nothing draws, which then decides, unseen, whether Floating opens docked or as the panel.
                    Model.Settings.SidebarMode == SidebarMode.PanelOnly,
                    Model.Sidebar.Toggle), // :38
                new("Command palette", // :40
                    shortcuts.KeyboardShortcut(AppShortcut.CommandPalette), // :41
                    false,
                    () => Model.IsCommandPalettePresented = !Model.IsCommandPalettePresented), // :40
                new("Toggle terminal", // :42
                    shortcuts.KeyboardShortcut(AppShortcut.ToggleTerminal), // :43
                    Model.SelectedThreadID is null, // :44
                    () =>
                    {
                        if (Runtime is { } runtime) runtime.IsTerminalVisible = !runtime.IsTerminalVisible; // :42
                    }),
                new("Toggle changes", // :45
                    shortcuts.KeyboardShortcut(AppShortcut.ToggleChanges), // :46
                    Model.SelectedThreadID is null, // :47
                    () => Runtime?.ToggleChanges()), // :45; web W4: the pane (Alt+click on the toolbar button is the popover, ToggleDiff)
                new("Toggle activity view", // :48
                    shortcuts.KeyboardShortcut(AppShortcut.ToggleActivityView), // :51
                    false,
                    () => Transaction.WithAnimation(Chrome.PanelSlide, // :49
                        () => Model.Settings.SidebarActivityView = !Model.Settings.SidebarActivityView)),
                // :52 Divider() is drawn by no menu. :53-62 the zoom items: literal key equivalents with .command, not
                // taken from the shortcut store, and no .disabled, so at either end the step leaves the value as it is.
                new("Zoom In", // :53
                    new KeyboardShortcut(new KeyEquivalent("+"), ChordModifiers.Control), // :54
                    false,
                    () => Model.Settings.AppZoom = AppZoom.Next(ZoomPercent(Model.Settings.AppZoom), increasing: true),
                    MatchesTypedCharacter: true), // :53
                new("Zoom Out", // :55
                    new KeyboardShortcut(new KeyEquivalent("-"), ChordModifiers.Control), // :56
                    false,
                    () => Model.Settings.AppZoom = AppZoom.Next(ZoomPercent(Model.Settings.AppZoom), increasing: false),
                    MatchesTypedCharacter: true), // :55
                new("Actual Size", // :57
                    new KeyboardShortcut(new KeyEquivalent("0"), ChordModifiers.Control), // :58
                    false,
                    () => Model.Settings.AppZoom = 100,
                    MatchesTypedCharacter: true), // :57
                // :59 US keyboards produce '=' for Command-plus without Shift. :62 .hidden(): no menu draws it either way.
                new("Zoom In", // :60
                    new KeyboardShortcut(new KeyEquivalent("="), ChordModifiers.Control), // :61
                    false,
                    () => Model.Settings.AppZoom = AppZoom.Next(ZoomPercent(Model.Settings.AppZoom), increasing: true),
                    MatchesTypedCharacter: true), // :60

                // :65-98 CommandMenu("Thread"), the items before the second divider
                new("Stop", // :66
                    shortcuts.KeyboardShortcut(AppShortcut.StopTurn), // :67
                    !(Runtime?.IsRunning ?? false), // :68
                    () => Runtime?.Interrupt()), // :66
                // :69-73 Off while the thread is waiting on an approval: the approve button ships with the same chord, and
                // a menu command always wins over a button's shortcut, so the queue would swallow the chord the card is
                // asking for. On with nothing to queue behind: the draft then just goes out (see queueDraftAsFollowUp).
                new("Queue a chat", // :74
                    shortcuts.KeyboardShortcut(AppShortcut.QueueChat), // :75
                    (Runtime?.DraftIsEmpty ?? true) || !((Runtime?.Approvals.Count ?? 0) == 0), // :76-77 (approvals.isEmpty)
                    () => Runtime?.QueueDraftAsFollowUp()), // :74
                new("Toggle plan mode", // :78
                    shortcuts.KeyboardShortcut(AppShortcut.TogglePlanMode), // :82
                    Model.SelectedThreadID is null, // :83
                    () =>
                    {
                        if (Model.SelectedThreadID is not { } id) return; // :79
                        Model.UpdateThread(id, thread => thread with
                        {
                            InteractionMode = thread.InteractionMode == InteractionMode.Plan ? InteractionMode.Build : InteractionMode.Plan,
                        }); // :80
                    }),
                new("Previous thread", // :85
                    shortcuts.KeyboardShortcut(AppShortcut.PreviousThread), // :86
                    false,
                    () => Model.SelectThread(offset: -1)), // :85
                new("Next thread", // :87
                    shortcuts.KeyboardShortcut(AppShortcut.NextThread), // :88
                    false,
                    () => Model.SelectThread(offset: 1)), // :87
                new(Model.TopScript is { } top ? "Run " + top.Name : "Run top script", // :89
                    shortcuts.KeyboardShortcut(AppShortcut.RunScript), // :93
                    Model.TopScript is null, // :94
                    () =>
                    {
                        if (Model.TopScript is not { } script || Model.SelectedThreadID is not { } threadID) return; // :90
                        Model.RunScript(script, threadID); // :91
                    }),
            };

            // :95-98 ForEach(1...9, id: \.self): "Thread 1" to "Thread 9" on Command and that digit, hard-coded.
            for (var number = 1; number <= 9; number++)
            {
                var digit = number.ToString(CultureInfo.InvariantCulture);
                var selected = number;
                body.Add(new MenuCommand("Thread " + digit, // :96
                    new KeyboardShortcut(new KeyEquivalent(digit), ChordModifiers.Control), // :97
                    false,
                    () => Model.SelectThreadNumber(selected))); // :96
            }

            // :99 Divider() is drawn by no menu.
            body.Add(new MenuCommand(Model.FinishActionTitle, // :100
                shortcuts.KeyboardShortcut(AppShortcut.FinishThread), // :101
                Model.SelectedThreadID is null, // :102
                Model.FinishSelectedThread)); // :100
            body.Add(new MenuCommand("Archive thread\U00002026", // :103, U+2026 HORIZONTAL ELLIPSIS
                shortcuts.KeyboardShortcut(AppShortcut.ArchiveThread), // :104
                Model.SelectedThreadID is null, // :105
                Model.AskToArchiveSelectedThread)); // :103
            body.Add(new MenuCommand(Model.SelectedThread?.IsSettled == true ? "Reopen thread" : "Settle thread", // :106
                shortcuts.KeyboardShortcut(AppShortcut.SettleThread), // :107
                Model.SelectedThread?.IsHelper ?? true, // :108, nothing selected reads as disabled
                Model.SettleSelectedThread)); // :106

            // :111-113 CommandGroup(replacing: .help): one item, no chord, never disabled.
            body.Add(new MenuCommand("Welcome tour", null, false, () => Tour.Present(Model))); // :112

            return body;
        }
    }

    /// <summary>
    /// A key equivalent pressed in a window of the app: the first enabled command carrying it runs, and true says the
    /// chord was taken (MenuKeyEquivalents.Perform). Port-only: upstream the menu bar does this (OD-0008 C3).
    /// </summary>
    public bool PerformKeyEquivalent(KeyboardShortcut shortcut) => MenuKeyEquivalents.Perform(Body, shortcut);

    /// <summary>
    /// A key-down read two ways (ED-270): by key position for the ordinary items and, only when a literal-character item
    /// (the zoom items) is reached, by the character it types. The windows' dispatch calls this one.
    /// </summary>
    public bool PerformKeyDown(KeyboardShortcut? positional, Func<KeyboardShortcut?> typed, ShortcutScope scope = ShortcutScope.Chrome) =>
        MenuKeyEquivalents.Perform(Body, positional, typed, scope);

    // :53, :55, :60 pass model.settings.appZoom, a Swift Int (long here), to AppZoom.next(_:increasing:), whose port
    // takes an int (UI/Common/AppZoom.cs). next reads the value only through normalized(_:) (AppZoom.swift:8-10, :13),
    // which gives 100 for anything that is not a level; a value outside int's range is no level, so it goes in as 0,
    // which is none either, and the step comes out as Swift's.
    private static int ZoomPercent(long percent) => percent is >= int.MinValue and <= int.MaxValue ? (int)percent : 0;
}
