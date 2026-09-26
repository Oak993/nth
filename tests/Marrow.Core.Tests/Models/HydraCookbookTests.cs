using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/HydraCookbook.swift (B3 52bc792), unit
// Core.Models.HydraCookbook, parts 1-2 of 2. Every expected value is transcribed from the cited Swift lines or
// the records themselves, never from the C# under test (R-TEST-4).
//
// UI-STORE-010 to UI-STORE-021 describe the ready-made pair rows the Hydra settings page draws; that page
// is not part of this unit (its "geometry", "typography" and "colors" fields are all "owned by the Hydra
// settings area"). What IS this unit's responsibility, and what those records' "notes" and "exact_text"
// fields transcribe, is the static HydraCookbook.recipes data those rows are built from, so all twelve rows'
// worth of table data are proved here against HydraCookbook.recipes, alongside PR-STORE-132 to PR-STORE-141
// and PR-STORE-172/173 (the same twelve recipes' exact fields) and PR-HYDRA-152 (the table's shape).
//
// B3 52bc792 renames "Opus 5" to "Opus 5.5" in five titles (:30-33, :40) and inserts two new recipes,
// astra-sol and sol-luna, at :37-38 (positions 8 and 9 of 12), pushing astra-astra, claude-opus-opus and
// claude-fable-fable down two places each (PR-HYDRA-152's b3_note; PR-STORE-172, PR-STORE-173).
public sealed class HydraCookbookTests
{
    // ---- PR-HYDRA-152 / PR-STORE-131: exactly twelve recipes, strongest first, with these ids and titles ----

    [Trait("Req", "PR-HYDRA-152")] // DroppyCode/Core/Models/HydraCookbook.swift:29-41 (B3)
    [Trait("Req", "PR-STORE-131")] // DroppyCode/Core/Models/HydraCookbook.swift:3-25 (B3)
    [Fact]
    public void Recipes_has_exactly_twelve_recipes_in_this_order_with_these_ids_and_titles()
    {
        var expected = new (string Id, string Title)[]
        {
            ("claude-fable-opus", "Fable 5.1 leads Opus 5.5"),
            ("claude-opus-gemini-flash", "Opus 5.5 leads Gemini 3.8 Flash"),
            ("claude-opus-deepseek", "Opus 5.5 leads DeepSeek V4.1"),
            ("claude-opus-glm", "Opus 5.5 leads GLM-5.3 Flash"),
            ("claude-fable-composer", "Fable 5.1 leads Composer 2.5"),
            ("astra-spark", "Astra 6 leads Spark 1.3 Contributor"),
            ("astra-terra", "Astra 6 leads Terra 5.6"),
            ("astra-sol", "Astra 6 leads Sol 5.6"),
            ("sol-luna", "Sol 5.6 leads Luna 5.6"),
            ("astra-astra", "Astra 6 high, Astra 6 medium"),
            ("claude-opus-opus", "Opus 5.5 max, Opus 5.5 medium"),
            ("claude-fable-fable", "Fable 5.1 high, Fable 5.1 medium"),
        };

        Assert.Equal(12, HydraCookbook.Recipes.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Id, HydraCookbook.Recipes[i].Id);
            Assert.Equal(expected[i].Title, HydraCookbook.Recipes[i].Title);
        }
    }

    [Trait("Req", "PR-HYDRA-152")] // DroppyCode/Core/Models/HydraCookbook.swift:31 (B3)
    [Fact]
    public void Only_the_second_recipe_carries_a_maxHeads_and_it_is_six()
    {
        for (var i = 0; i < HydraCookbook.Recipes.Count; i++)
        {
            if (i == 1)
            {
                Assert.Equal(6, HydraCookbook.Recipes[i].MaxHeads);
            }
            else
            {
                Assert.Null(HydraCookbook.Recipes[i].MaxHeads);
            }
        }
    }

    // ---- PR-HYDRA-153: the twelve taglines are exact user-visible strings ----

    [Trait("Req", "PR-HYDRA-153")] // DroppyCode/Core/Models/HydraCookbook.swift:30-41 (B3)
    [Fact]
    public void Recipe_taglines_are_exact_byte_for_byte_strings_in_recipe_order()
    {
        var expected = new[]
        {
            "The deepest lead over the strongest builders: Fable plans and checks, Opus heads build. Set the thinking effort of each side yourself.",
            "A careful lead and a wide, fast team: Opus writes the briefs, six Gemini Flash heads sprint through them.",
            "Opus keeps the plan tight; DeepSeek V4.1 Flash heads keep the bill small on the routine parts.",
            "Opus keeps the plan tight; GLM-5.3 Flash heads on a Z.ai Coding Plan keep the bill flat.",
            "Fable thinks; Cursor's Composer heads type at the speed Composer is known for.",
            "GPT-6 Astra orchestrates at high; Muse Spark Contributor heads build at medium.",
            "OpenAI top to bottom: Astra plans and reviews, Terra does the work at medium.",
            "Astra plans and reviews; Sol heads take the complex building at medium.",
            "Sol keeps the plan; quick Luna heads get through the routine parts.",
            "One model, two efforts: Astra thinks hard about the plan and the review, and Astra heads build at a working effort.",
            "Opus thinks as hard as it can where it counts; Opus heads at medium do the building.",
            "For the hardest, longest jobs: every seat on the most capable model, the lead at high and the heads at medium.",
        };

        Assert.Equal(expected.Length, HydraCookbook.Recipes.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], HydraCookbook.Recipes[i].Tagline);
        }
    }

    // ---- PR-STORE-132 to PR-STORE-141, PR-STORE-172/173: each recipe's exact sides, providers, models and effort ----

    [Trait("Req", "PR-STORE-132")] // DroppyCode/Core/Models/HydraCookbook.swift:30 (B3)
    [Fact]
    public void Recipe_1_claude_fable_opus_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[0];
        Assert.Equal("claude-fable-opus", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Lead.Models);
        Assert.Null(recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Heads.Models);
        Assert.Null(recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-133")] // DroppyCode/Core/Models/HydraCookbook.swift:31 (B3)
    [Fact]
    public void Recipe_2_claude_opus_gemini_flash_has_the_exact_sides_and_the_only_cap()
    {
        var recipe = HydraCookbook.Recipes[1];
        Assert.Equal("claude-opus-gemini-flash", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Antigravity, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Gemini 3.8 Flash", "Gemini", "Flash" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Equal(6, recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-134")] // DroppyCode/Core/Models/HydraCookbook.swift:32 (B3)
    [Fact]
    public void Recipe_3_claude_opus_deepseek_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[2];
        Assert.Equal("claude-opus-deepseek", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Deepseek, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "V4.1 Flash", "V4.1", "DeepSeek" }, recipe.Heads.Models);
        Assert.Equal("high", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-135")] // DroppyCode/Core/Models/HydraCookbook.swift:33 (B3)
    [Fact]
    public void Recipe_4_claude_opus_glm_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[3];
        Assert.Equal("claude-opus-glm", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Zai, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "5.3 Flash", "GLM" }, recipe.Heads.Models);
        Assert.Equal("high", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-136")] // DroppyCode/Core/Models/HydraCookbook.swift:34 (B3)
    [Fact]
    public void Recipe_5_claude_fable_composer_has_a_single_provider_heads_side()
    {
        var recipe = HydraCookbook.Recipes[4];
        Assert.Equal("claude-fable-composer", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Cursor }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Composer" }, recipe.Heads.Models);
        Assert.Null(recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-137")] // DroppyCode/Core/Models/HydraCookbook.swift:35 (B3)
    [Fact]
    public void Recipe_6_astra_spark_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[5];
        Assert.Equal("astra-spark", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Meta, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Contributor" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-138")] // DroppyCode/Core/Models/HydraCookbook.swift:36 (B3)
    [Fact]
    public void Recipe_7_astra_terra_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[6];
        Assert.Equal("astra-terra", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Terra" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-172")] // DroppyCode/Core/Models/HydraCookbook.swift:37 (B3, NEW at B3)
    [Fact]
    public void Recipe_8_astra_sol_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[7];
        Assert.Equal("astra-sol", recipe.Id);
        Assert.Equal("Astra 6 leads Sol 5.6", recipe.Title);
        Assert.Equal(
            "Astra plans and reviews; Sol heads take the complex building at medium.",
            recipe.Tagline);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Sol" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-173")] // DroppyCode/Core/Models/HydraCookbook.swift:38 (B3, NEW at B3)
    [Fact]
    public void Recipe_9_sol_luna_has_the_exact_sides()
    {
        var recipe = HydraCookbook.Recipes[8];
        Assert.Equal("sol-luna", recipe.Id);
        Assert.Equal("Sol 5.6 leads Luna 5.6", recipe.Title);
        Assert.Equal(
            "Sol keeps the plan; quick Luna heads get through the routine parts.",
            recipe.Tagline);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Sol" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Luna" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-139")] // DroppyCode/Core/Models/HydraCookbook.swift:39 (B3; was :37 at B2)
    [Fact]
    public void Recipe_10_astra_astra_is_one_model_at_two_efforts()
    {
        var recipe = HydraCookbook.Recipes[9];
        Assert.Equal("astra-astra", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-140")] // DroppyCode/Core/Models/HydraCookbook.swift:40 (B3; was :38 at B2)
    [Fact]
    public void Recipe_11_claude_opus_opus_leads_at_max_effort()
    {
        var recipe = HydraCookbook.Recipes[10];
        Assert.Equal("claude-opus-opus", recipe.Id);
        Assert.Equal("Opus 5.5 max, Opus 5.5 medium", recipe.Title);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Lead.Models);
        Assert.Equal("max", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "PR-STORE-141")] // DroppyCode/Core/Models/HydraCookbook.swift:41 (B3; was :39 at B2)
    [Fact]
    public void Recipe_12_claude_fable_fable_is_the_last_and_strongest_pair()
    {
        var recipe = HydraCookbook.Recipes[11];
        Assert.Equal("claude-fable-fable", recipe.Id);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    // ---- UI-STORE-010 to UI-STORE-021: the ready-made row data for all twelve rows ----

    [Trait("Req", "UI-STORE-010")] // DroppyCode/Core/Models/HydraCookbook.swift:30 (B3)
    [Fact]
    public void Row_1_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[0];
        Assert.Equal("Fable 5.1 leads Opus 5.5", recipe.Title);
        Assert.Equal(
            "The deepest lead over the strongest builders: Fable plans and checks, Opus heads build. Set the thinking effort of each side yourself.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-011")] // DroppyCode/Core/Models/HydraCookbook.swift:31 (B3)
    [Fact]
    public void Row_2_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[1];
        Assert.Equal("Opus 5.5 leads Gemini 3.8 Flash", recipe.Title);
        Assert.Equal(
            "A careful lead and a wide, fast team: Opus writes the briefs, six Gemini Flash heads sprint through them.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-012")] // DroppyCode/Core/Models/HydraCookbook.swift:32 (B3)
    [Fact]
    public void Row_3_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[2];
        Assert.Equal("Opus 5.5 leads DeepSeek V4.1", recipe.Title);
        Assert.Equal(
            "Opus keeps the plan tight; DeepSeek V4.1 Flash heads keep the bill small on the routine parts.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-013")] // DroppyCode/Core/Models/HydraCookbook.swift:33 (B3)
    [Fact]
    public void Row_4_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[3];
        Assert.Equal("Opus 5.5 leads GLM-5.3 Flash", recipe.Title);
        Assert.Equal(
            "Opus keeps the plan tight; GLM-5.3 Flash heads on a Z.ai Coding Plan keep the bill flat.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-014")] // DroppyCode/Core/Models/HydraCookbook.swift:34 (B3)
    [Fact]
    public void Row_5_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[4];
        Assert.Equal("Fable 5.1 leads Composer 2.5", recipe.Title);
        Assert.Equal(
            "Fable thinks; Cursor's Composer heads type at the speed Composer is known for.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-015")] // DroppyCode/Core/Models/HydraCookbook.swift:35 (B3)
    [Fact]
    public void Row_6_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[5];
        Assert.Equal("Astra 6 leads Spark 1.3 Contributor", recipe.Title);
        Assert.Equal(
            "GPT-6 Astra orchestrates at high; Muse Spark Contributor heads build at medium.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-016")] // DroppyCode/Core/Models/HydraCookbook.swift:36 (B3)
    [Fact]
    public void Row_7_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[6];
        Assert.Equal("Astra 6 leads Terra 5.6", recipe.Title);
        Assert.Equal(
            "OpenAI top to bottom: Astra plans and reviews, Terra does the work at medium.",
            recipe.Tagline);
    }

    // ---- UI-STORE-020/021: the ready-made row data for rows 8-9 (new at B3) ----

    [Trait("Req", "UI-STORE-020")] // DroppyCode/Core/Models/HydraCookbook.swift:37 (B3, NEW at B3)
    [Fact]
    public void Row_8_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[7];
        Assert.Equal("astra-sol", recipe.Id);
        Assert.Equal("Astra 6 leads Sol 5.6", recipe.Title);
        Assert.Equal(
            "Astra plans and reviews; Sol heads take the complex building at medium.",
            recipe.Tagline);
    }

    [Trait("Req", "UI-STORE-021")] // DroppyCode/Core/Models/HydraCookbook.swift:38 (B3, NEW at B3)
    [Fact]
    public void Row_9_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[8];
        Assert.Equal("sol-luna", recipe.Id);
        Assert.Equal("Sol 5.6 leads Luna 5.6", recipe.Title);
        Assert.Equal(
            "Sol keeps the plan; quick Luna heads get through the routine parts.",
            recipe.Tagline);
    }

    // ---- UI-STORE-017 to UI-STORE-019: the ready-made row data for rows 10-12 (pushed down two places at B3) ----

    [Trait("Req", "UI-STORE-017")] // DroppyCode/Core/Models/HydraCookbook.swift:39 (B3; was :37 at B2)
    [Fact]
    public void Row_10_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[9];
        Assert.Equal("astra-astra", recipe.Id);
        Assert.Equal("Astra 6 high, Astra 6 medium", recipe.Title);
        Assert.Equal(
            "One model, two efforts: Astra thinks hard about the plan and the review, and Astra heads build at a working effort.",
            recipe.Tagline);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Astra" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "UI-STORE-017")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void Row_10_is_marked_added_only_when_provider_headsProvider_orchestratorModel_and_workerModel_match_ignoring_effort()
    {
        var recipe = HydraCookbook.Recipes[9];
        var matchingDifferentEffort = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = "low",
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = "low",
        };
        var pair = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = recipe.Lead.Effort,
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = recipe.Heads.Effort,
        };

        Assert.True(HydraCookbook.IsAdded(pair, new ValueList<HydraPair>(new[] { matchingDifferentEffort })));
        Assert.False(HydraCookbook.IsAdded(pair, ValueList<HydraPair>.Empty));
    }

    [Trait("Req", "UI-STORE-018")] // DroppyCode/Core/Models/HydraCookbook.swift:40 (B3; was :38 at B2)
    [Fact]
    public void Row_11_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[10];
        Assert.Equal("claude-opus-opus", recipe.Id);
        Assert.Equal("Opus 5.5 max, Opus 5.5 medium", recipe.Title);
        Assert.Equal(
            "Opus thinks as hard as it can where it counts; Opus heads at medium do the building.",
            recipe.Tagline);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Lead.Models);
        Assert.Equal("max", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Opus" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "UI-STORE-018")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void Row_11_is_marked_added_only_when_provider_headsProvider_orchestratorModel_and_workerModel_match_ignoring_effort()
    {
        var recipe = HydraCookbook.Recipes[10];
        var matchingDifferentEffort = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = "low",
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = "low",
        };
        var pair = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = recipe.Lead.Effort,
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = recipe.Heads.Effort,
        };

        Assert.True(HydraCookbook.IsAdded(pair, new ValueList<HydraPair>(new[] { matchingDifferentEffort })));
        Assert.False(HydraCookbook.IsAdded(pair, ValueList<HydraPair>.Empty));
    }

    [Trait("Req", "UI-STORE-019")] // DroppyCode/Core/Models/HydraCookbook.swift:41 (B3; was :39 at B2)
    [Fact]
    public void Row_12_data_matches_the_recorded_exact_text_and_notes()
    {
        var recipe = HydraCookbook.Recipes[11];
        Assert.Equal("claude-fable-fable", recipe.Id);
        Assert.Equal("Fable 5.1 high, Fable 5.1 medium", recipe.Title);
        Assert.Equal(
            "For the hardest, longest jobs: every seat on the most capable model, the lead at high and the heads at medium.",
            recipe.Tagline);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Lead.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Lead.Models);
        Assert.Equal("high", recipe.Lead.Effort);
        Assert.Equal(new[] { ProviderKind.Claude, ProviderKind.Commandcode }, recipe.Heads.Providers);
        Assert.Equal(new[] { "Fable" }, recipe.Heads.Models);
        Assert.Equal("medium", recipe.Heads.Effort);
        Assert.Null(recipe.MaxHeads);
    }

    [Trait("Req", "UI-STORE-019")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void Row_12_is_marked_added_only_when_provider_headsProvider_orchestratorModel_and_workerModel_match_ignoring_effort()
    {
        var recipe = HydraCookbook.Recipes[11];
        var matchingDifferentEffort = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = "low",
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = "low",
        };
        var pair = new HydraPair(recipe.Lead.Providers[0])
        {
            OrchestratorModel = recipe.Lead.Models[0],
            OrchestratorEffort = recipe.Lead.Effort,
            WorkerProvider = recipe.Heads.Providers[0],
            WorkerModel = recipe.Heads.Models[0],
            WorkerEffort = recipe.Heads.Effort,
        };

        Assert.True(HydraCookbook.IsAdded(pair, new ValueList<HydraPair>(new[] { matchingDifferentEffort })));
        Assert.False(HydraCookbook.IsAdded(pair, ValueList<HydraPair>.Empty));
    }

    // ---- PR-HYDRA-154 / PR-STORE-142: Effort keeps the asked-for effort only when the resolved model offers it ----

    [Trait("Req", "PR-HYDRA-154")] // DroppyCode/Core/Models/HydraCookbook.swift:45-48 (B3; was :43-46 at B2)
    [Trait("Req", "PR-STORE-142")] // DroppyCode/Core/Models/HydraCookbook.swift:44-48 (B3; was :42-46 at B2)
    [Fact]
    public void Effort_returns_the_wanted_value_when_the_option_offers_it()
    {
        var option = new ModelOption("opus", "Opus", efforts: new ValueList<string>(new[] { "low", "medium", "high" }));
        Assert.Equal("high", HydraCookbook.Effort("high", option));
    }

    [Trait("Req", "PR-HYDRA-154")] // DroppyCode/Core/Models/HydraCookbook.swift:46 (B3; was :44 at B2)
    [Fact]
    public void Effort_returns_null_when_wanted_is_null()
    {
        var option = new ModelOption("opus", "Opus", efforts: new ValueList<string>(new[] { "low", "medium", "high" }));
        Assert.Null(HydraCookbook.Effort(null, option));
    }

    [Trait("Req", "PR-HYDRA-154")] // DroppyCode/Core/Models/HydraCookbook.swift:46 (B3; was :44 at B2)
    [Fact]
    public void Effort_returns_null_when_the_option_is_null()
    {
        Assert.Null(HydraCookbook.Effort("high", null));
    }

    [Trait("Req", "PR-HYDRA-154")] // DroppyCode/Core/Models/HydraCookbook.swift:46 (B3; was :44 at B2)
    [Trait("Req", "PR-STORE-142")] // DroppyCode/Core/Models/HydraCookbook.swift:46 (B3; was :44 at B2)
    [Fact]
    public void Effort_returns_null_when_the_options_efforts_do_not_contain_the_wanted_value()
    {
        var option = new ModelOption("gemini", "Gemini", efforts: new ValueList<string>(new[] { "low", "medium" }));
        Assert.Null(HydraCookbook.Effort("high", option));
    }

    [Trait("Req", "PR-STORE-142")] // DroppyCode/Core/Models/HydraCookbook.swift:46 (B3; was :44 at B2)
    [Fact]
    public void Effort_comparison_is_exact_with_no_case_folding_or_aliasing()
    {
        var option = new ModelOption("opus", "Opus", efforts: new ValueList<string>(new[] { "High", "Medium" }));
        Assert.Null(HydraCookbook.Effort("high", option));
    }

    // ---- PR-HYDRA-155 / PR-STORE-143: IsAdded compares exactly four fields, ignoring efforts, cap, name, profiles ----

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:51-53 (B3; was :49-51 at B2)
    [Trait("Req", "PR-STORE-143")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void IsAdded_true_when_provider_headsProvider_orchestratorModel_and_workerModel_all_match()
    {
        var existing = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "Opus",
        };
        var candidate = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "Opus",
        };

        Assert.True(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:52 (B3; was :50 at B2)
    [Fact]
    public void IsAdded_false_when_the_lead_provider_differs()
    {
        var existing = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Opus" };
        var candidate = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "Opus", WorkerModel = "Opus" };

        Assert.False(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:52 (B3; was :50 at B2)
    [Fact]
    public void IsAdded_false_when_the_orchestrator_model_differs()
    {
        var existing = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Opus" };
        var candidate = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Fable", WorkerModel = "Opus" };

        Assert.False(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:52 (B3; was :50 at B2)
    [Fact]
    public void IsAdded_false_when_the_worker_model_differs()
    {
        var existing = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Opus" };
        var candidate = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Fable" };

        Assert.False(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:52 (B3; was :50 at B2) - headsProvider, not workerProvider
    [Fact]
    public void IsAdded_a_nil_workerProvider_compares_equal_to_one_explicitly_set_to_the_lead_provider()
    {
        // headsProvider = workerProvider ?? provider (Hydra.cs:206), so an explicit workerProvider equal to
        // the lead's own provider must compare equal to one left nil.
        var existing = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Opus" };
        var candidate = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "Opus",
        };

        Assert.True(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
        Assert.True(HydraCookbook.IsAdded(existing, new ValueList<HydraPair>(new[] { candidate })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:51-53 (B3; was :49-51 at B2)
    [Trait("Req", "PR-STORE-143")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void IsAdded_ignores_efforts_so_two_efforts_of_one_model_both_read_as_already_added()
    {
        var existingMax = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            OrchestratorEffort = "max",
            WorkerModel = "Opus",
            WorkerEffort = "medium",
        };
        var candidateHigh = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            OrchestratorEffort = "high",
            WorkerModel = "Opus",
            WorkerEffort = "low",
        };

        Assert.True(HydraCookbook.IsAdded(candidateHigh, new ValueList<HydraPair>(new[] { existingMax })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:51-53 (B3; was :49-51 at B2)
    [Trait("Req", "PR-STORE-143")] // DroppyCode/Core/Models/HydraCookbook.swift:50-53 (B3; was :48-51 at B2)
    [Fact]
    public void IsAdded_ignores_maxHeads_name_and_headProfiles()
    {
        var existing = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            WorkerModel = "Opus",
            MaxHeads = 4,
            Name = "My custom pair",
            HeadProfiles = new ValueList<HydraHeadProfile>(new[] { new HydraHeadProfile("quick") }),
        };
        var candidate = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "Opus",
            WorkerModel = "Opus",
            MaxHeads = 8,
            Name = "A different name",
        };

        Assert.True(HydraCookbook.IsAdded(candidate, new ValueList<HydraPair>(new[] { existing })));
    }

    [Trait("Req", "PR-HYDRA-155")] // DroppyCode/Core/Models/HydraCookbook.swift:51-53 (B3; was :49-51 at B2)
    [Fact]
    public void IsAdded_false_when_the_pairs_list_is_empty()
    {
        var candidate = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "Opus", WorkerModel = "Opus" };

        Assert.False(HydraCookbook.IsAdded(candidate, ValueList<HydraPair>.Empty));
    }
}
