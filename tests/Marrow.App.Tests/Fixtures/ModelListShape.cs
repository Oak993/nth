using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-stutterfix-2: a model list's rows as ModelCatalog.Entries gives them, each Height tall and 1 apart.
internal sealed record ModelListShape(IReadOnlyList<ModelCatalog.Entry> Entries, double Height)
{
    public int Count => Entries.Count;
    public double Pitch => Height + 1; // rows 1 apart
    public double Extent => Count * Pitch - 1;
    public IEnumerable<string> Names(int start, int count) => Entries.Skip(start).Take(count).Select(e => e.Option.ShortName);
}
