using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :79-187,902-906.
public sealed class AppModelHydraMergeCoordinatorTests
{
    [Theory]
    [InlineData("legacy-empty")]
    [InlineData("empty")]
    [InlineData("already-main")]
    [InlineData("stray")]
    [Trait("Req", "PR-CHAT-189")] // App/AppModel+HydraMerge.swift:148-164,181-186,902-906
    [Trait("Req", "PR-HYDRAMERGE-015")] // App/AppModel+HydraMerge.swift:148-160
    public void Settlement_only_comes_from_legacy_held_empty_work_or_every_share_already_on_main_without_strays(string scenario)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", scenario == "already-main" ? ["a.txt"] : ["gone.txt"]);
        var head = f.AddHead(lead, ["gone.txt"]);
        var doc = f.Documents[lead.Id];
        if (scenario == "legacy-empty") f.Documents[lead.Id] = doc with { HydraMerges = new ValueList<HydraMergeRecord>([new HydraMergeRecord
        { At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Held, Files = 1, Detail = "old saved entry" }]) };
        if (scenario == "stray") f.Tool(lead, @"C:\real-work-outside-all-projects\stray.txt");
        f.Start(lead);
        f.Run(lead);
        var records = f.Runtime(lead).HydraMerges;
        Assert.DoesNotContain(records.Where(r => scenario != "legacy-empty" || r.Detail != "old saved entry"), r => r.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Held);
        Assert.DoesNotContain(f.Notes(lead), n => n.StartsWith("Hydra is waiting for another chat's files.", StringComparison.Ordinal));
        if (scenario is "legacy-empty" or "already-main")
        {
            var r = records.Last();
            Assert.Equal(HydraMergeRecord.Outcome.Settled, r.HydraMergeRecordOutcome);
            Assert.Equal(0, r.Files); Assert.Null(r.Project); Assert.Null(r.Label); Assert.Null(r.Url); Assert.Null(r.Detail);
            Assert.Equal(f.H.Main.Time.GetUtcNow(), r.At);
            Assert.Equal("Hydra has no files left to merge.\nThe checkout has no remaining changes from this job to send. No new merge request was needed.", Assert.Single(f.Notes(lead)));
            if (scenario == "legacy-empty") { Assert.Equal(2, records.Count); Assert.Equal(HydraMergeRecord.Outcome.Held, records[0].HydraMergeRecordOutcome); }
        }
        else if (scenario == "empty") { Assert.Empty(records); Assert.Empty(f.Notes(lead)); }
        else { Assert.Equal(HydraMergeRecord.Outcome.Stray, Assert.Single(records).HydraMergeRecordOutcome); Assert.DoesNotContain(records, r => r.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Settled); }
        Assert.Equal(scenario != "stray", f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.Equal(scenario != "stray", f.Model.Thread(head.Id)!.Hydra!.MergedAt is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-024")] // App/AppModel+HydraMerge.swift:168-179
    public void Partial_success_or_deferral_never_aborts_later_projects_and_books_no_turn_or_head(bool deferred)
    {
        using var f = new HydraMergeProofFixture();
        var own = f.AddProject("Own");
        var middle = f.AddProject("Middle", origin: deferred);
        var last = f.AddProject("Last", origin: false);
        var lead = f.AddLead(own, "Lead", ["a.txt"]);
        f.Tool(lead, Path.Combine(middle.Path, "a.txt"), Path.Combine(last.Path, "a.txt"));
        var head = f.AddHead(lead, ["a.txt"]);
        File.WriteAllText(Path.Combine(own.Path, "a.txt"), "own edit\n");
        if (deferred) File.WriteAllText(Path.Combine(middle.Path, ".git", "MERGE_HEAD"), f.Repos[middle.Id].Git("rev-parse", "HEAD"));
        f.Start(lead);
        f.Forge(own);
        var visited = new List<string>();
        f.Run(lead, call =>
        {
            if (HydraMergeReviewFixture.IsGit(call, "remote") && call.Arguments.Contains("get-url"))
            {
                if (!visited.Contains(call.Directory!)) visited.Add(call.Directory!);
            }
        });
        Assert.Equal([own.Path, middle.Path, last.Path], visited);
        var records = f.Runtime(lead).HydraMerges;
        Assert.Equal(deferred ? ["Own", "Last"] : ["Own", "Middle", "Last"], records.Select(r => r.Project));
        Assert.Equal(HydraMergeRecord.Outcome.Merged, records[0].HydraMergeRecordOutcome);
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.Null(f.Model.Thread(head.Id)!.Hydra!.MergedAt);
        Assert.DoesNotContain(records, r => r.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Held);
        if (deferred) Assert.DoesNotContain(f.Notes(lead), n => n.StartsWith("Middle:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("failed")]
    [InlineData("deferred")]
    [InlineData("gone")]
    [Trait("Req", "PR-HYDRAMERGE-123")] // App/AppModel+HydraMerge.swift:171-179
    public void Exactly_one_weak_resume_sweep_is_enqueued_before_booking_and_starts_after_the_merging_flag_clears(string outcome)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: outcome != "failed");
        var q = f.AddProject("Other", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var other = f.AddLead(q, "Other", ["a.txt"]);
        if (outcome == "deferred") File.WriteAllText(Path.Combine(p.Path, ".git", "MERGE_HEAD"), f.Repos[p.Id].Git("rev-parse", "HEAD"));
        f.Start(lead);
        var enqueued = 0;
        var executed = 0;
        var observer = new HydraMergeQueueObserver(f.H.Main.Loop, (body, starting) =>
        {
            if (starting)
            {
                executed++;
                Assert.False(f.Runtime(lead).IsHydraMerging);
                Assert.True(f.Runtime(lead).Turns.Single().HydraMerged);
            }
            else
            {
                enqueued++;
                Assert.True(f.Runtime(lead).IsHydraMerging);
                Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
                var fields = body.Target!.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .Select(field => field.GetValue(body.Target)).ToArray();
                var weak = Assert.Single(fields.OfType<WeakReference<AppModel>>());
                Assert.DoesNotContain(fields, v => v is AppModel);
                if (outcome == "gone") weak.SetTarget(null!); // Exact weak-target disappearance at the scheduled boundary.
            }
        });
        f.H.Main.Loop.Run(async () =>
        {
            using var scope = MainActor.Executor.Override(observer);
            await f.Model.AutoMergeHydraWork(lead.Id);
        }, AppModelHarness.Bound);
        f.H.Main.Loop.RunUntilIdle();
        var clean = outcome is "clean" or "gone";
        Assert.Equal(clean ? 1 : 0, enqueued);
        Assert.Equal(clean ? 1 : 0, executed);
        if (outcome == "clean")
        {
            f.H.Main.Loop.RunUntil(() => f.Model.ExistingRuntime(other.Id)?.HydraMerges.Count > 0, AppModelHarness.Bound);
            Assert.Single(f.Runtime(other).HydraMerges);
        }
        else Assert.Null(f.Model.ExistingRuntime(other.Id));
    }
}
