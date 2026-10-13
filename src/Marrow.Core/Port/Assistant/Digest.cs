// PORT-ONLY: Dextra (DESIGN.md rev 5, 6, canvas "Morning digest"; unit AS-11; ED-NEW-dextra-as11-1). No Swift twin.
// A composed digest: its header, its sections in the order of 6, the Suggestions lane, the seal of the last day's activity log, and the line it
// ends on. Pure data. The card draws it, the voice reads <see cref="Spoken"/>, the store keeps it.
using System.Globalization;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>The parts of the digest in the order 6 gives them (the Suggestions lane follows them).</summary>
public enum DigestPart { NeedsYou, Finished, Running, Usage, Duties, Today, Unusual, Answered, Asturs }

/// <summary>One thing a section lists: its words, where it leads, and a plain note after it ("(reviewed: passed)").</summary>
public sealed record DigestItem(string Text, DigestLink Link)
{
    public string Note { get; init; } = "";

    /// <summary>The words and the note, as one run of text.</summary>
    public string Full => Note.Length == 0 ? Text : Text + " " + Note;
}

/// <summary>
/// One section: a label ("Needs you:"), a lead ("5."), and the things it lists. An inline section runs its items on after the lead,
/// separated by semicolons ("Needs you: 5. a send for Store; a reserve check"); the others list one sentence per line.
/// </summary>
public sealed record DigestSection(DigestPart Part, string Label, string Lead, IReadOnlyList<DigestItem> Items, bool Inline)
{
    /// <summary>The section as plain text, the way it reads aloud and the way a screen reader gets it.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder(Label);
            if (Lead.Length > 0) text.Append(' ').Append(Lead);
            if (Items.Count == 0) return text.ToString();
            text.Append(' ').Append(string.Join(Inline ? "; " : " ", Items.Select(i => i.Full)));
            return text.ToString();
        }
    }
}

public sealed record Digest(string Id, DigestKind Kind, DateTimeOffset At, string Header, IReadOnlyList<DigestSection> Sections)
{
    /// <summary>The last line of every digest (6: it never scrolls on and invites no engagement).</summary>
    public const string EndLine = "That's everything for today.";

    /// <summary>The suggestions the lane offered when this digest was composed. Their live state (open, accepted, dismissed) lives in the ledger.</summary>
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];

    /// <summary>"Suggestions · 2 of at most 3 today"; empty when the lane has nothing to offer.</summary>
    public string LaneTitle { get; init; } = "";

    public DigestSeal? Seal { get; init; }

    /// <summary>A few sentences for the voice to read aloud on request (local voices only).</summary>
    public string Spoken { get; init; } = "";

    /// <summary>What the digest was made from that nth cannot vouch for (12.4: a digest paragraph carries its taint).</summary>
    public Taint Taint { get; init; } = Taint.Clean;

    public DateTimeOffset Since { get; init; }

    /// <summary>The seal as it is printed: the day and the first 16 hex digits, grouped by four.</summary>
    public string? SealLine => Seal is null ? null : SealWords(Seal);

    /// <summary>The whole digest as plain text, in the order it is drawn, ending on <see cref="EndLine"/>.</summary>
    public string Text
    {
        get
        {
            var lines = new List<string> { Header };
            lines.AddRange(Sections.Select(s => s.Text));
            if (Suggestions.Count > 0)
            {
                lines.Add(LaneTitle);
                lines.AddRange(Suggestions.Select(s => s.Line + " " + s.Cost));
            }
            if (SealLine is { } seal) lines.Add(seal);
            lines.Add(EndLine);
            return string.Join("\n", lines);
        }
    }

    public static string SealWords(DigestSeal seal)
    {
        var hex = seal.Hmac.Length >= 16 ? seal.Hmac[..16] : seal.Hmac;
        var groups = Enumerable.Range(0, (hex.Length + 3) / 4).Select(i => hex.Substring(i * 4, Math.Min(4, hex.Length - i * 4)));
        return string.Create(CultureInfo.InvariantCulture, $"Activity log {seal.Day:d MMM}: seal {string.Join(' ', groups)}");
    }
}
