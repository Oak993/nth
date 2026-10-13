// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Spoken status ... answered in two or three sentences", 3.3 "by voice a spoken summary of three
// sentences"; unit AS-18; ED-NEW-dextra-as18-6). No Swift twin.
// Text made fit to be said aloud and shown beside the voice: markdown and code taken out, links named, sentences counted, long replies
// cut at a sentence with the rest left on screen, and the words listed so the sheet can light the one being spoken. Pure.
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>One word of spoken text: where it starts in the text and how long it is.</summary>
public readonly record struct SpokenWord(int Start, int Length);

public static partial class SpokenText
{
    /// <summary>A status answer is at most this many sentences (2.4).</summary>
    public const int StatusSentences = 3;

    /// <summary>A reply from the brain is spoken to this many sentences; the whole reply stays in the chat.</summary>
    public const int ReplySentences = 5;

    /// <summary>... and to this many characters.</summary>
    public const int ReplyCharacters = 700;

    /// <summary>Said after a reply that was cut: the rest is in the chat.</summary>
    public const string RestOnScreen = "The rest is on screen.";

    [GeneratedRegex(@"```[\s\S]*?(?:```|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\[([^\]]+)\]\((?:[^)\s]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+|\d{1,3}[.)]\s+)", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LineMarker();

    [GeneratedRegex(@"[`*_~|]+", RegexOptions.CultureInvariant)]
    private static partial Regex Marks();

    [GeneratedRegex(@"[ \t ]+", RegexOptions.CultureInvariant)]
    private static partial Regex Blanks();

    [GeneratedRegex(@"(?<=[.!?])[""')\]”’]*\s+(?=[\p{Lu}\p{N}""'(“‘\[])", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    /// <summary>
    /// What is said of <paramref name="markdown"/>: code blocks gone (a line says so), inline code, emphasis, headings, bullets and
    /// table bars stripped, links named by their text and bare addresses by "a link", dashes and line breaks as commas and full
    /// stops. <paramref name="hadCode"/> tells that a code block was left out.
    /// </summary>
    public static string Clean(string? markdown, out bool hadCode)
    {
        hadCode = false;
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (CodeBlock().IsMatch(text))
        {
            hadCode = true;
            text = CodeBlock().Replace(text, "\n");
        }
        text = Link().Replace(text, "$1");
        text = Address().Replace(text, "a link");
        text = LineMarker().Replace(text, "");
        text = Marks().Replace(text, "");
        text = text.Replace('—', ',').Replace('–', ',').Replace(" - ", ", ", StringComparison.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var joined = new StringBuilder();
        foreach (var line in lines)
        {
            if (joined.Length > 0) joined.Append(char.IsAsciiLetterOrDigit(joined[^1]) || joined[^1] is ',' or ')' ? ". " : " ");
            joined.Append(line);
        }
        return Blanks().Replace(joined.ToString(), " ").Trim();
    }

    public static string Clean(string? markdown) => Clean(markdown, out _);

    /// <summary>The sentences of <paramref name="text"/>, in order. A decimal point or an abbreviation without a capital after it does not end one.</summary>
    public static IReadOnlyList<string> Sentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return [.. SentenceEnd().Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0)];
    }

    /// <summary>The first <paramref name="count"/> sentences of <paramref name="text"/>, and whether any were left.</summary>
    public static string First(string? text, int count, out bool cut)
    {
        var all = Sentences(text);
        cut = all.Count > count;
        return string.Join(' ', all.Take(Math.Max(0, count)));
    }

    /// <summary>
    /// A reply made ready to be said: cleaned, then cut to <see cref="ReplySentences"/> sentences and <see cref="ReplyCharacters"/>
    /// characters at a sentence end. A cut reply, or one that had code, ends with "The rest is on screen." (one more sentence).
    /// </summary>
    public static string ForSpeech(string? reply, int sentences = ReplySentences, int characters = ReplyCharacters)
    {
        var cleaned = Clean(reply, out var hadCode);
        if (cleaned.Length == 0) return hadCode ? "That is code. It is on screen." : "";
        var kept = new List<string>();
        var length = 0;
        var cut = false;
        foreach (var sentence in Sentences(cleaned))
        {
            if (kept.Count >= sentences || (kept.Count > 0 && length + sentence.Length + 1 > characters))
            {
                cut = true;
                break;
            }
            kept.Add(sentence);
            length += sentence.Length + 1;
        }
        var spoken = string.Join(' ', kept);
        if (spoken.Length > characters)
        {
            spoken = spoken[..characters];
            var space = spoken.LastIndexOf(' ');
            spoken = (space > characters / 2 ? spoken[..space] : spoken).TrimEnd(',', ';', ':') + ".";
            cut = true;
        }
        return cut || hadCode ? spoken + " " + RestOnScreen : spoken;
    }

    /// <summary>How many sentences <paramref name="text"/> has.</summary>
    public static int CountSentences(string? text) => Sentences(text).Count;

    /// <summary>The words of <paramref name="text"/> (runs without white space), so the sheet can light the one being spoken.</summary>
    public static IReadOnlyList<SpokenWord> Words(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return [.. Word().Matches(text).Select(m => new SpokenWord(m.Index, m.Length))];
    }

    /// <summary>
    /// A title or a name made safe to put inside a spoken sentence: no sentence marks of its own (so it never adds a sentence), quotes
    /// and brackets gone, one line.
    /// </summary>
    public static string Phrase(string? text, int most = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var decimalPoint = c == '.' && i > 0 && i + 1 < text.Length && char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i + 1]);
            builder.Append(decimalPoint ? c : c switch
            {
                '.' or '!' or '?' or '…' or ';' or '\n' or '\r' or '\t' => ' ',
                '"' or '“' or '”' or '‘' or '’' or '`' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' => ' ',
                _ => c,
            });
        }
        var phrase = Blanks().Replace(builder.ToString(), " ").Trim();
        if (phrase.Length <= most) return phrase;
        var cutAt = phrase.LastIndexOf(' ', most);
        return (cutAt > most / 2 ? phrase[..cutAt] : phrase[..most]).TrimEnd(',', ':', '-');
    }
}
