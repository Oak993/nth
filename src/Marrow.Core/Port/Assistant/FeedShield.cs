// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.4 "Shield on the way in"; unit AS-2; ED-NEW-dextra-as2-4). No Swift twin.
// The patterns every line of the feed passes before it can reach the Assistant's brain: keys, passwords, card numbers,
// one-time codes and IDs are masked. Pure: no I/O, no clock. Every pattern has a match timeout, and the text is cut
// to a bounded length before it is scanned, so a hostile reply cannot make the filter slow.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>Masks secrets in text that is about to leave a chat (5.4) and cuts text to the length the feed keeps.</summary>
public static partial class FeedShield
{
    public const string SecretMask = "[secret]";
    public const string CardMask = "[card]";
    public const string CodeMask = "[code]";
    public const string IdMask = "[id]";

    /// <summary>The longest text the shield reads (a longer text is cut first, then masked).</summary>
    public const int MaxScanChars = 4096;

    private const int TimeoutMs = 250;
    private const RegexOptions Flags = RegexOptions.CultureInvariant;

    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 ]*PRIVATE KEY-----|\z)", Flags, TimeoutMs)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", Flags, TimeoutMs)]
    private static partial Regex WebToken();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:sk-[A-Za-z0-9_-]{16,}|(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_-]{20,}|xox[abposr]-[A-Za-z0-9-]{10,}|(?:AKIA|ASIA)[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|(?:sk|rk|pk)_(?:live|test)_[A-Za-z0-9]{16,}|whsec_[A-Za-z0-9]{16,}|hf_[A-Za-z0-9]{30,}|npm_[A-Za-z0-9]{36}|pypi-[A-Za-z0-9_-]{30,}|SG\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,})", Flags, TimeoutMs)]
    private static partial Regex PrefixedKey();

    [GeneratedRegex(@"(?i)\b(?<scheme>Bearer|Basic|Token|Digest)\s+(?<value>(?=[A-Za-z0-9._~+/=-]*[0-9])[A-Za-z0-9._~+/=-]{8,})", Flags, TimeoutMs)]
    private static partial Regex AuthorizationValue();

    [GeneratedRegex(@"(?i)(?<head>\b[a-z][a-z0-9+.-]*://[^/\s:@]+:)(?<value>[^@\s/]+)(?=@)", Flags, TimeoutMs)]
    private static partial Regex UrlPassword();

    [GeneratedRegex(@"(?i)(?<head>[?&](?:key|sig|signature|code|auth|access_token|refresh_token|id_token|client_secret|password|token|apikey|api_key)=)(?<value>[^&#\s]+)", Flags, TimeoutMs)]
    private static partial Regex QuerySecret();

    // A name that ends in a secret word (password, api key, access token...), then ':' or '=', then the value. "tokens: 40"
    // and "max_tokens=4096" are not secrets: the secret word may not be followed by another letter.
    private const string SecretWord = @"(?:password|passwd|passphrase|pwd|secret|token|api[_ -]?key|apikey|access[_ -]?key|private[_ -]?key|client[_ -]?secret|credentials?|session[_ -]?id|cookie)";

    [GeneratedRegex(@"(?i)(?<name>[A-Za-z0-9_.-]*?" + SecretWord + @"(?:[_-]?(?:value|string|hash|text))?)(?<sep>""?\s*[:=]\s*)(?:(?<q>[""'])(?<qv>[^""'\r\n]{1,200})\k<q>|(?<v>[^\s""'&,;]+))", Flags, TimeoutMs)]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"(?i)(?<name>(?<![A-Za-z0-9-])--?[A-Za-z0-9_-]*?" + SecretWord + @"(?:[_-]?(?:value|string|hash|text))?)(?<sep>\s+)(?<value>[^\s""'-][^\s""']*)", Flags, TimeoutMs)]
    private static partial Regex FlagSecret();

    // "my password is hunter2", "the passcode was 4821": the word that names a password, then is/was/are, then the value.
    [GeneratedRegex(@"(?i)\b(?<name>password|passwd|passphrase|passcode|pwd)\s+(?<link>is|was|are)\s+(?<value>[^\s,;]+)", Flags, TimeoutMs)]
    private static partial Regex SpokenPassword();

    // "the api key is sk_abc123": prose such as "the api key is missing" has no digit and stays.
    [GeneratedRegex(@"(?i)\b(?<name>api key|secret key|access key)\s+(?<link>is|was|are)\s+(?<value>(?=\S*[0-9])[^\s,;]+)", Flags, TimeoutMs)]
    private static partial Regex SpokenKey();

    [GeneratedRegex(@"(?<![0-9])[0-9](?:[ -]?[0-9]){12,18}(?![0-9])", Flags, TimeoutMs)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"(?i)(?<head>\b(?:cvv2?|cvc2?|csc|security code)\b\W{0,3})(?<value>\d{3,4})\b", Flags, TimeoutMs)]
    private static partial Regex CardSecurityCode();

    [GeneratedRegex(@"(?i)(?<head>\b(?:exp(?:ires?|iry|iration)?(?: date)?|valid (?:thru|through|until))\b\W{0,3})(?<value>\d{1,2}\s*/\s*\d{2,4})\b", Flags, TimeoutMs)]
    private static partial Regex CardExpiry();

    // A one-time code: the phrase that names it, a short gap that holds only "is", ":" or "=" (or a space), then 4 to 8
    // digits. A bare "code" is not enough ("exit code 10061" is not a secret); "pin" needs its separator.
    [GeneratedRegex(@"(?i)(?<head>\b(?:one[- ]?time (?:code|password|passcode|pin)|(?:verification|security|login|log[- ]?in|sign[- ]?in|authentication|authenticator|confirmation|activation|reset|access|2fa|mfa|your|the|my) (?:code|pin|passcode)|otp|passcode)\b(?:\s*(?:is|was|=|:|-|#)\s*|\s+)[""'`(]?)(?<value>\d{4,8}(?:[ -]\d{2,4})?)\b", Flags, TimeoutMs)]
    private static partial Regex OneTimeCode();

    [GeneratedRegex(@"(?i)(?<head>\bpin\b\s*(?:is|was|=|:|#)\s*[""'`(]?)(?<value>\d{4,8})\b", Flags, TimeoutMs)]
    private static partial Regex PinCode();

    [GeneratedRegex(@"\bG-\d{6}\b", Flags, TimeoutMs)]
    private static partial Regex GoogleCode();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", Flags, TimeoutMs)]
    private static partial Regex Ssn();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b", Flags, TimeoutMs)]
    private static partial Regex Iban();

    [GeneratedRegex(@"(?i)(?<head>\b(?:ssn|social security(?: number| no\.?)?|passport(?: number| no\.?| #)?|driver'?s? licen[cs]e(?: number| no\.?| #)?|national id(?: number)?|id number|tax id|aadhaar(?: number)?)\b\W{0,3})(?<value>(?=[A-Z0-9-]*\d)[A-Z0-9][A-Z0-9-]{5,19})\b", Flags, TimeoutMs)]
    private static partial Regex LabelledId();

    /// <summary>The text with every recognised secret masked. Masking twice gives the same text. Never throws.</summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var scan = text.Length > MaxScanChars ? CutClean(text, MaxScanChars) : text;
        try
        {
            scan = PrivateKeyBlock().Replace(scan, SecretMask);
            scan = WebToken().Replace(scan, SecretMask);
            scan = PrefixedKey().Replace(scan, SecretMask);
            scan = AuthorizationValue().Replace(scan, match => match.Groups["scheme"].Value + " " + SecretMask);
            scan = UrlPassword().Replace(scan, match => match.Groups["head"].Value + SecretMask);
            scan = QuerySecret().Replace(scan, match => match.Groups["head"].Value + SecretMask);
            scan = NamedSecret().Replace(scan, NamedValue);
            scan = FlagSecret().Replace(scan, match => match.Groups["name"].Value + match.Groups["sep"].Value + SecretMask);
            scan = SpokenPassword().Replace(scan, match => match.Groups["name"].Value + " " + match.Groups["link"].Value + " " + SecretMask);
            scan = SpokenKey().Replace(scan, match => match.Groups["name"].Value + " " + match.Groups["link"].Value + " " + SecretMask);
            scan = CardSecurityCode().Replace(scan, match => match.Groups["head"].Value + CodeMask);
            scan = CardExpiry().Replace(scan, match => match.Groups["head"].Value + CardMask);
            scan = Ssn().Replace(scan, IdMask);
            scan = Iban().Replace(scan, match => LooksLikeIban(match.Value) ? IdMask : match.Value);
            scan = DigitRun().Replace(scan, match => LooksLikeCard(match.Value) ? CardMask : match.Value);
            scan = OneTimeCode().Replace(scan, match => match.Groups["head"].Value + CodeMask);
            scan = PinCode().Replace(scan, match => match.Groups["head"].Value + CodeMask);
            scan = GoogleCode().Replace(scan, CodeMask);
            scan = LabelledId().Replace(scan, match => match.Groups["head"].Value + IdMask);
            return scan;
        }
        catch (RegexMatchTimeoutException)
        {
            return SecretMask; // a text the patterns cannot finish on is not shown at all
        }
    }

    /// <summary>
    /// A one-line excerpt for the feed: whitespace folded, masked, then cut to <paramref name="maxChars"/> characters
    /// (never inside a mask or a surrogate pair), with an ellipsis when it was cut. Mask first, cut after: a key that
    /// straddles the cut must not leave its first half behind.
    /// </summary>
    public static string Excerpt(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChars <= 0) return "";
        var masked = Fold(Mask(text));
        if (masked.Length <= maxChars) return masked;
        var cut = maxChars;
        var open = masked.LastIndexOf('[', cut - 1);
        if (open >= 0 && masked.IndexOf(']', open) is var close && (close < 0 || close >= cut)) cut = open;
        if (cut > 0 && char.IsHighSurrogate(masked[cut - 1])) cut--;
        return masked[..cut].TrimEnd() + "…";
    }

    /// <summary>Whitespace folded to single spaces and trimmed.</summary>
    public static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static string CutClean(string text, int max)
    {
        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut];
    }

    private static string NamedValue(Match match)
    {
        var value = match.Groups["qv"].Success ? match.Groups["qv"].Value : match.Groups["v"].Value;
        if (value is SecretMask or CardMask or CodeMask or IdMask) return match.Value;
        var name = match.Groups["name"].Value;
        var sep = match.Groups["sep"].Value;
        return match.Groups["q"].Success
            ? name + sep + match.Groups["q"].Value + SecretMask + match.Groups["q"].Value
            : name + sep + SecretMask;
    }

    // A card number passes the Luhn check; a 13-digit timestamp or an order number mostly does not, and stays readable.
    internal static bool LooksLikeCard(string run)
    {
        var digits = new List<int>(19);
        foreach (var c in run) if (c is >= '0' and <= '9') digits.Add(c - '0');
        if (digits.Count is < 13 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Count; i++)
        {
            var digit = digits[digits.Count - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    // An IBAN passes the mod-97 check; an ordinary identifier of the same shape mostly does not.
    internal static bool LooksLikeIban(string value)
    {
        var compact = value.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length is < 15 or > 34) return false;
        var rearranged = compact[4..] + compact[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            int number;
            if (c is >= '0' and <= '9') number = c - '0';
            else if (c is >= 'A' and <= 'Z') number = c - 'A' + 10;
            else return false;
            remainder = (number >= 10 ? remainder * 100 + number : remainder * 10 + number) % 97;
        }
        return remainder == 1;
    }
}
