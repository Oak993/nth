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
    public GenerationFixture(bool windows = false)
    {
        fixture = new(windows);
        Protection = new SerializedGenerationProtection(fixture.Protection);
        Trees = new StoreTrees(Protection);
    }
    public StorePaths Paths => fixture.Paths;
    public IStoreProtection Protection { get; }
    public StoreTrees Trees { get; }
    public FakeStoreProtection Fake => fixture.Fake;
    public GenerationBudget Budget { get; } = new(8000);
    public SkillsStore? Store { get; private set; }
    public SkillGenerations? Service { get; private set; }

    public GenerationItem Add(string id, string name, bool installed = true, bool plugin = false, int priority = 0, bool explicitFlag = true, string? secondName = null, bool nestedPlugins = false)
    {
        var folder = Path.Combine(Paths.Store, id + "-pin");
        var relative = (nestedPlugins ? "first/" : "") + (plugin ? "skills/" : "") + name;
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [relative + "/SKILL.md"] = Encoding.UTF8.GetBytes($"---\nname: {name}\ndescription: Fixture description\n{(explicitFlag && name is ("screenshot" or "yeet" or "review-agent") ? "disable-model-invocation: true\n" : "")}---\nBODY SENTINEL\n"),
            [relative + "/references/example.txt"] = Encoding.UTF8.GetBytes("REFERENCE SENTINEL"),
            ["LICENSE.txt"] = Encoding.UTF8.GetBytes("Marrow fixture")
        };
        if (plugin)
        {
            var pluginPrefix = nestedPlugins ? "first/" : "";
            content[pluginPrefix + ".claude-plugin/plugin.json"] = Encoding.UTF8.GetBytes("{\"name\":\"fixture\"}");
            content[pluginPrefix + "hooks/hooks.json"] = Encoding.UTF8.GetBytes("{}");
            content[pluginPrefix + ".mcp.json"] = Encoding.UTF8.GetBytes("{}");
            content[pluginPrefix + "commands/sample.md"] = Encoding.UTF8.GetBytes("Fixture command");
        }
        var skillList = new List<CatalogueSkill> { new() { Folder = name, Name = name, Path = relative, Needs = [] } };
        var metadata = new List<GenerationSkill> { new(id, name, relative, "Fixture description", SkillGroup.Documents, priority, false) };
        if (secondName is not null)
        {
            var second = (nestedPlugins ? "second/" : "") + (plugin ? "skills/" : "") + secondName;
            content[second + "/SKILL.md"] = Encoding.UTF8.GetBytes($"---\nname: {secondName}\ndescription: Fixture description\n---\nSECOND BODY\n");
            if (nestedPlugins)
            {
                content["second/.claude-plugin/plugin.json"] = Encoding.UTF8.GetBytes("{\"name\":\"second-fixture\"}");
                content["second/.mcp.json"] = Encoding.UTF8.GetBytes("{}");
            }
            skillList.Add(new() { Folder = secondName, Name = secondName, Path = second, Needs = [] });
            metadata.Add(new(id, secondName, second, "Fixture description", SkillGroup.Documents, priority, false));
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
            ApproxSizeMb = null, Needs = [], Provides = [], Artifacts = [], Files = files, SkillsListed = true, SkillCount = skillList.Count,
            Skills = skillList
        };
        Trees.Lock(folder);
        var store = new StoreItem(catalogue, folder, new(files));
        var item = new GenerationItem(store, metadata, plugin ? "" : null);
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
            AddonsState.Restore(records, [], [], []), Protection, fixture.Deletion);
        Store = store;
        return Service = new(Paths, store, Protection, _ => []);
    }
    public void Dispose() { Service?.Dispose(); fixture.Dispose(); }
}
