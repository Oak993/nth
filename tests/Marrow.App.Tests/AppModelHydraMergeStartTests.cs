using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :49-75, :101-117 (the resume sweep and the start and
// end of one merge run). Every merge here runs real git in a disposable repository under the harness data root.
public sealed class AppModelHydraMergeStartTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRAMERGE-007")] // AppModel+HydraMerge.swift:60-72; ThreadRuntime.swift:2485-2490
    public void The_resume_sweep_needs_no_unmerged_completed_turn_but_the_threads_last_turn_must_be_completed(bool lastTurnCompleted)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.AddHead(lead, ["head.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "head.txt"), "head work\n");
        // Every turn the lead has reads as spent; the head's landed work is the only thing no merge has taken (:62-65).
        var spent = f.Documents[lead.Id].Turns[0] with { HydraMerged = true };
        var turns = new List<TurnRecord> { spent };
        if (!lastTurnCompleted)
            turns.Add(spent with { Id = Guid.NewGuid(), Index = 2, Status = TurnStatus.Interrupted, HydraMerged = false,
                TouchedPaths = new ValueList<string>(["a.txt"]) });
        f.Documents[lead.Id] = f.Documents[lead.Id] with { Turns = new ValueList<TurnRecord>(turns) };
        f.Start();

        f.H.Main.Loop.Run(() => f.Model.ResumeHydraMerges(), AppModelHarness.Bound);

        var records = f.H.Main.OnMain(() => f.Runtime(lead).HydraMerges.ToArray());
        if (lastTurnCompleted)
        {
            // The sweep went on to the merge with no unmerged turn left: the checkout has no origin to push to.
            var record = Assert.Single(records);
            Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
            Assert.Equal("Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", record.Detail);
        }
        else
        {
            // The last turn was interrupted: never resumed, whatever else is unmerged.
            Assert.Empty(records);
            Assert.Empty(f.Notes(lead));
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-011")] // AppModel+HydraMerge.swift:101-105; Hydra.swift:521
    public void Starting_a_merge_sets_the_running_flag_the_start_time_a_fresh_note_id_and_the_first_stage()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var started = new List<(bool Merging, DateTimeOffset? At, string? NoteID, string? Stage, HydraMergeStage? Step)>();
        // The first shell call of a run happens after the fields were set and before any stage moves on.
        var first = true;
        f.Run(lead, call =>
        {
            if (!first) return;
            first = false;
            var runtime = f.Runtime(lead);
            started.Add((runtime.IsHydraMerging, runtime.HydraMergeStartedAt, runtime.HydraMergeNoteID, runtime.HydraMergeStage, runtime.HydraMergeStep));
        });
        first = true;
        f.Run(lead, call =>
        {
            if (!first) return;
            first = false;
            var runtime = f.Runtime(lead);
            started.Add((runtime.IsHydraMerging, runtime.HydraMergeStartedAt, runtime.HydraMergeNoteID, runtime.HydraMergeStage, runtime.HydraMergeStep));
        });

        Assert.Equal(2, started.Count);
        foreach (var state in started)
        {
            Assert.True(state.Merging);
            Assert.Equal(f.H.Main.Time.GetUtcNow(), state.At);
            // UUID().uuidString: 36 characters, upper-case hex.
            Assert.Matches("^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", state.NoteID);
            Assert.Equal("Gathering the team's files", state.Stage);
            Assert.Equal(HydraMergeStage.Gathering, state.Step);
            Assert.Equal(0, (int)state.Step!.Value);
        }
        Assert.NotEqual(started[0].NoteID, started[1].NoteID);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failed")]
    [InlineData("nothing-left")]
    [InlineData("stray")]
    [InlineData("deferred")]
    [Trait("Req", "PR-HYDRAMERGE-012")] // AppModel+HydraMerge.swift:106-112 (defer), :147-165
    public void Every_exit_from_a_merge_clears_all_five_fields_and_the_outcome_note_takes_the_pills_identity(string exit)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: exit is "success" or "deferred");
        // A path that is gone, so that the stray exit still has one git call to observe from.
        var lead = f.AddLead(p, "Lead", exit == "stray" ? ["gone.txt"] : exit == "nothing-left" ? ["gone.txt"] : ["a.txt"]);
        if (exit == "stray") f.Tool(lead, @"C:\real-work-outside-all-projects\stray.txt");
        if (exit is "success" or "failed" or "deferred") File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        if (exit == "deferred") File.WriteAllText(Path.Combine(p.Path, ".git", "MERGE_HEAD"), f.Repos[p.Id].Git("rev-parse", "HEAD"));
        f.Start(lead);
        if (exit == "success") f.Forge(p);
        string? pillID = null;
        string? stageSeen = null;
        f.Run(lead, call =>
        {
            var runtime = f.Runtime(lead);
            pillID ??= runtime.HydraMergeNoteID;
            stageSeen = runtime.HydraMergeStage;
        });

        Assert.NotNull(pillID);
        Assert.NotNull(stageSeen);
        var runtime = f.H.Main.OnMain(() => f.Runtime(lead));
        f.H.Main.OnMain(() =>
        {
            Assert.False(runtime.IsHydraMerging);
            Assert.Null(runtime.HydraMergeStage);
            Assert.Null(runtime.HydraMergeStep);
            Assert.Null(runtime.HydraMergeStartedAt);
            Assert.Null(runtime.HydraMergeNoteID);
        });
        // The outcome note claimed the pill's identity: one timeline row from the first stage to the outcome.
        var notes = f.H.Main.OnMain(() => runtime.Entries
            .Where(e => e.Item.TimelineItemContent is TimelineItem.Content.User { Value.HydraHeads.Count: 0 })
            .Select(e => e.Item.Id).ToArray());
        if (exit is "nothing-left" or "deferred") Assert.Empty(notes);
        else Assert.Equal([pillID], notes);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-013")] // AppModel+HydraMerge.swift:113-116
    public void The_merge_waits_for_a_landing_already_in_flight_before_it_gathers_anything()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var gate = new TaskCompletionSource();
        f.H.Main.OnMain(() => f.Runtime(lead).HydraLanding = TaskHandle.Detached(async () => await gate.Task));
        var calls = new List<ShellInvocation>();
        Task merge = null!;

        f.H.Main.OnMain(() =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            merge = f.Model.AutoMergeHydraWork(lead.Id);
        });

        // The landing is still going into the checkout: the merge is on its way but has gathered nothing.
        Assert.False(merge.IsCompleted);
        Assert.Empty(calls);
        var waiting = f.H.Main.OnMain(() => (f.Runtime(lead).IsHydraMerging, f.Runtime(lead).HydraMergeStage, f.Runtime(lead).HydraMerges.Count));
        Assert.Equal((true, "Gathering the team's files", 0), waiting);

        gate.SetResult();
        f.H.Main.Loop.RunUntil(() => merge.IsCompleted, AppModelHarness.Bound);

        Assert.NotEmpty(calls);
        var record = Assert.Single(f.H.Main.OnMain(() => f.Runtime(lead).HydraMerges.ToArray()));
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        Assert.False(f.H.Main.OnMain(() => f.Runtime(lead).IsHydraMerging));
    }

    [Theory]
    [InlineData("history")]
    [InlineData("working-head")]
    [Trait("Req", "PR-HYDRAMERGE-111")] // AppModel+HydraMerge.swift:100-117; ThreadRuntime.swift:2485-2490
    public void After_the_landing_a_lead_that_is_no_longer_ready_ends_the_run_silently(string reason)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var head = f.AddHead(lead, ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var gate = new TaskCompletionSource();
        f.H.Main.OnMain(() => f.Runtime(lead).HydraLanding = TaskHandle.Detached(async () => await gate.Task));
        var calls = new List<ShellInvocation>();
        Task merge = null!;
        f.H.Main.OnMain(() =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            merge = f.Model.AutoMergeHydraWork(lead.Id);
        });
        Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).IsHydraMerging));

        // While the landing finishes, the lead stops being ready (history loading, or a head sent out again).
        f.H.Main.OnMain(() =>
        {
            if (reason == "history") typeof(ThreadRuntime).GetProperty("IsLoadingHistory")!.SetValue(f.Runtime(lead), true);
            else f.Model.UpdateThread(head.Id, t => t with { IsInPanel = true, Hydra = t.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
        });
        Assert.False(f.H.Main.OnMain(() => f.Runtime(lead).IsReadyForHydraMerge));
        gate.SetResult();
        f.H.Main.Loop.RunUntil(() => merge.IsCompleted, AppModelHarness.Bound);

        // Nothing was gathered, recorded, marked or noted, and the pill is gone.
        Assert.Empty(calls);
        f.H.Main.OnMain(() =>
        {
            var runtime = f.Runtime(lead);
            Assert.Empty(runtime.HydraMerges);
            Assert.False(runtime.Turns.Single().HydraMerged);
            Assert.False(runtime.IsHydraMerging);
            Assert.Null(runtime.HydraMergeStage);
            Assert.Null(runtime.HydraMergeStep);
            Assert.Null(runtime.HydraMergeStartedAt);
            Assert.Null(runtime.HydraMergeNoteID);
            Assert.DoesNotContain(runtime.Entries, e => e.Item.TimelineItemContent is TimelineItem.Content.User { Value.HydraHeads.Count: 0 });
        });
    }
}
