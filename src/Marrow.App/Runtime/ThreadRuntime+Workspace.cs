using Marrow.Core.Models;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3a and W3c, the thread's pane workspace (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.11 "Pane state
// moves off ThreadRuntime flags (IsTerminalVisible, IsDiffVisible) onto PaneWorkspaceState, which ThreadRuntime owns and the
// window reads. Old flags map in once. Restart restores the exact layout. Each ThreadWindow has its own workspace for its
// thread"; ED-NEW-web-w3a-1, ED-NEW-web-w3c-1). No Swift counterpart. The runtime is the one owner of a thread's workspace:
// it starts from what the thread saved (or the old flags and the ratios the user set last), every change is written to the
// thread in the library (the app saves it as it saves any thread change), and IsTerminalVisible and IsDiffVisible are the
// workspace's terminal drawer and Changes pane read as the flags they were.

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private PaneWorkspaceState workspace = PaneWorkspaceState.Default;

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
        set => Set(ref workspace, (value ?? PaneWorkspaceState.Default).Normalized(), WorkspaceChanged);
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
    internal StoredWorkspace StoredWorkspace => stored.WithState(workspace);

    // The constructor runs this before anything reads the flags: a thread that saved a workspace has it back (nothing re-clamped:
    // a layout from a larger monitor is kept whole and the solver fits it to this window); one that saved none maps the old
    // flags in once and starts from the ratios the user set last.
    private void InitializeWorkspace(AppModel app)
    {
        var saved = app.Thread(ThreadID)?.Workspace;
        stored = PaneWorkspaceMigration.Initial(saved, isTerminalVisible, isDiffVisible, app.Settings.PaneRatioDefaults);
        workspace = stored.State;
        isTerminalVisible = workspace.IsTerminalInDrawer;
        isDiffVisible = workspace.IsOpen(PaneKind.Changes);
    }

    private void WorkspaceChanged(PaneWorkspaceState before)
    {
        var after = workspace;
        if (before.Equals(after)) return;
        var terminal = after.IsTerminalInDrawer;
        if (terminal != isTerminalVisible)
        {
            var wasVisible = isTerminalVisible;
            Set(ref isTerminalVisible, terminal, property: nameof(IsTerminalVisible));
            if (!wasVisible && terminal) TerminalWantsFocus = true; // :241-243
        }
        var changes = after.IsOpen(PaneKind.Changes);
        if (changes != isDiffVisible) Set(ref isDiffVisible, changes, property: nameof(IsDiffVisible));
        RememberRatios(before, after);
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
        var next = stored.WithState(workspace);
        stored = next;
        App?.UpdateThread(ThreadID, thread => thread with { Workspace = next.IsEmpty ? null : next });
    }
}
