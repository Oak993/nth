// PORT-ONLY: first-release feature unit SK-16 (DL-19). Stub for the red run; replaced below.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Port.Skills.Fetch;

public sealed class SkillsDownloads(SkillsPipeline pipeline, SkillsCatalogue catalogue)
{
    public bool Available => throw new NotImplementedException();
    public bool CanFetch(CatalogueItem item) => throw new NotImplementedException();
    public Task<string> Install(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => throw new NotImplementedException();
    public bool IsRunning(string id) => throw new NotImplementedException();
    public FetchView? Running(string id) => throw new NotImplementedException();
    public IDisposable Watch(Action changed) => throw new NotImplementedException();
}
