// PORT-ONLY: web surfaces W14, Tabula's Code view (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1 "code (any language, shown
// with highlighting)", 6.3 "View switch: Preview / Code / Diff. Code is read-only with 'Edit'"; the approved canvas index.html
// section 4 ".code .k / .s / .c": keywords, strings and comments in their own colours; ED-NEW-web-w14-1). No Swift counterpart.
// A small lexical pass, not a parser: comments, strings, numbers, keywords, and the tags and attributes of markup, for the
// language families artifacts are written in. Anything it does not know stays plain, so a wrong guess only costs colour.
using System.Globalization;

namespace Marrow.Core.Web;

public enum CodeTokenKind
{
    Keyword,
    String,
    Comment,
    Number,
    Tag,
    Attribute,
}

/// <summary>A coloured run of the text; everything between runs is plain.</summary>
public readonly record struct CodeToken(int Start, int Length, CodeTokenKind Kind);

/// <summary>The family a language is coloured as.</summary>
public enum CodeFamily
{
    Plain,
    CLike,
    Hash,
    Markup,
    Css,
    Json,
    Sql,
    Markdown,
    Mermaid,
}

public static class CodeHighlight
{
    /// <summary>Texts longer than this are shown plain (the colouring would cost more than it gives).</summary>
    public const int MaxLength = 256 * 1024;

    private static readonly HashSet<string> CLikeWords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "default", "delete", "do",
        "else", "enum", "export", "extends", "false", "finally", "for", "from", "func", "function", "go", "if", "implements",
        "import", "in", "instanceof", "interface", "internal", "let", "namespace", "new", "null", "of", "override", "package",
        "private", "protected", "public", "readonly", "record", "return", "sealed", "static", "struct", "super", "switch",
        "this", "throw", "true", "try", "type", "typeof", "undefined", "using", "var", "void", "while", "yield", "fn", "impl",
        "mut", "pub", "use", "mod", "match", "where", "val", "fun", "def", "nil", "self", "int", "string", "bool", "double",
        "float", "char", "long", "byte", "object", "get", "set", "init", "required", "virtual", "unsafe", "lock", "foreach",
        "goto", "is", "not", "and", "or", "with", "guard", "let", "defer", "select", "chan", "map", "range",
    };

    private static readonly HashSet<string> HashWords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False",
        "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass",
        "raise", "return", "True", "try", "while", "with", "yield", "then", "fi", "do", "done", "esac", "case", "function",
        "local", "export", "echo", "true", "false", "null", "end", "begin", "unless", "until", "module", "require",
    };

    private static readonly HashSet<string> SqlWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "and", "or", "not", "insert", "into", "values", "update", "set", "delete", "create", "table",
        "drop", "alter", "index", "join", "left", "right", "inner", "outer", "on", "group", "by", "order", "having", "limit",
        "offset", "as", "distinct", "union", "all", "null", "is", "in", "like", "between", "primary", "key", "foreign",
        "references", "default", "case", "when", "then", "else", "end", "with", "returning", "asc", "desc", "count", "exists",
    };

    private static readonly HashSet<string> MermaidWords = new(StringComparer.Ordinal)
    {
        "graph", "flowchart", "sequenceDiagram", "classDiagram", "stateDiagram", "stateDiagram-v2", "erDiagram", "gantt", "pie",
        "journey", "gitGraph", "mindmap", "timeline", "quadrantChart", "participant", "actor", "loop", "alt", "else", "opt",
        "end", "note", "subgraph", "direction", "TD", "TB", "LR", "RL", "BT", "class", "state", "section", "title", "activate",
        "deactivate", "par", "and", "rect", "critical", "break",
    };

    /// <summary>The family for an artifact's kind and language (a code artifact's language, a project file's extension).</summary>
    public static CodeFamily FamilyOf(string? language)
    {
        var name = (language ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return name switch
        {
            "html" or "htm" or "xml" or "svg" or "xaml" or "vue" or "svelte" => CodeFamily.Markup,
            "css" or "scss" or "less" => CodeFamily.Css,
            "json" or "jsonc" or "geojson" => CodeFamily.Json,
            "sql" => CodeFamily.Sql,
            "md" or "markdown" or "mdx" => CodeFamily.Markdown,
            "mermaid" or "mmd" => CodeFamily.Mermaid,
            "py" or "python" or "sh" or "bash" or "zsh" or "shell" or "ps1" or "powershell" or "pwsh" or "rb" or "ruby" or "r"
                or "yaml" or "yml" or "toml" or "ini" or "dockerfile" or "makefile" or "nthui" or "nth-ui" => CodeFamily.Hash,
            "js" or "jsx" or "mjs" or "cjs" or "ts" or "tsx" or "javascript" or "typescript" or "react" or "c" or "h" or "cpp"
                or "c++" or "cc" or "hpp" or "cs" or "c#" or "csharp" or "java" or "kt" or "kotlin" or "go" or "rs" or "rust"
                or "swift" or "php" or "dart" or "scala" or "groovy" or "glsl" or "wgsl" or "zig" => CodeFamily.CLike,
            _ => CodeFamily.Plain,
        };
    }

    /// <summary>The coloured runs of <paramref name="text"/>, in order and never overlapping.</summary>
    public static IReadOnlyList<CodeToken> Tokens(string text, CodeFamily family)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxLength || family == CodeFamily.Plain) return [];
        var tokens = new List<CodeToken>();
        switch (family)
        {
            case CodeFamily.Markup: Markup(text, tokens); break;
            case CodeFamily.Markdown: Markdown(text, tokens); break;
            default: Program(text, family, tokens); break;
        }
        return tokens;
    }

    private static void Program(string text, CodeFamily family, List<CodeToken> tokens)
    {
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            // comments
            if ((family is CodeFamily.CLike or CodeFamily.Css or CodeFamily.Json) && c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                tokens.Add(new CodeToken(i, end - i, CodeTokenKind.Comment));
                i = end;
                continue;
            }
            if ((family is CodeFamily.CLike or CodeFamily.Json && c == '/' && i + 1 < n && text[i + 1] == '/')
                || (family == CodeFamily.Hash && c == '#')
                || (family == CodeFamily.Sql && c == '-' && i + 1 < n && text[i + 1] == '-')
                || (family == CodeFamily.Mermaid && c == '%' && i + 1 < n && text[i + 1] == '%'))
            {
                var end = text.IndexOf('\n', i);
                end = end < 0 ? n : end;
                tokens.Add(new CodeToken(i, end - i, CodeTokenKind.Comment));
                i = end;
                continue;
            }
            // strings
            if (c is '"' or '\'' || (c == '`' && family == CodeFamily.CLike))
            {
                var end = StringEnd(text, i, c, multiline: c == '`');
                tokens.Add(new CodeToken(i, end - i, CodeTokenKind.String));
                i = end;
                continue;
            }
            // numbers
            if (char.IsAsciiDigit(c) && (i == 0 || !IsWord(text[i - 1])))
            {
                var end = i + 1;
                while (end < n && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '.' or '_')) end++;
                tokens.Add(new CodeToken(i, end - i, CodeTokenKind.Number));
                i = end;
                continue;
            }
            // words
            if (IsWordStart(c) || (family == CodeFamily.Css && c == '@'))
            {
                var end = i + 1;
                while (end < n && (IsWord(text[end]) || (family == CodeFamily.Mermaid && text[end] == '-'))) end++;
                var word = text[i..end];
                if (IsKeyword(word, family, text, end)) tokens.Add(new CodeToken(i, end - i, family == CodeFamily.Css && c != '@' ? CodeTokenKind.Attribute : CodeTokenKind.Keyword));
                i = end;
                continue;
            }
            i++;
        }
    }

    private static bool IsKeyword(string word, CodeFamily family, string text, int end) => family switch
    {
        CodeFamily.CLike => CLikeWords.Contains(word),
        CodeFamily.Hash => HashWords.Contains(word),
        CodeFamily.Sql => SqlWords.Contains(word),
        CodeFamily.Mermaid => MermaidWords.Contains(word),
        CodeFamily.Json => word is "true" or "false" or "null",
        // a CSS property name (a word followed by a colon inside a block) or an at-rule
        CodeFamily.Css => word.StartsWith('@') || NextNonSpace(text, end) == ':' && IsInBlock(text, end),
        _ => false,
    };

    private static char NextNonSpace(string text, int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        return at < text.Length ? text[at] : '\0';
    }

    private static bool IsInBlock(string text, int at)
    {
        var open = text.LastIndexOf('{', Math.Max(0, at - 1));
        var close = text.LastIndexOf('}', Math.Max(0, at - 1));
        return open > close;
    }

    private static void Markup(string text, List<CodeToken> tokens)
    {
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                end = end < 0 ? n : end + 3;
                tokens.Add(new CodeToken(i, end - i, CodeTokenKind.Comment));
                i = end;
                continue;
            }
            if (text[i] == '<' && i + 1 < n && (char.IsAsciiLetter(text[i + 1]) || text[i + 1] is '/' or '!' or '?'))
            {
                var name = i + 1;
                while (name < n && text[name] is '/' or '!' or '?') name++;
                var nameEnd = name;
                while (nameEnd < n && (char.IsAsciiLetterOrDigit(text[nameEnd]) || text[nameEnd] is '-' or ':' or '.')) nameEnd++;
                tokens.Add(new CodeToken(i, nameEnd - i, CodeTokenKind.Tag));
                var at = nameEnd;
                while (at < n && text[at] != '>')
                {
                    var c = text[at];
                    if (c is '"' or '\'')
                    {
                        var end = StringEnd(text, at, c, multiline: true);
                        tokens.Add(new CodeToken(at, end - at, CodeTokenKind.String));
                        at = end;
                        continue;
                    }
                    if (char.IsAsciiLetter(c) || c is '@' or ':' or '_')
                    {
                        var end = at + 1;
                        while (end < n && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '-' or ':' or '_' or '.')) end++;
                        tokens.Add(new CodeToken(at, end - at, CodeTokenKind.Attribute));
                        at = end;
                        continue;
                    }
                    if (c == '<') break; // a broken tag: the next one starts here
                    at++;
                }
                if (at < n && text[at] == '>')
                {
                    var close = text[at - 1] == '/' ? at - 1 : at;
                    tokens.Add(new CodeToken(close, at + 1 - close, CodeTokenKind.Tag));
                    at++;
                }
                i = at;
                continue;
            }
            i++;
        }
    }

    private static void Markdown(string text, List<CodeToken> tokens)
    {
        var start = 0;
        var fenced = false;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            var line = text.AsSpan(start, end - start);
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                tokens.Add(new CodeToken(start, end - start, CodeTokenKind.Comment));
                fenced = !fenced;
            }
            else if (fenced) tokens.Add(new CodeToken(start, end - start, CodeTokenKind.String));
            else if (trimmed.StartsWith("#")) tokens.Add(new CodeToken(start, end - start, CodeTokenKind.Keyword));
            else
            {
                // `code` spans
                var at = 0;
                while (at < line.Length)
                {
                    var open = line[at..].IndexOf('`');
                    if (open < 0) break;
                    open += at;
                    var close = line[(open + 1)..].IndexOf('`');
                    if (close < 0) break;
                    close += open + 1;
                    tokens.Add(new CodeToken(start + open, close - open + 1, CodeTokenKind.String));
                    at = close + 1;
                }
            }
            if (end == text.Length) break;
            start = end + 1;
        }
    }

    private static int StringEnd(string text, int start, char quote, bool multiline)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\') { i += 2; continue; }
            if (c == quote) return i + 1;
            if (c == '\n' && !multiline) return i;
            i++;
        }
        return text.Length;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    /// <summary>The language a project file is coloured as: its extension.</summary>
    public static string? LanguageOfPath(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 || dot == path.Length - 1 ? null : path[(dot + 1)..].ToLower(CultureInfo.InvariantCulture);
    }
}
