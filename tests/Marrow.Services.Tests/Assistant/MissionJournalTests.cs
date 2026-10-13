using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 15.3 "Missions are journaled"; ED-NEW-dextra-as5-7): the mission record goes to the
// store's journal and comes back whole; a damaged body or another version reads as nothing; and a mission that was live
// when nth ended comes back Paused and marked Interrupted, through the store's own open.
[Trait("Decision", "ED-NEW-dextra-as5-7")]
[Trait("Feature", "AS-5")]
public sealed class MissionJournalTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private static MissionRecord Full(MissionState state = MissionState.Working)
    {
        var lead = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var head = Guid.Parse("10000000-0000-4000-8000-000000000002");
        var mission = new Mission(Guid.Parse("20000000-0000-4000-8000-000000000002"), "Fix the login timeout", "dextra", MissionOrigin.Owner, state)
        {
            Heads = new ValueList<MissionHead>([
                new MissionHead(head.ToString("D"), "Tests", HeadState.Working) { DependsOn = new ValueList<string>(["lead"]) },
                new MissionHead("10000000-0000-4000-8000-000000000003", "Docs", HeadState.Done),
            ]),
            ParentMissionId = Guid.Parse("30000000-0000-4000-8000-000000000003"),
            ChildMissionIds = new ValueList<Guid>([Guid.Parse("40000000-0000-4000-8000-000000000004")]),
            ThreadIds = new ValueList<Guid>([lead, head]),
            TaintContext = new TaintContext(new Taint(new ValueList<TaintSource>([new TaintSource(ContentOrigin.Web, "example.org/page")]))),
            ReviewRounds = 1,
            ChangedFiles = 3,
            WorktreePath = @"C:\nth\worktrees\m",
            Branch = "nth/fix-the-login-timeout-20000000",
        };
        return new MissionRecord(mission)
        {
            Brief = "Make the login wait 30 s.",
            ProjectId = Guid.Parse("50000000-0000-4000-8000-000000000005"),
            ProjectPath = @"C:\code\app",
            Provider = ProviderKind.Codex,
            Model = "model-a",
            Team = true,
            Level = 3,
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, @"C:\code\app", @"C:\nth\worktrees\m")
            {
                WorkingFolder = @"C:\nth\worktrees\m",
                Repository = @"C:\code\app",
                Branch = "nth/fix-the-login-timeout-20000000",
                BaseBranch = "main",
                BaseCommit = "0123456789012345678901234567890123456789",
            },
            LeadThread = lead,
            Report = "Changed the timeout and added a test.",
            Artifacts = new ValueList<string>(["src/login.cs"]),
            Reviews = new ValueList<MissionReview>([new MissionReview(false, "No test.", "model-b", At)]),
            Reason = null,
            PendingRedirect = "Also update the docs.",
            BudgetUsd = 2.5m,
            Created = At,
        };
    }

    [Fact]
    public void A_record_comes_back_whole()
    {
        var record = Full();
        var back = MissionJournal.FromJson(MissionJournal.ToJson(record));
        Assert.Equal(record, back);
        Assert.Equal("model-b", back!.ReviewedBy);
    }

    [Fact]
    public void A_bare_record_comes_back_whole()
    {
        var record = new MissionRecord(new Mission(Guid.NewGuid(), "Tidy", "dextra", MissionOrigin.Duty, MissionState.Planning))
        {
            ProjectId = Guid.NewGuid(),
            Created = At,
            Ended = At.AddHours(1),
        };
        Assert.Equal(record, MissionJournal.FromJson(MissionJournal.ToJson(record)));
    }

    [Theory]
    [InlineData("v")]
    [InlineData("state")]
    [InlineData("provider")]
    [InlineData("created")]
    [InlineData("project")]
    public void A_damaged_body_reads_as_nothing(string field)
    {
        var body = MissionJournal.ToJson(Full());
        body[field] = field == "v" ? 2 : "not-a-value";
        Assert.Null(MissionJournal.FromJson(body));
        body.Remove(field);
        Assert.Null(MissionJournal.FromJson(body));
        Assert.Null(MissionJournal.FromJson(null));
        Assert.Null(MissionJournal.FromJson(new JsonObject { ["v"] = 1, ["heads"] = "not a list" }));
    }

    [Theory]
    [InlineData(MissionState.Planning, MissionState.Planning)]
    [InlineData(MissionState.Working, MissionState.Working)]
    [InlineData(MissionState.Reviewing, MissionState.Reviewing)]
    [InlineData(MissionState.NeedsYou, MissionState.Working)]
    public void A_live_mission_comes_back_paused_and_interrupted(MissionState state, MissionState resumeTo)
    {
        var body = MissionJournal.Interrupted(MissionJournal.ToJson(Full(state) with { Waiting = "Waiting for a free agent slot." }));
        var back = MissionJournal.FromJson(body)!;
        Assert.Equal(MissionState.Paused, back.Mission.State);
        Assert.Equal(resumeTo, back.Mission.ResumeTo);
        Assert.Equal(PauseCause.Owner, back.Mission.PausedBy);
        Assert.True(back.Interrupted);
        Assert.Null(back.Waiting);
        Assert.Equal(HeadState.Paused, back.Mission.Heads[0].State);
        Assert.Equal(HeadState.Working, back.Mission.Heads[0].ResumeTo);
        Assert.Equal(HeadState.Done, back.Mission.Heads[1].State);
        Assert.Equal("Also update the docs.", back.PendingRedirect);
    }

    [Fact]
    public void A_stopping_mission_comes_back_stopped_and_a_pausing_one_paused()
    {
        var stopping = Full(MissionState.Stopping) with
        {
            Mission = Full().Mission with
            {
                State = MissionState.Stopping,
                Heads = new ValueList<MissionHead>([new MissionHead("h", "Tests", HeadState.Stopping)]),
            },
        };
        var stopped = MissionJournal.FromJson(MissionJournal.Interrupted(MissionJournal.ToJson(stopping)))!;
        Assert.Equal(MissionState.Stopped, stopped.Mission.State);
        Assert.Equal(HeadState.Stopped, stopped.Mission.Heads[0].State);
        Assert.False(stopped.Interrupted);

        var pausing = Full() with { Mission = Full().Mission with { State = MissionState.Pausing, ResumeTo = MissionState.Working, PausedBy = PauseCause.Reserve } };
        var paused = MissionJournal.FromJson(MissionJournal.Interrupted(MissionJournal.ToJson(pausing)))!;
        Assert.Equal(MissionState.Paused, paused.Mission.State);
        Assert.Equal(MissionState.Working, paused.Mission.ResumeTo);
        Assert.Equal(PauseCause.Reserve, paused.Mission.PausedBy);
        Assert.True(paused.Interrupted);
    }

    [Theory]
    [InlineData(MissionState.Paused)]
    [InlineData(MissionState.Stopped)]
    [InlineData(MissionState.Blocked)]
    [InlineData(MissionState.Done)]
    [InlineData(MissionState.Failed)]
    public void A_settled_mission_stays_as_it_was(MissionState state)
    {
        Assert.Null(MissionJournal.Interrupted(MissionJournal.ToJson(Full(state))));
        Assert.Null(MissionJournal.Interrupted(new JsonObject { ["v"] = 1 }));
    }

    [Fact]
    public void The_store_applies_the_rule_at_open_after_a_crash()
    {
        var options = new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted };
        var working = Full();
        var done = Full(MissionState.Done) with { Mission = Full().Mission with { Id = Guid.NewGuid(), State = MissionState.Done } };
        var crashing = AssistantStore.Open(temp.Roots, options);
        crashing.SaveMission(MissionJournal.StoreId(working.Id), MissionJournal.ToJson(working with { Mission = working.Mission with { State = MissionState.Planning } }));
        crashing.SaveMission(MissionJournal.StoreId(working.Id), MissionJournal.ToJson(working));
        crashing.SaveMission(MissionJournal.StoreId(done.Id), MissionJournal.ToJson(done));
        crashing.AbandonForTests();

        using var reopened = AssistantStore.Open(temp.Roots, options);
        Assert.Equal([MissionJournal.StoreId(working.Id)], reopened.OpenReport.Interrupted);
        var back = MissionJournal.FromJson(reopened.Mission(MissionJournal.StoreId(working.Id))!.Body)!;
        Assert.Equal(MissionState.Paused, back.Mission.State);
        Assert.Equal(MissionState.Working, back.Mission.ResumeTo);
        Assert.True(back.Interrupted);
        Assert.Equal(working.Workspace, back.Workspace);
        Assert.Equal(MissionState.Done, MissionJournal.FromJson(reopened.Mission(MissionJournal.StoreId(done.Id))!.Body)!.Mission.State);
        Assert.Equal(32, MissionJournal.StoreId(working.Id).Length);
    }
}
