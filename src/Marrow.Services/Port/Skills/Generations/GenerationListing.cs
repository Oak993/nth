// PORT-ONLY: SK-9 PLAN 2.1 read-only catalogue probes share one SK-5 lease with reference counting; sessions never receive it.
namespace Marrow.Services.Port.Skills.Generations;

public sealed class GenerationListing : IDisposable
{
    private readonly Generation generation;
    private readonly Action release;
    private bool disposed;
    internal GenerationListing(Generation generation, Action release) { this.generation = generation; this.release = release; }
    public string Folder { get { ObjectDisposedException.ThrowIf(disposed, this); return generation.Folder; } }
    public IReadOnlyList<PublishedSkill> Skills => generation.Skills;
    public string ForChat(string listedPath, Generation chat)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var listed = Skills.SingleOrDefault(s => Store.StorePaths.Comparer.Equals(s.File, listedPath));
        if (listed is null) return listedPath; // A user's own path is preserved unchanged.
        return chat.Skills.Single(s => s.Id == listed.Id).File;
    }
    public void Dispose() { if (disposed) return; disposed = true; release(); }
}
