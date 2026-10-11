// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 components v1, 7.3 lite subset; ED-NEW-web-w15-1). No Swift counterpart.
// The catalogue of nth-ui: every component and keyword, the values it takes in order, its attributes, its flags, what
// a tail after `=` fills, which children it holds, and whether it is in the lite subset that small local models get.
// Three words mean different things by place, as the design's own sample uses them: `row` inside a table is a table
// row (elsewhere a horizontal layout), and `toggle` inside `actions` or a `button` flips a bool instead of drawing a
// switch.

namespace Marrow.Core.NthUi;

/// <summary>What a kind of line is for.</summary>
public enum NthUiRole
{
    /// <summary>A visual leaf: text, heading, divider, badge, stat, callout, image, chart, map.</summary>
    Block,

    /// <summary>Holds other components: card, section, stack, row, grid, form, table, list, tabs and the like.</summary>
    Container,

    /// <summary>A control bound to a state: segmented, stepper, slider, toggle, textfield, select, date.</summary>
    Input,

    /// <summary>Does something when pressed: set, toggle, copy, open, export, send, context, call, button.</summary>
    Action,

    /// <summary>Declares a name: state and let. Not drawn.</summary>
    Declaration,

    /// <summary>Takes its indented lines verbatim: code, svg, diagram.</summary>
    Raw,

    /// <summary>Only inside one parent: a table row, a list item, a tab, a step, a chip, a series, a stop, a route.</summary>
    Part,
}

/// <summary>The kind of value an argument takes.</summary>
public enum NthUiArgType
{
    /// <summary>A quoted text (or a number, or a bracketed expression that computes the text).</summary>
    Text,

    /// <summary>A number, a name, or a bracketed expression.</summary>
    Number,

    /// <summary>true or false, or an expression.</summary>
    Bool,

    /// <summary>A state or let name.</summary>
    Name,

    /// <summary>One word from a fixed set.</summary>
    Choice,

    /// <summary>Any value or expression.</summary>
    Expr,

    /// <summary>A comma-separated text ("Compute,Egress"), or an expression that computes a list.</summary>
    Values,

    /// <summary>Like <see cref="Values"/>, with "value:Label" pairs.</summary>
    Options,

    /// <summary>A quoted address, checked for its scheme.</summary>
    Url,

    /// <summary>An icon name (lower-case letters, digits and dashes).</summary>
    Icon,

    /// <summary>An object literal `{ ... }` (an MCP tool's arguments).</summary>
    Object,

    /// <summary>`server.tool`.</summary>
    Tool,
}

/// <summary>What a kind may hold.</summary>
public enum NthUiChildren
{
    None,

    /// <summary>The top level: every block, input, action and declaration, plus followups.</summary>
    Root,

    /// <summary>Every block, input, action and declaration (cards, sections, stacks, rows, grids, tabs, steps, forms).</summary>
    Blocks,

    /// <summary>`actions`: buttons and actions.</summary>
    Actions,

    /// <summary>`button`: the actions it runs, in order.</summary>
    Steps,

    /// <summary>`table`: rows (and declarations).</summary>
    Rows,

    /// <summary>`list` and `checklist`: items.</summary>
    Items,

    /// <summary>`timeline`: events (and items).</summary>
    Events,

    /// <summary>`steps`: step panels.</summary>
    Panels,

    /// <summary>`tabs`: tab panels.</summary>
    Tabs,

    /// <summary>`chips` and `followups`: chips.</summary>
    Chips,

    /// <summary>`chart`: series.</summary>
    Series,

    /// <summary>`map`: stops and routes.</summary>
    Places,

    /// <summary>`code`, `svg`, `diagram`: the indented lines are the body, verbatim.</summary>
    Raw,
}

/// <summary>One argument of a kind: positional (in order) or an attribute (by name).</summary>
public sealed record NthUiArgSpec(string Name, NthUiArgType Type, bool Required = false, IReadOnlyList<string>? Choices = null,
    double Min = double.NegativeInfinity, double Max = double.PositiveInfinity);

/// <summary>One kind of line: a component, a part, an action or a declaration.</summary>
public sealed class NthUiKindSpec
{
    public required string Kind { get; init; }

    public required NthUiRole Role { get; init; }

    /// <summary>The values that may follow the head without a name, in order (each may also be written name=value).</summary>
    public IReadOnlyList<NthUiArgSpec> Positionals { get; init; } = [];

    public IReadOnlyList<NthUiArgSpec> Attributes { get; init; } = [];

    /// <summary>Bare words that switch something on (`row total`, `list ordered`).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>The argument a tail after `=` fills; null when the kind takes no tail.</summary>
    public string? Tail { get; init; }

    public bool TailRequired { get; init; }

    /// <summary>A table row: every positional value is a cell, and the tail is the last cell.</summary>
    public bool Cells { get; init; }

    /// <summary>When the values do not read, the rest of the line is the first positional's text (`text Hello there`).</summary>
    public bool FreeText { get; init; }

    public NthUiChildren Children { get; init; } = NthUiChildren.None;

    /// <summary>In the lite subset (DESIGN 7.3) for small local models.</summary>
    public bool Lite { get; init; }

    /// <summary>The argument spec named <paramref name="name"/>, positional or attribute, or a common one (id, when).</summary>
    public NthUiArgSpec? Find(string name)
    {
        foreach (var spec in Positionals)
        {
            if (string.Equals(spec.Name, name, StringComparison.Ordinal)) return spec;
        }
        foreach (var spec in Attributes)
        {
            if (string.Equals(spec.Name, name, StringComparison.Ordinal)) return spec;
        }
        if (Role == NthUiRole.Declaration) return null;
        return name switch
        {
            "id" => NthUiSchema.IdArg,
            "when" => NthUiSchema.WhenArg,
            _ => null,
        };
    }

    public override string ToString() => Kind;
}

public static class NthUiSchema
{
    internal static NthUiArgSpec IdArg { get; } = new("id", NthUiArgType.Name);

    internal static NthUiArgSpec WhenArg { get; } = new("when", NthUiArgType.Bool);

    private static readonly string[] Tones = ["neutral", "info", "success", "warning", "danger"];

    private static NthUiArgSpec Text(string name, bool required = false) => new(name, NthUiArgType.Text, required);

    private static NthUiArgSpec Number(string name, double min = double.NegativeInfinity, double max = double.PositiveInfinity) =>
        new(name, NthUiArgType.Number, Min: min, Max: max);

    private static NthUiArgSpec Bind(bool required = false) => new("bind", NthUiArgType.Name, required);

    private static NthUiArgSpec Icon() => new("icon", NthUiArgType.Icon);

    private static NthUiArgSpec Tone() => new("tone", NthUiArgType.Choice, Choices: Tones);

    private static NthUiKindSpec Input(string kind, bool lite, string[] flags, params NthUiArgSpec[] attributes) => new()
    {
        Kind = kind,
        Role = NthUiRole.Input,
        Positionals = [Bind(required: true)],
        Attributes = [Text("label"), .. attributes],
        Flags = flags,
        Lite = lite,
    };

    private static readonly NthUiKindSpec[] Catalogue =
    [
        // Text and layout.
        new() { Kind = "text", Role = NthUiRole.Block, Positionals = [Text("text", true)], Flags = ["muted", "strong", "small"], Tail = "text", FreeText = true, Lite = true },
        new() { Kind = "heading", Role = NthUiRole.Block, Positionals = [Text("text", true)], Attributes = [Number("level", 1, 3), Icon()], Tail = "text", FreeText = true },
        new() { Kind = "card", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Text("subtitle"), Icon()], Children = NthUiChildren.Blocks, Lite = true },
        new() { Kind = "section", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Icon()], Children = NthUiChildren.Blocks },
        new() { Kind = "stack", Role = NthUiRole.Container, Attributes = [Number("gap", 0, 64)], Children = NthUiChildren.Blocks },
        new() { Kind = "row", Role = NthUiRole.Container, Attributes = [Number("gap", 0, 64), new("align", NthUiArgType.Choice, Choices: ["start", "center", "end", "between"])], Children = NthUiChildren.Blocks },
        new() { Kind = "grid", Role = NthUiRole.Container, Attributes = [Number("cols", 1, 6), Number("gap", 0, 64)], Children = NthUiChildren.Blocks },
        new() { Kind = "divider", Role = NthUiRole.Block },
        new() { Kind = "badge", Role = NthUiRole.Block, Positionals = [Text("text", true)], Attributes = [Tone()], Tail = "text", FreeText = true },
        new() { Kind = "stat", Role = NthUiRole.Block, Positionals = [Text("label", true)], Attributes = [new("value", NthUiArgType.Expr), Text("unit"), new("delta", NthUiArgType.Expr), Icon(), Tone()], Tail = "value" },
        new() { Kind = "callout", Role = NthUiRole.Block, Positionals = [Text("text", true)], Attributes = [Text("title"), Tone(), Icon()], Tail = "text", FreeText = true },
        new() { Kind = "image", Role = NthUiRole.Block, Positionals = [new("src", NthUiArgType.Url, true)], Attributes = [Text("alt"), Number("width", 1, 4096), Number("height", 1, 4096), new("fit", NthUiArgType.Choice, Choices: ["contain", "cover"])] },
        new() { Kind = "svg", Role = NthUiRole.Raw, Attributes = [Text("alt"), Number("width", 1, 4096), Number("height", 1, 4096)], Children = NthUiChildren.Raw },
        new() { Kind = "diagram", Role = NthUiRole.Raw, Positionals = [Text("title")], Attributes = [Text("alt")], Children = NthUiChildren.Raw },
        new() { Kind = "code", Role = NthUiRole.Raw, Positionals = [Text("text")], Attributes = [new("lang", NthUiArgType.Choice, Choices: null), Text("title")], Children = NthUiChildren.Raw },

        // Data.
        new()
        {
            Kind = "chart", Role = NthUiRole.Container,
            Positionals = [new("type", NthUiArgType.Choice, Choices: ["line", "bar", "area", "pie", "scatter"])],
            Attributes = [Text("title"), new("x", NthUiArgType.Values), new("y", NthUiArgType.Values), new("series", NthUiArgType.Values), Text("unit"),
                Number("min"), Number("max"), Number("step", 0), new("editable", NthUiArgType.Choice, Choices: ["points"]), Bind()],
            Flags = ["stacked"], Children = NthUiChildren.Series, Lite = true,
        },
        new() { Kind = "series", Role = NthUiRole.Part, Positionals = [Text("name")], Attributes = [new("y", NthUiArgType.Values), new("x", NthUiArgType.Values)], Tail = "y", Lite = true },
        new() { Kind = "table", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [new("cols", NthUiArgType.Values), new("rows", NthUiArgType.Expr)], Children = NthUiChildren.Rows, Lite = true },
        new() { Kind = "list", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [new("items", NthUiArgType.Values)], Flags = ["ordered"], Children = NthUiChildren.Items },
        new() { Kind = "checklist", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Bind()], Children = NthUiChildren.Items, Lite = true },
        new() { Kind = "item", Role = NthUiRole.Part, Positionals = [Text("text", true)], Attributes = [Text("value"), Text("detail")], Flags = ["done"], Tail = "text", FreeText = true, Lite = true },
        new() { Kind = "timeline", Role = NthUiRole.Container, Positionals = [Text("title")], Children = NthUiChildren.Events },
        new() { Kind = "event", Role = NthUiRole.Part, Positionals = [Text("title", true)], Attributes = [Text("time"), Text("detail"), Tone()], Flags = ["done"], Tail = "detail", FreeText = true },
        new() { Kind = "steps", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Bind()], Children = NthUiChildren.Panels },
        new() { Kind = "step", Role = NthUiRole.Part, Positionals = [Text("title", true)], Children = NthUiChildren.Blocks },
        new() { Kind = "tabs", Role = NthUiRole.Container, Attributes = [Bind()], Children = NthUiChildren.Tabs },
        new() { Kind = "tab", Role = NthUiRole.Part, Positionals = [Text("label", true)], Attributes = [Text("value")], Children = NthUiChildren.Blocks },
        new() { Kind = "chips", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Bind()], Flags = ["single"], Children = NthUiChildren.Chips, Lite = true },
        new() { Kind = "chip", Role = NthUiRole.Part, Positionals = [Text("label", true)], Attributes = [Text("value")], Flags = ["selected"], FreeText = true, Lite = true },
        new() { Kind = "map", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Number("zoom", 0, 20)], Children = NthUiChildren.Places },
        new() { Kind = "stop", Role = NthUiRole.Part, Positionals = [Text("label", true)], Attributes = [new("lat", NthUiArgType.Number, true, Min: -90, Max: 90), new("lon", NthUiArgType.Number, true, Min: -180, Max: 180), Text("detail")] },
        new() { Kind = "route", Role = NthUiRole.Part, Positionals = [Text("label")], Attributes = [new("via", NthUiArgType.Values, true)] },
        new() { Kind = "followups", Role = NthUiRole.Container, Children = NthUiChildren.Chips, Lite = true },

        // Inputs: the positional name is the state they set (declared with `state`, or made with a default).
        Input("segmented", false, [], new NthUiArgSpec("options", NthUiArgType.Options, true)),
        Input("stepper", true, [], Number("min"), Number("max"), Number("step", 0), Text("unit")),
        Input("slider", true, [], Number("min"), Number("max"), Number("step", 0), Text("unit")),
        Input("toggle", false, []),
        Input("textfield", false, ["multiline"], Text("placeholder")),
        Input("select", false, [], new NthUiArgSpec("options", NthUiArgType.Options, true)),
        Input("date", false, [], Text("min"), Text("max")),
        new() { Kind = "form", Role = NthUiRole.Container, Positionals = [Text("title")], Attributes = [Text("submit")], Tail = "message", Children = NthUiChildren.Blocks },

        // Actions: drawn as buttons on their own, run in order inside a button.
        new() { Kind = "actions", Role = NthUiRole.Container, Children = NthUiChildren.Actions },
        new() { Kind = "button", Role = NthUiRole.Action, Positionals = [Text("label", true)], Flags = ["primary"], Children = NthUiChildren.Steps },
        new() { Kind = "copy", Role = NthUiRole.Action, Positionals = [Text("label")], Attributes = [new("value", NthUiArgType.Expr)], Tail = "value" },
        new() { Kind = "set", Role = NthUiRole.Action, Positionals = [new("target", NthUiArgType.Name, true), Text("label")], Tail = "value", TailRequired = true },
        new() { Kind = "open", Role = NthUiRole.Action, Positionals = [Text("label"), Text("url")], Attributes = [] },
        new() { Kind = "export", Role = NthUiRole.Action, Positionals = [new("format", NthUiArgType.Choice, true, ["csv", "png"]), Text("label")], Attributes = [new("value", NthUiArgType.Expr)] },
        new() { Kind = "send", Role = NthUiRole.Action, Positionals = [Text("text", true)], Tail = "message", FreeText = true },
        new() { Kind = "context", Role = NthUiRole.Action, Positionals = [Text("key")], Tail = "value", TailRequired = true },
        new() { Kind = "call", Role = NthUiRole.Action, Positionals = [new("tool", NthUiArgType.Tool, true), new("args", NthUiArgType.Object), Text("label")] },

        // Declarations.
        new() { Kind = "state", Role = NthUiRole.Declaration, Positionals = [new("name", NthUiArgType.Name, true)], Attributes = [new("type", NthUiArgType.Choice, Choices: ["number", "text", "string", "bool", "list", "selection"])], Tail = "value", TailRequired = true, Lite = true },
        new() { Kind = "let", Role = NthUiRole.Declaration, Positionals = [new("name", NthUiArgType.Name, true)], Tail = "value", TailRequired = true, Lite = true },
    ];

    private static readonly Dictionary<string, NthUiKindSpec> ByKind = Catalogue.ToDictionary(s => s.Kind, StringComparer.Ordinal);

    /// <summary>A row inside a table: its values are cells, the tail is the last cell.</summary>
    public static NthUiKindSpec TableRow { get; } = new()
    {
        Kind = "row", Role = NthUiRole.Part, Cells = true, Flags = ["total", "muted"], Tail = "cell", Lite = true,
    };

    /// <summary>`toggle name` inside actions or a button: flips a bool state.</summary>
    public static NthUiKindSpec ToggleAction { get; } = new()
    {
        Kind = "toggle", Role = NthUiRole.Action, Positionals = [new("target", NthUiArgType.Name, true), Text("label")],
    };

    /// <summary>Every kind the language knows (the place-dependent table row and toggle action not repeated).</summary>
    public static IReadOnlyList<NthUiKindSpec> Kinds => Catalogue;

    /// <summary>The kinds in the lite subset (DESIGN 7.3): card, text, table, chart, stepper, slider, chips, checklist,
    /// followups, with the rows, series, items and chips they hold and the state and let lines that drive them.</summary>
    public static IReadOnlyList<string> LiteKinds { get; } = Catalogue.Where(s => s.Lite).Select(s => s.Kind).ToArray();

    public static bool IsKnown(string kind) => ByKind.ContainsKey(kind);

    /// <summary>The spec of <paramref name="kind"/> written inside a <paramref name="parent"/> (null at the top level).</summary>
    public static NthUiKindSpec? Resolve(string kind, NthUiKindSpec? parent)
    {
        if (parent is not null)
        {
            if (kind == "row" && parent.Children == NthUiChildren.Rows) return TableRow;
            if (kind == "toggle" && parent.Children is NthUiChildren.Actions or NthUiChildren.Steps) return ToggleAction;
        }
        return ByKind.GetValueOrDefault(kind);
    }

    /// <summary>Whether a container holding <paramref name="children"/> takes <paramref name="spec"/>.</summary>
    public static bool Accepts(NthUiChildren children, NthUiKindSpec spec)
    {
        var kind = spec.Kind;
        return children switch
        {
            NthUiChildren.Root => spec.Role is not NthUiRole.Part,
            NthUiChildren.Blocks => spec.Role is not NthUiRole.Part && kind != "followups",
            NthUiChildren.Actions => spec.Role == NthUiRole.Action,
            NthUiChildren.Steps => spec.Role == NthUiRole.Action && kind != "button",
            NthUiChildren.Rows => ReferenceEquals(spec, TableRow) || spec.Role == NthUiRole.Declaration,
            NthUiChildren.Items => kind == "item",
            NthUiChildren.Events => kind is "event" or "item",
            NthUiChildren.Panels => kind == "step",
            NthUiChildren.Tabs => kind == "tab",
            NthUiChildren.Chips => kind == "chip",
            NthUiChildren.Series => kind == "series",
            NthUiChildren.Places => kind is "stop" or "route",
            NthUiChildren.None or NthUiChildren.Raw => false,
        };
    }

    /// <summary>Whether degraded lines (shown as text) may sit in a container holding <paramref name="children"/>.</summary>
    public static bool TakesText(NthUiChildren children) => children is NthUiChildren.Root or NthUiChildren.Blocks;
}
