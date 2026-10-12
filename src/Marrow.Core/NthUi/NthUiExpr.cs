using System.Globalization;
using System.Text;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2; ED-NEW-web-w15-1). No Swift counterpart. The expression language of
// nth-ui: numbers, texts, booleans, null, lists, object literals, + - * / %, comparisons, and or not, the ternary,
// indexing, a fixed set of pure functions, and one-parameter lambdas inside map, filter and sort. No loops, no
// recursion, no I/O. Parsing never throws and is bounded: nesting is limited while parsing and the finished tree's
// depth is limited too, so evaluating it can never overflow the stack.

namespace Marrow.Core.NthUi;

public enum NthUiExprKind
{
    Literal,
    Name,
    Unary,
    Binary,
    Conditional,
    Call,
    List,
    Object,
    Index,
    Member,
    Lambda,
}

/// <summary>One node of a parsed expression. Immutable once parsed.</summary>
public sealed class NthUiExpr
{
    /// <summary>The deepest a parsed expression tree may be.</summary>
    public const int MaxTreeDepth = 160;

    /// <summary>The deepest brackets, parentheses and prefix operators may nest while parsing.</summary>
    public const int MaxNesting = 48;

    /// <summary>The longest expression text read.</summary>
    public const int MaxLength = 8_000;

    private static readonly IReadOnlyList<NthUiExpr> None = [];
    private IReadOnlySet<string>? names;

    private NthUiExpr(NthUiExprKind kind, int start, int end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public NthUiExprKind Kind { get; }

    /// <summary>The operator of a unary or binary node: + - * / % == != &lt; &lt;= &gt; &gt;= and or not neg.</summary>
    public string Op { get; private init; } = "";

    /// <summary>A literal's value.</summary>
    public NthUiValue Value { get; private init; }

    /// <summary>A name, a called function, a member, or a lambda's parameter.</summary>
    public string Name { get; private init; } = "";

    /// <summary>Operands, list items, call arguments, object values; a lambda's body is its one argument.</summary>
    public IReadOnlyList<NthUiExpr> Args { get; private init; } = None;

    /// <summary>An object literal's keys, in the order of <see cref="Args"/>.</summary>
    public IReadOnlyList<string> Keys { get; private init; } = [];

    public int Start { get; }

    public int End { get; }

    /// <summary>The depth of the tree below and including this node.</summary>
    public int Depth { get; private init; } = 1;

    /// <summary>The text this expression was parsed from (set on the root).</summary>
    public string Source { get; private set; } = "";

    /// <summary>The names this expression reads (state and let names), without lambda parameters.</summary>
    public IReadOnlySet<string> Names => names ??= CollectNames();

    /// <summary>Parses <paramref name="text"/> as one whole expression. Never throws.</summary>
    public static bool TryParse(string text, out NthUiExpr? expr, out string error)
    {
        expr = null;
        if (text.Length > MaxLength)
        {
            error = "the expression is too long";
            return false;
        }
        var tokens = new List<Token>();
        if (!Tokenize(text, tokens, out error)) return false;
        var parser = new Parser(tokens);
        var result = parser.ParseRoot(out error);
        if (result is null) return false;
        result.Source = text.Trim();
        expr = result;
        return true;
    }

    /// <summary>Parses or returns null; for tests and literal attribute values.</summary>
    public static NthUiExpr? Parse(string text) => TryParse(text, out var expr, out _) ? expr : null;

    /// <summary>A name reference, as a bare word in an attribute reads.</summary>
    public static NthUiExpr Reference(string name) => new(NthUiExprKind.Name, 0, name.Length) { Name = name, Source = name };

    /// <summary>A literal value, as a quoted attribute reads.</summary>
    public static NthUiExpr Literal(NthUiValue value, string source) =>
        new(NthUiExprKind.Literal, 0, source.Length) { Value = value, Source = source };

    public override string ToString() => Source.Length > 0 ? Source : Kind.ToString();

    private HashSet<string> CollectNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Collect(this, set, null);
        return set;
    }

    private static void Collect(NthUiExpr node, HashSet<string> set, List<string>? bound)
    {
        switch (node.Kind)
        {
            case NthUiExprKind.Name:
                if (bound is null || !bound.Contains(node.Name)) set.Add(node.Name);
                return;
            case NthUiExprKind.Lambda:
                var inner = bound is null ? [] : new List<string>(bound);
                inner.Add(node.Name);
                Collect(node.Args[0], set, inner);
                return;
            default:
                foreach (var arg in node.Args) Collect(arg, set, bound);
                return;
        }
    }

    // ---- functions ------------------------------------------------------------------------------------------------

    /// <summary>The functions an expression may call, with their argument counts (DESIGN 7.2 plus a few pure helpers).</summary>
    public static IReadOnlyDictionary<string, (int Min, int Max)> Functions { get; } = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["min"] = (1, 64),
        ["max"] = (1, 64),
        ["round"] = (1, 2),
        ["floor"] = (1, 1),
        ["ceil"] = (1, 1),
        ["abs"] = (1, 1),
        ["sqrt"] = (1, 1),
        ["pow"] = (2, 2),
        ["clamp"] = (3, 3),
        ["fmt"] = (1, 2),
        ["len"] = (1, 1),
        ["sum"] = (1, 1),
        ["avg"] = (1, 1),
        ["map"] = (2, 2),
        ["filter"] = (2, 2),
        ["sort"] = (1, 2),
        ["reverse"] = (1, 1),
        ["slice"] = (2, 3),
        ["range"] = (1, 3),
        ["join"] = (1, 2),
        ["contains"] = (2, 2),
        ["str"] = (1, 1),
        ["num"] = (1, 1),
    };

    /// <summary>Functions whose second argument is a lambda (written `x => ...`, or any expression over `it`).</summary>
    public static bool TakesLambda(string function) => function is "map" or "filter" or "sort";

    /// <summary>Words no state, let or lambda parameter may be named.</summary>
    public static bool IsReserved(string name) =>
        Functions.ContainsKey(name) || name is "true" or "false" or "null" or "and" or "or" or "not" or "it";

    /// <summary>Whether <paramref name="name"/> is a well-formed name: a letter or _ then letters, digits or _ (at most 64).</summary>
    public static bool IsName(string name)
    {
        if (name.Length is 0 or > 64 || !(char.IsAsciiLetter(name[0]) || name[0] == '_')) return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

    // ---- tokens ---------------------------------------------------------------------------------------------------

    private enum TokenKind
    {
        Number,
        Text,
        Name,
        Op,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number, int Start, int End);

    private static bool Tokenize(string text, List<Token> tokens, out string error)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            var start = i;
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                if (!ScanNumber(text, ref i, out var value))
                {
                    error = $"the number at {start + 1} is not valid";
                    return false;
                }
                tokens.Add(new Token(TokenKind.Number, text[start..i], value, start, i));
                continue;
            }
            if (c is '"' or '\'')
            {
                if (!ScanString(text, ref i, out var value))
                {
                    error = "a text is not closed";
                    return false;
                }
                tokens.Add(new Token(TokenKind.Text, value, 0, start, i));
                continue;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Name, text[start..i], 0, start, i));
                continue;
            }
            var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "=>")
            {
                tokens.Add(new Token(TokenKind.Op, two, 0, start, i + 2));
                i += 2;
                continue;
            }
            if ("+-*/%<>!?:()[]{},.=".Contains(c, StringComparison.Ordinal))
            {
                tokens.Add(new Token(TokenKind.Op, c.ToString(), 0, start, i + 1));
                i++;
                continue;
            }
            error = $"unexpected '{c}' at {start + 1}";
            return false;
        }
        tokens.Add(new Token(TokenKind.End, "", 0, text.Length, text.Length));
        error = "";
        return true;
    }

    /// <summary>Scans a decimal number (digits, an optional fraction and exponent) starting at <paramref name="i"/>.</summary>
    internal static bool ScanNumber(string text, ref int i, out double value)
    {
        var start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        if (i < text.Length && text[i] == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        }
        if (i < text.Length && (text[i] is 'e' or 'E'))
        {
            var j = i + 1;
            if (j < text.Length && (text[j] is '+' or '-')) j++;
            if (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
                i = j;
            }
        }
        return double.TryParse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Scans a quoted text at <paramref name="i"/> (either quote), with \" \' \\ \n \t \r and \uXXXX escapes.</summary>
    internal static bool ScanString(string text, ref int i, out string value)
    {
        var quote = text[i];
        var builder = new StringBuilder();
        i++;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == quote)
            {
                i++;
                value = builder.ToString();
                return true;
            }
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                i += 2;
                switch (next)
                {
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'u' when i + 4 <= text.Length && int.TryParse(text.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default: builder.Append(next); break;
                }
                continue;
            }
            builder.Append(c);
            i++;
        }
        value = builder.ToString();
        return false;
    }

    // ---- parser ---------------------------------------------------------------------------------------------------

    private sealed class Parser(List<Token> tokens)
    {
        private int at;
        private int nesting;
        private string? failure;

        private Token Peek => tokens[at];

        public NthUiExpr? ParseRoot(out string error)
        {
            var expr = ParseExpression();
            if (expr is not null && failure is null && Peek.Kind != TokenKind.End)
                Fail(Peek.Kind == TokenKind.Op && Peek.Text == "=>" ? "a lambda is only allowed inside map, filter or sort" : $"unexpected '{Peek.Text}'");
            error = failure ?? "";
            return failure is null ? expr : null;
        }

        private NthUiExpr? Fail(string message)
        {
            failure ??= message;
            return null;
        }

        private bool IsOp(string op) => Peek.Kind == TokenKind.Op && Peek.Text == op;

        private bool IsWord(string word) => Peek.Kind == TokenKind.Name && Peek.Text == word;

        private bool Accept(string op)
        {
            if (!IsOp(op)) return false;
            at++;
            return true;
        }

        private NthUiExpr? Make(NthUiExpr node) =>
            node.Depth > MaxTreeDepth ? Fail("the expression is nested too deeply") : node;

        private static int DepthOf(IReadOnlyList<NthUiExpr> args) => 1 + (args.Count == 0 ? 0 : args.Max(a => a.Depth));

        private NthUiExpr? Node(NthUiExprKind kind, int start, string op, params NthUiExpr[] args) =>
            Make(new NthUiExpr(kind, start, tokens[Math.Max(0, at - 1)].End) { Op = op, Args = args, Depth = DepthOf(args) });

        private NthUiExpr? ParseExpression()
        {
            if (failure is not null) return null;
            if (++nesting > MaxNesting)
            {
                nesting--;
                return Fail("the expression is nested too deeply");
            }
            var result = ParseConditional();
            nesting--;
            return result;
        }

        private NthUiExpr? ParseConditional()
        {
            var start = Peek.Start;
            var condition = ParseOr();
            if (condition is null || !Accept("?")) return condition;
            var yes = ParseExpression();
            if (yes is null) return null;
            if (!Accept(":")) return Fail("a ? needs its :");
            var no = ParseExpression();
            return no is null ? null : Node(NthUiExprKind.Conditional, start, "?", condition, yes, no);
        }

        private NthUiExpr? ParseOr()
        {
            var start = Peek.Start;
            var left = ParseAnd();
            while (left is not null && (IsWord("or") || IsOp("||")))
            {
                at++;
                var right = ParseAnd();
                left = right is null ? null : Node(NthUiExprKind.Binary, start, "or", left, right);
            }
            return left;
        }

        private NthUiExpr? ParseAnd()
        {
            var start = Peek.Start;
            var left = ParseNot();
            while (left is not null && (IsWord("and") || IsOp("&&")))
            {
                at++;
                var right = ParseNot();
                left = right is null ? null : Node(NthUiExprKind.Binary, start, "and", left, right);
            }
            return left;
        }

        private NthUiExpr? ParseNot()
        {
            var start = Peek.Start;
            if (!IsWord("not") && !IsOp("!")) return ParseComparison();
            at++;
            if (++nesting > MaxNesting)
            {
                nesting--;
                return Fail("the expression is nested too deeply");
            }
            var operand = ParseNot();
            nesting--;
            return operand is null ? null : Node(NthUiExprKind.Unary, start, "not", operand);
        }

        private NthUiExpr? ParseComparison()
        {
            var start = Peek.Start;
            var left = ParseAdditive();
            while (left is not null && Peek.Kind == TokenKind.Op && Peek.Text is "==" or "!=" or "<" or "<=" or ">" or ">=" or "=")
            {
                var op = Peek.Text == "=" ? "==" : Peek.Text;
                at++;
                var right = ParseAdditive();
                left = right is null ? null : Node(NthUiExprKind.Binary, start, op, left, right);
            }
            return left;
        }

        private NthUiExpr? ParseAdditive()
        {
            var start = Peek.Start;
            var left = ParseMultiplicative();
            while (left is not null && (IsOp("+") || IsOp("-")))
            {
                var op = Peek.Text;
                at++;
                var right = ParseMultiplicative();
                left = right is null ? null : Node(NthUiExprKind.Binary, start, op, left, right);
            }
            return left;
        }

        private NthUiExpr? ParseMultiplicative()
        {
            var start = Peek.Start;
            var left = ParseUnary();
            while (left is not null && (IsOp("*") || IsOp("/") || IsOp("%")))
            {
                var op = Peek.Text;
                at++;
                var right = ParseUnary();
                left = right is null ? null : Node(NthUiExprKind.Binary, start, op, left, right);
            }
            return left;
        }

        private NthUiExpr? ParseUnary()
        {
            var start = Peek.Start;
            if (!IsOp("-") && !IsOp("+")) return ParsePostfix();
            var negate = Peek.Text == "-";
            at++;
            if (++nesting > MaxNesting)
            {
                nesting--;
                return Fail("the expression is nested too deeply");
            }
            var operand = ParseUnary();
            nesting--;
            if (operand is null) return null;
            if (!negate) return operand;
            if (operand.Kind == NthUiExprKind.Literal && operand.Value.IsNumber)
                return new NthUiExpr(NthUiExprKind.Literal, start, operand.End) { Value = NthUiValue.Of(-operand.Value.AsNumber) };
            return Node(NthUiExprKind.Unary, start, "neg", operand);
        }

        private NthUiExpr? ParsePostfix()
        {
            var start = Peek.Start;
            var target = ParsePrimary();
            while (target is not null && failure is null)
            {
                if (Accept("["))
                {
                    var index = ParseExpression();
                    if (index is null) return null;
                    if (!Accept("]")) return Fail("a [ needs its ]");
                    target = Node(NthUiExprKind.Index, start, "[]", target, index);
                }
                else if (IsOp(".") && tokens[at + 1].Kind == TokenKind.Name)
                {
                    at++;
                    var member = Peek.Text;
                    at++;
                    target = Make(new NthUiExpr(NthUiExprKind.Member, start, Peek.Start) { Name = member, Args = [target], Depth = target.Depth + 1 });
                }
                else if (IsOp("("))
                {
                    return Fail("only functions can be called");
                }
                else
                {
                    break;
                }
            }
            return target;
        }

        private NthUiExpr? ParsePrimary()
        {
            var token = Peek;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    at++;
                    return new NthUiExpr(NthUiExprKind.Literal, token.Start, token.End) { Value = NthUiValue.Of(token.Number) };
                case TokenKind.Text:
                    at++;
                    return new NthUiExpr(NthUiExprKind.Literal, token.Start, token.End) { Value = NthUiValue.Of(token.Text) };
                case TokenKind.Name:
                    at++;
                    switch (token.Text)
                    {
                        case "true": return new NthUiExpr(NthUiExprKind.Literal, token.Start, token.End) { Value = NthUiValue.True };
                        case "false": return new NthUiExpr(NthUiExprKind.Literal, token.Start, token.End) { Value = NthUiValue.False };
                        case "null": return new NthUiExpr(NthUiExprKind.Literal, token.Start, token.End) { Value = NthUiValue.Null };
                        case "and" or "or" or "not": return Fail($"'{token.Text}' needs a value before it");
                    }
                    if (IsOp("(")) return ParseCall(token);
                    if (IsOp("=>")) return Fail("a lambda is only allowed inside map, filter or sort");
                    return new NthUiExpr(NthUiExprKind.Name, token.Start, token.End) { Name = token.Text };
                case TokenKind.Op when token.Text == "(":
                    {
                        at++;
                        var inner = ParseExpression();
                        if (inner is null) return null;
                        return Accept(")") ? inner : Fail("a ( needs its )");
                    }
                case TokenKind.Op when token.Text == "[":
                    {
                        at++;
                        var items = new List<NthUiExpr>();
                        if (!Accept("]"))
                        {
                            do
                            {
                                if (IsOp("]")) break;
                                var item = ParseExpression();
                                if (item is null) return null;
                                items.Add(item);
                                if (items.Count > NthUiValue.MaxListLength) return Fail("the list is too long");
                            }
                            while (Accept(","));
                            if (!Accept("]")) return Fail("a [ needs its ]");
                        }
                        return Node(NthUiExprKind.List, token.Start, "[]", items.ToArray());
                    }
                case TokenKind.Op when token.Text == "{":
                    return ParseObject(token);
                case TokenKind.End:
                    return Fail("a value is missing");
                default:
                    return Fail($"unexpected '{token.Text}'");
            }
        }

        private NthUiExpr? ParseObject(Token open)
        {
            at++;
            var keys = new List<string>();
            var values = new List<NthUiExpr>();
            if (!Accept("}"))
            {
                do
                {
                    if (IsOp("}")) break;
                    var key = Peek;
                    if (key.Kind is not (TokenKind.Name or TokenKind.Text or TokenKind.Number)) return Fail("an object key must be a name or a text");
                    at++;
                    if (!Accept(":")) return Fail("an object key needs a :");
                    var value = ParseExpression();
                    if (value is null) return null;
                    keys.Add(key.Text);
                    values.Add(value);
                }
                while (Accept(","));
                if (!Accept("}")) return Fail("a { needs its }");
            }
            var args = values.ToArray();
            return Make(new NthUiExpr(NthUiExprKind.Object, open.Start, tokens[at - 1].End) { Args = args, Keys = keys, Depth = DepthOf(args) });
        }

        private NthUiExpr? ParseCall(Token name)
        {
            if (!Functions.TryGetValue(name.Text, out var arity)) return Fail($"there is no function '{name.Text}'");
            at++;
            var args = new List<NthUiExpr>();
            if (!Accept(")"))
            {
                do
                {
                    if (args.Count == 1 && TakesLambda(name.Text))
                    {
                        var lambda = ParseLambda();
                        if (lambda is null) return null;
                        args.Add(lambda);
                        continue;
                    }
                    var arg = ParseExpression();
                    if (arg is null) return null;
                    args.Add(arg);
                }
                while (Accept(","));
                if (!Accept(")")) return Fail("a ( needs its )");
            }
            if (args.Count < arity.Min || args.Count > arity.Max)
                return Fail(arity.Min == arity.Max ? $"{name.Text} takes {arity.Min} value{(arity.Min == 1 ? "" : "s")}" : $"{name.Text} takes {arity.Min} to {arity.Max} values");
            var array = args.ToArray();
            return Make(new NthUiExpr(NthUiExprKind.Call, name.Start, tokens[at - 1].End) { Name = name.Text, Args = array, Depth = DepthOf(array) });
        }

        /// <summary>`x => body`, `(x) => body`, or a bare body over the implicit parameter `it`.</summary>
        private NthUiExpr? ParseLambda()
        {
            var start = Peek.Start;
            var parameter = "it";
            if (Peek.Kind == TokenKind.Name && tokens[at + 1].Kind == TokenKind.Op && tokens[at + 1].Text == "=>")
            {
                parameter = Peek.Text;
                at += 2;
            }
            else if (IsOp("(") && tokens[at + 1].Kind == TokenKind.Name && tokens[at + 2].Kind == TokenKind.Op && tokens[at + 2].Text == ")"
                && tokens[at + 3].Kind == TokenKind.Op && tokens[at + 3].Text == "=>")
            {
                parameter = tokens[at + 1].Text;
                at += 4;
            }
            if (parameter != "it" && (IsReserved(parameter) || !IsName(parameter))) return Fail($"'{parameter}' cannot name a lambda's value");
            var body = ParseExpression();
            if (body is null) return null;
            return Make(new NthUiExpr(NthUiExprKind.Lambda, start, body.End) { Name = parameter, Args = [body], Depth = body.Depth + 1 });
        }
    }
}
