using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: final security gate S4, middle bullet (ED-NEW-secfinal-26; with ED-NEW-secfinal-22). Every Discard (the owner's, the 7-day sweep, the
// earlier-attempts sweep, a dry run) now carries the mission's own project folder from its record, and the real workspaces refuse a workspace whose
// repository is not that folder's repository, with the same plain MissionWorkspaceError as the other checks, before git or any delete.
[Trait("Decision", "ED-NEW-secfinal-26")]
public sealed class MissionDiscardRecordedSourceTests : IDisposable
{
    private static readonly Guid Mission = Guid.Parse("5c1f0e7a-3b42-4d19-9a6e-7f2b8d4c1e04");
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static StoreMissionWorkspaces Workspaces(string store) =>
        new((mission, attempt) => Path.Combine(store, "worktrees", mission.ToString("N") + (attempt > 1 ? "-" + attempt : "")));

    private static ApprovalVerdict Says(ActionClass actionClass, Verdict verdict, PolicyRule rule = PolicyRule.Alone, string reason = "the rules") =>
        new(new Classification(actionClass, reason), new PolicyDecision(verdict, rule, reason));

    private static ApprovalVerdict Policy(ApprovalRequest request) => Says(ActionClass.Unknown, Verdict.Ask, PolicyRule.Unknown, "Not recognised");

    private static void EndTurn(MissionRig rig, Guid thread, string? answer)
    {
        rig.Agents.Finish(thread, answer);
        rig.Runner.OnTurnFinished(thread, TurnStatus.Completed, continues: false);
    }

    [Fact]
    public async Task A_worktree_record_that_names_another_repository_is_refused_and_nothing_is_deleted()
    {
        using var runtime = new TestRuntime();
        using var mine = GitRepository.WithCommit();
        using var other = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspaces = Workspaces(store.Root);
        var workspace = await MissionWorkspaces.Create(Mission, "Discard", mine.Root, Path.Combine(store.Root, "worktrees", Mission.ToString("N")));
        other.Git("branch", workspace.Branch!);

        await Assert.ThrowsAsync<MissionWorkspaceError>(() => workspaces.Discard(workspace with { Repository = other.Root }, mine.Root));

        Assert.True(Directory.Exists(workspace.Path));
        Assert.Contains(workspace.Branch!, other.Git("branch", "--list", workspace.Branch!), StringComparison.Ordinal);
        await workspaces.Discard(workspace, mine.Root);
        Assert.False(Directory.Exists(workspace.Path));
    }

    [Fact]
    public void The_owners_discard_passes_the_missions_own_project_folder()
    {
        var rig = new MissionRig(temp.Folder, Policy);
        var record = rig.Started();
        EndTurn(rig, record.LeadThread!.Value, "Done.");
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the reviewer to start");
        EndTurn(rig, rig.Now(record).ReviewerThread!.Value, "VERDICT: PASS");
        MissionRig.Until(() => rig.Now(record).MergePending, "the merge card");
        var merge = rig.Queue.Of(record.Id).Single();

        Assert.Null(rig.Queue.Answer(merge.Id, DecisionAnswer.Discard));
        MissionRig.Until(() => rig.Now(record).Workspace is null, "the discard");

        Assert.Equal([record.ProjectPath], rig.Workspaces.DiscardedFor);
        Assert.Equal(Path.Combine(temp.Folder, "owner"), record.ProjectPath);
    }

    [Fact]
    public void The_seven_day_sweep_passes_the_missions_own_project_folder()
    {
        var time = new ManualTimeProvider();
        var rig = new MissionRig(temp.Folder, Policy, controls: new MissionRigControls { Time = time });
        var record = rig.Started();
        EndTurn(rig, record.LeadThread!.Value, "Done.");
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the reviewer to start");
        EndTurn(rig, rig.Now(record).ReviewerThread!.Value, "VERDICT: PASS");
        MissionRig.Until(() => rig.Now(record).MergePending, "the merge card");
        var merge = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Queue.Answer(merge.Id, DecisionAnswer.Merge));
        MissionRig.Until(() => !rig.Now(record).MergePending, "the merge");
        time.Advance(TimeSpan.FromDays(8));

        Assert.True(MissionRig.Within(rig.Runner.CleanUpWorkspaces(), 10));

        Assert.Equal([record.ProjectPath], rig.Workspaces.DiscardedFor);
    }

    [Fact]
    public void The_earlier_attempts_sweep_passes_the_missions_own_project_folder()
    {
        var rig = new ControlsRig(temp.Folder);
        var id = Guid.NewGuid();
        var path = Path.Combine(temp.Folder, "worktrees", id.ToString("N"));
        var branch = MissionWorkspaces.BranchName(id, "Fix the login timeout");
        var created = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var failed = new MissionRecord(new Mission(id, "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Failed) { ChangedFiles = 3, Branch = branch, WorktreePath = path })
        {
            Brief = "Make the login wait 30 s, not 5 s, and keep the tests green.",
            ProjectId = MissionRig.Project,
            ProjectPath = Path.Combine(temp.Folder, "owner"),
            Provider = ProviderKind.Claude,
            Model = "model-a",
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, Path.Combine(temp.Folder, "owner"), path)
            {
                WorkingFolder = path, Repository = Path.Combine(temp.Folder, "owner"), Branch = branch, BaseBranch = "main", BaseCommit = "0000000000000000000000000000000000000000",
            },
            Reason = "The reviewer failed it 3 times.",
            Created = created,
            Ended = created.AddHours(1),
        };
        rig.Runner.Load([failed]);
        Assert.Null(rig.Controls.Restart(failed.Id));
        MissionRig.Until(() => rig.Runner.Find(failed.Id)!.LeadThread is not null, "the restarted lead");
        rig.Time.Advance(TimeSpan.FromDays(7.2));

        Assert.True(MissionRig.Within(rig.Runner.CleanUpWorkspaces(), 10));

        Assert.Empty(rig.Runner.Find(failed.Id)!.Previous);
        Assert.Equal([failed.ProjectPath], rig.Rig.Workspaces.DiscardedFor);
    }

    [Fact]
    public void A_dry_runs_discard_passes_the_missions_own_project_folder()
    {
        using var rig = new DutyHostRig();
        rig.Save(DutyHostRig.Daily("d1", 8, "Read the invoices and file them."));
        rig.Host.Start();
        Assert.Null(rig.Host.Scheduler.RunNow("d1", dry: true));
        var mission = rig.MissionOf(rig.Host.Scheduler.Find("d1")!);
        var lead = rig.LeadOf(mission);

        rig.Agents.Finish(lead, "Would file 3 invoices.");
        rig.Controller.OnTurnFinished(lead, TurnStatus.Completed, false);
        Assert.True(SpinWait.SpinUntil(() => rig.Workspaces.Calls.Contains("discard"), TimeSpan.FromSeconds(10)), "the dry run's workspace was not discarded");

        Assert.Equal([mission.ProjectPath], rig.Workspaces.DiscardedFor);
        Assert.NotEqual("", mission.ProjectPath);
    }
}
