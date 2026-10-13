// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-3, ED-NEW-pcws-4; DESIGN.md 2.2 pc_launch_app
// place, 2.13, 4.7 "Use for new apps"). Where a new app opens: pc_launch_app's place (auto, desk or workspace) against the
// user's "Use for new apps" (Auto, Always, Never), whether the workspace is released and set up, and whether the app is
// already open on the desk. auto picks the workspace when it is on and the app is not already on the desk; Always picks it
// for every new app; Never and a workspace that is not set up keep auto on the desk, and refuse an explicit workspace with
// not_in_workspace. An app that opened on the desk when it was asked for the workspace (one copy per user across sessions)
// is remembered, so auto keeps it on the desk and an explicit workspace is refused before the user is asked anything.
// WorkspaceRefs: how the engine outside tells a workspace window and a workspace look from its own (ids the model sees).
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Workspace;

/// <summary>
/// Ids of workspace windows and looks as the model sees them (ED-NEW-pcws-4). A window inside the workspace has its own
/// session's handle, which may equal a desk handle, so its id carries bit 62 ("w:4000000000001a2b"); window handles use
/// 32 bits, so the bit never collides. Looks taken inside start above 2^40, so an obs id says where it was taken.
/// </summary>
public static class WorkspaceRefs
{
    public const long Bit = 1L << 62;
    public const long ObservationBase = 1L << 40;

    public static bool IsWorkspace(nint handle) => ((long)handle & Bit) != 0;

    public static nint Tag(nint handle) => (nint)(((long)handle & ~Bit) | Bit);

    public static nint Untag(nint handle) => (nint)((long)handle & ~Bit);

    /// <summary>The model's id for a window inside the workspace.</summary>
    public static string Format(nint handle) => PcWindowRef.Format(Tag(handle));

    /// <summary>Whether a look id names a look taken inside the workspace.</summary>
    public static bool IsWorkspaceObservation(string? obs) => PcObsId.TryParse(obs, out var number) && number > ObservationBase;
}

/// <summary>Where a new app opens.</summary>
public enum WorkspacePlace
{
    Desk,
    Workspace,
}

/// <summary>The route for one pc_launch_app: the place, or the not_in_workspace reason when the workspace was asked for and cannot be used.</summary>
public sealed record WorkspaceRoute(WorkspacePlace Place, string? Refusal = null)
{
    public static WorkspaceRoute Desk { get; } = new(WorkspacePlace.Desk);
    public static WorkspaceRoute Workspace { get; } = new(WorkspacePlace.Workspace);
}

/// <summary>Routes pc_launch_app by place (DESIGN.md 2.13) and remembers the apps that open only on the desk.</summary>
public sealed class WorkspaceRouter
{
    /// <summary>The most apps remembered as desk-only; the oldest is forgotten first.</summary>
    public const int MaxDeskOnly = 256;

    public const string NotReleased = "Tactus's separate workspace is a preview that is not switched on in this version, so nothing was started";
    public const string NotSetUp = "the separate workspace is not set up on this PC (the user sets it up in Settings, Tactus, Separate workspace), so nothing was started";
    public const string UserSaidNever = "the user keeps new apps on the desk (Settings, Tactus, Separate workspace, Use for new apps: Never), so nothing was started";
    public const string OneCopy = "this app opens only one copy per user and opened on the desk when it was last started for the workspace, so nothing was started; use it on the desk";

    private readonly Lock gate = new();
    private readonly LinkedList<string> deskOnlyOrder = new();
    private readonly HashSet<string> deskOnly = new(StringComparer.Ordinal);

    /// <summary>
    /// The route for an app: <paramref name="place"/> is pc_launch_app's (auto, desk, workspace), <paramref name="use"/> the
    /// user's "Use for new apps" (auto, always, never), <paramref name="released"/> the preview gate, <paramref name="status"/>
    /// the workspace's, <paramref name="openOnDesk"/> whether the app already has a window on the desk.
    /// </summary>
    public WorkspaceRoute Route(string place, string use, bool released, WorkspaceStatus status, bool openOnDesk, string appKey)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (place == "desk") return WorkspaceRoute.Desk;
        var usable = released && status.Usable;
        var oneCopy = DeskOnly(appKey);
        if (place == "workspace")
        {
            if (!released) return new WorkspaceRoute(WorkspacePlace.Workspace, NotReleased);
            if (!status.Usable) return new WorkspaceRoute(WorkspacePlace.Workspace, NotSetUp);
            if (use == "never") return new WorkspaceRoute(WorkspacePlace.Workspace, UserSaidNever);
            if (oneCopy) return new WorkspaceRoute(WorkspacePlace.Workspace, OneCopy);
            return WorkspaceRoute.Workspace;
        }
        // auto
        if (!usable || use == "never" || oneCopy) return WorkspaceRoute.Desk;
        if (use == "always") return WorkspaceRoute.Workspace;
        return openOnDesk ? WorkspaceRoute.Desk : WorkspaceRoute.Workspace;
    }

    /// <summary>The app opened on the desk when it was started for the workspace: from now on it opens on the desk.</summary>
    public void OpenedOnDesk(string appKey)
    {
        lock (gate)
        {
            if (!deskOnly.Add(appKey)) return;
            deskOnlyOrder.AddLast(appKey);
            while (deskOnlyOrder.Count > MaxDeskOnly)
            {
                deskOnly.Remove(deskOnlyOrder.First!.Value);
                deskOnlyOrder.RemoveFirst();
            }
        }
    }

    public bool DeskOnly(string appKey)
    {
        lock (gate) return deskOnly.Contains(appKey);
    }

    /// <summary>How many apps are remembered as desk-only (at most <see cref="MaxDeskOnly"/>).</summary>
    public int DeskOnlyCount
    {
        get
        {
            lock (gate) return deskOnly.Count;
        }
    }

    /// <summary>"w:4000000000001a2b "Untitled - Notepad"" lines for a reason or a body.</summary>
    public static string Windows(IEnumerable<PcWindowInfo> windows) =>
        string.Join(", ", windows.Select(w => PcWindowRef.Format(w.Handle) + " " + PcWindowLine.Quote(TreeFormatter.Clean(w.Title, 80))));
}
