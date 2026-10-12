// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, sections 12.5 and 12.7; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-4: what the audit log masks.
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Assistant;

/// <summary>
/// Masks secrets in text before it reaches the audit log (12.5: "secrets are masked"). It recognises, and replaces with
/// <see cref="Mask"/>: private-key blocks; JSON web tokens; the prefixed keys of the common providers (Anthropic and
/// OpenAI <c>sk-</c>, GitHub, GitLab, Slack, AWS access key ids, Google API keys, Stripe, Hugging Face, npm); the value of
/// an <c>Authorization</c> scheme (Bearer, Basic, Token); the value after a secret-like name (<c>password=</c>,
/// <c>"token": "…"</c>, <c>--api-key …</c>, with plain, single or escaped quotes, so JSON carried inside a string is
/// masked too); a card's security code, expiry and number fields (12.7); the password in a URL's user info; secret query
/// parameters; and card numbers: digit groups of 13 to 19 digits in all, joined by spaces, dots, dashes, underscores or
/// line breaks, that pass the Luhn check (<see cref="HoldsCard"/>). Text over <see cref="MaxScanChars"/> is cut before
/// the scan and the last <see cref="CutMargin"/> characters scanned are dropped too, then the masked text is cut to
/// <see cref="MaxKeptChars"/>, so a secret split by the first cut is never inside what is kept. A scan that times out
/// masks the whole value.
/// </summary>
public static partial class AuditRedactor
{
    public const string Mask = "[secret]";
    public const int MaxScanChars = 65536;
    public const int MaxKeptChars = 8192;
    private const int Timeout = 1000;

    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 ]*PRIVATE KEY-----|\z)", RegexOptions.None, Timeout)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.None, Timeout)]
    private static partial Regex WebToken();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:sk-[A-Za-z0-9_-]{16,}|(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_-]{20,}|xox[abposr]-[A-Za-z0-9-]{10,}|(?:AKIA|ASIA)[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|(?:sk|rk|pk)_(?:live|test)_[A-Za-z0-9]{16,}|whsec_[A-Za-z0-9]{16,}|hf_[A-Za-z0-9]{30,}|npm_[A-Za-z0-9]{36})", RegexOptions.None, Timeout)]
    private static partial Regex PrefixedKey();

    [GeneratedRegex(@"(?i)\b(?<scheme>Bearer|Basic|Token|Digest)\s+(?<value>[A-Za-z0-9._~+/=-]{8,})", RegexOptions.None, Timeout)]
    private static partial Regex AuthorizationValue();

    private const string SecretName =
        "(?:password|passwd|passphrase|pwd|secret|token|api[_-]?key|apikey|access[_-]?key|private[_-]?key|client[_-]?secret|credential|session[_-]?id|cookie)";

    // The joint between a name and its value: ':' or '=', with the name's and the value's quotes, plain ("), single (')
    // or escaped (\" in JSON carried inside a string, any depth), and spaces around it.
    private const string Joint = @"(?<sep>\\{0,7}[""']?\s*[:=]\s*\\{0,7}[""']?)";

    // A value runs to a space, a quote (plain or escaped), '&', ',' or ';'. A backslash inside it is kept unless it
    // escapes a closing quote.
    private const string Value = @"(?<value>(?:[^\s""'&,;\\]|\\(?!\\*[""']))+)";

    // A name starts only where a word starts (the lookbehind) and its parts are bounded, so a long run of word
    // characters is scanned once, never once per position (no quadratic backtracking).
    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_.-])(?<name>[A-Za-z0-9_.-]{0,64}?" + SecretName + @"[A-Za-z0-9_.-]{0,64})" + Joint + Value, RegexOptions.None, Timeout)]
    private static partial Regex NamedSecret();

    // A card's fields (12.7: nothing of the card beyond its last 4 digits): the security code, the expiry and the
    // number, as a whole name or a name's last part ("card_cvc", "billing.cardNumber", "expMonth"). A part ends at '_',
    // '.', '-' or a lower-to-upper case change, so "regexp" or "japan" is never read as "exp" or "pan".
    private const string CardName =
        "(?:cvc2?|cvv2?|cvn|security[_.-]?code|card[_.-]?(?:number|num|no|code|pan|exp(?:iry|iration)?)|cc[_.-]?(?:number|num|no|exp)|ccnum|pan|exp|expiry|expiration|exp(?:iry|iration)?[_.-]?(?:date|month|year|mm|yy|yyyy))";

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_.-])(?<name>(?:[A-Za-z0-9]{1,24}(?:[_.-]|(?-i:(?<=[a-z0-9])(?=[A-Z])))){0,3}" + CardName + ")" + Joint + Value, RegexOptions.None, Timeout)]
    private static partial Regex CardField();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_-])(?<name>--?[A-Za-z0-9_-]{0,64}?" + SecretName + @"[A-Za-z0-9_-]{0,64})(?<sep>\s+)(?<value>[^\s""'-][^\s""']*)", RegexOptions.None, Timeout)]
    private static partial Regex FlagSecret();

    [GeneratedRegex(@"(?i)(?<head>(?<![a-z0-9+.-])[a-z][a-z0-9+.-]{0,31}://[^/\s:@]{1,256}:)(?<value>[^@\s/]{1,256})(?=@)", RegexOptions.None, Timeout)]
    private static partial Regex UrlPassword();

    [GeneratedRegex(@"(?i)(?<head>[?&](?:key|sig|signature|code|auth|access_token|refresh_token|id_token|client_secret|password|token)=)(?<value>[^&#\s]+)", RegexOptions.None, Timeout)]
    private static partial Regex QuerySecret();

    // A run of digit groups: up to 160 digits, each joined to the next by up to 3 of space, tab, no-break space, '.',
    // '_', '-' or a line break (a number split across lines). Its groups are then checked for a card (HoldsCard).
    [GeneratedRegex(@"(?<![0-9])[0-9](?:[ \t ._\r\n-]{0,3}[0-9]){12,159}", RegexOptions.None, Timeout)]
    private static partial Regex DigitRun();

    /// <summary>
    /// When the text was cut before the scan, this much of the scanned end is dropped as well: a secret the cut split
    /// (a card's first 12 digits, a key shorter than its pattern, a URL password before its '@') ends there, unmasked.
    /// </summary>
    public const int CutMargin = 1024;

    /// <summary>The text with every recognised secret replaced by <see cref="Mask"/>, cut to <see cref="MaxKeptChars"/>.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var cut = text.Length > MaxScanChars ? text[..MaxScanChars] : text;
        string masked;
        try
        {
            masked = PrivateKeyBlock().Replace(cut, Mask);
            masked = WebToken().Replace(masked, Mask);
            masked = PrefixedKey().Replace(masked, Mask);
            masked = AuthorizationValue().Replace(masked, m => m.Groups["value"].Value == Mask ? m.Value : m.Groups["scheme"].Value + " " + Mask);
            masked = UrlPassword().Replace(masked, m => m.Groups["head"].Value + Mask);
            masked = QuerySecret().Replace(masked, m => m.Groups["head"].Value + Mask);
            masked = NamedSecret().Replace(masked, KeepName);
            masked = CardField().Replace(masked, KeepName);
            masked = FlagSecret().Replace(masked, KeepName);
            masked = DigitRun().Replace(masked, m => HoldsCard(m.Value) ? Mask : m.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return Mask;
        }
        if (masked.Length <= MaxKeptChars && cut.Length == text.Length) return masked;
        // Unmasked text keeps its length, so the scanned end is the masked text's end: a secret the scan cut split lies
        // within its last CutMargin characters.
        if (cut.Length < text.Length) masked = masked[..Math.Max(0, masked.Length - CutMargin)];
        var kept = masked.Length > MaxKeptChars ? masked[..MaxKeptChars] : masked;
        return kept + "…[+" + (text.Length - kept.Length) + " chars]";
    }

    /// <summary>
    /// Whether a digit run holds a card number: one group of 13 to 19 digits, or consecutive groups of 13 to 19 digits
    /// in all, each of 4 to 8 digits but the last (3 to 8), that pass the Luhn check. The groups are the card's own
    /// blocks (4-4-4-4, 4-6-5, 4-4-4-4-3), so a card with other digits before or after it is still found, while dotted
    /// addresses and lists of small numbers are not read as cards.
    /// </summary>
    public static bool HoldsCard(string run)
    {
        var groups = new List<string>();
        var current = new StringBuilder();
        foreach (var c in run)
        {
            if (c is >= '0' and <= '9')
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                groups.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) groups.Add(current.ToString());
        var digits = new StringBuilder();
        for (var first = 0; first < groups.Count; first++)
        {
            digits.Clear();
            for (var last = first; last < groups.Count; last++)
            {
                if (last > first && groups[last - 1].Length is < 4 or > 8) break; // a block before the last is 4 to 8
                digits.Append(groups[last]);
                if (digits.Length > 19) break;
                if (digits.Length < 13) continue;
                if ((last == first || groups[last].Length is >= 3 and <= 8) && PassesLuhn(digits.ToString())) return true;
            }
        }
        return false;
    }

    private static string KeepName(Match m) =>
        m.Groups["value"].Value.StartsWith(Mask, StringComparison.Ordinal) ? m.Value : m.Groups["name"].Value + m.Groups["sep"].Value + Mask;

    /// <summary>The Luhn check over the digits of <paramref name="run"/> (spaces and dashes skipped).</summary>
    public static bool PassesLuhn(string run)
    {
        var digits = new StringBuilder();
        foreach (var c in run)
            if (c is >= '0' and <= '9') digits.Append(c);
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
