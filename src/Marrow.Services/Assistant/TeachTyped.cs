// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1 "typed text, with password fields detected and recorded as [secret]", 12.5; unit AS-19; ED-NEW-dextra-as19-2). No Swift twin.
// What a recording keeps of what the owner typed. A password field (UI Automation says IsPassword) is a secret whatever it holds; a text
// the privacy patterns would change (a key, a card number, a one-time code) is a secret too, and the whole text goes, not a masked copy;
// a field nth could not look up keeps only how many characters were typed; any other field keeps its text, cut to a bounded length. A
// secret keeps only its length. The text is what the field held when the owner stopped typing, read by UI Automation: nth never
// records keystrokes.
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What a typed burst keeps: its text (or the secret mark, or nothing), how many characters, and whether it was a secret.</summary>
public readonly record struct TeachKept(string? Text, int Length, bool Secret);

public static class TeachTyped
{
    /// <summary>
    /// The record of one typed burst. <paramref name="observed"/> is the field's value read after the burst (null when it could not be read),
    /// <paramref name="typed"/> how many characters the owner typed.
    /// </summary>
    public static TeachKept Record(TeachElement? element, string? observed, int typed)
    {
        var length = Math.Max(0, typed);
        if (element is null || element.IsUnknown) return new TeachKept(null, length, false);
        if (element.IsPassword) return new TeachKept(TeachWords.Secret, length, true);
        if (string.IsNullOrEmpty(observed)) return new TeachKept(null, length, false);
        if (MemoryGuard.HoldsSecret(observed)) return new TeachKept(TeachWords.Secret, length, true);
        var kept = observed.Length <= TeachLimits.TypedChars ? observed : CutClean(observed, TeachLimits.TypedChars);
        return new TeachKept(kept, length, false);
    }

    // Cut to a length, never inside a surrogate pair.
    private static string CutClean(string text, int limit)
    {
        var cut = limit;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut];
    }
}
