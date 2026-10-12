using Marrow.Core.Models;

// PORT-ONLY: web surfaces W3a, the thread's pane workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Pane state
// moves off ThreadRuntime flags ... onto PaneWorkspaceState, which ThreadRuntime owns and the window reads";
// ED-NEW-web-w3a-1). No Swift counterpart. W3a keeps the state in memory for the thread's runtime; W3c maps the old
// IsTerminalVisible and IsDiffVisible flags in once (PaneWorkspaceState.FromLegacyFlags) and saves it with the thread.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private PaneWorkspaceState workspace = PaneWorkspaceState.Default;

    /// <summary>
    /// The thread's pane workspace: which panes are open, their order, ratios, the expanded pane. The window's
    /// PaneWorkspace draws it and writes back what the user changes; other surfaces open panes by setting it
    /// (<c>runtime.Workspace = runtime.Workspace.Open(kind)</c>).
    /// </summary>
    public PaneWorkspaceState Workspace
    {
        get => Read(workspace);
        set => Set(ref workspace, value ?? PaneWorkspaceState.Default);
    }
}
