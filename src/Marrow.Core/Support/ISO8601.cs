using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/ISO8601.swift (B2 1fbf81e), :1-32.

/// <summary>
/// :8 <c>enum ISO8601</c>: ISO 8601 stamps as they arrive from provider CLIs and from GitLab. The two
/// Foundation styles (fractional, plain) are built once as static values (Port/SwiftDate.cs
/// FoundationISO8601), not per stamp.
/// </summary>
public static class ISO8601
{
    /// <summary>
    /// :18-21 <c>static func date(from text: String) -> Date?</c>: a stamp with or without fractional
    /// seconds; the fractional style is tried first, then the plain style, in that order; <c>nil</c> for
    /// anything else. Never throws (both branches are <c>try?</c>).
    /// </summary>
    public static DateTimeOffset? Date(string text) => FoundationISO8601.ParseFractional(text) ?? FoundationISO8601.ParsePlain(text);

    /// <summary>
    /// :23-26 <c>static func dateOnly(from text: String) -> Date?</c>: a bare date with no time
    /// (`2026-09-13`); <c>nil</c> for anything else.
    /// </summary>
    public static DateTimeOffset? DateOnly(string text) => FoundationISO8601.ParseDateOnly(text);

    /// <summary>
    /// :28-31 <c>static func string(from date: Date) -> String</c>: a stamp for a filename or a prompt,
    /// whole seconds by floor, UTC, `Z` suffix.
    /// </summary>
    public static string String(DateTimeOffset date) => FoundationISO8601.Format(date);
}
