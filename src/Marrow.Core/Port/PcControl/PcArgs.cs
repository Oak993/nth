// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-7; DESIGN.md 2.2, 2.12). Reading a tool call's arguments
// (strict: a wrong type is refused, never guessed), the target locator and the expect post-condition, and the rules a
// pc_batch plan must meet before any step runs (critique C26, C29): 2 to 50 known steps; a window bound with "$name"
// only after the step that opens it; bound text only after the pc_read that binds it; and an expect on every step a later
// step depends on.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.PcControl;

/// <summary>A tool call refused for its arguments before anything ran (bad_args).</summary>
public sealed class PcArgsException(string message) : Exception(message);

public static partial class PcArgs
{
    public static string? Str(JsonObject args, string name)
    {
        if (args[name] is null) return null;
        if (args[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw new PcArgsException(name + " must be text");
    }

    public static bool Bool(JsonObject args, string name, bool fallback)
    {
        if (args[name] is null) return fallback;
        if (args[name] is JsonValue value && value.TryGetValue<bool>(out var flag)) return flag;
        throw new PcArgsException(name + " must be true or false");
    }

    public static int? Int(JsonObject args, string name)
    {
        if (args[name] is not JsonValue value) return args[name] is null ? null : throw new PcArgsException(name + " must be a whole number");
        if (value.TryGetValue<int>(out var number)) return number;
        if (value.TryGetValue<long>(out var wide) && wide is >= int.MinValue and <= int.MaxValue) return (int)wide;
        if (value.TryGetValue<double>(out var real) && real == Math.Floor(real) && Math.Abs(real) < int.MaxValue) return (int)real;
        throw new PcArgsException(name + " must be a whole number");
    }

    public static double? Num(JsonObject args, string name)
    {
        if (args[name] is not JsonValue value) return args[name] is null ? null : throw new PcArgsException(name + " must be a number");
        if (value.TryGetValue<double>(out var real) && double.IsFinite(real)) return real;
        if (value.TryGetValue<int>(out var whole)) return whole;
        if (value.TryGetValue<long>(out var wide)) return wide;
        throw new PcArgsException(name + " must be a number");
    }

    /// <summary>[x, y] as numbers; null when absent.</summary>
    public static (double X, double Y)? Pair(JsonNode? node, string name)
    {
        if (node is null) return null;
        if (node is JsonArray { Count: 2 } array && Number(array[0]) is { } x && Number(array[1]) is { } y) return (x, y);
        throw new PcArgsException(name + " must be [x, y]");
    }

    /// <summary>[x, y, width, height] with a positive size; null when absent.</summary>
    public static (double X, double Y, double Width, double Height)? Region(JsonObject args, string name = "region")
    {
        if (args[name] is null) return null;
        if (args[name] is JsonArray { Count: 4 } array && array.Select(Number).ToList() is var values && values.All(v => v is not null)
            && values[2] > 0 && values[3] > 0)
            return (values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value);
        throw new PcArgsException(name + " must be [x, y, width, height] with a positive width and height");
    }

    /// <summary>The target locator, or null when the call gave none.</summary>
    public static PcLocator? Locator(JsonObject args)
    {
        if (args["target"] is null) return null;
        if (args["target"] is not JsonObject target) throw new PcArgsException("target must be an object with name, type, id, within or nth");
        var locator = new PcLocator(Str(target, "name"), Str(target, "type"), Str(target, "id"), Str(target, "within"), Int(target, "nth"));
        if (locator.Empty) throw new PcArgsException("target needs a name, a type or an id");
        if (locator.Nth is < 1) throw new PcArgsException("target nth counts from 1");
        return locator;
    }

    /// <summary>The expect post-condition, or null when the call gave none.</summary>
    public static PcExpect? Expect(JsonObject args)
    {
        if (args["expect"] is null) return null;
        if (args["expect"] is not JsonObject expect) throw new PcArgsException("expect must be an object");
        PcWindowMatch? opens = null;
        if (expect["window_opens"] is JsonObject window) opens = new PcWindowMatch(Str(window, "title"), Str(window, "class"));
        else if (expect["window_opens"] is not null) throw new PcArgsException("expect window_opens must be an object with title or class");
        var parsed = new PcExpect
        {
            Value = Str(expect, "value"),
            Name = Str(expect, "name"),
            Appears = Str(expect, "appears"),
            Gone = Str(expect, "gone"),
            Selected = Flag(expect, "selected"),
            Checked = Flag(expect, "checked"),
            FocusedInApp = Flag(expect, "focused_in_app"),
            WindowOpens = opens,
            TextInWindow = Str(expect, "text_in_window"),
            Bind = Str(expect, "bind"),
        };
        if (parsed.Bind is { } bind)
        {
            if (!IsName(bind)) throw new PcArgsException("expect bind must be a name such as $dlg");
            if (opens is null) throw new PcArgsException("expect bind names the window that opens, so it needs window_opens");
        }
        if (!parsed.Any) throw new PcArgsException("expect names nothing to check");
        return parsed;
    }

    /// <summary>A bound name: "$" then a letter or "_", then letters, digits or "_".</summary>
    public static bool IsName(string? text) => text is not null && BoundName().IsMatch(text);

    [GeneratedRegex(@"^\$[A-Za-z_][A-Za-z0-9_]{0,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex BoundName();

    private static bool? Flag(JsonObject args, string name) => args[name] is null ? null : Bool(args, name, false);

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var real) && double.IsFinite(real)) return real;
        if (value.TryGetValue<int>(out var whole)) return whole;
        if (value.TryGetValue<long>(out var wide)) return wide;
        return null;
    }

    internal static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The rules a pc_batch plan meets before any of its steps runs (DESIGN.md 2.2, 2.12).</summary>
public static class PcBatchPlan
{
    /// <summary>
    /// A step a later step depends on (critique C26): a click, a key, an element action other than focus and
    /// scroll_into_view, and a typed text that submits. Such a step needs an expect when steps follow it in its window.
    /// </summary>
    public static bool Dependent(string tool, JsonObject step) => tool switch
    {
        PcToolSchemas.Click or PcToolSchemas.Key => true,
        PcToolSchemas.Action => PcArgs.Str(step, "action") is not ("focus" or "scroll_into_view"),
        PcToolSchemas.Type => PcArgs.Bool(step, "submit", false),
        _ => false,
    };

    /// <summary>The first problem with the plan, or null when it may run.</summary>
    public static string? Problem(JsonArray actions)
    {
        if (actions.Count is < 2 or > PcToolSchemas.MaxBatchSteps)
            return "actions must hold 2 to " + PcArgs.Count(PcToolSchemas.MaxBatchSteps) + " steps";
        var windows = new HashSet<string>(StringComparer.Ordinal);
        var texts = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<(string Tool, JsonObject Step, string Window)>();
        for (var i = 0; i < actions.Count; i++)
        {
            var number = "step " + PcArgs.Count(i + 1);
            if (actions[i] is not JsonObject step) return number + " must be an object with tool";
            string? tool;
            string? window;
            PcExpect? expect;
            try
            {
                tool = PcArgs.Str(step, "tool");
                window = PcArgs.Str(step, "window");
                expect = PcArgs.Expect(step);
            }
            catch (PcArgsException error) { return number + ": " + error.Message; }
            if (tool is null || !PcToolSchemas.BatchSteps.Contains(tool))
                return number + " must name one of " + string.Join(", ", PcToolSchemas.BatchSteps.Order(StringComparer.Ordinal));
            if (window is not null && window.StartsWith('$') && !windows.Contains(window))
                return number + " uses the window " + window + " before a step's expect window_opens binds it";
            if (window is not null && !window.StartsWith('$') && !PcWindowRef.TryParse(window, out _))
                return number + ": window must be a w:... id or a bound name such as $dlg";
            foreach (var field in new[] { "text", "value" })
            {
                if (step[field] is JsonValue value && value.TryGetValue<string>(out var text) && PcArgs.IsName(text) && !texts.Contains(text))
                    return number + " uses " + text + " as its " + field + ", but no earlier pc_read binds it";
            }
            if (expect?.Bind is { } bound) windows.Add(bound);
            if (step["bind"] is not null)
            {
                string? name;
                try { name = PcArgs.Str(step, "bind"); }
                catch (PcArgsException error) { return number + ": " + error.Message; }
                if (tool != PcToolSchemas.Read) return number + ": only a pc_read step binds text (bind)";
                if (!PcArgs.IsName(name)) return number + ": bind must be a name such as $total";
                texts.Add(name!);
            }
            plan.Add((tool, step, window ?? ""));
        }
        for (var i = 0; i < plan.Count; i++)
        {
            var (tool, step, window) = plan[i];
            bool dependent;
            try { dependent = Dependent(tool, step); }
            catch (PcArgsException error) { return "step " + PcArgs.Count(i + 1) + ": " + error.Message; }
            if (!dependent || step["expect"] is not null) continue;
            // Steps in the same window, or in a window an earlier step opened, come after it.
            var later = plan.Skip(i + 1).Any(p => p.Window == window || p.Window.StartsWith('$'));
            if (later)
                return "step " + PcArgs.Count(i + 1) + " (" + tool + ") has steps after it that depend on its outcome, so it needs an expect (what must be true after it)";
        }
        return null;
    }
}
