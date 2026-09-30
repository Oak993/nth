using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :352-362.
public sealed class AppModelHydraMergeRequestFailureTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-055")]
    public void A_thrown_request_creation_error_keeps_the_pushed_branch_and_schedules_a_quiet_retry()
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        var requestReached = false;
        f.Run(call =>
        {
            if (!call.Arguments.Contains("create")) return;
            requestReached = true;
            throw new ShellError("request interrupted");
        });

        Assert.True(requestReached);
        var branch = (string)HydraMergeProofFixture.Call("HydraBranchName", null, f.Lead)!;
        var title = $"Hydra pushed {branch} but could not open a merge request.";
        var body = $"request interrupted\n\nThe work is in the checkout and on `{branch}`; the next try opens the request.";
        var record = Assert.Single(f.Runtime.HydraMerges);
        Assert.Equal(title + ": " + body, record.Detail);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        Assert.Equal("Project", record.Project);
        Assert.Null(record.Label);
        Assert.Null(record.Url);
        Assert.Equal(0, record.Files);
        Assert.True(record.Retryable);
        Assert.True(f.Runtime.HasHydraMergeRetryPending);
        Assert.Empty(f.Notes);
        Assert.Equal("team edit\n", File.ReadAllText(Path.Combine(f.Repo.Root, "a.txt")));
        Assert.NotEqual("", f.Repo.Git("rev-parse", branch).Trim());
    }
}
