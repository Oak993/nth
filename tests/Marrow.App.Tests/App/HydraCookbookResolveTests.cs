using System.Globalization;
using Marrow.App.Tests.App.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// Source: DroppyCode/App/HydraCookbook+Resolve.swift (B2 1fbf81e), :1-69.
// ED-164: the test file and class drop the "+"; static members are called through HydraCookbook.
public sealed class HydraCookbookResolveTests
{
    private static HydraPairRecipe.Side Side(ProviderKind[] providers, string[] models, string? effort = null) =>
        new()
        {
            Providers = new ValueList<ProviderKind>(providers),
            Models = new ValueList<string>(models),
            Effort = effort,
        };

    private static HydraPairRecipe Recipe(HydraPairRecipe.Side lead, HydraPairRecipe.Side heads, long? maxHeads = null) =>
        new()
        {
            Id = "recipe",
            Title = "Recipe",
            Tagline = "Tagline",
            Lead = lead,
            Heads = heads,
            MaxHeads = maxHeads,
        };

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:8-10
    [Trait("Decision", "ED-164")]
    public void IsReady_requires_settings_enabled_and_registry_status_installed()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Zai, false);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Codex, false);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, true);

            Assert.True(HydraCookbook.IsReady(ProviderKind.Claude, registry, settings));
            Assert.False(HydraCookbook.IsReady(ProviderKind.Codex, registry, settings));
            Assert.False(HydraCookbook.IsReady(ProviderKind.Zai, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:14-19
    [Trait("Decision", "ED-164")]
    public void Provider_uses_the_first_ready_provider_that_lists_a_named_model()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-opus", "Claude Opus 4.1"));
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Codex,
                HydraCookbookResolveFixture.Model("codex-only-model-needle-5fb8e8", "Codex-only-model-needle-5fb8e8"));

            var side = Side([ProviderKind.Claude, ProviderKind.Codex], ["codex-only-model-needle-5fb8e8"]);

            Assert.Equal(ProviderKind.Codex, HydraCookbook.Provider(side, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:14-19
    [Trait("Decision", "ED-164")]
    public void Provider_uses_the_first_ready_provider_when_no_models_are_named()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Codex, true);

            var side = Side([ProviderKind.Claude, ProviderKind.Codex], []);

            Assert.Equal(ProviderKind.Claude, HydraCookbook.Provider(side, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:14-19
    [Trait("Decision", "ED-164")]
    public void Provider_is_nil_when_no_ready_provider_lists_a_named_model()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Zai, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, false);
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-opus", "Claude Opus 4.1"));

            var side = Side([ProviderKind.Claude, ProviderKind.Zai], ["not-in-any-provider-catalog-8a0f41"]);

            Assert.Null(HydraCookbook.Provider(side, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:23-28
    [Trait("Decision", "ED-164")]
    public void MissingProvider_checks_the_lead_side_first_and_names_its_first_provider()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, false);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, false);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Codex, true);

            var recipe = Recipe(
                Side([ProviderKind.Claude, ProviderKind.Zai], []),
                Side([ProviderKind.Codex], []));

            Assert.Equal(ProviderKind.Claude, HydraCookbook.MissingProvider(recipe, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:23-28
    [Trait("Decision", "ED-164")]
    public void MissingProvider_returns_nil_at_once_for_a_lead_side_without_providers_before_checking_heads()
    {
        // :24-25 an empty side has no ready provider, so the loop body runs and returns side.providers.first, which
        // is nil: the heads side is never checked, even though its only provider is not ready.
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, false);

            var recipe = Recipe(
                Side([], []),
                Side([ProviderKind.Zai], []));

            Assert.Null(HydraCookbook.MissingProvider(recipe, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:23-28
    [Trait("Decision", "ED-164")]
    public void MissingProvider_names_the_heads_side_first_provider_when_only_heads_lacks_readiness()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Zai, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Grok, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, false);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Grok, false);

            var recipe = Recipe(
                Side([ProviderKind.Claude], []),
                Side([ProviderKind.Zai, ProviderKind.Grok], []));

            Assert.Equal(ProviderKind.Zai, HydraCookbook.MissingProvider(recipe, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:23-28
    [Trait("Decision", "ED-164")]
    public void MissingProvider_is_nil_when_each_side_has_a_ready_provider_even_if_models_do_not_match()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Codex, true);

            var recipe = Recipe(
                Side([ProviderKind.Claude], ["sonnet"]),
                Side([ProviderKind.Codex], ["sonnet"]));

            Assert.Null(HydraCookbook.MissingProvider(recipe, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:32-45
    [Trait("Decision", "ED-164")]
    public void Resolve_sets_models_filtered_efforts_distinct_worker_provider_and_cap()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Zai, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, true);
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5", ["low", "high"], "low"));
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Zai,
                HydraCookbookResolveFixture.Model("zai-glm-4-7", "GLM 4.7", ["low"], "low"));

            var recipe = Recipe(
                Side([ProviderKind.Claude], ["sonnet"], "high"),
                Side([ProviderKind.Zai], ["glm"], "high"),
                maxHeads: 3);

            var pair = HydraCookbook.Resolve(recipe, registry, settings);

            Assert.NotNull(pair);
            Assert.Equal(ProviderKind.Claude, pair.Provider);
            Assert.Equal("claude-sonnet-4-5", pair.OrchestratorModel);
            Assert.Equal("high", pair.OrchestratorEffort);
            Assert.Equal(ProviderKind.Zai, pair.WorkerProvider);
            Assert.Equal("zai-glm-4-7", pair.WorkerModel);
            Assert.Null(pair.WorkerEffort);
            Assert.Equal(3, pair.MaxHeads);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:32-45
    [Trait("Decision", "ED-164")]
    public void Resolve_leaves_worker_provider_unset_when_both_sides_use_the_same_provider()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5"),
                HydraCookbookResolveFixture.Model("claude-haiku-4-5", "Claude Haiku 4.5"));

            var recipe = Recipe(
                Side([ProviderKind.Claude], ["sonnet"]),
                Side([ProviderKind.Claude], ["haiku"]));

            var pair = HydraCookbook.Resolve(recipe, registry, settings);

            Assert.NotNull(pair);
            Assert.Null(pair.WorkerProvider);
            Assert.Equal(ProviderKind.Claude, pair.HeadsProvider);
            Assert.Equal("claude-haiku-4-5", pair.WorkerModel);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:36-42
    [Trait("Decision", "ED-164")]
    public void Resolve_uses_the_provider_default_model_to_filter_effort_when_a_side_names_no_models()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);

            var recipe = Recipe(
                Side([ProviderKind.Claude], [], "high"),
                Side([ProviderKind.Claude], [], null));

            var pair = HydraCookbook.Resolve(recipe, registry, settings);

            Assert.NotNull(pair);
            Assert.Null(pair.OrchestratorModel);
            Assert.Equal("high", pair.OrchestratorEffort);
            Assert.Null(pair.WorkerModel);
            Assert.Null(pair.WorkerEffort);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-156")] // App/HydraCookbook+Resolve.swift:32-34
    [Trait("Decision", "ED-164")]
    public void Resolve_is_nil_when_either_side_has_no_ready_provider_that_lists_its_model()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var settings = new AppSettings();
            var registry = new ProviderRegistry(settings);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetEnabled(settings, ProviderKind.Zai, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(registry, ProviderKind.Zai, false);
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5"));

            var recipe = Recipe(
                Side([ProviderKind.Claude], ["sonnet"]),
                Side([ProviderKind.Zai], ["glm"]));

            Assert.Null(HydraCookbook.Resolve(recipe, registry, settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:49-62
    [Trait("Decision", "ED-164")]
    public void Model_uses_the_first_fragment_that_matches_any_option()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-haiku-4-5", "Claude Haiku 4.5"),
                HydraCookbookResolveFixture.Model("claude-opus-9-9", "Claude Opus 9.9"));

            var picked = HydraCookbook.Model(
                names: new ValueList<string>(["haiku", "opus"]),
                provider: ProviderKind.Claude,
                registry: registry);
            var fallback = HydraCookbook.Model(
                names: new ValueList<string>(["nothing-matches-this", "haiku"]),
                provider: ProviderKind.Claude,
                registry: registry);

            Assert.Equal("claude-haiku-4-5", picked?.Id);
            Assert.Equal("claude-haiku-4-5", fallback?.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:51-53
    [Trait("Decision", "ED-164")]
    public void Model_matches_fragments_case_insensitively_in_display_names_and_ids()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5"));

            var byName = HydraCookbook.Model(
                names: new ValueList<string>(["SONNET"]),
                provider: ProviderKind.Claude,
                registry: registry);
            var byId = HydraCookbook.Model(
                names: new ValueList<string>(["sonnet-4"]),
                provider: ProviderKind.Claude,
                registry: registry);

            Assert.Equal("claude-sonnet-4-5", byName?.Id);
            Assert.Equal("claude-sonnet-4-5", byId?.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:54-59
    [Trait("Decision", "ED-164")]
    public void Model_picks_the_highest_display_name_version_and_keeps_the_first_registry_entry_on_a_tie()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-opus-4-1", "Claude Opus 4.1"),
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5"),
                HydraCookbookResolveFixture.Model("claude-plain", "Claude Plain"));

            var newest = HydraCookbook.Model(
                names: new ValueList<string>(["claude"]),
                provider: ProviderKind.Claude,
                registry: registry);

            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Codex,
                HydraCookbookResolveFixture.Model("first", "Model 5.2"),
                HydraCookbookResolveFixture.Model("second", "Model 5.2"));

            var tied = HydraCookbook.Model(
                names: new ValueList<string>(["model"]),
                provider: ProviderKind.Codex,
                registry: registry);

            Assert.Equal("claude-sonnet-4-5", newest?.Id);
            Assert.Equal("first", tied?.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:54-58
    [Trait("Decision", "ED-164")]
    public void Model_treats_an_absent_version_as_minus_one()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Codex,
                HydraCookbookResolveFixture.Model("plain", "Plain"),
                HydraCookbookResolveFixture.Model("versioned", "Model 2.0"));

            var picked = HydraCookbook.Model(
                names: new ValueList<string>(["model"]),
                provider: ProviderKind.Codex,
                registry: registry);

            Assert.Equal("versioned", picked?.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:65-68
    [Trait("Decision", "ED-164")]
    public void Model_reads_only_the_first_version_run_in_the_display_name()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Codex,
                HydraCookbookResolveFixture.Model("first-run", "Gemini 3.8 Flash (build 99)"),
                HydraCookbookResolveFixture.Model("other", "Gemini 3.5 Pro 99"));

            var picked = HydraCookbook.Model(
                names: new ValueList<string>(["gemini"]),
                provider: ProviderKind.Codex,
                registry: registry);

            Assert.Equal("first-run", picked?.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:65-68
    [Trait("Decision", "ED-164")]
    public void Model_parses_decimal_versions_independently_of_the_current_locale()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Codex,
                HydraCookbookResolveFixture.Model("lower", "Gemini 3.8 Flash"),
                HydraCookbookResolveFixture.Model("higher", "Gemini 3.9 Flash"));

            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var picked = HydraCookbook.Model(
                    names: new ValueList<string>(["gemini"]),
                    provider: ProviderKind.Codex,
                    registry: registry);

                Assert.Equal("higher", picked?.Id);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-157")] // App/HydraCookbook+Resolve.swift:49-62
    [Trait("Decision", "ED-164")]
    public void Model_is_nil_when_no_fragment_matches_any_option()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            HydraCookbookResolveFixture.SetModels(registry, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("claude-sonnet-4-5", "Claude Sonnet 4.5"));

            var picked = HydraCookbook.Model(
                names: new ValueList<string>(["zai", "glm"]),
                provider: ProviderKind.Claude,
                registry: registry);

            Assert.Null(picked);
        });
    }
}
