using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
internal sealed class PipelineRig : IDisposable
{
    private readonly InstallRig install = new();
    private readonly PipelineProtection protection = new();
    private readonly List<StoreItem> versions = [];
    private readonly InstallPlan plan;
    private SkillsCatalogue catalogue;
    public StorePaths Paths { get; }
    public CatalogueItem Old { get; }
    public CatalogueItem Next { get; }
    public int Downloads { get; private set; }
    public List<double?> Reports { get; } = [];
    public IProgress<double?> Progress => new PipelineProgress(Reports);
    public PipelineRig(bool nonOpen = false)
    {
        Paths = new(install.Root);
        plan = install.Plan(InstallerKind.Zip);
        Old = plan.Item;
        Next = Old with { Pin = "abcdef1234567890abcdef1234567890abcdef12", TermsClass = nonOpen ? TermsClass.Proprietary : TermsClass.Open };
        catalogue = new() { Schema = 1, ReadOn = "2026-10-04", Items = [Next] };
    }
    public async Task<SkillsPipeline> Create(Action<PipelineStep>? crash = null, InstallStep? installCrash = null)
    {
        Directory.CreateDirectory(Paths.Addons);
        var result = await new ItemInstaller(protection).InstallAsync(plan);
        var old = new StoreItem(Old, result.Path, new StoreManifest(result.Manifest));
        versions.Add(old);
        PipelineRoutes.Prepare(Paths.StateFile, Bind(Old, result));
        var state = AddonsState.Restore([result.Record], [], [], []);
        return Build(state, crash, installCrash);
    }
    private SkillsPipeline Build(AddonsState state, Action<PipelineStep>? crash = null, InstallStep? installCrash = null) => new(Paths, catalogue, versions, state,
        new ItemInstaller(protection, checkpoint: (step, _) => { if (step == installCrash) throw new IOException("Injected crash"); }), protection,
        downloads: (item, record, progress, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested(); Downloads++; progress.Report(0.5);
            return Task.FromResult(plan with { Item = item, Record = record });
        }, bindings: (item, result) =>
        {
            versions.Add(new StoreItem(item, result.Path, new StoreManifest(result.Manifest)));
            return Bind(item, result);
        }, checkpoint: crash);
    private static PipelineRoute Bind(CatalogueItem item, InstallResult result) => new(item.Id, item.Pin!, result.Path, result.SparePaths.ToArray(),
        new(StringComparer.Ordinal) { [item.Id + ".exe"] = new ShimDefinition { Target = Path.Combine(result.Path, "payload.txt") } });
    public SkillsPipeline Restart() => Build(AddonsState.Empty);
    public async Task<SkillsPipeline> CreateDependencies()
    {
        Directory.CreateDirectory(Paths.Addons);
        var items = new List<CatalogueItem>();
        var records = new List<ItemRecord>();
        foreach (var id in new[] { "T1", "P1", "P2", "P3", "P4", "P5", "A1" })
        {
            // All use offline ZIP installation; kind is the removal graph's classification.
            var item = Old with { Id = id, Kind = id == "T1" ? ItemKind.Tool : id == "A1" ? ItemKind.Tool : ItemKind.Pyset, Needs = id == "T1" ? [] : ["T1"] };
            var result = await new ItemInstaller(protection).InstallAsync(plan with { Item = item, Record = plan.Record with { Id = id } });
            versions.Add(new StoreItem(item, result.Path, new StoreManifest(result.Manifest)));
            PipelineRoutes.Prepare(Paths.StateFile, Bind(item, result));
            items.Add(item); records.Add(result.Record);
        }
        catalogue = catalogue with { Items = items.Select(i => i.Id == "A1" ? i with { Kind = ItemKind.Skillset } : i).ToArray() };
        return Build(AddonsState.Restore(records, [], [], []));
    }
    public void Dispose() => install.Dispose();
}
