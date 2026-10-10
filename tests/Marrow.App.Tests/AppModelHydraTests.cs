using System.Linq;
using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.App.Runtime;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+Hydra.swift (B2 1fbf81e), :132-1143; HydraWatchdog :114-127.
// Tests cover launch, reporting, landing, cleanup and routing. Every model is kept inside an
// AppModelHarness so storage, UserDefaults and the main actor use throwaway test state (R-TEST-9).
public sealed class AppModelHydraTests
{
    [Fact]
    [Trait("Req", "PR-HYDRA-104")] // App/AppModel+Hydra.swift:205-210
    public void A_pair_worker_model_without_effort_uses_the_leads_effort_scale()
    {
        using var h=new AppModelHarness();var (model,_,thread)=MakeModel(h,model:"lead-model",effort:"max");
        h.Main.OnMain(()=> {
            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("lead-model","Lead") {Efforts=new ValueList<string>(["low","medium","max"])},
                new ModelOption("worker-model","Worker") {Efforts=new ValueList<string>(["low","high","max"])}]),ProviderKind.Codex);
            var pair=new HydraPair(ProviderKind.Codex) {WorkerModel="worker-model"};model.Settings.AddHydraPair(pair);
            model.Settings.HydraTempersHeadEffort=true;
            var launch=model.HydraLaunch(thread with {HydraPairID=pair.Id});
            Assert.Equal("worker-model",launch!.WorkerModel);Assert.Equal("medium",launch.WorkerEffort);
        });
    }

    [Theory]
    [InlineData(TurnStatus.Completed,false)] [InlineData(TurnStatus.Failed,true)]
    [Trait("Req", "PR-HYDRA-121")] // App/AppModel+Hydra.swift:669-671,680-700
    public void Process_session_is_stopped_synchronously_before_retry_or_landing(TurnStatus status,bool retry)
    {
        using var h=new AppModelHarness();var project=AppModelSamples.Project("Project",h.NewFolder("project"));
        var head=HydraHeadThread(project,Guid.NewGuid(),HydraHeadInfo.Status.Running,0);var turn=Guid.NewGuid();
        h.WriteLibrary([project],[head]);WriteThreadDocument(head.Id,retry ? [UserItem(null,"original brief"),NoticeItem(turn,Notice.Level.Error,"database is locked")] : [],[ClosedTurn(turn,1,status)]);
        var model=h.MakeModel();var runtime=LoadRuntime(h,model,head.Id);
        h.Main.OnMain(()=> {
            var session=new TestProviderSession();SetRuntimeField(runtime,"session",session);
            model.HydraHeadTurnFinished(head,status);
            // This assertion runs before MainActor.Spawn's retry/landing continuation can run.
            Assert.Null(typeof(ThreadRuntime).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime));
            if(retry) Assert.Equal(1L,model.HydraHeadRetries[head.Id]);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-121")] // App/AppModel+Hydra.swift:669-671
    public void Memory_only_api_session_is_retained_when_a_head_finishes()
    {
        using var h=new AppModelHarness();var project=AppModelSamples.Project("Project",h.NewFolder("project"));
        var head=HydraHeadThread(project,Guid.NewGuid(),HydraHeadInfo.Status.Running,0) with {Provider=ProviderKind.Deepseek};
        h.WriteLibrary([project],[head]);WriteThreadDocument(head.Id,[],[ClosedTurn(Guid.NewGuid(),1,TurnStatus.Completed)]);
        var model=h.MakeModel();var runtime=LoadRuntime(h,model,head.Id);var session=new TestProviderSession();
        h.Main.OnMain(()=> {SetRuntimeField(runtime,"session",session);model.HydraHeadTurnFinished(head,TurnStatus.Completed);});
        Assert.Same(session,typeof(ThreadRuntime).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime));
    }

    [Theory]
    [InlineData(TurnStatus.Completed)] [InlineData(TurnStatus.Interrupted)] [InlineData(TurnStatus.Running)]
    [Trait("Req", "PR-HYDRA-121")] // App/AppModel+Hydra.swift:656-663;589
    public void Nonfailed_head_with_no_assistant_message_overwrites_an_old_report_with_empty_text(TurnStatus status)
    {
        using var h=new AppModelHarness();var project=AppModelSamples.Project("Project",h.NewFolder("project"));
        var head=HydraHeadThread(project,Guid.NewGuid(),HydraHeadInfo.Status.Running,0);
        head=head with {Hydra=head.Hydra! with {Summary="old failure text"}};
        h.WriteLibrary([project],[head]);WriteThreadDocument(head.Id,[],[ClosedTurn(Guid.NewGuid(),1,status)]);
        var model=h.MakeModel();_=LoadRuntime(h,model,head.Id);h.Main.OnMain(()=>model.HydraHeadTurnFinished(head,status));h.Main.Loop.RunUntilIdle();
        Assert.Equal("",model.Thread(head.Id)!.Hydra!.Summary);
    }
    [Fact]
    [Trait("Req", "PR-HYDRA-042")] // AppModel+Hydra.swift:141-143
    [Trait("Req", "PR-HYDRA-060")] // AppModel+Hydra.swift:141-143; this file's half of the native-or-API-key union (the other half, ThreadRuntime.KeepsHydraPolicyInSystemPrompt, is proven in Marrow.App.Tests/Runtime/ThreadRuntimeSecondTests.cs)
    public void Hydra_is_native_for_exactly_Claude_Codex_and_Copilot()
    {
        Assert.Equal(
            new[] { ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Copilot },
            SwiftEnum<ProviderKind>.AllCases.Where(AppModel.HydraIsNative));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-099")] // AppModel+Hydra.swift:132-134
    public void A_chat_leads_a_team_only_when_both_switches_are_on_and_it_is_not_a_helper()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h);

        h.Main.OnMain(() =>
        {
            Assert.True(model.HydraIsOn(thread));
            Assert.False(model.HydraIsOn(thread with { HydraEnabled = false }));
            Assert.False(model.HydraIsOn(thread with { ParentThreadID = Guid.NewGuid() }));

            model.Settings.HydraEnabled = false;
            Assert.False(model.HydraIsOn(thread));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-100")] // AppModel+Hydra.swift:148-151
    public void A_pinned_pair_is_used_only_while_it_exists_and_matches_the_chat_provider()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h);
        var pair = new HydraPair(ProviderKind.Codex);

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            var pinned = thread with { HydraPairID = pair.Id };

            Assert.Equal(pair.Id, model.HydraPair(pinned)?.Id);
            Assert.Null(model.HydraPair(pinned with { Provider = ProviderKind.Claude }));

            model.Settings.RemoveHydraPair(pair.Id);
            Assert.Null(model.HydraPair(pinned));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-101")] // AppModel+Hydra.swift:156-160
    public void An_unknown_heads_provider_counts_as_installed_but_a_disabled_one_falls_back()
    {
        using var h = new AppModelHarness();
        var (model, _, _) = MakeModel(h);
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Devin };

        h.Main.OnMain(() =>
        {
            Assert.Equal(ProviderKind.Devin, model.HydraHeadsProvider(pair));
            model.Settings.SetEnabled(false, ProviderKind.Devin);
            Assert.Equal(ProviderKind.Codex, model.HydraHeadsProvider(pair));
            Assert.Equal(ProviderKind.Codex, model.HydraHeadsProvider(pair with { WorkerProvider = ProviderKind.Codex }));
            Assert.Equal(ProviderKind.Codex, model.HydraHeadsProvider(pair with { WorkerProvider = null }));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-102")] // AppModel+Hydra.swift:165-169
    public void A_disabled_provider_fallback_note_uses_the_status_installation_reason()
    {
        using var h = new AppModelHarness();
        var (model, _, _) = MakeModel(h);
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Devin };

        h.Main.OnMain(() =>
        {
            model.Settings.SetEnabled(false, ProviderKind.Devin);

            Assert.Equal(
                "The pair's heads run on Codex for now: Devin is not installed on this PC.",
                model.HydraHeadsFallbackNote(pair));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-103")] // AppModel+Hydra.swift:178-184
    public void Head_effort_is_limited_to_the_medium_or_middle_rung()
    {
        Assert.Equal("medium", AppModel.HydraHeadsEffort("max", ["low", "medium", "high", "max"]));
        Assert.Equal("high", AppModel.HydraHeadsEffort("max", ["low", "high", "max"]));
        Assert.Equal("low", AppModel.HydraHeadsEffort("low", ["low", "medium", "high"]));
        Assert.Equal("other", AppModel.HydraHeadsEffort("other", ["low", "medium", "high"]));
        Assert.Null(AppModel.HydraHeadsEffort(null, ["low", "medium", "high"]));
        Assert.Null(AppModel.HydraHeadsEffort("", ["low", "medium", "high"]));
        Assert.Equal("max", AppModel.HydraHeadsEffort("max", []));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-104")] // AppModel+Hydra.swift:189-229
    public void Hydra_launch_puts_the_chat_project_first_and_applies_caps_and_pair_settings()
    {
        using var h = new AppModelHarness();
        var first = AppModelSamples.Project("First", h.NewFolder("first"));
        var own = AppModelSamples.Project("Own", h.NewFolder("own"));
        var last = AppModelSamples.Project("Last", h.NewFolder("last"));
        var thread = new ChatThread(own.Id, ProviderKind.Codex, "gpt", "high", RuntimeMode.Supervised)
        {
            HydraEnabled = true,
        };
        h.WriteLibrary([first, own, last], [thread]);
        var model = h.MakeModel();
        var pair = new HydraPair(ProviderKind.Codex)
        {
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "claude-haiku",
            WorkerEffort = "high",
            MaxHeads = 6,
        };

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.Settings.HydraMaxHeads = 3;
            model.Settings.HydraIsolateHeads = false;
            model.Settings.HydraAutoMerge = true;
            model.Settings.HydraReviewHeads = true;
            model.Settings.AntiSlopEnabled = false;
            model.UpdateThread(thread.Id, value => value with { HydraPairID = pair.Id });

            var launch = model.HydraLaunch(model.Thread(thread.Id)!);

            Assert.NotNull(launch);
            Assert.Equal(ProviderKind.Claude, launch!.HeadsProvider);
            Assert.False(launch.RunsNatively);
            Assert.Equal("claude-haiku", launch.WorkerModel);
            Assert.Equal("high", launch.WorkerEffort);
            Assert.Equal(3L, launch.MaxHeads);
            Assert.False(launch.IsolatesHeads);
            Assert.True(launch.AutoMerges);
            Assert.True(launch.ReviewsHeads);
            Assert.False(launch.AntiSlopEnabled);
            Assert.EndsWith("on Claude", launch.HeadsLabel!, StringComparison.Ordinal);
            Assert.Equal([own.Path, first.Path, last.Path], launch.Projects.Select(project => project.Path));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-104")] // AppModel+Hydra.swift:189-229
    public void A_heads_provider_fallback_drops_both_worker_fields_and_the_heads_label()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h, model: "gpt", effort: "high");
        var pair = new HydraPair(ProviderKind.Codex)
        {
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "worker-model",
            WorkerEffort = "max",
        };

        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.Settings.SetEnabled(false, ProviderKind.Claude);
            model.Settings.HydraTempersHeadEffort = false;
            model.UpdateThread(thread.Id, value => value with { HydraPairID = pair.Id });

            var launch = model.HydraLaunch(model.Thread(thread.Id)!);

            Assert.NotNull(launch);
            Assert.Equal(ProviderKind.Codex, launch!.HeadsProvider);
            Assert.True(launch.RunsNatively);
            Assert.Null(launch.HeadsLabel);
            Assert.Null(launch.WorkerModel);
            Assert.Null(launch.WorkerEffort);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-104")] // AppModel+Hydra.swift:132-134, :189-190
    public void Hydra_launch_is_nil_when_disabled_or_when_the_chat_is_a_helper()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h);

        h.Main.OnMain(() =>
        {
            Assert.NotNull(model.HydraLaunch(thread));
            Assert.Null(model.HydraLaunch(thread with { HydraEnabled = false }));
            Assert.Null(model.HydraLaunch(thread with { ParentThreadID = Guid.NewGuid() }));

            model.Settings.HydraEnabled = false;
            Assert.Null(model.HydraLaunch(thread));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-105")] // AppModel+Hydra.swift:234-256
    public void SetHydra_updates_the_app_default_and_adopts_the_picked_pairs_effort()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h, model: "existing-model", effort: "low");

        h.Main.OnMain(() =>
        {
            model.SetHydra(false, thread.Id);
            Assert.False(model.Thread(thread.Id)!.HydraEnabled);
            Assert.False(model.Settings.HydraDefaultEnabled);

            var pair = new HydraPair(ProviderKind.Codex) { OrchestratorEffort = "high" };
            model.Settings.AddHydraPair(pair);
            model.SetHydra(true, thread.Id);

            var updated = model.Thread(thread.Id)!;
            Assert.True(updated.HydraEnabled);
            Assert.Equal(pair.Id, updated.HydraPairID);
            Assert.Equal("existing-model", updated.Model);
            Assert.Equal("high", updated.Effort);
            Assert.True(model.Settings.HydraDefaultEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-106")] // AppModel+Hydra.swift:261-309
    public void Team_queries_scope_heads_to_the_parent_and_separate_panel_and_running_states()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var lead = AppModelSamples.Thread(project);
        ChatThread Head(string title, long index, HydraHeadInfo.Kind kind, HydraHeadInfo.Status status, bool inPanel = true) =>
            new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                Title = title,
                ParentThreadID = lead.Id,
                IsInPanel = inPanel,
                Hydra = AppModelSamples.Head(status) with { Index = index, HydraHeadInfoKind = kind },
            };
        var droppy = Head("droppy", 2, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Status.Running);
        var native = Head("native", 0, HydraHeadInfo.Kind.Native, HydraHeadInfo.Status.Running);
        var finished = Head("finished", 1, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Status.Completed, inPanel: false);
        var otherLead = AppModelSamples.Thread(project);
        var otherHead = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = otherLead.Id,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        h.WriteLibrary([project], [lead, droppy, native, finished, otherLead, otherHead]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            foreach (var head in new[] { droppy, native })
                model.UpdateThread(head.Id, thread => thread with
                {
                    Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
                });

            Assert.Equal([native.Id, droppy.Id], model.HydraHeads(lead.Id).Select(thread => thread.Id));
            Assert.Equal([native.Id, finished.Id, droppy.Id], model.HydraTeam(lead.Id).Select(thread => thread.Id));
            Assert.Equal(1L, model.RunningDroppyHeads(lead.Id));
            Assert.Equal(2L, model.RunningHydraHeads(lead.Id));
            Assert.Equal([droppy.Hydra!.Persona.Name], model.WorkingHydraHeadNames(lead.Id));
            Assert.Empty(model.WorkingHydraHeadNames(lead.Id, [2]));
            Assert.Equal(0L, model.RunningHydraHeads(otherHead.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-107")] // AppModel+Hydra.swift:283-295
    public void An_unmerged_file_count_is_zero_until_the_lead_has_a_runtime()
    {
        using var h = new AppModelHarness();
        var (model, _, thread) = MakeModel(h);

        h.Main.OnMain(() => Assert.Equal(0L, model.HydraUnmergedFileCount(thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-108")] // AppModel+Hydra.swift:319-343
    public void Hydra_project_prefers_case_insensitive_names_and_the_longest_containing_path()
    {
        using var h = new AppModelHarness();
        var rootPath = h.NewFolder("project");
        var nestedPath = Path.Combine(rootPath, "nested");
        Directory.CreateDirectory(nestedPath);
        var root = AppModelSamples.Project("Project", rootPath);
        var nested = AppModelSamples.Project("Nested", nestedPath);
        var lead = AppModelSamples.Thread(root);
        h.WriteLibrary([root, nested], [lead]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Equal(root.Id, model.HydraProject("pRoJeCt", lead)!.Id);
            Assert.Equal(nested.Id, model.HydraProject(Path.Combine(nestedPath, "work.cs"), lead)!.Id);
            Assert.Equal(root.Id, model.HydraProject(null, lead)!.Id);
            Assert.Null(model.HydraProject(@"R:\no-such-project", lead));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-108")] // AppModel+Hydra.swift:330-343
    public void Hydra_project_adds_an_existing_git_root_to_the_sidebar()
    {
        using var h = new AppModelHarness();
        var root = AppModelSamples.Project("Root", h.NewFolder("root"));
        var lead = AppModelSamples.Thread(root);
        var repositoryPath = h.NewFolder("external");
        _ = AppModelGitRepo.Empty(repositoryPath);
        h.WriteLibrary([root], [lead]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            var added = model.HydraProject(repositoryPath, lead);

            Assert.NotNull(added);
            Assert.Equal(repositoryPath, added!.Path);
            Assert.Contains(model.Projects, project => project.Id == added.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-109")] // AppModel+Hydra.swift:398-428
    public void Roster_allocation_restarts_for_an_empty_team_and_preserves_a_pinned_name()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var emptyLead = AppModelSamples.Thread(project) with { HydraSpawnCount = 25 };
        var pinnedLead = AppModelSamples.Thread(project) with { HydraSpawnCount = 8 };
        h.WriteLibrary([project], [emptyLead, pinnedLead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);

        ChatThread? reset = null;
        ChatThread? pinned = null;
        ChatThread? next = null;
        long counterAfterPin = -1;
        h.Main.OnMain(() =>
        {
            reset = SpawnDroppy(model, emptyLead.Id, task: "reset");
            pinned = SpawnDroppy(model, pinnedLead.Id, task: "pin Gus", preferredIndex: 24);
            counterAfterPin = model.Thread(pinnedLead.Id)!.HydraSpawnCount;
            next = SpawnDroppy(model, pinnedLead.Id, task: "next");
        });

        Assert.Equal(0L, reset!.Hydra!.Index); // Empty teams restart at Hank even when the old counter is high.
        Assert.Equal(24L, pinned!.Hydra!.Index);
        Assert.Equal(9L, counterAfterPin); // A pin advances an 8 counter by exactly one.
        Assert.Equal(9L, next!.Hydra!.Index); // Pinning Gus leaves the next default head at Ezra.
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-109")] // AppModel+Hydra.swift:398-428
    public void Roster_allocation_climbs_by_roster_size_only_when_the_announced_holder_is_finished()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var finishedLead = AppModelSamples.Thread(project);
        var finishedTova = AppModelSamples.Thread(project) with
        {
            ParentThreadID = finishedLead.Id,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { Index = 23 },
        };
        var workingLead = AppModelSamples.Thread(project);
        var workingHank = AppModelSamples.Thread(project) with
        {
            ParentThreadID = workingLead.Id,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with { Index = 0 },
        };
        h.WriteLibrary([project], [finishedLead, finishedTova, workingLead, workingHank]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);

        // AppModel startup normalizes persisted running heads to stopped; restore the
        // precondition so the active Hank is not treated as a finished roster slot.
        h.Main.OnMain(() => model.UpdateThread(workingHank.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
        }));

        ChatThread? finishedCollision = null;
        ChatThread? workingCollision = null;
        h.Main.OnMain(() =>
        {
            finishedCollision = SpawnDroppy(model, finishedLead.Id, "Tova again", preferredIndex: 23);
            workingCollision = SpawnDroppy(model, workingLead.Id, "Hank collision", preferredIndex: 0);
        });

        Assert.Equal(48L, finishedCollision!.Hydra!.Index); // Finished Tova (23) advances one full 25-name round.
        Assert.Equal(1L, workingCollision!.Hydra!.Index); // An active Hank is skipped by the ordinary fallback.
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-110")] // AppModel+Hydra.swift:438-441; OD-0008 B4
    public void An_unresolved_project_note_uses_the_raw_reference_and_keeps_the_head_in_the_leads_project()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Lead Project", h.NewFolder("lead"));
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        const string reference = @"E:\missing\Somewhere";
        h.PrepareBootstrap(model);

        ChatThread? head = null;
        h.Main.OnMain(() => head = SpawnDroppy(model, lead.Id, "look there", project: reference));

        Assert.NotNull(head);
        Assert.Equal(project.Id, head!.ProjectID);
        h.Main.Loop.RunUntil(
            () => HydraUserMessages(model.ExistingRuntime(lead.Id)).Any(message => message.IsFromHydra && message.Text ==
                $"Hydra: no project named {reference} is in the sidebar or on this PC, so {head.Hydra!.Persona.Name} works in Lead Project."),
            AppModelHarness.Bound);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-110")] // AppModel+Hydra.swift:438-441; OD-0008 B4
    public void An_unresolved_project_note_names_the_chat_project_when_its_project_is_missing()
    {
        using var h = new AppModelHarness();
        var lead = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised);
        h.WriteLibrary([], [lead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        const string reference = "A project absent from the sidebar";

        ChatThread? head = null;
        h.Main.OnMain(() => head = SpawnDroppy(model, lead.Id, "look there", project: reference));

        Assert.NotNull(head);
        Assert.Equal(lead.ProjectID, head!.ProjectID);
        h.Main.Loop.RunUntil(
            () => HydraUserMessages(model.ExistingRuntime(lead.Id)).Any(message => message.IsFromHydra && message.Text ==
                $"Hydra: no project named {reference} is in the sidebar or on this PC, so {head.Hydra!.Persona.Name} works in this chat's project."),
            AppModelHarness.Bound);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-111")] // AppModel+Hydra.swift:446-461
    [Trait("Req", "PR-HYDRA-112")] // AppModel+Hydra.swift:462-475
    [Trait("Req", "PR-HYDRA-113")] // AppModel+Hydra.swift:476-491
    public void A_routed_droppy_head_uses_profile_precedence_title_and_canonical_metadata()
    {
        using var h = new AppModelHarness();
        var own = AppModelSamples.Project("Own", h.NewFolder("own"));
        var other = AppModelSamples.Project("Other Project", h.NewFolder("other"));
        var lead = AppModelSamples.Thread(own) with
        {
            WorktreePath = h.NewFolder("lead-worktree"),
            Branch = "feature/lead",
            FastMode = true,
        };
        h.WriteLibrary([own, other], [lead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var profile = new HydraHeadProfile("Canonical Route")
        {
            Provider = ProviderKind.Deepseek,
            Model = "profile-model",
            Effort = "profile-effort",
        };
        var pair = new HydraPair(ProviderKind.Codex)
        {
            WorkerProvider = ProviderKind.Claude,
            WorkerModel = "launch-model",
            WorkerEffort = "launch-effort",
            HeadProfiles = new ValueList<HydraHeadProfile>([profile]),
        };
        const string longTask = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

        ChatThread? head = null;
        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.UpdateThread(lead.Id, thread => thread with { HydraPairID = pair.Id });
            head = SpawnDroppy(model, lead.Id, longTask + "\nsecond line", project: "Other Project", profile: "canonical-route");
        });

        Assert.NotNull(head);
        Assert.Equal(ProviderKind.Deepseek, head!.Provider); // Routed profile wins over launch provider.
        Assert.Equal("profile-model", head.Model);
        Assert.Equal("profile-effort", head.Effort);
        Assert.Equal(RuntimeMode.FullAccess, head.RuntimeMode);
        Assert.False(head.FastMode);
        Assert.Equal("Hank · " + longTask[..59] + "\u2026 · Other Project · Canonical Route", head.Title);
        Assert.True(head.HasCustomTitle);
        Assert.True(head.IsInPanel);
        Assert.Equal(lead.Id, head.ParentThreadID);
        Assert.Equal(other.Id, head.ProjectID);
        Assert.Null(head.WorktreePath); // A head in another project does not inherit the lead worktree.
        Assert.Null(head.Branch);
        Assert.Equal("Canonical Route", head.Hydra!.Profile);
        Assert.True(head.Hydra.IsBackground);
        Assert.True(head.Hydra.CanStop);
        Assert.Null(head.Hydra.BatchID);
        var leadRuntime = model.ExistingRuntime(lead.Id);
        Assert.NotNull(leadRuntime);
        Assert.False(leadRuntime!.IsHydraPanelHidden);
        Assert.Equal(head.Id, leadRuntime.HydraSelectedHeadID);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-111")] // AppModel+Hydra.swift:446-461
    [Trait("Req", "PR-HYDRA-113")] // AppModel+Hydra.swift:476-491
    [Trait("Req", "PR-HYDRA-114")] // AppModel+Hydra.swift:354-361
    public void A_native_head_mirrors_the_spawn_and_rehearses_its_prompt_without_a_copy()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with
        {
            Provider = ProviderKind.Claude,
            RuntimeMode = RuntimeMode.AutoAcceptEdits,
        };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var spawn = new AgentSpawn
        {
            Id = "provider-native-id",
            TaskID = null,
            ToolUseID = "provider-tool-use-id",
            Description = "Inspect the implementation",
            Prompt = "Read the packet and report the exact API gaps.",
            Model = "native-model",
            IsBackground = false,
        };

        ChatThread? head = null;
        h.Main.OnMain(() => head = model.SpawnNativeHead(lead.Id, spawn));

        Assert.NotNull(head);
        Assert.Equal(lead.Provider, head!.Provider);
        Assert.Equal("native-model", head.Model);
        Assert.Equal(lead.Effort, head.Effort);
        Assert.Equal(lead.RuntimeMode, head.RuntimeMode);
        Assert.False(head.FastMode);
        Assert.Equal(spawn.Description, head.Hydra!.Task);
        Assert.Equal(HydraHeadInfo.Kind.Native, head.Hydra.HydraHeadInfoKind);
        Assert.Equal(HydraHeadInfo.Origin.Delegated, head.Hydra.HydraHeadInfoOrigin);
        Assert.Equal(spawn.Id, head.Hydra.NativeID);
        Assert.Equal(spawn.TaskID, head.Hydra.NativeTaskID);
        Assert.Equal(spawn.ToolUseID, head.Hydra.ToolUseID);
        Assert.False(head.Hydra.IsBackground);
        Assert.False(head.Hydra.CanStop); // A native head needs a provider task id unless the lead is Codex.
        Assert.False(head.Hydra.HasOwnCopy);
        Assert.Contains(spawn.Prompt!, HydraUserMessages(model.ExistingRuntime(head.Id)).Select(message => message.Text));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-113")] // AppModel+Hydra.swift:476-491
    public void A_native_head_is_stoppable_with_a_provider_task_id_or_a_Codex_lead()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var claudeLead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude };
        var codexLead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Codex };
        h.WriteLibrary([project], [claudeLead, codexLead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        AgentSpawn Spawn(string id, string? taskID) => new()
        {
            Id = id,
            TaskID = taskID,
            Description = "task",
            Prompt = "brief",
        };

        ChatThread? claudeTaskHead = null;
        ChatThread? codexHead = null;
        h.Main.OnMain(() =>
        {
            claudeTaskHead = model.SpawnNativeHead(claudeLead.Id, Spawn("claude", "task-id"));
            codexHead = model.SpawnNativeHead(codexLead.Id, Spawn("codex", null));
        });

        Assert.True(claudeTaskHead!.Hydra!.CanStop);
        Assert.True(codexHead!.Hydra!.CanStop);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-112")] // AppModel+Hydra.swift:462-475
    public void An_empty_head_task_uses_the_bare_persona_name_and_keeps_the_lead_worktree_in_the_same_project()
    {
        using var h = new AppModelHarness();
        var worktree = h.NewFolder("lead-worktree");
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { WorktreePath = worktree, Branch = "feature/lead" };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);

        ChatThread? head = null;
        h.Main.OnMain(() => head = SpawnDroppy(model, lead.Id, task: ""));

        Assert.NotNull(head);
        Assert.Equal(head!.Hydra!.Persona.Name, head.Title);
        Assert.True(head.HasCustomTitle);
        Assert.Equal(worktree, head.WorktreePath);
        Assert.Equal("feature/lead", head.Branch);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-115")] // AppModel+Hydra.swift:371-385, :495-528
    [Trait("Req", "PR-HYDRA-116")] // AppModel+Hydra.swift:495-528
    public void A_droppy_head_is_inserted_before_its_brief_closure_runs_and_sends_the_attachments()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var attachment = new Attachment { Name = "brief.txt", Path = Path.Combine(h.Scratch, "brief.txt"), MimeType = "text/plain" };
        File.WriteAllText(attachment.Path, "reference");
        HydraPersona? briefPersona = null;
        HydraPrompts.Workplace? briefWorkplace = null;
        ChatThread? head = null;

        h.Main.OnMain(() =>
        {
            head = model.SpawnDroppyHead(
                lead.Id,
                "Read the attachment",
                HydraHeadInfo.Origin.Delegated,
                [attachment],
                null,
                null,
                null,
                null,
                (persona, workplace) =>
                {
                    briefPersona = persona;
                    briefWorkplace = workplace;
                    return "The generated brief.";
                });
            Assert.NotNull(model.Thread(head!.Id)); // Creation is synchronous, prep and send are deferred.
        });

        Assert.NotNull(head);
        h.Main.Loop.RunUntil(() => briefPersona is not null, AppModelHarness.Bound);
        Assert.Equal(head!.Hydra!.Persona.Name, briefPersona!.Name);
        Assert.IsType<HydraPrompts.Workplace.Shared>(briefWorkplace);
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.ExecutionStartedAt is not null, AppModelHarness.Bound);
        var startedHead = model.Thread(head.Id)!.Hydra!;
        Assert.NotNull(startedHead.ExecutionStartedAt);
        Assert.Null(startedHead.ExecutionFinishedAt);
        h.Main.Loop.RunUntil(
            () => HydraUserMessages(model.ExistingRuntime(head.Id)).Any(message =>
                message.HydraBrief == true && message.Text == "The generated brief." && message.Attachments.Contains(attachment)),
            AppModelHarness.Bound);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-041")] // AppModel+Hydra.swift:506-514
    [Trait("Req", "PR-HYDRA-116")] // AppModel+Hydra.swift:495-528
    public void Isolation_falls_back_to_the_shared_checkout_when_the_checkout_has_no_commit()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.Empty(h.NewFolder("empty-repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = true);
        h.PrepareBootstrap(model);
        HydraPrompts.Workplace? resolved = null;
        ChatThread? head = null;

        h.Main.OnMain(() => head = model.SpawnDroppyHead(
            lead.Id,
            "Inspect the empty checkout",
            HydraHeadInfo.Origin.Delegated,
            [],
            null,
            null,
            null,
            null,
            (_, workplace) => { resolved = workplace; return "brief"; }));

        Assert.NotNull(head);
        h.Main.Loop.RunUntil(() => resolved is not null, AppModelHarness.Bound);
        Assert.Equal(new HydraPrompts.Workplace.Shared(repository.Root), resolved);
        Assert.Null(model.Thread(head!.Id)!.Hydra!.BaseTree);
        Assert.Null(model.Thread(head.Id)!.WorktreePath);
        Assert.False(model.Thread(head.Id)!.Hydra!.HasOwnCopy);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-041")] // AppModel+Hydra.swift:506-514
    public void A_valid_checkout_stays_shared_when_head_isolation_is_off()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        HydraPrompts.Workplace? resolved = null;
        ChatThread? head = null;

        h.Main.OnMain(() => head = model.SpawnDroppyHead(
            lead.Id,
            "Use the shared checkout",
            HydraHeadInfo.Origin.Delegated,
            [],
            null,
            null,
            null,
            null,
            (_, workplace) => { resolved = workplace; return "brief"; }));

        Assert.NotNull(head);
        h.Main.Loop.RunUntil(() => resolved is not null, AppModelHarness.Bound);
        Assert.Equal(new HydraPrompts.Workplace.Shared(repository.Root), resolved);
        Assert.Null(model.Thread(head!.Id)!.Hydra!.BaseTree);
        Assert.Null(model.Thread(head.Id)!.WorktreePath);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-041")] // AppModel+Hydra.swift:506-514
    [Trait("Req", "PR-HYDRA-117")] // AppModel+Hydra.swift:535-557
    [Trait("Req", "PR-HYDRA-118")] // AppModel+Hydra.swift:28-107
    public void Isolated_heads_share_a_cached_starting_tree_then_recapture_after_checkout_changes()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project Name", repository.Root);
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = true);
        h.PrepareBootstrap(model);

        HydraPrompts.Workplace? firstWorkplace = null;
        ChatThread? first = null;
        ChatThread? second = null;
        h.Main.OnMain(() =>
        {
            first = model.SpawnDroppyHead(
                lead.Id,
                "first copy",
                HydraHeadInfo.Origin.Delegated,
                [],
                null,
                null,
                null,
                null,
                (_, workplace) => { firstWorkplace = workplace; return "brief"; });
            second = SpawnDroppy(model, lead.Id, "second copy");
        });
        Assert.NotNull(first);
        Assert.NotNull(second);
        h.Main.Loop.RunUntil(
            () => model.Thread(first!.Id)?.Hydra?.BaseTree is not null && model.Thread(second!.Id)?.Hydra?.BaseTree is not null,
            AppModelHarness.Bound);

        var firstCopy = model.Thread(first!.Id)!;
        var secondCopy = model.Thread(second!.Id)!;
        Assert.Equal(firstCopy.Hydra!.BaseTree, secondCopy.Hydra!.BaseTree);
        var workplace = Assert.IsType<HydraPrompts.Workplace.OwnCopy>(firstWorkplace);
        Assert.Equal(firstCopy.WorktreePath, workplace.Path);
        Assert.Null(firstCopy.Branch);
        Assert.True(firstCopy.Hydra.HasOwnCopy);
        Assert.True(Directory.Exists(firstCopy.WorktreePath));
        var personaSlug = firstCopy.Hydra.Persona.Name.Replace(' ', '-').ToLowerInvariant();
        var expectedDirectory = $"project-name-{personaSlug}-{SwiftString.Lowercased(SwiftUUID.UuidString(first.Id))[..8]}";
        Assert.Equal(expectedDirectory, Path.GetFileName(firstCopy.WorktreePath));
        Assert.Equal("Nth: a Hydra head's starting point", AppModelGitRepo.At(firstCopy.WorktreePath!).Git("log", "-1", "--format=%s").Trim());

        File.WriteAllText(Path.Combine(repository.Root, "a.txt"), "changed\n");
        repository.Git("add", "a.txt");
        ChatThread? afterChange = null;
        h.Main.OnMain(() => afterChange = SpawnDroppy(model, lead.Id, "after checkout change"));
        Assert.NotNull(afterChange);
        h.Main.Loop.RunUntil(
            () => model.Thread(afterChange!.Id)?.Hydra?.BaseTree is not null,
            AppModelHarness.Bound);
        Assert.NotEqual(firstCopy.Hydra.BaseTree, model.Thread(afterChange!.Id)!.Hydra!.BaseTree);
    }

    [Theory]
    [InlineData(null, "current tool output")]
    [InlineData("current finish", "current finish")]
    [Trait("Req", "PR-HYDRA-119")] // AppModel+Hydra.swift:588-592,615-619; ThreadRuntime.swift:2702-2711.
    public void A_native_finish_forwards_only_its_current_summary_while_retaining_the_stored_summary(
        string? currentSummary, string expectedToolOutput)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0);
        head = head with
        {
            Hydra = head.Hydra! with
            {
                HydraHeadInfoKind = HydraHeadInfo.Kind.Native,
                NativeID = "native-head",
                ToolUseID = "agent-tool",
                IsBackground = false,
                Summary = "retained old summary",
            },
        };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = LoadRuntime(h, model, lead.Id);
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = false;
            RestoreRunningHead(model, head.Id);
            SetRuntimeField(runtime, "phase", RuntimePhase.Running);
            runtime.Rehearse(new ProviderEvent.ToolStarted("agent-tool", new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Agent,
                Title = "Current task",
                Output = "current tool output",
            }));

            model.FinishHydraHead(head.Id, TurnStatus.Completed, currentSummary);

            var call = Assert.IsType<TimelineItem.Content.Tool>(
                runtime.Entries.Single(entry => entry.Id == "agent-tool").Item.TimelineItemContent).Value;
            Assert.Equal(ToolCall.Status.Completed, call.ToolCallStatus);
            Assert.Equal(expectedToolOutput, call.Output);
            Assert.Equal(currentSummary ?? "retained old summary", model.Thread(head.Id)!.Hydra!.Summary);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRA-119")] // AppModel+Hydra.swift:588-592,615-619; ThreadRuntime.swift:2713-2727.
    public void A_Droppy_finish_reports_current_summary_and_landing_without_replaying_retained_values(bool hasCurrentPayload)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var previousLanding = new HydraLanding { PatchPath = "previous.patch" };
        var currentLanding = hasCurrentPayload ? new HydraLanding { PatchPath = "current.patch" } : null;
        var currentSummary = hasCurrentPayload ? "current report" : null;
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0);
        head = head with { Hydra = head.Hydra! with { Summary = "retained old report", Landing = previousLanding } };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = LoadRuntime(h, model, lead.Id);
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = false;
            RestoreRunningHead(model, head.Id);
            SetRuntimeField(runtime, "phase", RuntimePhase.Running); // Keep the report queued; no provider turn starts.

            model.FinishHydraHead(head.Id, TurnStatus.Completed, currentSummary, currentLanding);

            var field = typeof(ThreadRuntime).GetField("hydraPendingReports", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var report = Assert.Single(Assert.IsType<List<HydraReport>>(field.GetValue(runtime)));
            Assert.Equal(currentSummary ?? "", report.Text);
            Assert.Equal(currentLanding, report.Landing);
            var stored = model.Thread(head.Id)!.Hydra!;
            Assert.Equal(currentSummary ?? "retained old report", stored.Summary);
            Assert.Equal(currentLanding ?? previousLanding, stored.Landing);
        });
    }

    [Theory]
    [InlineData(TurnStatus.Completed, HydraHeadInfo.Status.Completed)]
    [InlineData(TurnStatus.Failed, HydraHeadInfo.Status.Failed)]
    [InlineData(TurnStatus.Interrupted, HydraHeadInfo.Status.Stopped)]
    [InlineData(TurnStatus.Running, HydraHeadInfo.Status.Running)]
    [Trait("Req", "PR-HYDRA-119")] // AppModel+Hydra.swift:572-629
    public void Finishing_a_head_maps_the_turn_status_and_keeps_a_parentless_report_on_the_head(
        TurnStatus turnStatus,
        HydraHeadInfo.Status expected)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var now = SwiftDate.Now;
        var head = AppModelSamples.Thread(project) with
        {
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with
            {
                Summary = "old summary",
                ToolCalls = 7,
                Tokens = 50,
            },
        };
        h.WriteLibrary([project], [head]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.UpdateThread(head.Id, thread => thread with
            {
                Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            });
            model.FinishHydraHead(head.Id, turnStatus, "final report");
        });

        var finished = model.Thread(head.Id)!;
        Assert.Equal(expected, finished.Hydra!.HydraHeadInfoStatus);
        Assert.Equal("final report", finished.Hydra.Summary);
        Assert.Equal(7L, finished.Hydra.ToolCalls);
        Assert.Equal(50L, finished.Hydra.Tokens);
        Assert.NotNull(finished.Hydra.FinishedAt);
        Assert.True(finished.Hydra.FinishedAt!.Value >= now);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-120")] // AppModel+Hydra.swift:602-628; HydraPanel.swift:41
    [Trait("Req", "PR-HYDRA-169")] // AppModel+Hydra.swift:604 (windows_path: the auto-clear delay is the hold plus 200 ms)
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169: the auto-clear delay clause only
    public void Auto_clear_waits_for_the_finish_wave_then_removes_the_head_and_its_five_panel_fields()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 0);
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        var timerCount = h.Main.Time.ActiveTimers;
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = true;
            RestoreRunningHead(model, head.Id);
            runtime.HydraSelectedHeadID = head.Id;
            runtime.HydraPoppedHeadID = head.Id;
            runtime.HydraAutoPopHeld = new ValueSet<Guid>([head.Id]);
            runtime.HydraAutoPanelDocks = new ValueMap<Guid, PanelDockCorner>([KeyValuePair.Create(head.Id, PanelDockCorner.TopTrailing)]);
            runtime.PanelStackOrder = new ValueList<FloatingPanelID>([new FloatingPanelID.Auto(head.Id), new FloatingPanelID.Hydra()]);
            model.FinishHydraHead(head.Id, TurnStatus.Completed, "done");
        });

        h.Main.Loop.RunUntil(() => h.Main.Time.ActiveTimers > timerCount, AppModelHarness.Bound);
        h.Main.Advance(TimeSpan.FromMilliseconds(2_199));
        Assert.True(model.Thread(head.Id)!.IsInPanel);
        h.Main.Advance(TimeSpan.FromMilliseconds(1));
        h.Main.Loop.RunUntil(() => !model.Thread(head.Id)!.IsInPanel, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Null(runtime.HydraSelectedHeadID);
            Assert.Null(runtime.HydraPoppedHeadID);
            Assert.Empty(runtime.HydraAutoPopHeld);
            Assert.Empty(runtime.HydraAutoPanelDocks);
            Assert.Equal([new FloatingPanelID.Hydra()], runtime.PanelStackOrder);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-120")] // AppModel+Hydra.swift:602-614 reads the current head and parent after the hold.
    public void Auto_clear_uses_the_heads_current_parent_after_the_finish_hold()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var originalLead = AppModelSamples.Thread(project);
        var currentLead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, originalLead.Id, HydraHeadInfo.Status.Running, 0);
        // A provider head with no tool row completes locally, without scheduling a report turn.
        head = head with { Hydra = head.Hydra! with { HydraHeadInfoKind = HydraHeadInfo.Kind.Native, ToolUseID = null } };
        h.WriteLibrary([project], [originalLead, currentLead, head]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var originalRuntime = h.Main.OnMain(() => model.Runtime(originalLead.Id));
        var currentRuntime = h.Main.OnMain(() => model.Runtime(currentLead.Id));
        var timerCount = h.Main.Time.ActiveTimers;
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = true;
            RestoreRunningHead(model, head.Id);
            originalRuntime.HydraSelectedHeadID = head.Id;
            model.FinishHydraHead(head.Id, TurnStatus.Completed, "done");
        });
        h.Main.Loop.RunUntil(() => h.Main.Time.ActiveTimers > timerCount, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            model.UpdateThread(head.Id, thread => thread with { ParentThreadID = currentLead.Id });
            currentRuntime.HydraSelectedHeadID = head.Id;
            currentRuntime.HydraPoppedHeadID = head.Id;
            currentRuntime.HydraAutoPopHeld = new ValueSet<Guid>([head.Id]);
            currentRuntime.HydraAutoPanelDocks = new ValueMap<Guid, PanelDockCorner>(
                [KeyValuePair.Create(head.Id, PanelDockCorner.TopTrailing)]);
            currentRuntime.PanelStackOrder = new ValueList<FloatingPanelID>(
                [new FloatingPanelID.Auto(head.Id), new FloatingPanelID.Hydra()]);
        });

        h.Main.Advance(TimeSpan.FromMilliseconds(2_200));
        h.Main.Loop.RunUntil(() => !model.Thread(head.Id)!.IsInPanel, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Equal(currentLead.Id, model.Thread(head.Id)!.ParentThreadID);
            Assert.Null(currentRuntime.HydraSelectedHeadID);
            Assert.Null(currentRuntime.HydraPoppedHeadID);
            Assert.Empty(currentRuntime.HydraAutoPopHeld);
            Assert.Empty(currentRuntime.HydraAutoPanelDocks);
            Assert.Equal([new FloatingPanelID.Hydra()], currentRuntime.PanelStackOrder);
            Assert.Equal(head.Id, originalRuntime.HydraSelectedHeadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-121")] // AppModel+Hydra.swift:634-716
    [Trait("Req", "PR-HYDRA-122")] // AppModel+Hydra.swift:721-726
    public void Failed_reports_use_only_the_closed_turn_error_and_label_trimmed_prose_as_partial()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var first = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 0);
        var second = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 1);
        var oldTurn = Guid.NewGuid();
        var firstTurn = Guid.NewGuid();
        var secondTurn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, first, second]);
        WriteThreadDocument(first.Id,
        [
            UserItem(null, "first brief", isFromHydra: true),
            NoticeItem(oldTurn, Notice.Level.Error, "an error from the earlier turn"),
            AssistantItem(oldTurn, "earlier report"),
            NoticeItem(firstTurn, Notice.Level.Warning, "not a failure cause"),
            AssistantItem(firstTurn, "  unfinished words  "),
            ToolItem(firstTurn, "Read", new FileEdit("src/file.cs")),
        ], [ClosedTurn(oldTurn, 1, TurnStatus.Failed), ClosedTurn(firstTurn, 2, TurnStatus.Failed)]);
        WriteThreadDocument(second.Id,
        [
            UserItem(null, "second brief", isFromHydra: true),
            NoticeItem(secondTurn, Notice.Level.Error, "the current turn failed"),
            AssistantItem(secondTurn, "   "),
        ], [ClosedTurn(secondTurn, 1, TurnStatus.Failed)]);
        var model = h.MakeModel();
        var firstRuntime = LoadRuntime(h, model, first.Id);
        _ = LoadRuntime(h, model, second.Id);
        var now = SwiftDate.Now;

        h.Main.OnMain(() =>
        {
            model.HydraHeadTurnFinished(first, TurnStatus.Failed);
            model.HydraHeadTurnFinished(second, TurnStatus.Failed);
        });

        h.Main.Loop.RunUntil(
            () => model.Thread(first.Id)?.Hydra?.IsFinished == true && model.Thread(second.Id)?.Hydra?.IsFinished == true,
            AppModelHarness.Bound);
        var firstFinished = model.Thread(first.Id)!.Hydra!;
        var secondFinished = model.Thread(second.Id)!.Hydra!;
        Assert.Equal("The turn failed without a recorded error.\n\nPartial output before the failure:\n\nunfinished words", firstFinished.Summary);
        Assert.Equal("the current turn failed", secondFinished.Summary);
        Assert.Equal(1L, firstFinished.ToolCalls);
        Assert.NotNull(firstFinished.ExecutionFinishedAt);
        Assert.True(firstFinished.ExecutionFinishedAt >= now);
        Assert.DoesNotContain("earlier report", firstRuntime.SentPrompts);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-121")] // AppModel+Hydra.swift:634-716
    public void A_completed_head_reports_the_last_assistant_message_and_counts_tool_entries()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var head = HydraHeadThread(project, Guid.NewGuid(), HydraHeadInfo.Status.Running, 0);
        var oldTurn = Guid.NewGuid();
        var currentTurn = Guid.NewGuid();
        h.WriteLibrary([project], [head]);
        WriteThreadDocument(head.Id,
        [
            NoticeItem(oldTurn, Notice.Level.Error, "old failure"),
            AssistantItem(oldTurn, "old report"),
            AssistantItem(currentTurn, "first current reply"),
            ToolItem(currentTurn, "Edit", new FileEdit("a.cs"), new FileEdit("b.cs")),
            AssistantItem(currentTurn, "final current reply"),
        ], [ClosedTurn(oldTurn, 1, TurnStatus.Failed), ClosedTurn(currentTurn, 2, TurnStatus.Completed)]);
        var model = h.MakeModel();
        var runtime = LoadRuntime(h, model, head.Id);
        var now = SwiftDate.Now;

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Completed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.IsFinished == true, AppModelHarness.Bound);

        Assert.Equal("final current reply", model.Thread(head.Id)!.Hydra!.Summary);
        Assert.Equal(1L, model.Thread(head.Id)!.Hydra!.ToolCalls);
        Assert.True(model.Thread(head.Id)!.Hydra!.ExecutionFinishedAt >= now);
        Assert.DoesNotContain(runtime.SentPrompts, prompt => prompt == "old report");
    }

    [Theory]
    [InlineData("DATABASE IS LOCKED")]
    [InlineData("database locked")]
    [InlineData("SQLITE_BUSY")]
    [InlineData("did not start a session")]
    [InlineData("The agent exited")]
    [InlineData("stopped unexpectedly")]
    [Trait("Req", "PR-HYDRA-123")] // AppModel+Hydra.swift:680-699, :730-734
    public void A_launch_error_before_any_tool_schedules_one_brief_retry_without_reporting_to_the_lead(string error)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 1);
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "the original brief", isFromHydra: true), NoticeItem(turn, Notice.Level.Error, error)],
            [ClosedTurn(turn, 1, TurnStatus.Failed)]);
        var model = h.MakeModel();
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Failed));

        Assert.Equal(1L, model.HydraHeadRetries[head.Id]);
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Null(model.ExistingRuntime(lead.Id)); // The lead never hears about a launch-only failure.
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-123")] // AppModel+Hydra.swift:696-697; ThreadRuntime.swift:744-754.
    public void A_launch_retry_uses_the_composer_send_path_and_its_local_command_handling()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 0);
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        // /plan makes ordinary Send observable without invoking any provider: it toggles the local mode.
        WriteThreadDocument(head.Id,
            [UserItem(null, "/plan", isFromHydra: true), NoticeItem(turn, Notice.Level.Error, "database is locked")],
            [ClosedTurn(turn, 1, TurnStatus.Failed)]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = LoadRuntime(h, model, head.Id);
        var originalMode = h.Main.OnMain(() => model.Thread(head.Id)!.InteractionMode);
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = false;
            RestoreRunningHead(model, head.Id);
            runtime.Draft = new ComposerDraft { Text = "unsent head edit" };
            model.HydraHeadTurnFinished(head, TurnStatus.Failed);
            Assert.Equal(1L, model.HydraHeadRetries[head.Id]);
        });
        h.Main.Loop.RunUntilIdle();
        h.Main.Advance(TimeSpan.FromSeconds(2)); // First retry, roster index zero: exactly two seconds.
        h.Main.OnMain(() =>
        {
            Assert.Equal(originalMode == InteractionMode.Plan ? InteractionMode.Build : InteractionMode.Plan,
                model.Thread(head.Id)!.InteractionMode);
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
            Assert.Single(runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
            Assert.Null(model.ExistingRuntime(lead.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-123")] // AppModel+Hydra.swift:680-699, :730-734
    public void Launch_failure_text_in_the_head_prose_does_not_trigger_a_retry()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var head = HydraHeadThread(project, Guid.NewGuid(), HydraHeadInfo.Status.Running, 1);
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "first brief", isFromHydra: true), AssistantItem(turn, "The database is locked; partial notes.")],
            [ClosedTurn(turn, 1, TurnStatus.Failed)]);
        var model = h.MakeModel();
        _ = LoadRuntime(h, model, head.Id);

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Failed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.IsFinished == true, AppModelHarness.Bound);

        Assert.DoesNotContain(head.Id, model.HydraHeadRetries.Keys);
        Assert.Equal("The turn failed without a recorded error.\n\nPartial output before the failure:\n\nThe database is locked; partial notes.",
            model.Thread(head.Id)!.Hydra!.Summary);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-123")] // AppModel+Hydra.swift:680-699, :730-734
    // The retry gate (:680-681) checks only the head's timeline for a tool entry, never its assistant prose: a
    // Failed turn that produced partial assistant text but never ran a tool is still retry-eligible (see the
    // sibling test A_launch_error_before_any_tool_schedules_one_brief_retry_without_reporting_to_the_lead, whose
    // sibling Launch_failure_text_in_the_head_prose_does_not_trigger_a_retry shows text alone, with no error-level
    // notice, never counts as a launch failure either way). What actually disqualifies a retry is a tool having
    // run: "past the door" (:678) means the head's own actions, not its words.
    public void A_launch_error_after_a_tool_ran_is_reported_without_retrying()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 1);
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [
                UserItem(null, "the original brief", isFromHydra: true),
                ToolItem(turn, "Read one file"),
                NoticeItem(turn, Notice.Level.Error, "DATABASE IS LOCKED"),
                AssistantItem(turn, "partial answer before the launch error"),
            ],
            [ClosedTurn(turn, 1, TurnStatus.Failed)]);
        var model = h.MakeModel();
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Failed));

        Assert.DoesNotContain(head.Id, model.HydraHeadRetries.Keys);
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.IsFinished == true, AppModelHarness.Bound);
        Assert.Equal(HydraHeadInfo.Status.Failed, model.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Equal(
            "DATABASE IS LOCKED\n\nPartial output before the failure:\n\npartial answer before the launch error",
            model.Thread(head.Id)!.Hydra!.Summary);
        Assert.NotNull(model.Thread(head.Id)!.Hydra!.ExecutionFinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-121")] // AppModel.swift:1173-1216; AppModel+Hydra.swift:634-716
    public void Turn_finished_routes_a_Droppy_head_through_the_Hydra_completion_coordinator()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 0);
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "the original brief", isFromHydra: true), AssistantItem(turn, "completed answer")],
            [ClosedTurn(turn, 1, TurnStatus.Completed)]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraAutoClearFinished = false);
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));

        h.Main.OnMain(() => model.TurnFinished(head.Id, TurnStatus.Completed, continues: false));
        h.Main.Loop.RunUntilIdle();

        var finished = model.Thread(head.Id)!;
        Assert.True(finished.Hydra!.IsFinished);
        Assert.Equal(TurnStatus.Completed, finished.LastStatus);
        Assert.Equal(HydraHeadInfo.Status.Completed, finished.Hydra.HydraHeadInfoStatus);
        Assert.Equal("completed answer", finished.Hydra.Summary);
        Assert.NotNull(finished.Hydra.ExecutionFinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-124")] // AppModel+Hydra.swift:700-715
    public void A_failed_head_keeps_its_own_copy_and_never_lands_into_the_leads_checkout()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var id = Guid.NewGuid();
        var copy = CreateHeadCopy(repository, id, 0);
        File.WriteAllText(Path.Combine(copy.Path, "a.txt"), "unfinished head work\n");
        AppModelGitRepo.At(copy.Path).Git("add", "a.txt");
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, index: 0, copy.Tree, copy.Path, copy.Branch) with { Id = id };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Failed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.IsFinished == true, AppModelHarness.Bound);

        Assert.Equal(HydraHeadInfo.Status.Failed, model.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Null(model.Thread(head.Id)!.Hydra!.Landing);
        Assert.Equal(copy.Path, model.Thread(head.Id)!.WorktreePath);
        Assert.True(Directory.Exists(copy.Path));
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-124")] // AppModel+Hydra.swift:700-715
    public void A_head_steered_on_while_its_landing_waits_is_left_running()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var id = Guid.NewGuid();
        var baseTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        var copy = CreateHeadCopy(repository, id, index: 0);
        File.WriteAllText(Path.Combine(copy.Path, "a.txt"), "head work\n");
        AppModelGitRepo.At(copy.Path).Git("add", "a.txt");
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0, baseTree, copy.Path, null) with { Id = id };
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "brief", isFromHydra: true), AssistantItem(turn, "report")],
            [ClosedTurn(turn, 1, TurnStatus.Completed)]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraAutoClearFinished = false);
        var leadRuntime = LoadRuntime(h, model, lead.Id);
        var headRuntime = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousLanding = TaskHandle.Detached(async () => await gate.Task);
        h.Main.OnMain(() => leadRuntime.HydraLanding = previousLanding);

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Completed));
        h.Main.Loop.RunUntil(() => !ReferenceEquals(leadRuntime.HydraLanding, previousLanding), AppModelHarness.Bound);
        h.Main.OnMain(() => SetRuntimeProperty(headRuntime, nameof(ThreadRuntime.Phase), RuntimePhase.Running));
        gate.SetResult();
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.LandingFinishedAt is not null, AppModelHarness.Bound);

        Assert.Equal("head work\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.True(model.Thread(head.Id)!.IsInPanel);
        Assert.Null(model.Thread(head.Id)!.Hydra!.FinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-125")] // AppModel+Hydra.swift:747-795
    [Trait("Req", "PR-HYDRA-126")] // AppModel+Hydra.swift:797-809
    [Trait("Req", "PR-HYDRA-247")] // AppModel+Hydra.swift:839-844: LandingStartedAt/LandingFinishedAt, the latter
    // stamped from a `finally` (the port of Swift's `defer`) so every exit from the attempt sets it.
    public void A_completed_copy_lands_source_changes_and_counts_build_output_it_drops()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var id = Guid.NewGuid();
        var baseTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        var copy = CreateHeadCopy(repository, id, index: 0);
        File.WriteAllText(Path.Combine(copy.Path, "a.txt"), "head work\n");
        var buildOutput = Path.Combine(copy.Path, ".build", "Debug", "net9.0", "generated.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(buildOutput)!);
        File.WriteAllBytes(buildOutput, [1, 2, 3]);
        AppModelGitRepo.At(copy.Path).Git("add", "-A");
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0, baseTree, copy.Path, null) with { Id = id };
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "brief", isFromHydra: true), AssistantItem(turn, "report")],
            [ClosedTurn(turn, 1, TurnStatus.Completed)]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraAutoClearFinished = false);
        _ = LoadRuntime(h, model, lead.Id);
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Completed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.Landing is not null, AppModelHarness.Bound);

        var finished = model.Thread(head.Id)!.Hydra!;
        Assert.NotNull(finished.LandingStartedAt);
        Assert.NotNull(finished.LandingFinishedAt);
        var copyURL = SwiftURL.Create("file:///" + copy.Path.Replace('\\', '/'))!;
        Assert.Equal([copyURL.Appending("a.txt").Path], finished.Landing!.Files.Select(file => file.Path));
        Assert.Equal(1L, finished.Landing.DroppedBuildOutputFiles);
        Assert.True(finished.Landing.Landed);
        Assert.Equal("head work\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));
        Assert.False(File.Exists(Path.Combine(repository.Root, ".build", "Debug", "net9.0", "generated.dll")));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-127")] // AppModel+Hydra.swift:816-833; ThreadRuntime.swift:501-505
    public void A_landing_waits_for_the_leads_current_turn_to_leave_the_patch_path()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var id = Guid.NewGuid();
        var baseTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        var copy = CreateHeadCopy(repository, id, index: 0);
        File.WriteAllText(Path.Combine(copy.Path, "a.txt"), "head work\n");
        AppModelGitRepo.At(copy.Path).Git("add", "a.txt");
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0, baseTree, copy.Path, null) with { Id = id };
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "brief", isFromHydra: true), AssistantItem(turn, "report")],
            [ClosedTurn(turn, 1, TurnStatus.Completed)]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraAutoClearFinished = false);
        var leadRuntime = LoadRuntime(h, model, lead.Id);
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));
        h.Main.OnMain(() =>
        {
            SetRuntimeProperty(leadRuntime, nameof(ThreadRuntime.Phase), RuntimePhase.Running);
            SetRuntimeProperty(leadRuntime, nameof(ThreadRuntime.HydraCurrentTurnTouchedPaths),
                new ValueSet<string>(["a.txt"], SwiftStringComparer.Instance));
            model.HydraHeadTurnFinished(head, TurnStatus.Completed);
        });
        h.Main.Loop.RunUntil(() => leadRuntime.HydraLanding is not null, AppModelHarness.Bound);
        h.Main.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Null(model.Thread(head.Id)!.Hydra!.LandingFinishedAt);
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));

        h.Main.OnMain(() =>
        {
            SetRuntimeProperty(leadRuntime, nameof(ThreadRuntime.HydraCurrentTurnTouchedPaths),
                new ValueSet<string>([], SwiftStringComparer.Instance));
            SetRuntimeProperty(leadRuntime, nameof(ThreadRuntime.Phase), RuntimePhase.Idle);
        });
        h.Main.Advance(TimeSpan.FromMilliseconds(250));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.Landing is not null, AppModelHarness.Bound);
        Assert.NotNull(model.Thread(head.Id)!.Hydra!.LandingStartedAt);
        Assert.NotNull(model.Thread(head.Id)!.Hydra!.LandingFinishedAt);
        Assert.Equal("head work\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-128")] // AppModel+Hydra.swift:835-872; Git.swift:372-379
    [Trait("Req", "PR-HYDRA-129")] // AppModel+Hydra.swift:876-881
    public void An_in_progress_merge_keeps_the_patch_without_advancing_the_head_base()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var id = Guid.Parse("b34aa41a-8b16-4d85-9ea2-398f0c1a6d44");
        var baseTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        var copy = CreateHeadCopy(repository, id, index: 0);
        File.WriteAllText(Path.Combine(copy.Path, "a.txt"), "head work\n");
        var copyURL = SwiftURL.Create("file:///" + copy.Path.Replace('\\', '/'))!;
        var filePath = copyURL.Appending("a.txt").Path;
        AppModelGitRepo.At(copy.Path).Git("add", "a.txt");
        File.WriteAllText(Path.Combine(repository.Root, ".git", "MERGE_HEAD"), repository.Git("rev-parse", "HEAD").Trim());
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0, baseTree, copy.Path, null) with { Id = id };
        var turn = Guid.NewGuid();
        h.WriteLibrary([project], [lead, head]);
        WriteThreadDocument(head.Id,
            [UserItem(null, "brief", isFromHydra: true), AssistantItem(turn, "report")],
            [ClosedTurn(turn, 1, TurnStatus.Completed)]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraAutoClearFinished = false);
        var leadRuntime = LoadRuntime(h, model, lead.Id);
        _ = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() => RestoreRunningHead(model, head.Id));

        h.Main.OnMain(() => model.HydraHeadTurnFinished(head, TurnStatus.Completed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.Landing is not null, AppModelHarness.Bound);

        var info = model.Thread(head.Id)!.Hydra!;
        var landing = info.Landing!;
        var slug = info.Persona.Name.Replace(' ', '-').ToLowerInvariant();
        var expectedPatch = Path.Combine(Storage.PatchesDirectory, $"{slug}-{SwiftString.Lowercased(SwiftUUID.UuidString(id))[..8]}.patch");
        Assert.Equal("a rebase or merge is underway in the checkout, or its git state cannot be read", landing.Error);
        Assert.Equal(expectedPatch, landing.PatchPath);
        Assert.True(File.Exists(expectedPatch));
        var patchBytes = File.ReadAllBytes(expectedPatch);
        Assert.False(patchBytes.Length >= 3 && patchBytes[0] == 0xef && patchBytes[1] == 0xbb && patchBytes[2] == 0xbf);
        Assert.Equal(baseTree, info.BaseTree);
        Assert.NotNull(info.LandingStartedAt);
        Assert.NotNull(info.LandingFinishedAt);
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(repository.Root, "a.txt")));
        Assert.Contains(
            $"{info.Persona.Name}'s work did not land and was kept as a patch.\n`{filePath}`\n\nWhy it did not land: {landing.Error}.\n\nApply it with `git apply --3way {expectedPatch}` and settle what conflicts.",
            HydraNoteMessages(leadRuntime));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-130")] // AppModel+Hydra.swift:886-909
    public void Landing_notes_describe_conflicts_and_kept_patches_with_the_exact_bodies()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Completed, 0);
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        var first = new HydraLanding.File { Path = "a.cs", Additions = 1, Deletions = 0 };
        var second = new HydraLanding.File { Path = "b.cs", Additions = 0, Deletions = 2 };
        var patch = Path.Combine(h.Scratch, "head.patch");
        var landing = new HydraLanding
        {
            Files = new ValueList<HydraLanding.File>([first, second]),
            Conflicts = new ValueList<string>(["a.cs", "b.cs"]),
            PatchPath = patch,
            Error = "git state unavailable",
        };

        h.Main.OnMain(() => InvokePrivate(model, "NoteHydraLanding", landing, head.Hydra!, lead.Id));

        var notes = HydraNoteMessages(model.ExistingRuntime(lead.Id));
        var name = head.Hydra!.Persona.Name;
        Assert.Contains(
            $"{name}'s work landed; the lead is settling a conflict in 2 files.\n`a.cs`, `b.cs`\n\nThe checkout had moved on where {name} was working, so the merge was three-way and left markers in these files. The lead resolves them before the work goes out; nothing to do on your side.",
            notes);
        Assert.Contains(
            $"{name}'s work did not land and was kept as a patch.\n`a.cs`, `b.cs`\n\nWhy it did not land: git state unavailable.\n\nApply it with `git apply --3way {patch}` and settle what conflicts.",
            notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-131")] // AppModel+Hydra.swift:915-933
    public void Stopping_an_unstarted_droppy_head_finishes_it_without_a_report()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var head = HydraHeadThread(project, Guid.NewGuid(), HydraHeadInfo.Status.Running, 0);
        h.WriteLibrary([project], [head]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.StopHydraHead(head.Id));

        var stopped = model.Thread(head.Id)!.Hydra!;
        Assert.Equal(HydraHeadInfo.Status.Stopped, stopped.HydraHeadInfoStatus);
        Assert.Null(stopped.Summary);
        Assert.NotNull(stopped.FinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-131")] // AppModel+Hydra.swift:915-933
    public void A_native_stop_refused_by_the_lead_provider_removes_its_stop_affordance()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var native = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0) with
        {
            Hydra = new HydraHeadInfo(0, "native task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
            {
                NativeID = "provider-head-id",
                CanStop = true,
            },
        };
        h.WriteLibrary([project], [lead, native]);
        var model = h.MakeModel();
        var leadRuntime = LoadRuntime(h, model, lead.Id);
        h.Main.OnMain(() =>
        {
            RestoreRunningHead(model, native.Id);
            SetRuntimeField(leadRuntime, "session", new TestProviderSession());
        });

        h.Main.OnMain(() => model.StopHydraHead(native.Id));
        h.Main.Loop.RunUntil(() => model.Thread(native.Id)?.Hydra?.CanStop == false, AppModelHarness.Bound);

        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(native.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-131")] // AppModel+Hydra.swift:925-931: runtime(for: parentID) creates the lead runtime when none is live
    public void A_native_stop_whose_lead_has_no_live_runtime_still_asks_the_lead_and_drops_the_stop_affordance()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var native = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0) with
        {
            Hydra = new HydraHeadInfo(0, "native task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
            {
                NativeID = "provider-head-id",
                CanStop = true,
            },
        };
        h.WriteLibrary([project], [lead, native]);
        var model = h.MakeModel();
        h.Main.OnMain(() => RestoreRunningHead(model, native.Id));
        Assert.Null(h.Main.OnMain(() => model.ExistingRuntime(lead.Id)));

        h.Main.OnMain(() => model.StopHydraHead(native.Id));
        h.Main.Loop.RunUntil(() => model.Thread(native.Id)?.Hydra?.CanStop == false, AppModelHarness.Bound);

        // A lead with no session cannot stop its provider's subagent (ThreadRuntime stopNativeHead returns false).
        Assert.NotNull(h.Main.OnMain(() => model.ExistingRuntime(lead.Id)));
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(native.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-133")] // AppModel+Hydra.swift:944-953: runtime(for: parentID), not an existing-runtime lookup
    public void Dismissing_the_panel_of_a_lead_with_no_live_runtime_still_records_the_hidden_panel()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var finished = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Completed, 0);
        h.WriteLibrary([project], [lead, finished]);
        var model = h.MakeModel();
        Assert.Null(h.Main.OnMain(() => model.ExistingRuntime(lead.Id)));

        h.Main.OnMain(() => model.DismissHydraHeads(lead.Id));

        var runtime = h.Main.OnMain(() => model.ExistingRuntime(lead.Id));
        Assert.NotNull(runtime);
        Assert.True(runtime!.IsHydraPanelHidden);
        Assert.Null(runtime.HydraSelectedHeadID);
        Assert.Null(runtime.HydraPoppedHeadID);
        Assert.False(model.Thread(finished.Id)!.IsInPanel);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-132")] // AppModel+Hydra.swift:936-940
    public void Stop_all_only_stops_running_heads_in_the_requested_team()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var leadID = Guid.NewGuid();
        var otherLeadID = Guid.NewGuid();
        var first = HydraHeadThread(project, leadID, HydraHeadInfo.Status.Running, 0);
        var second = HydraHeadThread(project, leadID, HydraHeadInfo.Status.Running, 1);
        var finished = HydraHeadThread(project, leadID, HydraHeadInfo.Status.Completed, 2);
        var other = HydraHeadThread(project, otherLeadID, HydraHeadInfo.Status.Running, 0);
        h.WriteLibrary([project], [first, second, finished, other]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            foreach (var id in new[] { first.Id, second.Id, other.Id })
                model.UpdateThread(id, thread => thread with
                {
                    Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
                });
        });

        h.Main.OnMain(() => model.StopAllHydraHeads(leadID));

        Assert.Equal(HydraHeadInfo.Status.Stopped, model.Thread(first.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Equal(HydraHeadInfo.Status.Stopped, model.Thread(second.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Equal(HydraHeadInfo.Status.Completed, model.Thread(finished.Id)!.Hydra!.HydraHeadInfoStatus);
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(other.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-133")] // AppModel+Hydra.swift:944-953
    public void Dismissing_the_panel_clears_finished_heads_and_all_five_runtime_fields()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var finished = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Completed, 0);
        var running = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 1);
        h.WriteLibrary([project], [lead, finished, running]);
        var model = h.MakeModel();
        h.Main.OnMain(() => RestoreRunningHead(model, running.Id));
        var runtime = h.Main.OnMain(() => model.Runtime(lead.Id));
        h.Main.OnMain(() =>
        {
            runtime.HydraSelectedHeadID = finished.Id;
            runtime.HydraPoppedHeadID = finished.Id;
            runtime.HydraAutoPopHeld = new ValueSet<Guid>([finished.Id]);
            runtime.HydraAutoPanelDocks = new ValueMap<Guid, PanelDockCorner>([KeyValuePair.Create(finished.Id, PanelDockCorner.TopTrailing)]);
            runtime.PanelStackOrder = new ValueList<FloatingPanelID>([new FloatingPanelID.Auto(finished.Id), new FloatingPanelID.Hydra()]);
            model.DismissHydraHeads(lead.Id);
        });

        Assert.True(runtime.IsHydraPanelHidden);
        Assert.Null(runtime.HydraSelectedHeadID);
        Assert.Null(runtime.HydraPoppedHeadID);
        Assert.Empty(runtime.HydraAutoPopHeld);
        Assert.Empty(runtime.HydraAutoPanelDocks);
        Assert.Equal([new FloatingPanelID.Hydra()], runtime.PanelStackOrder);
        Assert.False(model.Thread(finished.Id)!.IsInPanel);
        Assert.True(model.Thread(running.Id)!.IsInPanel);
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(running.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-134")] // AppModel+Hydra.swift:956-961
    public void Clearing_finished_heads_hides_each_finished_row_and_keeps_running_rows_in_the_panel()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var finished = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Failed, 0);
        var stopped = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Stopped, 1);
        var running = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 2);
        h.WriteLibrary([project], [lead, finished, stopped, running]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.UpdateThread(running.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
        }));

        h.Main.OnMain(() => model.ClearFinishedHydraHeads(lead.Id));

        Assert.False(model.Thread(finished.Id)!.IsInPanel);
        Assert.False(model.Thread(stopped.Id)!.IsInPanel);
        Assert.True(model.Thread(running.Id)!.IsInPanel);
        Assert.Equal(HydraHeadInfo.Status.Running, model.Thread(running.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-135")] // AppModel+Hydra.swift:965-987
    [Trait("Req", "PR-HYDRA-136")] // AppModel+Hydra.swift:991-1010
    public void Releasing_an_unlanded_copy_keeps_its_work_as_a_named_patch_and_preserves_a_live_copy()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project);
        var stoppedID = Guid.NewGuid();
        var stoppedCopy = CreateHeadCopy(repository, stoppedID, index: 0);
        File.WriteAllText(Path.Combine(stoppedCopy.Path, "a.txt"), "unfinished work\n");
        AppModelGitRepo.At(stoppedCopy.Path).Git("add", "a.txt");
        var stopped = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Stopped, 0, stoppedCopy.Tree, stoppedCopy.Path, "old-branch") with { Id = stoppedID };
        var runningID = Guid.NewGuid();
        var runningCopy = CreateHeadCopy(repository, runningID, index: 1);
        var running = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 1, runningCopy.Tree, runningCopy.Path, "live-branch") with { Id = runningID };
        var emptyID = Guid.NewGuid();
        var emptyCopy = CreateHeadCopy(repository, emptyID, index: 2);
        var empty = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Stopped, 2, emptyCopy.Tree, emptyCopy.Path, "empty-branch") with { Id = emptyID };
        h.WriteLibrary([project], [lead, stopped, running, empty]);
        var model = h.MakeModel();
        var leadRuntime = LoadRuntime(h, model, lead.Id);
        var runningRuntime = LoadRuntime(h, model, running.Id);
        h.Main.OnMain(() => SetRuntimeProperty(runningRuntime, nameof(ThreadRuntime.Phase), RuntimePhase.Running));

        h.Main.OnMain(() =>
        {
            model.ReleaseHydraCopy(stopped.Id);
            model.ReleaseHydraCopy(running.Id);
            model.ReleaseHydraCopy(empty.Id);
        });
        var stoppedInfo = model.Thread(stopped.Id)!.Hydra!;
        var emptyInfo = model.Thread(empty.Id)!.Hydra!;
        var slug = stoppedInfo.Persona.Name.Replace(' ', '-').ToLowerInvariant();
        var expectedPatch = Path.Combine(Storage.PatchesDirectory, $"{slug}-{SwiftString.Lowercased(SwiftUUID.UuidString(stoppedID))[..8]}-unlanded.patch");
        var emptySlug = emptyInfo.Persona.Name.Replace(' ', '-').ToLowerInvariant();
        var emptyPatch = Path.Combine(Storage.PatchesDirectory, $"{emptySlug}-{SwiftString.Lowercased(SwiftUUID.UuidString(emptyID))[..8]}-unlanded.patch");
        h.Main.Loop.RunUntil(() => File.Exists(expectedPatch) && !Directory.Exists(stoppedCopy.Path) && !Directory.Exists(emptyCopy.Path), AppModelHarness.Bound);

        Assert.Null(model.Thread(stopped.Id)!.WorktreePath);
        Assert.Null(model.Thread(stopped.Id)!.Branch);
        Assert.Null(model.Thread(stopped.Id)!.Hydra!.BaseTree);
        Assert.True(Directory.Exists(runningCopy.Path));
        Assert.Equal(runningCopy.Path, model.Thread(running.Id)!.WorktreePath);
        Assert.Equal("live-branch", model.Thread(running.Id)!.Branch);
        Assert.Equal(runningCopy.Tree, model.Thread(running.Id)!.Hydra!.BaseTree);
        Assert.False(File.Exists(emptyPatch));
        Assert.DoesNotContain(HydraNoteMessages(leadRuntime), note => note.StartsWith(
            $"{emptyInfo.Persona.Name}'s unfinished work", StringComparison.Ordinal));
        Assert.Contains(
            $"{stoppedInfo.Persona.Name}'s unfinished work was kept as a patch before its copy was cleared.\n{expectedPatch}\n\nNothing of it had landed in your checkout. Apply it with `git apply --3way {expectedPatch}` if you still want it.",
            HydraNoteMessages(leadRuntime));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-137")] // AppModel+Hydra.swift:114-127, :1021-1030
    public void Starting_the_watchdog_twice_keeps_one_loop_on_the_thirty_second_tick()
    {
        ResetWatchdog();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0);
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        var runtime = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() =>
        {
            RestoreRunningHead(model, head.Id);
            SetRuntimeProperty(runtime, nameof(ThreadRuntime.Phase), RuntimePhase.Running);
            runtime.HydraLastEventAt = SwiftDate.Now.AddSeconds(-181);
            runtime.HydraToolCalls = 4;
        });

        var initialTimerCount = h.Main.Time.ActiveTimers;
        h.Main.OnMain(() => model.StartHydraWatchdog());
        h.Main.Loop.RunUntil(() => h.Main.Time.ActiveTimers > initialTimerCount, AppModelHarness.Bound);
        var timerCount = h.Main.Time.ActiveTimers;
        h.Main.OnMain(() => model.StartHydraWatchdog());
        Assert.Equal(timerCount, h.Main.Time.ActiveTimers);
        h.Main.Advance(TimeSpan.FromMilliseconds(29_999));
        Assert.Null(runtime.HydraActivity);
        h.Main.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal("Thinking for 3 min…", runtime.HydraActivity);
        ResetWatchdog();
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-138")] // AppModel+Hydra.swift:1033-1058
    public void A_watchdog_look_labels_a_quiet_running_droppy_head()
    {
        ResetWatchdog();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0);
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();
        var runtime = LoadRuntime(h, model, head.Id);
        h.Main.OnMain(() =>
        {
            RestoreRunningHead(model, head.Id);
            SetRuntimeProperty(runtime, nameof(ThreadRuntime.Phase), RuntimePhase.Running);
            runtime.HydraLastEventAt = SwiftDate.Now.AddSeconds(-239);
            runtime.HydraToolCalls = 2;
            runtime.HydraEditCount = 0;
            runtime.HydraActivity = "Searching files";
            InvokePrivate(model, "WatchHydraHeads");
        });

        Assert.Equal("Thinking for 3 min…", runtime.HydraActivity);
        var stalled = StalledHeadIDs();
        h.Main.OnMain(() =>
        {
            runtime.HydraLastEventAt = SwiftDate.Now.AddMinutes(-10);
            runtime.HydraEditCount = 1;
            InvokePrivate(model, "WatchHydraHeads");
        });
        Assert.DoesNotContain(head.Id, stalled);
        h.Main.OnMain(() =>
        {
            runtime.HydraEditCount = 0;
            runtime.HydraToolCalls = 3;
            InvokePrivate(model, "WatchHydraHeads");
        });
        Assert.DoesNotContain(head.Id, stalled); // A new tool since the last look keeps it from being called stuck.
        h.Main.OnMain(() => InvokePrivate(model, "WatchHydraHeads"));
        Assert.Contains(head.Id, stalled);
        ResetWatchdog();
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-139")] // AppModel+Hydra.swift:126, :575-578
    public void A_stalled_head_report_gets_the_exact_note_once()
    {
        ResetWatchdog();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = HydraHeadThread(project, lead.Id, HydraHeadInfo.Status.Running, 0);
        h.WriteLibrary([project], [head]);
        var model = h.MakeModel();
        var stalled = StalledHeadIDs();
        stalled.Add(head.Id);

        h.Main.OnMain(() => model.UpdateThread(head.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
        }));

        h.Main.OnMain(() => model.FinishHydraHead(head.Id, TurnStatus.Interrupted, "partial report"));
        h.Main.OnMain(() => model.FinishHydraHead(head.Id, TurnStatus.Interrupted, "second report"));

        Assert.Equal(
            "Stalled: no edits for 10 minutes; stopped by Nth. Resend with a sharper brief.\n\npartial report", // Swift :126; OD-0012 F1 branding.
            model.Thread(head.Id)!.Hydra!.Summary);
        Assert.DoesNotContain(head.Id, stalled);
        ResetWatchdog();
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-140")] // AppModel+Hydra.swift:1064-1082
    [Trait("Req", "PR-HYDRA-141")] // AppModel+Hydra.swift:1091-1113
    public void Copy_sweep_heals_missing_thread_paths_and_removes_only_old_matching_orphans()
    {
        ResetWatchdog();
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("project"));
        var project = AppModelSamples.Project("App Name", repository.Root);
        var longerProject = AppModelSamples.Project("App Name Archive", h.NewFolder("longer-project"));
        var missing = HydraHeadThread(project, Guid.NewGuid(), HydraHeadInfo.Status.Stopped, 0,
            baseTree: "base-tree", worktreePath: Path.Combine(h.Scratch, "missing-copy"), branch: "hydra-branch");
        var namedPath = Path.Combine(Storage.WorktreesDirectory, "app-name-owned-kept-folder");
        var named = HydraHeadThread(project, Guid.NewGuid(), HydraHeadInfo.Status.Stopped, 1,
            baseTree: "base-tree", worktreePath: namedPath);
        h.WriteLibrary([project, longerProject], [missing, named]);
        Directory.CreateDirectory(Storage.WorktreesDirectory);
        Directory.CreateDirectory(namedPath);
        var persona = HydraRoster.Persona(0).Name.Replace(' ', '-').ToLowerInvariant();
        var oldMatch = Path.Combine(Storage.WorktreesDirectory, $"app-name-{persona}-1-deadbeef");
        var youngMatch = Path.Combine(Storage.WorktreesDirectory, $"app-name-{persona}-cafefeed");
        var invalidPersona = Path.Combine(Storage.WorktreesDirectory, "app-name-not-a-persona-deadbeef");
        var invalidSuffix = Path.Combine(Storage.WorktreesDirectory, $"app-name-{persona}-nothex12");
        var oldLongerMatch = Path.Combine(Storage.WorktreesDirectory, $"app-name-archive-{persona}-babe1234");
        foreach (var path in new[] { oldMatch, youngMatch, invalidPersona, invalidSuffix, oldLongerMatch }) Directory.CreateDirectory(path);
        Directory.SetCreationTimeUtc(oldMatch, DateTime.UtcNow.AddMinutes(-11));
        Directory.SetCreationTimeUtc(invalidPersona, DateTime.UtcNow.AddMinutes(-11));
        Directory.SetCreationTimeUtc(invalidSuffix, DateTime.UtcNow.AddMinutes(-11));
        Directory.SetCreationTimeUtc(oldLongerMatch, DateTime.UtcNow.AddMinutes(-11));

        var model = h.MakeModel();
        h.Main.OnMain(() => model.SweepHydraCopies());
        h.Main.Loop.RunUntil(() => !Directory.Exists(oldMatch) && !Directory.Exists(oldLongerMatch)
            && model.Thread(missing.Id)?.WorktreePath is null && WatchdogLoop() is not null, AppModelHarness.Bound);

        Assert.Null(model.Thread(missing.Id)!.Branch);
        Assert.Null(model.Thread(missing.Id)!.Hydra!.BaseTree);
        Assert.True(Directory.Exists(namedPath));
        Assert.True(Directory.Exists(youngMatch));
        Assert.True(Directory.Exists(invalidPersona));
        Assert.True(Directory.Exists(invalidSuffix));
        Assert.False(Directory.Exists(oldLongerMatch));
        Assert.NotNull(WatchdogLoop());
        ResetWatchdog();
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-142")] // AppModel+Hydra.swift:1118-1143
    public void Hydra_chat_context_skips_relay_messages_and_collects_distinct_paths_from_the_current_turn()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var oldTurn = Guid.NewGuid();
        var currentTurn = Guid.NewGuid();
        h.WriteLibrary([project], [lead]);
        WriteThreadDocument(lead.Id,
        [
            UserItem(oldTurn, "older prompt"),
            AssistantItem(oldTurn, "older reply"),
            ToolItem(oldTurn, "Edit", new FileEdit("old.cs")),
            UserItem(currentTurn, "latest user prompt"),
            UserItem(currentTurn, "Hydra report relay", isFromHydra: true),
            AssistantItem(currentTurn, "latest assistant reply"),
            ToolItem(currentTurn, "Edit one", new FileEdit("b.cs"), new FileEdit(""), new FileEdit("a.cs")),
            ToolItem(currentTurn, "Edit two", new FileEdit("b.cs"), new FileEdit("c.cs")),
        ], [new TurnRecord { Id = oldTurn, Index = 1, Status = TurnStatus.Completed, CompletedAt = SwiftDate.Now }, new TurnRecord { Id = currentTurn, Index = 2 }]);
        var model = h.MakeModel();
        var runtime = LoadRuntime(h, model, lead.Id);
        h.Main.OnMain(() =>
        {
            // Startup recovery marks persisted running turns interrupted; simulate a live turn in memory.
            runtime.RehearseTurn(null);
            runtime.Rehearse(new ProviderEvent.ToolStarted("edit-one", new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Read,
                Title = "Edit one",
                Edits = new ValueList<FileEdit>([new("b.cs"), new(""), new("a.cs")]),
            }));
            runtime.Rehearse(new ProviderEvent.ToolStarted("edit-two", new ToolCall
            {
                ToolCallKind = ToolCall.Kind.Read,
                Title = "Edit two",
                Edits = new ValueList<FileEdit>([new("b.cs"), new("c.cs")]),
            }));
        });

        var context = h.Main.OnMain(() => model.HydraChatContext(lead.Id));

        Assert.Equal("latest user prompt", context.LastUserPrompt);
        Assert.Equal("latest assistant reply", context.LastReply);
        Assert.Equal(["b.cs", "a.cs", "c.cs"], context.TouchedPaths);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-237")] // AppModel+Hydra.swift:310-313, :345-347
    public void A_head_uses_the_leads_worktree_verbatim_or_falls_back_to_its_project_folder()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var missingWorktree = Path.Combine(h.Scratch, "missing", "..", "stored-worktree");
        var worktreeLead = AppModelSamples.Thread(project) with { WorktreePath = missingWorktree, Branch = "ignored-branch" };
        var projectLead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [worktreeLead, projectLead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        HydraPrompts.Workplace? firstWorkplace = null;
        HydraPrompts.Workplace? secondWorkplace = null;

        h.Main.OnMain(() =>
        {
            model.SpawnDroppyHead(worktreeLead.Id, "worktree path", HydraHeadInfo.Origin.Delegated, [], null, null, null, null,
                (_, workplace) => { firstWorkplace = workplace; return "brief"; });
            model.SpawnDroppyHead(projectLead.Id, "project path", HydraHeadInfo.Origin.Delegated, [], null, null, null, null,
                (_, workplace) => { secondWorkplace = workplace; return "brief"; });
        });

        h.Main.Loop.RunUntil(() => firstWorkplace is not null && secondWorkplace is not null, AppModelHarness.Bound);
        Assert.Equal(new HydraPrompts.Workplace.Shared(missingWorktree), firstWorkplace);
        Assert.Equal(new HydraPrompts.Workplace.Shared(project.Path), secondWorkplace);
        Assert.Null(InvokePrivate<string?>(model, "HydraCheckout", projectLead with { ProjectID = Guid.NewGuid() }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-238")] // AppModel+Hydra.swift:344-347, :496-497
    public void A_head_in_another_project_than_its_lead_checks_out_its_own_project_never_the_leads_worktree()
    {
        using var h = new AppModelHarness();
        var leadProject = AppModelSamples.Project("Lead project", h.NewFolder("lead-project"));
        var otherProject = AppModelSamples.Project("Other project", h.NewFolder("other-project"));
        var leadWorktree = Path.Combine(h.Scratch, "lead-worktree");
        Directory.CreateDirectory(leadWorktree);
        var lead = AppModelSamples.Thread(leadProject) with { WorktreePath = leadWorktree, Branch = "lead-branch" };
        h.WriteLibrary([leadProject, otherProject], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        HydraPrompts.Workplace? workplace = null;

        var head = h.Main.OnMain(() => model.SpawnDroppyHead(lead.Id, "other project's task",
            HydraHeadInfo.Origin.Delegated, [], null, null, otherProject.Name, null,
            (_, resolved) => { workplace = resolved; return "brief"; }));
        h.Main.Loop.RunUntil(() => workplace is not null, AppModelHarness.Bound);

        Assert.NotNull(head);
        Assert.Equal(otherProject.Id, head!.ProjectID);
        Assert.Equal(new HydraPrompts.Workplace.Shared(otherProject.Path), workplace);
        Assert.Equal(otherProject.Path, InvokePrivate<string?>(model, "HydraCheckout", head, lead));
        Assert.Equal(leadWorktree, InvokePrivate<string?>(model, "HydraCheckout", lead, lead)); // sanity: same-project still uses the lead's worktree
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-246")] // AppModel+Hydra.swift:518-527, :634-638
    public void A_droppy_heads_execution_interval_is_stamped_when_its_brief_goes_out_and_when_its_turn_ends()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);

        var head = h.Main.OnMain(() => model.SpawnDroppyHead(lead.Id, "task",
            HydraHeadInfo.Origin.Delegated, [], null, null, null, null, static (_, _) => "brief"));
        Assert.NotNull(head);
        h.Main.Loop.RunUntil(() => model.Thread(head!.Id)?.Hydra?.ExecutionStartedAt is not null, AppModelHarness.Bound);

        var started = model.Thread(head!.Id)!.Hydra!.ExecutionStartedAt;
        Assert.NotNull(started);
        Assert.Null(model.Thread(head.Id)!.Hydra!.ExecutionFinishedAt);

        WriteThreadDocument(head.Id,
            [UserItem(null, "task", isFromHydra: true), AssistantItem(Guid.NewGuid(), "done")],
            [ClosedTurn(Guid.NewGuid(), 1, TurnStatus.Completed)]);
        h.Main.OnMain(() => model.HydraHeadTurnFinished(model.Thread(head.Id)!, TurnStatus.Completed));
        h.Main.Loop.RunUntil(() => model.Thread(head.Id)?.Hydra?.ExecutionFinishedAt is not null, AppModelHarness.Bound);

        Assert.Equal(started, model.Thread(head.Id)!.Hydra!.ExecutionStartedAt); // the start stamp from launch is untouched
        Assert.NotNull(model.Thread(head.Id)!.Hydra!.ExecutionFinishedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-245")] // AppModel+Hydra.swift:34-36, :44-50, :53-98
    public async Task A_landing_bumps_the_checkouts_capture_generation_so_a_stale_capture_is_never_reused()
    {
        var repository = AppModelGitRepo.WithCommit(HydraTreeCacheScratch());
        var git = new Git(repository.Root);

        var first = await CaptureHydraTree(repository.Root, git);

        InvalidateHydraTreeCache(repository.Root); // stands in for a landing (AppModel+Hydra.cs:712)
        File.WriteAllText(Path.Combine(repository.Root, "a.txt"), "changed\n");
        var afterInvalidation = await CaptureHydraTree(repository.Root, git);
        Assert.NotEqual(first.Tree, afterInvalidation.Tree); // the bumped generation forced a fresh walk
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-245")] // AppModel+Hydra.swift:56-61: the generation is checked again after the tree is read
    public async Task A_landing_while_a_fresh_capture_is_being_checked_makes_a_new_starting_point()
    {
        var repository = AppModelGitRepo.WithCommit(HydraTreeCacheScratch());
        var git = new Git(repository.Root);

        // The check's git read must really wait, not merely usually. With the checkout's watch standing, the read can be
        // answered without waiting at all: when the watch's cookie has already been seen by the time it is registered
        // (the watcher thread beat it) and the watch's snapshot is current, currentTree returns inline, the whole check
        // finishes before the test can land anything, and the old starting point is rightly handed out (about 1 run in 5).
        // With the watch down, the read is the scratch-index walk through git processes, and a process can never have
        // exited before it was awaited. This is the same read the cache makes for a checkout nothing watches.
        WorkingTreeWatch.Shared(git.Directory, Path.Combine(git.Directory, ".git"))?.TearDown();

        var first = await CaptureHydraTree(repository.Root, git);
        await Task.Delay(TimeSpan.FromSeconds(1.2)); // inside the 3 s lifetime; a new commit now gets a later timestamp

        // The capture is still fresh, so the next head checks the tree against it; a landing happens while that read is
        // under way. The capture from before the landing must not be handed out, however recent and however equal the tree.
        // The cache's own lock is held from the start of the check to the landing, so the check cannot finish in between
        // whatever the machine's load; the check is proved to be waiting on git before the landing is made.
        var gate = HydraTreeCacheType().GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Task<(string Tree, string Commit)> checking;
        Monitor.Enter(gate);
        try
        {
            checking = StartHydraTreeCapture(repository.Root, git);
            Assert.False(checking.IsCompleted, "the check finished before the landing: its read did not wait on git");
            InvalidateHydraTreeCache(repository.Root);
        }
        finally
        {
            Monitor.Exit(gate);
        }
        var after = await checking;

        Assert.Equal(first.Tree, after.Tree); // nothing in the checkout changed
        Assert.NotEqual(first.Commit, after.Commit); // yet the starting point was made again
    }

    private static string HydraTreeCacheScratch()
    {
        var folder = Path.Combine(TestDataDirectory.Read()!, "AppModelHydraTests", "tree-cache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Type HydraTreeCacheType()
    {
        var type = typeof(AppModel).GetNestedType("HydraTreeCache", BindingFlags.NonPublic);
        Assert.NotNull(type);
        return type!;
    }

    private static Task<(string Tree, string Commit)> CaptureHydraTree(string checkout, Git git) => StartHydraTreeCapture(checkout, git);

    // Starts a capture and returns once it first waits (on git), so a test can act while it is under way.
    private static async Task<(string Tree, string Commit)> StartHydraTreeCapture(string checkout, Git git)
    {
        var method = HydraTreeCacheType().GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(null, [checkout, git])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var resultType = result.GetType();
        var tree = (string)resultType.GetProperty("Tree")!.GetValue(result)!;
        var commit = (string)resultType.GetProperty("Commit")!.GetValue(result)!;
        return (tree, commit);
    }

    private static void InvalidateHydraTreeCache(string checkout)
    {
        var method = HydraTreeCacheType().GetMethod("Invalidate", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, [checkout]);
    }
    private static ChatThread? SpawnDroppy(
        AppModel model,
        Guid parentID,
        string task,
        long? preferredIndex = null,
        string? project = null,
        string? profile = null) =>
        model.SpawnDroppyHead(
            parentID,
            task,
            HydraHeadInfo.Origin.Delegated,
            [],
            null,
            preferredIndex,
            project,
            profile,
            static (_, _) => "brief");

    private static IEnumerable<UserMessage> HydraUserMessages(ThreadRuntime? runtime) =>
        runtime?.Entries.Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>()
            .Select(user => user.Value)
        ?? [];

    private static IEnumerable<string> HydraNoteMessages(ThreadRuntime? runtime) =>
        runtime?.Entries.Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>()
            .Select(user => user.Value)
            .Where(message => message.HydraHeads is { Count: 0 })
            .Select(message => message.Text)
        ?? [];

    private static ChatThread HydraHeadThread(
        Project project,
        Guid parentID,
        HydraHeadInfo.Status status,
        long index,
        string? baseTree = null,
        string? worktreePath = null,
        string? branch = null,
        bool isInPanel = true) =>
        new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = parentID,
            IsInPanel = isInPanel,
            WorktreePath = worktreePath,
            Branch = branch,
            Hydra = AppModelSamples.Head(status, baseTree) with { Index = index },
        };

    private static TimelineItem UserItem(Guid? turnID, string text, bool isFromHydra = false) =>
        new(turnID, new TimelineItem.Content.User(new UserMessage
        {
            Text = text,
            HydraHeads = isFromHydra ? new ValueList<long>([0]) : null,
        }));

    private static TimelineItem AssistantItem(Guid? turnID, string text) =>
        new(turnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }));

    private static TimelineItem NoticeItem(Guid? turnID, Notice.Level level, string text) =>
        new(turnID, new TimelineItem.Content.Notice(new Notice { NoticeLevel = level, Message = text }));

    private static TimelineItem ToolItem(Guid? turnID, string title, params FileEdit[] edits) =>
        new(turnID, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Read,
            Title = title,
            ToolCallStatus = ToolCall.Status.Completed,
            Edits = new ValueList<FileEdit>(edits),
        }));

    private static TurnRecord ClosedTurn(Guid id, long index, TurnStatus status) =>
        new()
        {
            Id = id,
            Index = index,
            Status = status,
            CompletedAt = SwiftDate.Now,
        };

    private static void WriteThreadDocument(Guid threadID, IReadOnlyList<TimelineItem> items, IReadOnlyList<TurnRecord> turns) =>
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(threadID)
            {
                Items = new ValueList<TimelineItem>(items),
                Turns = new ValueList<TurnRecord>(turns),
            },
            Storage.ThreadURL(threadID));

    private static ThreadRuntime LoadRuntime(AppModelHarness harness, AppModel model, Guid threadID)
    {
        var runtime = harness.Main.OnMain(() => model.Runtime(threadID));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return runtime;
    }

    private static (string Tree, string Path, string? Branch) CreateHeadCopy(
        AppModelGitRepo repository,
        Guid headID,
        long index)
    {
        Directory.CreateDirectory(Storage.WorktreesDirectory);
        var personaSlug = HydraRoster.Persona(index).Name.Replace(' ', '-').ToLowerInvariant();
        var suffix = SwiftString.Lowercased(SwiftUUID.UuidString(headID))[..8];
        var path = Path.Combine(Storage.WorktreesDirectory, $"test-{personaSlug}-{suffix}");
        repository.Git("worktree", "add", "-q", "--detach", path, "HEAD");
        var tree = AppModelGitRepo.At(path).Git("rev-parse", "HEAD^{tree}").Trim();
        return (tree, path, null);
    }

    private static void SetRuntimeProperty(ThreadRuntime runtime, string propertyName, object value)
    {
        var property = typeof(ThreadRuntime).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(runtime, value);
    }

    private static void SetRuntimeField(ThreadRuntime runtime, string fieldName, object value)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(runtime, value);
    }

    private static void RestoreRunningHead(AppModel model, Guid headID) =>
        model.UpdateThread(headID, thread => thread with
        {
            Hydra = thread.Hydra! with
            {
                HydraHeadInfoStatus = HydraHeadInfo.Status.Running,
                FinishedAt = null,
            },
        });

    private static MethodInfo ResolvePrivateMethod(string methodName, int argumentCount)
    {
        var candidates = typeof(AppModel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(candidate => candidate.Name == methodName && candidate.GetParameters().Length == argumentCount)
            .ToList();
        Assert.Single(candidates); // overloaded private members (Swift's hydraCheckout(of:lead:)) disambiguate by arity
        return candidates[0];
    }

    private static void InvokePrivate(AppModel model, string methodName, params object[] arguments) =>
        ResolvePrivateMethod(methodName, arguments.Length).Invoke(model, arguments);

    private static T? InvokePrivate<T>(AppModel model, string methodName, params object[] arguments) =>
        (T?)ResolvePrivateMethod(methodName, arguments.Length).Invoke(model, arguments);

    private static HashSet<Guid> StalledHeadIDs()
    {
        var watchdog = typeof(AppModel).GetNestedType("HydraWatchdog", BindingFlags.NonPublic);
        Assert.NotNull(watchdog);
        var field = watchdog!.GetField("StalledHeadIDs", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<HashSet<Guid>>(field!.GetValue(null));
    }

    private static void ResetWatchdog()
    {
        var watchdog = typeof(AppModel).GetNestedType("HydraWatchdog", BindingFlags.NonPublic);
        if (watchdog is null) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var fields = watchdog.GetFields(flags);
        var loopField = fields.FirstOrDefault(field => typeof(TaskHandle).IsAssignableFrom(field.FieldType));
        if (loopField?.GetValue(null) is TaskHandle loop)
        {
            loop.Cancel();
            if (!loopField.IsInitOnly) loopField.SetValue(null, null);
        }
        foreach (var field in fields)
        {
            if (field.GetValue(null) is System.Collections.IDictionary dictionary) dictionary.Clear();
            if (field.GetValue(null) is HashSet<Guid> ids) ids.Clear();
        }
    }

    private static TaskHandle? WatchdogLoop()
    {
        var watchdog = typeof(AppModel).GetNestedType("HydraWatchdog", BindingFlags.NonPublic);
        if (watchdog is null) return null;
        var loopField = watchdog.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(field => typeof(TaskHandle).IsAssignableFrom(field.FieldType));
        return loopField?.GetValue(null) as TaskHandle;
    }

    private static (AppModel Model, Project Project, ChatThread Thread) MakeModel(
        AppModelHarness harness,
        string? model = null,
        string? effort = null)
    {
        var project = AppModelSamples.Project("Project", harness.NewFolder("project"));
        var thread = new ChatThread(project.Id, ProviderKind.Codex, model, effort, RuntimeMode.Supervised)
        {
            HydraEnabled = true,
        };
        harness.WriteLibrary([project], [thread]);
        return (harness.MakeModel(), project, thread);
    }
}
