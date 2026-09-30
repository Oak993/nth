using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :226-237.
public sealed class AppModelHydraMergeFailureTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:226-237
    public void A_nonretryable_failure_records_and_announces_once_with_no_retry()
    {
        using var f = new HydraMergeReviewFixture(origin: false);
        f.Edit();
        f.Run();
        f.Run();

        var record = Assert.Single(f.Runtime.HydraMerges);
        const string title = "Hydra did not merge: no remote.";
        const string body = "The team's work is in the checkout; there is no origin to push it to.";
        Assert.Equal(title + ": " + body, record.Detail);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        Assert.Equal(f.H.Main.Time.GetUtcNow(), record.At);
        Assert.Equal("Project", record.Project);
        Assert.Null(record.Label); Assert.Null(record.Url); Assert.Equal(0, record.Files); Assert.Null(record.Retryable);
        Assert.Equal(title + "\n" + body, Assert.Single(f.Notes));
        Assert.False(f.Runtime.HasHydraMergeRetryPending);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:229-235
    public void An_identical_detail_after_a_nonfailure_record_is_a_new_failure()
    {
        using var f = new HydraMergeReviewFixture(origin: false);
        f.Edit();
        f.H.Main.OnMain(() => f.Runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged,
            Project = "Project", Files = 0,
            Detail = "Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.",
        }));

        f.Run();

        Assert.Equal(2, f.Runtime.HydraMerges.Count);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, f.Runtime.HydraMerges.Last().HydraMergeRecordOutcome);
        Assert.Single(f.Notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:234; Runtime/ThreadRuntime.swift:2554-2559
    public void A_failure_trims_the_record_list_to_twenty_and_saves_the_newest_record()
    {
        using var f = new HydraMergeReviewFixture(origin: false);
        f.Edit();
        f.H.Main.OnMain(() =>
        {
            for (var i = 0; i < 20; i++) f.Runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged,
                Project = "Project", Files = 1, Detail = "old " + i,
            });
        });

        f.Run();
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(20, f.Runtime.HydraMerges.Count);
        Assert.Equal("old 1", f.Runtime.HydraMerges[0].Detail);
        var saved = Coding.Decode<ThreadDocument>(File.ReadAllBytes(Storage.ThreadURL(f.Lead.Id)));
        Assert.NotNull(saved);
        Assert.Equal(20, saved.HydraMerges.Count);
        Assert.Equal("old 1", saved.HydraMerges[0].Detail);
        Assert.Equal(f.Runtime.HydraMerges.Last().Detail, saved.HydraMerges.Last().Detail);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, saved.HydraMerges.Last().HydraMergeRecordOutcome);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-125")] // App/AppModel+HydraMerge.swift:230-235
    public void A_retryable_failure_speaks_only_if_the_two_most_recent_records_are_retryable_failures()
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        f.Forge.Reply(0, stderr: "offline create", status: "1");
        f.H.Main.OnMain(() =>
        {
            f.Runtime.RecordHydraMerge(new HydraMergeRecord
            { At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Files = 0, Detail = "old retry", Retryable = true });
            f.Runtime.RecordHydraMerge(new HydraMergeRecord
            { At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged, Files = 1, Detail = "success" });
            f.Runtime.RecordHydraMerge(new HydraMergeRecord
            { At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Files = 0, Detail = "new retry", Retryable = true });
        });

        f.Run();

        Assert.Equal(4, f.Runtime.HydraMerges.Count);
        var record = f.Runtime.HydraMerges.Last();
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        Assert.Equal(f.H.Main.Time.GetUtcNow(), record.At);
        Assert.Equal("Project", record.Project);
        Assert.Null(record.Label); Assert.Null(record.Url); Assert.Equal(0, record.Files);
        Assert.True(record.Retryable);
        Assert.Equal("Hydra pushed hydra/team-job-" + f.Lead.Id.ToString("D")[..6] +
            " but could not open a merge request.: offline create\n\nThe work is in the checkout and on `hydra/team-job-" +
            f.Lead.Id.ToString("D")[..6] + "`; the next try opens the request.", record.Detail);
        Assert.Empty(f.Notes);
    }
}
