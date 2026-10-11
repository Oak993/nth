// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.1, 2.2). The result envelope every PC tool returns: a first
// line "ok" or "error: <code>: <reason>. <one recovery step>.", a window line, then a diff, a tree or nothing; an optional
// JPEG goes in its own MCP image part. Error codes are stable wire names.
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
    UserTookOver,
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
        PcErrorCode.UserTookOver => "user_took_over",
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
        PcErrorCode.OccludedPoint => "Call pc_focus_window, then pc_observe and try again",
        PcErrorCode.WindowGone => "Call pc_list_apps to find the window again",
        PcErrorCode.StoppedByUser => "Stop using the PC and tell the user you stopped",
        PcErrorCode.UserTookOver => "Wait for the user to finish, then ask before you continue",
        PcErrorCode.NeedsConfirmation => "Ask the user in chat before you try this again",
        PcErrorCode.Timeout => "Call pc_observe to see the current state",
        PcErrorCode.BadArgs => "Fix the arguments and call again",
        PcErrorCode.Disabled => "Pick an enabled element or change the state first",
        PcErrorCode.OwnWindow => "nth's own windows are the user's to use; pick another window",
        PcErrorCode.OffScreen => "Use a point inside the window's screenshot",
        PcErrorCode.Busy => "Wait for the running PC action to finish",
        PcErrorCode.PcOff => "Ask the user to turn on PC control in nth's Settings",
        PcErrorCode.Unattended => "Only a chat the user is watching can use the PC",
        PcErrorCode.DeniedKey => "Use another key or ask the user to press it",
        PcErrorCode.CaptureFailed => "Call pc_observe with screenshot no and use the tree",
        PcErrorCode.NotSupported => "Use another tool or a point click",
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

    internal static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>A JPEG for the model, with its pixel size.</summary>
public sealed record PcImage(byte[] Jpeg, int Width, int Height, string ObsId);

/// <summary>One tool's result.</summary>
public sealed record PcResult
{
    public PcErrorCode? Error { get; init; }
    public string? Reason { get; init; }
    public PcWindowLine? Window { get; init; }
    public string? Body { get; init; }
    public PcImage? Image { get; init; }

    public bool Ok => Error is null;

    public static PcResult Success(PcWindowLine? window = null, string? body = null, PcImage? image = null) => new() { Window = window, Body = body, Image = image };

    public static PcResult Fail(PcErrorCode code, string reason, PcWindowLine? window = null) => new() { Error = code, Reason = reason, Window = window };

    /// <summary>The text part.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        if (Error is { } code)
            text.Append("error: ").Append(PcErrorCodes.Wire(code)).Append(": ").Append(Sentence(Reason ?? "failed")).Append(' ').Append(PcErrorCodes.Recovery(code)).Append('.');
        else text.Append("ok");
        if (Window is not null) text.Append('\n').Append(Window.Render());
        if (!string.IsNullOrEmpty(Body)) text.Append('\n').Append(Body.TrimEnd('\n'));
        return text.ToString();
    }

    private static string Sentence(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }
}
