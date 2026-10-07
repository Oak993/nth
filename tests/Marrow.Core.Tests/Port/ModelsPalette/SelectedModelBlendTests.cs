using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Tests.Port.ModelsPalette;

// PORT-ONLY: OD-0048/0051's explicitly approved geometry; ED-613.
// No Req credit: geometry alone does not prove the liquid renderer or the automatic colour engine.
[Trait("Decision", "ED-NEW-modelspalette-2")]
public sealed class SelectedModelBlendTests
{
    [Fact]
    public void Every_model_has_a_span_with_workers_left_and_the_first_lead_at_the_knob()
    {
        var spans = SelectedModelBlend.Spans([
            new("lead-a", true), new("worker-a", false),
            new("lead-b", true), new("worker-b", false)]);
        Assert.Equal(["worker-a", "worker-b", "lead-b", "lead-a"], spans.Select(span => span.ModelId));
        Assert.Equal([false, false, true, true], spans.Select(span => span.IsLead));
        double[] edges = [0, 1.0 / 5.2, 2.0 / 5.2, 3.6 / 5.2, 1];
        for (var i = 0; i < spans.Count; i++)
        {
            Assert.Equal(edges[i], spans[i].Start, 12);
            Assert.Equal(edges[i + 1], spans[i].End, 12);
        }
    }

    [Fact]
    public void A_pair_has_exactly_the_approved_one_point_six_lead_share()
    {
        var spans = SelectedModelBlend.Spans([new("lead", true), new("worker", false)]);
        Assert.Equal("worker", spans[0].ModelId);
        Assert.Equal(1.0 / 2.6, spans[0].End, 12);
        Assert.Equal("lead", spans[1].ModelId);
        Assert.Equal(1.6 / 2.6, spans[1].End - spans[1].Start, 12);
        Assert.Equal(spans[0].End, spans[1].Start);
        Assert.Equal(1, spans[1].End);
    }

    [Fact]
    public void Models_of_the_same_maker_keep_their_own_spans()
    {
        var spans = SelectedModelBlend.Spans([
            new("anthropic/opus", true), new("anthropic/sonnet", false), new("anthropic/haiku", false)]);
        Assert.Equal(["anthropic/sonnet", "anthropic/haiku", "anthropic/opus"], spans.Select(span => span.ModelId));
        Assert.Equal(1.0 / 3.6, spans[0].End, 12);
        Assert.Equal(2.0 / 3.6, spans[1].End, 12);
    }

    [Fact]
    public void A_solo_model_fills_the_tube_and_an_empty_selection_has_no_spans()
    {
        var span = Assert.Single(SelectedModelBlend.Spans([new("solo", true)]));
        Assert.Equal(0, span.Start);
        Assert.Equal(1, span.End);
        Assert.Empty(SelectedModelBlend.Spans([]));
    }
}
