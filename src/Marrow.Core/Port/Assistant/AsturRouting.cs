// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.3 "Talking to one directly"; unit AS-16; ED-NEW-dextra-as16-6). No Swift twin.
// Talk by name. "@Channel A, start the next video" in text, or "Channel A, start the next video" by voice, goes straight to Channel
// A's home thread. A name that fits more than one Astur ("Channel" with Channel A and Channel B) is not guessed: the owner is asked
// "Which one?". A message that names two Asturs spans areas and stays with Dextra, which sends one piece to each (9.3). A name that
// matches nothing is no route at all, so "@" in an address or a handle changes nothing. Pure: names are compared by their letters
// and digits without case, accents or apostrophes; routing never reads a retired Astur.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>Where a message the owner typed or said goes.</summary>
public enum AsturRouteKind
{
    /// <summary>To nobody in particular: the chat it was typed in keeps it, unchanged.</summary>
    None,
    /// <summary>To Dextra by name ("@Dextra", "Dextra, ..."), the name taken off.</summary>
    Dextra,
    /// <summary>To one Astur, the name taken off.</summary>
    Astur,
    /// <summary>To one of several that fit: ask which.</summary>
    Which,
}

/// <summary>A routing decision. <see cref="Message"/> is what the addressee receives (the name taken off when it began the message).</summary>
public sealed record AsturRoute(AsturRouteKind Kind, string Message)
{
    public AssistantProfile? Target { get; init; }

    /// <summary>For Which: the Asturs the name fits, in the order the team lists them.</summary>
    public IReadOnlyList<AssistantProfile> Candidates { get; init; } = [];

    public static AsturRoute Unrouted(string message) => new(AsturRouteKind.None, message);
}

public static partial class AsturRouting
{
    /// <summary>Words that may open a spoken request before the name ("Hey Channel A, ...", "Ask Channel A to ...").</summary>
    private static readonly HashSet<string> Openers = new(StringComparer.Ordinal) { "hey", "hi", "hello", "ok", "okay", "yo", "ask", "tell" };

    /// <summary>Of the openers, the ones that hand a request on, so a "to" or "that" after the name is not part of the request.</summary>
    private static readonly HashSet<string> Relays = new(StringComparer.Ordinal) { "ask", "tell" };

    [GeneratedRegex(@"[\p{L}\p{N}\p{M}]+(?:['’][\p{L}\p{N}\p{M}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    /// <summary>
    /// What two names are the same by: letters and digits only, lower case, accents and apostrophes gone, one space between words.
    /// "Café  Désk" and "cafe desk" are one name.
    /// </summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return string.Join(' ', WordPattern().Matches(text.Normalize(NormalizationForm.FormKD)).Select(m => WordKey(m.Value)).Where(w => w.Length > 0));
    }

    // Letters and digits in lower case; the apostrophe gone; an accent on a Latin letter gone ("Café" is "cafe"). The marks of other scripts
    // (a Hindi vowel sign, an Arabic vowel) are part of the letter and stay, so two different names never fold into one.
    private static string WordKey(string word)
    {
        var folded = new StringBuilder(word.Length);
        var previous = (char)0;
        foreach (var c in word.Normalize(NormalizationForm.FormKD))
        {
            if (c is '\'' or '’') continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            var mark = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            if (mark && IsLatin(previous)) continue;
            if (!mark && !char.IsLetterOrDigit(c)) continue;
            folded.Append(char.ToLowerInvariant(c));
            previous = c;
        }
        return folded.ToString();
    }

    private static bool IsLatin(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ɏ' or >= 'Ḁ' and <= 'ỿ';

    private readonly record struct Word(string Key, int Start, int End);

    private sealed record Name(AssistantProfile Owner, string[] Words);

    /// <summary>
    /// Where <paramref name="text"/> goes. Typed text routes on an "@" mention at the start (a mention elsewhere is left in the message
    /// and still counts for the "two Asturs" rule); spoken text routes on a name first, after at most one opener, when a comma, colon or
    /// dash follows it, an opener ("Hey", "Ask", "Tell") came before it, or it is the whole utterance.
    /// </summary>
    public static AsturRoute Route(string? text, IReadOnlyList<AssistantProfile> profiles, bool spoken = false)
    {
        var message = text ?? "";
        if (string.IsNullOrWhiteSpace(message)) return AsturRoute.Unrouted(message);
        var names = NamesOf(profiles);
        if (names.Count == 0) return AsturRoute.Unrouted(message);

        // Two Asturs named anywhere in the message: it spans areas, so it stays with Dextra (9.3 "Spanning areas").
        if (MentionedAsturs(message, names, profiles).Count > 1) return AsturRoute.Unrouted(message);

        var words = Words(message);
        var trimmed = message.TrimStart();
        var offset = message.Length - trimmed.Length;
        if (trimmed.Length > 1 && trimmed[0] == '@' && char.IsLetterOrDigit(trimmed[1]))
        {
            var after = Words(message[(offset + 1)..], offset + 1);
            return Match(message, after, 0, names, requireSeparator: false, relayed: false);
        }
        // Spoken text starts with its name: a message that opens with a mark or symbol is not a call.
        if (!spoken || words.Count == 0 || words[0].Start != offset) return AsturRoute.Unrouted(message);

        // After an opener the name needs no separator. An Astur that is itself called "Ask" is still reached by its name alone.
        if (Openers.Contains(words[0].Key) && words.Count > 1
            && Match(message, words, 1, names, requireSeparator: false, relayed: Relays.Contains(words[0].Key)) is { Kind: not AsturRouteKind.None } opened)
            return opened;
        return Match(message, words, 0, names, requireSeparator: true, relayed: false);
    }

    // ---- matching ----

    private static List<Name> NamesOf(IReadOnlyList<AssistantProfile> profiles)
    {
        var names = new List<Name>();
        foreach (var profile in profiles.Where(p => !p.Retired))
        {
            foreach (var text in new[] { profile.Name }.Concat(profile.Aliases))
            {
                var words = Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0) names.Add(new Name(profile, words));
            }
        }
        return names;
    }

    private static List<Word> Words(string text, int shift = 0) =>
        [.. WordPattern().Matches(text).Select(m => new Word(WordKey(m.Value), m.Index + shift, m.Index + m.Length + shift)).Where(w => w.Key.Length > 0)];

    private static AsturRoute Match(string message, List<Word> words, int first, List<Name> names, bool requireSeparator, bool relayed)
    {
        // Every name that begins the words from `first`: whole (all its words match) or partial (the text gave only its first m words).
        var whole = new List<(AssistantProfile Owner, int Length)>();
        var partial = new List<(AssistantProfile Owner, int Length)>();
        foreach (var name in names)
        {
            var matched = 0;
            while (matched < name.Words.Length && first + matched < words.Count && words[first + matched].Key == name.Words[matched]) matched++;
            if (matched == name.Words.Length) whole.Add((name.Owner, matched));
            else if (matched > 0) partial.Add((name.Owner, matched));
        }
        var pool = whole.Count > 0 ? whole : partial;
        if (pool.Count == 0) return AsturRoute.Unrouted(message);
        // A name that only begins a name is not enough to send a message on: it is asked about, even when only one name begins so ("Do you mean Channel B?"
        // when "Channel A" was typed and is gone), never guessed.
        var asked = whole.Count == 0;
        var longest = pool.Max(p => p.Length);
        var owners = pool.Where(p => p.Length == longest).Select(p => p.Owner).DistinctBy(p => p.Id).ToList();
        var last = words[first + longest - 1];
        if (requireSeparator && !FollowedBySeparator(message, last.End)) return AsturRoute.Unrouted(message);
        var rest = Rest(message, last.End, relayed);
        if (owners.Count > 1 || asked) return new AsturRoute(AsturRouteKind.Which, rest) { Candidates = owners };
        var owner = owners[0];
        return owner.IsRoot
            ? new AsturRoute(AsturRouteKind.Dextra, rest) { Target = owner }
            : new AsturRoute(AsturRouteKind.Astur, rest) { Target = owner };
    }

    // A spoken name must stand apart: a comma, colon or dash after it, or nothing at all ("Store the files" is not a call to Store).
    private static bool FollowedBySeparator(string message, int end)
    {
        var i = end;
        while (i < message.Length && char.IsWhiteSpace(message[i])) i++;
        return i >= message.Length || message[i] is ',' or ':' or '-' or '–' or '—' or ';' or '.' or '!';
    }

    private static string Rest(string message, int from, bool relayed)
    {
        var rest = message[from..].TrimStart(' ', '\t', '\r', '\n', ',', ':', ';', '-', '–', '—', '.', '!').TrimEnd();
        if (!relayed) return rest;
        foreach (var lead in new[] { "to ", "that " })
            if (rest.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) return rest[lead.Length..].TrimStart();
        return rest;
    }

    /// <summary>The distinct Asturs the message names anywhere after an "@".</summary>
    private static List<AssistantProfile> MentionedAsturs(string message, List<Name> names, IReadOnlyList<AssistantProfile> profiles)
    {
        var found = new List<AssistantProfile>();
        for (var at = message.IndexOf('@', StringComparison.Ordinal); at >= 0 && at < message.Length; at = message.IndexOf('@', at + 1))
        {
            if (at > 0 && !char.IsWhiteSpace(message[at - 1]) && message[at - 1] is not ('(' or '[' or '"' or '\'' or ',')) continue;
            if (at + 1 >= message.Length || !char.IsLetterOrDigit(message[at + 1])) continue;
            var words = Words(message[(at + 1)..], at + 1);
            var route = Match(message, words, 0, names, requireSeparator: false, relayed: false);
            if (route.Kind == AsturRouteKind.Astur && route.Target is { } target && found.All(f => f.Id != target.Id)) found.Add(target);
        }
        return profiles.Where(p => found.Any(f => f.Id == p.Id)).ToList();
    }

    // ---- the question ----

    /// <summary>The question asked when a name fits several: "Which one: Channel A or Channel B?"</summary>
    public static string Question(IReadOnlyList<AssistantProfile> candidates)
    {
        var names = candidates.Select(c => c.Name).ToList();
        return names.Count switch
        {
            0 => "Which one?",
            1 => "Do you mean " + names[0] + "?",
            2 => $"Which one: {names[0]} or {names[1]}?",
            _ => "Which one: " + string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1] + "?",
        };
    }
}
