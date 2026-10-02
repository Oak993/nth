namespace Marrow.Core.Models;

// SettingsView.swift:719-727 "Get a <name> key"; OD-0072: "a" or "an" by the first sound of the provider's name.
public static class ProviderKeyLink
{
    // Letters whose spoken name starts with a vowel sound: A, E, F (ef), H (aitch), I, L (el), M (em), N (en), O, R (ar), S (es), X (ex).
    private const string VowelSoundLetters = "AEFHILMNORSX";

    // Words that start with a silent "h", and "u" words that start with a "yoo" sound.
    private static readonly string[] SilentH = ["hour", "honest", "honor", "heir"];
    private static readonly string[] YooU = ["uni", "use", "usu", "uti", "uk", "eu"];

    // All-capital names read as a word, not letter by letter.
    private static readonly string[] SpokenAcronyms = ["NASA", "NATO", "ASCII", "LASER"];

    // The article for a name. A short all-capital word (SDK, FBI) or one with a lower-case first letter (xAI) is spelled out,
    // so its first letter's name decides; otherwise the first letter decides, with the usual "h", "u" and "eu" exceptions.
    public static string Article(string name)
    {
        var word = name.TrimStart();
        var end = 0; while (end < word.Length && char.IsLetter(word[end])) end++;
        var head = word[..end];
        if (head.Length == 0) return "a";
        var spelled = head.Length is >= 2 and <= 4 && (head.All(char.IsUpper) && !SpokenAcronyms.Contains(head, StringComparer.Ordinal) || char.IsLower(head[0]) && head.Skip(1).All(char.IsUpper));
        if (spelled) return VowelSoundLetters.Contains(char.ToUpperInvariant(head[0]), StringComparison.Ordinal) ? "an" : "a";
        var lower = head.ToLowerInvariant();
        if (SilentH.Any(p => lower.StartsWith(p, StringComparison.Ordinal))) return "an";
        if (YooU.Any(p => lower.StartsWith(p, StringComparison.Ordinal))) return "a";
        return "aeiou".Contains(lower[0], StringComparison.Ordinal) ? "an" : "a";
    }

    // The text of the provider key link: "Get a Meta key", "Get an OpenRouter key".
    public static string Text(string displayName) => $"Get {Article(displayName)} {displayName} key";
}

public static class ProviderKeyLinkExtensions
{
    extension(ProviderKind kind)
    {
        public string GetKeyLinkText => ProviderKeyLink.Text(kind.DisplayName);
    }
}
