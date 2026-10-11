// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2, 7.4 "keeps a keyed tree. Each completed line adds or updates one node";
// ED-NEW-web-w15-1). No Swift counterpart. The tree a parse builds: one node per completed line, keyed by its explicit
// id or by its place (its parent's key, a dot, its index), so a key never changes once given and earlier nodes never
// move while later lines arrive.

namespace Marrow.Core.NthUi;

public enum NthUiProfile
{
    /// <summary>Every component of the language.</summary>
    Full,

    /// <summary>The subset for small local models (DESIGN 7.3); other components degrade to their text.</summary>
    Lite,
}

public enum NthUiSeverity
{
    /// <summary>Something was left out or guessed, and the view still works (an unknown attribute, an undefined name).</summary>
    Warning,

    /// <summary>A line could not be used and shows as its text.</summary>
    Error,
}

/// <summary>A note about one line (1-based), for the teaching fixtures and for debugging a model's output.</summary>
public sealed record NthUiDiagnostic(int Line, NthUiSeverity Severity, string Message)
{
    public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>One argument of a node: a literal value, or an expression evaluated against the view's state.</summary>
public sealed class NthUiArg
{
    public required string Name { get; init; }

    public required NthUiArgType Type { get; init; }

    /// <summary>The value when the argument is a literal.</summary>
    public NthUiValue Literal { get; init; }

    /// <summary>The expression when the argument is computed; null for a literal.</summary>
    public NthUiExpr? Expr { get; init; }

    public bool IsLiteral => Expr is null;

    public override string ToString() => Expr?.ToString() ?? Literal.Display();
}

/// <summary>One node of a live view: a component, a part, an action, a declaration, or a line degraded to its text.</summary>
public sealed class NthUiNode
{
    private static readonly IReadOnlyDictionary<string, NthUiArg> NoArgs = new Dictionary<string, NthUiArg>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>(StringComparer.Ordinal);

    internal readonly List<NthUiNode> ChildList = [];
    internal List<string>? BodyLines;
    private string? body;

    /// <summary>The node's key: its explicit id, or its place ("0", "0.3", "0.3.1"). Given once, never changed.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>The explicit `id=` of the line, if it had a unique one.</summary>
    public string? Id { get; internal set; }

    /// <summary>The component or keyword ("card", "row", "state"); "text" for a degraded line.</summary>
    public required string Kind { get; init; }

    public required NthUiKindSpec Spec { get; init; }

    public NthUiRole Role => Spec.Role;

    /// <summary>Why the line could not be used as written; it then shows as its text. Null for a good line.</summary>
    public string? Problem { get; init; }

    public bool Degraded => Problem is not null;

    /// <summary>The 0-based index of the line in the stream.</summary>
    public required int Line { get; init; }

    /// <summary>The line without its indent.</summary>
    public required string Source { get; init; }

    public NthUiNode? Parent { get; internal set; }

    /// <summary>0 at the top level.</summary>
    public int Depth { get; internal set; }

    public IReadOnlyList<NthUiNode> Children => ChildList;

    public IReadOnlyDictionary<string, NthUiArg> Args { get; init; } = NoArgs;

    public IReadOnlySet<string> Flags { get; init; } = NoNames;

    /// <summary>A table row's cells, in order.</summary>
    public IReadOnlyList<NthUiArg> Cells { get; init; } = [];

    /// <summary>The names of states and lets this node reads; a change to one of them re-renders it.</summary>
    public IReadOnlySet<string> Reads { get; init; } = NoNames;

    /// <summary>Whether the indented lines below are this node's body (code, svg, diagram), even when degraded.</summary>
    public bool TakesBody { get; init; }

    /// <summary>The verbatim body of a code, svg or diagram node, without trailing blank lines; null when it has none.</summary>
    public string? Body
    {
        get
        {
            if (BodyLines is null || BodyLines.Count == 0) return null;
            if (body is not null) return body;
            var count = BodyLines.Count;
            while (count > 0 && BodyLines[count - 1].Trim().Length == 0) count--;
            return body = string.Join("\n", BodyLines.Take(count));
        }
    }

    internal void AddBodyLine(string line)
    {
        (BodyLines ??= []).Add(line);
        body = null;
    }

    public NthUiArg? Arg(string name) => Args.GetValueOrDefault(name);

    public bool Has(string flag) => Flags.Contains(flag);

    /// <summary>The literal text of the node's first text argument (text, title, label, name of a series or stop).</summary>
    public string? Text
    {
        get
        {
            foreach (var spec in Spec.Positionals)
            {
                if (spec.Type != NthUiArgType.Text) continue;
                var arg = Arg(spec.Name);
                if (arg is not null && arg.IsLiteral) return arg.Literal.Display();
            }
            return null;
        }
    }

    /// <summary>The state or let this node binds, declares or sets.</summary>
    public string? Name => (Arg("bind") ?? Arg("name") ?? Arg("target"))?.Literal.AsText;

    public override string ToString() => $"{Key} {Kind}";
}

/// <summary>A parsed live view: its top-level nodes, every node in arrival order, and the notes the parse made.</summary>
public sealed class NthUiDocument
{
    internal readonly List<NthUiNode> RootList = [];
    internal readonly List<NthUiNode> NodeList = [];
    internal readonly Dictionary<string, NthUiNode> ByKey = new(StringComparer.Ordinal);
    internal readonly List<NthUiDiagnostic> DiagnosticList = [];
    internal readonly Dictionary<string, NthUiNode> DeclarationsByName = new(StringComparer.Ordinal);

    internal NthUiDocument(NthUiProfile profile)
    {
        Profile = profile;
    }

    public NthUiProfile Profile { get; }

    public IReadOnlyList<NthUiNode> Roots => RootList;

    /// <summary>Every node, in the order its line arrived.</summary>
    public IReadOnlyList<NthUiNode> Nodes => NodeList;

    public IReadOnlyList<NthUiDiagnostic> Diagnostics => DiagnosticList;

    /// <summary>The state and let declarations by name.</summary>
    public IReadOnlyDictionary<string, NthUiNode> Declarations => DeclarationsByName;

    /// <summary>Whether the stream has ended (<see cref="NthUiParser.Complete"/> was called).</summary>
    public bool IsComplete { get; internal set; }

    /// <summary>No line degraded and no error notes: what a teaching fixture must produce.</summary>
    public bool IsClean => NodeList.All(n => !n.Degraded) && DiagnosticList.All(d => d.Severity != NthUiSeverity.Error);

    public NthUiNode? Find(string key) => ByKey.GetValueOrDefault(key);
}
