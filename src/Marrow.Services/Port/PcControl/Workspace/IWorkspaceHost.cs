// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; OD-0140, OD-0145, ED-NEW-pcws-1, ED-NEW-pcws-2; DESIGN.md
// 2.13, 3.1, 4.7, section 8 P18). The seam the engine drives the workspace through: its readiness (WTSIsChildSessionsEnabled,
// read through IChildSessionReadiness and never written), an app launched inside it, its windows, and one tool call handed
// to the agent that runs the same backends inside the child session. The app binds ChildSessionHost (the Remote Desktop
// control and a current-user pipe); tests bind a fake. nth never changes a Windows setting here: the only writer of the
// child-session switch is IChildSessionSwitch, and only the --tactus-workspace-enable and -disable commands reach it, which
// the user runs in a terminal they opened (WorkspaceSetup). Until NP-5 to NP-7 pass, the workspace is a preview this build
// does not use (WorkspacePreview.Released).
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Workspace;

/// <summary>Where the workspace stands (DESIGN.md 4.7, the status line).</summary>
public enum WorkspaceState
{
    /// <summary>Child sessions are off (or Windows could not say): "Not set up".</summary>
    NotSetUp,

    /// <summary>Child sessions are on and no workspace runs: "Ready".</summary>
    Ready,

    /// <summary>A workspace runs, with its agent connected: "Running, 2 apps".</summary>
    Running,
}

/// <summary>The status line of Settings › Tactus › Separate workspace, and what pc_list_apps says about the workspace.</summary>
public sealed record WorkspaceStatus(WorkspaceState State, int Apps = 0)
{
    public const string NotSetUpText = "Not set up";
    public const string ReadyText = "Ready";

    public static WorkspaceStatus NotSetUp { get; } = new(WorkspaceState.NotSetUp);

    public static WorkspaceStatus Ready { get; } = new(WorkspaceState.Ready);

    /// <summary>The status when no workspace runs: Ready only when Windows says child sessions are on.</summary>
    public static WorkspaceStatus Of(bool? childSessionsEnabled) => childSessionsEnabled == true ? Ready : NotSetUp;

    /// <summary>"Not set up", "Ready" or "Running, 2 apps" (DESIGN.md 4.7).</summary>
    public string Text => State switch
    {
        WorkspaceState.Running => "Running, " + Apps.ToString(System.Globalization.CultureInfo.InvariantCulture) + (Apps == 1 ? " app" : " apps"),
        WorkspaceState.Ready => ReadyText,
        _ => NotSetUpText,
    };

    /// <summary>Whether apps can be started there: set up, running or not.</summary>
    public bool Usable => State is WorkspaceState.Ready or WorkspaceState.Running;
}

/// <summary>Windows' child-session switch, read only (WTSIsChildSessionsEnabled): null when Windows could not say.</summary>
public interface IChildSessionReadiness
{
    bool? Enabled();
}

/// <summary>
/// The one writer of the child-session switch (WTSEnableChildSessions). Only WorkspaceSetup.Run reaches it, for the
/// --tactus-workspace-enable and -disable commands the user runs; nothing in the engine, the host or Settings holds one.
/// </summary>
public interface IChildSessionSwitch
{
    /// <summary>Turns child sessions on or off; false with the Windows error when Windows refused.</summary>
    bool Set(bool enabled, out int error);
}

/// <summary>A window inside the workspace: its facts as the agent reads them (its handle is the child session's own) and whether it runs elevated.</summary>
public sealed record WorkspaceWindow(PcWindowInfo Info, bool Elevated)
{
    /// <summary>The window as the engine outside knows it: the same facts with its handle tagged as a workspace window.</summary>
    public PcWindowInfo Tagged() => Info with { Handle = WorkspaceRefs.Tag(Info.Handle) };
}

/// <summary>What starting an app in the workspace did: its new windows there, or why nothing started.</summary>
public sealed record WorkspaceLaunch(bool Started, IReadOnlyList<WorkspaceWindow> Windows, string? Problem)
{
    public static WorkspaceLaunch Failed(string problem) => new(false, [], problem);
}

/// <summary>One tool call for the agent's engine: the chat, the tool and its arguments as the model gave them, and what the engine outside knows about the chat.</summary>
public sealed record WorkspaceCall(string Thread, string Tool, JsonObject Args)
{
    /// <summary>The settings now: the deny list, confirmations, screenshot detail and OCR apply inside too.</summary>
    public PcSettings Settings { get; init; } = PcSettings.Default;
    public bool Vision { get; init; } = true;
    public PcGridProfile? Grid { get; init; }
    public string? Head { get; init; }
    public string? Model { get; init; }
}

/// <summary>What the user answered on a guarded-action card shown for a step in the workspace, and what they wrote with a refusal.</summary>
public sealed record WorkspaceConfirm(bool Allowed, string? Note);

/// <summary>What the user answered on an approval asked for a step in the workspace, and what they said with a denial.</summary>
public sealed record WorkspaceApproval(PcApproval Answer, string? Said);

/// <summary>
/// The asks a step in the workspace makes of the user: the same approvals and guarded-action cards as on the desk, shown by
/// nth outside (the pill and the chat), never by the agent (DESIGN.md 2.13: approvals and guarded actions apply unchanged).
/// </summary>
public interface IWorkspaceAsks
{
    Task<WorkspaceApproval> Approve(PcAppIdentity app, string appName, PcCapability capability, CancellationToken cancellationToken);

    Task<WorkspaceConfirm> Confirm(string word, string elementName, string appName, CancellationToken cancellationToken);
}

/// <summary>The separate workspace as the engine sees it (DESIGN.md 2.13).</summary>
public interface IWorkspaceHost
{
    /// <summary>Whether this build lets Tactus use the workspace: false until NP-5 to NP-7 pass (WorkspacePreview).</summary>
    bool Released { get; }

    /// <summary>Not set up, ready or running (readiness from WTSIsChildSessionsEnabled); never changes anything.</summary>
    WorkspaceStatus Status();

    /// <summary>Starts the app inside the workspace (the workspace starts first when it is not running); its new windows there.</summary>
    Task<WorkspaceLaunch> Launch(string appIdOrPath, CancellationToken cancellationToken);

    /// <summary>The windows inside the workspace now; none when it is not running.</summary>
    Task<IReadOnlyList<WorkspaceWindow>> Windows(CancellationToken cancellationToken);

    /// <summary>One window's live facts (by its own handle, untagged); null when it is gone or the workspace is not running.</summary>
    Task<WorkspaceWindow?> Window(long handle, CancellationToken cancellationToken);

    /// <summary>Runs one tool call in the agent's engine; the user's asks come back through <paramref name="asks"/>.</summary>
    Task<PcResult> Call(WorkspaceCall call, IWorkspaceAsks asks, CancellationToken cancellationToken);

    /// <summary>Completes at once unless the user took over in "Watch workspace"; then when they give it back.</summary>
    Task WhileTakenOver(CancellationToken cancellationToken);

    /// <summary>The chat's turn ended: the agent's engine for it ends its turn too.</summary>
    void EndTurn(string thread);
}

/// <summary>
/// The preview gate (DESIGN.md 2.13, section 8 P18: "Release as preview only after NP-5 to NP-7 pass"). False in this
/// build: Settings shows the readiness and the setup steps, and the engine never starts anything in the workspace. The lead
/// turns it on once the owner's native probes pass.
/// </summary>
public static class WorkspacePreview
{
    public static readonly bool Released;

    /// <summary>The line Settings shows while the preview is not released.</summary>
    public const string NotReleasedLine = "Preview: Tactus does not use the workspace in this version yet.";
}
