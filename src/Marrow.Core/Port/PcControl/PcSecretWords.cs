// PORT-ONLY: Tactus, pill v2 (OD-0145, ED-NEW-pcctl-12; DESIGN.md 10.4, 10.9). Secrets never go through the pill: a pc_ask
// whose text asks for a password, a PIN, a one-time code, a card or bank number, a recovery phrase or a key is shown only
// in the chat ("Answer this in the chat"), and the pill offers no composer for it. The check is a fixed word list in
// English, Hindi and Urdu, matched on whole words, so "PIN" matches and "pinned" does not. A false match only moves a
// question to the chat, which is the safe side; a missed secret is what the list exists to prevent, so it leans wide.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.PcControl;

public static class PcSecretWords
{
    // Each entry is one phrase, matched as a run of whole words (case-insensitive). Written for nth; no list from elsewhere.
    private static readonly string[] English =
    [
        "password", "passwords", "passcode", "passphrase", "pin code", "pin number", "pincode", "your pin", "my pin", "the pin",
        "card pin", "atm pin", "sim pin", "pin for",
        "one-time code", "one time code", "one-time password", "one time password", "otp", "2fa code", "two-factor code",
        "verification code", "security code", "authentication code", "login code", "sign-in code", "auth code", "mfa code",
        "card number", "credit card", "debit card", "cvv", "cvc", "cvv2",
        "bank account", "account number", "iban", "routing number", "sort code", "swift code",
        "recovery phrase", "seed phrase", "secret phrase", "mnemonic", "recovery code", "backup code", "backup codes",
        "private key", "secret key", "api key", "access key", "recovery key", "encryption key", "security key", "license key",
        "product key", "access token", "secret token", "credentials", "social security number", "ssn",
    ];

    // Hindi (Devanagari), including the English loan words as Hindi writes them.
    private static readonly string[] Hindi =
    [
        "पासवर्ड", "पासकोड", "पिन", "पिन कोड", "पिनकोड", "ओटीपी", "वन टाइम पासवर्ड", "वन टाइम कोड", "सत्यापन कोड",
        "सुरक्षा कोड", "गुप्त कोड", "गुप्त शब्द", "कूटशब्द", "कार्ड नंबर", "कार्ड संख्या", "क्रेडिट कार्ड", "डेबिट कार्ड", "सीवीवी",
        "बैंक खाता", "खाता संख्या", "खाता नंबर", "अकाउंट नंबर", "आईएफएससी", "रिकवरी फ्रेज़", "रिकवरी फ्रेज", "रिकवरी कोड",
        "निजी कुंजी", "गुप्त कुंजी", "एपीआई कुंजी", "एपीआई की", "प्राइवेट की", "सीक्रेट की",
    ];

    // Urdu (Arabic script), including the English loan words as Urdu writes them.
    private static readonly string[] Urdu =
    [
        "پاس ورڈ", "پاسورڈ", "پاس کوڈ", "پن", "پن کوڈ", "او ٹی پی", "اوٹی پی", "تصدیقی کوڈ", "حفاظتی کوڈ", "خفیہ کوڈ",
        "خفیہ نمبر", "خفیہ لفظ", "کارڈ نمبر", "کریڈٹ کارڈ", "ڈیبٹ کارڈ", "سی وی وی", "بینک اکاؤنٹ", "بینک کھاتہ",
        "اکاؤنٹ نمبر", "کھاتہ نمبر", "ریکوری فریز", "ریکوری کوڈ", "نجی کلید", "خفیہ کلید", "پرائیویٹ کی", "سیکرٹ کی",
    ];

    private static readonly string[][] Phrases = [.. English.Concat(Hindi).Concat(Urdu).Select(Words).Where(words => words.Length > 0)];

    /// <summary>True when <paramref name="text"/> asks for a secret: the question then goes only to the chat.</summary>
    public static bool AsksForSecret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = Words(text);
        foreach (var phrase in Phrases)
        {
            for (var start = 0; start + phrase.Length <= words.Length; start++)
            {
                var match = true;
                for (var i = 0; i < phrase.Length && match; i++) match = words[start + i] == phrase[i];
                if (match) return true;
            }
        }
        // "PIN" in capitals is the secret; "pin" alone is often the verb (pin a tab), so in lower case only the phrases count.
        return Words(text, lower: false).Any(word => word is "PIN" or "PINs");
    }

    /// <summary>
    /// The text as whole words: letters, digits and the vowel signs and marks of Devanagari and Arabic script stay in a
    /// word (they are marks, not letters, in Unicode); hyphens join; everything else splits. Zero-width joiners vanish.
    /// </summary>
    public static string[] Words(string text) => Words(text, lower: true);

    private static string[] Words(string text, bool lower)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in normalized)
        {
            if (c is '‌' or '‍') continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            var inWord = char.IsLetterOrDigit(c) || c is '-' or '\'' or '’'
                || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
            if (inWord)
            {
                word.Append(c == '’' ? '\'' : c);
                continue;
            }
            Flush();
        }
        Flush();
        return [.. words];

        void Flush()
        {
            if (word.Length == 0) return;
            var finished = word.ToString().Trim('-', '\'');
            word.Clear();
            if (finished.Length == 0) return;
            words.Add(lower ? finished.ToLowerInvariant() : finished);
        }
    }
}
