// PORT-ONLY: Tactus, the chat's side of pill v2 (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1 "Resume after the turn ended",
// 10.2, 10.4 "Resolution", 10.7). The words the chat shows for a pause, a resume, a stop and an answer ("Paused Excel (from
// the pill)", "Resumed (Ctrl+Alt+Space)", "Stopped (Esc pressed 3 times)", "You allowed Outlook for this chat (from the
// pill)"), the strip's line, and the rule for a resume after the turn ended: nth sends the fixed message "Tactus was
// resumed in Excel. Continue the task." into that chat, shown as a chip ("Resumed from the pill"), only when all four
// hold: the setting is on, the turn ended under 10 minutes ago, the user has not written in that chat since, and that turn
// is still the chat's latest. Otherwise the chat says "Resumed. Ask to continue.", so an old task never restarts on its own.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>What a resume does in its chat.</summary>
public enum PcResumeOutcome
{
    /// <summary>The turn still runs: its held steps go on, nothing is sent; the chat shows the resume row.</summary>
    None,

    /// <summary>All four conditions hold: the fixed message goes to the chat as a chip.</summary>
    Continue,

    /// <summary>The chat says "Resumed. Ask to continue." and nothing is sent.</summary>
    AskToContinue,
}

/// <summary>The chat's last turn as the resume rule reads it.</summary>
/// <param name="EndedAtMs">When it ended (the feed's clock); null while it runs.</param>
/// <param name="Latest">It is still the chat's latest turn.</param>
/// <param name="UserWroteSince">The user wrote in that chat after it ended.</param>
public sealed record PcTurnFacts(long? EndedAtMs, bool Latest, bool UserWroteSince);

public static class PcChatMirror
{
    /// <summary>A resume continues the chat only within this time after the turn ended.</summary>
    public const long ContinueWindowMs = 10 * 60 * 1000;

    public const string AskToContinueLine = "Resumed. Ask to continue.";

    /// <summary>The fixed message a resume sends into the chat.</summary>
    public static string ContinueMessage(string app) => $"Tactus was resumed in {app}. Continue the task.";

    /// <summary>The four conditions (DESIGN.md 10.1); a running turn needs no message.</summary>
    public static PcResumeOutcome Resume(bool continuesChat, PcTurnFacts turn, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (turn.EndedAtMs is not { } ended) return PcResumeOutcome.None;
        var all = continuesChat && nowMs - ended < ContinueWindowMs && !turn.UserWroteSince && turn.Latest;
        return all ? PcResumeOutcome.Continue : PcResumeOutcome.AskToContinue;
    }

    /// <summary>The chip the sent message shows instead of its text: "Resumed from the pill", "with Ctrl+Alt+Space".</summary>
    public static string ResumedChip(PcPauseSource source, string? chord) => "Resumed " + From(source, chord);

    /// <summary>"Paused Excel (from the pill)".</summary>
    public static string PausedRow(string app, PcPauseSource source, string? chord) => $"Paused {app} ({Said(source, chord)})";

    /// <summary>"Resumed (Ctrl+Alt+Space)".</summary>
    public static string ResumedRow(PcPauseSource source, string? chord) => $"Resumed ({Said(source, chord)})";

    /// <summary>"Stopped (Esc pressed 3 times)": the path in the chat's own words.</summary>
    public static string StoppedRow(PcStopPath path, string? stopChord, string? app = null) => path switch
    {
        PcStopPath.EscapeThreeTimes => "Stopped (Esc pressed 3 times)",
        PcStopPath.StopKey when stopChord is { Length: > 0 } => $"Stopped ({stopChord})",
        PcStopPath.StopKey => "Stopped (the stop key)",
        PcStopPath.PillRow when app is { Length: > 0 } => $"Stopped {app} (from the pill)",
        PcStopPath.StripRow when app is { Length: > 0 } => $"Stopped {app} (from the chat)",
        PcStopPath.PillRow or PcStopPath.PillStop => "Stopped (from the pill)",
        PcStopPath.StripRow or PcStopPath.ChatStop => "Stopped (from the chat)",
        PcStopPath.Tray => "Stopped (from the tray)",
        PcStopPath.MasterSwitch => "Stopped (Tactus turned off in Settings)",
        _ => "Stopped",
    };

    /// <summary>
    /// The answer row of an ask (DESIGN.md 10.4 "Resolution"): "You allowed Outlook for this chat (from the pill)", "You
    /// answered from the pill: Use the March file", "You cancelled Send in Outlook (from the pill)". An answer in the chat
    /// reads the same without the source. A secret answer never shows; a provider approval keeps its own chat row (null).
    /// </summary>
    public static string? AnswerRow(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var pill = source == PcAnswerSource.Pill;
        var tail = pill ? " (from the pill)" : "";
        var app = ask.AppName is { Length: > 0 } name ? name : "the app";
        switch (ask.Kind)
        {
            case PcAskKind.Approval:
                return (answer is PcApprovalReply reply ? reply.Approval : answer) switch
                {
                    PcApproval.Chat => $"You allowed {app} for this chat{tail}",
                    PcApproval.Always => $"You always allowed {app}{tail}",
                    PcApproval.Denied => $"You did not allow {app}{tail}",
                    _ => null,
                };
            case PcAskKind.Confirm:
                var confirmed = answer is PcConfirmReply confirm ? confirm.Confirmed : answer is true;
                var verb = ask.Payload is PcConfirmContent content ? content.Verb : "the action";
                return confirmed ? $"You allowed {verb} in {app}{tail}" : $"You cancelled {verb} in {app}{tail}";
            case PcAskKind.ShowWindow:
                var window = ask.Payload is PcShowAsk show && show.WindowTitle.Length > 0 ? "\u201C" + show.WindowTitle + "\u201D" : app;
                return answer is PcShowAnswer.Show ? $"You showed {window}{tail}" : $"You kept {window} hidden{tail}";
            case PcAskKind.Question:
            case PcAskKind.ProviderQuestion:
                var secret = ask.Payload is PcProviderAsk { Secret: true };
                var said = answer is PcAnswer typed ? typed.Text ?? typed.Choice : null;
                if (secret || said is not { Length: > 0 }) return pill ? "You answered from the pill" : "You answered in the chat";
                return pill ? "You answered from the pill: " + said : "You answered in the chat: " + said;
            default:
                return null;
        }
    }

    /// <summary>"You dismissed the question (from the pill)": only the pill's card has Dismiss.</summary>
    public const string DismissedRow = "You dismissed the question (from the pill)";

    /// <summary>
    /// The strip's line (DESIGN.md 10.7): "Tactus. Using Excel and Outlook", "Tactus. Using Outlook. Excel paused",
    /// "Tactus. Excel pausing".
    /// </summary>
    public static string StripLine(IReadOnlyList<PcStripApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var parts = new List<string>();
        var working = apps.Where(a => a.State == PcStripAppState.Working).Select(a => a.Name).ToList();
        var pausing = apps.Where(a => a.State == PcStripAppState.Pausing).Select(a => a.Name).ToList();
        var paused = apps.Where(a => a.State == PcStripAppState.Paused).Select(a => a.Name).ToList();
        if (working.Count > 0) parts.Add("Using " + And(working));
        if (pausing.Count > 0) parts.Add(And(pausing) + " pausing");
        if (paused.Count > 0) parts.Add(And(paused) + " paused");
        return "Tactus. " + string.Join(". ", parts);
    }

    /// <summary>"Excel", "Excel and Outlook", "Excel, Outlook and Word".</summary>
    public static string And(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    /// <summary>The name an app key reads as until the app's own name is known: "exe:c:\office\excel.exe" reads "Excel".</summary>
    public static string NameOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var bare = key.StartsWith("exe:", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(key[4..])
            : key.StartsWith("aumid:", StringComparison.Ordinal) ? key[6..].Split('!')[^1]
            : key;
        return bare.Length == 0 ? key : char.ToUpperInvariant(bare[0]) + bare[1..];
    }

    // "from the pill", "from the chat", "with Ctrl+Alt+Space", "from the tray"
    private static string From(PcPauseSource source, string? chord) => source switch
    {
        PcPauseSource.Chord => chord is { Length: > 0 } ? "with " + chord : "with the pause key",
        PcPauseSource.Chat => "from the chat",
        PcPauseSource.Tray => "from the tray",
        _ => "from the pill",
    };

    // inside the row's parentheses a chord stands alone: "Resumed (Ctrl+Alt+Space)"
    private static string Said(PcPauseSource source, string? chord) =>
        source == PcPauseSource.Chord && chord is { Length: > 0 } ? chord : From(source, chord);
}
