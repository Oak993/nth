// PORT-ONLY: OD-0048/0051's approved N-model stretch geometry; ED-613.
// This is renderer-independent geometry, not a replacement for the locked liquid/ink renderer.
namespace Marrow.Core.Port.ModelsPalette;

public readonly record struct SelectedModel(string ModelId, bool IsLead);
public readonly record struct SelectedModelSpan(string ModelId, bool IsLead, double Start, double End);

public static class SelectedModelBlend
{
    /// <summary>
    /// Workers keep their selection order at the left; leads follow, with the first lead right against
    /// the knob. Every selected model keeps a stretch, including models with the same maker.
    /// A lead has 1.6 times a worker's share (OD-0051), represented exactly as integer weights 8 and 5.
    /// </summary>
    public static ValueList<SelectedModelSpan> Spans(IReadOnlyList<SelectedModel> selection)
    {
        if (selection.Count == 0) return ValueList<SelectedModelSpan>.Empty;
        var ordered = selection.Where(model => !model.IsLead)
            .Concat(selection.Where(model => model.IsLead).Reverse()).ToArray();
        var total = ordered.Sum(model => model.IsLead ? 8L : 5L);
        long edge = 0;
        return new ValueList<SelectedModelSpan>(ordered.Select(model =>
        {
            var start = edge / (double)total;
            edge += model.IsLead ? 8 : 5;
            return new SelectedModelSpan(model.ModelId, model.IsLead, start, edge / (double)total);
        }));
    }
}
