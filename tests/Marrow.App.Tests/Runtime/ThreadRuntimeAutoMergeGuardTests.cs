using System.Collections;
using System.Reflection;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeAutoMergeGuardTests
{
    [Theory]
    [InlineData("continues")]
    [InlineData("status")]
    [InlineData("reports")]
    [InlineData("batches")]
    [InlineData("waiting")]
    [InlineData("thread")]
    [InlineData("hydra")]
    [InlineData("setting")]
    [InlineData("running-head")]
    [Trait("Req", "PR-RUNTIME-128")] // App/Runtime/ThreadRuntime.swift:2449-2461
    public void An_automatic_merge_gate_rejects_each_unmet_condition(string unmetCondition)
    {
        using var h = new AppModelHarness();
        var headKind = unmetCondition == "running-head" ? HydraHeadInfo.Kind.Native : (HydraHeadInfo.Kind?)null;
        var context = CreateRuntime(h, headKind);
        var runtime = context.Runtime;
        var continues = unmetCondition == "continues";
        var status = unmetCondition == "status" ? TurnStatus.Interrupted : TurnStatus.Completed;

        h.Main.OnMain(() =>
        {
            switch (unmetCondition)
            {
                case "reports":
                    AddPrivateSequenceItem(runtime, "hydraPendingReports");
                    break;
                case "batches":
                    AddPrivateDictionaryItem(runtime, "hydraBatches");
                    break;
                case "waiting":
                    AddPrivateSequenceItem(runtime, "hydraWaitingStorage");
                    break;
                case "thread":
                    runtime = new ThreadRuntime(Guid.NewGuid(), context.Model);
                    break;
                case "hydra":
                    context.Model.UpdateThread(context.Thread.Id, thread => thread with { HydraEnabled = false });
                    break;
                case "setting":
                    context.Model.Settings.HydraAutoMerge = false;
                    break;
            }

            Assert.Null(InvokePrivate<AppModel?>(runtime, "AutoMergeAppIfReady", continues, status));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-128")] // App/Runtime/ThreadRuntime.swift:2449-2461
    public void An_automatic_merge_gate_returns_the_app_when_every_condition_holds()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h);

        Assert.Same(context.Model,
            h.Main.OnMain(() => InvokePrivate<AppModel?>(context.Runtime, "AutoMergeAppIfReady", false, TurnStatus.Completed)));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-125")] // App/Runtime/ThreadRuntime.swift:2429-2435
    public void A_completed_turn_continues_while_a_Droppy_run_head_is_still_running()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h, HydraHeadInfo.Kind.Droppy);

        Assert.True(h.Main.OnMain(() =>
            InvokePrivate<bool>(context.Runtime, "ShouldContinueForRunningDroppyHeads", false, TurnStatus.Completed)));
        Assert.False(h.Main.OnMain(() =>
            InvokePrivate<bool>(context.Runtime, "ShouldContinueForRunningDroppyHeads", true, TurnStatus.Completed)));
        Assert.False(h.Main.OnMain(() =>
            InvokePrivate<bool>(context.Runtime, "ShouldContinueForRunningDroppyHeads", false, TurnStatus.Interrupted)));
        Assert.Equal(1L, h.Main.OnMain(() => context.Model.RunningDroppyHeads(context.Thread.Id)));

        var headID = h.Main.OnMain(() => Assert.Single(context.Model.HydraHeads(context.Thread.Id)).Id);
        h.Main.OnMain(() => context.Model.UpdateThread(headID, head => head with
        {
            Hydra = head.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed },
        }));
        Assert.False(h.Main.OnMain(() =>
            InvokePrivate<bool>(context.Runtime, "ShouldContinueForRunningDroppyHeads", false, TurnStatus.Completed)));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-166")] // App/Runtime/ThreadRuntime.swift:1836-1845
    public void Revert_is_rejected_while_a_Droppy_run_head_is_still_running()
    {
        using var h = new AppModelHarness();
        var context = CreateRuntime(h, HydraHeadInfo.Kind.Droppy);
        h.Main.OnMain(() =>
        {
            context.Runtime.RehearseTurn("The user asked for a change.");
            context.Runtime.Rehearse(new ProviderEvent.TurnStarted("provider-turn"));
            context.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
        WaitForEndedTurn(h.Main, context.Runtime);
        var turnID = Assert.Single(context.Runtime.Turns).Id;

        var error = Assert.Throws<ProviderError>(() =>
            h.Main.Loop.Run(() => context.Runtime.Revert(turnID, restoreFiles: false), AppModelHarness.Bound));

        Assert.Equal("Stop the running agents before reverting this conversation.", error.ErrorDescription);
        Assert.Equal(turnID, Assert.Single(context.Runtime.Turns).Id);
    }

    private static (AppModel Model, ChatThread Thread, ThreadRuntime Runtime) CreateRuntime(
        AppModelHarness harness,
        HydraHeadInfo.Kind? runningHeadKind = null)
    {
        var project = AppModelSamples.Project("Runtime Hydra", harness.NewFolder("runtime-hydra"));
        var thread = AppModelSamples.Thread(project) with { HydraEnabled = true };
        var threads = new List<ChatThread> { thread };
        if (runningHeadKind is { } kind)
        {
            threads.Add(new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                ParentThreadID = thread.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with
                {
                    HydraHeadInfoKind = kind,
                    HydraHeadInfoStatus = HydraHeadInfo.Status.Running,
                },
            });
        }

        harness.WriteLibrary([project], threads);
        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
            if (runningHeadKind is not null)
                model.UpdateThread(threads[1].Id, head => head with
                {
                    Hydra = head.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
                });
        });
        return (model, thread, runtime);
    }

    private static void WaitForEndedTurn(TestRuntime main, ThreadRuntime runtime) =>
        main.Loop.RunUntil(
            () => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Any(turn =>
                turn.Status != TurnStatus.Running && turn.CompletedAt is not null),
            AppModelHarness.Bound);

    private static T InvokePrivate<T>(ThreadRuntime runtime, string methodName, params object?[] arguments)
    {
        var method = typeof(ThreadRuntime).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ThreadRuntime).FullName, methodName);
        return (T)method.Invoke(runtime, arguments)!;
    }

    private static void AddPrivateSequenceItem(ThreadRuntime runtime, string fieldName)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ThreadRuntime).FullName, fieldName);
        var sequence = (IList)field.GetValue(runtime)!;
        var elementType = field.FieldType.GetGenericArguments()[0];
        sequence.Add(elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
    }

    private static void AddPrivateDictionaryItem(ThreadRuntime runtime, string fieldName)
    {
        var field = typeof(ThreadRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ThreadRuntime).FullName, fieldName);
        var dictionary = (IDictionary)field.GetValue(runtime)!;
        dictionary.Add(Guid.NewGuid(), null);
    }
}
