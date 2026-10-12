using Marrow.Core.Models;

// PORT-ONLY: web surfaces W3a, the thread's pane workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Pane state
// moves off ThreadRuntime flags ... onto PaneWorkspaceState, which ThreadRuntime owns and the window reads";
// ED-NEW-web-w3a-1). No Swift counterpart. W3a keeps the state in memory for the thread's runtime; W3c maps the old
// IsTerminalVisible and IsDiffVisible flags in once (PaneWorkspaceState.FromLegacyFlags) and saves it with the thread.
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

    /// <summary>
    /// The thread's pane workspace: which panes are open, their order, ratios, the expanded pane. The window's
    /// PaneWorkspace draws it and writes back what the user changes; other surfaces open panes by setting it
    /// (<c>runtime.Workspace = runtime.Workspace.Open(kind)</c>).
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

    // The workspace was written: the flags it implies follow (each tells its observers only when it flips), and a terminal that
    // changed place between the drawer and the grid while it stayed open asks for the keyboard, because the session view is hosted
    // anew (the new host spends this request when it is attached).
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
    }
}
