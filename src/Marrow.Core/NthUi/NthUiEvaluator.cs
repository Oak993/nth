using System.Globalization;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 "Evaluated by nth in C#"; ED-NEW-web-w15-1). No Swift counterpart. Evaluates
// a parsed expression against the view's names. Pure and bounded: every node visited and every list item made costs a
// step, and an evaluation that runs out of steps returns an error value instead of running on. Errors are values, so a
// bad sub-expression shows as a dash where it is used and never throws.

namespace Marrow.Core.NthUi;

/// <summary>A step allowance shared by the evaluations of one pass (a recompute after a change, or a render).</summary>
public sealed class NthUiBudget(int steps = NthUiBudget.DefaultSteps)
{
    /// <summary>The steps one pass may take before it gives up (a few milliseconds of work at most).</summary>
    public const int DefaultSteps = 400_000;

    public int Remaining { get; private set; } = steps;

    public bool Exhausted => Remaining < 0;

    internal bool Spend(int cost)
    {
        Remaining -= cost;
        return Remaining >= 0;
    }
}

public sealed class NthUiEvaluator
{
    /// <summary>How deep evaluations may call each other through names (a let that reads a let that reads a let ...).</summary>
    public const int MaxNameNesting = 64;

    private readonly Func<string, NthUiValue> resolve;
    private readonly CultureInfo culture;
    private readonly NthUiBudget? shared;
    private readonly List<KeyValuePair<string, NthUiValue>> locals = [];
    private NthUiBudget budget = new();
    private int nesting;

    /// <param name="resolve">Looks up a state or let name; an unknown name should return an error value. It may call
    /// <see cref="Evaluate"/> again (a let reading a let); those nested evaluations share the budget.</param>
    /// <param name="budget">A budget shared by every evaluation until the caller makes a new one; without it each
    /// outermost evaluation gets its own <see cref="NthUiBudget.DefaultSteps"/>.</param>
    public NthUiEvaluator(Func<string, NthUiValue>? resolve = null, CultureInfo? culture = null, NthUiBudget? budget = null)
    {
        this.resolve = resolve ?? (name => NthUiValue.Error($"'{name}' is not defined"));
        this.culture = culture ?? CultureInfo.InvariantCulture;
        shared = budget;
    }

    public CultureInfo Culture => culture;

    public NthUiValue Evaluate(NthUiExpr expr)
    {
        if (nesting == 0) budget = shared ?? new NthUiBudget();
        if (nesting >= MaxNameNesting) return NthUiValue.Error("names build on each other too deeply");
        var saved = locals.Count > 0 ? locals.ToArray() : null;
        locals.Clear();
        nesting++;
        try
        {
            var value = Eval(expr);
            return budget.Exhausted ? OutOfSteps : value;
        }
        finally
        {
            nesting--;
            locals.Clear();
            if (saved is not null) locals.AddRange(saved);
        }
    }

    /// <summary>Evaluates a standalone expression with no names, for tests and constant attributes.</summary>
    public static NthUiValue Constant(string text, CultureInfo? culture = null) =>
        NthUiExpr.TryParse(text, out var expr, out var error) ? new NthUiEvaluator(null, culture).Evaluate(expr!) : NthUiValue.Error(error);

    private bool Spend(int cost = 1) => budget.Spend(cost);

    private static NthUiValue OutOfSteps => NthUiValue.Error("the expression takes too much work");

    private NthUiValue Eval(NthUiExpr node)
    {
        if (!Spend()) return OutOfSteps;
        switch (node.Kind)
        {
            case NthUiExprKind.Literal:
                return node.Value;
            case NthUiExprKind.Name:
                for (var i = locals.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(locals[i].Key, node.Name, StringComparison.Ordinal)) return locals[i].Value;
                }
                return resolve(node.Name);
            case NthUiExprKind.Unary:
                {
                    var operand = Eval(node.Args[0]);
                    if (node.Op == "not") return operand.IsError ? operand : NthUiValue.Of(!operand.IsTruthy);
                    if (operand.IsError) return operand;
                    return operand.IsNumber ? NthUiValue.Of(-operand.AsNumber) : NthUiValue.Error($"cannot negate a {operand.TypeName}");
                }
            case NthUiExprKind.Binary:
                return Binary(node);
            case NthUiExprKind.Conditional:
                {
                    var condition = Eval(node.Args[0]);
                    if (condition.IsError) return condition;
                    return Eval(condition.IsTruthy ? node.Args[1] : node.Args[2]);
                }
            case NthUiExprKind.List:
                {
                    var items = new NthUiValue[node.Args.Count];
                    for (var i = 0; i < items.Length; i++)
                    {
                        items[i] = Eval(node.Args[i]);
                        if (budget.Exhausted) return OutOfSteps;
                    }
                    return NthUiValue.List(items);
                }
            case NthUiExprKind.Object:
                {
                    var fields = new List<KeyValuePair<string, NthUiValue>>(node.Args.Count);
                    for (var i = 0; i < node.Args.Count; i++)
                    {
                        fields.Add(new(node.Keys[i], Eval(node.Args[i])));
                        if (budget.Exhausted) return OutOfSteps;
                    }
                    return NthUiValue.Object(fields);
                }
            case NthUiExprKind.Index:
                return Index(Eval(node.Args[0]), Eval(node.Args[1]));
            case NthUiExprKind.Member:
                return Member(Eval(node.Args[0]), node.Name);
            case NthUiExprKind.Call:
                return Call(node);
            case NthUiExprKind.Lambda:
                return NthUiValue.Error("a lambda is not a value");
        }
        return NthUiValue.Error("unknown expression");
    }

    private NthUiValue Binary(NthUiExpr node)
    {
        if (node.Op is "and" or "or")
        {
            var first = Eval(node.Args[0]);
            if (first.IsError) return first;
            if (node.Op == "and" ? !first.IsTruthy : first.IsTruthy) return NthUiValue.Of(first.IsTruthy);
            var second = Eval(node.Args[1]);
            return second.IsError ? second : NthUiValue.Of(second.IsTruthy);
        }
        var left = Eval(node.Args[0]);
        if (left.IsError) return left;
        var right = Eval(node.Args[1]);
        if (right.IsError) return right;
        switch (node.Op)
        {
            case "==":
                return NthUiValue.Of(EqualWithinBudget(left, right));
            case "!=":
                return NthUiValue.Of(!EqualWithinBudget(left, right));
            case "<" or "<=" or ">" or ">=":
                {
                    int order;
                    if (left.IsNumber && right.IsNumber) order = left.AsNumber.CompareTo(right.AsNumber);
                    else if (left.IsText && right.IsText) order = string.CompareOrdinal(left.AsText, right.AsText);
                    else return NthUiValue.Error($"cannot compare a {left.TypeName} with a {right.TypeName}");
                    return NthUiValue.Of(node.Op switch { "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 });
                }
            case "+":
                if (left.IsNumber && right.IsNumber) return NthUiValue.Of(left.AsNumber + right.AsNumber);
                if (left.IsList && right.IsList)
                    return Spend(left.Items.Count + right.Items.Count) ? NthUiValue.List(left.Items.Concat(right.Items).ToArray()) : OutOfSteps;
                if (left.IsText || right.IsText)
                {
                    var a = left.Display(culture);
                    var b = right.Display(culture);
                    if (!Spend(1 + (a.Length + b.Length) / 64)) return OutOfSteps;
                    return a.Length + b.Length > NthUiValue.MaxTextLength ? NthUiValue.Error("the text is too long") : NthUiValue.Of(a + b);
                }
                return NthUiValue.Error($"cannot add a {left.TypeName} and a {right.TypeName}");
        }
        if (!left.IsNumber || !right.IsNumber) return NthUiValue.Error($"'{node.Op}' needs two numbers");
        var x = left.AsNumber;
        var y = right.AsNumber;
        return node.Op switch
        {
            "-" => NthUiValue.Of(x - y),
            "*" => NthUiValue.Of(x * y),
            "/" => y == 0 ? NthUiValue.Error("division by zero") : NthUiValue.Of(x / y),
            "%" => y == 0 ? NthUiValue.Error("division by zero") : NthUiValue.Of(x % y),
            _ => NthUiValue.Error($"unknown operator '{node.Op}'"),
        };
    }

    // Equality walks every nested value under the same pass budget. A compact shared
    // value graph can otherwise expand into millions of comparisons (ED-NEW-web-review-6).
    private bool EqualWithinBudget(NthUiValue left, NthUiValue right, int depth = 0)
    {
        if (!Spend()) return false;
        if (depth >= MaxNameNesting) { Spend(Math.Max(0, budget.Remaining) + 1); return false; }
        if (left.Kind != right.Kind) return false;
        if (left.IsList)
        {
            if (left.Items.Count != right.Items.Count) return false;
            for (var i = 0; i < left.Items.Count; i++)
                if (!EqualWithinBudget(left.Items[i], right.Items[i], depth + 1)) return false;
            return true;
        }
        if (left.IsObject)
        {
            if (left.Fields.Count != right.Fields.Count) return false;
            foreach (var field in left.Fields)
            {
                var found = false;
                foreach (var candidate in right.Fields)
                {
                    // ED-NEW-web-review-23: field lookup is work too, including key comparisons.
                    if (!Spend(1 + Math.Max(field.Key.Length, candidate.Key.Length) / 64)) return false;
                    if (!string.Equals(field.Key, candidate.Key, StringComparison.Ordinal)) continue;
                    if (!EqualWithinBudget(field.Value, candidate.Value, depth + 1)) return false;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }
        if (left.IsText && !Spend(left.AsText.Length / 64)) return false;
        return left.Equals(right);
    }

    private static int Size(NthUiValue value) => value.Kind switch
    {
        NthUiValueKind.List => value.Items.Count,
        NthUiValueKind.Object => value.Fields.Count,
        NthUiValueKind.Text => value.AsText.Length / 64,
        _ => 0,
    };

    private static NthUiValue Index(NthUiValue target, NthUiValue index)
    {
        if (target.IsError) return target;
        if (index.IsError) return index;
        if (target.IsObject)
        {
            var key = index.IsText ? index.AsText : index.Display();
            return target.TryGetField(key, out var field) ? field : NthUiValue.Error($"there is no '{key}'");
        }
        if (!index.IsNumber || index.AsNumber != Math.Floor(index.AsNumber)) return NthUiValue.Error("an index must be a whole number");
        var count = target.IsList ? target.Items.Count : target.IsText ? target.AsText.Length : -1;
        if (count < 0) return NthUiValue.Error($"cannot index a {target.TypeName}");
        var at = index.AsNumber < 0 ? count + index.AsNumber : index.AsNumber;
        if (at < 0 || at >= count) return NthUiValue.Error("the index is outside the list");
        var i = (int)at;
        return target.IsList ? target.Items[i] : NthUiValue.Of(target.AsText[i].ToString());
    }

    private static NthUiValue Member(NthUiValue target, string name)
    {
        if (target.IsError) return target;
        if (target.IsObject) return target.TryGetField(name, out var field) ? field : NthUiValue.Error($"there is no '{name}'");
        if (name is "length" or "count")
        {
            if (target.IsList) return NthUiValue.Of(target.Items.Count);
            if (target.IsText) return NthUiValue.Of(target.AsText.Length);
        }
        return NthUiValue.Error($"a {target.TypeName} has no '{name}'");
    }

    private NthUiValue Call(NthUiExpr node)
    {
        var name = node.Name;
        if (NthUiExpr.TakesLambda(name) && node.Args.Count == 2) return CallWithLambda(node);
        var args = new NthUiValue[node.Args.Count];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = Eval(node.Args[i]);
            if (budget.Exhausted) return OutOfSteps;
            if (args[i].IsError) return args[i];
        }
        switch (name)
        {
            case "min":
            case "max":
                {
                    var values = args.Length == 1 && args[0].IsList ? args[0].Items : args;
                    if (values.Count == 0) return NthUiValue.Error($"{name} of an empty list");
                    if (!Spend(values.Count)) return OutOfSteps;
                    if (values.Any(v => !v.IsNumber)) return NthUiValue.Error($"{name} needs numbers");
                    return NthUiValue.Of(name == "min" ? values.Min(v => v.AsNumber) : values.Max(v => v.AsNumber));
                }
            case "round":
                {
                    if (!args[0].IsNumber) return NthUiValue.Error("round needs a number");
                    var digits = args.Length > 1 && args[1].IsNumber ? Math.Clamp(args[1].AsNumber, 0, 10) : 0;
                    return NthUiValue.Of(Math.Round(args[0].AsNumber, (int)digits, MidpointRounding.AwayFromZero));
                }
            case "floor":
                return Numeric(args[0], Math.Floor);
            case "ceil":
                return Numeric(args[0], Math.Ceiling);
            case "abs":
                return Numeric(args[0], Math.Abs);
            case "sqrt":
                return args[0].IsNumber && args[0].AsNumber < 0 ? NthUiValue.Error("the square root of a negative number") : Numeric(args[0], Math.Sqrt);
            case "pow":
                return args[0].IsNumber && args[1].IsNumber ? NthUiValue.Of(Math.Pow(args[0].AsNumber, args[1].AsNumber)) : NthUiValue.Error("pow needs two numbers");
            case "clamp":
                {
                    if (!args[0].IsNumber || !args[1].IsNumber || !args[2].IsNumber) return NthUiValue.Error("clamp needs three numbers");
                    double lo = args[1].AsNumber, hi = args[2].AsNumber;
                    return NthUiValue.Of(lo > hi ? lo : Math.Clamp(args[0].AsNumber, lo, hi));
                }
            case "fmt":
                return Spend(1 + Size(args[0]))
                    ? NthUiValue.Of(NthUiFormat.Format(args[0], args.Length > 1 && args[1].IsText ? args[1].AsText : null, culture))
                    : OutOfSteps;
            case "len":
                return args[0].Kind switch
                {
                    NthUiValueKind.List => NthUiValue.Of(args[0].Items.Count),
                    NthUiValueKind.Text => NthUiValue.Of(args[0].AsText.Length),
                    NthUiValueKind.Object => NthUiValue.Of(args[0].Fields.Count),
                    _ => NthUiValue.Error($"len of a {args[0].TypeName}"),
                };
            case "sum":
            case "avg":
                {
                    if (!args[0].IsList) return NthUiValue.Error($"{name} needs a list");
                    var items = args[0].Items;
                    if (!Spend(items.Count)) return OutOfSteps;
                    if (items.Any(v => !v.IsNumber)) return NthUiValue.Error($"{name} needs a list of numbers");
                    if (name == "avg" && items.Count == 0) return NthUiValue.Error("avg of an empty list");
                    var total = items.Sum(v => v.AsNumber);
                    return NthUiValue.Of(name == "sum" ? total : total / items.Count);
                }
            case "sort":
                {
                    if (!args[0].IsList) return NthUiValue.Error("sort needs a list");
                    var items = args[0].Items;
                    if (!Spend(items.Count * 4)) return OutOfSteps;
                    if (items.All(v => v.IsNumber)) return NthUiValue.List(items.OrderBy(v => v.AsNumber).ToArray());
                    if (items.All(v => v.IsText)) return NthUiValue.List(items.OrderBy(v => v.AsText, StringComparer.Ordinal).ToArray());
                    return NthUiValue.Error("sort needs a list of numbers or of texts");
                }
            case "reverse":
                if (args[0].IsList) return Spend(args[0].Items.Count) ? NthUiValue.List(args[0].Items.Reverse().ToArray()) : OutOfSteps;
                if (args[0].IsText) return NthUiValue.Of(new string(args[0].AsText.Reverse().ToArray()));
                return NthUiValue.Error("reverse needs a list or a text");
            case "slice":
                return Slice(args);
            case "range":
                return Range(args);
            case "join":
                {
                    if (!args[0].IsList) return NthUiValue.Error("join needs a list");
                    if (!Spend(args[0].Items.Count)) return OutOfSteps;
                    var separator = args.Length > 1 ? args[1].Display(culture) : ", ";
                    var text = new System.Text.StringBuilder();
                    for (var i = 0; i < args[0].Items.Count; i++)
                    {
                        var value = args[0].Items[i].Display(culture);
                        var between = i == 0 ? "" : separator;
                        if (text.Length + between.Length + value.Length > NthUiValue.MaxTextLength) return NthUiValue.Error("the text is too long");
                        if (!Spend(1 + (between.Length + value.Length) / 64)) return OutOfSteps;
                        text.Append(between);
                        text.Append(value);
                    }
                    return NthUiValue.Of(text.ToString());
                }
            case "contains":
                if (args[0].IsList)
                {
                    foreach (var item in args[0].Items)
                    {
                        if (EqualWithinBudget(item, args[1])) return NthUiValue.Of(true);
                        if (budget.Exhausted) return OutOfSteps;
                    }
                    return NthUiValue.Of(false);
                }
                if (args[0].IsText) return NthUiValue.Of(args[0].AsText.Contains(args[1].Display(culture), StringComparison.OrdinalIgnoreCase));
                if (args[0].IsObject) return NthUiValue.Of(args[0].TryGetField(args[1].Display(culture), out _));
                return NthUiValue.Error("contains needs a list or a text");
            case "str":
                return NthUiValue.Of(args[0].Display(culture));
            case "num":
                return args[0].Kind switch
                {
                    NthUiValueKind.Number => args[0],
                    NthUiValueKind.Bool => NthUiValue.Of(args[0].AsNumber),
                    NthUiValueKind.Text when double.TryParse(args[0].AsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        => NthUiValue.Of(parsed),
                    _ => NthUiValue.Error($"cannot read a number from a {args[0].TypeName}"),
                };
        }
        return NthUiValue.Error($"there is no function '{name}'");
    }

    private static NthUiValue Numeric(NthUiValue value, Func<double, double> f) =>
        value.IsNumber ? NthUiValue.Of(f(value.AsNumber)) : NthUiValue.Error($"needs a number, not a {value.TypeName}");

    private NthUiValue Slice(NthUiValue[] args)
    {
        var count = args[0].IsList ? args[0].Items.Count : args[0].IsText ? args[0].AsText.Length : -1;
        if (count < 0) return NthUiValue.Error("slice needs a list or a text");
        if (!args[1].IsNumber || (args.Length > 2 && !args[2].IsNumber)) return NthUiValue.Error("slice needs whole numbers");
        int Bound(double v) => (int)Math.Clamp(v < 0 ? count + Math.Ceiling(v) : Math.Floor(v), 0, count);
        var from = Bound(args[1].AsNumber);
        var to = args.Length > 2 ? Bound(args[2].AsNumber) : count;
        if (to < from) to = from;
        if (!Spend(to - from)) return OutOfSteps;
        return args[0].IsList ? NthUiValue.List(args[0].Items.Skip(from).Take(to - from).ToArray()) : NthUiValue.Of(args[0].AsText[from..to]);
    }

    private NthUiValue Range(NthUiValue[] args)
    {
        if (args.Any(a => !a.IsNumber)) return NthUiValue.Error("range needs numbers");
        double start = 0, end, step = 1;
        if (args.Length == 1) end = args[0].AsNumber;
        else
        {
            start = args[0].AsNumber;
            end = args[1].AsNumber;
            if (args.Length > 2) step = args[2].AsNumber;
        }
        if (step == 0) return NthUiValue.Error("range needs a step that is not 0");
        var count = Math.Ceiling((end - start) / step);
        if (!double.IsFinite(count)) return NthUiValue.Error("range needs finite numbers");
        if (count <= 0) return NthUiValue.List([]);
        if (count > NthUiValue.MaxListLength) return NthUiValue.Error("the range is too long");
        var n = (int)count;
        if (!Spend(n)) return OutOfSteps;
        var items = new NthUiValue[n];
        for (var i = 0; i < n; i++) items[i] = NthUiValue.Of(Math.Round(start + i * step, 10));
        return NthUiValue.List(items);
    }

    private NthUiValue CallWithLambda(NthUiExpr node)
    {
        var list = Eval(node.Args[0]);
        if (list.IsError) return list;
        if (!list.IsList) return NthUiValue.Error($"{node.Name} needs a list");
        var lambda = node.Args[1];
        var items = list.Items;
        var results = new List<NthUiValue>(items.Count);
        var keys = node.Name == "sort" ? new List<NthUiValue>(items.Count) : null;
        locals.Add(new(lambda.Name, NthUiValue.Null));
        var slot = locals.Count - 1;
        try
        {
            foreach (var item in items)
            {
                if (!Spend()) return OutOfSteps;
                locals[slot] = new(lambda.Name, item);
                var value = Eval(lambda.Args[0]);
                if (budget.Exhausted) return OutOfSteps;
                if (value.IsError) return value;
                switch (node.Name)
                {
                    case "map":
                        results.Add(value);
                        break;
                    case "filter":
                        if (value.IsTruthy) results.Add(item);
                        break;
                    default:
                        results.Add(item);
                        keys!.Add(value);
                        break;
                }
            }
        }
        finally
        {
            locals.RemoveAt(slot);
        }
        if (keys is null) return NthUiValue.List(results.ToArray());
        if (!Spend(items.Count * 4)) return OutOfSteps;
        var order = Enumerable.Range(0, results.Count).ToArray();
        if (keys.All(k => k.IsNumber)) order = order.OrderBy(i => keys[i].AsNumber).ToArray();
        else if (keys.All(k => k.IsText)) order = order.OrderBy(i => keys[i].AsText, StringComparer.Ordinal).ToArray();
        else return NthUiValue.Error("sort needs keys that are all numbers or all texts");
        return NthUiValue.List(order.Select(i => results[i]).ToArray());
    }
}
