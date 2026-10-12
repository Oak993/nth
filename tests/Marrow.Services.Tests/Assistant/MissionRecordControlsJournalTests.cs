using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6a, 12.6b; ED-NEW-dextra-as6-7): what Revert, Restart and a paused task's release add to the
// mission journal. The keys are optional and written only when set, so a journal written before them loads as it did and a
// record without them writes exactly what it wrote; the ones that are set come back whole.
[Trait("Decision", "ED-NEW-dextra-as6-7")]
[Trait("Feature", "AS-6")]
public sealed class MissionRecordControlsJournalTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private static MissionWorkspace Worktree(string name) => new(MissionWorkspaceKind.Worktree, @"C:\code\app", @"C:\nth\worktrees\" + name)
    {
        WorkingFolder = @"C:\nth\worktrees\" + name,
        Repository = @"C:\code\app",
        Branch = "nth/mission-20000000-fix",
        BaseBranch = "main",
        BaseCommit = "0123456789012345678901234567890123456789",
    };

    private static MissionRecord Plain() => new(new Mission(Guid.Parse("20000000-0000-4000-8000-000000000002"), "Fix the login timeout", "dextra",
        MissionOrigin.Owner, MissionState.Stopped) { Reverted = true, ResumeTo = MissionState.Working, StoppedBy = StopCause.Owner })
    {
        Brief = "Make the login wait 30 s.",
        ProjectId = Guid.Parse("50000000-0000-4000-8000-000000000005"),
        ProjectPath = @"C:\code\app",
        Provider = ProviderKind.Codex,
        Created = At,
    };

    [Fact]
    public void A_record_without_the_new_keys_writes_none_of_them_and_an_old_journal_loads_as_the_first_try()
    {
        var json = MissionJournal.ToJson(Plain());
        foreach (var key in new[] { "attempt", "agentsReleased", "coldStartTokens", "revertedWork", "previous" }) Assert.False(json.ContainsKey(key), key);
        var back = MissionJournal.FromJson(json)!;
        Assert.Equal(1, back.Attempt);
        Assert.False(back.AgentsReleased);
        Assert.Null(back.RevertedWork);
        Assert.Empty(back.Previous);
        Assert.Equal(json.ToJsonString(), MissionJournal.ToJson(back).ToJsonString());
    }

    [Fact]
    public void Reverted_work_the_attempt_the_earlier_workspaces_and_the_release_come_back_whole()
    {
        var reverted = new RevertedWork(Worktree("m1"), "refs/nth/reverted/20000000000040008000000000000002/20261012T093000000Z",
            "0123456789012345678901234567890123456789", At, 3);
        var record = Plain() with
        {
            Attempt = 3,
            AgentsReleased = true,
            ColdStartTokens = 60_400,
            RevertedWork = reverted,
            Previous = new ValueList<PreviousWorkspace>([new PreviousWorkspace(Worktree("m1-2"), At.AddHours(-2)), new PreviousWorkspace(Worktree("m1-3"), At.AddHours(-1))]),
        };
        var back = MissionJournal.FromJson(MissionJournal.ToJson(record))!;
        Assert.Equal(3, back.Attempt);
        Assert.True(back.AgentsReleased);
        Assert.Equal(60_400, back.ColdStartTokens);
        Assert.Equal(reverted, back.RevertedWork);
        Assert.Equal(At.AddDays(7), back.RevertedWork!.Expires);
        Assert.Equal(2, back.Previous.Count);
        Assert.Equal(At.AddHours(-2), back.Previous[0].At);
        Assert.Equal(@"C:\nth\worktrees\m1-3", back.Previous[1].Workspace.Path);
        Assert.Equal(MissionJournal.ToJson(record).ToJsonString(), MissionJournal.ToJson(back).ToJsonString());
    }

    [Fact]
    public void A_copy_set_aside_by_a_revert_comes_back_with_its_manifest_and_a_damaged_revert_reads_as_a_damaged_body()
    {
        var snapshot = new MissionWorkspace(MissionWorkspaceKind.Snapshot, @"C:\code\app", @"C:\nth\worktrees\s1")
        {
            WorkingFolder = @"C:\nth\worktrees\s1",
            Manifest = new ValueList<SnapshotEntry>([new SnapshotEntry("a.txt", "ab12", 3)]),
        };
        var reverted = new RevertedWork(snapshot, @"C:\nth\worktrees\s1.reverted-20261012T093000000Z", null, At, 1);
        var back = MissionJournal.FromJson(MissionJournal.ToJson(Plain() with { RevertedWork = reverted }))!;
        Assert.Equal(reverted, back.RevertedWork);
        Assert.Equal("a.txt", back.RevertedWork!.Workspace.Manifest.Single().Path);

        var damaged = MissionJournal.ToJson(Plain() with { RevertedWork = reverted });
        damaged["revertedWork"]!.AsObject().Remove("ref");
        Assert.Null(MissionJournal.FromJson(damaged));
    }
}
