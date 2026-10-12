using System.Text;

// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.3 "Local models get a lite subset ... and, where the runtime supports
// it (llama.cpp GBNF, ...), grammar-constrained output"; ED-NEW-web-w17-1). No Swift counterpart. A GBNF grammar of the
// lite subset, written from NthUiSchema so it cannot drift from the language: the kinds, their values, attributes and
// flags, the tail after =, and the indent of every level (card at 0, its parts at 2, a table's rows at 4). The
// expression grammar is a strict subset of the language's (single spaces around operators), so a constrained model
// writes one canonical style. The grammar starts at the opening fence, because a llama.cpp server applies it lazily:
// free text until the model writes ```nth-ui, then only a valid lite view, ending with the closing fence.
//
// GBNF here follows llama.cpp's reader: rule names of letters, digits and dashes; each rule on one line; no repetition
// counts; escapes \n \" \\ in literals and classes.

namespace Marrow.Core.NthUi;

public static class NthUiGrammar
{
    /// <summary>What the model writes to open a view; a lazy grammar waits for this text.</summary>
    public const string FenceOpen = "```nth-ui";

    /// <summary>The text that starts the grammar (llama.cpp grammar triggers).</summary>
    public static IReadOnlyList<string> Triggers { get; } = [FenceOpen];

    /// <summary>The root rule's name.</summary>
    public const string Root = "root";

    private static readonly Lazy<string> Text = new(Build);

    /// <summary>The grammar, one rule per line.</summary>
    public static string Gbnf => Text.Value;

    private const int MaxLevel = 2;

    private static string Lit(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
            builder.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", _ => c.ToString() });
        return builder.Append('"').ToString();
    }

    private static string Alternatives(IEnumerable<string> parts) => string.Join(" | ", parts);

    private static string Name(string kind) => kind.Replace('_', '-');

    private static IEnumerable<NthUiKindSpec> LiteSpecs() =>
        NthUiSchema.Kinds.Where(spec => spec.Lite).Concat([NthUiSchema.TableRow]);

    private static string Build()
    {
        var rules = new List<(string Name, string Body)>();
        void Rule(string name, string body) => rules.Add((name, body));

        // The lexical rules.
        Rule("str", "\"\\\"\" strchar* \"\\\"\"");
        Rule("strchar", "[^\"\\\\\\n] | \"\\\\\" [\"\\\\ntr]");
        Rule("num", "\"-\"? ([0-9]+ (\".\" [0-9]+)? | \".\" [0-9]+)");
        Rule("ident", "[a-zA-Z_] [a-zA-Z0-9_]*");
        Rule("word", "[a-z] [a-z0-9-]*");

        // Expressions: the language's own, with one space around every operator.
        Rule("expr", "logic (\" ? \" expr \" : \" expr)?");
        Rule("logic", "conj (\" or \" conj)*");
        Rule("conj", "neg (\" and \" neg)*");
        Rule("neg", "\"not \" neg | cmp");
        Rule("cmp", "sum ((\" == \" | \" != \" | \" <= \" | \" >= \" | \" < \" | \" > \") sum)?");
        Rule("sum", "prod ((\" + \" | \" - \") prod)*");
        Rule("prod", "unary ((\" * \" | \" / \" | \" % \") unary)*");
        Rule("unary", "\"-\" unary | postfix");
        Rule("postfix", "atom (\"[\" expr \"]\" | \".\" ident)*");
        Rule("atom", "call | num | str | \"true\" | \"false\" | ident | \"(\" expr \")\" | list");
        Rule("call", Alternatives(NthUiExpr.Functions.Keys.Select(name => "fn-" + name)));
        foreach (var (name, (min, max)) in NthUiExpr.Functions) Rule("fn-" + name, FunctionBody(name, min, max));
        Rule("lambda", "ident \" => \" expr");
        Rule("list", "\"[\" (expr (\", \" expr)*)? \"]\"");

        // The value rules for attributes and slots.
        Rule("val-text", "str | num | \"(\" expr \")\"");
        Rule("val-word", "str | num | ident | \"(\" expr \")\""); // a named attribute also takes a bare word as its text (value=cs)
        Rule("val-number", "num | ident | \"(\" expr \")\"");
        Rule("val-bool", "\"true\" | \"false\" | ident | \"(\" expr \")\"");
        Rule("val-list", "str | list | \"(\" expr \")\"");
        Rule("val-any", "num | str | list | \"true\" | \"false\" | ident | \"(\" expr \")\"");

        var specs = LiteSpecs().ToArray();
        foreach (var spec in specs) Rule(LineName(spec), LineBody(spec));

        // The structure: which kind sits at which level (0 is the top, each level indents two more spaces).
        foreach (var spec in specs)
        {
            for (var level = 0; level <= MaxLevel; level++)
            {
                if (!Appears(spec, level)) continue;
                var body = (level == 0 ? "" : Lit(new string(' ', level * 2)) + " ") + LineName(spec) + " \"\\n\"";
                if (level < MaxLevel && ChildrenOf(spec) is { Length: > 0 } children)
                    body += " (" + Alternatives(children.Select(child => LevelName(child, level + 1))) + ")*";
                Rule(LevelName(spec, level), body);
            }
        }
        Rule("view", "(" + Alternatives(specs.Where(spec => Appears(spec, 0)).Select(spec => LevelName(spec, 0))) + ")+");
        Rule(Root, Lit(FenceOpen + "\n") + " view " + Lit("```") + " " + Lit("\n") + "?");

        // root first, as llama.cpp's own samples do; the rest in the order they were made.
        var builder = new StringBuilder();
        var ordered = rules.Where(rule => rule.Name == Root).Concat(rules.Where(rule => rule.Name != Root));
        foreach (var (name, body) in ordered) builder.Append(name).Append(" ::= ").Append(body).Append('\n');
        return builder.ToString();
    }

    // A call with the argument counts the language checks, and a lambda where map, filter and sort take one.
    private static string FunctionBody(string name, int min, int max)
    {
        string Arg(int index) => index == 1 && NthUiExpr.TakesLambda(name) ? "lambda" : "expr";
        var body = Lit(name + "(") + " " + Arg(0);
        if (max >= 64) body += " (\", \" expr)*";
        else
        {
            for (var index = 1; index < min; index++) body += " \", \" " + Arg(index);
            var optional = "";
            for (var index = max - 1; index >= min; index--)
                optional = "(\", \" " + Arg(index) + (optional.Length > 0 ? " " + optional : "") + ")?";
            if (optional.Length > 0) body += " " + optional;
        }
        return body + " \")\"";
    }

    private static string LineName(NthUiKindSpec spec) => Name(spec.Kind) + "-line";

    private static string LevelName(NthUiKindSpec spec, int level) => Name(spec.Kind) + "-" + level;

    /// <summary>Where a kind may sit: parts only under their container, a card only at the top, everything else at the top and inside a card.</summary>
    private static bool Appears(NthUiKindSpec spec, int level)
    {
        if (spec.Role == NthUiRole.Part) return level is >= 1 and <= MaxLevel;
        if (spec.Kind == "card") return level == 0;
        if (spec.Kind == "followups") return level == 0;
        return level <= 1;
    }

    private static NthUiKindSpec[] ChildrenOf(NthUiKindSpec spec) =>
    [
        .. LiteSpecs().Where(child => child.Role != NthUiRole.Part && spec.Children == NthUiChildren.Blocks && NthUiSchema.Accepts(NthUiChildren.Blocks, child) && child.Kind != "card"),
        .. LiteSpecs().Where(child => child.Role == NthUiRole.Part && spec.Children is not (NthUiChildren.Blocks or NthUiChildren.None) && NthUiSchema.Accepts(spec.Children, child)),
    ];

    // A positional slot takes a quoted text, a number or an expression; a named attribute may also be a bare word.
    private static string ValueOf(NthUiArgSpec arg, bool positional) => arg.Type switch
    {
        NthUiArgType.Text => positional ? "val-text" : "val-word",
        NthUiArgType.Number => "val-number",
        NthUiArgType.Bool => "val-bool",
        NthUiArgType.Name => "ident",
        NthUiArgType.Choice => arg.Choices is { Count: > 0 } choices ? "(" + Alternatives(choices.Select(Lit)) + ")" : "word",
        NthUiArgType.Values or NthUiArgType.Options => "val-list",
        NthUiArgType.Icon => "word",
        NthUiArgType.Url => "str",
        _ => "val-any",
    };

    // The schema's order, except a chart names its series before their data (x, series, y), as the reference's examples write it.
    private static IEnumerable<NthUiArgSpec> Ordered(NthUiKindSpec spec)
    {
        if (spec.Kind != "chart") return spec.Attributes;
        string[] order = ["title", "x", "series", "y", "unit", "min", "max", "step", "editable", "bind"];
        return spec.Attributes.OrderBy(a => Array.IndexOf(order, a.Name) is var at && at < 0 ? int.MaxValue : at);
    }

    // One line of a kind, in the canonical order the reference shows: head, flags that lead (text and row), the values in order,
    // attributes in the schema's order (each once), flags that trail, id and when, then the tail. Each name and flag can appear
    // once, so a constrained model can never write the "value given twice" that the language degrades.
    private static string LineBody(NthUiKindSpec spec)
    {
        var parts = new List<string> { Lit(spec.Kind) };
        var flagsLead = spec.Kind is "text" or "row";
        if (flagsLead) parts.AddRange(spec.Flags.Select(flag => "(" + Lit(" " + flag) + ")?"));
        var tailUsed = false;
        if (spec.Cells)
        {
            parts.Add("((\" \" val-any)+ (\" = \" expr)? | \" = \" expr)");
            tailUsed = true;
        }
        foreach (var slot in spec.Positionals)
        {
            var value = "\" \" " + ValueOf(slot, positional: true);
            if (slot.Required && spec.Tail == slot.Name)
            {
                // The slot or the tail fills it: text "Hello" or text = expression.
                parts.Add("(" + value + " | \" = \" expr)");
                tailUsed = true;
            }
            else parts.Add(slot.Required ? value : "(" + value + ")?");
        }
        // An attribute the tail also fills (stat value=, series y=, copy value=) is the tail's alternative at the end of the line, never both:
        // the language degrades a value given twice.
        NthUiArgSpec? tailAttribute = null;
        foreach (var attribute in Ordered(spec).Where(a => a.Type is not (NthUiArgType.Object or NthUiArgType.Tool)))
        {
            if (!tailUsed && attribute.Name == spec.Tail) tailAttribute = attribute;
            else parts.Add("(" + Lit(" " + attribute.Name + "=") + " " + ValueOf(attribute, positional: false) + ")?");
        }
        if (!flagsLead) parts.AddRange(spec.Flags.Select(flag => "(" + Lit(" " + flag) + ")?"));
        if (spec.Role != NthUiRole.Declaration)
        {
            parts.Add("(" + Lit(" id=") + " ident)?");
            parts.Add("(" + Lit(" when=") + " (ident | \"(\" expr \")\"))?");
        }
        if (spec.Tail is not null && !tailUsed)
        {
            var tail = "\" = \" expr";
            if (tailAttribute is not null) tail = Lit(" " + tailAttribute.Name + "=") + " " + ValueOf(tailAttribute, positional: false) + " | " + tail;
            parts.Add(spec.TailRequired ? "(" + tail + ")" : "(" + tail + ")?");
        }
        return string.Join(" ", parts);
    }
}
