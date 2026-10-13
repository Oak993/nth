// PORT-ONLY: web surfaces W22 (DESIGN.md 5.10 "Guarded actions ... localised lists", 14 W22; Tactus DESIGN 4.3; ED-NEW-web-w22-1). No Swift counterpart.
// The guarded-action words of every language nth keeps a list for, behind one question: "what guarded word is this control named?". Tactus
// (PcPolicy.GuardedWord) and the web guard (WebAgentPolicy) both ask it here, so the two products ask the user about the same words.
using System.Text.RegularExpressions;

namespace Marrow.Core.Localisation;

public sealed partial class GuardedWords
{
    /// <summary>
    /// The guarded words, as the English word each stands for in every language's list. WebAgentPolicy.CategoryOf and Tactus's card
    /// (ConfirmContent) switch on these.
    /// </summary>
    public static IReadOnlyList<string> Canonical { get; } =
    [
        "send", "delete", "delete account", "pay", "buy", "purchase", "order", "submit", "publish", "post", "transfer", "install", "uninstall",
        "sign out",
    ];

    private const string ResourcePrefix = "Marrow.Core.Guarded.";

    private static readonly Lazy<GuardedWords> embedded = new(LoadEmbedded);

    private readonly GuardedWordList[] lists;
    private readonly Dictionary<string, string> identifiers = new(StringComparer.OrdinalIgnoreCase);

    // An identifier splits at a lower-to-upper change ("btnDelete"), around a run of capitals ("HTMLSend") and at digits.
    [GeneratedRegex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierTokens();

    public GuardedWords(IEnumerable<GuardedWordList> lists)
    {
        this.lists = [.. lists];
        foreach (var list in this.lists)
            foreach (var (token, word) in list.Identifiers)
                identifiers.TryAdd(token, word);
    }

    /// <summary>The lists nth ships, read from the embedded files, English first (<see cref="UiLanguages.Guarded"/> gives the order).</summary>
    public static GuardedWords Shared => embedded.Value;

    /// <summary>One list per language, in the order a name is tried: English first, then the others as <see cref="UiLanguages.Guarded"/> names them.</summary>
    public IReadOnlyList<GuardedWordList> Lists => lists;

    public IReadOnlyList<string> Languages => [.. lists.Select(list => list.Language)];

    /// <summary>
    /// The languages among <paramref name="languages"/> that have no list. The test that guards <see cref="UiLanguages.Shipped"/> asks this:
    /// a language nth is translated into without a guarded-action list would leave its pages unguarded in their own words.
    /// </summary>
    public IReadOnlyList<string> LanguagesWithoutList(IEnumerable<string> languages) =>
        [.. languages.Where(language => !lists.Any(list => string.Equals(list.Language, language, StringComparison.Ordinal)))];

    /// <summary>Reads lists from files given as (name, json), for the embedded set and for tests.</summary>
    public static GuardedWords FromJson(IEnumerable<(string Name, string Json)> files)
    {
        var parsed = files.Select(file => GuardedWordList.Parse(file.Name, file.Json)).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in parsed)
            if (!seen.Add(list.Language)) throw new InvalidDataException("Two guarded-word files are for \"" + list.Language + "\".");
        return new GuardedWords(parsed.OrderBy(list => Rank(list.Language)).ThenBy(list => list.Language, StringComparer.Ordinal));
    }

    /// <summary>
    /// The guarded word a control's visible name holds (the English word it stands for, lower case), or null. Every language's list is
    /// tried, the English list first, so a page in Hindi or Urdu is guarded whatever language nth's own window is in. Of several words in one
    /// name the longest wins ("Publish post" is "publish"; "Delete account" is the phrase).
    /// </summary>
    public string? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var folded = GuardedText.Fold(name);
        var separated = GuardedText.FoldSeparated(name);
        foreach (var list in lists)
        {
            if (list.FindWord(folded) is { } word) return word;
            if (!string.Equals(separated, folded, StringComparison.Ordinal) && list.FindWord(separated) is { } spaced) return spaced;
        }
        return null;
    }

    /// <summary>The guarded word an identifier names ("SendButton", "btnDelete", "id_pay_now", "SignOut"), or null. Identifiers are code, not words a person reads.</summary>
    public string? FindIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        var tokens = IdentifierTokens().Matches(identifier).Select(match => match.Value.ToLowerInvariant()).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i + 1 < tokens.Count && identifiers.TryGetValue(tokens[i] + tokens[i + 1], out var pair)) return pair;
            if (identifiers.TryGetValue(tokens[i], out var word)) return word;
        }
        return null;
    }

    /// <summary>True when the text names a payment the model must never complete ("Place your order", "Buy now") in any listed language.</summary>
    public bool IsPaymentPhrase(string? text) => Matches(text, (list, folded) => list.HasPaymentPhrase(folded));

    /// <summary>True when the text names an account sign-up ("Sign up", "Create an account") in any listed language.</summary>
    public bool IsAccountPhrase(string? text) => Matches(text, (list, folded) => list.HasAccountPhrase(folded));

    /// <summary>True when the text (a name or a class) names a CAPTCHA in any listed language.</summary>
    public bool IsCaptchaPhrase(string? text) => Matches(text, (list, folded) => list.HasCaptchaPhrase(folded));

    private bool Matches(string? text, Func<GuardedWordList, string, bool> test)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var folded = GuardedText.Fold(text);
        var separated = GuardedText.FoldSeparated(text);
        foreach (var list in lists)
            if (test(list, folded) || (!string.Equals(separated, folded, StringComparison.Ordinal) && test(list, separated))) return true;
        return false;
    }

    private static int Rank(string language)
    {
        for (var i = 0; i < UiLanguages.Guarded.Count; i++)
            if (string.Equals(UiLanguages.Guarded[i], language, StringComparison.Ordinal)) return i;
        return UiLanguages.Guarded.Count;
    }

    private static GuardedWords LoadEmbedded()
    {
        var assembly = typeof(GuardedWords).Assembly;
        var files = new List<(string Name, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("Missing resource " + resource);
            using var reader = new StreamReader(stream);
            files.Add((resource, reader.ReadToEnd()));
        }
        var set = FromJson(files);
        // The file's name says its language too; a file filed under the wrong name would be a list nobody looks for.
        foreach (var (name, _) in files)
        {
            var language = name[(ResourcePrefix.Length + "guarded-".Length)..^".json".Length];
            if (!set.Languages.Contains(language, StringComparer.Ordinal))
                throw new InvalidDataException(name + " does not hold a list for \"" + language + "\".");
        }
        return set;
    }
}
