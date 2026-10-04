// PORT-ONLY: first-release feature unit SK-10 (DL-10 of porting/first-release/skills-ONDEMAND-PLAN.md section 7); there is no Swift twin.
// The quote-aware command-line tokenizer of ONDEMAND-PLAN.md 5.3 "Command parsing". Pure: it reads text and nothing else.
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Skills.Detect;

/// <summary>One word of a command line; <see cref="Quoted"/> is true when any part of it was inside quotes.</summary>
public sealed record CommandWord(string Text, bool Quoted);

/// <summary>What a segment runs: its program (path and executable extension removed, lower case) and, for a python launcher with <c>-m</c>, the module.</summary>
public sealed record SegmentTarget(string Program, string? Module);

/// <summary>
/// ONDEMAND-PLAN.md 5.3: "split the command line into pipeline and &amp;&amp;/; segments with a quote-aware tokenizer (cmd, PowerShell and bash quoting); take each segment's
/// first word; strip a path and an extension; skip leading cd, &amp;, call, start, env assignments and py -3 -m &lt;module&gt; (the module counts)". The shell is not
/// known, so one scanner serves all three (ENGINEERING DECISIONS, ED-NEW-f-sk-10): separators are <c>| || |&amp; &amp;&amp; &amp; ;</c> and line breaks outside quotes (an
/// ampersand next to a redirection, as in <c>2&gt;&amp;1</c> or <c>&amp;&gt;</c>, belongs to the word); single and double quotes group; inside double quotes a backslash
/// before a quote escapes it; every other backslash is a plain character, so <c>C:\x\soffice.exe</c> survives; an unterminated quote runs to the end.
/// </summary>
public static partial class CommandTokenizer
{
    private static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat", ".com"];

    private static readonly string[] PythonLaunchers = ["py", "python", "python3"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*=")] private static partial Regex Assignment();

    public static IReadOnlyList<IReadOnlyList<CommandWord>> Segments(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var segments = new List<IReadOnlyList<CommandWord>>();
        var words = new List<CommandWord>();
        var text = new StringBuilder();
        var started = false;
        var quoted = false;
        var quote = '\0';

        void EndWord()
        {
            if (started) words.Add(new CommandWord(text.ToString(), quoted));
            text.Clear();
            started = false;
            quoted = false;
        }

        void EndSegment()
        {
            EndWord();
            if (words.Count > 0) segments.Add(words.ToArray());
            words.Clear();
        }

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (quote == '\'')
            {
                if (c == '\'') quote = '\0';
                else text.Append(c);
                continue;
            }
            if (quote == '"')
            {
                if (c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    text.Append('"');
                    i++;
                }
                else if (c == '"') quote = '\0';
                else text.Append(c);
                continue;
            }
            switch (c)
            {
                case ' ' or '\t':
                    EndWord();
                    break;
                case '\n' or '\r' or ';':
                    EndSegment();
                    break;
                case '"' or '\'':
                    quote = c;
                    started = true;
                    quoted = true;
                    break;
                case '|':
                    EndSegment();
                    if (i + 1 < commandLine.Length && commandLine[i + 1] is '|' or '&') i++;
                    break;
                case '&':
                    if (i + 1 < commandLine.Length && commandLine[i + 1] == '&')
                    {
                        EndSegment();
                        i++;
                    }
                    else if ((i > 0 && commandLine[i - 1] is '>' or '<') || (i + 1 < commandLine.Length && commandLine[i + 1] == '>'))
                    {
                        text.Append(c);
                        started = true;
                    }
                    else EndSegment();
                    break;
                default:
                    text.Append(c);
                    started = true;
                    break;
            }
        }
        EndSegment();
        return segments;
    }

    /// <summary>The last path component, in lower case, without a trailing <c>.exe</c>, <c>.cmd</c>, <c>.bat</c> or <c>.com</c>; any other extension stays (a file name is not a program).</summary>
    public static string NormalizeProgram(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var name = word[(word.AsSpan().LastIndexOfAny('/', '\\') + 1)..].ToLowerInvariant();
        foreach (var extension in ExecutableExtensions)
            if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.Ordinal)) return name[..^extension.Length];
        return name;
    }

    /// <summary>The program of every segment that runs one, in order, after the leading words the plan says to skip.</summary>
    public static IReadOnlyList<SegmentTarget> Targets(string commandLine)
    {
        var targets = new List<SegmentTarget>();
        foreach (var words in Segments(commandLine))
        {
            var at = ProgramIndex(words);
            if (at < 0) continue;
            var program = NormalizeProgram(words[at].Text);
            if (program.Length == 0) continue;
            targets.Add(new SegmentTarget(program, PythonLaunchers.Contains(program, StringComparer.Ordinal) ? ModuleAfter(words, at) : null));
        }
        return targets;
    }

    // The index of the word that names the program, or -1 when the segment runs none (a cd, a bare assignment, a start with nothing to start).
    private static int ProgramIndex(IReadOnlyList<CommandWord> words)
    {
        var i = 0;
        while (i < words.Count)
        {
            var word = words[i];
            switch (word.Text.ToLowerInvariant())
            {
                case "cd":
                    return -1;
                case "call" or "env":
                    i++;
                    continue;
                case "start":
                    i++;
                    var title = false;
                    // cmd's start takes /options and one quoted window title before the program.
                    while (i < words.Count && ((!words[i].Quoted && words[i].Text.StartsWith('/')) || (!title && words[i].Quoted)))
                    {
                        if (words[i].Quoted) title = true;
                        i++;
                    }
                    continue;
            }
            if (Assignment().IsMatch(word.Text))
            {
                i++;
                continue;
            }
            return i;
        }
        return -1;
    }

    private static string? ModuleAfter(IReadOnlyList<CommandWord> words, int program)
    {
        for (var j = program + 1; j < words.Count; j++)
        {
            var text = words[j].Text;
            if (text == "-m") return j + 1 < words.Count && words[j + 1].Text.Length > 0 ? words[j + 1].Text : null;
            if (!text.StartsWith('-')) return null;
        }
        return null;
    }

    /// <summary>Every word of the command, each normalised like a program, in order and without repeats, and the words of any quoted word that holds blanks (one level: <c>cmd /c "soffice a.docx"</c>).</summary>
    public static IReadOnlyList<string> OrderedTokens(string commandLine)
    {
        var tokens = new List<string>();
        void Add(string text)
        {
            var token = NormalizeProgram(text);
            if (token.Length > 0 && !tokens.Contains(token, StringComparer.Ordinal)) tokens.Add(token);
        }
        foreach (var words in Segments(commandLine))
            foreach (var word in words)
            {
                Add(word.Text);
                if (word.Quoted && word.Text.Any(char.IsWhiteSpace))
                    foreach (var inner in Segments(word.Text))
                        foreach (var innerWord in inner) Add(innerWord.Text);
            }
        return tokens;
    }

    public static IReadOnlySet<string> Tokens(string commandLine) => new HashSet<string>(OrderedTokens(commandLine), StringComparer.Ordinal);
}
