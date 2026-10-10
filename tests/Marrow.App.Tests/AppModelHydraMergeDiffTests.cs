using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :310-313.
public sealed class AppModelHydraMergeDiffTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-034")]
    public void The_patch_is_the_parents_diff_to_the_captured_tree_and_drives_request_files_and_success_note()
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        var parent = f.Repo.Git("rev-parse", "HEAD").Trim();
        var calls = new List<ShellInvocation>();

        f.Run(calls.Add);

        var record = Assert.Single(f.Runtime.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Merged, record.HydraMergeRecordOutcome);
        Assert.Equal(1, record.Files);
        var branch = (string)HydraMergeProofFixture.Call("HydraBranchName", null, f.Lead)!;
        var captured = f.Repo.Git("rev-parse", branch + "^{tree}").Trim();
        var diff = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "diff") && !call.Arguments.Contains("--name-only"));
        Assert.Equal(["diff", "--no-ext-diff", "-M", parent, captured], HydraMergeReviewFixture.GitArgs(diff));
        var create = Assert.Single(f.Forge.Calls(), call => call.Arguments.Contains("create"));
        var description = create.Arguments.ToList().FindIndex(argument => argument is "--description" or "--body");
        Assert.True(description >= 0);
        // AppModel+HydraMerge.swift:844-869 uses U+2212 for deletions; OD-0008 authorizes the Marrow product name.
        Assert.Equal("Opened by Nth once its Hydra team finished.\n\n## Files\n- `a.txt` (+1 −1)",
            create.Arguments[description + 1]);
        Assert.Contains("1 file (+1 −1)", Assert.Single(f.Notes), StringComparison.Ordinal); // Swift:908-912, including U+2212.
    }
}
