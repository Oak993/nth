// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-2: what the Activity view holds of one audit line, and how a hostile line is made safe to show.
using System.Globalization;
using System.Text;

namespace Marrow.Services.Assistant;

/// <summary>What one line of the audit log is, as the Activity view sees it.</summary>
public enum ActivityRowKind
{
    /// <summary>An action an assistant, an agent or the owner's tools took (12.5).</summary>
    Action,

    /// <summary>A record the log wrote about itself: a line cut off by a crash, a key replaced, a cut log.</summary>
    Record,

    /// <summary>A line that is not a sealed audit line (damaged, planted, too long, cut off): shown, never hidden.</summary>
    Unreadable,
}

/// <summary>How far a line is vouched for by the chain check that ran when the view opened.</summary>
public enum ActivityTrust
{
    /// <summary>The check passed this line: its number, its previous HMAC and its keyed HMAC are right.</summary>
    Verified,

    /// <summary>The chain fails at this line or before it, or the log as a whole does not verify.</summary>
    NotVerified,

    /// <summary>No check covers this line: it was written after the check read the files, the check could not run, or the line is a cut piece the log repaired.</summary>
    Unchecked,
}

/// <summary>One named input of an action, as the log kept it (12.5: typed text is its length only, a secret is masked).</summary>
public sealed record ActivityInput(string Name, AuditInputKind Kind, string Text = "", double Number = 0, int TypedLength = 0, bool Flag = false)
{
    /// <summary>The value in words: the masked text, "typed 148 characters", "[secret]", a number, or yes and no.</summary>
    public string Display => Kind switch
    {
        AuditInputKind.Typed => TypedLength == 1 ? "typed 1 character" : $"typed {TypedLength.ToString(CultureInfo.InvariantCulture)} characters",
        AuditInputKind.Secret => AuditRedactor.Mask,
        AuditInputKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        AuditInputKind.Flag => Flag ? "yes" : "no",
        _ => Text,
    };
}

/// <summary>
/// One line of the audit log as the Activity view holds it (12.5): every field, read back with the strictness the chain
/// check uses (a repeated property name is a damaged line) and made safe to show (<see cref="ActivityText.Safe"/>). The
/// log is not parsed again to filter or sort: rows are the unit the filter, the lineage and both exports work on.
/// </summary>
public sealed record ActivityRow
{
    /// <summary>The day file, the line's number in it (from 1) and its number in the chain.</summary>
    public required AuditLineRef At { get; init; }

    public ActivityRowKind Kind { get; init; }

    /// <summary>The local day the line was written, from its day file's name (the digest prints each day's final HMAC by the same day).</summary>
    public DateOnly Day { get; init; }

    /// <summary>When the line was written (UTC). Default for an unreadable line.</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>The log's own record kind (<c>tornLine</c>, <c>keyReplaced</c>, <c>logCut</c>), or null for an action.</summary>
    public string? Event { get; init; }

    public string Assistant { get; init; } = "";

    public string? Mission { get; init; }

    /// <summary>The agent's chat thread.</summary>
    public string? Agent { get; init; }

    public string Tool { get; init; } = "";

    public string Class { get; init; } = "";

    public string? Target { get; init; }

    public IReadOnlyList<ActivityInput> Inputs { get; init; } = [];

    /// <summary>How many inputs the line did not keep (the writer bounds a line).</summary>
    public int InputsNotLogged { get; init; }

    public string? Result { get; init; }

    public decimal? CostUsd { get; init; }

    public long? Tokens { get; init; }

    /// <summary>The grant or decision id that allowed the action (12.2: every audit entry that used a grant names it).</summary>
    public string? AllowedBy { get; init; }

    /// <summary>
    /// For a record, the words that say what it means; for an unreadable line, why it cannot be read and what is there
    /// (masked and cut short).
    /// </summary>
    public string? Note { get; init; }

    /// <summary>The previous line's HMAC this line names (64 hex characters; empty for an unreadable line).</summary>
    public string Prev { get; init; } = "";

    /// <summary>This line's HMAC (empty for an unreadable line).</summary>
    public string Hmac { get; init; } = "";

    public ActivityTrust Trust { get; init; }

    /// <summary>The line's number in the chain, or 0 for a line that has none.</summary>
    public long Sequence => At.Sequence ?? 0;
}

/// <summary>
/// Makes text from the log safe to show. The log is written by one writer, but its text comes from agents and tools (a
/// file path, a command, a reply), so a line may carry control characters and the invisible marks that reorder text
/// (a right-to-left override in "evil‮fdp.exe" shows a different name than the file has). Each such character is
/// shown as its escape, <c>‮</c>, so what the owner reads is what the line holds.
/// </summary>
public static class ActivityText
{
    /// <summary>The text with every control, format, line-separator and unpaired-surrogate character shown as its escape. A newline, a carriage return and a tab stay.</summary>
    public static string Safe(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var at = FirstUnsafe(text);
        if (at < 0) return text;
        var builder = new StringBuilder(text.Length + 16);
        builder.Append(text, 0, at);
        for (var i = at; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
                continue;
            }
            if (NeedsEscape(c) || char.IsSurrogate(c)) builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>The text on one line, for a table cell: line breaks and tabs become one space each and the text is cut at <paramref name="max"/> characters with an ellipsis.</summary>
    public static string OneLine(string? text, int max = 200)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, max + 1));
        foreach (var c in text)
        {
            if (builder.Length >= max)
            {
                builder.Append('…');
                break;
            }
            builder.Append(c is '\n' or '\r' or '\t' ? ' ' : c);
        }
        return builder.ToString();
    }

    private static int FirstUnsafe(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }
            if (NeedsEscape(c) || char.IsSurrogate(c)) return i;
        }
        return -1;
    }

    private static bool NeedsEscape(char c)
    {
        if (c is '\n' or '\r' or '\t') return false;
        if (c < 0x20 || (c >= 0x7F && c <= 0x9F)) return true;
        // Format characters (zero-width marks, the bidirectional overrides, isolates and marks, the byte order mark), the
        // line and paragraph separators, and the private-use area: nothing the owner can read.
        return CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
    }
}
