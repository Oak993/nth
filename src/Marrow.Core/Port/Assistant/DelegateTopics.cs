// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "What it never answers", 12.1 the action classes; unit AS-22; ED-NEW-dextra-as22-2). No Swift twin.
// What a question is about, read from its words: money, credentials, anything sent out, deleting, installing, a promise (each one the
// owner always confirms) and a taste pick. English, Hindi and Urdu (both scripts and romanised), through hidden characters, look-alike
// letters and full-width forms (the same normalising as InstructionScanner). It errs on the side of asking: a word that only might mean
// one of these sends the question to the owner. It is the first gate and the second one: the delegate's model is asked only about what
// passes it, and the answer it picks passes it again. Pure: no I/O, no clock; the text is cut to a bounded length and every pattern has a
// match timeout, so hostile text cannot make it slow.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public static partial class DelegateTopics
{
    /// <summary>The most characters read from a text.</summary>
    public const int ScanChars = 4_000;

    private const int TimeoutMs = 250;
    private const RegexOptions Flags = RegexOptions.CultureInvariant;

    // ---- credentials ----

    [GeneratedRegex(@"\b(?:pass(?:word|words|code|codes|phrase|phrases)|passwd|pwd|credentials?|api[ _-]?keys?|(?:access|auth|authentication|bearer|refresh|session|personal access|id|api|oauth) tokens?|secrets?|secret keys?|private keys?|ssh keys?|signing keys?|license keys?|2fa|mfa|otp|one[- ]time (?:code|password|passcode)|verification code|security code|log ?in|login|logins|sign[ -]?in|sign[ -]?on|keychain|credential manager|1password|bitwarden|lastpass|keepass|cvv|cvc|pin code)\b|पासवर्ड|पासकोड|लॉग ?इन|लॉगिन|गुप्त (?:कुंजी|कोड)|कुंजी|پاس ?ورڈ|لاگ ?ان|خفیہ|\.env\b", Flags, TimeoutMs)]
    private static partial Regex Credentials();

    // ---- money ----

    [GeneratedRegex(@"\b(?:pay|pays|paid|paying|payments?|purchas\w*|buy|buys|bought|buying|checkout|check out|subscri\w*|billing|invoice[sd]?|credit cards?|debit cards?|card number|refunds?|donat\w*|wire|transfer(?:s|red)? (?:money|funds|cash)|money|cash|prices?|pricing|charged?|charges|fees?|salary|payroll|upgrade (?:your |the |to |my )?(?:plan|tier|account)|paid plan|pro plan|enterprise plan|paise|paisa|paisay|rupay|rupaye|rupees?|kharid\w*|bhugtan|adaigi|qeemat|keemat|kimat)\b|[$€£₹]\s?\d|\b\d[\d,.]*\s?(?:usd|eur|gbp|inr|pkr|rs\.?|dollars?|euros?|pounds?)\b|खरीद|भुगतान|पैसे|पैसा|रुपये|रुपए|कीमत|ادائیگی|خرید|پیسے|روپے|قیمت", Flags, TimeoutMs)]
    private static partial Regex Money();

    // ---- anything sent out ----

    [GeneratedRegex(@"\b(?:send|sends|sent|sending|e-?mails?|e-?mailed|e-?mailing|mail(?:ing|ed)? (?:it|them|this|to)|post(?:s|ed|ing)? (?:it|this|that|them|to|on|a|the)|tweet\w*|publish\w*|share[sd]?|sharing|forward\w*|repl(?:y|ies|ied) to|dm|direct messages?|notify|notif(?:y|ies|ied|ication|ications)|announce\w*|invite[sd]?|broadcast\w*|upload\w*|push(?:es|ed|ing)? (?:it|this|to|the (?:branch|commits?|changes?))|git push|(?:open|create|submit|raise) (?:a |an |the )?(?:pr|pull request)|merge (?:the |this |a )?(?:pr|pull request)|deploy\w*|release[sd]? (?:it|this|to|the|a|v?\d)|ship(?:s|ped|ping)? (?:it|this|to)|slack|whatsapp|telegram|discord|sms|text message|message (?:him|her|them|the team|everyone|all)|contact (?:him|her|them|the|support)|reach out|submit\w*|bhej\w*|mail kar\w*|post kar\w*|share kar\w*)\b|भेज|ईमेल|ई-मेल|मेल कर|पोस्ट कर|शेयर कर|प्रकाशित|بھیج|ای ?میل|شیئر|پوسٹ کر", Flags, TimeoutMs)]
    private static partial Regex Outward();

    // ---- deleting ----

    [GeneratedRegex(@"\b(?:delet\w*|remov\w*|eras\w*|wip(?:e|es|ed|ing)|drop(?:s|ped|ping)? (?:the |a |all )?(?:table|tables|database|db|schema|collection|branch|index)|truncat\w*|overwrit\w*|destroy\w*|purg\w*|clean(?:s|ed|ing)? (?:up )?(?:all|everything|the (?:folder|directory|repo|workspace|cache|disk))|format(?:s|ted|ting)? (?:the )?(?:drive|disk|partition|volume)|uninstall\w*|reset --hard|force[- ]push|rm -rf|rmdir|rd /s|discard\w*|irreversib\w*|permanent\w*|lose (?:your|the|all)|losing (?:your|the|all)|mita\w*|hata\w*)\b|मिटा|हटा|डिलीट|साफ़ कर|حذف|مٹا|ہٹا", Flags, TimeoutMs)]
    private static partial Regex Destructive();

    // ---- installing ----

    [GeneratedRegex(@"\b(?:install\w*|set ?up (?:the )?(?:tool|package|software|app|program)|winget|chocolatey|choco|scoop|apt(?:-get)?|brew|pip3? install|npm (?:i|install|add)|yarn add|pnpm add|dotnet (?:tool|add package)|cargo install|go install|add (?:(?:a|an|the|new|another) )*(?:dependenc\w+|packages?|librar\w+|extensions?|plug-?ins?|modules?|nuget)|download(?:s|ed|ing)? (?:and (?:run|install|open)|the (?:installer|exe|setup))|upgrade (?:the |all )?(?:packages?|dependenc\w+|sdk|runtime)|update (?:the |all )?(?:packages?|dependenc\w+)|\w+\.(?:exe|msi|msix))\b|इंस्टॉल|स्थापित कर|انسٹال", Flags, TimeoutMs)]
    private static partial Regex Install();

    // ---- promises ----

    [GeneratedRegex(@"\b(?:book\w*|reserv\w+|schedul\w* (?:a |an |the )?(?:meeting|call|appointment|interview|visit)|appointment|rsvp|accept(?:s|ed|ing)? (?:the |this |an |a )?(?:offer|invit\w+|terms|agreement|contract|proposal|quote|cookies?)|agree\w*|sign(?:s|ed|ing)? (?:the |a |this )?(?:contract|agreement|document|form|petition|lease|nda|waiver)|confirm(?:s|ed|ing)? (?:the |your |my )?(?:booking|order|reservation|appointment|attendance|registration|purchase)|commit(?:s|ted|ting)? to|promis\w+|guarantee\w*|register(?:s|ed|ing)? (?:for|me|us)|sign up|signup|enrol\w*|enroll\w*|i'?m on my way|on my way|pledge|apply (?:for|to)|submit (?:the |an |my )?application)\b", Flags, TimeoutMs)]
    private static partial Regex Commitment();

    // ---- taste ----

    [GeneratedRegex(@"\b(?:styles?|styling|colou?rs?|palette|theme|themes|fonts?|typeface|typography|layout|looks?|feels?|aesthetic\w*|designs?|designed|pretty|ugly|beautiful|nicer|logo|icons?|wording|tone|phrasing|copywriting|nam(?:e|es|ed|ing)|title it|call it|brand\w*|taste|prefer\w*|favou?rites?|you'?d like|you would like|your call|up to you|what do you want|which (?:one )?(?:do you|would you)|spacing|mood|vibe)\b", Flags, TimeoutMs)]
    private static partial Regex Taste();

    private static readonly (DelegateReason Reason, Func<Regex> Pattern)[] Hard =
    [
        (DelegateReason.Credentials, Credentials),
        (DelegateReason.Money, Money),
        (DelegateReason.Outward, Outward),
        (DelegateReason.Destructive, Destructive),
        (DelegateReason.Install, Install),
        (DelegateReason.Commitment, Commitment),
    ];

    /// <summary>
    /// The first topic the owner always confirms that the text touches (credentials, then money, sending, deleting, installing, a
    /// promise), or null. Text the patterns cannot finish in time counts as touching one (Credentials, the strictest), never as clean.
    /// </summary>
    public static DelegateReason? Find(string? text)
    {
        var normal = Read(text);
        if (normal.Length == 0) return null;
        foreach (var (reason, pattern) in Hard)
        {
            try
            {
                if (pattern().IsMatch(normal)) return reason;
            }
            catch (RegexMatchTimeoutException)
            {
                return DelegateReason.Credentials;
            }
        }
        return null;
    }

    /// <summary>Whether the text reads like a design or taste pick (a style, a colour, a name, "your call").</summary>
    public static bool LooksLikeTaste(string? text)
    {
        var normal = Read(text);
        if (normal.Length == 0) return false;
        try { return Taste().IsMatch(normal); }
        catch (RegexMatchTimeoutException) { return true; }
    }

    // The text as the patterns read it, cut to a bounded length first.
    private static string Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var cut = text.Length <= ScanChars ? text : text[..ScanChars];
        try { return InstructionScanner.Normalize(cut); }
        catch (ArgumentException) { return cut.ToLowerInvariant(); }
    }
}
