// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

public static class GenerationFilters
{
    private static readonly string[][] Groups = [["docx", "documents", "doc"], ["xlsx", "spreadsheets", "spreadsheet"], ["pptx", "presentations", "slides"], ["pdf"]];
    private static readonly string[] CursorReserved = ["automate", "autopilot", "canvas", "create-hook", "create-rule", "create-skill", "create-subagent", "cursor-blame", "loop", "migrate-to-skills", "review", "review-bugbot", "review-security", "sdk", "shell", "split-to-prs", "statusline", "update-cli-config", "update-cursor-settings"];
    public static bool PluginLoading(string provider) => provider is "claude" or "grok";
    public static void RequireProvider(string provider)
    {
        if (provider is not ("claude" or "codex" or "copilot" or "opencode" or "opencode-go" or "pi" or "commandcode" or "grok" or "agy" or "cursor" or "devin" or "native"))
            throw new ArgumentException("An explicit supported generation provider is required.", nameof(provider));
    }
    public static bool Allows(string provider, GenerationSkill skill)
    {
        if (skill.Name == "skill-creator")
        {
            if (provider == "codex") return false;
            if (skill.ItemId == "A1") return provider is "claude" or "grok";
            if (skill.ItemId == "O1") return provider is not ("claude" or "grok");
            return false; // Unknown source needs a reviewed rule, never silently pick one by ordering.
        }
        if (skill.Name is "define-goal" or "hatch-pet" or "playwright-interactive" or "plugin-creator" && provider != "codex") return false;
        if (skill.Name == "academy-guide" && provider != "claude") return false;
        if (skill.Name == "claude-api" && provider == "claude") return false;
        if (skill.Name is "review-agent" or "imagegen" or "openai-docs" && provider == "codex") return false;
        return !skill.ExplicitOnly || provider is "claude" or "codex" or "pi" or "native" or "cursor" or "devin" or "agy";
    }
    public static string Normalize(string name) => name[(name.LastIndexOf(':') + 1)..].ToLowerInvariant();
    // RR-16: equivalence is a comparison rule, never evidence that an account supplied a skill.
    public static IReadOnlyList<string> Equivalents(string name)
    {
        var normalized = Normalize(name);
        return Array.AsReadOnly((Groups.FirstOrDefault(g => g.Contains(normalized, StringComparer.Ordinal)) ?? [normalized]).Order(StringComparer.Ordinal).ToArray());
    }
    public static bool Clashes(string provider, string name, IEnumerable<ExistingSkill> existing)
    {
        name = Normalize(name);
        var names = existing.Where(s => !s.Name.StartsWith("marrow:", StringComparison.OrdinalIgnoreCase)).Select(s => Normalize(s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (provider is "opencode" or "opencode-go") names.Add("customize-opencode");
        if (provider == "cursor") names.UnionWith(CursorReserved);
        if (provider == "commandcode") names.UnionWith(["skill-builder", "design", "loop"]);
        return names.Contains(name) || Groups.Any(g => g.Contains(name, StringComparer.OrdinalIgnoreCase) && g.Any(names.Contains));
    }
}
