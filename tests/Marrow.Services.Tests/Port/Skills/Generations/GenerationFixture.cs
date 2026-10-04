// PORT-ONLY: SK-9 shared offline fixture; reuses SK-5's lock teardown and explicit protection double.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Generations;

public sealed class GenerationFixture : IDisposable
{
    private readonly StoreFixture fixture;
    private readonly List<GenerationItem> items = [];
    private readonly List<ItemRecord> records = [];
    public GenerationFixture(bool windows = false) => fixture = new(windows);
    public StorePaths Paths => fixture.Paths;
    public StoreTrees Trees => fixture.Trees;
    public FakeStoreProtection Fake => fixture.Fake;
    public GenerationBudget Budget { get; } = new(8000);
    public SkillGenerations? Service { get; private set; }

    public GenerationItem Add(string id, string name, bool installed = true, bool plugin = false, int priority = 0)
    {
        var folder = Path.Combine(Paths.Store, id + "-pin");
        var relative = (plugin ? "skills/" : "") + name;
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [relative + "/SKILL.md"] = Encoding.UTF8.GetBytes($"---\nname: {name}\ndescription: Fixture description\n---\nBODY SENTINEL\n"),
            [relative + "/references/example.txt"] = Encoding.UTF8.GetBytes("REFERENCE SENTINEL"),
            ["LICENSE.txt"] = Encoding.UTF8.GetBytes("Marrow fixture")
        };
        if (plugin)
        {
            content[".claude-plugin/plugin.json"] = Encoding.UTF8.GetBytes("{\"name\":\"fixture\"}");
            content["hooks/hooks.json"] = Encoding.UTF8.GetBytes("{}");
            content[".mcp.json"] = Encoding.UTF8.GetBytes("{}");
            content["commands/sample.md"] = Encoding.UTF8.GetBytes("Fixture command");
        }
        foreach (var (path, bytes) in content)
        {
            var dest = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, bytes);
        }
        var files = content.Select(c => new CatalogueFile { Path = c.Key, Size = c.Value.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(c.Value)) }).ToArray();
        var catalogue = new CatalogueItem
        {
            Id = id, Kind = plugin ? ItemKind.Plugin : ItemKind.Skillset, Title = "Fixture", Summary = "Offline fixture", Vendor = "Marrow",
            CountState = CountState.Plan, PinState = PinState.Pinned, Pin = new string('a', 40), SourceUrl = "https://example.invalid/fixture",
            LicenceName = "MIT", LicenceFile = "LICENSE.txt", LicenceUrl = "https://example.invalid/LICENSE.txt", TermsClass = TermsClass.Open,
            ApproxSizeMb = null, Needs = [], Provides = [], Artifacts = [], Files = files, SkillsListed = true, SkillCount = 1,
            Skills = [new() { Folder = name, Name = name, Path = relative, Needs = [] }]
        };
        Trees.Lock(folder);
        var store = new StoreItem(catalogue, folder, new(files));
        var item = new GenerationItem(store, [new GenerationSkill(id, name, relative, "Fixture description", SkillGroup.Documents, priority)], plugin ? "" : null);
        items.Add(item);
        if (installed) records.Add(new(id, ItemState.Installed, store.Pin, null, null));
        return item;
    }

    public SkillSwitches Switches(params (GenerationItem Item, bool On)[] switches) => new("fixture",
        switches.ToDictionary(s => s.Item.Skills[0].Id, s => s.On, StringComparer.Ordinal), new Dictionary<string, bool>(StringComparer.Ordinal));

    public SkillGenerations Open()
    {
        // Store descriptors are installed pins only; available items must never enter SK-5's inventory.
        var store = new SkillsStore(Paths, items.Where(i => records.Any(r => r.Id == i.Store.Catalogue.Id)).Select(i => i.Store),
            AddonsState.Restore(records, [], [], []), fixture.Protection, fixture.Deletion);
        return Service = new(Paths, store, fixture.Protection);
    }
    public void Dispose() => fixture.Dispose();
}
