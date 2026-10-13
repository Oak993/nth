using Marrow.Core.Models;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.Models.Panes;

[Trait("Decision", "ED-NEW-web-review-6")]
public sealed class WebReviewBoundsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void A_tiny_narrow_workspace_must_keep_its_strip_and_panes_inside_its_height(double height)
    {
        var layout = PaneLayout.Solve(PaneWorkspaceState.Default.Open(PaneKind.Browser), 320, height);
        Assert.True(layout.NarrowStrip!.Value.Bottom <= height,
            $"Tab strip bottom {layout.NarrowStrip.Value.Bottom} exceeds workspace height {height}.");
        Assert.All(layout.Bounds.Values, rect => Assert.True(rect.Y <= height && rect.Bottom <= height));
    }

    [Fact]
    public void Deep_equality_must_spend_budget_for_nested_leaf_comparisons()
    {
        static NthUiValue Tree()
        {
            var leaf = NthUiValue.List(Enumerable.Range(0, 100).Select(i => NthUiValue.Of((double)i)).ToArray());
            var middle = NthUiValue.List(Enumerable.Repeat(leaf, 100).ToArray());
            return NthUiValue.List(Enumerable.Repeat(middle, 100).ToArray());
        }
        var left = Tree();
        var right = Tree();
        var budget = new NthUiBudget(1000);
        var evaluator = new NthUiEvaluator(name => name == "a" ? left : right, budget: budget);
        Assert.True(NthUiExpr.TryParse("a == b", out var expression, out _));
        var result = evaluator.Evaluate(expression!);
        Assert.True(result.IsError, $"Result {result.Display()}; budget remaining {budget.Remaining}");
    }

    [Fact]
    public void Object_equality_spends_budget_on_field_name_searches()
    {
        var fields = Enumerable.Range(0, 100).Select(i => new KeyValuePair<string, NthUiValue>("field" + i, NthUiValue.Of((double)i))).ToArray();
        var left = NthUiValue.Object(fields);
        var right = NthUiValue.Object(fields.Reverse());
        var evaluator = new NthUiEvaluator(name => name == "a" ? left : right, budget: new NthUiBudget(1000));
        Assert.True(NthUiExpr.TryParse("a == b", out var expression, out _));
        Assert.True(evaluator.Evaluate(expression!).IsError);
    }

}
