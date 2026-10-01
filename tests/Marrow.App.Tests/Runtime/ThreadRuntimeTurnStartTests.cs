using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// What a turn does before its message reaches the provider: ThreadRuntime.swift:1204-1290 (B3 1.7.0; the records cite the
// B2 numbers :1204-1288). Turns run through the real StartTurn over an offline scripted session.
public sealed class ThreadRuntimeTurnStartTests
{
    private static HydraDelegationBudget SpentBudget() =>
        new HydraDelegationBudget().ChargeRound().ChargeRound().Refuse("first refusal").Budget.NoteDropped("No heads went out.");

    // ---- the delegation budget (PR-RUNTIME-056) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-056")] // :1204-1208 a turn of the user's own ends the stream and reader, clears the touched paths, resets the budget
    public void A_turn_of_the_users_own_starts_the_delegation_budget_over_and_ends_the_streamed_block()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        rig.H.Main.OnMain(() => rig.Runtime.RehearseTurn("earlier", ["a.txt"]));
        rig.Complete();
        Assert.Contains("a.txt", rig.Runtime.HydraCurrentTurnTouchedPaths);
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraBudget", SpentBudget());
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraStream", (Guid.NewGuid(), "entry", Guid.NewGuid(), 4L));
        ThreadRuntimeReflection.Set(rig.Runtime, "delegationParser", new HydraDelegationStreamParser(Guid.NewGuid(), "entry"));
        HydraDelegationBudget? budget = null;
        object? stream = "unset";
        object? parser = "unset";
        var touched = -1;

        var task = rig.StartTurn("new request", whileStarting: () =>
        {
            budget = ThreadRuntimeReflection.Get<HydraDelegationBudget>(rig.Runtime, "hydraBudget");
            stream = ThreadRuntimeReflection.Get<object>(rig.Runtime, "hydraStream");
            parser = ThreadRuntimeReflection.Get<object>(rig.Runtime, "delegationParser");
            touched = rig.Runtime.HydraCurrentTurnTouchedPaths.Count;
        });
        rig.Finish(task);

        Assert.Equal(0, budget!.Rounds);
        Assert.Equal(0, budget.Refusals);
        Assert.Null(stream);
        Assert.Null(parser);
        Assert.Equal(0, touched);
        // beginTurn leaves a waiting drop notice alone: only the send spends it (Hydra.swift:573-576).
        Assert.StartsWith("[Hydra] No heads went out.\n\n---\n\n", rig.Session.Inputs.Last().Text, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PR-RUNTIME-056")] // :1207 hydraBudget.beginTurn(userAuthored: hydraHeads == nil): a heads report or a refusal turn continues the count
    [InlineData(1)]
    [InlineData(0)]
    public void A_heads_report_turn_and_a_refusal_turn_keep_the_count_but_still_end_the_streamed_block(int heads)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraBudget", SpentBudget());
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraStream", (Guid.NewGuid(), "entry", Guid.NewGuid(), 4L));
        ThreadRuntimeReflection.Set(rig.Runtime, "delegationParser", new HydraDelegationStreamParser(Guid.NewGuid(), "entry"));
        HydraDelegationBudget? budget = null;
        object? stream = "unset";
        object? parser = "unset";

        var task = rig.StartTurn("[Hydra] report", hydraHeads: Enumerable.Range(1, heads).Select(index => (long)index).ToArray(), whileStarting: () =>
        {
            budget = ThreadRuntimeReflection.Get<HydraDelegationBudget>(rig.Runtime, "hydraBudget");
            stream = ThreadRuntimeReflection.Get<object>(rig.Runtime, "hydraStream");
            parser = ThreadRuntimeReflection.Get<object>(rig.Runtime, "delegationParser");
        });
        rig.Finish(task);

        Assert.Equal(2, budget!.Rounds);
        Assert.Equal(1, budget.Refusals);
        Assert.Null(stream);
        Assert.Null(parser);
    }

    // ---- a finished head told more (PR-RUNTIME-057) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-057")] // :1211-1216 a finished Droppy-run head is put back to running, finishedAt cleared
    public void A_finished_droppy_run_head_told_more_is_put_back_to_running()
    {
        var finishedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with
        {
            Hydra = new HydraHeadInfo(1, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated)
            {
                HydraHeadInfoStatus = HydraHeadInfo.Status.Completed,
                FinishedAt = finishedAt,
            },
        });
        HydraHeadInfo? during = null;

        var task = rig.StartTurn("one more thing", whileStarting: () => during = rig.App.Thread(rig.InitialThread.Id)!.Hydra);

        Assert.Equal(HydraHeadInfo.Status.Running, during!.HydraHeadInfoStatus);
        Assert.Null(during.FinishedAt);
        rig.Finish(task);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-057")] // :1211 `info.isFinished` - a head that is still running keeps its record untouched
    public void A_running_head_keeps_its_record_when_a_turn_starts()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with
        {
            Hydra = new HydraHeadInfo(1, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated),
        });

        rig.Send("first");

        Assert.Equal(HydraHeadInfo.Status.Running, rig.Thread.Hydra!.HydraHeadInfoStatus);
    }

    // ---- ensureModel (PR-RUNTIME-061) ----

    private static void SeedCodexCatalog(ThreadRuntimeTurnRig rig) =>
        rig.H.Main.OnMain(() => rig.App.Providers.UpdateCatalog(new ValueList<ModelOption>(
        [
            new ModelOption("model-default", "Default", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "high", isDefault: true),
            new ModelOption("model-other", "Other", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "low"),
        ]), ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-RUNTIME-061")] // :1401-1410 no model: the provider default and its default effort
    public void A_thread_with_no_model_takes_the_providers_default_model_and_its_default_effort()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex, thread => thread with { Model = null, Effort = null });
        SeedCodexCatalog(rig);

        rig.Send("hello");

        Assert.Equal("model-default", rig.Thread.Model);
        Assert.Equal("high", rig.Thread.Effort);
        Assert.Equal("model-default", rig.Session.Inputs.Single().Model);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-061")] // :1409 `$0.effort ?? model.defaultEffort`: an existing effort is never overwritten
    public void An_effort_the_thread_already_has_is_kept_when_the_default_model_is_filled_in()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex, thread => thread with { Model = null, Effort = "low" });
        SeedCodexCatalog(rig);

        rig.Send("hello");

        Assert.Equal("model-default", rig.Thread.Model);
        Assert.Equal("low", rig.Thread.Effort);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-061")] // :1402 `guard thread.model == nil`: a thread that has a model does no work at all
    public void A_thread_that_already_has_a_model_is_left_alone()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex, thread => thread with { Model = "model-other", Effort = null });
        SeedCodexCatalog(rig);

        rig.Send("hello");

        Assert.Equal("model-other", rig.Thread.Model);
        Assert.Null(rig.Thread.Effort);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-061")] // failure_cases: no default model means nothing is written and the session gets a nil model
    public void With_no_default_model_nothing_is_written_and_the_turn_goes_out_without_one()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Opencode, thread => thread with { Model = null, Effort = "low" });
        rig.H.Main.OnMain(() => Assert.Null(rig.App.Providers.DefaultModel(ProviderKind.Opencode)));

        rig.Send("hello");

        Assert.Null(rig.Thread.Model);
        Assert.Equal("low", rig.Thread.Effort);
        Assert.Null(rig.Session.Inputs.Single().Model);
    }

    // ---- the cached working-tree snapshots (PR-RUNTIME-063) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-063")] // :1261-1263 settledTree = nil; latestTree = nil; treeEpoch += 1
    public void A_turn_start_invalidates_the_cached_snapshots()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        ThreadRuntimeReflection.Set(rig.Runtime, "settledTree", ("old-tree", 3L));
        ThreadRuntimeReflection.Set(rig.Runtime, "latestTree", "newer-tree");
        var epochBefore = ThreadRuntimeReflection.Get<long>(rig.Runtime, "treeEpoch");
        object? settled = "unset";
        object? latest = "unset";
        long epochAfter = -1;

        var task = rig.StartTurn("go", whileStarting: () =>
        {
            settled = ThreadRuntimeReflection.Get<object>(rig.Runtime, "settledTree");
            latest = ThreadRuntimeReflection.Get<object>(rig.Runtime, "latestTree");
            epochAfter = ThreadRuntimeReflection.Get<long>(rig.Runtime, "treeEpoch");
        });
        rig.Finish(task);

        Assert.Null(settled);
        Assert.Null(latest);
        Assert.Equal(epochBefore + 1, epochAfter);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-063")] // :1257-1260 hydraTouched is deliberately NOT cleared: the next turn's finish counts a native head's files
    public void Files_a_native_head_changed_between_turns_are_counted_by_the_next_turn()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        ThreadRuntimeReflection.Get<HashSet<string>>(rig.Runtime, "hydraTouched")!.Add("native/head.txt");

        rig.Send("go");
        rig.Complete();

        Assert.Equal(["native/head.txt"], Assert.Single(rig.Runtime.Turns).TouchedPaths!);
    }

    // ---- the base checkpoint (PR-RUNTIME-062, PR-RUNTIME-064) ----

    private static string RefPrefix(Guid thread) => "refs/droppy-code/checkpoints/" + thread.ToString().ToLowerInvariant() + "/";

    [Fact]
    [Trait("Req", "PR-RUNTIME-062")] // :1254-1270 refs/droppy-code/checkpoints/<thread uuid lowercased>/<turn>-start in the project directory
    public void The_start_checkpoint_is_a_ref_named_for_the_thread_and_turn_in_the_project_repository()
    {
        AppModelGitRepo? repo = null;
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, projectFactory: h => (repo = AppModelGitRepo.WithCommit(h.NewFolder("repo"))).Root);

        rig.Send("first");
        rig.Complete();
        rig.Send("second");

        var id = rig.InitialThread.Id;
        Assert.Equal([RefPrefix(id) + "0-start", RefPrefix(id) + "1-start"], rig.Runtime.Turns.Select(turn => turn.BaseCheckpoint));
        foreach (var turn in rig.Runtime.Turns)
            Assert.True(repo!.Raw("rev-parse", "--verify", "--quiet", turn.BaseCheckpoint!).Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-062")] // :1255 `initialThread.worktreePath ?? project.path`
    public void The_start_checkpoint_is_taken_in_the_threads_worktree_when_it_has_one()
    {
        AppModelGitRepo? project = null;
        AppModelGitRepo? worktree = null;
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude,
            projectFactory: h => (project = AppModelGitRepo.WithCommit(h.NewFolder("project-repo"))).Root,
            configureWithHarness: (h, thread) => thread with { WorktreePath = (worktree = AppModelGitRepo.WithCommit(h.NewFolder("worktree-repo"))).Root });

        rig.Send("first");

        var reference = RefPrefix(rig.InitialThread.Id) + "0-start";
        Assert.Equal(reference, Assert.Single(rig.Runtime.Turns).BaseCheckpoint);
        Assert.True(worktree!.Raw("rev-parse", "--verify", "--quiet", reference).Succeeded);
        Assert.False(project!.Raw("rev-parse", "--verify", "--quiet", reference).Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-062")] // :1267 `guard await git.isRepository() else { return false }`: no checkpoint, and the turn still goes
    public void In_a_directory_that_is_not_a_repository_the_turn_has_no_base_checkpoint_and_still_goes_out()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);

        rig.Send("first");

        Assert.Null(Assert.Single(rig.Runtime.Turns).BaseCheckpoint);
        Assert.Equal("first", Assert.Single(rig.Session.Inputs).Text);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-064")] // :1272-1282 the checkpoint is recorded on the turn before the message reaches the model
    public void The_base_checkpoint_is_on_the_turn_before_the_message_is_sent()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude,
            projectFactory: h => AppModelGitRepo.WithCommit(h.NewFolder("repo")).Root);
        string? atSend = "unset";
        rig.Session.SendScript = _ =>
        {
            atSend = rig.Runtime.Turns.Last().BaseCheckpoint;
            return Task.CompletedTask;
        };

        rig.Send("first");

        Assert.Equal(RefPrefix(rig.InitialThread.Id) + "0-start", atSend);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-064")] // :1273 `if let warmup { await warmup.value }`: a message waits out the warm-up before it is sent
    public void A_turn_waits_for_a_pending_warm_up_before_it_sends()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var gate = new TaskCompletionSource();
        ThreadRuntimeReflection.Set(rig.Runtime, "warmup", MainActor.Spawn(async () => await gate.Task));

        var task = rig.StartTurn("typed meanwhile");
        for (var round = 0; round < 40; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(25);
        }
        Assert.Empty(rig.Session.Inputs);
        Assert.Equal(RuntimePhase.Starting, rig.Runtime.Phase);

        gate.SetResult();
        rig.Finish(task);

        Assert.Equal("typed meanwhile", Assert.Single(rig.Session.Inputs).Text);
    }
}
