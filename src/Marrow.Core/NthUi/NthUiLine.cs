// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 "Line-oriented and indented by two spaces"; ED-NEW-web-w15-1). No Swift
// counterpart. Reads one nth-ui line into its parts: the indent, the head word (the component or keyword), the values
// after it (quoted texts, numbers, bare words, bracketed expressions, and name=value attributes) and an optional tail
// after a standalone `=`. Reading never throws; a line it cannot read carries an Error and later degrades to its text.

namespace Marrow.Core.NthUi;

public enum NthUiTokenKind
{
    /// <summary>A quoted text.</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A bare word: a name, a flag, or an option such as `bar` or `points`.</summary>
    Word,

    /// <summary>A bracketed expression: `( ... )`, `[ ... ]`, `{ ... }`, or a call such as `fmt(x, "$0")`.</summary>
    Expr,
}

/// <summary>One value on a line, positional or an attribute (`name=value`).</summary>
public sealed class NthUiToken
{
    public NthUiTokenKind Kind { get; init; }

    /// <summary>The attribute's name, lower-cased; null for a positional value.</summary>
    public string? Name { get; init; }

    public bool IsAttribute => Name is not null;

    /// <summary>The value's source text (a quoted text without its quotes and escapes).</summary>
    public string Raw { get; init; } = "";

    /// <summary>The literal value: the text, the number, a word's text (true and false are bools).</summary>
    public NthUiValue Literal { get; init; }

    /// <summary>The value as an expression: literals, a word that is a name, or the bracketed expression; null for a word
    /// that cannot be a name (such as `arrow-up` or `server.tool`).</summary>
    public NthUiExpr? Expr { get; init; }
}

/// <summary>One line of nth-ui, read but not yet validated.</summary>
public sealed class NthUiLine
{
    /// <summary>The longest line read; a longer one degrades to its text.</summary>
    public const int MaxLength = 8_000;

    /// <summary>The most values one line may carry.</summary>
    public const int MaxTokens = 256;

    private NthUiLine(string source)
    {
        Source = source;
    }

    /// <summary>The line as received, without its line break.</summary>
    public string Source { get; }

    /// <summary>The indent in columns (a tab counts as two).</summary>
    public int Indent { get; private set; }

    /// <summary>The line without its indent and trailing spaces.</summary>
    public string Content { get; private set; } = "";

    /// <summary>The first word, lower-cased; empty when the line does not start with a word.</summary>
    public string Head { get; private set; } = "";

    /// <summary>Everything after the head word, trimmed (the free text of a text line).</summary>
    public string Rest { get; private set; } = "";

    public IReadOnlyList<NthUiToken> Tokens { get; private set; } = [];

    /// <summary>The expression after a standalone `=`, as written.</summary>
    public string? TailSource { get; private set; }

    public NthUiExpr? Tail { get; private set; }

    /// <summary>Why the line could not be read, or null.</summary>
    public string? Error { get; private set; }

    public bool IsBlank => Content.Length == 0;

    public static NthUiLine Read(string source)
    {
        var line = new NthUiLine(source);
        line.Parse();
        return line;
    }

    private void Parse()
    {
        var indent = 0;
        var i = 0;
        while (i < Source.Length && (Source[i] == ' ' || Source[i] == '\t'))
        {
            indent += Source[i] == '\t' ? 2 : 1;
            i++;
        }
        Indent = indent;
        Content = Source[i..].TrimEnd();
        if (Content.Length == 0) return;
        if (Content.Length > MaxLength)
        {
            Error = "the line is too long";
            return;
        }
        var end = 0;
        if (char.IsAsciiLetter(Content[0]))
        {
            while (end < Content.Length && (char.IsAsciiLetterOrDigit(Content[end]) || Content[end] == '_')) end++;
        }
        if (end == 0 || (end < Content.Length && !char.IsWhiteSpace(Content[end])))
        {
            Error = "the line does not start with a component";
            return;
        }
        Head = Content[..end].ToLowerInvariant();
        Rest = Content[end..].Trim();
        Tokens = ReadTokens(end);
    }

    private List<NthUiToken> ReadTokens(int i)
    {
        var tokens = new List<NthUiToken>();
        var text = Content;
        while (Error is null)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;
            if (tokens.Count >= MaxTokens)
            {
                Error = "the line has too many values";
                break;
            }
            var c = text[i];
            if (c == '=' && (i + 1 >= text.Length || text[i + 1] != '='))
            {
                ReadTail(text[(i + 1)..]);
                break;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '-' or '.')) i++;
                var word = text[start..i];
                if (i < text.Length && text[i] == '=' && (i + 1 >= text.Length || text[i + 1] != '='))
                {
                    i++;
                    var value = ReadValue(text, ref i, word.ToLowerInvariant());
                    if (value is not null) tokens.Add(value);
                    continue;
                }
                if (i < text.Length && text[i] == '(')
                {
                    i = start;
                    var call = ReadValue(text, ref i, null);
                    if (call is not null) tokens.Add(call);
                    continue;
                }
                if (!AtBoundary(text, i)) break;
                tokens.Add(WordToken(word, null));
                continue;
            }
            var token = ReadValue(text, ref i, null);
            if (token is not null) tokens.Add(token);
        }
        return tokens;
    }

    private void ReadTail(string tail)
    {
        TailSource = tail.Trim();
        if (TailSource.Length == 0)
        {
            Error = "a value is missing after =";
            return;
        }
        if (NthUiExpr.TryParse(TailSource, out var expr, out var error)) Tail = expr;
        else Error = error;
    }

    /// <summary>Reads one value at <paramref name="i"/>: a quoted text, a number, a bracketed expression, a call, or a word.</summary>
    private NthUiToken? ReadValue(string text, ref int i, string? name)
    {
        if (name is not null && !IsAttributeName(name))
        {
            Error = $"'{name}' cannot name an attribute";
            return null;
        }
        if (i >= text.Length || char.IsWhiteSpace(text[i]))
        {
            Error = name is null ? "a value is missing" : $"{name}= needs a value";
            return null;
        }
        var start = i;
        var c = text[i];
        NthUiToken token;
        if (c is '"' or '\'')
        {
            if (!NthUiExpr.ScanString(text, ref i, out var value))
            {
                Error = "a text is not closed";
                return null;
            }
            token = new NthUiToken { Kind = NthUiTokenKind.Text, Name = name, Raw = value, Literal = NthUiValue.Of(value), Expr = NthUiExpr.Literal(NthUiValue.Of(value), text[start..i]) };
        }
        else if (char.IsAsciiDigit(c) || (c is '-' or '+' or '.' && i + 1 < text.Length && (char.IsAsciiDigit(text[i + 1]) || (text[i + 1] == '.' && c != '.'))))
        {
            var sign = c == '-' ? -1 : 1;
            if (c is '-' or '+') i++;
            if (!NthUiExpr.ScanNumber(text, ref i, out var number))
            {
                Error = $"the number at {start + 1} is not valid";
                return null;
            }
            var value = NthUiValue.Of(sign * number);
            token = new NthUiToken { Kind = NthUiTokenKind.Number, Name = name, Raw = text[start..i], Literal = value, Expr = NthUiExpr.Literal(value, text[start..i]) };
        }
        else if (c is '(' or '[' or '{' || char.IsAsciiLetter(c) || c == '_')
        {
            if (c is not ('(' or '[' or '{'))
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '-' or '.')) i++;
                if (i >= text.Length || text[i] != '(')
                {
                    if (!AtBoundary(text, i)) return null;
                    return WordToken(text[start..i], name);
                }
            }
            if (!ScanBalanced(text, ref i))
            {
                Error = "a bracket is not closed";
                return null;
            }
            var source = text[start..i];
            if (!NthUiExpr.TryParse(source, out var expr, out var error))
            {
                Error = error;
                return null;
            }
            token = new NthUiToken { Kind = NthUiTokenKind.Expr, Name = name, Raw = source, Expr = expr };
        }
        else
        {
            Error = $"unexpected '{c}' at {i + 1}";
            return null;
        }
        return AtBoundary(text, i) ? token : null;
    }

    private bool AtBoundary(string text, int i)
    {
        if (i >= text.Length || char.IsWhiteSpace(text[i]) || (text[i] == '=' && (i + 1 >= text.Length || text[i + 1] != '='))) return true;
        Error = $"unexpected '{text[i]}' at {i + 1}";
        return false;
    }

    private static NthUiToken WordToken(string word, string? name)
    {
        NthUiValue literal;
        NthUiExpr? expr;
        switch (word)
        {
            case "true":
                literal = NthUiValue.True;
                expr = NthUiExpr.Literal(literal, word);
                break;
            case "false":
                literal = NthUiValue.False;
                expr = NthUiExpr.Literal(literal, word);
                break;
            default:
                literal = NthUiValue.Of(word);
                expr = NthUiExpr.IsName(word) && !NthUiExpr.IsReserved(word) ? NthUiExpr.Reference(word) : null;
                break;
        }
        return new NthUiToken { Kind = NthUiTokenKind.Word, Name = name, Raw = word, Literal = literal, Expr = expr };
    }

    private static bool IsAttributeName(string name)
    {
        if (name.Length is 0 or > 32 || !char.IsAsciiLetter(name[0])) return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return false;
        }
        return true;
    }

    /// <summary>Scans from an opening bracket (or a word then its parenthesis) to the matching close, skipping quoted texts.</summary>
    private static bool ScanBalanced(string text, ref int i)
    {
        var closers = new Stack<char>();
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '"' or '\'':
                    if (!NthUiExpr.ScanString(text, ref i, out _)) return false;
                    if (closers.Count == 0) return true;
                    continue;
                case '(':
                    closers.Push(')');
                    break;
                case '[':
                    closers.Push(']');
                    break;
                case '{':
                    closers.Push('}');
                    break;
                case ')' or ']' or '}':
                    if (closers.Count == 0 || closers.Pop() != c) return false;
                    if (closers.Count == 0)
                    {
                        i++;
                        return true;
                    }
                    break;
            }
            if (closers.Count > NthUiExpr.MaxNesting * 2) return false;
            i++;
        }
        return false;
    }
}
