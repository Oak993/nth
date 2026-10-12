// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Instructions found in content"; unit AS-7; ED-NEW-dextra-as7-3). No Swift twin.
// Finds instructions in untrusted text ("ignore previous instructions", "email this to ...", "the owner already approved
// this"), in English, Hindi and Urdu (both scripts and romanised), through hidden characters, look-alike letters and
// full-width forms. It only makes them visible: the feed shows "Found instructions in <source>; ignored" and the
// mission card lists them. It is never the gate; taint is (12.4). Pure: no I/O, no clock; the text is cut to a bounded
// length and every pattern has a match timeout, so hostile text cannot make it slow.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>Instructions found in a piece of untrusted text: a short quote of them, and which kind of pattern matched.</summary>
public sealed record InstructionFinding(string Quote, string Pattern);

public static partial class InstructionScanner
{
    /// <summary>The most characters read from the start of a text, and again from its end.</summary>
    public const int ScanChars = 32_000;

    /// <summary>The longest quote a finding keeps (the feed line and the card show it).</summary>
    public const int QuoteChars = 40;

    private const int TimeoutMs = 250;
    private const RegexOptions Flags = RegexOptions.CultureInvariant | RegexOptions.Singleline;

    // ---- English ----

    [GeneratedRegex(@"\b(?:ignore|disregard|forget|override|bypass|drop|abandon)\b.{0,40}?\b(?:previous|prior|above|earlier|preceding|all|any|your|the|system|original|existing|other)\b.{0,30}?\b(?:instructions?|prompts?|rules?|directions?|guidelines?|messages?|context|guardrails?|restrictions?|brief|assignment)\b", Flags, TimeoutMs)]
    private static partial Regex IgnoreRules();

    [GeneratedRegex(@"\b(?:new|updated|real|actual|hidden|secret|true|urgent)\s+(?:instructions?|task|orders?|directives?|objective)\s*[:\-!]", Flags, TimeoutMs)]
    private static partial Regex NewInstructions();

    [GeneratedRegex(@"\byou\s+are\s+(?:now|no\s+longer)\b|\bfrom\s+now\s+on\b.{0,40}\byou\b|\bpretend\s+(?:to\s+be|you\s+are)\b|\bact\s+as\s+(?:an?\s+)?(?:admin|administrator|root|developer|system|unrestricted)\b", Flags, TimeoutMs)]
    private static partial Regex RoleChange();

    [GeneratedRegex(@"<\|im_start\|>|<\|system\|>|\[/?inst\]|<<sys>>|</?system>|(?:^|\n)\s*(?:system|assistant|developer)\s*:|\b(?:system|developer)\s+(?:prompt|message|note|override)\s*[:\-]", Flags, TimeoutMs)]
    private static partial Regex RoleMarker();

    [GeneratedRegex(@"\b(?:email|e-mail|mail|send|forward|post|upload|share|transmit|exfiltrate|leak|copy|paste|text|dm)\b[^\n]{0,80}?\b(?:to|at|into)\b\s*:?\s*(?:[\w.+-]+@[\w-]+\.[\w.-]+|https?://\S+|\+?\d[\d\s-]{6,}|#[\w-]+)", Flags, TimeoutMs)]
    private static partial Regex SendTo();

    [GeneratedRegex(@"\b(?:always\s+)?b?cc\b\s*:?\s*[\w.+-]+@[\w-]+\.[\w.-]+|\b(?:email|e-mail|mail|send|forward)\s+(?:this|it|them|these|the\s+\w+|all|everything|your|my|our)\b[^\n.]{0,40}\bto\b", Flags, TimeoutMs)]
    private static partial Regex SendThis();

    [GeneratedRegex(@"\b(?:run|execute|exec)\s+(?:this|the\s+following|these|that|it)\b|\bcurl\s+[^|\n]{0,200}\|\s*(?:ba|z)?sh\b|\biex\s*\(|\binvoke-expression\b|\bpowershell(?:\.exe)?\s+-(?:e|en|enc|encodedcommand)\b|\brm\s+-rf\s+[/~*]|\bgit\s+push\s+(?:--force|-f)\b|\b(?:pip3?|npm|yarn|pnpm|uv)\b[^\n]{0,80}--(?:extra-)?(?:index-url|registry)\s*=?\s*https?://", Flags, TimeoutMs)]
    private static partial Regex RunThis();

    [GeneratedRegex(@"\b(?:owner|user|admin|administrator|operator|boss|human|manager)\b.{0,30}?\b(?:already|has|have|had|pre-?)\s*(?:approved|authori[sz]ed|allowed|permitted|consented|confirmed|signed\s+off|okayed|ok'?d)\b|\b(?:pre-?approved|already\s+(?:approved|authori[sz]ed|confirmed)|approval\s+(?:was\s+)?granted|no\s+need\s+to\s+(?:ask|confirm|check))\b", Flags, TimeoutMs)]
    private static partial Regex ApprovalClaim();

    [GeneratedRegex(@"\b(?:note|message|instructions?|attention|important)\s+(?:to|for)\s+(?:the\s+|any\s+|all\s+)?(?:ai|assistants?|agents?|llms?|models?|chatbots?|bots?|claude|gpt|codex|gemini|dextra)\b|\b(?:ai|llm|assistant|agent|model|bot)s?\s+(?:reading|processing|summari[sz]ing|parsing|viewing)\s+(?:this|these)\b|\bif\s+you\s+are\s+an?\s+(?:ai|llm|assistant|agent|bot|language\s+model)\b|(?:^|[.\n!?:;]\s*|//\s*|#\s*)(?:attention\s+)?(?:all\s+|any\s+)?(?:ai\s+)?(?:coding\s+)?(?:assistants?|agents?|llms?)\s*:\s*(?:please\s+)?(?:also\s+)?(?:log\s*in|sign\s*in|open|run|send|ignore|disregard|create|delete|remove|install|fetch|visit|click|email|forward|upload|download|execute|pay|buy|transfer|add|write|copy|share|post)\b", Flags, TimeoutMs)]
    private static partial Regex AddressedToModel();

    [GeneratedRegex(@"\b(?:add|save|store|write|put)\b.{0,20}\b(?:to|in|into)\s+(?:your\s+)?(?:memory|notes|long-term\s+memory)\b|\bremember\s+(?:to\s+)?always\b|\b(?:schedule|create|set\s+up|add)\s+(?:a\s+|an\s+)?(?:new\s+)?(?:recurring\s+|daily\s+|standing\s+)?(?:duty|task|job|cron|watcher|reminder|reserve\s+rule)\b|\bgrant\s+(?:yourself|me|this\s+agent|access|permission)\b|\bcreate\s+(?:a\s+|an\s+)?(?:new\s+)?(?:assistant|astur|agent)\b", Flags, TimeoutMs)]
    private static partial Regex StandingChange();

    [GeneratedRegex(@"\b(?:do\s+not|don'?t|never)\s+(?:tell|inform|mention|alert|notify|ask|warn|show)\s+(?:the\s+|your\s+)?(?:user|owner|human|operator)\b|\bwithout\s+(?:asking|telling|notifying)\s+(?:the\s+|your\s+)?(?:user|owner|human)\b", Flags, TimeoutMs)]
    private static partial Regex Secrecy();

    [GeneratedRegex(@"\b(?:buy|purchase|order|pay|transfer|wire|send|donate|tip)\b[^\n]{0,40}?(?:\$|usd|eur|gbp|inr|pkr|rs\.?|₹|€|£)\s?\d", Flags, TimeoutMs)]
    private static partial Regex MoneyAsk();

    // ---- Hindi (Devanagari) ----

    [GeneratedRegex(@"(?:निर्देश|हिदायत|आदेश)[^।\n]{0,40}(?:अनदेखा|नज़?रअंदाज़?|भूल)|(?:अनदेखा|नज़?रअंदाज़?|भूल)[^।\n]{0,30}(?:निर्देश|हिदायत)", Flags, TimeoutMs)]
    private static partial Regex HindiIgnore();

    // email/mail karo, karein, kar do, kar dein; bhejo, bhejein, bhej do, bhej dein.
    [GeneratedRegex(@"(?:ई-?मेल|मेल)\s*(?:करो|करें|कर\s*दो|कर\s*दें)|भेज(?:ो|ें|\s*दो|\s*दें)", Flags, TimeoutMs)]
    private static partial Regex HindiSend();

    // malik/upyogkarta ... manzoori/anumati/ijazat de di (hai).
    [GeneratedRegex(@"(?:मालिक|उपयोगकर्ता|यूज़?र)[^।\n]{0,40}(?:मंज़?ूरी|अनुमति|इजाज़?त)", Flags, TimeoutMs)]
    private static partial Regex HindiApproved();

    // ---- Urdu (Arabic script) ----

    [GeneratedRegex(@"(?:ہدایات|ہدایت|احکامات)[^۔\n]{0,40}(?:نظر\s*انداز|بھول)|(?:نظر\s*انداز|بھول)[^۔\n]{0,30}(?:ہدایات|ہدایت)", Flags, TimeoutMs)]
    private static partial Regex UrduIgnore();

    // ای میل / میل کرو، کریں، کر دو، کر دیں؛ بھیجو، بھیجیں، بھیج دو، بھیج دیں
    [GeneratedRegex(@"(?:ای\s*میل|میل)\s*(?:کرو|کریں|کر\s*دو|کر\s*دیں)|بھیج(?:و|یں|\s*دو|\s*دیں)", Flags, TimeoutMs)]
    private static partial Regex UrduSend();

    [GeneratedRegex(@"(?:مالک|صارف)[^۔\n]{0,40}(?:منظوری|اجازت)", Flags, TimeoutMs)]
    private static partial Regex UrduApproved();

    // ---- Hindi and Urdu written in Latin letters ----

    [GeneratedRegex(@"\b(?:pichh?le|purane|saare|sab|tamam)\b.{0,25}\b(?:nirdesh|hidayat|hidayaat|instructions?|hukm)\b.{0,25}\b(?:ignore|bhool|bhul|nazar\s*andaz|nazarandaz)|\b(?:ignore|nazar\s*andaz|nazarandaz)\s+(?:karo|karein|kar\s*do|kardo|kar\s*dein)\b", Flags, TimeoutMs)]
    private static partial Regex RomanIgnore();

    [GeneratedRegex(@"\b(?:email|e-mail|mail|bhej)\s*(?:kar\s*do|kardo|karo|karein|kar\s*dein|do|dein|dena)\b|\b(?:bhejo|bhejein|bhejiye)\b", Flags, TimeoutMs)]
    private static partial Regex RomanSend();

    [GeneratedRegex(@"\b(?:malik|maalik|owner|sahab|sahib)\s*(?:ne)?\b.{0,25}\b(?:approve|manzoori|manzuri|ijazat|ijaazat|anumati|ok)\b.{0,15}\b(?:kar\s*di|de\s*di|dedi|kar\s*diya|kardiya|hai|ho\s*gaya)\b", Flags, TimeoutMs)]
    private static partial Regex RomanApproved();

    private static readonly (string Name, Func<Regex> Pattern)[] Patterns =
    [
        ("ignore-rules", IgnoreRules), ("new-instructions", NewInstructions), ("role-change", RoleChange), ("role-marker", RoleMarker),
        ("send-to", SendTo), ("send-this", SendThis), ("run-this", RunThis), ("approval-claim", ApprovalClaim),
        ("addressed-to-model", AddressedToModel), ("standing-change", StandingChange), ("secrecy", Secrecy), ("money", MoneyAsk),
        ("hindi-ignore", HindiIgnore), ("hindi-send", HindiSend), ("hindi-approved", HindiApproved),
        ("urdu-ignore", UrduIgnore), ("urdu-send", UrduSend), ("urdu-approved", UrduApproved),
        ("roman-ignore", RomanIgnore), ("roman-send", RomanSend), ("roman-approved", RomanApproved),
    ];

    /// <summary>The first instructions found in the text, or null. Never throws.</summary>
    public static InstructionFinding? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var scan = text.Length <= 2 * ScanChars ? text : text[..ScanChars] + "\n" + text[^ScanChars..];
        string normal;
        try { normal = Normalize(scan); }
        catch (ArgumentException) { normal = scan.ToLowerInvariant(); }
        foreach (var (name, pattern) in Patterns)
        {
            try
            {
                var match = pattern().Match(normal);
                if (match.Success) return new InstructionFinding(Quote(match.Value), name);
            }
            catch (RegexMatchTimeoutException)
            {
                // Text the patterns cannot finish on is reported, not passed as clean.
                return new InstructionFinding("text too tangled to read", "timeout");
            }
        }
        return null;
    }

    /// <summary>
    /// The text as the patterns read it: hidden characters removed (zero-width, bidi controls, soft hyphens), full-width
    /// and other compatibility forms folded (NFKC), look-alike Cyrillic and Greek letters read as Latin, lower case, and
    /// each run of spaces as one.
    /// </summary>
    public static string Normalize(string text)
    {
        var folded = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(folded.Length);
        var space = false;
        foreach (var raw in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.Format) continue;
            var ch = LookAlike(char.ToLowerInvariant(raw));
            if (ch is ' ' or '\t' or ' ' or '\r')
            {
                if (!space) builder.Append(' ');
                space = true;
                continue;
            }
            space = false;
            builder.Append(ch);
        }
        return builder.ToString();
    }

    private static char LookAlike(char ch) => ch switch
    {
        'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c', 'у' => 'y', 'х' => 'x',
        'і' => 'i', 'ј' => 'j', 'ѕ' => 's', 'ԁ' => 'd', 'ɡ' => 'g', 'һ' => 'h', 'ї' => 'i',
        'ο' => 'o', 'α' => 'a', 'ι' => 'i', 'ν' => 'v', 'ρ' => 'p', 'τ' => 't', 'κ' => 'k',
        'ѵ' => 'v', 'ӏ' => 'l', 'ґ' => 'r',
        _ => ch,
    };

    private static string Quote(string match)
    {
        var line = string.Join(' ', match.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= QuoteChars ? line : line[..QuoteChars].TrimEnd() + "…";
    }
}
