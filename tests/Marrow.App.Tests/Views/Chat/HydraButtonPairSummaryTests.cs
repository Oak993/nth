using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// HydraPairSummary (App/Views/Chat/HydraButton.swift:648-711): the one-line summaries of a pair. The registry is the
/// real seeded one (Claude: Opus, Fable, Sonnet; Antigravity: Gemini 3.8 Flash default), so every name below is what
/// the user would read.
/// </summary>
public sealed class HydraButtonPairSummaryTests
{
    // The seeded Claude catalogue's Fable row (ProviderRegistry.swift:74); its short name is "Fable".
    private const string FableID = "claude-fable-5-1[1m]";

    [Fact]
    [Trait("Req", "PR-HYDRA-233")] // HydraButton.swift:652-657 workerModel
    public void The_worker_model_is_the_pairs_own_name_for_it_on_the_heads_provider_or_the_raw_id()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            // :654 a model the registry knows is shown by its short name.
            Assert.Equal("Opus", HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerModel = "opus" }, registry));
            // :654 `?? model`: one the catalogue has not got is shown by its raw id.
            Assert.Equal("mystery-model", HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerModel = "mystery-model" }, registry));
            // :654 looked up on the HEADS provider (Antigravity), not on the lead's: "opus" is not an Antigravity
            // model, "gemini-3.7-flash" is not a Claude one.
            Assert.Equal("opus", HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity, WorkerModel = "opus" }, registry));
            Assert.Equal("Gemini 3.7 Flash", HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity, WorkerModel = "gemini-3.7-flash" }, registry));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-233")] // HydraButton.swift:655-656
    public void With_no_worker_model_it_is_nil_on_the_leads_provider_and_the_other_providers_default_elsewhere()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            // :655 the heads stay on the lead's provider and inherit the chat's model.
            Assert.Null(HydraPairSummary.WorkerModel(new HydraPair(ProviderKind.Claude), registry));
            Assert.Null(HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Claude }, registry));
            // :656 sent elsewhere with none chosen: that provider's default model, which is what they run there.
            Assert.Equal("Gemini 3.8 Flash", HydraPairSummary.WorkerModel(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity }, registry));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-234")] // HydraButton.swift:661-673 workers
    public void The_heads_sentence_names_the_model_and_marks_the_other_provider()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            Assert.Equal("Heads on Opus", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerModel = "opus" }, registry)); // :665 no suffix on the lead's provider
            Assert.Equal("Heads on Gemini 3.8 Flash (Antigravity)", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity }, registry)); // :665
            Assert.Equal("Heads on Gemini 3.7 Flash (Antigravity)", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity, WorkerModel = "gemini-3.7-flash" }, registry));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-234")] // HydraButton.swift:666-670
    public void The_heads_sentence_falls_back_to_the_provider_name_and_then_to_the_chats_model()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            // A provider whose catalogue is empty has no default model to name (Codex's is filled by a refresh).
            Assert.Null(registry.DefaultModel(ProviderKind.Codex));

            Assert.Equal("Heads on Codex", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, registry)); // :667
            Assert.Equal("Heads on the chat's model", HydraPairSummary.Workers(new HydraPair(ProviderKind.Claude), registry)); // :669
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-234")] // HydraButton.swift:671-672
    public void A_worker_effort_adds_its_title_and_the_word_effort_after_a_middle_dot()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            Assert.Equal("Heads on Opus · High effort", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerModel = "opus", WorkerEffort = "high" }, registry));
            Assert.Equal("Heads on the chat's model · Extra High effort", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerEffort = "xhigh" }, registry)); // ModelOption.effortTitle "xhigh"
            Assert.Equal("Heads on Codex · Low effort", HydraPairSummary.Workers(
                new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex, WorkerEffort = "low" }, registry));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-235")] // HydraButton.swift:676-678 providers
    public void The_providers_line_names_lead_and_heads_only_across_providers()
    {
        Assert.Equal("Claude lead, Antigravity heads", HydraPairSummary.Providers(
            new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Antigravity }));
        Assert.Equal("Claude", HydraPairSummary.Providers(new HydraPair(ProviderKind.Claude)));
        Assert.Equal("Claude", HydraPairSummary.Providers(
            new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Claude }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-235")] // HydraButton.swift:683-693 efforts
    public void The_efforts_line_has_one_exact_shape_per_case()
    {
        static string? Efforts(string? lead, string? heads) => HydraPairSummary.Efforts(
            new HydraPair(ProviderKind.Claude) { OrchestratorEffort = lead, WorkerEffort = heads });

        Assert.Equal("High effort", Efforts("high", "high")); // :687 both equal, capitalised
        Assert.Equal("Extra High effort", Efforts("xhigh", "xhigh")); // :684-687 each word of "extra high" is capitalised
        Assert.Equal("Lead high, heads medium effort", Efforts("high", "medium")); // :688
        Assert.Equal("Lead extra high, heads low effort", Efforts("xhigh", "low")); // :684-685 lowercased titles
        Assert.Equal("Lead high effort", Efforts("high", null)); // :689
        Assert.Equal("Heads medium effort", Efforts(null, "medium")); // :690
        Assert.Null(Efforts(null, null)); // :691
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-236")] // HydraButton.swift:697-704 modelsTitle
    public void The_models_title_joins_the_lead_and_the_heads_model()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            Assert.Equal("Fable + Opus", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { OrchestratorModel = FableID, WorkerModel = "opus" }, registry));
            Assert.Equal("Any Claude model + Sonnet", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { WorkerModel = "sonnet" }, registry)); // :699 no lead model
            Assert.Equal("Opus + itself", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus" }, registry)); // :701
            Assert.Equal("a-model-the-catalogue-lacks + itself", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { OrchestratorModel = "a-model-the-catalogue-lacks" }, registry)); // :699 raw id
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-236")] // HydraButton.swift:700-703 across providers
    public void Across_providers_the_heads_name_is_their_model_or_else_the_provider()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());

            Assert.Equal("Fable + Gemini 3.8 Flash", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { OrchestratorModel = FableID, WorkerProvider = ProviderKind.Antigravity }, registry));
            Assert.Null(registry.DefaultModel(ProviderKind.Codex));
            Assert.Equal("Fable + Codex", HydraPairSummary.ModelsTitle(
                new HydraPair(ProviderKind.Claude) { OrchestratorModel = FableID, WorkerProvider = ProviderKind.Codex }, registry)); // :700
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-236")] // HydraButton.swift:707-710 title
    public void The_users_own_trimmed_name_wins_when_it_is_not_blank()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            var named = new HydraPair(ProviderKind.Claude) { Name = "  Night shift  ", OrchestratorModel = "opus", WorkerModel = "sonnet" };

            Assert.Equal("Night shift", HydraPairSummary.Title(named, registry));
            Assert.Equal("Opus + Sonnet", HydraPairSummary.Title(named with { Name = "   " }, registry)); // blank: the models title
            Assert.Equal("Opus + Sonnet", HydraPairSummary.Title(named with { Name = null }, registry));
        });
    }
}
