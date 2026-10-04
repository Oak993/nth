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
    private readonly IStoreProtection protection;
    private readonly IInstallLock sealing;
    private readonly List<StoreItem> versions = [];
    private readonly Dictionary<(string Id, string Pin), IReadOnlyList<string>> directories = [];
    private readonly InstallPlan plan;
    private readonly IReadOnlyDictionary<string, string> nextFiles;
    private SkillsCatalogue catalogue;
    public StorePaths Paths { get; }
    public CatalogueItem Old { get; }
    public CatalogueItem Next { get; }
    public int Downloads { get; private set; }
    public List<double?> Reports { get; } = [];
    public IProgress<double?> Progress => new PipelineProgress(Reports);
    public PipelineRig(bool nonOpen = false, bool windows = false, bool skillset = false, bool multiPin = false, bool emptyDirectory = false)
    {
        if (windows) { protection = new WindowsStoreProtection(); sealing = new WindowsInstallLock(); }
        else { var fake = new PipelineProtection(); protection = fake; sealing = fake; }
        Paths = new(install.Root);
        plan = install.Plan(InstallerKind.Zip, emptyDirectory ? InstallRig.Zip(("payload.txt", "offline fixture"), ("empty/", "")) : null);
        if (skillset)
        {
            var skill = "Fixture skill instructions"u8.ToArray();
            plan = plan with { Kind = InstallerKind.Skillset, Item = plan.Item with { Kind = ItemKind.Skillset, Artifacts = [], SkillCount = 1, SkillsListed = true,
                Files = plan.Item.Files.Append(new CatalogueFile { Path = "skill/SKILL.md", Size = skill.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(skill)) }).ToArray(),
                Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [] }] },
                LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["skill/SKILL.md"] = install.Input("skill", skill) },
                Spares = [new SpareProjection("claude", "fixture", ["skill/SKILL.md"]) { Prefix = "skills" }] };
        }
        Old = plan.Item with { TermsClass = nonOpen ? TermsClass.Proprietary : TermsClass.Open };
        plan = plan with { Item = Old };
        Next = Old with { Pin = "abcdef1234567890abcdef1234567890abcdef12", TermsClass = nonOpen ? TermsClass.Proprietary : TermsClass.Open };
        nextFiles = plan.LocalFiles;
        if (nonOpen)
        {
            var licence = "new fixture licence"u8.ToArray();
            Next = Next with { Files = Next.Files.Select(f => f.Path == "LICENSE" ? f with { Size = licence.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(licence)) } : f).ToArray() };
            nextFiles = new Dictionary<string, string>(plan.LocalFiles) { ["LICENSE"] = install.Input("NEXT-LICENSE", licence) };
        }
        if (multiPin) Next = Next with { Pin = null, Skills = Next.Skills.Select(s => s with { Pin = "abcdef1234567890abcdef1234567890abcdef12" }).ToArray() };
        catalogue = new() { Schema = 1, ReadOn = "2026-10-04", Items = [Next] };
    }
    public async Task<SkillsPipeline> Create(Action<PipelineStep>? crash = null, InstallStep? installCrash = null, InstallStep? hardInstallCrash = null)
    {
        Directory.CreateDirectory(Paths.Addons);
        var result = await new ItemInstaller(sealing).InstallAsync(plan);
        var old = new StoreItem(Old, result.Path, new StoreManifest(result.Manifest));
        versions.Add(old);
        directories[(Old.Id, old.Pin)] = result.Directories;
        PipelineRoutes.Prepare(Paths.StateFile, Bind(Old, result));
        var state = AddonsState.Restore([result.Record], [], [], []);
        if (Old.TermsClass != TermsClass.Open) state = state.AcceptTerms(ItemTerms.LicenceHash(Old)!, Old.Id, new DateTimeOffset(2026, 10, 4, 11, 0, 0, TimeSpan.Zero));
        return Build(state, crash, installCrash, hardInstallCrash);
    }
    private SkillsPipeline Build(AddonsState state, Action<PipelineStep>? crash = null, InstallStep? installCrash = null, InstallStep? hardInstallCrash = null) => new(Paths, catalogue, versions.GroupBy(v => (v.Catalogue.Id, v.Pin)).Select(g => g.Last()), state,
        new ItemInstaller(sealing, checkpoint: (step, _) => { if (step == hardInstallCrash) Environment.Exit(71); if (step == installCrash) throw new IOException("Injected crash"); }), protection,
        downloads: (item, record, progress, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested(); Downloads++; progress.Report(0.5);
            return Task.FromResult(plan with { Item = item, Record = record, LocalFiles = nextFiles });
        }, bindings: (item, result) =>
        {
            versions.Add(new StoreItem(item, result.Path, new StoreManifest(result.Manifest), result.Record.InstalledPin));
            directories[(item.Id, result.Record.InstalledPin!)] = result.Directories;
            return Bind(item, result);
        }, checkpoint: crash, trustedDirectories: directories);
    private static PipelineRoute Bind(CatalogueItem item, InstallResult result) => new(item.Id, UpdateCheck.CataloguePin(item)!, result.Path, result.SparePaths.ToArray(),
        item.Kind == ItemKind.Skillset ? new(StringComparer.Ordinal) : new(StringComparer.Ordinal) { [item.Id + ".exe"] = new ShimDefinition { Target = Path.Combine(result.Path, "payload.txt") } });
    public SkillsPipeline Restart() => Build(AddonsState.Empty);
    public SkillsPipeline Unbound() => new(Paths, catalogue, versions, AddonsState.Empty, new ItemInstaller(sealing), protection,
        trustedDirectories: directories);
    public async Task<SkillsPipeline> CreateDependencies()
    {
        Directory.CreateDirectory(Paths.Addons);
        var items = new List<CatalogueItem>();
        var records = new List<ItemRecord>();
        foreach (var id in new[] { "T1", "P1", "P2", "P3", "P4", "P5", "A1" })
        {
            // All use offline ZIP installation; kind is the removal graph's classification.
            var item = Old with { Id = id, Kind = id == "T1" ? ItemKind.Tool : id == "A1" ? ItemKind.Tool : ItemKind.Pyset, Needs = id == "T1" ? [] : ["T1"] };
            var result = await new ItemInstaller(sealing).InstallAsync(plan with { Item = item, Record = plan.Record with { Id = id } });
            versions.Add(new StoreItem(item, result.Path, new StoreManifest(result.Manifest), result.Record.InstalledPin));
            directories[(item.Id, result.Record.InstalledPin!)] = result.Directories;
            PipelineRoutes.Prepare(Paths.StateFile, Bind(item, result));
            items.Add(item); records.Add(result.Record);
        }
        catalogue = catalogue with { Items = items.Select(i => i.Id == "A1" ? i with { Kind = ItemKind.Skillset } : i).ToArray() };
        return Build(AddonsState.Restore(records, [], [], []));
    }
    public void Dispose()
    {
        var trees = new StoreTrees(protection);
        foreach (var root in new[] { Paths.Tools, Paths.Store, Paths.Views }) trees.Unlock(root);
        install.Dispose();
    }
}
