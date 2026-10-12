using System.Text.RegularExpressions;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 "Unknown or invalid lines degrade to their text"; ED-NEW-web-w15-1). No
// Swift counterpart. Binds a read line to its kind's spec: positional values to their slots in order, attributes by
// name, flags, the tail, required arguments, and the few rules one kind has (an editable chart needs bind=, open needs
// an http or https address, an image only loads https, data: raster or artifact: sources). A line that does not fit
// degrades to its text; a text-like line that does not fit is read as plain words instead. Unknown attributes and bad
// icons are left out with a warning, so a cosmetic slip never costs the whole line.

namespace Marrow.Core.NthUi;

public static partial class NthUiValidator
{
    [GeneratedRegex(@"\A[a-z][a-z0-9-]{0,31}\z")]
    private static partial Regex IconName();

    [GeneratedRegex(@"\A[a-z0-9+#._-]{1,24}\z")]
    private static partial Regex AnyChoice();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\.[A-Za-z0-9_.-]{1,128}\z")]
    private static partial Regex ToolName();

    [GeneratedRegex(@"\Adata:image/(png|jpeg|gif|webp)(;base64)?,[A-Za-z0-9+/=%._-]*\z")]
    private static partial Regex RasterDataUri();

    [GeneratedRegex(@"\Aartifact:[A-Za-z0-9_-]{1,64}(/(?!\.\.?(?:/|\z))[A-Za-z0-9_.-]{1,128}){1,8}\z")]
    private static partial Regex ArtifactAsset();

    internal sealed record Bound(Dictionary<string, NthUiArg> Args, HashSet<string> Flags, List<NthUiArg> Cells);

    /// <summary>Binds <paramref name="line"/> to <paramref name="spec"/>, or says why it does not fit.</summary>
    internal static Bound? Bind(NthUiLine line, NthUiKindSpec spec, List<string> warnings, out string problem)
    {
        var bound = TryBind(line, spec, warnings, out problem);
        if (bound is not null || !spec.FreeText || line.Content.Length > NthUiLine.MaxLength) return bound;
        var text = line.Rest;
        if (text.Length > 0 && text[0] is '"' or '\'' && line.Error is not null)
        {
            var at = 0;
            NthUiExpr.ScanString(text, ref at, out var partial);
            text = partial;
        }
        if (text.Trim().Length == 0) return null;
        var first = spec.Positionals[0].Name;
        problem = "";
        return new Bound(
            new Dictionary<string, NthUiArg>(StringComparer.Ordinal) { [first] = new() { Name = first, Type = NthUiArgType.Text, Literal = NthUiValue.Of(text) } },
            new HashSet<string>(StringComparer.Ordinal),
            []);
    }

    private static Bound? TryBind(NthUiLine line, NthUiKindSpec spec, List<string> warnings, out string problem)
    {
        problem = "";
        if (line.Error is not null)
        {
            problem = line.Error;
            return null;
        }
        var args = new Dictionary<string, NthUiArg>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var cells = new List<NthUiArg>();
        var slot = 0;
        foreach (var token in line.Tokens)
        {
            if (token.IsAttribute)
            {
                var name = token.Name!;
                if (spec.Flags.Contains(name))
                {
                    if (token.Kind == NthUiTokenKind.Word && token.Literal.IsBool)
                    {
                        if (token.Literal.AsBool) flags.Add(name);
                        continue;
                    }
                    problem = $"{name}= takes true or false";
                    return null;
                }
                var argSpec = spec.Find(name);
                if (argSpec is null)
                {
                    warnings.Add($"{spec.Kind} has no {name}=, so it was left out");
                    continue;
                }
                if (args.ContainsKey(name))
                {
                    problem = $"{name} is given twice";
                    return null;
                }
                if (!TryConvert(token, argSpec, spec.Kind, out var arg, out var why))
                {
                    if (argSpec.Type == NthUiArgType.Icon)
                    {
                        warnings.Add(why);
                        continue;
                    }
                    problem = why;
                    return null;
                }
                args[name] = arg!;
                continue;
            }
            if (token.Kind == NthUiTokenKind.Word && spec.Flags.Contains(token.Raw.ToLowerInvariant()))
            {
                flags.Add(token.Raw.ToLowerInvariant());
                continue;
            }
            if (spec.Cells)
            {
                if (token.Expr is null)
                {
                    problem = $"'{token.Raw}' is not a value";
                    return null;
                }
                cells.Add(Cell(token));
                continue;
            }
            var placed = false;
            var refused = "";
            for (var j = slot; j < spec.Positionals.Count; j++)
            {
                var positional = spec.Positionals[j];
                if (args.ContainsKey(positional.Name)) continue;
                if (Fits(token, positional))
                {
                    if (TryConvert(token, positional, spec.Kind, out var arg, out var why))
                    {
                        args[positional.Name] = arg!;
                        slot = j + 1;
                        placed = true;
                        break;
                    }
                    // A name, an address or a tool in the wrong shape says why; a word that is no choice is simply not taken.
                    if (refused.Length == 0 && positional.Type is NthUiArgType.Name or NthUiArgType.Url or NthUiArgType.Tool) refused = why;
                }
                // A bracketed value where an object belongs (call's arguments) is not read as a later text slot.
                if (positional.Required || (positional.Type == NthUiArgType.Object && token.Kind == NthUiTokenKind.Expr)) break;
            }
            if (!placed)
            {
                problem = refused.Length > 0 ? refused : $"{spec.Kind} does not take {Describe(token)}";
                return null;
            }
        }
        if (line.Tail is not null)
        {
            if (spec.Tail is null)
            {
                problem = $"{spec.Kind} takes no = value";
                return null;
            }
            var tail = FromExpr(spec.Tail, NthUiArgType.Expr, line.Tail);
            if (spec.Cells) cells.Add(tail);
            else if (args.ContainsKey(spec.Tail))
            {
                problem = $"{spec.Tail} is given twice";
                return null;
            }
            else args[spec.Tail] = tail;
        }
        else if (spec.TailRequired)
        {
            var missing = spec.Positionals.FirstOrDefault(p => p.Required && !args.ContainsKey(p.Name));
            problem = missing is null ? $"{spec.Kind} needs = and a value" : $"{spec.Kind} needs {missing.Name}";
            return null;
        }
        foreach (var required in spec.Positionals.Concat(spec.Attributes))
        {
            if (required.Required && !args.ContainsKey(required.Name))
            {
                problem = $"{spec.Kind} needs {required.Name}";
                return null;
            }
        }
        if (!KindRules(spec, args, warnings, out problem)) return null;
        return new Bound(args, flags, cells);
    }

    private static bool KindRules(NthUiKindSpec spec, Dictionary<string, NthUiArg> args, List<string> warnings, out string problem)
    {
        problem = "";
        switch (spec.Kind)
        {
            case "copy" or "export" when args.TryGetValue("value", out var what) && what.Expr is { Kind: NthUiExprKind.Name, Name: "table" or "view" }:
                // `copy value=table` names the nearest table (and `view` the whole view), not a state called table.
                args["value"] = new NthUiArg { Name = "value", Type = what.Type, Literal = NthUiValue.Of(what.Expr.Name) };
                break;
            case "chart" when args.ContainsKey("editable") && !args.ContainsKey("bind"):
                args.Remove("editable");
                warnings.Add("editable=points needs bind= and a list state, so the chart is not editable");
                break;
            case "open":
                {
                    if (!args.ContainsKey("url") && args.TryGetValue("label", out var label) && label.IsLiteral && IsWebUrl(label.Literal.AsText))
                    {
                        args["url"] = new NthUiArg { Name = "url", Type = label.Type, Literal = label.Literal };
                        args.Remove("label");
                    }
                    if (!args.TryGetValue("url", out var url) || !url.IsLiteral)
                    {
                        problem = "open needs an http or https address in quotes";
                        return false;
                    }
                    if (!IsWebUrl(url.Literal.AsText))
                    {
                        problem = $"'{Clip(url.Literal.AsText)}' is not an address nth opens";
                        return false;
                    }
                    break;
                }
        }
        return true;
    }

    /// <summary>Whether a positional token may go to <paramref name="slot"/> (before converting it).</summary>
    private static bool Fits(NthUiToken token, NthUiArgSpec slot) => slot.Type switch
    {
        NthUiArgType.Text => token.Kind is NthUiTokenKind.Text or NthUiTokenKind.Number or NthUiTokenKind.Expr,
        NthUiArgType.Url => token.Kind == NthUiTokenKind.Text,
        NthUiArgType.Name => token.Kind == NthUiTokenKind.Word,
        NthUiArgType.Choice => token.Kind is NthUiTokenKind.Word or NthUiTokenKind.Text,
        NthUiArgType.Object => token.Kind == NthUiTokenKind.Expr && token.Expr?.Kind == NthUiExprKind.Object,
        NthUiArgType.Tool => token.Kind == NthUiTokenKind.Word,
        NthUiArgType.Icon => token.Kind is NthUiTokenKind.Word or NthUiTokenKind.Text,
        NthUiArgType.Number or NthUiArgType.Bool or NthUiArgType.Expr or NthUiArgType.Values or NthUiArgType.Options => true,
    };

    private static bool TryConvert(NthUiToken token, NthUiArgSpec spec, string kind, out NthUiArg? arg, out string why)
    {
        arg = null;
        why = "";
        var name = spec.Name;
        switch (spec.Type)
        {
            case NthUiArgType.Text:
                switch (token.Kind)
                {
                    case NthUiTokenKind.Text or NthUiTokenKind.Word or NthUiTokenKind.Number:
                        arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw));
                        return true;
                    default:
                        arg = Computed(name, spec.Type, token.Expr!);
                        return true;
                }
            case NthUiArgType.Number:
                switch (token.Kind)
                {
                    case NthUiTokenKind.Number:
                        arg = Lit(name, spec.Type, NthUiValue.Of(Math.Clamp(token.Literal.AsNumber, spec.Min, spec.Max)));
                        return true;
                    case NthUiTokenKind.Text when double.TryParse(token.Raw.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed):
                        arg = Lit(name, spec.Type, NthUiValue.Of(Math.Clamp(parsed, spec.Min, spec.Max)));
                        return true;
                    case NthUiTokenKind.Word when token.Expr is { Kind: NthUiExprKind.Name }:
                    case NthUiTokenKind.Expr:
                        arg = Computed(name, spec.Type, token.Expr!);
                        return true;
                }
                why = $"{name} needs a number";
                return false;
            case NthUiArgType.Bool:
                if (token.Literal.IsBool || token.Kind == NthUiTokenKind.Number)
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Literal.IsTruthy));
                    return true;
                }
                if (token.Kind == NthUiTokenKind.Text && token.Raw is "true" or "false")
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw == "true"));
                    return true;
                }
                if (token.Expr is not null && token.Kind != NthUiTokenKind.Text)
                {
                    arg = Computed(name, spec.Type, token.Expr);
                    return true;
                }
                why = $"{name} needs true, false or a condition";
                return false;
            case NthUiArgType.Name:
                if (token.Kind == NthUiTokenKind.Word && NthUiExpr.IsName(token.Raw) && !NthUiExpr.IsReserved(token.Raw))
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw));
                    return true;
                }
                why = $"'{token.Raw}' cannot be a name";
                return false;
            case NthUiArgType.Choice:
                {
                    var word = token.Raw.ToLowerInvariant();
                    var ok = token.Kind is NthUiTokenKind.Word or NthUiTokenKind.Text
                        && (spec.Choices is null ? AnyChoice().IsMatch(word) : spec.Choices.Contains(word));
                    if (ok)
                    {
                        arg = Lit(name, spec.Type, NthUiValue.Of(word));
                        return true;
                    }
                    why = spec.Choices is null ? $"'{token.Raw}' is not a valid {name}" : $"{name} is one of {string.Join(", ", spec.Choices)}";
                    return false;
                }
            case NthUiArgType.Expr:
            case NthUiArgType.Values:
            case NthUiArgType.Options:
                arg = token.Kind is NthUiTokenKind.Text or NthUiTokenKind.Number || token.Literal.IsBool || token.Expr is null
                    ? Lit(name, spec.Type, token.Kind == NthUiTokenKind.Expr ? NthUiValue.Of(token.Raw) : token.Literal)
                    : Computed(name, spec.Type, token.Expr);
                return true;
            case NthUiArgType.Url:
                if (token.Kind == NthUiTokenKind.Text && IsAllowedUrl(kind, token.Raw))
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw));
                    return true;
                }
                why = kind == "image" ? "an image loads only from https, data:image or artifact: sources" : $"'{Clip(token.Raw)}' is not an address nth opens";
                return false;
            case NthUiArgType.Icon:
                if (token.Kind is NthUiTokenKind.Word or NthUiTokenKind.Text && IconName().IsMatch(token.Raw))
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw));
                    return true;
                }
                why = $"'{Clip(token.Raw)}' is not an icon name, so it was left out";
                return false;
            case NthUiArgType.Object:
                if (token.Kind == NthUiTokenKind.Expr && token.Expr?.Kind == NthUiExprKind.Object)
                {
                    arg = Computed(name, spec.Type, token.Expr);
                    return true;
                }
                why = $"{name} needs an object in braces";
                return false;
            case NthUiArgType.Tool:
                if (token.Kind == NthUiTokenKind.Word && ToolName().IsMatch(token.Raw))
                {
                    arg = Lit(name, spec.Type, NthUiValue.Of(token.Raw));
                    return true;
                }
                why = "call needs server.tool";
                return false;
        }
        why = $"{name} cannot be read";
        return false;
    }

    private static NthUiArg Cell(NthUiToken token) => token.Kind switch
    {
        NthUiTokenKind.Text or NthUiTokenKind.Number => Lit("cell", NthUiArgType.Expr, token.Literal),
        _ when token.Literal.IsBool => Lit("cell", NthUiArgType.Expr, token.Literal),
        _ => Computed("cell", NthUiArgType.Expr, token.Expr!),
    };

    private static NthUiArg FromExpr(string name, NthUiArgType type, NthUiExpr expr) =>
        expr.Kind == NthUiExprKind.Literal ? Lit(name, type, expr.Value) : Computed(name, type, expr);

    private static NthUiArg Lit(string name, NthUiArgType type, NthUiValue value) => new() { Name = name, Type = type, Literal = value };

    private static NthUiArg Computed(string name, NthUiArgType type, NthUiExpr expr) => new() { Name = name, Type = type, Expr = expr };

    private static string Describe(NthUiToken token) => token.Kind switch
    {
        NthUiTokenKind.Text => $"the text \"{Clip(token.Raw)}\"",
        NthUiTokenKind.Number => $"the number {token.Raw}",
        NthUiTokenKind.Word => $"the word '{Clip(token.Raw)}'",
        NthUiTokenKind.Expr => $"the value {Clip(token.Raw)}",
    };

    private static string Clip(string text) => text.Length > 40 ? text[..40] + "…" : text;

    /// <summary>An http or https address (what `open` hands to Visus through the link setting).</summary>
    public static bool IsWebUrl(string url) =>
        url.Length <= 4096 && !url.Any(char.IsWhiteSpace) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Length > 0;

    /// <summary>The sources a kind may name: images load https, data:image rasters and artifact assets (DESIGN 7.2 Images).</summary>
    public static bool IsAllowedUrl(string kind, string url)
    {
        if (kind != "image") return IsWebUrl(url);
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return RasterDataUri().IsMatch(url);
        if (url.StartsWith("artifact:", StringComparison.Ordinal)) return ArtifactAsset().IsMatch(url);
        return IsWebUrl(url) && url.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The structural rules every parsed tree keeps, for tests and fuzzing: each node is in the tree once with a unique
    /// key and a consistent parent; each node sits where its kind is allowed (a degraded line only where text may sit);
    /// in the lite profile only lite kinds remain; depth and node count stay inside their limits. Empty when all hold.
    /// </summary>
    public static IReadOnlyList<string> Problems(NthUiDocument document)
    {
        var problems = new List<string>();
        var seen = new HashSet<NthUiNode>(ReferenceEqualityComparer.Instance);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(NthUiNode Node, NthUiNode? Parent, int Depth)>();
        foreach (var root in document.Roots.Reverse()) stack.Push((root, null, 0));
        while (stack.Count > 0)
        {
            var (node, parent, depth) = stack.Pop();
            if (!seen.Add(node))
            {
                problems.Add($"{node} appears twice");
                continue;
            }
            if (!keys.Add(node.Key)) problems.Add($"key {node.Key} is used twice");
            if (document.Find(node.Key) != node) problems.Add($"{node} is not found by its key");
            if (node.Parent != parent) problems.Add($"{node} has the wrong parent");
            if (node.Depth != depth) problems.Add($"{node} has depth {node.Depth}, not {depth}");
            if (depth > NthUiParser.MaxDepth) problems.Add($"{node} is deeper than {NthUiParser.MaxDepth}");
            var children = parent?.Spec.Children ?? NthUiChildren.Root;
            if (parent?.Degraded == true) children = NthUiChildren.None;
            if (node.Degraded)
            {
                if (node.Kind != "text" || node.Text is not { Length: > 0 }) problems.Add($"{node} degraded without its text");
                if (!NthUiSchema.TakesText(children)) problems.Add($"{node} degraded inside {parent?.Kind}, which takes no text");
                if (node.Children.Count > 0) problems.Add($"{node} degraded but has children");
            }
            else
            {
                if (!NthUiSchema.Accepts(children, node.Spec)) problems.Add($"{node} cannot be inside {parent?.Kind ?? "the top level"}");
                if (document.Profile == NthUiProfile.Lite && !node.Spec.Lite) problems.Add($"{node} is not in the lite subset");
                if (node.Spec.Children is NthUiChildren.None or NthUiChildren.Raw && node.Children.Count > 0) problems.Add($"{node} is a leaf with children");
                foreach (var required in node.Spec.Positionals.Concat(node.Spec.Attributes))
                {
                    if (required.Required && !node.Args.ContainsKey(required.Name)) problems.Add($"{node} lacks {required.Name}");
                }
            }
            if (node.Body is not null && !node.TakesBody) problems.Add($"{node} has a body it does not take");
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push((node.Children[i], node, depth + 1));
        }
        if (seen.Count != document.Nodes.Count) problems.Add($"the tree holds {seen.Count} nodes, the stream {document.Nodes.Count}");
        if (document.Nodes.Count > NthUiParser.MaxNodes) problems.Add($"{document.Nodes.Count} nodes is over the limit");
        for (var i = 1; i < document.Nodes.Count; i++)
        {
            if (document.Nodes[i].Line <= document.Nodes[i - 1].Line) problems.Add($"{document.Nodes[i]} arrived out of order");
        }
        return problems;
    }
}
