// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>A frozen installed pin and its reviewed disclosure metadata; plugin root is relative to the store (empty means its root).</summary>
public sealed class GenerationItem
{
    public GenerationItem(StoreItem store, IEnumerable<GenerationSkill> skills, string? pluginRoot = null)
    {
        Store = store;
        Skills = Array.AsReadOnly(skills.ToArray());
        PluginRoot = pluginRoot;
        var roots = pluginRoot is null ? [] : new[] { pluginRoot };
        if (pluginRoot == "" && !store.Manifest.Files.Any(f => f.Path is ".claude-plugin/plugin.json" or ".codex-plugin/plugin.json" or ".cursor-plugin/plugin.json"))
        {
            roots = store.Manifest.Files.Where(f => f.Path.EndsWith("/.claude-plugin/plugin.json", StringComparison.Ordinal) ||
                f.Path.EndsWith("/.codex-plugin/plugin.json", StringComparison.Ordinal) || f.Path.EndsWith("/.cursor-plugin/plugin.json", StringComparison.Ordinal))
                .Select(f => f.Path[..f.Path.LastIndexOf("/.", StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (roots.Length == 0) throw new ArgumentException("The pinned plugin set has no declared plugin roots.");
        }
        PluginRoots = Array.AsReadOnly(roots);
        if (store.Catalogue.Kind is not (ItemKind.Skillset or ItemKind.Plugin)) throw new ArgumentException("Only skill sets and plugins make generations.");
        if ((store.Catalogue.Kind == ItemKind.Plugin) != (pluginRoot is not null)) throw new ArgumentException("Plugin roots must be explicit.");
        if (!string.IsNullOrEmpty(pluginRoot)) StoreManifest.RequireRelative(pluginRoot);
        foreach (var s in Skills)
        {
            StoreManifest.RequireRelative(s.SourcePath);
            if (!Enum.IsDefined(s.Group) || s.ItemId != store.Catalogue.Id || !SkillName.IsValid(s.Name) || string.IsNullOrWhiteSpace(s.Description) || s.Description.Any(char.IsControl))
                throw new ArgumentException("Disclosure metadata must name a reviewed catalogue skill with a one-line description.");
            if (!store.Catalogue.Skills.Any(c => c.Path == s.SourcePath && c.Name == s.Name)) throw new ArgumentException("Metadata is not in the installed catalogue.");
            if (!store.Manifest.Files.Any(f => f.Path == s.SourcePath + "/SKILL.md")) throw new ArgumentException("The pinned skill file is missing.");
            if (pluginRoot is not null && pluginRoot.Length != 0 && !s.SourcePath.StartsWith(pluginRoot + "/", StringComparison.Ordinal))
                throw new ArgumentException("The skill is outside its plugin root.");
        }
        if (pluginRoot is not null && Skills.Any(s => PluginRoots.Count(r => r.Length == 0 || s.SourcePath.StartsWith(r + "/", StringComparison.Ordinal)) != 1))
            throw new ArgumentException("Every plugin skill must belong to exactly one pinned plugin root.");
        if (Skills.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Skills.Count || Skills.Count != store.Catalogue.Skills.Count)
            throw new ArgumentException("Every installed skill requires exactly one reviewed metadata entry.");
        var declared = Skills.Select(s => s.SourcePath + "/SKILL.md").ToHashSet(StringComparer.Ordinal);
        if (store.Manifest.Files.Any(f => f.Path.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase) && !declared.Contains(f.Path)))
            throw new ArgumentException("An undeclared skill could bypass the enabled switches.");
    }
    public StoreItem Store { get; }
    public IReadOnlyList<GenerationSkill> Skills { get; }
    public string? PluginRoot { get; }
    public IReadOnlyList<string> PluginRoots { get; }
}
