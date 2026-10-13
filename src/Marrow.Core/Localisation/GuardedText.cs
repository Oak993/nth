// PORT-ONLY: web surfaces W22 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "localised lists", 14 W22; ED-NEW-web-w22-1). No Swift counterpart.
// What a guard reads is the name a page (or a window) gives a control, in any script and any spelling the page happens to use. This folds a name
// to one form, so a word listed once matches the way a page may write it, and so a page cannot dodge a list by writing the same word with
// characters nobody sees.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Localisation;

public static class GuardedText
{
    /// <summary>
    /// <paramref name="text"/> made comparable: compatibility-normalised (full-width and presentation forms become the plain letters), lower
    /// case, white space collapsed to one space, every invisible format character gone (zero-width joiners, direction marks, soft hyphens, the
    /// byte order mark), the Arabic marks and tatweel that carry no meaning in a name gone, the Devanagari nukta gone, and the letters Urdu
    /// writes in more than one way folded to one (Arabic and Urdu yeh, kaf and heh; chandrabindu and anusvara). The same fold runs over the lists,
    /// so both sides agree. Never throws.
    /// </summary>
    public static string Fold(string? text) => Fold(text, spaceAtZeroWidthSpace: false);

    /// <summary>
    /// The same fold, but a zero-width space counts as a space, as scripts that write no spaces use it. A guard tries <see cref="Fold"/> first
    /// (a page may hide a zero-width space inside a word to dodge a list) and this second (a page may use it between two words).
    /// </summary>
    public static string FoldSeparated(string? text) => Fold(text, spaceAtZeroWidthSpace: true);

    private static string Fold(string? text, bool spaceAtZeroWidthSpace)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalised = Normalised(text);
        var builder = new StringBuilder(normalised.Length);
        var gap = false;
        foreach (var c in normalised)
        {
            if (char.IsWhiteSpace(c) || (spaceAtZeroWidthSpace && c == '\u200B'))
            {
                gap = builder.Length > 0;
                continue;
            }
            if (Ignorable(c)) continue;
            if (gap)
            {
                builder.Append(' ');
                gap = false;
            }
            builder.Append(Unified(c));
        }
        return builder.ToString().ToLowerInvariant();
    }

    // string.Normalize throws on text that is not well-formed UTF-16 (a lone surrogate, which a page can put in a name). The guard must not
    // fail on it: the text is compared as it is.
    private static string Normalised(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static bool Ignorable(char c) =>
        c is '\u0640' or '\u0670' or '\u093C' or (>= '\u064B' and <= '\u065F')
        || char.GetUnicodeCategory(c) == UnicodeCategory.Format;

    private static char Unified(char c) => c switch
    {
        '\u064A' or '\u0649' => '\u06CC',
        '\u0643' => '\u06A9',
        '\u0647' => '\u06C1',
        '\u0901' => '\u0902',
        _ => c,
    };
}
