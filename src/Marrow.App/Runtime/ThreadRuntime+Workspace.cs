using Marrow.Core.Models;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3a, W3c and W4, the thread's pane workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Pane
// state moves off ThreadRuntime flags (IsTerminalVisible, IsDiffVisible) onto PaneWorkspaceState, which ThreadRuntime owns and the
// window reads. Old flags map in once. Restart restores the exact layout. Each ThreadWindow has its own workspace for its thread";
// ED-NEW-web-w3a-1, ED-NEW-web-w3c-1, ED-NEW-web-w4-1). No Swift counterpart. The runtime is the one owner of a thread's
// workspace: it starts from what the thread saved (or the old flags and the ratios the user set last), every change is written
// to the thread in the library (the app saves it as it saves any thread change), and the terminal and Changes flags below are
// read off it.
//
// Web surfaces W4 (ED-NEW-web-w4-1): the terminal and Changes are panes now. IsTerminalVisible (ThreadRuntime.swift:239-243)
// is a view of the workspace: the terminal is visible when it is open, in the chat's drawer or in the grid, and setting it
// opens it where it was last (the drawer by default) or closes it. Changes opens as a pane (ToggleChanges, Ctrl+D); the
// old IsDiffVisible flag stays the Alt-click popover's.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private PaneWorkspaceState workspace = PaneWorkspaceState.Default;

    // What the workspace says about the terminal and Changes, kept as values of their own so an observer of one of them is told
    // only when it flips: a gutter drag writes the workspace on every pointer move, and the chat's toolbar must not rebuild for it.
    private bool isTerminalVisible;
    private bool isTerminalInDrawer;
    private bool isTerminalInGrid;
    private bool isChangesPaneOpen;

    // Everything saved with the workspace but the layout: each pane unit's data and what a newer build wrote that this one keeps.
    private StoredWorkspace stored = new();

    /// <summary>
    /// The thread's pane workspace: which panes are open, their order, ratios, the expanded pane. The window's
    /// PaneWorkspace draws it and writes back what the user changes; other surfaces open panes by setting it
    /// (<c>runtime.Workspace = runtime.Workspace.Open(kind)</c>). Every change is saved with the thread.
    /// </summary>
    public PaneWorkspaceState Workspace
    {
        get => Read(workspace);
        set => Set(ref workspace, value ?? PaneWorkspaceState.Default, WorkspaceChanged);
    }

    // :239-243 The terminal is shown anywhere in the workspace: the chat's drawer or a grid cell. Opening it from nothing asks
    // for the keyboard (:241-243); closing it closes it wherever it is.
    public bool IsTerminalVisible
    {
        get => Read(isTerminalVisible);
        set
        {
            if (value == isTerminalVisible) return;
            if (value) TerminalWantsFocus = true; // :241-243, before anything is told, as the source's didSet is
            Workspace = value ? Workspace.Open(PaneKind.Terminal) : Workspace.Close(PaneKind.Terminal);
        }
    }

    /// <summary>The terminal is the chat column's drawer (its default place; ChatView draws that drawer).</summary>
    public bool IsTerminalInDrawer => Read(isTerminalInDrawer);

    /// <summary>The terminal is a cell of the pane grid (the workspace draws it).</summary>
    public bool IsTerminalInGrid => Read(isTerminalInGrid);

    /// <summary>Changes is open as a pane (the Alt-click popover is <see cref="IsDiffVisible"/>).</summary>
    public bool IsChangesPaneOpen => Read(isChangesPaneOpen);

    /// <summary>
    /// Ctrl+D, the toolbar's Changes button and the palette's "Toggle changes" (web W4, DESIGN 3.1, 3.7): the Changes pane
    /// opens, or closes. A popover that Alt+click opened closes first, as ToggleDiff's second press always has.
    /// </summary>
    public void ToggleChanges()
    {
        ClearDiffFocus(); // ThreadRuntime.swift:409
        if (IsDiffVisible)
        {
            IsDiffVisible = false;
            return;
        }
        Workspace = Workspace.Toggle(PaneKind.Changes);
    }

    /// <summary>The pane's data its unit keeps with the thread (browser tabs, the artifact shown), as JSON text; null when it has none.</summary>
    public string? GetPaneState(PaneKind kind) => stored.PaneData.TryGetValue(PaneKinds.Id(kind), out var json) ? json : null;

    /// <summary>Keeps a pane's data with the thread and saves it. Null, or text that is not JSON, forgets it.</summary>
    public void SetPaneState(PaneKind kind, string? json)
    {
        var next = stored.WithPaneData(PaneKinds.Id(kind), json);
        if (next.Equals(stored)) return;
        stored = next;
        Save();
    }

    /// <summary>The workspace as it is saved with the thread (tests read it; the library holds the same).</summary>
    internal StoredWorkspace StoredWorkspace => stored.WithState(workspace.Normalized());

    // The constructor runs this before anything reads the flags (web W3c): a thread that saved a workspace has it back (nothing
    // re-clamped: a layout from a larger monitor is kept whole and the solver fits it to this window, and a restored terminal asks
    // for no keyboard); one that saved none maps the old flags in once and starts from the ratios the user set last. The cached
    // flags below are set as fields, so nobody is told: nobody is watching yet.
    private void InitializeWorkspace(AppModel app)
    {
        var saved = app.Thread(ThreadID)?.Workspace;
        stored = PaneWorkspaceMigration.Initial(saved, isTerminalVisible, isDiffVisible, app.Settings.PaneRatioDefaults);
        workspace = stored.State;
        var now = workspace.Normalized();
        isTerminalVisible = now.IsOpen(PaneKind.Terminal);
        isTerminalInDrawer = now.IsTerminalInDrawer;
        isTerminalInGrid = now.SlotIndexOf(PaneKind.Terminal) >= 0;
        isChangesPaneOpen = now.IsOpen(PaneKind.Changes);
    }

    // The workspace was written: the flags it implies follow (each tells its observers only when it flips), and a terminal that
    // changed place between the drawer and the grid while it stayed open asks for the keyboard, because the session view is hosted
    // anew (the new host spends this request when it is attached). Then it is saved with the thread.
    private void WorkspaceChanged(PaneWorkspaceState before)
    {
        var was = before.Normalized();
        var now = workspace.Normalized();
        // PORT-ONLY (web surfaces W6, DESIGN 3.9): closing Side chat ends what it was allowed to do (AppModel.SideChatClosed).
        if (was.IsSideChatOpen && !now.IsSideChatOpen) App?.SideChatClosed(ThreadID);
        if (was.IsOpen(PaneKind.Terminal) && now.IsOpen(PaneKind.Terminal) && was.IsTerminalInDrawer != now.IsTerminalInDrawer)
            TerminalWantsFocus = true;
        Set(ref isTerminalVisible, now.IsOpen(PaneKind.Terminal), property: nameof(IsTerminalVisible));
        Set(ref isTerminalInDrawer, now.IsTerminalInDrawer, property: nameof(IsTerminalInDrawer));
        Set(ref isTerminalInGrid, now.SlotIndexOf(PaneKind.Terminal) >= 0, property: nameof(IsTerminalInGrid));
        Set(ref isChangesPaneOpen, now.IsOpen(PaneKind.Changes), property: nameof(IsChangesPaneOpen));
        if (was.Equals(now)) return;
        RememberRatios(was, now);
        Save();
    }

    // The ratios the user set last start the next thread that has no workspace of its own.
    private void RememberRatios(PaneWorkspaceState before, PaneWorkspaceState after)
    {
        if (before.Ratios.Equals(after.Ratios) || App?.Settings is not { } settings) return;
        settings.PaneRatioDefaults = settings.PaneRatioDefaults.Mutating(map =>
        {
            foreach (var key in before.Ratios.Keys.Concat(after.Ratios.Keys).Distinct())
            {
                if (after.Ratios.TryGetValue(key, out var ratio)) map[key] = ratio;
                else map.Remove(key);
            }
        });
    }

    private void Save()
    {
        var next = stored.WithState(workspace.Normalized());
        stored = next;
        App?.UpdateThread(ThreadID, thread => thread with { Workspace = next.IsEmpty ? null : next });
    }
}
