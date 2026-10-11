// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-1, ED-NEW-pcctl-7; DESIGN.md 2.1, 2.2). The result envelope every
// PC tool returns: a first line "ok", "unverified: <reason>. <next step>.", "waiting_for_user: <reason>. token: <t>. <next
// step>." or "error: <code>: <reason>. <one recovery step>.", a window line, then a diff, a tree or nothing; JPEGs go in
// their own MCP image parts (the screenshot, then any full-resolution detail crops). Error codes are stable wire
// names.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.PcControl;

public enum PcErrorCode
{
    NotApproved,
    DeniedApp,
    Elevated,
    SecureDesktop,
    Locked,
    PasswordField,
    StaleObs,
    StaleElement,
    OccludedPoint,
    WindowGone,
    StoppedByUser,
    NeedsConfirmation,
    Timeout,
    BadArgs,
    Disabled,
    // ED-NEW-pcctl-2 (legacy Marrow pcService.ts:88-98): refusals the engine makes itself.
    OwnWindow,
    OffScreen,
    Busy,
    PcOff,
    Unattended,
    DeniedKey,
    CaptureFailed,
    NotSupported,
    // ED-NEW-pcctl-7 (DESIGN.md 2.2, parallel use): user_took_over is gone, because user input never stops Tactus.
    AmbiguousTarget,
    ExpectFailed,
    DeferredByUser,
    DeclinedByUser,
    UserTypingHere,
    BusyWindow,
    NeedsRealInput,
    NotInWorkspace,
}

/// <summary>A result that ran but is not plainly ok (DESIGN.md 2.2): a status, not an error.</summary>
public enum PcStatus
{
    Ok,
    Unverified,

    /// <summary>The step waits for the user past the 25 s budget (critique C10); it resumes with pc_wait until "step".</summary>
    WaitingForUser,
}

/// <summary>Why a step waits for the user (DESIGN.md 2.2, critique C10): the reason word of waiting_for_user.</summary>
public static class PcWaitReason
{
    public const string Approval = "approval";
    public const string HandsOff = "hands_off";
    public const string TypingHere = "typing_here";
    public const string ConfirmCard = "confirm_card";
    public const string BatchBudget = "batch_budget";

    /// <summary>The user paused that app (DESIGN.md 10.1): the call returns at once, and only the user resumes it.</summary>
    public const string Paused = "paused";

    /// <summary>A pc_ask waiting for the user's answer (DESIGN.md 10.9).</summary>
    public const string Question = "question";
}

/// <summary>Why no cursor-free rung could do a step and real input may not (DESIGN.md 2.2, the reasons of needs_real_input).</summary>
public static class PcRealInputReason
{
    public const string Off = "real input off";
    public const string NotAllowed = "not allowed for this app";
    public const string ForegroundProtected = "foreground protected";
    public const string MonitorUnhealthy = "monitor unhealthy";
    public const string ActivationUnproven = "activation unproven";
}

public static class PcErrorCodes
{
    public static string Wire(PcErrorCode code) => code switch
    {
        PcErrorCode.NotApproved => "not_approved",
        PcErrorCode.DeniedApp => "denied_app",
        PcErrorCode.Elevated => "elevated",
        PcErrorCode.SecureDesktop => "secure_desktop",
        PcErrorCode.Locked => "locked",
        PcErrorCode.PasswordField => "password_field",
        PcErrorCode.StaleObs => "stale_obs",
        PcErrorCode.StaleElement => "stale_element",
        PcErrorCode.OccludedPoint => "occluded_point",
        PcErrorCode.WindowGone => "window_gone",
        PcErrorCode.StoppedByUser => "stopped_by_user",
        PcErrorCode.NeedsConfirmation => "needs_confirmation",
        PcErrorCode.Timeout => "timeout",
        PcErrorCode.BadArgs => "bad_args",
        PcErrorCode.Disabled => "disabled",
        PcErrorCode.OwnWindow => "own_window",
        PcErrorCode.OffScreen => "off_screen",
        PcErrorCode.Busy => "busy",
        PcErrorCode.PcOff => "pc_off",
        PcErrorCode.Unattended => "unattended",
        PcErrorCode.DeniedKey => "denied_key",
        PcErrorCode.CaptureFailed => "capture_failed",
        PcErrorCode.NotSupported => "not_supported",
        PcErrorCode.AmbiguousTarget => "ambiguous_target",
        PcErrorCode.ExpectFailed => "expect_failed",
        PcErrorCode.DeferredByUser => "deferred_by_user",
        PcErrorCode.DeclinedByUser => "declined_by_user",
        PcErrorCode.UserTypingHere => "user_typing_here",
        PcErrorCode.BusyWindow => "busy_window",
        PcErrorCode.NeedsRealInput => "needs_real_input",
        PcErrorCode.NotInWorkspace => "not_in_workspace",
    };

    /// <summary>The one next step each error tells the model.</summary>
    public static string Recovery(PcErrorCode code) => code switch
    {
        PcErrorCode.NotApproved => "Tell the user this app was not allowed and ask them how to continue",
        PcErrorCode.DeniedApp => "Do this step another way or ask the user to do it",
        PcErrorCode.Elevated => "Ask the user to relaunch the app without admin rights, or to do this step themselves",
        PcErrorCode.SecureDesktop => "Stop and ask the user to answer the Windows prompt themselves",
        PcErrorCode.Locked => "Stop and tell the user the PC is locked",
        PcErrorCode.PasswordField => "Ask the user to type it themselves",
        PcErrorCode.StaleObs => "Call pc_observe and use the new obs",
        PcErrorCode.StaleElement => "Call pc_observe and pick again",
        PcErrorCode.OccludedPoint => "Use an element number instead of a point",
        PcErrorCode.WindowGone => "Call pc_list_apps to find the window again",
        PcErrorCode.StoppedByUser => "Stop using the PC and tell the user you stopped",
        PcErrorCode.NeedsConfirmation => "Ask the user in chat before you try this again",
        PcErrorCode.Timeout => "Call pc_observe to see the current state",
        PcErrorCode.BadArgs => "Fix the arguments and call again",
        PcErrorCode.Disabled => "Pick an enabled element or change the state first",
        PcErrorCode.OwnWindow => "nth's own windows are the user's to use; pick another window",
        PcErrorCode.OffScreen => "Use a point inside the window's screenshot",
        PcErrorCode.Busy => "Wait for this app's earlier Tactus actions to finish",
        PcErrorCode.PcOff => "Ask the user to turn on Tactus in nth's Settings",
        PcErrorCode.Unattended => "Only a chat the user is watching can use Tactus",
        PcErrorCode.DeniedKey => "Use another key or ask the user to press it",
        PcErrorCode.CaptureFailed => "Call pc_observe with screenshot no and use the tree",
        PcErrorCode.NotSupported => "Use another element or another tool, or ask the user to do this step",
        PcErrorCode.AmbiguousTarget => "Pick one by the details listed (a target with within or nth), or call pc_zoom; never guess",
        PcErrorCode.ExpectFailed => "Call pc_observe and decide from what is there now",
        PcErrorCode.DeferredByUser => "Do other steps first, or ask the user in chat",
        PcErrorCode.DeclinedByUser => "Ask the user in chat how to go on",
        PcErrorCode.UserTypingHere => "Do other steps first, then try this one again",
        PcErrorCode.BusyWindow => "Pick another window or ask the user",
        PcErrorCode.NeedsRealInput => "Ask the user in chat to do this step",
        PcErrorCode.NotInWorkspace => "Ask the user to open the app in the workspace, or use it on the desk",
    };
}

/// <summary>The window line: title, exe, observation, model size and scale.</summary>
public sealed record PcWindowLine(string Title, string Exe, string? Obs, int Width, int Height, double Scale)
{
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("window: ").Append(Quote(Title)).Append(" (").Append(Exe).Append(')');
        if (Obs is not null) text.Append(" obs: ").Append(Obs);
        if (Width > 0 && Height > 0)
            text.Append(" size: ").Append(Width.ToString(CultureInfo.InvariantCulture)).Append('x').Append(Height.ToString(CultureInfo.InvariantCulture))
                .Append(" scale: ").Append(Math.Round(Scale, 3).ToString("0.###", CultureInfo.InvariantCulture));
        return text.ToString();
    }

    public static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>A JPEG for the model, with its pixel size.</summary>
public sealed record PcImage(byte[] Jpeg, int Width, int Height, string ObsId);

/// <summary>One tool's result.</summary>
public sealed record PcResult
{
    public PcErrorCode? Error { get; init; }
    public PcStatus Status { get; init; }
    public string? Reason { get; init; }
    public PcWindowLine? Window { get; init; }
    public string? Body { get; init; }
    public PcImage? Image { get; init; }

    /// <summary>
    /// Full-resolution crops that go with the screenshot, each in its own image part after it (DESIGN.md 2.12): small or dense
    /// targets, the native tiles of a weak tree, the 256 x 256 crop around a point click.
    /// </summary>
    public IReadOnlyList<PcImage> Details { get; init; } = [];

    /// <summary>For waiting_for_user: the token pc_wait until "step" resumes the same step with.</summary>
    public string? Token { get; init; }

    /// <summary>On the first result after a Resume (DESIGN.md 10.1): the resumed block.</summary>
    public PcResumed? Resumed { get; init; }

    /// <summary>The action ran and its check passed (not an error, not unverified, not waiting).</summary>
    public bool Ok => Error is null && Status == PcStatus.Ok;

    public static PcResult Success(PcWindowLine? window = null, string? body = null, PcImage? image = null) => new() { Window = window, Body = body, Image = image };

    public static PcResult Fail(PcErrorCode code, string reason, PcWindowLine? window = null) => new() { Error = code, Reason = reason, Window = window };

    /// <summary>The action ran, but no change Tactus could tie to its target was seen.</summary>
    public static PcResult Unverified(string reason, PcWindowLine? window = null, string? body = null) =>
        new() { Status = PcStatus.Unverified, Reason = reason, Window = window, Body = body };

    /// <summary>
    /// The step is still waiting for the user (an approval, a confirmation card, a still moment) after the 25 s budget, so
    /// the call returns now and the step stays queued under <paramref name="token"/>; <paramref name="body"/> holds the steps
    /// already done.
    /// </summary>
    public static PcResult WaitingFor(string reason, string token, string? body = null, PcWindowLine? window = null) =>
        new() { Status = PcStatus.WaitingForUser, Reason = reason, Token = token, Body = body, Window = window };

    /// <summary>The text part.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        if (Error is { } code)
            text.Append("error: ").Append(PcErrorCodes.Wire(code)).Append(": ").Append(Sentence(Reason ?? "failed")).Append(' ').Append(PcErrorCodes.Recovery(code)).Append('.');
        else if (Status == PcStatus.Unverified)
            text.Append("unverified: ").Append(Sentence(Reason ?? "no change was seen")).Append(" Call pc_observe or give an expect; do not assume it worked.");
        else if (Status == PcStatus.WaitingForUser && Reason == PcWaitReason.Paused)
            text.Append("waiting_for_user: paused. token: ").Append(Token)
                .Append(". The user paused this app: do not retry it or reach it another way. Work in other apps; if nothing is left, call pc_wait once with until \"step\" and this token, and if it is still paused, end your turn with one short line.");
        else if (Status == PcStatus.WaitingForUser)
            text.Append("waiting_for_user: ").Append(Reason ?? PcWaitReason.Approval).Append(". token: ").Append(Token)
                .Append(". Do other steps, then call pc_wait with until \"step\" and this token.");
        else text.Append("ok");
        if (Window is not null) text.Append('\n').Append(Window.Render());
        if (Resumed is not null) text.Append('\n').Append(Resumed.Render());
        if (!string.IsNullOrEmpty(Body)) text.Append('\n').Append(Body.TrimEnd('\n'));
        return text.ToString();
    }

    private static string Sentence(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }
}
