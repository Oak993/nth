// PORT-ONLY: web surfaces W22 (DESIGN.md 5.10, 14 W22; Tactus DESIGN 4.3 "localised word lists"; ED-NEW-web-w22-1). No Swift counterpart.
// One language's guarded-action words, read from its embedded file (Localisation/Guarded/guarded-<language>.json).
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.Core.Localisation;

/// <summary>
/// One language's lists: the guarded words (each as the English word it stands for), the identifier tokens, and the phrases of the three hard
/// denies a web guard keeps (a payment, an account sign-up, a CAPTCHA). <see cref="WholeWords"/> says how the words and the payment and
/// account phrases are matched: as whole words (scripts that put spaces between words and have no marks inside a word) or as text found
/// anywhere in the name (Devanagari and Arabic script, whose vowel signs and marks the regular-expression word boundary does not treat as
/// letters). CAPTCHA phrases are always found anywhere in the name or class.
/// </summary>
public sealed class GuardedWordList
{
    private readonly PhraseMatcher words;
    private readonly PhraseMatcher payment;
    private readonly PhraseMatcher account;
    private readonly PhraseMatcher captcha;

    private GuardedWordList(string language, bool wholeWords, IReadOnlyDictionary<string, IReadOnlyList<string>> wordForms,
        IReadOnlyDictionary<string, string> identifiers, IReadOnlyList<string> paymentPhrases, IReadOnlyList<string> accountPhrases,
        IReadOnlyList<string> captchaPhrases)
    {
        Language = language;
        WholeWords = wholeWords;
        Words = wordForms;
        Identifiers = identifiers;
        Payment = paymentPhrases;
        Account = accountPhrases;
        Captcha = captchaPhrases;
        words = new PhraseMatcher(wordForms.SelectMany(pair => pair.Value.Select(form => (form, pair.Key))), wholeWords);
        payment = new PhraseMatcher(paymentPhrases.Select(phrase => (phrase, phrase)), wholeWords);
        account = new PhraseMatcher(accountPhrases.Select(phrase => (phrase, phrase)), wholeWords);
        captcha = new PhraseMatcher(captchaPhrases.Select(phrase => (phrase, phrase)), wholeWords: false);
    }

    /// <summary>The language tag of the file ("en", "hi", "ur").</summary>
    public string Language { get; }

    public bool WholeWords { get; }

    /// <summary>Each guarded word (as the English word) and the forms this language writes it in, as listed.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Words { get; }

    /// <summary>Identifier tokens ("signout", "btnDelete" split into "btn" and "delete") and the guarded word each stands for.</summary>
    public IReadOnlyDictionary<string, string> Identifiers { get; }

    public IReadOnlyList<string> Payment { get; }

    public IReadOnlyList<string> Account { get; }

    public IReadOnlyList<string> Captcha { get; }

    /// <summary>The guarded word in an already folded name (<see cref="GuardedText.Fold"/>), or null.</summary>
    public string? FindWord(string folded) => words.Find(folded);

    public bool HasPaymentPhrase(string folded) => payment.Find(folded) is not null;

    public bool HasAccountPhrase(string folded) => account.Find(folded) is not null;

    public bool HasCaptchaPhrase(string folded) => captcha.Find(folded) is not null;

    /// <summary>Reads one language file; <paramref name="name"/> is the file's name in an error. A malformed file throws <see cref="InvalidDataException"/>.</summary>
    public static GuardedWordList Parse(string name, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException(name + ": the file is not an object.");
            var language = RequiredText(root, "language", name);
            var wholeWords = RequiredText(root, "match", name) switch
            {
                "word" => true,
                "text" => false,
                var other => throw new InvalidDataException(name + ": \"match\" is \"" + other + "\", not \"word\" or \"text\"."),
            };
            var wordForms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var property in RequiredObject(root, "words", name).EnumerateObject())
            {
                if (!GuardedWords.Canonical.Contains(property.Name, StringComparer.Ordinal))
                    throw new InvalidDataException(name + ": \"" + property.Name + "\" is not a guarded word.");
                if (!wordForms.TryAdd(property.Name, Strings(property.Value, name + " words." + property.Name)))
                    throw new InvalidDataException(name + ": \"" + property.Name + "\" is listed twice.");
            }
            // One folded form must stand for one word, or the order of the lists would decide which word a button is.
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (word, forms) in wordForms)
            {
                foreach (var form in forms)
                {
                    var folded = GuardedText.Fold(form);
                    if (owner.TryGetValue(folded, out var other) && !string.Equals(other, word, StringComparison.Ordinal))
                        throw new InvalidDataException(name + ": \"" + form + "\" stands for both \"" + other + "\" and \"" + word + "\".");
                    owner[folded] = word;
                }
            }
            var identifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("identifiers", out var identifierValue) && identifierValue.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in identifierValue.EnumerateObject())
                {
                    var word = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (word is null || !GuardedWords.Canonical.Contains(word, StringComparer.Ordinal))
                        throw new InvalidDataException(name + ": identifier \"" + property.Name + "\" does not name a guarded word.");
                    identifiers[property.Name] = word;
                }
            }
            return new GuardedWordList(language, wholeWords, wordForms, identifiers, Strings(RequiredArray(root, "payment", name), name + " payment"),
                Strings(RequiredArray(root, "account", name), name + " account"), Strings(RequiredArray(root, "captcha", name), name + " captcha"));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(name + ": " + error.Message, error);
        }
    }

    private static string RequiredText(JsonElement root, string property, string name) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException(name + ": \"" + property + "\" is missing.");

    private static JsonElement RequiredObject(JsonElement root, string property, string name) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException(name + ": \"" + property + "\" is missing.");

    private static JsonElement RequiredArray(JsonElement root, string property, string name) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException(name + ": \"" + property + "\" is missing.");

    private static List<string> Strings(JsonElement array, string where)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new InvalidDataException(where + ": a list was expected.");
        var forms = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } form || GuardedText.Fold(form).Length == 0)
                throw new InvalidDataException(where + ": every form must be a non-empty string.");
            if (forms.Contains(form, StringComparer.Ordinal)) throw new InvalidDataException(where + ": \"" + form + "\" is listed twice.");
            forms.Add(form);
        }
        return forms;
    }

    /// <summary>Finds the best of a set of folded forms in a folded text.</summary>
    private sealed class PhraseMatcher
    {
        private readonly Regex? whole;
        private readonly Dictionary<string, string> valueByForm = new(StringComparer.Ordinal);
        private readonly (string Form, string Value)[] anywhere;

        public PhraseMatcher(IEnumerable<(string Form, string Value)> entries, bool wholeWords)
        {
            var folded = entries.Select(entry => (Form: GuardedText.Fold(entry.Form), entry.Value)).Where(entry => entry.Form.Length > 0).ToList();
            foreach (var (form, value) in folded) valueByForm.TryAdd(form, value);
            // The longest form first, so "uninstall" is found before "install" and "delete account" before "delete".
            var ordered = folded.OrderByDescending(entry => entry.Form.Length).ToArray();
            if (wholeWords && ordered.Length > 0)
            {
                var alternatives = ordered.Select(entry => Regex.Escape(entry.Form)).Distinct(StringComparer.Ordinal);
                whole = new Regex(@"\b(?:" + string.Join('|', alternatives) + @")\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                anywhere = [];
            }
            else
            {
                anywhere = ordered;
            }
        }

        public string? Find(string folded)
        {
            if (folded.Length == 0) return null;
            if (whole is not null)
            {
                string? best = null;
                foreach (Match match in whole.Matches(folded))
                    if (best is null || match.Length > best.Length) best = match.Value;
                return best is null ? null : valueByForm[best];
            }
            foreach (var (form, value) in anywhere)
                if (folded.Contains(form, StringComparison.Ordinal)) return value;
            return null;
        }
    }
}
