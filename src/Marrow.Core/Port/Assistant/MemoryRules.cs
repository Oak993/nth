// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1, 8.2; unit AS-12; ED-NEW-dextra-as12-2). No Swift twin.
// The pure rules for the text of a memory note: how it is cleaned to one safe line, the key two notes are compared by, the words
// a search and a duplicate check look at, and the topic names notes are filed under. No I/O, no clock, no model.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public static partial class MemoryRules
{
    /// <summary>A note is a sentence or two: up to this many characters (8.2 keeps the Memory view readable and a prompt small).</summary>
    public const int MaxNoteChars = 500;

    /// <summary>A note shorter than this says nothing.</summary>
    public const int MinNoteChars = 3;

    public const int MaxTopicChars = 40;

    /// <summary>The staging file new notes are written to, until the night's folding files them under their topics (8.2).</summary>
    public const string RecentTopic = "recent";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "memory", "recent", "quarantine", "state", "con", "prn", "aux", "nul", "shared",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    private static readonly HashSet<string> Glue = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "for", "with", "by", "from", "as", "is", "are", "was", "were", "be", "been",
        "it", "its", "this", "that", "these", "those", "he", "she", "they", "them", "his", "her", "their", "i", "me", "my", "we", "our", "you", "your",
        "owner", "user", "s", "do", "does", "did", "has", "have", "had", "will", "would", "can", "could", "should", "not", "no", "also", "very", "so",
    };

    /// <summary>
    /// The note's text as one safe line: every control, line break, zero-width and direction-changing character is gone (they hide
    /// text or reorder it on screen), the comment markers a markdown file could be broken by are gone, and runs of spaces are one
    /// space. The result may be empty; it is never longer than the text.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Format:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.Surrogate:
                    continue;
                case UnicodeCategory.Control:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    builder.Append(' ');
                    continue;
                default:
                    if (rune == Rune.ReplacementChar) continue;
                    builder.Append(rune.ToString());
                    break;
            }
        }
        var line = builder.ToString().Replace("<!--", " ", StringComparison.Ordinal).Replace("-->", " ", StringComparison.Ordinal);
        var squeezed = new StringBuilder(line.Length);
        var space = false;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && squeezed.Length > 0) squeezed.Append(' ');
            space = false;
            squeezed.Append(c);
        }
        return squeezed.ToString();
    }

    /// <summary>The key two notes are the same note by: letters and digits in lower case, one space between words.</summary>
    public static string NormalKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in Folded(text))
        {
            var category = char.GetUnicodeCategory(c);
            var keep = char.IsLetterOrDigit(c) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
            if (!keep)
            {
                if (c is '\'' or '’') continue; // "owner's" is one word
                space = true;
                continue;
            }
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    // Compatibility-folded ("ﬁ" is "fi", a full-width letter is its plain one); text that cannot be folded (a lone surrogate) is used as it is.
    private static string Folded(string text)
    {
        try { return text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { return text; }
    }

    /// <summary>The words a note is searched and compared by: its key's words without the glue words, plurals folded.</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        var words = new List<string>();
        foreach (var word in NormalKey(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;
            if (Glue.Contains(word) || Glue.Contains(stem)) continue;
            if (stem.Length < 2 && !char.IsDigit(stem[0])) continue;
            if (!words.Contains(stem)) words.Add(stem);
        }
        return words;
    }

    /// <summary>How much of the two token sets they share (0 to 1): the shared words over the words in either.</summary>
    public static double Similarity(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    /// <summary>
    /// A topic's file-safe name: lower case ASCII letters, digits and single hyphens, up to <see cref="MaxTopicChars"/>, never a
    /// reserved name (a Windows device name or one of the store's own files). Empty when the hint holds nothing to name it by.
    /// </summary>
    public static string TopicSlug(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "";
        var builder = new StringBuilder();
        var dash = false;
        foreach (var c in FoldedForSlug(hint))
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                if (dash && builder.Length > 0) builder.Append('-');
                dash = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c is '\'' or '’') continue;
            else dash = true;
        }
        var slug = builder.Length > MaxTopicChars ? builder.ToString(0, MaxTopicChars).TrimEnd('-') : builder.ToString();
        if (slug.Length == 0) return "";
        return Reserved.Contains(slug) ? slug + "-notes" : slug;
    }

    private static string FoldedForSlug(string text)
    {
        try { return text.Normalize(NormalizationForm.FormKD); }
        catch (ArgumentException) { return text; }
    }

    /// <summary>Whether a name is a topic the store can use as a file name.</summary>
    public static bool IsTopic(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxTopicChars + 6 && slug == TopicSlug(slug) && slug[0] != '-';

    /// <summary>The topic a note is filed under when nothing else names one (8.1: the shared set's is "About you").</summary>
    public static string DefaultTopic(string scope, NoteKind kind) => kind switch
    {
        NoteKind.Preference or NoteKind.Fact when scope == MemoryScope.Shared => "about-you",
        NoteKind.Preference => "preferences",
        NoteKind.Fact => "facts",
        NoteKind.Procedure => "procedures",
        _ => "projects",
    };

    /// <summary>A topic as the owner reads it: "writing-style" is "Writing style".</summary>
    public static string TopicTitle(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return "Notes";
        var words = slug.Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex(@"\A\s*(?:please\s+)?(?:forget|delete|remove|erase|wipe)\s+(?:everything|all|anything)(?:\s+that\s+you\s+know)?\s+(?:about|on|regarding|concerning)\s+(?<subject>.+?)[\s.!?]*\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, 250)]
    private static partial Regex ForgetRequest();

    /// <summary>
    /// The subject of "forget everything about ⟨subject⟩" (8.3: it works by text or by voice, and the notes it will remove are shown
    /// first), or null when the words are not such a request.
    /// </summary>
    public static string? ForgetSubject(string? request)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;
        try { return ForgetRequest().Match(request) is { Success: true } m ? m.Groups["subject"].Value.Trim() : null; }
        catch (RegexMatchTimeoutException) { return null; }
    }

    [GeneratedRegex(@"\b(?:before|after|whenever|when|then|first|step|steps|workflow)\b|\A(?:run|use|build|test|commit|deploy|install|open|check|start|stop|restart|launch|clone|merge|push|pull|create|update|set|add|remove|follow|keep|write|read|call|ask|send|make|ship|verify|review)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex ProcedureWords();

    [GeneratedRegex(@"\b(?:always|never|prefer(?:s|red)?|likes?|dislikes?|don'?t|do not|avoid|must|should|wants?|please|no need|hates?|loves?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex PreferenceWords();

    /// <summary>
    /// A kind for a line read from a file nobody labelled (an import): "always", "never" and "prefers" make a Preference, steps and
    /// commands ("run", "before", "then") a Procedure, and the rest take <paramref name="fallback"/>. A guess, never more than a starting place the owner edits.
    /// </summary>
    public static NoteKind GuessKind(string? text, NoteKind fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try
        {
            if (PreferenceWords().IsMatch(text)) return NoteKind.Preference;
            if (ProcedureWords().IsMatch(text)) return NoteKind.Procedure;
        }
        catch (RegexMatchTimeoutException) { }
        return fallback;
    }

    /// <summary>The kind's word as the owner reads it.</summary>
    public static string KindWord(NoteKind kind) => kind switch
    {
        NoteKind.Preference => "Preference",
        NoteKind.Fact => "Fact",
        NoteKind.Procedure => "Procedure",
        _ => "Project",
    };
}
