using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

[Trait("Decision", "ED-NEW-web-review-18")]
public sealed class WebReviewTextBoundsTests
{
    [Fact]
    public void Displaying_nested_values_stops_at_the_text_limit_without_expanding_the_entire_graph()
    {
        var leaf = NthUiValue.List(Enumerable.Repeat(NthUiValue.Of(new string('x', 1000)), 100).ToArray());
        var tree = NthUiValue.List(Enumerable.Repeat(leaf, 100).ToArray());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var text = tree.Display();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(NthUiValue.MaxTextLength, text.Length);
        Assert.True(allocated < 500_000, $"Display allocated {allocated} bytes for a bounded 20k result.");
    }

    [Fact]
    public void Joining_an_oversized_result_stops_before_allocating_the_whole_result()
    {
        var list = NthUiValue.List(Enumerable.Repeat(NthUiValue.Of(new string('x', 1000)), 1000).ToArray());
        Assert.True(NthUiExpr.TryParse("join(a)", out var expression, out _));
        var evaluator = new NthUiEvaluator(_ => list);
        _ = evaluator.Evaluate(expression!);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = new NthUiEvaluator(_ => list).Evaluate(expression!);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(result.IsError);
        Assert.True(allocated < 500_000, $"Oversized join allocated {allocated} bytes before refusing it.");
    }
}
