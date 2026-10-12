using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, the expression evaluator: arithmetic, ternary, fmt, lists (web surfaces W15;
// ED-NEW-web-w15-1, traceability alias ED-NEW-webnthui-1). 7.2: numbers, strings, booleans, + - * / %, comparisons,
// and or not, ternary, min max round floor ceil abs, fmt, len sum map filter over lists, references to state and let;
// no loops, no recursion, no I/O. Errors are values (a dash where shown), never exceptions.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiExpressionTests
{
    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("10 - 4 - 3", "3")]
    [InlineData("2 * -3", "-6")]
    [InlineData("-2 * 3 + 1", "-5")]
    [InlineData("7 % 3", "1")]
    [InlineData("-7 % 3", "-1")]
    [InlineData("1 / 4", "0.25")]
    [InlineData("0.1 + 0.2", "0.3")]
    [InlineData("1e3 + .5", "1000.5")]
    [InlineData("3 > 2 and 2 > 1", "true")]
    [InlineData("3 > 2 && 1 > 2 || true", "true")]
    [InlineData("not 1 == 2", "true")]
    [InlineData("!true", "false")]
    [InlineData("1 == 1.0", "true")]
    [InlineData("\"a\" < \"b\"", "true")]
    [InlineData("\"eu\" = \"eu\"", "true")]
    [InlineData("[1, 2] == [1, 2]", "true")]
    [InlineData("1 == \"1\"", "false")]
    [InlineData("true ? 1 : 2", "1")]
    [InlineData("false ? 1 : 2 > 1 ? \"yes\" : \"no\"", "yes")]
    [InlineData("\"n=\" + 3", "n=3")]
    [InlineData("3 + \" items\"", "3 items")]
    [InlineData("'single' + \" and double\"", "single and double")]
    [InlineData("\"tab\\there\"", "tab\there")]
    [InlineData("\"\\u00e9t\\u00e9\"", "été")]
    [InlineData("min(3, 1, 2)", "1")]
    [InlineData("max([4, 9, 2])", "9")]
    [InlineData("round(2.5)", "3")]
    [InlineData("round(-2.5)", "-3")]
    [InlineData("round(3.14159, 2)", "3.14")]
    [InlineData("floor(-1.5) + ceil(1.2) + abs(-4)", "4")]
    [InlineData("sqrt(16) + pow(2, 10)", "1028")]
    [InlineData("clamp(15, 0, 10)", "10")]
    [InlineData("len([1, 2, 3]) + len(\"abcd\")", "7")]
    [InlineData("sum([1, 2, 3.5])", "6.5")]
    [InlineData("avg([2, 4])", "3")]
    [InlineData("map([1, 2, 3], x => x * x)", "1, 4, 9")]
    [InlineData("map([1, 2, 3], it * 10)", "10, 20, 30")]
    [InlineData("map([1, 2], (n) => n + 1)", "2, 3")]
    [InlineData("filter([1, 2, 3, 4], n => n % 2 == 0)", "2, 4")]
    [InlineData("sum(map(filter(range(10), n => n > 6), n => n * 2))", "48")]
    [InlineData("sort([3, 1, 2])", "1, 2, 3")]
    [InlineData("sort([\"b\", \"a\"])", "a, b")]
    [InlineData("sort([{n: 2}, {n: 1}], x => x.n)", "{\"n\":1}, {\"n\":2}")]
    [InlineData("reverse([1, 2, 3])", "3, 2, 1")]
    [InlineData("slice([1, 2, 3, 4], 1, 3)", "2, 3")]
    [InlineData("slice(\"abcdef\", -2)", "ef")]
    [InlineData("range(3)", "0, 1, 2")]
    [InlineData("range(1, 2, 0.25)", "1, 1.25, 1.5, 1.75")]
    [InlineData("range(5, 1)", "")]
    [InlineData("join([1, 2, 3], \" / \")", "1 / 2 / 3")]
    [InlineData("contains([1, 2], 2) and contains(\"Hello\", \"ell\")", "true")]
    [InlineData("str(1.5) + num(\"2.5\")", "1.52.5")]
    [InlineData("num(\"2.5\") + 1", "3.5")]
    [InlineData("[10, 20, 30][1] + [10, 20, 30][-1]", "50")]
    [InlineData("{eu: 0.046, us: 0.042}[\"us\"]", "0.042")]
    [InlineData("{a: {b: 3}}.a.b", "3")]
    [InlineData("[1, 2, 3].length", "3")]
    [InlineData("[1, 2] + [3]", "1, 2, 3")]
    [InlineData("null", "")]
    [InlineData("fmt(1234.5, \"$#,##0.00\")", "$1,234.50")]
    [InlineData("fmt(0.256, \"0.0%\")", "25.6%")]
    public void Expressions_evaluate_like_the_reference_says(string expression, string expected)
    {
        using var culture = CultureScope.Of("de-DE");
        Assert.Equal(expected, NthUiEvaluator.Constant(expression).Display());
    }

    [Theory]
    [InlineData("1 / 0", "division by zero")]
    [InlineData("5 % 0", "division by zero")]
    [InlineData("\"a\" * 2", "'*' needs two numbers")]
    [InlineData("-\"a\"", "cannot negate a text")]
    [InlineData("\"a\" < 1", "cannot compare a text with a number")]
    [InlineData("[1, 2][5]", "the index is outside the list")]
    [InlineData("[1, 2][0.5]", "an index must be a whole number")]
    [InlineData("{a: 1}.b", "there is no 'b'")]
    [InlineData("min([])", "min of an empty list")]
    [InlineData("sum([1, \"a\"])", "sum needs a list of numbers")]
    [InlineData("sqrt(-1)", "the square root of a negative number")]
    [InlineData("pow(10, 400)", "the result is not a finite number")]
    [InlineData("range(0, 1, 0)", "range needs a step that is not 0")]
    [InlineData("range(100000)", "the range is too long")]
    [InlineData("missing + 1", "'missing' is not defined")]
    [InlineData("map(5, x => x)", "map needs a list")]
    [InlineData("sort([1, \"a\"])", "sort needs a list of numbers or of texts")]
    public void Errors_are_values_that_say_why(string expression, string message)
    {
        var value = NthUiEvaluator.Constant(expression);
        Assert.True(value.IsError);
        Assert.Equal(message, value.AsText);
        Assert.Equal("—", value.Display());
    }

    [Theory]
    [InlineData("", "a value is missing")]
    [InlineData("1 +", "a value is missing")]
    [InlineData("(1 + 2", "a ( needs its )")]
    [InlineData("[1, 2", "a [ needs its ]")]
    [InlineData("a ? b", "a ? needs its :")]
    [InlineData("\"open", "a text is not closed")]
    [InlineData("1 # 2", "unexpected '#' at 3")]
    [InlineData("foo(1)", "there is no function 'foo'")]
    [InlineData("round()", "round takes 1 to 2 values")]
    [InlineData("abs(1, 2)", "abs takes 1 value")]
    [InlineData("x => x", "a lambda is only allowed inside map, filter or sort")]
    [InlineData("map([1], fmt => 1)", "'fmt' cannot name a lambda's value")]
    [InlineData("(1)(2)", "only functions can be called")]
    [InlineData("1 2", "unexpected '2'")]
    [InlineData("{1 + 2: 3}", "an object key needs a :")]
    public void Malformed_expressions_do_not_parse(string expression, string error)
    {
        Assert.False(NthUiExpr.TryParse(expression, out var expr, out var why));
        Assert.Null(expr);
        Assert.Equal(error, why);
    }

    [Fact]
    public void Names_resolve_through_the_caller_and_lambda_values_shadow_them()
    {
        var values = new Dictionary<string, NthUiValue>(StringComparer.Ordinal) { ["x"] = NthUiValue.Of(10), ["rate"] = NthUiValue.Of(2) };
        var evaluator = new NthUiEvaluator(name => values.TryGetValue(name, out var v) ? v : NthUiValue.Error($"'{name}' is not defined"));
        var expr = NthUiExpr.Parse("map([1, 2], x => x * rate) + [x]")!;
        Assert.Equal("2, 4, 10", evaluator.Evaluate(expr).Display());
        Assert.Equal(["rate", "x"], expr.Names.Order(StringComparer.Ordinal));
        Assert.Equal(["a", "b"], NthUiExpr.Parse("filter(a, it > b)")!.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Nesting_is_bounded_while_parsing_and_evaluating()
    {
        Assert.NotNull(NthUiExpr.Parse(new string('(', 40) + "1" + new string(')', 40)));
        Assert.False(NthUiExpr.TryParse(new string('(', 100) + "1" + new string(')', 100), out _, out var why));
        Assert.Equal("the expression is nested too deeply", why);
        Assert.False(NthUiExpr.TryParse(string.Concat(Enumerable.Repeat("-", 200)) + "1", out _, out _));
        Assert.False(NthUiExpr.TryParse(string.Join(" + ", Enumerable.Repeat("1", 400)), out _, out why));
        Assert.Equal("the expression is nested too deeply", why);
        Assert.Equal("150", NthUiEvaluator.Constant(string.Join(" + ", Enumerable.Repeat("1", 150))).Display());
        Assert.False(NthUiExpr.TryParse(new string('1', NthUiExpr.MaxLength + 1), out _, out why));
        Assert.Equal("the expression is too long", why);
    }

    [Fact]
    public void Evaluation_runs_out_of_steps_instead_of_running_on()
    {
        var heavy = NthUiExpr.Parse("sum(map(range(10000), a => sum(map(range(10000), b => a * b))))")!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var value = new NthUiEvaluator().Evaluate(heavy);
        Assert.True(value.IsError);
        Assert.Equal("the expression takes too much work", value.AsText);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");

        var growing = NthUiExpr.Parse("join(map(range(10000), n => join(range(10000), \"\")), \"\")")!;
        Assert.True(new NthUiEvaluator().Evaluate(growing).IsError);
        var budget = new NthUiBudget(200);
        var shared = new NthUiEvaluator(budget: budget);
        Assert.False(shared.Evaluate(NthUiExpr.Parse("sum(range(50))")!).IsError);
        Assert.True(shared.Evaluate(NthUiExpr.Parse("sum(range(80))")!).IsError);
        Assert.True(budget.Exhausted);
    }

    [Fact]
    public void Values_round_trip_through_json()
    {
        var value = NthUiEvaluator.Constant("{a: [1, 2.5, \"x\", true, null], b: {c: \"d\"}}");
        Assert.Equal("{\"a\":[1,2.5,\"x\",true,null],\"b\":{\"c\":\"d\"}}", value.ToJson());
        Assert.Equal(value, NthUiValue.FromJson(value.ToJson()));
        Assert.True(NthUiValue.FromJson("{not json").IsError);
        Assert.True(NthUiValue.FromJson(new string('[', 40) + new string(']', 40)).IsError);
        Assert.Equal("null", NthUiValue.Error("x").ToJson());
    }
}
