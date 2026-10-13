// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Privacy shield on the way out"; unit AS-7; ED-NEW-dextra-as7-4). No Swift
// twin. The shield's own patterns, read for where they match rather than to mask: an outward card highlights each secret
// in place. The same patterns as Mask, so a card and the feed never disagree on what a secret is.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public static partial class FeedShield
{
    /// <summary>
    /// Where the shield's patterns match in <paramref name="text"/> (read up to <paramref name="maxChars"/>): each match's
    /// start, length and whether it is a card number, a code or an ID rather than a key or password. Never throws; a
    /// pattern that times out reports the whole text.
    /// </summary>
    internal static List<(int Start, int Length, string Kind)> SecretSpans(string text, int maxChars)
    {
        var spans = new List<(int, int, string)>();
        if (string.IsNullOrEmpty(text)) return spans;
        var scan = text.Length > maxChars ? CutClean(text, maxChars) : text;
        void All(Regex pattern, string kind, string? group = null, Func<Match, bool>? keep = null)
        {
            foreach (Match match in pattern.Matches(scan))
            {
                if (keep is not null && !keep(match)) continue;
                var g = group is null ? match : match.Groups[group];
                if (g.Success && g.Length > 0) spans.Add((g.Index, g.Length, kind));
            }
        }
        try
        {
            All(PrivateKeyBlock(), "secret");
            All(WebToken(), "secret");
            All(PrefixedKey(), "secret");
            All(AuthorizationValue(), "secret", "value");
            All(UrlPassword(), "secret", "value");
            All(QuerySecret(), "secret", "value");
            foreach (Match match in NamedSecret().Matches(scan))
            {
                var start = match.Index + match.Length;
                var end = start;
                if (end < scan.Length && scan[end] is '"' or '\'') end++;
                var valueStart = end;
                while (end < scan.Length && !char.IsWhiteSpace(scan[end]) && scan[end] is not ('"' or '\'' or '&' or ',' or ';')) end++;
                if (end > valueStart) spans.Add((valueStart, end - valueStart, "secret"));
            }
            All(FlagSecret(), "secret", "value");
            All(SpokenPassword(), "secret", "value");
            All(SpokenKey(), "secret", "value");
            All(CardSecurityCode(), "card", "value");
            All(CardExpiry(), "card", "value");
            All(Ssn(), "id");
            All(Iban(), "bank", keep: m => LooksLikeIban(m.Value));
            All(DigitRun(), "card", keep: m => LooksLikeCard(m.Value));
            All(OneTimeCode(), "code", "value");
            All(PinCode(), "code", "value");
            All(GoogleCode(), "code");
            All(LabelledId(), "id", "value");
        }
        catch (RegexMatchTimeoutException)
        {
            spans.Clear();
            spans.Add((0, scan.Length, "unreadable"));
        }
        return spans;
    }
}
