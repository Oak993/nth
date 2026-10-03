using Marrow.App.Tests.Fixtures;
using Marrow.App.Runtime;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :302-304.
public sealed class AppModelHydraMergeDeferralTests
{
    [Theory]
    [InlineData("MERGE_HEAD")]
    [InlineData("rebase-merge")]
    [Trait("Req", "PR-HYDRAMERGE-124")]
    public void A_checkout_operation_returns_deferred_without_record_note_or_commit(string marker)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        f.Start(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team edit\n");
        var path = Path.Combine(project.Path, ".git", marker);
        if (marker == "MERGE_HEAD") File.WriteAllText(path, f.Repos[project.Id].Git("rev-parse", "HEAD"));
        else Directory.CreateDirectory(path);
        var calls = new List<ShellInvocation>();

        var outcome = f.Pass(lead, project, observer: calls.Add);

        Assert.Equal("Deferred", outcome);
        Assert.DoesNotContain(calls, call => HydraMergeReviewFixture.IsGit(call, "commit-tree") || HydraMergeReviewFixture.IsGit(call, "update-ref") || HydraMergeReviewFixture.IsGit(call, "push"));
        Assert.Empty(f.Runtime(lead).HydraMerges);
        Assert.Empty(f.Notes(lead));
        Assert.True(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-124")]
    public void A_lead_that_resumes_during_capture_returns_deferred_without_record_note_or_retry()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        f.Start(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team edit\n");
        var calls = new List<ShellInvocation>();

        var outcome = f.Pass(lead, project, observer: call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, "write-tree"))
                typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime(lead), true);
        });

        Assert.Equal("Deferred", outcome);
        Assert.Contains(calls, call => HydraMergeReviewFixture.IsGit(call, "write-tree"));
        Assert.DoesNotContain(calls, call => HydraMergeReviewFixture.IsGit(call, "commit-tree") || HydraMergeReviewFixture.IsGit(call, "update-ref") || HydraMergeReviewFixture.IsGit(call, "push"));
        Assert.Empty(f.Runtime(lead).HydraMerges);
        Assert.Empty(f.Notes(lead));
        Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
    }

    [Theory]
    [InlineData("MERGE_HEAD", true)]
    [InlineData("rebase-merge", true)]
    [InlineData("resumed", false)]
    [Trait("Req", "PR-HYDRAMERGE-124")] // App/AppModel+HydraMerge.swift:171,174,177-179: the caller of a .deferred pass stops before marking and before the resume sweep
    public void A_deferred_pass_stops_the_coordinator_before_anything_is_marked_or_the_resume_sweep_is_scheduled(string cause, bool retryScheduled)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var elsewhere = f.AddProject("Elsewhere", origin: false);
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        var head = f.AddHead(lead, ["a.txt"]);
        var waiting = f.AddLead(elsewhere, "Waiting", ["a.txt"]); // a cold lead with work: only a resume sweep would load its runtime (:54-58)
        f.Start(lead);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team edit\n");
        var path = Path.Combine(project.Path, ".git", cause);
        if (cause == "MERGE_HEAD") File.WriteAllText(path, f.Repos[project.Id].Git("rev-parse", "HEAD"));
        else if (cause == "rebase-merge") Directory.CreateDirectory(path);
        var calls = new List<ShellInvocation>();

        f.Run(lead, call =>
        {
            calls.Add(call);
            if (cause == "resumed" && HydraMergeReviewFixture.IsGit(call, "write-tree"))
                typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime(lead), true);
        });
        f.H.Main.Loop.RunUntilIdle(); // anything the coordinator scheduled would run now

        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree") || HydraMergeReviewFixture.IsGit(c, "update-ref") || HydraMergeReviewFixture.IsGit(c, "push"));
        Assert.Empty(f.Runtime(lead).HydraMerges);
        Assert.Empty(f.Notes(lead));
        Assert.Equal(retryScheduled, f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged); // nothing marked merged (:177)
        Assert.Null(f.Model.Thread(head.Id)!.Hydra!.MergedAt); // nor any head (:179)
        Assert.Null(f.Model.ExistingRuntime(waiting.Id)); // and no resume sweep (:174)
        Assert.False(f.Runtime(lead).IsHydraMerging);
        Assert.Null(f.Runtime(lead).HydraMergeStage); Assert.Null(f.Runtime(lead).HydraMergeStep);
        Assert.Null(f.Runtime(lead).HydraMergeStartedAt); Assert.Null(f.Runtime(lead).HydraMergeNoteID);
    }
}
