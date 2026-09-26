using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.App.Tests;

public sealed class AppModelHydraPairsTests
{
    [Fact]
    [Trait("Req", "PR-HYDRA-143")]
    public void Hydra_picker_pairs_keep_setup_order_and_filter_disabled_and_missing_providers_but_keep_unprobed_ones()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, _) = MakeModel(h);
        var firstUnprobed = new HydraPair(ProviderKind.Grok);
        var disabled = new HydraPair(ProviderKind.Copilot);
        var missing = new HydraPair(ProviderKind.Pi);
        var secondUnprobed = new HydraPair(ProviderKind.Devin);

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(firstUnprobed);
            model.Settings.AddHydraPair(disabled);
            model.Settings.AddHydraPair(missing);
            model.Settings.AddHydraPair(secondUnprobed);
            model.Settings.SetEnabled(false, disabled.Provider);
            model.Settings.SetBinaryPath(Path.Combine(h.Scratch, "missing-pi.exe"), missing.Provider);
        });
        h.Main.Loop.Run(() => model.Providers.Refresh(missing.Provider), HydraPairsAppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.True(model.Providers.CountsAsInstalled(firstUnprobed.Provider));
            Assert.False(model.Providers.CountsAsInstalled(missing.Provider));
            Assert.Equal([firstUnprobed, secondUnprobed], model.HydraPickerPairs.ToArray());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-144")]
    public void A_pair_checkmark_requires_hydra_to_be_on_a_matching_pinned_pair_and_provider()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h);
        var pair = new HydraPair(thread.Provider);
        var otherPair = new HydraPair(thread.Provider);

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.Settings.AddHydraPair(otherPair);
            var pinned = thread with { HydraPairID = pair.Id, HydraEnabled = true };

            Assert.True(model.LeadsWithHydraPair(pair, pinned));
            Assert.False(model.LeadsWithHydraPair(otherPair, pinned));
            Assert.False(model.LeadsWithHydraPair(pair, pinned with { HydraPairID = null }));
            Assert.False(model.LeadsWithHydraPair(pair, pinned with { HydraEnabled = false }));
            Assert.False(model.LeadsWithHydraPair(pair, pinned with { Provider = ProviderKind.Claude }));
            Assert.False(model.LeadsWithHydraPair(pair, pinned with { ParentThreadID = Guid.NewGuid() }));

            model.Settings.HydraEnabled = false;
            Assert.False(model.LeadsWithHydraPair(pair, pinned));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-145")]
    public void Entering_a_same_provider_pair_applies_its_lead_and_updates_the_five_settings_without_clearing_the_session()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "before-model",
            Effort = "low",
            FastMode = false,
            ProviderSessionID = "existing-session",
            HydraEnabled = false,
        });
        var pair = new HydraPair(ProviderKind.Codex)
        {
            OrchestratorModel = "lead-model",
            OrchestratorEffort = "high",
        };

        h.Main.OnMain(() =>
        {
            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("lead-model", "Lead", efforts: new ValueList<string>(["low", "high"]), fastTier: "fast"),
            ]), ProviderKind.Codex);
            model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = true }, ProviderKind.Codex, "lead-model");
            model.Settings.AddHydraPair(pair);
            model.Settings.DefaultProvider = ProviderKind.Grok;
            model.Settings.LastHydraPairID = Guid.NewGuid();
            model.Settings.HydraEnabled = false;
            model.Settings.HydraDefaultEnabled = false;

            model.EnterHydraPair(pair, thread.Id);

            var entered = model.Thread(thread.Id)!;
            Assert.Equal(ProviderKind.Codex, entered.Provider);
            Assert.Equal("existing-session", entered.ProviderSessionID);
            Assert.Equal("lead-model", entered.Model);
            Assert.Equal("high", entered.Effort);
            Assert.True(entered.FastMode);
            Assert.Equal(pair.Id, entered.HydraPairID);
            Assert.True(entered.HydraEnabled);
            Assert.Equal("lead-model", model.Settings.LastModel(ProviderKind.Codex));
            Assert.Equal("high", model.Settings.LastEffort(ProviderKind.Codex));
            Assert.Equal(ProviderKind.Codex, model.Settings.DefaultProvider);
            Assert.Equal(pair.Id, model.Settings.LastHydraPairID);
            Assert.True(model.Settings.HydraEnabled);
            Assert.True(model.Settings.HydraDefaultEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-145")] // App/AppModel+HydraPairs.swift:46
    public void Entering_a_cross_provider_pair_creates_no_runtime_for_a_thread_that_has_none()
    {
        // :46 existingRuntime(for: threadID)?.stopSession(): only a runtime that already exists is stopped; entering
        // a pair never builds one.
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            ProviderSessionID = "old-session",
            HydraEnabled = false,
        });
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-lead", OrchestratorEffort = "high" };

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            Assert.Null(model.ExistingRuntime(thread.Id));

            model.EnterHydraPair(pair, thread.Id);

            Assert.Equal(ProviderKind.Claude, model.Thread(thread.Id)!.Provider);
            Assert.Null(model.ExistingRuntime(thread.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-145")]
    public void Entering_a_cross_provider_pair_clears_the_session_id_and_stops_the_old_session()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "before-model",
            Effort = "low",
            FastMode = true,
            ProviderSessionID = "old-session",
            HydraEnabled = false,
        });
        var pair = new HydraPair(ProviderKind.Claude)
        {
            OrchestratorModel = "claude-lead",
            OrchestratorEffort = "high",
        };
        var stopped = new HydraPairsStopTrackingProviderSession();

        h.Main.OnMain(() =>
        {
            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("claude-lead", "Lead", efforts: new ValueList<string>(["low", "high"]), fastTier: "fast"),
            ]), ProviderKind.Claude);
            model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = true }, ProviderKind.Claude, "claude-lead");
            model.Settings.AddHydraPair(pair);
            model.Settings.DefaultProvider = ProviderKind.Grok;
            model.Settings.HydraEnabled = true;
            model.Settings.HydraDefaultEnabled = false;
            var runtime = model.Runtime(thread.Id);
            SetRuntimeSession(runtime, stopped);

            model.EnterHydraPair(pair, thread.Id);

            var entered = model.Thread(thread.Id)!;
            Assert.Equal(ProviderKind.Claude, entered.Provider);
            Assert.Null(entered.ProviderSessionID);
            Assert.Equal("claude-lead", entered.Model);
            Assert.Equal("high", entered.Effort);
            Assert.True(entered.FastMode);
            Assert.Equal(pair.Id, entered.HydraPairID);
            Assert.True(entered.HydraEnabled);
            Assert.Equal(1, stopped.StopCount);
            Assert.Equal("claude-lead", model.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal("high", model.Settings.LastEffort(ProviderKind.Claude));
            Assert.Equal(ProviderKind.Claude, model.Settings.DefaultProvider);
            Assert.Equal(pair.Id, model.Settings.LastHydraPairID);
            Assert.True(model.Settings.HydraEnabled);
            Assert.True(model.Settings.HydraDefaultEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-146")]
    public void Hydra_lead_keeps_an_unlisted_pair_model_and_effort_then_resolves_supported_preferences_after_catalog_load()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "current-model",
            Effort = "current-effort",
            FastMode = false,
        });
        var pair = new HydraPair(ProviderKind.Codex)
        {
            OrchestratorModel = "not-loaded-yet",
            OrchestratorEffort = "max",
        };

        h.Main.OnMain(() =>
        {
            model.Settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true },
                ProviderKind.Codex, "not-loaded-yet");

            var beforeCatalog = ResolveHydraLead(model, pair, thread);
            Assert.Equal("not-loaded-yet", beforeCatalog.Model);
            Assert.Equal("max", beforeCatalog.Effort);
            Assert.False(beforeCatalog.FastMode);

            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("not-loaded-yet", "Lead", efforts: new ValueList<string>(["low", "high"]), fastTier: "fast", isDefault: true),
            ]), ProviderKind.Codex);
            var afterCatalog = ResolveHydraLead(model, pair, thread);
            Assert.Equal("not-loaded-yet", afterCatalog.Model);
            Assert.Equal("high", afterCatalog.Effort);
            Assert.True(afterCatalog.FastMode);

            var noLeadSameProvider = ResolveHydraLead(model, new HydraPair(ProviderKind.Codex), thread);
            Assert.Equal("current-model", noLeadSameProvider.Model);

            var enteringFromAnotherProvider = ResolveHydraLead(model, new HydraPair(ProviderKind.Codex),
                thread with { Provider = ProviderKind.Claude, Model = "other-model" });
            Assert.Equal("not-loaded-yet", enteringFromAnotherProvider.Model);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-147")]
    public void Updating_a_pair_moves_only_its_Hydra_enabled_leads_and_an_identical_change_is_a_no_op()
    {
        using var h = new HydraPairsAppModelHarness();
        var project = HydraPairsAppModelSamples.Project("Project", h.NewFolder("project"));
        var active = HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "old-model",
            Effort = "low",
            HydraEnabled = true,
        };
        var disabled = HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "disabled-model",
            Effort = "medium",
            HydraEnabled = false,
        };
        h.WriteLibrary([project], [active, disabled]);
        var model = h.MakeModel();
        var pair = new HydraPair(ProviderKind.Codex)
        {
            OrchestratorModel = "old-model",
            OrchestratorEffort = "low",
        };

        h.Main.OnMain(() =>
        {
            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("new-model", "New", efforts: new ValueList<string>(["low", "high"]), fastTier: "fast"),
            ]), ProviderKind.Codex);
            model.Settings.SetPreference(new ModelPreference { Effort = "medium", FastMode = true }, ProviderKind.Codex, "new-model");
            model.Settings.AddHydraPair(pair);
            model.UpdateThread(active.Id, thread => thread with { HydraPairID = pair.Id });
            model.UpdateThread(disabled.Id, thread => thread with { HydraPairID = pair.Id });

            model.UpdateHydraPair(pair.Id, current => current with
            {
                OrchestratorModel = "new-model",
                OrchestratorEffort = "high",
            });

            var updated = model.Thread(active.Id)!;
            Assert.Equal("new-model", updated.Model);
            Assert.Equal("high", updated.Effort);
            Assert.True(updated.FastMode);
            Assert.Equal(pair.Id, updated.HydraPairID);
            Assert.Equal("disabled-model", model.Thread(disabled.Id)!.Model);
            Assert.Equal("new-model", model.Settings.LastModel(ProviderKind.Codex));
            Assert.Equal("high", model.Settings.LastEffort(ProviderKind.Codex));

            h.Defaults.Writes.Clear();
            model.UpdateHydraPair(pair.Id, current => current);
            Assert.Empty(h.Defaults.Writes);
            Assert.Equal(updated, model.Thread(active.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-147")]
    public void Changing_a_pairs_lead_provider_releases_its_threads_without_moving_them()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "codex-model",
            Effort = "medium",
            FastMode = true,
            HydraEnabled = true,
        });
        var pair = new HydraPair(ProviderKind.Codex)
        {
            OrchestratorModel = "codex-model",
            OrchestratorEffort = "high",
        };

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.UpdateThread(thread.Id, current => current with { HydraPairID = pair.Id });

            model.UpdateHydraPair(pair.Id, current => current with
            {
                Provider = ProviderKind.Claude,
                OrchestratorModel = "claude-model",
            });

            var released = model.Thread(thread.Id)!;
            Assert.Null(released.HydraPairID);
            Assert.Equal(ProviderKind.Codex, released.Provider);
            Assert.Equal("codex-model", released.Model);
            Assert.Equal("medium", released.Effort);
            Assert.True(released.FastMode);
            Assert.True(released.HydraEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-148")]
    public void Removing_a_pair_clears_its_chat_and_project_references_and_matching_last_pair_only()
    {
        using var h = new HydraPairsAppModelHarness();
        var projectWithRule = HydraPairsAppModelSamples.Project("With rule", h.NewFolder("with-rule"));
        var projectWithOtherRule = HydraPairsAppModelSamples.Project("Other rule", h.NewFolder("other-rule"));
        var projectWithoutRule = HydraPairsAppModelSamples.Project("No rule", h.NewFolder("no-rule"));
        var pair = new HydraPair(ProviderKind.Codex);
        var otherPair = new HydraPair(ProviderKind.Claude);
        projectWithRule = projectWithRule with { HydraPairID = pair.Id };
        projectWithOtherRule = projectWithOtherRule with { HydraPairID = otherPair.Id };
        var pinned = HydraPairsAppModelSamples.Thread(projectWithRule) with { HydraPairID = pair.Id, HydraEnabled = true, Model = "lead" };
        var secondPinned = HydraPairsAppModelSamples.Thread(projectWithoutRule) with { HydraPairID = pair.Id, HydraEnabled = false, Model = "other" };
        var unrelated = HydraPairsAppModelSamples.Thread(projectWithOtherRule) with { HydraPairID = otherPair.Id, Model = "kept" };
        h.WriteLibrary([projectWithRule, projectWithOtherRule, projectWithoutRule], [pinned, secondPinned, unrelated]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.Settings.AddHydraPair(otherPair);
            model.Settings.LastHydraPairID = pair.Id;

            model.RemoveHydraPair(pair.Id);

            Assert.Null(model.Settings.HydraPair(pair.Id));
            Assert.Null(model.Settings.LastHydraPairID);
            Assert.Null(model.Thread(pinned.Id)!.HydraPairID);
            Assert.Null(model.Thread(secondPinned.Id)!.HydraPairID);
            Assert.Equal("lead", model.Thread(pinned.Id)!.Model);
            Assert.True(model.Thread(pinned.Id)!.HydraEnabled);
            Assert.Equal(otherPair.Id, model.Thread(unrelated.Id)!.HydraPairID);
            Assert.Null(model.Project(projectWithRule.Id)!.HydraPairID);
            Assert.Equal(otherPair.Id, model.Project(projectWithOtherRule.Id)!.HydraPairID);
            Assert.Null(model.Project(projectWithoutRule.Id)!.HydraPairID);

            model.Settings.LastHydraPairID = otherPair.Id;
            model.RemoveHydraPair(otherPair.Id);
            Assert.Null(model.Settings.LastHydraPairID);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-149")]
    public void Project_pair_rule_can_be_set_or_cleared_without_changing_open_threads()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, project, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Model = "open-model",
            Effort = "high",
            HydraPairID = null,
        });
        var pair = new HydraPair(ProviderKind.Codex);

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.SetHydraPair(pair.Id, project.Id);
            Assert.Equal(pair.Id, model.Project(project.Id)!.HydraPairID);
            Assert.Equal(thread, model.Thread(thread.Id));

            model.SetHydraPair(null, project.Id);
            Assert.Null(model.Project(project.Id)!.HydraPairID);
            Assert.Equal(thread, model.Thread(thread.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-150")]
    public void Leaving_a_pair_clears_only_the_chat_pin_and_remembered_pair()
    {
        using var h = new HydraPairsAppModelHarness();
        var (model, _, thread) = MakeModel(h, project => HydraPairsAppModelSamples.Thread(project) with
        {
            Provider = ProviderKind.Claude,
            Model = "claude-model",
            Effort = "medium",
            FastMode = true,
            HydraEnabled = true,
            HydraPairID = Guid.NewGuid(),
        });
        var pairID = thread.HydraPairID!.Value;

        h.Main.OnMain(() =>
        {
            model.Settings.LastHydraPairID = pairID;
            model.Settings.HydraEnabled = false;
            model.Settings.HydraDefaultEnabled = true;

            model.LeaveHydraPair(thread.Id);

            var left = model.Thread(thread.Id)!;
            Assert.Null(left.HydraPairID);
            Assert.Equal(ProviderKind.Claude, left.Provider);
            Assert.Equal("claude-model", left.Model);
            Assert.Equal("medium", left.Effort);
            Assert.True(left.FastMode);
            Assert.True(left.HydraEnabled);
            Assert.Null(model.Settings.LastHydraPairID);
            Assert.False(model.Settings.HydraEnabled);
            Assert.True(model.Settings.HydraDefaultEnabled);
        });
    }

    private static (AppModel Model, Project Project, ChatThread Thread) MakeModel(
        HydraPairsAppModelHarness harness, Func<Project, ChatThread>? makeThread = null)
    {
        var project = HydraPairsAppModelSamples.Project("Project", harness.NewFolder("project"));
        var thread = makeThread?.Invoke(project) ?? HydraPairsAppModelSamples.Thread(project);
        harness.WriteLibrary([project], [thread]);
        return (harness.MakeModel(), project, thread);
    }

    private static (string? Model, string? Effort, bool FastMode) ResolveHydraLead(
        AppModel model, HydraPair pair, ChatThread thread)
    {
        var method = typeof(AppModel).GetMethod("HydraLead", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var value = method!.Invoke(model, [pair, thread]);
        Assert.NotNull(value);
        return ((string? Model, string? Effort, bool FastMode))value;
    }

    private static void SetRuntimeSession(ThreadRuntime runtime, ProviderSession session)
    {
        var field = typeof(ThreadRuntime).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(runtime, session);
    }

}