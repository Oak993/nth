// PORT-ONLY: RR-16 / OD-0099, skills-ONDEMAND-PLAN 4.3 and skills-PLAN 2.4.
using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Pages;

/// <summary>Read-only provider observations. Only a reported skill path or Claude's observed sync root
/// establishes a skill. Ordinary slash commands, managed installs and imported copies are never account facts.
/// Synced files are read again on each snapshot, so removal or a changed description cannot stay cached.</summary>
public sealed class ProviderSkillReports(Func<string> home, string dataRoot) : IProviderSkills
{
    private sealed record Observation(string Label, IReadOnlyList<SlashCommand> Commands);
    private readonly object gate = new();
    private readonly Dictionary<(string Provider, string Directory), Observation> observations = [];
    private readonly string data = Path.GetFullPath(dataRoot);

    public void Observe(string provider, string label, string directory, IEnumerable<SlashCommand> commands)
    {
        GenerationFilters.RequireProvider(provider);
        var snapshot = Array.AsReadOnly(commands.Where(c => c.SkillPath is not null).ToArray());
        lock (gate) observations[(provider, Path.GetFullPath(directory))] = new(label, snapshot);
    }

    public IReadOnlyList<ExistingSkill> Existing(string provider, string directory)
    {
        Observation? observation;
        lock (gate) observations.TryGetValue((provider, Path.GetFullPath(directory)), out observation);
        return observation is null ? [] : Read(provider, observation);
    }

    public IReadOnlyList<ProviderSkillGroup> Report()
    {
        KeyValuePair<(string Provider, string Directory), Observation>[] snapshot;
        lock (gate) snapshot = observations.ToArray();
        return snapshot.GroupBy(p => p.Key.Provider, StringComparer.Ordinal).Select(group =>
            new ProviderSkillGroup(group.First().Value.Label, Array.AsReadOnly(group.SelectMany(p => Read(p.Key.Provider, p.Value))
                .DistinctBy(s => (s.Name, s.SourcePath)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray())))
            .Where(g => g.Skills.Count > 0).OrderBy(g => g.Provider, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<ExistingSkill> Read(string provider, Observation observation)
    {
        var userHome = home();
        if (!Path.IsPathFullyQualified(userHome)) return [];
        var result = new List<ExistingSkill>();
        foreach (var command in observation.Commands)
        {
            if (command.Name.StartsWith("marrow:", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(command.SkillPath)
                || !Path.IsPathFullyQualified(command.SkillPath)) continue;
            var path = Path.GetFullPath(command.SkillPath);
            if (StorePaths.Comparer.Equals(path, data) || StorePaths.Under(path, data)) continue;
            var origin = Origin(provider, userHome, path);
            // Synced account claims come only from the current readable sync tree below.
            if (origin == SkillOrigin.AccountSynced) continue;
            // The listing itself establishes a skill even if the provider owns a virtual/unreadable path.
            result.Add(new(command.Name, command.Detail)
            {
                SourcePath = path,
                Origin = origin,
            });
        }
        if (provider == "claude")
        {
            var synced = Path.Combine(userHome, ".claude", "skills", "synced");
            try
            {
                StorePaths.EnsureAncestors(synced);
                if (Directory.Exists(synced))
                    foreach (var file in StoreTrees.Entries(synced).Where(f => Path.GetFileName(f) == "SKILL.md"))
                    {
                        try
                        {
                            StorePaths.EnsureAncestors(file);
                            if (new FileInfo(file).Length > 256 * 1024) continue;
                            var disclosure = SkillFrontmatter.Disclosure(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file)));
                            if (!disclosure.Name.StartsWith("marrow:", StringComparison.OrdinalIgnoreCase))
                                result.Add(disclosure with { Origin = SkillOrigin.AccountSynced, SourcePath = file });
                        }
                        catch (Exception error) when (Unreadable(error)) { }
                    }
            }
            catch (Exception error) when (Unreadable(error)) { } // missing/unreadable origin never becomes an account claim
        }
        return Array.AsReadOnly(result.DistinctBy(s => (s.Name, s.SourcePath)).ToArray());
    }

    private static bool Unreadable(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or ArgumentException;

    private static SkillOrigin Origin(string provider, string userHome, string path)
    {
        bool Under(string relative) => StorePaths.Under(path, Path.Combine(userHome, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (provider == "claude" && Under(".claude/skills/synced")) return SkillOrigin.AccountSynced;
        if (provider == "codex" && Under(".codex/skills/.system")) return SkillOrigin.ProviderSystem;
        if ((provider == "codex" && Under(".codex/plugins/cache")) || (provider == "claude" && Under(".claude/plugins/cache"))) return SkillOrigin.ProviderPlugin;
        if ((provider == "codex" && (Under(".codex/skills") || Under(".agents/skills"))) || (provider == "claude" && Under(".claude/skills"))) return SkillOrigin.User;
        return SkillOrigin.ProviderReported;
    }
}
