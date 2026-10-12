using System.Text;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 "Each line is complete when it ends, so the renderer builds the UI while
// tokens arrive and never repairs JSON", 7.4; ED-NEW-web-w15-1). No Swift counterpart. The streaming parser: feed it
// text in chunks of any size; each completed line becomes one node (or grows a code, svg or diagram body) and is
// reported as a change. A line's parent is the nearest line above it with a smaller indent; when that parent cannot
// hold the line's kind, the line goes to the nearest ancestor that can, and a line that fits nowhere degrades to its
// text. Nothing already placed ever moves, so a renderer can draw each node as it arrives. Every step is bounded by
// the input's length and the limits below.

namespace Marrow.Core.NthUi;

public enum NthUiChangeKind
{
    /// <summary>A new node: draw it.</summary>
    Added,

    /// <summary>A code, svg or diagram node's body grew by a line.</summary>
    BodyChanged,
}

public readonly record struct NthUiChange(NthUiChangeKind Kind, NthUiNode Node);

/// <summary>The line still arriving, when it is text the renderer can show early (DESIGN 7.4 "Text streams into text
/// nodes as it arrives"): a text, heading or callout line, or the next line of a body.</summary>
public sealed record NthUiPending(string Kind, string Text, NthUiNode? Parent);

public sealed class NthUiParser
{
    /// <summary>The most nodes one view may have; later lines are dropped with a note.</summary>
    public const int MaxNodes = 2_000;

    /// <summary>The deepest a node may sit (0 is the top level); deeper lines join the deepest allowed container.</summary>
    public const int MaxDepth = 24;

    /// <summary>The most characters a code, svg or diagram body may hold.</summary>
    public const int MaxBodyLength = 262_144;

    private const int MaxPartial = MaxBodyLength + NthUiLine.MaxLength;

    private readonly StringBuilder partial = new();
    private readonly List<(int Indent, NthUiNode Node)> open = [];
    private readonly List<NthUiChange> changes = [];
    private readonly HashSet<string> boundNames = new(StringComparer.Ordinal);
    private NthUiNode? bodyOwner;
    private int bodyIndent;
    private int bodyLength;
    private int lineIndex;
    private bool droppedNodes;

    public NthUiParser(NthUiProfile profile = NthUiProfile.Full)
    {
        Document = new NthUiDocument(profile);
    }

    public NthUiDocument Document { get; }

    /// <summary>Parses a whole text at once.</summary>
    public static NthUiDocument Parse(string text, NthUiProfile profile = NthUiProfile.Full)
    {
        var parser = new NthUiParser(profile);
        parser.Feed(text);
        parser.Complete();
        return parser.Document;
    }

    /// <summary>Adds streamed text. Returns the changes the completed lines made (the list is reused by the next call).</summary>
    public IReadOnlyList<NthUiChange> Feed(string chunk)
    {
        changes.Clear();
        if (Document.IsComplete) return changes;
        var start = 0;
        while (start < chunk.Length)
        {
            var newline = chunk.IndexOf('\n', start);
            if (newline < 0)
            {
                Append(chunk.AsSpan(start));
                break;
            }
            Append(chunk.AsSpan(start, newline - start));
            EndLine();
            start = newline + 1;
        }
        return changes;
    }

    /// <summary>Ends the stream: the last line counts even without a line break, and the notes about names are made.</summary>
    public IReadOnlyList<NthUiChange> Complete()
    {
        changes.Clear();
        if (Document.IsComplete) return changes;
        if (partial.Length > 0) EndLine();
        Document.IsComplete = true;
        NoteUndefinedNames();
        return changes;
    }

    /// <summary>The line still arriving, if it is text worth showing early; null otherwise.</summary>
    public NthUiPending? Pending
    {
        get
        {
            if (partial.Length == 0 || Document.IsComplete) return null;
            var text = partial.ToString().TrimEnd('\r');
            if (bodyOwner is not null && Indent(text) > bodyIndent) return new NthUiPending("body", StripIndent(text, bodyIndent + 2), bodyOwner);
            if (text.Length > NthUiLine.MaxLength) return null;
            var line = NthUiLine.Read(text);
            if (line.Head is not ("text" or "heading" or "callout") || line.Rest.Length == 0) return null;
            var shown = line.Rest;
            if (shown[0] is '"' or '\'')
            {
                var at = 0;
                NthUiExpr.ScanString(shown, ref at, out shown);
            }
            return new NthUiPending(line.Head, shown, ParentFor(line.Indent));
        }
    }

    private void Append(ReadOnlySpan<char> text)
    {
        var room = MaxPartial - partial.Length;
        if (room > 0) partial.Append(text[..Math.Min(room, text.Length)]);
    }

    private void EndLine()
    {
        var text = partial.ToString();
        partial.Clear();
        if (text.EndsWith('\r')) text = text[..^1];
        var index = lineIndex++;
        if (bodyOwner is not null)
        {
            if (text.Trim().Length == 0 || Indent(text) > bodyIndent)
            {
                var bodyLine = StripIndent(text, bodyIndent + 2);
                if (bodyLength + bodyLine.Length + 1 <= MaxBodyLength)
                {
                    bodyLength += bodyLine.Length + 1;
                    bodyOwner.AddBodyLine(bodyLine);
                    changes.Add(new NthUiChange(NthUiChangeKind.BodyChanged, bodyOwner));
                }
                return;
            }
            bodyOwner = null;
        }
        var line = NthUiLine.Read(text);
        if (line.IsBlank || line.Content.StartsWith("```", StringComparison.Ordinal)) return;
        if (Document.NodeList.Count >= MaxNodes)
        {
            if (!droppedNodes) Document.DiagnosticList.Add(new(index + 1, NthUiSeverity.Error, $"the view has more than {MaxNodes} lines, so the rest were left out"));
            droppedNodes = true;
            return;
        }
        while (open.Count > 0 && open[^1].Indent >= line.Indent) open.RemoveAt(open.Count - 1);
        var node = Place(line, index, open.Count > 0 ? open[^1].Node : null);
        open.Add((line.Indent, node));
        if (node.TakesBody)
        {
            bodyOwner = node;
            bodyIndent = line.Indent;
            bodyLength = 0;
        }
    }

    private NthUiNode? ParentFor(int indent)
    {
        for (var i = open.Count - 1; i >= 0; i--)
        {
            if (open[i].Indent < indent) return open[i].Node;
        }
        return null;
    }

    /// <summary>Builds the node for <paramref name="line"/> and attaches it under the nearest ancestor that can hold it.</summary>
    private NthUiNode Place(NthUiLine line, int index, NthUiNode? structural)
    {
        var warnings = new List<string>();
        // A line that starts with a kind is placed and bound even when it does not read cleanly: binding reports the read
        // error, and a text-like kind can still take the rest of the line as its text.
        string? problem = line.Head.Length == 0 ? line.Error ?? "the line does not start with a component" : null;
        NthUiKindSpec? spec = null;
        NthUiNode? parent = null;
        var takesBody = line.Head is "code" or "svg" or "diagram";
        if (problem is null)
        {
            for (var candidate = structural; ; candidate = candidate.Parent)
            {
                var children = candidate is null ? NthUiChildren.Root : candidate.Degraded ? NthUiChildren.None : candidate.Spec.Children;
                var resolved = NthUiSchema.Resolve(line.Head, candidate?.Spec);
                if (resolved is not null && NthUiSchema.Accepts(children, resolved) && (candidate is null || candidate.Depth < MaxDepth))
                {
                    spec = resolved;
                    parent = candidate;
                    break;
                }
                if (candidate is null) break;
            }
            if (spec is null)
            {
                problem = NthUiSchema.IsKnown(line.Head) || line.Head == "row"
                    ? $"{line.Head} cannot be {(structural is null ? "at the top level" : $"inside {structural.Kind}")}"
                    : $"there is no component '{line.Head}'";
            }
            else if (Document.Profile == NthUiProfile.Lite && !spec.Lite)
            {
                problem = $"{spec.Kind} is not in the lite subset";
                spec = null;
            }
        }
        NthUiValidator.Bound? bound = null;
        if (spec is not null)
        {
            bound = NthUiValidator.Bind(line, spec, warnings, out var why);
            if (bound is null) problem = why.Length > 0 ? why : $"{spec.Kind} could not be read";
            else problem = CheckNames(spec, bound);
        }
        foreach (var warning in warnings) Document.DiagnosticList.Add(new(index + 1, NthUiSeverity.Warning, warning));
        NthUiNode node;
        if (problem is null && spec is not null && bound is not null)
        {
            node = new NthUiNode
            {
                Kind = spec.Kind,
                Spec = spec,
                Line = index,
                Source = line.Content,
                Args = bound.Args,
                Flags = bound.Flags,
                Cells = bound.Cells,
                Reads = ReadsOf(bound),
                TakesBody = takesBody,
            };
            Register(node);
        }
        else
        {
            Document.DiagnosticList.Add(new(index + 1, NthUiSeverity.Error, $"{problem}; shown as text"));
            parent = structural;
            while (parent is not null && (parent.Degraded || !NthUiSchema.TakesText(parent.Spec.Children) || parent.Depth >= MaxDepth)) parent = parent.Parent;
            var text = line.Content.Length > NthUiLine.MaxLength ? line.Content[..NthUiLine.MaxLength] + "…" : line.Content;
            var textSpec = NthUiSchema.Resolve("text", null)!;
            node = new NthUiNode
            {
                Kind = "text",
                Spec = textSpec,
                Problem = problem ?? "the line could not be read",
                Line = index,
                Source = line.Content,
                Args = new Dictionary<string, NthUiArg>(StringComparer.Ordinal)
                {
                    ["text"] = new() { Name = "text", Type = NthUiArgType.Text, Literal = NthUiValue.Of(text) },
                },
                TakesBody = takesBody,
            };
        }
        Attach(node, parent, node.Degraded ? null : bound?.Args.GetValueOrDefault("id")?.Literal.AsText, index);
        return node;
    }

    /// <summary>The name rules a single line can check: no name declared twice, a let never set by a control or `set`.</summary>
    private string? CheckNames(NthUiKindSpec spec, NthUiValidator.Bound bound)
    {
        var name = bound.Args.GetValueOrDefault(spec.Role == NthUiRole.Declaration ? "name" : spec.Kind == "set" || spec == NthUiSchema.ToggleAction ? "target" : "bind")?.Literal.AsText;
        if (string.IsNullOrEmpty(name)) return null;
        var declared = Document.DeclarationsByName.GetValueOrDefault(name);
        if (spec.Role == NthUiRole.Declaration)
        {
            if (declared is not null) return $"{name} is already declared on line {declared.Line + 1}";
            if (spec.Kind == "let" && boundNames.Contains(name)) return $"{name} is set by a control, so it cannot be a let";
            return null;
        }
        if (declared?.Kind == "let") return $"{name} is computed by a let, so nothing can set it";
        return null;
    }

    private void Register(NthUiNode node)
    {
        if (node.Role == NthUiRole.Declaration)
        {
            Document.DeclarationsByName[node.Name!] = node;
            return;
        }
        var bind = node.Arg("bind")?.Literal.AsText;
        if (!string.IsNullOrEmpty(bind)) boundNames.Add(bind);
        var target = node.Arg("target")?.Literal.AsText;
        if (!string.IsNullOrEmpty(target)) boundNames.Add(target);
    }

    private static HashSet<string> ReadsOf(NthUiValidator.Bound bound)
    {
        var reads = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in bound.Args.Values.Concat(bound.Cells))
        {
            if (arg.Expr is not null) reads.UnionWith(arg.Expr.Names);
            else if (arg.Name == "bind" && arg.Literal.IsText) reads.Add(arg.Literal.AsText);
        }
        return reads;
    }

    private void Attach(NthUiNode node, NthUiNode? parent, string? id, int index)
    {
        var siblings = parent?.ChildList ?? Document.RootList;
        var key = parent is null ? siblings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : parent.Key + "." + siblings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(id))
        {
            if (Document.ByKey.ContainsKey(id)) Document.DiagnosticList.Add(new(index + 1, NthUiSeverity.Warning, $"the id {id} is used twice, so this one was left out"));
            else
            {
                key = id;
                node.Id = id;
            }
        }
        node.Key = key;
        node.Parent = parent;
        node.Depth = parent is null ? 0 : parent.Depth + 1;
        siblings.Add(node);
        Document.NodeList.Add(node);
        Document.ByKey[key] = node;
        changes.Add(new NthUiChange(NthUiChangeKind.Added, node));
    }

    private void NoteUndefinedNames()
    {
        var known = new HashSet<string>(Document.DeclarationsByName.Keys, StringComparer.Ordinal);
        known.UnionWith(boundNames);
        foreach (var node in Document.NodeList)
        {
            foreach (var name in node.Reads.Order(StringComparer.Ordinal))
            {
                if (!known.Contains(name)) Document.DiagnosticList.Add(new(node.Line + 1, NthUiSeverity.Warning, $"{name} is not defined, so it shows as a dash"));
            }
        }
    }

    private static int Indent(string text)
    {
        var columns = 0;
        foreach (var c in text)
        {
            if (c == ' ') columns++;
            else if (c == '\t') columns += 2;
            else break;
        }
        return columns;
    }

    /// <summary>Removes up to <paramref name="columns"/> columns of indent (a tab counts as two).</summary>
    private static string StripIndent(string text, int columns)
    {
        var i = 0;
        var removed = 0;
        while (i < text.Length && removed < columns && (text[i] == ' ' || text[i] == '\t'))
        {
            removed += text[i] == '\t' ? 2 : 1;
            i++;
        }
        return text[i..];
    }
}
