using System.Collections;
using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The gate on starting the auto-merge when a lead's turn finishes: ThreadRuntime.swift:2494-2505 (B3; the record cites
// :2456-2461). The merge itself is the coordinator's (AppModel+HydraMerge); here its start is observed through the merge
// record it leaves, which in a repository with no origin is a "no remote" failure and never touches a network.
public sealed class ThreadRuntimeAutoMergeGateTests
{

    private static void Prepare(HydraMergeProofFixture f, ChatThread lead)
    {
        f.Model.Settings.HydraAlwaysHeads = false;
        f.Model.Settings.HydraQueueHeads = false;
        f.Model.Settings.HydraIsolateHeads = false;
        f.Model.Settings.HydraAutoClearFinished = false;
        f.Model.Settings.AntiSlopEnabled = false;
        f.Model.Settings.ChimeWhenFinished = false;
        f.Model.Settings.NotifyWhenFinished = false;
        f.Model.UpdateThread(lead.Id, current => current with { Model = "offline-model" });
    }

    private static void Attach(HydraMergeProofFixture f, ChatThread lead, string directory, ThreadRuntimeScriptedSession session)
    {
        var current = f.Model.Thread(lead.Id)!;
        var type = typeof(ThreadRuntime);
        var keeps = (bool)type.GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current.Provider])!;
        var signature = type.GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [current, directory, false, keeps ? f.Model.HydraLaunch(current) : null]);
        f.Runtime(lead).Observe(session);
        ThreadRuntimeReflection.Set(f.Runtime(lead), "session", session);
        ThreadRuntimeReflection.Set(f.Runtime(lead), "sessionSignature", signature);
    }

    private static void Quiet(HydraMergeProofFixture f)
    {
        for (var round = 0; round < 40; round++)
        {
            f.H.Main.RunUntilIdle();
            Thread.Sleep(25);
        }
    }

    [Theory]
    [Trait("Req", "PR-HYDRAMERGE-002")] // :2494-2505 the merge starts only when all nine conditions hold
    [InlineData(Blocker.None)]
    [InlineData(Blocker.TurnFailed)]
    [InlineData(Blocker.TurnInterrupted)]
    [InlineData(Blocker.AutoMergeSettingOff)]
    [InlineData(Blocker.HydraOffForTheChat)]
    [InlineData(Blocker.BatchOutstanding)]
    [InlineData(Blocker.HeadWaiting)]
    [InlineData(Blocker.NativeHeadStillRunning)]
    [InlineData(Blocker.TurnContinues)]
    public void A_finished_lead_turn_starts_the_auto_merge_only_when_nothing_of_the_team_is_outstanding(Blocker blocker)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project", origin: false);
        var lead = f.AddLead(project, "Lead", ["a.txt"], hydra: blocker != Blocker.HydraOffForTheChat);
        f.Tool(lead, "a.txt");
        f.Start(lead);
        var runtime = f.Runtime(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "finished team work\n");
        var session = new ThreadRuntimeScriptedSession { SendScript = _ => new TaskCompletionSource().Task };
        var status = blocker switch
        {
            Blocker.TurnFailed => TurnStatus.Failed,
            Blocker.TurnInterrupted => TurnStatus.Interrupted,
            _ => TurnStatus.Completed,
        };
        f.H.Main.OnMain(() =>
        {
            Prepare(f, lead);
            f.Model.Settings.HydraAutoMerge = blocker != Blocker.AutoMergeSettingOff;
            Attach(f, lead, project.Path, session);
            runtime.RehearseTurn("Work on the change.", ["a.txt"]);
            switch (blocker)
            {
                case Blocker.BatchOutstanding:
                    ((IDictionary)ThreadRuntimeReflection.Get<object>(runtime, "hydraBatches")!).Add(Guid.NewGuid(),
                        Activator.CreateInstance(typeof(ThreadRuntime).GetNestedType("HydraBatch", BindingFlags.NonPublic)!, true)!);
                    break;
                case Blocker.HeadWaiting:
                    var waiting = (IList)ThreadRuntimeReflection.Get<object>(runtime, "hydraWaitingStorage")!;
                    waiting.Add(Activator.CreateInstance(waiting.GetType().GetGenericArguments()[0])!);
                    break;
                case Blocker.NativeHeadStillRunning:
                    runtime.Rehearse(new ProviderEvent.AgentStarted(
                        new AgentSpawn { Id = "agent-1", Description = "Read the packet", Prompt = "brief", IsBackground = true }));
                    break;
                case Blocker.TurnContinues:
                    runtime.EnqueueFollowUp("and then this", []);
                    break;
            }
            runtime.Rehearse(new ProviderEvent.TurnCompleted(status, null));
        });
        f.H.Main.Loop.RunUntil(() => f.Model.Thread(lead.Id)!.LastStatus == status && runtime.Turns.Last().CompletedAt is not null,
            AppModelHarness.Bound);

        if (blocker == Blocker.None)
        {
            f.H.Main.Loop.RunUntil(() => runtime.HydraMerges.Count == 1 && !runtime.IsHydraMerging, AppModelHarness.Bound);
            Assert.Equal(HydraMergeRecord.Outcome.Failed, Assert.Single(runtime.HydraMerges).HydraMergeRecordOutcome);
            return;
        }

        if (blocker == Blocker.TurnContinues) f.H.Main.Loop.RunUntil(() => session.Inputs.Count == 1, AppModelHarness.Bound);
        Quiet(f);
        Assert.Empty(runtime.HydraMerges);
        Assert.False(runtime.IsHydraMerging);
        Assert.False(runtime.IsHydraMergeQueued);
    }
}
