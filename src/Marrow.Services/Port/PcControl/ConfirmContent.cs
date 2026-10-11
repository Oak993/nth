// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 4.3, 10.4). What a guarded action's confirm shows:
// "nth wants to click Send in Outlook." and the exact content by category (send, post, pay, delete, submit, install), one
// row per field. A field that could not be read shows "Could not read" and the first line says so; nothing is guessed.
// At the click the content is read again: if any row differs, nothing is sent, the changed rows are marked and the card
// asks again with the category's words ("The recipients changed while this waited. Check, then confirm.").
using System.Globalization;
using System.Text;

namespace Marrow.Services.Port.PcControl;

public enum PcConfirmCategory
{
    /// <summary>Mail, message, chat post.</summary>
    Send,

    /// <summary>Social post, blog, store listing.</summary>
    Post,

    /// <summary>Pay, buy, order, transfer.</summary>
    Pay,

    /// <summary>Delete, uninstall, delete account, sign out.</summary>
    Delete,

    /// <summary>A form's submit.</summary>
    Submit,

    Install,
}

/// <summary>One field of the confirm. A null value is a field that could not be read.</summary>
public sealed record PcConfirmRow(string Label, string? Value)
{
    public const string CouldNotRead = "Could not read";

    public bool Unreadable => Value is null;

    public string Shown => Value ?? CouldNotRead;
}

/// <summary>A guarded action's confirm content.</summary>
public sealed record PcConfirmContent(PcConfirmCategory Category, string Word, string Element, string App, IReadOnlyList<PcConfirmRow> Rows)
{
    public const int BodyChars = 200;
    public const string Dots = "••••••••";
    public const string None = "none";
    public const string Recipients = "To";
    public const string CcBcc = "Cc, Bcc";

    /// <summary>The category of a guarded word (PcPolicy.GuardedWord); "press" is a mail or chat app's unnamed button, which may be Send.</summary>
    public static PcConfirmCategory CategoryOf(string word) => word switch
    {
        "send" or "press" => PcConfirmCategory.Send,
        "post" or "publish" => PcConfirmCategory.Post,
        "pay" or "buy" or "purchase" or "order" or "transfer" => PcConfirmCategory.Pay,
        "delete" or "delete account" or "uninstall" or "sign out" => PcConfirmCategory.Delete,
        "install" => PcConfirmCategory.Install,
        _ => PcConfirmCategory.Submit,
    };

    /// <summary>"nth wants to click Send in Outlook." and, when a field could not be read, a second sentence that says so.</summary>
    public string FirstLine
    {
        get
        {
            var line = "nth wants to click " + Verb + " in " + App + ".";
            return Rows.Any(r => r.Unreadable) ? line + " Some of it could not be read." : line;
        }
    }

    /// <summary>The rows of a mail, message or chat post: Cc and Bcc share one row when both are empty.</summary>
    public static IReadOnlyList<PcConfirmRow> SendRows(string? from, string? to, string? cc, string? bcc, string? subject,
        IReadOnlyList<string>? attachments, string? body)
    {
        var rows = new List<PcConfirmRow> { new("From", from), new(Recipients, to) };
        if (cc is { Length: 0 } && bcc is { Length: 0 }) rows.Add(new(CcBcc, None));
        else
        {
            rows.Add(new("Cc", cc is { Length: 0 } ? None : cc));
            rows.Add(new("Bcc", bcc is { Length: 0 } ? None : bcc));
        }
        rows.Add(new("Subject", subject));
        rows.Add(new("Attachments", attachments is null ? null : attachments.Count == 0 ? None : string.Join(", ", attachments)));
        rows.Add(new("Message", body is null ? null : Start(body)));
        return rows;
    }

    /// <summary>The first 200 characters of a text, never splitting a surrogate pair, with an ellipsis when cut.</summary>
    public static string Start(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= BodyChars) return text;
        var cut = char.IsHighSurrogate(text[BodyChars - 1]) ? BodyChars - 1 : BodyChars;
        return text[..cut] + "…";
    }

    /// <summary>A payment method with only its last four digits ("Visa •••• 4242"); null when no number can be told apart.</summary>
    public static string? LastFour(string? method)
    {
        if (method is null) return null;
        var digits = new StringBuilder();
        var words = new StringBuilder();
        foreach (var c in method)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (char.IsLetter(c) || c == ' ') words.Append(c);
        }
        if (digits.Length < 4) return digits.Length == 0 ? method.Trim() : null;
        var name = string.Join(' ', words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var last = digits.ToString()[^4..];
        return (name.Length > 0 ? name + " " : "") + "•••• " + last;
    }

    /// <summary>A form field: a secret field shows dots, never its length or text.</summary>
    public static PcConfirmRow Field(string label, string? value, bool secret) => new(label, secret && value is not null ? Dots : value);

    /// <summary>The labels of the rows that differ in <paramref name="now"/> (changed, gone or new), in the order shown.</summary>
    public IReadOnlyList<string> Changes(PcConfirmContent now)
    {
        ArgumentNullException.ThrowIfNull(now);
        var before = Rows.ToDictionary(r => r.Label, r => r.Value, StringComparer.Ordinal);
        var changed = new List<string>();
        foreach (var row in now.Rows)
        {
            if (!before.Remove(row.Label, out var old) || !string.Equals(old, row.Value, StringComparison.Ordinal)) changed.Add(row.Label);
        }
        changed.AddRange(before.Keys);
        return changed;
    }

    /// <summary>What the card says when the content changed while the confirm waited, in the category's words.</summary>
    public string ChangedLine(IReadOnlyList<string> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var what = Category switch
        {
            PcConfirmCategory.Send when changed.Any(l => l is Recipients or "Cc" or "Bcc" or CcBcc) => "The recipients",
            PcConfirmCategory.Send => "The message",
            PcConfirmCategory.Post => "The post",
            PcConfirmCategory.Pay => "The payment",
            PcConfirmCategory.Delete => "What goes",
            PcConfirmCategory.Install => "What installs",
            _ => "The form",
        };
        return what + " changed while this waited. Check, then confirm.";
    }

    /// <summary>The word as the card's title and the too-late line show it ("Send").</summary>
    public string Verb => Element.Length > 0 ? Element : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Word);
}
