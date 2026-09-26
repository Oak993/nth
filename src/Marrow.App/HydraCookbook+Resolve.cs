using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App;

// Source: DroppyCode/App/HydraCookbook+Resolve.swift (B2 1fbf81e), :1-69.
public static partial class HydraCookbookResolve
{
    extension(HydraCookbook)
    {
        // :8-10
        public static bool IsReady(ProviderKind provider, ProviderRegistry registry, AppSettings settings) =>
            settings.IsEnabled(provider) && registry.Status(provider).IsInstalled;

        // :14-19
        public static ProviderKind? Provider(HydraPairRecipe.Side side, ProviderRegistry registry, AppSettings settings)
        {
            foreach (var provider in side.Providers)
            {
                if (!IsReady(provider, registry, settings)) continue;
                if (side.Models.Count == 0 || Model(side.Models, provider, registry) is not null) return provider;
            }

            return null;
        }

        // :23-28
        public static ProviderKind? MissingProvider(HydraPairRecipe recipe, ProviderRegistry registry, AppSettings settings)
        {
            foreach (var side in new[] { recipe.Lead, recipe.Heads })
            {
                var hasReadyProvider = false;
                foreach (var provider in side.Providers)
                {
                    if (!IsReady(provider, registry, settings)) continue;
                    hasReadyProvider = true;
                    break;
                }

                // :25 return side.providers.first: nil for an empty side, which ends the loop before heads (R-COLL-3).
                if (!hasReadyProvider) return side.Providers.Count > 0 ? side.Providers[0] : null;
            }

            return null;
        }

        // :32-45
        public static HydraPair? Resolve(HydraPairRecipe recipe, ProviderRegistry registry, AppSettings settings)
        {
            var leadProvider = Provider(recipe.Lead, registry, settings);
            var headsProvider = Provider(recipe.Heads, registry, settings);
            if (leadProvider is not { } lead || headsProvider is not { } heads) return null;

            var leadModel = Model(recipe.Lead.Models, lead, registry);
            var headsModel = Model(recipe.Heads.Models, heads, registry);
            return new HydraPair(lead)
            {
                OrchestratorModel = leadModel?.Id,
                OrchestratorEffort = Marrow.Core.Models.HydraCookbook.Effort(
                    recipe.Lead.Effort, leadModel ?? registry.DefaultModel(lead)),
                WorkerProvider = heads != lead ? heads : null,
                WorkerModel = headsModel?.Id,
                WorkerEffort = Marrow.Core.Models.HydraCookbook.Effort(
                    recipe.Heads.Effort, headsModel ?? registry.DefaultModel(heads)),
                MaxHeads = recipe.MaxHeads,
            };
        }

        // :49-62
        public static ModelOption? Model(ValueList<string> names, ProviderKind provider, ProviderRegistry registry)
        {
            var options = registry.Models(provider);
            foreach (var name in names)
            {
                ModelOption? best = null;
                var bestVersion = -1.0;
                foreach (var option in options)
                {
                    if (name.Length == 0 ||
                        (!SwiftString.LocalizedCaseInsensitiveContains(option.Name, name) &&
                         !SwiftString.LocalizedCaseInsensitiveContains(option.Id, name))) continue;

                    var version = Version(option.Name) ?? -1.0;
                    if (best is not null && version <= bestVersion) continue;
                    best = option;
                    bestVersion = version;
                }

                if (best is not null) return best;
            }

            return null;
        }
    }

    // :65-68
    private static double? Version(string name)
    {
        var match = VersionPattern().Match(name);
        return match.Success ? SwiftNumber.ParseDouble(match.Value) : null;
    }

    [GeneratedRegex("[0-9]+(?:\\.[0-9]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
