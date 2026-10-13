using System.Globalization;
using System.Text;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 state, let, actions, editable charts; 7.4 "State changes recompute every
// let and re-render only nodes that read it"; ED-NEW-web-w15-1). No Swift counterpart. The small local state of one live
// view: states hold values the controls change, lets are computed from them on demand and cached until the next
// change, and every change answers with the keys of the nodes that read what changed, so the renderer redraws only
// those. Actions that leave the view (copy, open, export, send, context, call) come back as effects for the host to
// carry out with its normal approvals; the runtime itself does no I/O.

namespace Marrow.Core.NthUi;

public enum NthUiStateType
{
    Number,
    Text,
    Bool,
    List,

    /// <summary>A list of chosen option values (chips, a checklist).</summary>
    Selection,
}

public enum NthUiEffectKind
{
    /// <summary>Put <see cref="NthUiEffect.Text"/> on the clipboard.</summary>
    Copy,

    /// <summary>Open the http or https address in <see cref="NthUiEffect.Text"/> (in Visus, through the link setting).</summary>
    Open,

    /// <summary>Save a file: csv with the table in <see cref="NthUiEffect.Text"/>, or png of the node keyed by it.</summary>
    Export,

    /// <summary>Post <see cref="NthUiEffect.Text"/> as the user's next message.</summary>
    Send,

    /// <summary>Attach <see cref="NthUiEffect.Json"/> under the key <see cref="NthUiEffect.Text"/> to the next user message, silently.</summary>
    Context,

    /// <summary>Call the MCP tool <see cref="NthUiEffect.Server"/>.<see cref="NthUiEffect.Tool"/> with <see cref="NthUiEffect.Json"/>, with the normal approvals.</summary>
    Call,
}

public sealed record NthUiEffect(NthUiEffectKind Kind, string Text, string? Format = null, string? Server = null, string? Tool = null, string? Json = null);

public sealed record NthUiActionResult(IReadOnlyList<NthUiEffect> Effects, IReadOnlySet<string> Changed)
{
    public static NthUiActionResult None { get; } = new([], new HashSet<string>(StringComparer.Ordinal));
}

public sealed record NthUiTableRow(IReadOnlyList<string> Cells, bool Total, bool Muted, string? Key);

public sealed record NthUiTable(IReadOnlyList<string> Columns, IReadOnlyList<NthUiTableRow> Rows);

public sealed record NthUiSeries(string Name, IReadOnlyList<double?> Values, IReadOnlyList<double>? X = null);

public sealed record NthUiChartData(string Type, IReadOnlyList<string> Labels, IReadOnlyList<NthUiSeries> Series, double? Min, double? Max,
    double Step, bool Editable, string? Bind, string? Unit, string? Title, bool Stacked);

public sealed class NthUiRuntime
{
    /// <summary>The steps one pass (a change and the redraw after it) may take across all its evaluations.</summary>
    public const int PassBudget = 1_000_000;

    /// <summary>The longest text a text field keeps.</summary>
    public const int MaxFieldLength = 4_000;

    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>(StringComparer.Ordinal);

    private readonly Dictionary<string, Slot> states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NthUiValue> letCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> evaluating = new(StringComparer.Ordinal);
    private NthUiBudget budget = new(PassBudget);
    private int synced;
    private int letDepth;
    private Dictionary<string, List<string>>? dependents;
    private int dependentsFor = -1;

    public NthUiRuntime(NthUiDocument document, CultureInfo? culture = null)
    {
        Document = document;
        Culture = culture ?? CultureInfo.InvariantCulture;
        Sync();
    }

    public NthUiDocument Document { get; }

    public CultureInfo Culture { get; }

    private sealed class Slot
    {
        public required NthUiStateType Type { get; set; }

        public NthUiValue Value { get; set; }

        /// <summary>Declared by a `state` line (not made by a control's default).</summary>
        public bool Declared { get; set; }

        /// <summary>Changed by the user or a saved state, so a later declaration keeps the value.</summary>
        public bool Changed { get; set; }
    }

    // ---- reading --------------------------------------------------------------------------------------------------

    /// <summary>Starts a new pass with a fresh step allowance (a redraw that is not after a change).</summary>
    public void BeginPass()
    {
        budget = new NthUiBudget(PassBudget);
    }

    /// <summary>The current value of a state or let; an error value for a name nothing defines.</summary>
    public NthUiValue Get(string name)
    {
        if (states.TryGetValue(name, out var slot)) return slot.Value;
        if (letCache.TryGetValue(name, out var cached)) return cached;
        if (Document.Declarations.TryGetValue(name, out var declaration) && declaration.Kind == "let")
        {
            if (letDepth >= NthUiEvaluator.MaxNameNesting) return NthUiValue.Error("lets build on each other too deeply");
            if (!evaluating.Add(name)) return NthUiValue.Error($"{name} depends on itself");
            letDepth++;
            try
            {
                var value = Evaluate(declaration.Arg("value"));
                if (!budget.Exhausted) letCache[name] = value;
                return value;
            }
            finally
            {
                letDepth--;
                evaluating.Remove(name);
            }
        }
        return NthUiValue.Error($"'{name}' is not defined");
    }

    public bool IsState(string name) => states.ContainsKey(name);

    public NthUiStateType? TypeOf(string name) => states.TryGetValue(name, out var slot) ? slot.Type : null;

    /// <summary>Every state's current value (internal ones, named after a node's key with a leading #, included).</summary>
    public IReadOnlyDictionary<string, NthUiValue> States => states.ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.Ordinal);

    public NthUiValue Evaluate(NthUiExpr expr) => EvaluateShared(expr);

    /// <summary>An argument's value: its literal, or its expression evaluated now. Null arguments are null values.</summary>
    public NthUiValue Evaluate(NthUiArg? arg) => arg is null ? NthUiValue.Null : arg.IsLiteral ? arg.Literal : EvaluateShared(arg.Expr!);

    public NthUiValue Value(NthUiNode node, string arg) => Evaluate(node.Arg(arg));

    /// <summary>The node's main text (its text, title or label), evaluated and ready to show.</summary>
    public string Text(NthUiNode node)
    {
        foreach (var spec in node.Spec.Positionals)
        {
            if (spec.Type != NthUiArgType.Text) continue;
            var arg = node.Arg(spec.Name);
            if (arg is not null) return Evaluate(arg).Display(Culture);
        }
        return "";
    }

    /// <summary>A node's value argument (`stat`, `copy`, a `set`'s new value) as text.</summary>
    public string Display(NthUiNode node, string arg) => Value(node, arg).Display(Culture);

    /// <summary>Whether `when=` lets the node show (an error hides it until the names it needs arrive).</summary>
    public bool IsVisible(NthUiNode node)
    {
        var when = node.Arg("when");
        return when is null || Evaluate(when).IsTruthy;
    }

    private NthUiValue EvaluateShared(NthUiExpr expr)
    {
        if (budget.Exhausted) return NthUiValue.Error("the view takes too much work");
        var nested = new NthUiEvaluator(Get, Culture, budget);
        return nested.Evaluate(expr);
    }

    // ---- structure readers for the renderer ----------------------------------------------------------------------

    /// <summary>A table row's cells, evaluated.</summary>
    public IReadOnlyList<NthUiValue> Cells(NthUiNode row) => row.Cells.Select(Evaluate).ToArray();

    /// <summary>The values of a Values argument: a comma-separated text, a number, or an expression that makes a list.</summary>
    public IReadOnlyList<NthUiValue> Values(NthUiNode node, string arg)
    {
        var a = node.Arg(arg);
        if (a is null) return [];
        if (a.IsLiteral && a.Literal.IsText) return SplitList(a.Literal.AsText).Select(ParseItem).ToArray();
        var value = Evaluate(a);
        return value.IsList ? value.Items : value.IsNull || value.IsError ? [] : [value];
    }

    /// <summary>The options of a segmented control or select: "value:Label" pairs, or a list (texts, pairs, or objects).</summary>
    public IReadOnlyList<(string Value, string Label)> Options(NthUiNode node)
    {
        var a = node.Arg("options");
        if (a is null) return [];
        if (a.IsLiteral && a.Literal.IsText)
        {
            return SplitList(a.Literal.AsText).Select(item =>
            {
                var colon = IndexOfUnescaped(item, ':');
                return colon < 0 ? (Unescape(item), Unescape(item)) : (Unescape(item[..colon]).Trim(), Unescape(item[(colon + 1)..]).Trim());
            }).Where(o => o.Item1.Length > 0).ToArray();
        }
        var value = Evaluate(a);
        if (!value.IsList) return [];
        return value.Items.Select(item =>
        {
            if (item.IsList && item.Items.Count >= 2) return (item.Items[0].Display(Culture), item.Items[1].Display(Culture));
            if (item.IsObject)
            {
                var v = item.TryGetField("value", out var fv) ? fv.Display(Culture) : "";
                var l = item.TryGetField("label", out var fl) ? fl.Display(Culture) : v;
                return (v, l);
            }
            var text = item.Display(Culture);
            return (text, text);
        }).Where(o => o.Item1.Length > 0).ToArray();
    }

    /// <summary>A table's columns and rows (the `rows=` data first, then the row lines), as shown text.</summary>
    public NthUiTable Table(NthUiNode table)
    {
        var columns = Values(table, "cols").Select(v => v.Display(Culture)).ToArray();
        var rows = new List<NthUiTableRow>();
        var data = Value(table, "rows");
        if (data.IsList)
        {
            foreach (var item in data.Items)
            {
                IReadOnlyList<string> cells = item.Kind switch
                {
                    NthUiValueKind.List => item.Items.Select(v => v.Display(Culture)).ToArray(),
                    NthUiValueKind.Object => columns.Length > 0
                        ? columns.Select(c => item.TryGetField(c, out var f) ? f.Display(Culture) : "").ToArray()
                        : item.Fields.Select(f => f.Value.Display(Culture)).ToArray(),
                    _ => [item.Display(Culture)],
                };
                rows.Add(new NthUiTableRow(cells, false, false, null));
            }
        }
        foreach (var child in table.Children)
        {
            if (child.Kind != "row" || child.Degraded || !IsVisible(child)) continue;
            rows.Add(new NthUiTableRow(Cells(child).Select(v => v.Display(Culture)).ToArray(), child.Has("total"), child.Has("muted"), child.Key));
        }
        return new NthUiTable(columns, rows);
    }

    /// <summary>The table as tab-separated lines with its header, for "Copy as table" (pastes into spreadsheets).</summary>
    public string TableText(NthUiNode table)
    {
        var data = Table(table);
        static string Cell(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        var lines = new List<string>();
        if (data.Columns.Count > 0) lines.Add(string.Join('\t', data.Columns.Select(Cell)));
        lines.AddRange(data.Rows.Select(r => string.Join('\t', r.Cells.Select(Cell))));
        return string.Join('\n', lines);
    }

    /// <summary>The table as CSV (RFC 4180 quoting), for `export csv`.</summary>
    public string TableCsv(NthUiNode table)
    {
        var data = Table(table);
        static string Cell(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        var lines = new List<string>();
        if (data.Columns.Count > 0) lines.Add(string.Join(',', data.Columns.Select(Cell)));
        lines.AddRange(data.Rows.Select(r => string.Join(',', r.Cells.Select(Cell))));
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>A chart's data: labels, series (the bound state's list for an editable chart), range and axis step.</summary>
    public NthUiChartData Chart(NthUiNode chart)
    {
        var type = chart.Arg("type")?.Literal.AsText is { Length: > 0 } t ? t : "bar";
        var bind = chart.Arg("bind")?.Literal.AsText;
        var names = Values(chart, "series").Select(v => v.Display(Culture)).ToArray();
        var series = new List<NthUiSeries>();
        var x = Values(chart, "x");
        // A scatter plots y against x: numbers in `x` are the points' positions (labels would only name them), shared by
        // every series the chart's own `y` makes (W16b, ED-NEW-web-w16b-1).
        var positions = type == "scatter" && x.Count > 0 && x.All(v => v.IsNumber) ? x.Select(v => v.AsNumber).ToArray() : null;
        if (!string.IsNullOrEmpty(bind))
        {
            series.Add(new NthUiSeries(names.FirstOrDefault() ?? "", Numbers(Get(bind)), positions));
        }
        else if (chart.Arg("y") is not null)
        {
            var y = Values(chart, "y");
            if (y.Count > 0 && y.All(v => v.IsList))
            {
                for (var i = 0; i < y.Count; i++) series.Add(new NthUiSeries(i < names.Length ? names[i] : "", Numbers(y[i]), positions));
            }
            else series.Add(new NthUiSeries(names.FirstOrDefault() ?? "", y.Select(v => v.IsNumber ? v.AsNumber : (double?)null).ToArray(), positions));
        }
        foreach (var child in chart.Children)
        {
            if (child.Kind != "series" || child.Degraded || !IsVisible(child)) continue;
            var xs = child.Arg("x") is null ? null : Numbers(Value(child, "x")).Select(v => v ?? 0).ToArray();
            series.Add(new NthUiSeries(Text(child), Numbers(Value(child, "y")), xs));
        }
        var count = series.Count == 0 ? 0 : series.Max(s => s.Values.Count);
        var labels = x.Count > 0 && type != "scatter"
            ? x.Select(v => v.Display(Culture)).ToArray()
            : Enumerable.Range(1, count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        double? min = Value(chart, "min") is { IsNumber: true } mn ? mn.AsNumber : null;
        double? max = Value(chart, "max") is { IsNumber: true } mx ? mx.AsNumber : null;
        var editable = chart.Arg("editable") is not null && !string.IsNullOrEmpty(bind);
        return new NthUiChartData(type, labels, series, min, max, AxisStep(chart, series, min, max), editable, bind,
            chart.Arg("unit") is null ? null : Display(chart, "unit"), chart.Arg("title") is null ? null : Display(chart, "title"), chart.Has("stacked"));
    }

    private IReadOnlyList<double?> Numbers(NthUiValue value) =>
        value.IsList ? value.Items.Select(v => v.IsNumber ? v.AsNumber : (double?)null).ToArray() : [];

    private double AxisStep(NthUiNode chart, IReadOnlyList<NthUiSeries> series, double? min, double? max)
    {
        var step = Value(chart, "step");
        if (step.IsNumber && step.AsNumber > 0) return step.AsNumber;
        var values = series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        var lo = min ?? Math.Min(0, values.Length == 0 ? 0 : values.Min());
        var hi = max ?? (values.Length == 0 ? 10 : values.Max());
        return NiceStep((hi - lo) / 10);
    }

    /// <summary>The nearest of 1, 2 and 5 times a power of ten (0.1, 0.2, 0.5, 1, 2, 5, 10 ...); 1 for nothing to go on.</summary>
    public static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0) return 1;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
        return nice * power;
    }

    /// <summary>Whether a filter chip is chosen (in its chips' bound state, or the chips' own selection).</summary>
    public bool IsSelected(NthUiNode chip)
    {
        if (chip.Parent is not { Kind: "chips" } chips) return false;
        var value = Get(SelectionName(chips));
        var key = OptionKey(chip);
        return value.IsList ? value.Items.Any(v => v.IsText && v.AsText == key) : value.IsText && value.AsText == key;
    }

    /// <summary>Whether a checklist item is ticked.</summary>
    public bool IsChecked(NthUiNode item)
    {
        if (item.Parent is not { Kind: "checklist" } list) return item.Has("done");
        var value = Get(SelectionName(list));
        var key = OptionKey(item);
        return value.IsList && value.Items.Any(v => v.IsText && v.AsText == key);
    }

    /// <summary>The tab a tabs node shows (its bound state, or the first tab).</summary>
    public NthUiNode? SelectedTab(NthUiNode tabs)
    {
        var tabList = tabs.Children.Where(c => c.Kind == "tab" && !c.Degraded).ToArray();
        var value = Get(SelectionName(tabs));
        return tabList.FirstOrDefault(t => value.IsText && OptionKey(t) == value.AsText) ?? tabList.FirstOrDefault();
    }

    /// <summary>The 0-based step a steps node shows.</summary>
    public int CurrentStep(NthUiNode steps)
    {
        var count = steps.Children.Count(c => c.Kind == "step" && !c.Degraded);
        var value = Get(SelectionName(steps));
        return count == 0 ? 0 : value.IsNumber ? (int)Math.Clamp(Math.Floor(value.AsNumber), 0, count - 1) : 0;
    }

    private static string OptionKey(NthUiNode node) =>
        node.Arg("value") is { IsLiteral: true } v ? v.Literal.Display() : node.Text ?? node.Key;

    private static string SelectionName(NthUiNode owner) =>
        owner.Arg("bind")?.Literal.AsText is { Length: > 0 } bind ? bind : "#" + owner.Key;

    // ---- syncing new lines ---------------------------------------------------------------------------------------

    /// <summary>Takes in the nodes parsed since the last call: declares states, gives controls their default states, and
    /// returns the keys of nodes that read a name that only now became defined.</summary>
    public IReadOnlySet<string> Sync()
    {
        var fresh = new HashSet<string>(StringComparer.Ordinal);
        BeginPass();
        for (; synced < Document.Nodes.Count; synced++)
        {
            var node = Document.Nodes[synced];
            if (node.Degraded) continue;
            switch (node.Kind)
            {
                case "state":
                    Declare(node);
                    fresh.Add(node.Name!);
                    break;
                case "let":
                    letCache.Clear();
                    fresh.Add(node.Name!);
                    break;
                case "chip" when node.Parent is { Kind: "chips" } chips && node.Has("selected"):
                    AddDefaultOption(chips, OptionKey(node), chips.Has("single"));
                    break;
                case "item" when node.Parent is { Kind: "checklist" } list && node.Has("done"):
                    AddDefaultOption(list, OptionKey(node), false);
                    break;
                case "tab" when node.Parent is { Kind: "tabs" } tabs:
                    if (Get(SelectionName(tabs)) is { IsText: true, AsText.Length: 0 }) DefaultTo(SelectionName(tabs), NthUiValue.Of(OptionKey(node)));
                    break;
                default:
                    if (node.Role == NthUiRole.Input || node.Kind is "chips" or "checklist" or "tabs" or "steps" || (node.Kind == "chart" && node.Arg("bind") is not null))
                    {
                        var name = node.Kind is "chips" or "checklist" or "tabs" or "steps" ? SelectionName(node) : node.Name!;
                        if (!states.ContainsKey(name))
                        {
                            states[name] = DefaultSlot(node);
                            fresh.Add(name);
                        }
                    }
                    break;
            }
        }
        if (fresh.Count == 0) return NoKeys;
        letCache.Clear();
        return Affected(fresh);
    }

    private void Declare(NthUiNode node)
    {
        var name = node.Name!;
        var initial = Value(node, "value");
        var typeWord = node.Arg("type")?.Literal.AsText;
        var type = typeWord switch
        {
            "number" => NthUiStateType.Number,
            "text" or "string" => NthUiStateType.Text,
            "bool" => NthUiStateType.Bool,
            "list" => NthUiStateType.List,
            "selection" => NthUiStateType.Selection,
            _ => Infer(initial),
        };
        var value = Coerce(initial, type) ?? Empty(type);
        if (states.TryGetValue(name, out var slot))
        {
            if (!slot.Changed)
            {
                slot.Type = type;
                slot.Value = value;
            }
            else
            {
                slot.Value = Coerce(slot.Value, type) ?? value;
                slot.Type = type;
            }
            slot.Declared = true;
            return;
        }
        states[name] = new Slot { Type = type, Value = value, Declared = true };
    }

    private Slot DefaultSlot(NthUiNode node)
    {
        switch (node.Kind)
        {
            case "stepper" or "slider":
                {
                    var min = Value(node, "min");
                    var max = Value(node, "max");
                    var start = min.IsNumber ? min.AsNumber : max.IsNumber && max.AsNumber < 0 ? max.AsNumber : 0;
                    return new Slot { Type = NthUiStateType.Number, Value = NthUiValue.Of(start) };
                }
            case "toggle":
                return new Slot { Type = NthUiStateType.Bool, Value = NthUiValue.False };
            case "segmented" or "select":
                {
                    var first = Options(node).FirstOrDefault().Value ?? "";
                    return new Slot { Type = NthUiStateType.Text, Value = NthUiValue.Of(first) };
                }
            case "chips" when node.Has("single"):
            case "tabs":
                return new Slot { Type = NthUiStateType.Text, Value = NthUiValue.Of("") };
            case "chips" or "checklist":
                return new Slot { Type = NthUiStateType.Selection, Value = NthUiValue.List([]) };
            case "steps":
                return new Slot { Type = NthUiStateType.Number, Value = NthUiValue.Of(0) };
            case "chart":
                {
                    var y = Values(node, "y").Where(v => v.IsNumber).ToArray();
                    return new Slot { Type = NthUiStateType.List, Value = NthUiValue.List(y) };
                }
            default:
                return new Slot { Type = NthUiStateType.Text, Value = NthUiValue.Of("") };
        }
    }

    private void AddDefaultOption(NthUiNode owner, string option, bool single)
    {
        var name = SelectionName(owner);
        if (!states.TryGetValue(name, out var slot) || slot.Changed || slot.Declared) return;
        if (single || slot.Type == NthUiStateType.Text) slot.Value = NthUiValue.Of(option);
        else if (!slot.Value.Items.Any(v => v.AsText == option)) slot.Value = NthUiValue.List(slot.Value.Items.Append(NthUiValue.Of(option)).ToArray());
    }

    private void DefaultTo(string name, NthUiValue value)
    {
        if (states.TryGetValue(name, out var slot) && !slot.Changed && !slot.Declared) slot.Value = value;
    }

    private static NthUiStateType Infer(NthUiValue value) => value.Kind switch
    {
        NthUiValueKind.Number => NthUiStateType.Number,
        NthUiValueKind.Bool => NthUiStateType.Bool,
        NthUiValueKind.List => NthUiStateType.List,
        _ => NthUiStateType.Text,
    };

    private static NthUiValue Empty(NthUiStateType type) => type switch
    {
        NthUiStateType.Number => NthUiValue.Of(0),
        NthUiStateType.Text => NthUiValue.Of(""),
        NthUiStateType.Bool => NthUiValue.False,
        NthUiStateType.List or NthUiStateType.Selection => NthUiValue.List([]),
    };

    /// <summary>The value as the state type keeps it, or null when it cannot be.</summary>
    private NthUiValue? Coerce(NthUiValue value, NthUiStateType type)
    {
        if (value.IsError) return null;
        switch (type)
        {
            case NthUiStateType.Number:
                if (value.IsNumber) return value;
                if (value.IsBool) return NthUiValue.Of(value.AsNumber);
                if (value.IsText && double.TryParse(value.AsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)) return NthUiValue.Of(n);
                return null;
            case NthUiStateType.Text:
                return value.IsText ? value : value.IsNull ? NthUiValue.Of("") : NthUiValue.Of(value.Display(Culture));
            case NthUiStateType.Bool:
                return NthUiValue.Of(value.IsTruthy);
            case NthUiStateType.List:
                return value.IsList ? value : null;
            case NthUiStateType.Selection:
                if (value.IsList) return NthUiValue.List(value.Items.Select(v => v.IsText ? v : NthUiValue.Of(v.Display(Culture))).Distinct().ToArray());
                if (value.IsText) return NthUiValue.List(value.AsText.Length == 0 ? [] : [value]);
                return null;
        }
        return null;
    }

    // ---- changing state ------------------------------------------------------------------------------------------

    /// <summary>Sets a state (making it if nothing declared it) and returns the keys of the nodes to redraw. A let, an
    /// error value or a value the state's type cannot hold changes nothing.</summary>
    public IReadOnlySet<string> Set(string name, NthUiValue value)
    {
        if (value.IsError || name.Length == 0) return NoKeys;
        if (name[0] != '#' && (!NthUiExpr.IsName(name) || NthUiExpr.IsReserved(name))) return NoKeys;
        if (Document.Declarations.TryGetValue(name, out var declaration) && declaration.Kind == "let") return NoKeys;
        if (!states.TryGetValue(name, out var slot))
        {
            slot = new Slot { Type = Infer(value), Value = NthUiValue.Null };
            states[name] = slot;
        }
        var coerced = Coerce(value, slot.Type);
        if (coerced is null) return NoKeys;
        slot.Changed = true;
        if (slot.Value.Equals(coerced.Value)) return NoKeys;
        slot.Value = coerced.Value;
        letCache.Clear();
        BeginPass();
        return Affected([name]);
    }

    public IReadOnlySet<string> Toggle(string name) => Set(name, NthUiValue.Of(!Get(name).IsTruthy));

    /// <summary>Adds or removes <paramref name="option"/> in a selection; a single choice sets it (or clears it when chosen).</summary>
    public IReadOnlySet<string> ToggleOption(string name, string option, bool single = false)
    {
        var current = Get(name);
        if (single || (states.TryGetValue(name, out var slot) && slot.Type == NthUiStateType.Text))
            return Set(name, NthUiValue.Of(current.IsText && current.AsText == option ? "" : option));
        var items = current.IsList ? current.Items.ToList() : [];
        var at = items.FindIndex(v => v.IsText && v.AsText == option);
        if (at >= 0) items.RemoveAt(at);
        else items.Add(NthUiValue.Of(option));
        return Set(name, NthUiValue.List(items.ToArray()));
    }

    /// <summary>Sets a control's state the way the control allows: steppers and sliders clamp to min and max and snap
    /// to their step, choices must be one of the options, dates must be yyyy-MM-dd inside min and max.</summary>
    public IReadOnlySet<string> SetInput(NthUiNode input, NthUiValue value)
    {
        var name = input.Name;
        if (input.Role != NthUiRole.Input || string.IsNullOrEmpty(name)) return NoKeys;
        switch (input.Kind)
        {
            case "stepper" or "slider":
                {
                    if (!value.IsNumber) return NoKeys;
                    return Set(name, NthUiValue.Of(Snap(input, value.AsNumber)));
                }
            case "toggle":
                return Set(name, NthUiValue.Of(value.IsTruthy));
            case "textfield":
                {
                    var text = value.Display(Culture);
                    return Set(name, NthUiValue.Of(text.Length > MaxFieldLength ? text[..MaxFieldLength] : text));
                }
            case "segmented" or "select":
                {
                    var choice = value.Display(Culture);
                    return Options(input).Any(o => o.Value == choice) ? Set(name, NthUiValue.Of(choice)) : NoKeys;
                }
            case "date":
                {
                    var text = value.Display(Culture).Trim();
                    if (text.Length == 0) return Set(name, NthUiValue.Of(""));
                    if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return NoKeys;
                    var min = Display(input, "min");
                    var max = Display(input, "max");
                    if (min.Length == 10 && string.CompareOrdinal(text, min) < 0) text = min;
                    if (max.Length == 10 && string.CompareOrdinal(text, max) > 0) text = max;
                    return Set(name, NthUiValue.Of(text));
                }
        }
        return NoKeys;
    }

    /// <summary>A stepper's minus and plus, or a slider's arrow keys: one step down (-1) or up (+1).</summary>
    public IReadOnlySet<string> StepInput(NthUiNode input, int direction)
    {
        if (input.Kind is not ("stepper" or "slider") || string.IsNullOrEmpty(input.Name)) return NoKeys;
        var current = Get(input.Name);
        if (!current.IsNumber) return NoKeys;
        return SetInput(input, NthUiValue.Of(current.AsNumber + Math.Sign(direction) * InputStep(input)));
    }

    /// <summary>A stepper's or slider's step: its step=, else 1 for a stepper and a hundredth of the range for a slider.</summary>
    public double InputStep(NthUiNode input)
    {
        var step = Value(input, "step");
        if (step.IsNumber && step.AsNumber > 0) return step.AsNumber;
        if (input.Kind == "slider")
        {
            var min = Value(input, "min");
            var max = Value(input, "max");
            if (min.IsNumber && max.IsNumber && max.AsNumber > min.AsNumber) return NiceStep((max.AsNumber - min.AsNumber) / 100);
        }
        return 1;
    }

    private double Snap(NthUiNode input, double value)
    {
        var min = Value(input, "min");
        var max = Value(input, "max");
        var step = Value(input, "step");
        var lo = min.IsNumber ? min.AsNumber : double.NegativeInfinity;
        var hi = max.IsNumber ? max.AsNumber : double.PositiveInfinity;
        if (step.IsNumber && step.AsNumber > 0)
        {
            var origin = double.IsFinite(lo) ? lo : 0;
            value = origin + Math.Round((value - origin) / step.AsNumber, MidpointRounding.AwayFromZero) * step.AsNumber;
        }
        value = Math.Round(value, 10);
        if (hi < lo) hi = lo;
        return Math.Clamp(value, lo, hi);
    }

    /// <summary>Drags point <paramref name="index"/> of an editable chart to <paramref name="value"/>: the bound list
    /// changes, every let recomputes, and the nodes that read them redraw (the "editable graph", no model call).</summary>
    public IReadOnlySet<string> SetPoint(NthUiNode chart, int index, double value)
    {
        var data = Chart(chart);
        if (!data.Editable || data.Bind is null || !double.IsFinite(value)) return NoKeys;
        var list = Get(data.Bind);
        if (!list.IsList || index < 0 || index >= list.Items.Count || !list.Items[index].IsNumber) return NoKeys;
        value = Math.Round(Math.Round(value / data.Step, MidpointRounding.AwayFromZero) * data.Step, 10);
        if (data.Min is double lo) value = Math.Max(lo, value);
        if (data.Max is double hi) value = Math.Min(hi, value);
        var items = list.Items.ToArray();
        items[index] = NthUiValue.Of(value);
        return Set(data.Bind, NthUiValue.List(items));
    }

    /// <summary>Up and Down on a focused point: one axis step.</summary>
    public IReadOnlySet<string> NudgePoint(NthUiNode chart, int index, int direction)
    {
        var data = Chart(chart);
        if (!data.Editable || data.Bind is null) return NoKeys;
        var list = Get(data.Bind);
        if (!list.IsList || index < 0 || index >= list.Items.Count || !list.Items[index].IsNumber) return NoKeys;
        return SetPoint(chart, index, list.Items[index].AsNumber + Math.Sign(direction) * data.Step);
    }

    /// <summary>The keys of the nodes that read any of <paramref name="names"/>, directly or through lets.</summary>
    public IReadOnlySet<string> Affected(IEnumerable<string> names)
    {
        var changed = new HashSet<string>(names, StringComparer.Ordinal);
        var queue = new Queue<string>(changed);
        var dependents = Dependents();
        while (queue.Count > 0)
        {
            if (!dependents.TryGetValue(queue.Dequeue(), out var lets)) continue;
            foreach (var let in lets)
            {
                if (changed.Add(let)) queue.Enqueue(let);
            }
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Document.Nodes)
        {
            if (node.Role == NthUiRole.Declaration) continue;
            if (ReadsAny(node, changed)) keys.Add(node.Key);
            else if (node.Kind is "chips" or "checklist" or "tabs" or "steps" && changed.Contains(SelectionName(node))) keys.Add(node.Key);
        }
        return keys;
    }

    private static bool ReadsAny(NthUiNode node, HashSet<string> names)
    {
        foreach (var read in node.Reads)
        {
            if (names.Contains(read)) return true;
        }
        return false;
    }

    /// <summary>For each name, the lets that read it; rebuilt when declarations arrive.</summary>
    private Dictionary<string, List<string>> Dependents()
    {
        if (dependents is not null && dependentsFor == Document.Declarations.Count) return dependents;
        dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var let in Document.Declarations.Values)
        {
            if (let.Kind != "let") continue;
            foreach (var read in let.Reads)
            {
                if (!dependents.TryGetValue(read, out var list)) dependents[read] = list = [];
                list.Add(let.Name!);
            }
        }
        dependentsFor = Document.Declarations.Count;
        return dependents;
    }

    // ---- actions -------------------------------------------------------------------------------------------------

    /// <summary>Runs what pressing <paramref name="node"/> does: an action, a button's actions in order, a follow-up or
    /// filter chip, a checklist item, a tab, or a form's submit. Hidden nodes do nothing.</summary>
    public NthUiActionResult Run(NthUiNode node)
    {
        if (node.Degraded || !IsVisible(node)) return NthUiActionResult.None;
        var effects = new List<NthUiEffect>();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        RunInto(node, effects, changed);
        return new NthUiActionResult(effects, changed);
    }

    private void RunInto(NthUiNode node, List<NthUiEffect> effects, HashSet<string> changed)
    {
        switch (node.Kind)
        {
            case "button":
                foreach (var step in node.Children)
                {
                    if (!step.Degraded && step.Role == NthUiRole.Action && IsVisible(step)) RunInto(step, effects, changed);
                }
                break;
            case "set":
                changed.UnionWith(Set(node.Name!, Value(node, "value")));
                break;
            case "toggle" when node.Role == NthUiRole.Action:
                changed.UnionWith(Toggle(node.Name!));
                break;
            case "toggle":
                changed.UnionWith(SetInput(node, NthUiValue.Of(!Get(node.Name!).IsTruthy)));
                break;
            case "copy":
                effects.Add(new NthUiEffect(NthUiEffectKind.Copy, CopyText(node)));
                break;
            case "open":
                effects.Add(new NthUiEffect(NthUiEffectKind.Open, node.Arg("url")!.Literal.AsText));
                break;
            case "export":
                {
                    var format = node.Arg("format")!.Literal.AsText;
                    var table = NearestTable(node);
                    var content = format == "csv" ? table is null ? "" : TableCsv(table) : (NearestCard(node)?.Key ?? "");
                    effects.Add(new NthUiEffect(NthUiEffectKind.Export, content, format));
                    break;
                }
            case "send":
                {
                    var text = node.Arg("message") is not null ? Display(node, "message") : Text(node);
                    if (text.Trim().Length > 0) effects.Add(new NthUiEffect(NthUiEffectKind.Send, text));
                    break;
                }
            case "context":
                {
                    var key = node.Arg("key") is null ? "context" : Text(node);
                    effects.Add(new NthUiEffect(NthUiEffectKind.Context, key, Json: Value(node, "value").ToJson()));
                    break;
                }
            case "call":
                {
                    var tool = node.Arg("tool")!.Literal.AsText;
                    var dot = tool.IndexOf('.', StringComparison.Ordinal);
                    var args = node.Arg("args") is null ? "{}" : Value(node, "args").ToJson();
                    effects.Add(new NthUiEffect(NthUiEffectKind.Call, Text(node), Server: tool[..dot], Tool: tool[(dot + 1)..], Json: args));
                    break;
                }
            case "chip" when node.Parent?.Kind == "followups":
                effects.Add(new NthUiEffect(NthUiEffectKind.Send, Text(node)));
                break;
            case "chip" when node.Parent is { Kind: "chips" } chips:
                changed.UnionWith(ToggleOption(SelectionName(chips), OptionKey(node), chips.Has("single")));
                break;
            case "item" when node.Parent is { Kind: "checklist" } list:
                changed.UnionWith(ToggleOption(SelectionName(list), OptionKey(node)));
                break;
            case "tab" when node.Parent is { Kind: "tabs" } tabs:
                changed.UnionWith(Set(SelectionName(tabs), NthUiValue.Of(OptionKey(node))));
                break;
            case "step" when node.Parent is { Kind: "steps" } steps:
                changed.UnionWith(Set(SelectionName(steps), NthUiValue.Of(steps.Children.Where(c => c.Kind == "step" && !c.Degraded).ToList().IndexOf(node))));
                break;
            case "form":
                {
                    var text = node.Arg("message") is not null ? Display(node, "message") : FormText(node);
                    if (text.Trim().Length > 0) effects.Add(new NthUiEffect(NthUiEffectKind.Send, text));
                    break;
                }
        }
    }

    /// <summary>Moves a steps node to the previous (-1) or next (+1) step.</summary>
    public IReadOnlySet<string> MoveStep(NthUiNode steps, int direction)
    {
        if (steps.Kind != "steps") return NoKeys;
        var count = steps.Children.Count(c => c.Kind == "step" && !c.Degraded);
        if (count == 0) return NoKeys;
        return Set(SelectionName(steps), NthUiValue.Of(Math.Clamp(CurrentStep(steps) + Math.Sign(direction), 0, count - 1)));
    }

    private string CopyText(NthUiNode node)
    {
        var value = node.Arg("value");
        if (value is { IsLiteral: true, Literal.IsText: true } && value.Literal.AsText is "table" or "view")
        {
            if (value.Literal.AsText == "table" && NearestTable(node) is { } table) return TableText(table);
            return ViewText();
        }
        if (value is not null)
        {
            var computed = Evaluate(value);
            if (computed.IsText && computed.AsText == "table" && NearestTable(node) is { } t) return TableText(t);
            return computed.Display(Culture);
        }
        return Text(node);
    }

    private NthUiNode? NearestTable(NthUiNode from)
    {
        for (var scope = from.Parent; ; scope = scope.Parent)
        {
            var found = FirstOf(scope is null ? Document.Roots : scope.Children, "table");
            if (found is not null || scope is null) return found;
        }
    }

    private static NthUiNode? NearestCard(NthUiNode from)
    {
        var top = from;
        for (var scope = from.Parent; scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == "card") return scope;
            top = scope;
        }
        return top;
    }

    private static NthUiNode? FirstOf(IReadOnlyList<NthUiNode> nodes, string kind)
    {
        var stack = new Stack<NthUiNode>(nodes.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Kind == kind && !node.Degraded) return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return null;
    }

    private string FormText(NthUiNode form)
    {
        var lines = new List<string>();
        var title = Text(form);
        if (title.Length > 0) lines.Add(title);
        var stack = new Stack<NthUiNode>(form.Children.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Degraded || !IsVisible(node)) continue;
            if (node.Role == NthUiRole.Input && node.Name is { Length: > 0 } name)
            {
                var label = node.Arg("label") is null ? name : Display(node, "label");
                var value = Get(name);
                var shown = node.Kind is "segmented" or "select" ? Options(node).FirstOrDefault(o => o.Value == value.Display(Culture)).Label ?? value.Display(Culture) : value.Display(Culture);
                lines.Add($"{label}: {shown}");
            }
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return string.Join('\n', lines);
    }

    /// <summary>The view as plain text, one visible node a line (for `copy value=view` and summaries).</summary>
    public string ViewText()
    {
        var lines = new List<string>();
        var stack = new Stack<NthUiNode>(Document.Roots.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Role == NthUiRole.Declaration || !IsVisible(node)) continue;
            switch (node.Kind)
            {
                case "table":
                    lines.Add(TableText(node));
                    continue;
                case "stat":
                    lines.Add($"{Text(node)}: {Display(node, "value")}");
                    break;
                case "stepper" or "slider" or "segmented" or "select" or "toggle" or "textfield" or "date":
                    lines.Add($"{(node.Arg("label") is null ? node.Name : Display(node, "label"))}: {Get(node.Name!).Display(Culture)}");
                    break;
                case "followups" or "actions" or "chart" or "image" or "svg" or "diagram" or "map" or "divider":
                    continue;
                default:
                    {
                        var text = node.Kind == "code" ? node.Body ?? Text(node) : Text(node);
                        if (text.Length > 0) lines.Add(text);
                        break;
                    }
            }
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return string.Join('\n', lines);
    }

    // ---- saving --------------------------------------------------------------------------------------------------

    /// <summary>The states as a JSON object, to keep the view as the user left it.</summary>
    public string SaveState() =>
        NthUiValue.Object(states.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, NthUiValue>(p.Key, p.Value.Value))).ToJson();

    /// <summary>Restores saved states (also before their declarations arrive; a declaration then keeps the saved value).</summary>
    public IReadOnlySet<string> LoadState(string json)
    {
        var saved = NthUiValue.FromJson(json);
        if (!saved.IsObject) return NoKeys;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in saved.Fields) keys.UnionWith(Set(name, value));
        return keys;
    }

    // ---- lists in text -------------------------------------------------------------------------------------------

    /// <summary>Splits "a,b,c" on commas not escaped with a backslash; at most <see cref="NthUiValue.MaxListLength"/> items.</summary>
    public static IReadOnlyList<string> SplitList(string text)
    {
        var items = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < text.Length && items.Count < NthUiValue.MaxListLength; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                current.Append(c).Append(text[++i]);
                continue;
            }
            if (c == ',')
            {
                items.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0 || text.EndsWith(',')) items.Add(current.ToString().Trim());
        return items.Where(s => s.Length > 0).ToArray();
    }

    private static NthUiValue ParseItem(string item)
    {
        var text = Unescape(item);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? NthUiValue.Of(n) : NthUiValue.Of(text);
    }

    private static int IndexOfUnescaped(string text, char c)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == c) return i;
        }
        return -1;
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal)) return text;
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) i++;
            builder.Append(text[i]);
        }
        return builder.ToString();
    }
}
