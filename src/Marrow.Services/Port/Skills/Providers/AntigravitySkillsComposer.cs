// PORT-ONLY: SK-17 / OD-0099 Q-SKILLS-8 A. Read-only launch composition over SK-13's single host.
using System.Security.Cryptography;
using System.Text;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class AntigravitySkillsComposer
{
    public static Generation Prepare(SkillsHost host, string chat, string directory, string home, GenerationBudget budget)
        => PrepareForProvider(host, "agy", chat, directory, home, budget);

    internal static Generation PrepareForProvider(SkillsHost host, string provider, string chat, string directory, string home, GenerationBudget budget)
        => PrepareForProvider(host, provider, chat, directory, home, budget, [], null);

    internal static Generation PrepareForProvider(SkillsHost host, string provider, string chat, string directory, string home, GenerationBudget budget,
        IEnumerable<string> extraPaths, string? providerHome)
    {
        var items = new List<GenerationItem>();
        var held = new List<StoreLease>();
        try
        {
            foreach (var item in host.Pipeline.InstalledSkillItems())
            {
                var lease = host.Pipeline.AcquireForViewBuild(item.Catalogue.Id);
                if (lease is null) continue; // The pipeline marks a damaged installed pin; it is never disclosed.
                held.Add(lease);
                if (!string.Equals(lease.Folder, item.Folder, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The installed skill pin changed during launch preparation.");
                var skills = item.Catalogue.Skills.Select(s =>
                {
                    var path = s.Path ?? throw new InvalidDataException("A pinned skill is missing its source path.");
                    var file = item.Manifest.Files.Single(f => f.Path == path + "/SKILL.md");
                    var source = Path.Combine(item.Folder, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    StorePaths.EnsureAncestors(source);
                    var bytes = File.ReadAllBytes(source);
                    if (bytes.LongLength != file.Size || !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Pinned skill disclosure changed during launch preparation.");
                    var disclosure = SkillFrontmatter.Disclosure(new UTF8Encoding(false, true).GetString(bytes));
                    if (s.Name is not null && s.Name != disclosure.Name) throw new InvalidDataException("Pinned skill name differs from the catalogue.");
                    return new GenerationSkill(item.Catalogue.Id, disclosure.Name, path, disclosure.Description,
                        ReviewedGroup(disclosure.Name), 0, false);
                }).ToArray();
                items.Add(new GenerationItem(item, skills, item.Catalogue.Kind == Marrow.Core.Port.Skills.Catalogue.ItemKind.Plugin ? "" : null));
            }
            var switches = SkillSwitches.Load(host.Paths.EnabledFile);
            if (string.IsNullOrWhiteSpace(switches.Version))
                switches = new(host.Pipeline.CatalogueVersion, switches.Skills, switches.Plugins);
            var existing = new SkillClashScanner(home, directory, RepositoryRoot(directory)).Scan(provider, extraPaths,
                host.ProviderSkills.Existing(provider, directory), providerHome: providerHome);
            return host.Generations.Start(provider, chat, items, switches, budget, existing);
        }
        finally { foreach (var lease in held) lease.Dispose(); }
    }

    // Exact membership from skills-PLAN 1.2 Default-on set (Q1), not inferred from description text.
    private static SkillGroup ReviewedGroup(string name) => name switch
    {
        "pdf" or "doc" or "spreadsheet" => SkillGroup.Documents,
        "review-agent" => SkillGroup.Review,
        "security-best-practices" or "security-threat-model" => SkillGroup.Security,
        "gh-address-comments" or "gh-fix-ci" => SkillGroup.GitHub,
        "frontend-design" or "canvas-design" or "theme-factory" or "internal-comms" => SkillGroup.Design,
        "skill-creator" or "webapp-testing" or "mcp-builder" or "claude-api" => SkillGroup.SkillBuilding,
        _ => SkillGroup.Other,
    };

    private static string RepositoryRoot(string directory)
    {
        for (var candidate = Path.GetFullPath(directory); candidate is not null; candidate = Path.GetDirectoryName(candidate))
            if (Path.Exists(Path.Combine(candidate, ".git"))) return candidate;
        return Path.GetFullPath(directory);
    }
}
