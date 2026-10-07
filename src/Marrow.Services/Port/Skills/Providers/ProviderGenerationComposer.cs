// PORT-ONLY: RR-15 / SK-11a..f. One host builds provider-specific managed and imported snapshots.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Store;
using System.Text.Json;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderGenerationComposer
{
    public static Generation? Prepare(SkillsHost host, string provider, string chat, string directory, string home,
        GenerationBudget budget, IReadOnlyDictionary<string, string> environment)
    {
        GenerationFilters.RequireProvider(provider);
        var extra = new List<string>();
        string? providerHome = null;
        string? Value(string key) => environment.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
        if (Value("HOME") is { } exportedHome)
        {
            if (!Path.IsPathFullyQualified(exportedHome)) throw new InvalidDataException("The provider home must be absolute.");
            home = exportedHome;
        }
        var homeKey = provider switch { "codex" => "CODEX_HOME", "copilot" => "COPILOT_HOME", "pi" => "PI_CODING_AGENT_DIR", "grok" => "GROK_HOME", _ => null };
        if (homeKey is not null) providerHome = Value(homeKey);
        if (provider is "opencode" or "opencode-go")
        {
            var launch = new OpenCodeSkillLaunch(Path.Combine(host.Paths.Views, provider, "chat-" + chat), directory, host.Paths.Root, home);
            var paths = OpenCodeSkillPaths.Resolve(environment, launch, Value("OPENCODE_CONFIG_CONTENT"));
            if (paths.LeftOut) return null; // WU-9: unknown ordering leaves this session's additional skills out
            extra.AddRange(paths.Paths!);
            if (Value("XDG_CONFIG_HOME") is { } config) extra.Add(Path.Combine(config, "opencode", "skills"));
        }
        if (provider == "commandcode")
        {
            // Read only, whole-array precedence from research-loading's Command Code row.
            string[]? configured = null;
            foreach (var file in new[] { Path.Combine(home, ".commandcode", "settings.json"),
                Path.Combine(directory, ".commandcode", "settings.json"), Path.Combine(directory, ".commandcode", "settings.local.json") })
            {
                StorePaths.EnsureAncestors(file);
                if (!File.Exists(file)) continue;
                if (new FileInfo(file).Length > 256 * 1024) throw new InvalidDataException("The provider settings are too large to resolve safely.");
                using var document = JsonDocument.Parse(File.ReadAllBytes(file));
                if (!document.RootElement.TryGetProperty("skills", out var skills)) continue;
                if (skills.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The provider skills setting must be a path array.");
                configured = skills.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                    ? value.GetString()! : throw new InvalidDataException("The provider skill path must be a string.")).ToArray();
            }
            extra.AddRange(configured ?? []);
        }
        return AntigravitySkillsComposer.PrepareForProvider(host, provider, chat, directory, home, budget, extra, providerHome);
    }
}
