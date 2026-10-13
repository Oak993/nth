// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22: "guarded-action lists for every UI language nth ships ... every new string in the resource files";
// ED-NEW-web-w22-1). No Swift counterpart. The one place that says which languages nth speaks.
using System.Globalization;

namespace Marrow.Core.Localisation;

public static class UiLanguages
{
    /// <summary>The language every string exists in, and the one a missing translation falls back to.</summary>
    public const string Fallback = "en";

    /// <summary>
    /// The languages nth's own words are written in. The pinned source ships English only (ED-132: the pinned app has an English localisation only), so this is
    /// "en"; a language is added here, with a complete string file for it and a guarded-word list, in one step (the tests refuse a language that
    /// lacks either).
    /// </summary>
    public static IReadOnlyList<string> Shipped { get; } = ["en"];

    /// <summary>
    /// The languages that have a guarded-action list. Every shipped language must have one. Hindi and Urdu are on it as well because the owner
    /// works in them and the pages he opens are written in them (Tactus DESIGN 4.3: "English, Hindi, Urdu first; more as added"): a guard reads
    /// the page's words, not nth's, so the lists of all of them are always tried. English comes first.
    /// </summary>
    public static IReadOnlyList<string> Guarded { get; } = ["en", "hi", "ur"];

    /// <summary>
    /// The language to speak for <paramref name="culture"/>: the culture's own tag when <paramref name="available"/> has it ("ur-PK" when there is
    /// such a file), else its language ("ur"), else <see cref="Fallback"/>. The invariant culture and an unknown one speak the fallback.
    /// </summary>
    public static string Resolve(CultureInfo? culture, IEnumerable<string> available)
    {
        var have = available as ICollection<string> ?? [.. available];
        var name = culture?.Name ?? "";
        if (name.Length == 0) return Fallback;
        foreach (var tag in have)
            if (string.Equals(tag, name, StringComparison.OrdinalIgnoreCase)) return tag;
        var cut = name.IndexOf('-', StringComparison.Ordinal);
        var language = cut < 0 ? name : name[..cut];
        foreach (var tag in have)
            if (string.Equals(tag, language, StringComparison.OrdinalIgnoreCase)) return tag;
        return Fallback;
    }
}
