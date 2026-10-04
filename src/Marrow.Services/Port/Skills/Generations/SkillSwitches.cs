// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.
using System.Collections.Frozen;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>PLAN WU-14: source-qualified switches avoid the two vendors' skill-creator collision (ED-324).</summary>
public sealed class SkillSwitches
{
    public SkillSwitches(string version, IReadOnlyDictionary<string, bool> skills, IReadOnlyDictionary<string, bool> plugins)
    {
        Version = version;
        Skills = skills.ToFrozenDictionary(StringComparer.Ordinal);
        Plugins = plugins.ToFrozenDictionary(StringComparer.Ordinal);
    }
    public static SkillSwitches Empty { get; } = new("", new Dictionary<string, bool>(), new Dictionary<string, bool>());
    public string Version { get; }
    public IReadOnlyDictionary<string, bool> Skills { get; }
    public IReadOnlyDictionary<string, bool> Plugins { get; }
    public bool Enabled(GenerationSkill skill) => Skills.TryGetValue(skill.Id, out var on) ? on : skill.DefaultOn;
    public bool PluginEnabled(string id) => Plugins.TryGetValue(id, out var on) && on;
    public bool PluginEnabled(string id, string root) => Plugins.TryGetValue(id + "/" + root, out var on) ? on : PluginEnabled(id);
    public SkillSwitches WithSkill(GenerationSkill skill, bool on) => new(Version, Skills.Where(p => p.Key != skill.Id).Append(new(skill.Id, on)).ToDictionary(), Plugins);
    public SkillSwitches WithPlugin(string id, bool on) => new(Version, Skills, Plugins.Where(p => p.Key != id && (id.Contains('/', StringComparison.Ordinal) || !p.Key.StartsWith(id + "/", StringComparison.Ordinal))).Append(new(id, on)).ToDictionary());
    public SkillSwitches Reconcile(string version, IEnumerable<GenerationItem> items)
    {
        var list = items.ToArray();
        return new(version, list.SelectMany(i => i.Skills).ToDictionary(s => s.Id, Enabled, StringComparer.Ordinal),
            list.SelectMany(i => i.PluginRoots.Select(r => new KeyValuePair<string, bool>(i.Store.Catalogue.Id + "/" + r, PluginEnabled(i.Store.Catalogue.Id, r)))).ToDictionary(StringComparer.Ordinal));
    }
    public void Save(string file)
    {
        StorePaths.EnsureAncestors(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        FileOps.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(new EnabledSkillsData(Version, Skills, Plugins)));
    }
    public static SkillSwitches Load(string file)
    {
        StorePaths.EnsureAncestors(file);
        if (!File.Exists(file)) return Empty;
        var data = JsonSerializer.Deserialize<EnabledSkillsData>(File.ReadAllBytes(file)) ?? throw new InvalidDataException("Invalid skills switches.");
        if (data.Version is null || data.Skills is null || data.Plugins is null) throw new InvalidDataException("Incomplete skills switches.");
        return new(data.Version, data.Skills, data.Plugins);
    }
}
