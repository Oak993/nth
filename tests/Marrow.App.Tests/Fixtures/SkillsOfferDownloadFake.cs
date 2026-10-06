using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Offers;
namespace Marrow.App.Tests.Fixtures;
// In-memory only. An unconfigured invocation fails closed.
public sealed class SkillsOfferDownloadFake : ISkillsOfferDownload
{
    public int AttachCalls { get; private set; }
    public int Calls { get; private set; }
    public Func<IReadOnlyList<CatalogueItem>, Action<OfferProgress>, CancellationToken, Task<IReadOnlyDictionary<string, string>>>? Script { get; set; }
    public Task<IReadOnlyDictionary<string, string>> Install(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        Calls++;
        return (Script ?? throw new InvalidOperationException("Offline download script was not bound."))(items, progress, cancellation);
    }
    public Task<IReadOnlyDictionary<string, string>> Attach(IReadOnlyList<CatalogueItem> items, Action<OfferProgress> progress, CancellationToken cancellation)
    {
        AttachCalls++;
        return (Script ?? throw new InvalidOperationException("No offline attachment was bound."))(items, progress, cancellation);
    }
}
