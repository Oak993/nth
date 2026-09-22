namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/AntiSlopPolicy.swift (B2 1fbf81e), :1-19.
//
// Requirements: PR-STORE-129 (the enabled text), PR-STORE-130 (the disabled text), PR-HYDRA-055 (the text the Hydra
// head prompts end with). This file owns the two texts and the selector. The delivery points the records name
// (Hydra.swift claudeAgents and copilotAgents, ThreadRuntime.swift:1323-1334 and :1365-1367, and the Claude, Codex,
// Copilot and OpenAI-compatible session launches) are ported with those files' units and call Instructions(enabled:).
//
// Each Swift multi-line literal is written as the string it evaluates to (R-STR-9): the closing delimiter's
// indentation stripped, lines joined by LF, no trailing newline. The one non-ASCII character is an eight-digit escape.

// :1 enum AntiSlopPolicy (a caseless enum used as a namespace)
public static class AntiSlopPolicy
{
    // :2-8 static let enabledInstructions
    // DEVIATION OD-0012 F1: the first line (:3) is "[Marrow anti-slop guidance]"; upstream writes
    // "[Droppy Code anti-slop guidance]". Every other line is unchanged.
    public const string EnabledInstructions =
        "[Marrow anti-slop guidance]\n" + // :3
        "Give the shortest complete answer, leading with the result. Use plain, natural language. Skip filler, hype, stock openings, repeated summaries and unsolicited closers. Preserve requested detail, facts, uncertainty, blockers and required validation.\n" + // :4
        "Make focused edits using existing patterns. Add comments only for non-obvious reasons, constraints or pitfalls; omit banners, obvious narration and speculative TODOs. Preserve useful documentation. Avoid unrelated cleanup, dependencies and abstractions.\n" + // :5
        "For UI work, follow the product\U00002019s components, typography and colors. Prefer purposeful structure and real content; preserve accessibility, responsive layouts and visible loading, empty and error states.\n" + // :6, U+2019 RIGHT SINGLE QUOTATION MARK; US spelling "colors" as upstream
        "Reuse evidence, read relevant sections and batch independent lookups. Avoid repeated scans, polling and echoed logs. Perform required checks; brevity never excuses incomplete work. Apply this guidance to delegated work too. Report changes, affected files and material limits once. User requests and project requirements take precedence."; // :7

    // :10 Resumed conversations may still contain the enabled guidance.
    // :11-14 static let disabledInstructions
    // DEVIATION OD-0012 F1: the first line (:12) is "[Marrow anti-slop guidance: off]"; upstream writes
    // "[Droppy Code anti-slop guidance: off]". The second line is unchanged.
    public const string DisabledInstructions =
        "[Marrow anti-slop guidance: off]\n" + // :12
        "Disregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements."; // :13

    // :16-18 static func instructions(enabled: Bool) -> String. Always one of the two texts, never empty.
    public static string Instructions(bool enabled) =>
        enabled ? EnabledInstructions : DisabledInstructions; // :17
}
