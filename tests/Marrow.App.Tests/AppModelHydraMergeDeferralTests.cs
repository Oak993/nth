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
}
