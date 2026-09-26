using System.Globalization;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792), :1-115.
// DEVIATION OD-0008 C3: the port draws no menu bar. `var body: some Commands` (:11-114) becomes Body, the same commands
// as a table in the source's order, each with its action, its enablement rule and its chord; Port/MenuKeyEquivalents.cs
// dispatches the chords with no menu present. The groups, the "Thread" menu and the dividers (:24, :32, :52, :84, :99)
// are drawn by nothing, so they have no entries. Chords are named in Windows notation (OD-0008 B7, ED-050, ED-099).
// Part 1 of 3 (App.AppCommands): :1-51, :65-98 and :111-113. Not written here:
// - :12-15 (the Settings item) and :21-25 (the app-name item that shows the main window) call WindowManager.shared,
//   still MARROW_STUB (App/Window/WindowManager.cs): STOP App.AppCommands #1 in tests/Marrow.App.Tests/AppCommandsTests.cs.
// - :17-19 replaces the save placement with nothing, so it has no entry; Ctrl+W stays free for the archive command and
//   a secondary window closes itself (ThreadWindow.cs SecondaryWindow).
// - :52-62 (the zoom items) and :99-108 (the finish, archive and settle items) are App.AppCommands part 2.

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
                    false,
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
                    () => Runtime?.ToggleDiff()), // :45
                new("Toggle activity view", // :48
                    shortcuts.KeyboardShortcut(AppShortcut.ToggleActivityView), // :51
                    false,
                    () => Transaction.WithAnimation(Chrome.PanelSlide, // :49
                        () => Model.Settings.SidebarActivityView = !Model.Settings.SidebarActivityView)),

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
}
