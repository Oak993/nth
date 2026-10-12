// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-1, ED-NEW-pcctl-2, ED-NEW-pcctl-7,
// ED-NEW-pcctl-8; DESIGN.md 2.2, 2.10, 4.1, 4.3, 4.4, 4.6). The seams the engine asks people and the app through: the
// per-app approval (asked separately for seeing and for acting; a Hydra head is never offered "Always"), the
// guarded-action card, the "Show this window?" question on the pill, the stop key's registration (fail closed) and the
// audit log. The app binds the real ones (sheets, cards, RegisterHotKey); tests bind fakes.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>Who is asking: the chat thread, the app the card names, and the Hydra head when it is not the lead chat.</summary>
public sealed record PcAskSource(string Thread, string? App = null, string? Head = null)
{
    /// <summary>"Head 3 of Inbox triage in Outlook", or the app's name.</summary>
    public string Label => Head is { Length: > 0 } head ? head + (App is { Length: > 0 } a ? " in " + a : "") : App ?? "Tactus";
}

public interface IPcApprover
{
    /// <summary>A standing ("Always") grant for this app and capability, if the user made one; null when none. Never asked for heads.</summary>
    PcApproval? Standing(PcAppIdentity app, PcCapability capability);

    /// <summary>
    /// Asks the user (the sheet and the chat) and waits for the answer. <paramref name="allowAlways"/> is false for a Hydra
    /// head (critique C14): the sheet then offers "This chat" only. The engine stops waiting after its 25 s budget and the
    /// ask stays up until it is answered, withdrawn by a cancelled token, or two minutes pass (TimedOut).
    /// </summary>
    Task<PcApproval> Ask(PcAskSource source, PcAppIdentity app, string appName, PcCapability capability,
        bool risky, bool allowAlways, CancellationToken cancellationToken);

    /// <summary>
    /// What the user said with a denial of this chat's ask (the card's "Reply instead", or a See-only answer to a step that
    /// needed Use), once; null when nothing was said. It goes to the model in the not_approved result (DESIGN.md 10.4).
    /// </summary>
    string? Reason(string thread, PcAppIdentity app, PcCapability capability) => null;
}

/// <summary>What the user answered on the pill's "Show this window?" question (DESIGN.md 2.2, critique C15).</summary>
public enum PcShowAnswer
{
    Show,
    NotNow,
}

public interface IPcConfirmer
{
    /// <summary>The guarded-action card ("Tactus wants to press Send in Outlook."): true when the user allows it.</summary>
    Task<bool> Confirm(PcAskSource source, string word, string elementName, string appName, CancellationToken cancellationToken);

    /// <summary>The pill asks "Show Outlook?" with Show and Not now; the window is shown only after Show.</summary>
    Task<PcShowAnswer> Show(PcAskSource source, string appName, string windowTitle, CancellationToken cancellationToken);
}

/// <summary>
/// The stop key (DESIGN.md 4.4, critique C11): Arm registers the configured chord (Ctrl+Alt+Esc by default) while any
/// stream is active and answers whether it works. False means the chord is taken by another app, and no new stream starts
/// (fail closed). The app's RegisterHotKey binding is unit P5; tests bind a fake.
/// </summary>
public interface IPcStopKey
{
    bool Arm();

    /// <summary>The chord as the pill prints it ("Ctrl+Alt+Esc").</summary>
    string Chord { get; }
}

/// <summary>The audit log: one line per tool call.</summary>
public interface IPcAudit
{
    void Write(PcAuditEntry entry);
}

/// <summary>
/// One audit line: time, tool, app, window title, element name and role, result, duration, the rung used, and the
/// milliseconds of each phase (guard, approve, queue, resolve, act, settle, verify, observe, capture), so every call's
/// latency is on record (DESIGN.md 2.11 item 10).
/// </summary>
public sealed record PcAuditEntry(DateTimeOffset Time, string Tool, string? App, string? Window, string? Element, string? Role, string Result, long DurationMs)
{
    /// <summary>The typed text's length (the text itself only when the user turned on "Log typed text").</summary>
    public int? TextLength { get; init; }
    public string? Text { get; init; }
    public string? Rung { get; init; }
    public IReadOnlyList<KeyValuePair<string, long>> Steps { get; init; } = [];
}
