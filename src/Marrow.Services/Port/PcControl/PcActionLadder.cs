// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.6, 2.8,
// critique C7, C16). The action ladder. Every input is planned as an ordered list of ways to do it, highest rung first:
// R1 the element's own UI Automation pattern, R2 a message to one child window, R4 a borrow of the real pointer or focus
// (not built yet: it answers needs_real_input with its reason). Each rung is used only where the framework gate allows it
// for the app's framework; "probe" ways run with the self-check, and a pass promotes them for that exe. What Tactus learns
// is kept per exe, version, rung, method and control type (PcCapabilities): a way that moved the user's foreground or
// cursor is never used again for 7 days, and one that moved focus is not used while the user is active in that window.
// The engine runs the plan: the first way, then (after a failed check) the next way once, and reports the rung used.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public enum PcInputKind
{
    Click,
    Type,
    Key,
    Scroll,
    Drag,

    /// <summary>A pc_action or pc_set_value command on an element (its pattern, or a message for stock controls).</summary>
    Command,
}

/// <summary>One input, already resolved and checked by the engine: the live element and the fresh tree, or a screen point.</summary>
public sealed record PcInputRequest(PcInputKind Kind, PcWindowInfo Window)
{
    /// <summary>
    /// Between two runs of a typed text (characters typed so far, the total): null goes on; a reason ends the typing as
    /// expect_failed (a field that changed while the app was paused, DESIGN.md 10.1).
    /// </summary>
    public Func<int, int, CancellationToken, Task<string?>>? Boundary { get; init; }

    public PcTree? Tree { get; init; }
    public PcUiNode? Element { get; init; }
    public PcPoint? Point { get; init; }
    public PcMouseButton Button { get; init; }
    public int Count { get; init; } = 1;
    public IReadOnlyList<ushort> Modifiers { get; init; } = [];
    public string? Text { get; init; }

    /// <summary>pc_type mode: insert at the caret (true) or replace the whole text.</summary>
    public bool Insert { get; init; } = true;
    public KeyChord? Chord { get; init; }
    public string? KeysText { get; init; }
    public int Repeat { get; init; } = 1;
    public PcScrollDirection Direction { get; init; }
    public PcScrollUnit Unit { get; init; }
    public double Amount { get; init; } = 1;
    public IReadOnlyList<PcPoint> Path { get; init; } = [];
    public int HoldMs { get; init; }

    /// <summary>For <see cref="PcInputKind.Command"/>: the element command to run.</summary>
    public PcElementCommand? Command { get; init; }

    /// <summary>The user is active in this window now (focus-moving ways wait like a borrow; DESIGN.md 2.9).</summary>
    public bool UserActive { get; init; }

    /// <summary>The window is the user's foreground window now (ways that may activate a window run only then).</summary>
    public bool InFront { get; init; }

    /// <summary>A physical modifier key is down: posted keys and characters are never used then (DESIGN.md 2.8).</summary>
    public bool ModifiersDown { get; init; }
}

/// <summary>
/// One way to do an input: its rung, a method name for learning and the audit ("invoke", "bm_click", "em_replacesel"), the
/// call itself, and what the check looks at afterwards.
/// </summary>
public sealed record PcRungPlan(PcRung Rung, string Method, Func<CancellationToken, Task<PcUiaOutcome>> Run)
{
    /// <summary>The element command this way runs (drives the implicit check); null for messages that are not commands.</summary>
    public PcElementCommand? Command { get; init; }

    /// <summary>The element whose state the check reads (it may differ from the request's: an accelerator's command).</summary>
    public PcUiNode? Target { get; init; }

    /// <summary>A menu item's text when the way is a menu command (the guard reads it, critique C7).</summary>
    public string? MenuText { get; init; }

    /// <summary>"pressed button "Save"" for the result.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Whether this way writes text into a control (the yield rule of DESIGN.md 2.9 applies).</summary>
    public bool Writes { get; init; }

    /// <summary>The key what Tactus learns about this way is kept under; null for ways that need no learning.</summary>
    public PcCapabilityKey? Key { get; init; }

    /// <summary>True when the framework gate marks this way "probe" and it has not passed its self-check for this exe yet.</summary>
    public bool Probe { get; init; }
}

/// <summary>What one rung offers for a request: its ways, or a refusal that ends the ladder (disabled, a password field).</summary>
public sealed record PcRungOffer(IReadOnlyList<PcRungPlan> Plans, PcErrorCode? Refusal = null, string? Reason = null)
{
    public static PcRungOffer None { get; } = new([]);
    public static PcRungOffer Refuse(PcErrorCode code, string reason) => new([], code, reason);
}

/// <summary>The ladder's plan for one input: its ways in order, and what to say when none of them can (or all fail).</summary>
public sealed record PcLadderPlan(IReadOnlyList<PcRungPlan> Plans, PcErrorCode? Refusal, string? Reason)
{
    public bool Refused => Plans.Count == 0;
}

public interface IPcInputRung
{
    PcRung Rung { get; }

    /// <summary>The ways this rung can do the request, in its order of preference, or a refusal.</summary>
    PcRungOffer Offer(PcInputRequest request, PcFramework framework);
}

/// <summary>The UI framework that draws an element (DESIGN.md 2.6 framework gate).</summary>
public enum PcFramework
{
    Unknown,
    Win32,
    WinForms,
    Wpf,
    Xaml,
    Chromium,
    Office,
    Java,
    Qt,
}

/// <summary>Whether a rung is used for a framework: never, after a passing self-check per exe, or always.</summary>
public enum PcGate
{
    No,
    Probe,
    Yes,
}

/// <summary>What an R2 message does, for the framework gate.</summary>
public enum PcMessageKind
{
    Click,
    Text,
    Keys,
}

/// <summary>The framework gate (DESIGN.md 2.6, initial conservative table).</summary>
public static class PcFrameworkGate
{
    private static readonly HashSet<string> officeExes = new(StringComparer.OrdinalIgnoreCase) { "winword.exe", "excel.exe", "outlook.exe", "powerpnt.exe", "onenote.exe", "msaccess.exe" };

    /// <summary>The framework of an element (its UIA FrameworkId first, then the window root's, the class and the exe).</summary>
    public static PcFramework Of(PcWindowInfo window, PcUiNode? element, PcTree? tree)
    {
        if (officeExes.Contains(window.ExeName)) return PcFramework.Office;
        if (window.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)) return PcFramework.Chromium;
        if (window.ClassName.StartsWith("SunAwt", StringComparison.Ordinal)) return PcFramework.Java;
        var id = element?.Framework ?? (tree is { } t && t.Elements.TryGetValue(1, out var root) ? root.Framework : null);
        var byId = id?.Trim() switch
        {
            "Win32" => PcFramework.Win32,
            "WinForm" => PcFramework.WinForms,
            "WPF" => PcFramework.Wpf,
            "XAML" or "DirectUI" => PcFramework.Xaml,
            "Chrome" => PcFramework.Chromium,
            "Qt" => PcFramework.Qt,
            "Java" or "JAVA" => PcFramework.Java,
            _ => PcFramework.Unknown,
        };
        if (byId == PcFramework.Win32 && (element?.ClassName ?? window.ClassName).StartsWith("WindowsForms10.", StringComparison.Ordinal)) return PcFramework.WinForms;
        if (byId != PcFramework.Unknown) return byId;
        if (window.ClassName.StartsWith("WindowsForms10.", StringComparison.Ordinal)) return PcFramework.WinForms;
        if (window.ClassName.StartsWith("Qt", StringComparison.Ordinal)) return PcFramework.Qt;
        return PcFramework.Unknown;
    }

    /// <summary>Whether R2 messages of a kind are used for a framework.</summary>
    public static PcGate Messages(PcFramework framework, PcMessageKind kind) => (framework, kind) switch
    {
        (PcFramework.Win32, _) => PcGate.Yes,
        (PcFramework.WinForms, PcMessageKind.Keys) => PcGate.Probe,
        (PcFramework.WinForms, _) => PcGate.Yes,
        (PcFramework.Chromium, PcMessageKind.Click) => PcGate.Probe,
        (PcFramework.Qt, _) => PcGate.Probe,
        _ => PcGate.No,
    };
}

/// <summary>Where a learned result is kept: exe, version, rung, method and control type (critique C16).</summary>
public sealed record PcCapabilityKey(string Exe, string Version, PcRung Rung, string Method, string ControlType);

/// <summary>What Tactus learned about one way in one app.</summary>
public enum PcLearned
{
    Unknown,
    Passed,

    /// <summary>It moved focus inside the app: used only while the user is not active in that window.</summary>
    FocusMoving,

    /// <summary>It moved the user's foreground or cursor, or failed its self-check: not used for 7 days.</summary>
    Refused,
}

/// <summary>
/// The capability store (DESIGN.md 2.6 self-check). Kept in memory; the app saves and loads it with <see cref="Export"/>
/// and <see cref="Import"/> in nth's settings and the user can clear it ("forget learned behaviour").
/// </summary>
public sealed class PcCapabilities(Func<DateTimeOffset> now)
{
    /// <summary>How long a refused way stays refused.</summary>
    public static readonly TimeSpan RefusedFor = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<PcCapabilityKey, (PcLearned State, DateTimeOffset At)> learned = new();

    public PcLearned State(PcCapabilityKey key)
    {
        if (!learned.TryGetValue(key, out var entry)) return PcLearned.Unknown;
        if (entry.State == PcLearned.Refused && now() - entry.At >= RefusedFor)
        {
            learned.TryRemove(key, out _);
            return PcLearned.Unknown;
        }
        return entry.State;
    }

    /// <summary>The self-check passed: a probe way becomes a used way for this exe. Never promotes a refused or focus-moving way.</summary>
    public void Passed(PcCapabilityKey key)
    {
        var state = State(key);
        if (state is PcLearned.Unknown) learned[key] = (PcLearned.Passed, now());
    }

    public void FocusMoving(PcCapabilityKey key)
    {
        if (State(key) != PcLearned.Refused) learned[key] = (PcLearned.FocusMoving, now());
    }

    public void Refuse(PcCapabilityKey key) => learned[key] = (PcLearned.Refused, now());

    public void Clear() => learned.Clear();

    public string Export()
    {
        var items = new JsonArray();
        foreach (var (key, (state, at)) in learned)
        {
            items.Add(new JsonObject
            {
                ["exe"] = key.Exe,
                ["version"] = key.Version,
                ["rung"] = (int)key.Rung,
                ["method"] = key.Method,
                ["control"] = key.ControlType,
                ["state"] = state.ToString(),
                ["at"] = at.ToUnixTimeMilliseconds(),
            });
        }
        return items.ToJsonString();
    }

    /// <summary>Loads an export; anything malformed is skipped (learning starts over for it, which is safe).</summary>
    public void Import(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray items) return;
            foreach (var item in items.OfType<JsonObject>())
            {
                if (item["exe"]?.GetValue<string>() is not { } exe || item["method"]?.GetValue<string>() is not { } method) continue;
                if (!Enum.TryParse<PcLearned>(item["state"]?.GetValue<string>(), out var state)) continue;
                var key = new PcCapabilityKey(exe, item["version"]?.GetValue<string>() ?? "", (PcRung)(item["rung"]?.GetValue<int>() ?? 0), method, item["control"]?.GetValue<string>() ?? "");
                learned[key] = (state, DateTimeOffset.FromUnixTimeMilliseconds(item["at"]?.GetValue<long>() ?? 0));
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (FormatException) { }
    }
}

/// <summary>
/// Builds the plan for an input from the rungs in ladder order (R2 first for inserting text into a stock Win32 edit,
/// critique C31), keeping only the ways the framework gate and the capability store allow.
/// </summary>
public sealed class PcActionLadder(IReadOnlyList<IPcInputRung> rungs, PcCapabilities capabilities)
{
    public IReadOnlyList<IPcInputRung> Rungs { get; } = rungs;
    public PcCapabilities Capabilities { get; } = capabilities;

    public PcLadderPlan Plan(PcInputRequest request)
    {
        var framework = PcFrameworkGate.Of(request.Window, request.Element, request.Tree);
        var plans = new List<PcRungPlan>();
        PcErrorCode? refusal = null;
        string? reason = null;
        foreach (var rung in Order(request, framework))
        {
            var offer = rung.Offer(request, framework);
            foreach (var plan in offer.Plans)
            {
                if (plan.Key is not { } key)
                {
                    plans.Add(plan);
                    continue;
                }
                switch (Capabilities.State(key))
                {
                    case PcLearned.Refused:
                        reason ??= "the way Tactus tried before (" + plan.Method + ") took the user's focus or pointer in this app";
                        continue;
                    case PcLearned.FocusMoving when request.UserActive:
                        reason ??= plan.Method + " moves focus in this app, and the user is working in this window";
                        continue;
                    case PcLearned.Passed:
                        plans.Add(plan with { Probe = false });
                        continue;
                    default:
                        plans.Add(plan);
                        continue;
                }
            }
            if (offer.Refusal is { } code)
            {
                // A refusal that is not "needs the real input" (disabled, a password field, read-only, two commands) ends the
                // plan even when a rung before it offered ways: it says the input must not be done at all.
                if (plans.Count == 0 || code != PcErrorCode.NeedsRealInput)
                    return new PcLadderPlan([], code, reason is null || code != PcErrorCode.NeedsRealInput ? offer.Reason : offer.Reason + "; " + reason);
                refusal ??= code;
                reason = offer.Reason;
                break;
            }
        }
        if (plans.Count == 0) return new PcLadderPlan([], PcErrorCode.NeedsRealInput, reason ?? PcRealInputReason.Off);
        return new PcLadderPlan(plans, refusal, reason);
    }

    private IEnumerable<IPcInputRung> Order(PcInputRequest request, PcFramework framework)
    {
        var editInsert = request.Kind == PcInputKind.Type && request.Insert && request.Element is { NativeWindow: not 0 } element
            && PcMessageRung.IsEdit(element.ClassName) && framework is PcFramework.Win32 or PcFramework.WinForms;
        return editInsert ? Rungs.OrderBy(r => r.Rung == PcRung.R2 ? 0 : (int)r.Rung) : Rungs.OrderBy(r => (int)r.Rung);
    }
}
