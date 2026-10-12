// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1 "Personal data out", 12.4 "Privacy shield on the way out"; unit AS-7;
// ED-NEW-dextra-as7-4). No Swift twin.
// The outward privacy check: a card for something that leaves the PC (send, post, a connector write, a booking) marks
// every piece of personal data and every secret in its text, and under any grant personal data goes only to recipients
// on the owner's list. A step whose text holds such data while a recipient is not on the list, or while nth cannot name
// who receives it, is blocked: its Allow becomes a card, and the card takes no approval until the recipient is listed.
// Pure: no I/O; every pattern has a match timeout and the text is read to a bounded length (text past it counts as
// unchecked, so it blocks too).
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a marked piece of an outward text is.</summary>
public enum PrivacyKind { Email, Phone, Address, Id, BirthDate, Card, Bank, Secret, Code, Unchecked }

/// <summary>One marked piece: which of the card's inputs it is in, where, and what it is.</summary>
public sealed record PrivacySpan(string Input, int Start, int Length, PrivacyKind Kind)
{
    /// <summary>A secret rather than personal data: a key, a password, a code, a card or a bank account.</summary>
    public bool IsSecret => Kind is PrivacyKind.Secret or PrivacyKind.Code or PrivacyKind.Card or PrivacyKind.Bank;
}

/// <summary>The check of one outward step: the marked pieces, who receives it, and who of them is not on the list.</summary>
public sealed record OutwardCheck(ValueList<PrivacySpan> Spans, ValueList<string> Recipients, ValueList<string> Unlisted)
{
    public static OutwardCheck None { get; } = new(ValueList<PrivacySpan>.Empty, ValueList<string>.Empty, ValueList<string>.Empty);

    /// <summary>The text holds personal data or a secret.</summary>
    public bool HasPersonalData => Spans.Count > 0;

    /// <summary>Blocked: personal data with a recipient not on the list, or with no recipient nth can name.</summary>
    public bool Blocked => HasPersonalData && (Recipients.Count == 0 || Unlisted.Count > 0);

    /// <summary>Why the card takes no approval, or null.</summary>
    public string? Refusal => !Blocked ? null
        : Recipients.Count == 0
            ? "It holds personal data, and nth cannot tell who receives it. Personal data goes only to recipients on your list."
            : "It holds personal data. Personal data goes only to recipients on your list: add " + string.Join(", ", Unlisted) + " first, or deny it.";
}

public static partial class OutwardPrivacy
{
    /// <summary>The most characters of one input that are read; more counts as unchecked.</summary>
    public const int ScanChars = 64_000;

    private const int TimeoutMs = 250;
    private const RegexOptions Flags = RegexOptions.CultureInvariant;

    /// <summary>The classes whose text leaves the PC towards someone (12.1).</summary>
    public static bool Applies(ActionClass actionClass) =>
        actionClass is ActionClass.Outward or ActionClass.PersonalDataOut or ActionClass.ConnectorWrite or ActionClass.Commitment;

    /// <summary>The fields of a tool's input that name who receives it.</summary>
    private static readonly HashSet<string> RecipientFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "cc", "bcc", "recipient", "recipients", "email", "emails", "address", "addresses", "channel", "channel_id", "channelid",
        "user", "user_id", "userid", "users", "chat_id", "chatid", "phone", "phone_number", "number", "attendees", "invitees",
        "participants", "guests", "reply_to", "replyto", "target", "destination", "conversation", "thread_ts_channel",
    };

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+", Flags, TimeoutMs)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?i)\b(?:phone|tel|telephone|mobile|cell|whatsapp|contact(?:\s+number)?|ph)\b\W{0,4}(?<value>\+?\d[\d\s().-]{6,18}\d)", Flags, TimeoutMs)]
    private static partial Regex LabelledPhone();

    [GeneratedRegex(@"(?<![\w+])\+\d{1,3}[\s.-]?\(?\d{1,5}\)?(?:[\s.-]?\d{2,5}){2,4}(?!\w)|(?<!\w)\(?\d{3}\)?[\s.-]\d{3}[\s.-]\d{4}(?!\w)|(?<!\w)03\d{2}[\s-]?\d{7}(?!\w)|(?<![\w-])[6-9]\d{9}(?![\w-])", Flags, TimeoutMs)]
    private static partial Regex Phone();

    [GeneratedRegex(@"(?i)\b\d{1,6}[A-Za-z]?,?\s+(?:[A-Za-z0-9.'-]+\s+){0,4}(?:street|st|avenue|ave|road|rd|boulevard|blvd|lane|ln|drive|dr|court|ct|way|place|pl|terrace|crescent|highway|hwy|sector|block|colony|nagar|marg|gali|chowk|bazaar|mohalla)\b\.?", Flags, TimeoutMs)]
    private static partial Regex StreetAddress();

    [GeneratedRegex(@"(?i)\b(?:home\s+|postal\s+|mailing\s+|street\s+|shipping\s+|billing\s+)?address\s*[:=]\s*(?<value>[^\n]{6,120})", Flags, TimeoutMs)]
    private static partial Regex LabelledAddress();

    [GeneratedRegex(@"(?<![\w-])\d{4}\s\d{4}\s\d{4}(?![\w-])|(?<![\w-])\d{5}-\d{7}-\d(?![\w-])|\b[A-Z]{5}\d{4}[A-Z]\b|(?i)\b(?:passport|cnic|aadhaar|aadhar|pan|nic|nin|national\s+insurance)(?:\s+(?:no\.?|number|#))?\W{0,3}(?<value>(?=[A-Z0-9-]*\d)[A-Z0-9][A-Z0-9-]{5,19})\b", Flags, TimeoutMs)]
    private static partial Regex IdNumber();

    [GeneratedRegex(@"(?i)\b(?:dob|d\.o\.b\.?|date\s+of\s+birth|born\s+on|birthday)\W{0,4}(?<value>\d{1,4}[/.\- ]\d{1,2}[/.\- ]\d{1,4})", Flags, TimeoutMs)]
    private static partial Regex BirthDate();

    [GeneratedRegex(@"(?i)\bhttps?://(?<host>[^/\s:'""<>@]+)", Flags, TimeoutMs)]
    private static partial Regex UrlHost();

    /// <summary>
    /// The check of one outward step's inputs (the card's exact inputs: a command, a tool's JSON input, a message) against
    /// the owner's list of recipients personal data may go to.
    /// </summary>
    public static OutwardCheck Check(IEnumerable<DecisionInput> inputs, IEnumerable<string> allowlist)
    {
        var list = inputs.ToList();
        var recipients = new List<string>();
        foreach (var input in list) recipients.AddRange(RecipientsOf(input.Value));
        var distinct = recipients.Select(Normalize).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var spans = new List<PrivacySpan>();
        foreach (var input in list) spans.AddRange(Scan(input.Name, input.Value, distinct));
        var listed = allowlist.Select(Normalize).Where(a => a.Length > 0).ToHashSet(StringComparer.Ordinal);
        var unlisted = distinct.Where(r => !Listed(r, listed)).ToList();
        return new OutwardCheck(new ValueList<PrivacySpan>(spans), new ValueList<string>(distinct), new ValueList<string>(unlisted));
    }

    /// <summary>
    /// An engine decision with the check applied (12.4: "under any grant, personal data can go only to allowlisted
    /// recipients"): an Allow whose step is blocked becomes Ask. Ask and Deny stay as they are.
    /// </summary>
    public static PolicyDecision Apply(PolicyDecision decision, OutwardCheck check) =>
        decision.Verdict == Verdict.Allow && check.Blocked
            ? decision with { Verdict = Verdict.Ask, Rule = PolicyRule.PersonalData, Reason = check.Refusal!, GrantsUsed = ValueList<Guid>.Empty }
            : decision;

    /// <summary>The marked pieces of one input. A recipient's own address is not marked: it is who the step goes to.</summary>
    public static IReadOnlyList<PrivacySpan> Scan(string input, string text, IReadOnlyCollection<string>? recipients = null)
    {
        var spans = new List<PrivacySpan>();
        if (string.IsNullOrEmpty(text)) return spans;
        var scan = text.Length > ScanChars ? text[..ScanChars] : text;
        void Add(int start, int length, PrivacyKind kind)
        {
            if (length <= 0 || spans.Any(s => start < s.Start + s.Length && s.Start < start + length)) return;
            spans.Add(new PrivacySpan(input, start, length, kind));
        }
        bool IsRecipient(string value) => recipients is not null && recipients.Contains(Normalize(value));
        try
        {
            foreach (var (start, length, kind) in FeedShield.SecretSpans(scan, ScanChars))
                Add(start, length, kind switch
                {
                    "card" => PrivacyKind.Card,
                    "bank" => PrivacyKind.Bank,
                    "code" => PrivacyKind.Code,
                    "id" => PrivacyKind.Id,
                    "unreadable" => PrivacyKind.Unchecked,
                    _ => PrivacyKind.Secret,
                });
            foreach (Match m in IdNumber().Matches(scan))
            {
                var g = m.Groups["value"].Success ? m.Groups["value"] : (Group)m;
                Add(g.Index, g.Length, PrivacyKind.Id);
            }
            foreach (Match m in BirthDate().Matches(scan)) Add(m.Groups["value"].Index, m.Groups["value"].Length, PrivacyKind.BirthDate);
            foreach (Match m in LabelledAddress().Matches(scan)) Add(m.Groups["value"].Index, m.Groups["value"].Length, PrivacyKind.Address);
            foreach (Match m in StreetAddress().Matches(scan)) Add(m.Index, m.Length, PrivacyKind.Address);
            foreach (Match m in EmailAddress().Matches(scan))
                if (!IsRecipient(m.Value)) Add(m.Index, m.Length, PrivacyKind.Email);
            foreach (Match m in LabelledPhone().Matches(scan))
                if (!IsRecipient(m.Groups["value"].Value)) Add(m.Groups["value"].Index, m.Groups["value"].Length, PrivacyKind.Phone);
            foreach (Match m in Phone().Matches(scan))
                if (!IsRecipient(m.Value) && Digits(m.Value) is >= 9 and <= 15) Add(m.Index, m.Length, PrivacyKind.Phone);
        }
        catch (RegexMatchTimeoutException)
        {
            spans.Clear();
            spans.Add(new PrivacySpan(input, 0, scan.Length, PrivacyKind.Unchecked));
        }
        if (text.Length > ScanChars) spans.Add(new PrivacySpan(input, ScanChars, text.Length - ScanChars, PrivacyKind.Unchecked));
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    /// <summary>
    /// Who an input sends to: the recipient fields of a tool's JSON input (to, cc, channel, attendees...), and the host of
    /// each web address in a command. Nothing for plain text.
    /// </summary>
    public static IReadOnlyList<string> RecipientsOf(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = 32 });
                Walk(document.RootElement, false, found, 0);
                return found;
            }
            catch (JsonException) { }
        }
        try
        {
            foreach (Match m in UrlHost().Matches(text.Length > ScanChars ? text[..ScanChars] : text)) found.Add(m.Groups["host"].Value);
        }
        catch (RegexMatchTimeoutException) { }
        return found;
    }

    private static void Walk(JsonElement element, bool recipient, List<string> found, int depth)
    {
        if (depth > 32 || found.Count > 200) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    // A recipient object ({"email": ..., "name": ...}) is read for its address, not its name.
                    if (recipient && property.Name is "name" or "displayName" or "display_name") continue;
                    Walk(property.Value, recipient || RecipientFields.Contains(property.Name), found, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, recipient, found, depth + 1);
                break;
            case JsonValueKind.String when recipient:
                foreach (var part in (element.GetString() ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    found.Add(part);
                break;
            case JsonValueKind.Number when recipient:
                found.Add(element.GetRawText());
                break;
        }
    }

    /// <summary>
    /// A recipient as the list compares it: lower case, without "mailto:" or a display name, a phone number as its digits
    /// (with a leading +), a host without "www.".
    /// </summary>
    public static string Normalize(string recipient)
    {
        var r = recipient.Trim().Trim('"', '\'');
        if (r.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) r = r[7..];
        var open = r.LastIndexOf('<');
        if (open >= 0 && r.EndsWith('>')) r = r[(open + 1)..^1];
        r = r.Trim().ToLowerInvariant();
        if (r.Length > 0 && Digits(r) >= 7 && r.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '.'))
            return (r.StartsWith('+') ? "+" : "") + new string([.. r.Where(char.IsDigit)]);
        if (r.StartsWith("www.", StringComparison.Ordinal)) r = r[4..];
        return r;
    }

    /// <summary>Listed: the recipient itself, or an entry "@domain" for an address at that domain.</summary>
    private static bool Listed(string recipient, HashSet<string> listed)
    {
        if (listed.Contains(recipient)) return true;
        var at = recipient.LastIndexOf('@');
        return at > 0 && listed.Contains(recipient[at..]);
    }

    private static int Digits(string text) => text.Count(char.IsAsciiDigit);

    /// <summary>A one-line note of what a card's check found, for its Note line ("Personal data: 1 email, 1 phone").</summary>
    public static string Summary(OutwardCheck check)
    {
        if (!check.HasPersonalData) return "";
        var parts = check.Spans.GroupBy(s => s.Kind).OrderBy(g => g.Key)
            .Select(g => g.Count().ToString(CultureInfo.InvariantCulture) + " " + Word(g.Key, g.Count()));
        return "Personal data: " + string.Join(", ", parts) + ".";
    }

    private static string Word(PrivacyKind kind, int count) => kind switch
    {
        PrivacyKind.Email => count == 1 ? "email address" : "email addresses",
        PrivacyKind.Phone => count == 1 ? "phone number" : "phone numbers",
        PrivacyKind.Address => count == 1 ? "address" : "addresses",
        PrivacyKind.Id => count == 1 ? "ID number" : "ID numbers",
        PrivacyKind.BirthDate => count == 1 ? "date of birth" : "dates of birth",
        PrivacyKind.Card => count == 1 ? "card detail" : "card details",
        PrivacyKind.Bank => count == 1 ? "bank account" : "bank accounts",
        PrivacyKind.Secret => count == 1 ? "secret" : "secrets",
        PrivacyKind.Code => count == 1 ? "one-time code" : "one-time codes",
        _ => count == 1 ? "part nth could not check" : "parts nth could not check",
    };
}
