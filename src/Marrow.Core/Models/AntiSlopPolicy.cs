namespace Marrow.Core.Models;

/// <summary>
/// Port of DroppyCode/Core/Models/AntiSlopPolicy.swift (task T-M2-001).
///
/// The two bodies are byte-for-byte copies of the Swift multi-line string literals. The escaped
/// U+2019 and \n sequences are used deliberately so no editor can silently turn the RIGHT SINGLE
/// QUOTATION MARK into an ASCII apostrophe or the newlines into CRLF.
/// </summary>
public static class AntiSlopPolicy
{
    public const string EnabledInstructions =
        "[Droppy Code anti-slop guidance]\n" +
        "Give the shortest complete answer, leading with the result. Use plain, natural language. Skip filler, hype, stock openings, repeated summaries and unsolicited closers. Preserve requested detail, facts, uncertainty, blockers and required validation.\n" +
        "Make focused edits using existing patterns. Add comments only for non-obvious reasons, constraints or pitfalls; omit banners, obvious narration and speculative TODOs. Preserve useful documentation. Avoid unrelated cleanup, dependencies and abstractions.\n" +
        "For UI work, follow the product\u2019s components, typography and colors. Prefer purposeful structure and real content; preserve accessibility, responsive layouts and visible loading, empty and error states.\n" +
        "Reuse evidence, read relevant sections and batch independent lookups. Avoid repeated scans, polling and echoed logs. Perform required checks; brevity never excuses incomplete work. Apply this guidance to delegated work too. Report changes, affected files and material limits once. User requests and project requirements take precedence.";

    // Resumed conversations may still contain the enabled guidance.
    public const string DisabledInstructions =
        "[Droppy Code anti-slop guidance: off]\n" +
        "Disregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements.";

    public static string Instructions(bool enabled) =>
        enabled ? EnabledInstructions : DisabledInstructions;
}
