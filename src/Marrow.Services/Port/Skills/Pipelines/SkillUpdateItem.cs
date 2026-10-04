using Marrow.Core.Port.Updates;
namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-8 adapter for UE-2's per-item Skills and tools group, ONDEMAND 3.7 / OD-0100.
public sealed class SkillUpdateItem(SkillsPipeline pipeline, string id, string title) : IUpdateItem
{
    public string Id => id;
    public string Title => title;
    public UpdateItemKind Kind => UpdateItemKind.SkillsAndTools;
    public Task<string?> InstalledVersion(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(pipeline.State.Get(id).InstalledPin); }
    public Task<string?> LatestVersion(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(pipeline.LatestPin(id)); }
    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        var before = pipeline.State.Get(id).InstalledPin;
        try
        {
            await pipeline.UpdateAsync(id, progress, cancellation).ConfigureAwait(false);
            var after = pipeline.State.Get(id).InstalledPin;
            return new(true, after, before != after, false, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(false, before, false, false, error.Message); }
    }
}
