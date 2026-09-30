using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// Offline regression drafts: exercise the completion callback, not only its readiness helper.
public sealed class ThreadRuntimeHydraAutoMergeCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-RUNTIME-128")]
    public void A_finished_job_enters_the_merge_coordinator_after_the_leads_final_reply(bool headReport)
    {
        using var f = new HydraMergeProofFixture();
        // Deliberately no origin: observing the coordinator's real no-remote result stays offline.
        var project = f.AddProject("Project", origin: false);
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        var head = headReport ? f.AddHead(lead, ["a.txt"], index: 1) : null;
        f.Tool(lead, "a.txt");
        f.Start(lead);
        var runtime = f.Runtime(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "finished team work\n");
        var captured = new HydraPromptCaptureSession();
        f.H.Main.OnMain(() =>
        {
            Prepare(f, lead);
            if (head is not null)
            {
                f.Model.UpdateThread(head.Id, current => current with
                {
                    Hydra = current.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
                });
                AttachOfflineSession(f, lead, project.Path, captured);
                // Finishing a real head queues its report and starts the lead's review turn.
                f.Model.FinishHydraHead(head.Id, TurnStatus.Completed, "The team change is ready.", head.Hydra!.Landing);
            }
            else
            {
                runtime.RehearseTurn("Finish the change.", ["a.txt"]);
            }
        });
        if (head is not null)
        {
            f.H.Main.Loop.RunUntil(() => captured.Inputs.Count == 1, AppModelHarness.Bound);
            Assert.Contains("The team change is ready.", Assert.Single(captured.Inputs).Text, StringComparison.Ordinal);
            Assert.Contains(runtime.Entries, entry => entry.Item.TimelineItemContent is
                TimelineItem.Content.User { Value.HydraHeads: { } heads } && heads.Contains(1L));
        }
        Assert.Equal(RuntimePhase.Running, runtime.Phase);
        Assert.Empty(runtime.HydraMerges);
        f.H.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        f.H.Main.Loop.RunUntil(() => runtime.HydraMerges.Count == 1 && !runtime.IsHydraMerging, AppModelHarness.Bound);

        Assert.Equal(TurnStatus.Completed, runtime.Turns.Last().Status);
        var merge = Assert.Single(runtime.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, merge.HydraMergeRecordOutcome);
        Assert.Equal("Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", merge.Detail);
        Assert.Contains(f.Notes(lead), note => note.StartsWith("Hydra did not merge: no remote.", StringComparison.Ordinal));
        Assert.False(runtime.Turns.Last().HydraMerged);
        Assert.False(runtime.IsHydraMergeQueued);
        if (head is not null) Assert.Equal(HydraHeadInfo.Status.Completed, f.Model.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus);
    }

    [Theory]
    [InlineData(TurnStatus.Interrupted, true)]
    [InlineData(TurnStatus.Failed, true)]
    [InlineData(TurnStatus.Completed, false)]
    [Trait("Req", "PR-RUNTIME-128")]
    public void An_unsuccessful_turn_or_disabled_setting_never_enters_the_merge_coordinator(TurnStatus status, bool enabled)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project", origin: false);
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        f.Tool(lead, "a.txt");
        f.Start(lead);
        var runtime = f.Runtime(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "pending team work\n");
        f.H.Main.OnMain(() =>
        {
            Prepare(f, lead);
            f.Model.Settings.HydraAutoMerge = enabled;
            runtime.RehearseTurn("Work on the change.", ["a.txt"]);
            runtime.Rehearse(new ProviderEvent.TurnCompleted(status, null));
        });
        f.H.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle &&
            runtime.Turns.Last().CompletedAt is not null && f.Model.Thread(lead.Id)!.LastStatus == status,
            AppModelHarness.Bound);
        f.H.Main.Loop.RunUntilIdle();
        Assert.Empty(runtime.HydraMerges);
        Assert.False(runtime.IsHydraMerging);
        Assert.False(runtime.IsHydraMergeQueued);
        Assert.All(runtime.Turns, turn => Assert.False(turn.HydraMerged));
    }

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

    private static void AttachOfflineSession(HydraMergeProofFixture f, ChatThread lead, string directory,
        HydraPromptCaptureSession captured)
    {
        var current = f.Model.Thread(lead.Id)!;
        var type = typeof(ThreadRuntime);
        var keepsPolicy = (bool)type.GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [current.Provider])!;
        var launch = f.Model.HydraLaunch(current);
        var signature = type.GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [current, directory, false, keepsPolicy ? launch : null]);
        type.GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Runtime(lead), captured);
        type.GetField("sessionSignature", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Runtime(lead), signature);
    }
}
