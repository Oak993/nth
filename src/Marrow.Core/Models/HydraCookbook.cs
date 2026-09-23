using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/HydraCookbook.swift (B2 1fbf81e), :1-53.

// :7-17 `struct HydraPairRecipe: Identifiable, Sendable { struct Side: Sendable { ... } ... }`.
public sealed record HydraPairRecipe
{
    // :8-17 `struct Side: Sendable`.
    public sealed record Side
    {
        // :9-11 "Providers that serve this side's model, best first: the model's own house
        // ahead of aggregators, so a lead on Claude's own CLI wins over one relayed."
        public required ValueList<ProviderKind> Providers { get; init; } // :11

        // :12-14 "Names to look for in the provider's catalog, best first; a recipe whose
        // names find nothing is not offered."
        public required ValueList<string> Models { get; init; } // :14

        // :15 "The effort asked for, kept only when the model found offers it."
        public string? Effort { get; init; } // :16
    }

    public required string Id { get; init; } // :19
    public required string Title { get; init; } // :20
    public required string Tagline { get; init; } // :21
    public required Side Lead { get; init; } // :22
    public required Side Heads { get; init; } // :23
    public long? MaxHeads { get; init; } // :24
}

// :27-52 `enum HydraCookbook`.
public static class HydraCookbook
{
    // :28-29 "The best pairs, strongest first: cross-model pairs, then one model at two
    // efforts." `static let recipes: [HydraPairRecipe] = [...]`.
    // :30-39 the ten recipes, transcribed byte for byte.
    public static ValueList<HydraPairRecipe> Recipes { get; } = new HydraPairRecipe[]
    {
        // :30
        new()
        {
            Id = "claude-fable-opus",
            Title = "Fable 5.1 leads Opus 5",
            Tagline = "The deepest lead over the strongest builders: Fable plans and checks, Opus heads build. Set the thinking effort of each side yourself.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Fable" }),
                Effort = null,
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = null,
            },
            MaxHeads = null,
        },
        // :31
        new()
        {
            Id = "claude-opus-gemini-flash",
            Title = "Opus 5 leads Gemini 3.8 Flash",
            Tagline = "A careful lead and a wide, fast team: Opus writes the briefs, six Gemini Flash heads sprint through them.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Antigravity, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Gemini 3.8 Flash", "Gemini", "Flash" }),
                Effort = "medium",
            },
            MaxHeads = 6,
        },
        // :32
        new()
        {
            Id = "claude-opus-deepseek",
            Title = "Opus 5 leads DeepSeek V4.1",
            Tagline = "Opus keeps the plan tight; DeepSeek V4.1 Flash heads keep the bill small on the routine parts.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Deepseek, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "V4.1 Flash", "V4.1", "DeepSeek" }),
                Effort = "high",
            },
            MaxHeads = null,
        },
        // :33
        new()
        {
            Id = "claude-opus-glm",
            Title = "Opus 5 leads GLM-5.3 Flash",
            Tagline = "Opus keeps the plan tight; GLM-5.3 Flash heads on a Z.ai Coding Plan keep the bill flat.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Zai, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "5.3 Flash", "GLM" }),
                Effort = "high",
            },
            MaxHeads = null,
        },
        // :34
        new()
        {
            Id = "claude-fable-composer",
            Title = "Fable 5.1 leads Composer 2.5",
            Tagline = "Fable thinks; Cursor's Composer heads type at the speed Composer is known for.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Fable" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Cursor }),
                Models = new ValueList<string>(new[] { "Composer" }),
                Effort = null,
            },
            MaxHeads = null,
        },
        // :35
        new()
        {
            Id = "astra-spark",
            Title = "Astra 6 leads Spark 1.3 Contributor",
            Tagline = "GPT-6 Astra orchestrates at high; Muse Spark Contributor heads build at medium.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Codex, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Astra" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Meta, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Contributor" }),
                Effort = "medium",
            },
            MaxHeads = null,
        },
        // :36
        new()
        {
            Id = "astra-terra",
            Title = "Astra 6 leads Terra 5.6",
            Tagline = "OpenAI top to bottom: Astra plans and reviews, Terra does the work at medium.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Codex, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Astra" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Codex, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Terra" }),
                Effort = "medium",
            },
            MaxHeads = null,
        },
        // :37
        new()
        {
            Id = "astra-astra",
            Title = "Astra 6 high, Astra 6 medium",
            Tagline = "One model, two efforts: Astra thinks hard about the plan and the review, and Astra heads build at a working effort.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Codex, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Astra" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Codex, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Astra" }),
                Effort = "medium",
            },
            MaxHeads = null,
        },
        // :38
        new()
        {
            Id = "claude-opus-opus",
            Title = "Opus 5 max, Opus 5 medium",
            Tagline = "Opus thinks as hard as it can where it counts; Opus heads at medium do the building.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = "max",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Opus" }),
                Effort = "medium",
            },
            MaxHeads = null,
        },
        // :39
        new()
        {
            Id = "claude-fable-fable",
            Title = "Fable 5.1 high, Fable 5.1 medium",
            Tagline = "For the hardest, longest jobs: every seat on the most capable model, the lead at high and the heads at medium.",
            Lead = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Fable" }),
                Effort = "high",
            },
            Heads = new HydraPairRecipe.Side
            {
                Providers = new ValueList<ProviderKind>(new[] { ProviderKind.Claude, ProviderKind.Commandcode }),
                Models = new ValueList<string>(new[] { "Fable" }),
                Effort = "medium",
            },
            MaxHeads = null,
        },
    };

    // :42 "The effort asked for when the model offers it; nil otherwise, which keeps the
    // default."
    // :43-46 `static func effort(_ wanted: String?, for option: ModelOption?) -> String? {
    // guard let wanted, let option, option.efforts.contains(wanted) else { return nil };
    // return wanted }`.
    public static string? Effort(string? wanted, ModelOption? option)
    {
        if (wanted is null || option is null || !option.Efforts.Contains(wanted))
        {
            return null;
        }

        return wanted;
    }

    // :48 "Whether a pair like this one is already set up: the same providers and models."
    // :49-51 `static func isAdded(_ pair: HydraPair, in pairs: [HydraPair]) -> Bool { pairs
    // .contains { $0.provider == pair.provider && $0.headsProvider == pair.headsProvider &&
    // $0.orchestratorModel == pair.orchestratorModel && $0.workerModel == pair.workerModel } }`.
    public static bool IsAdded(HydraPair pair, IReadOnlyList<HydraPair> pairs)
    {
        foreach (var candidate in pairs)
        {
            if (candidate.Provider == pair.Provider
                && candidate.HeadsProvider == pair.HeadsProvider
                && candidate.OrchestratorModel == pair.OrchestratorModel
                && candidate.WorkerModel == pair.WorkerModel)
            {
                return true;
            }
        }

        return false;
    }
}
