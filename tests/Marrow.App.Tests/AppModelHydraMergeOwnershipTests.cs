using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :533-771.
public sealed class AppModelHydraMergeOwnershipTests
{
    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, false)]
    [InlineData(HydraHeadInfo.Status.Stopped, true)]
    [InlineData(HydraHeadInfo.Status.Failed, true)]
    [InlineData(HydraHeadInfo.Status.Running, true)]
    [Trait("Req", "PR-HYDRAMERGE-117")] // App/AppModel+HydraMerge.swift:705-722
    [Trait("Req", "PR-HYDRAMERGE-077")] // :646-654 blocking versus nonblocking reservations.
    public void Only_a_completed_heads_eligible_later_parent_loses_its_block_while_reserving_the_path(HydraHeadInfo.Status status, bool blocks)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var low = f.AddLead(p, "Low", ["a.txt"], new Guid("00000000-0000-0000-0000-000000000001"));
        var high = f.AddLead(p, "High", ["a.txt"], new Guid("00000000-0000-0000-0000-000000000002"));
        var head = f.AddHead(high, ["a.txt"], status);
        f.Start(low, high);
        // AppModel.swift:132-136 resets persisted running heads; this models the live status AFTER bootstrap.
        f.H.Main.OnMain(() => f.Model.UpdateThread(head.Id, t => t with { Hydra = t.Hydra! with { HydraHeadInfoStatus = status } }));
        var maps = f.Owned(low, p);
        Assert.Equal("High", maps.Reserved["a.txt"]);
        Assert.Equal(blocks, maps.Blocking.ContainsKey("a.txt"));
        if (blocks) Assert.Equal("High", maps.Blocking["a.txt"]);
        var lowPaths = HydraMergeProofFixture.Property<ValueList<string>>(f.Work(low, p), "Paths");
        Assert.Equal(blocks ? [] : ["a.txt"], lowPaths);
        Assert.Equal("Low", f.Owned(high, p).Blocking["a.txt"]); // Earlier owner always blocks.
        f.H.Main.OnMain(() => f.Runtime(high).IsHydraMerging = true);
        Assert.Equal("High", f.Owned(low, p).Blocking["a.txt"]); // Not idle/eligible.
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-075")] // App/AppModel+HydraMerge.swift:690-728
    public void Reservations_use_exact_owner_titles_exclusions_last_writer_and_cold_chat_asymmetry()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var other = f.AddProject("Other");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var ownHead = f.AddHead(lead, ["own.txt"]);
        var archived = f.AddLead(p, "Archived", [], archived: true);
        f.AddHead(archived, ["archived.txt"]);
        var inactive = f.AddLead(p, "Off", [], hydra: false);
        f.AddHead(inactive, ["off.txt"]);
        var old = f.AddLead(p, "Old", [], updated: SwiftDate.Now - TimeSpan.FromDays(3));
        f.AddHead(old, ["old.txt"]);
        var incomplete = f.AddLead(p, "Incomplete", [], status: TurnStatus.Interrupted);
        f.AddHead(incomplete, ["incomplete.txt"]);
        f.AddLead(p, "Cold", ["cold.txt"]);
        var unrelated = f.AddLead(other, "Other lead", ["foreign.txt"], status: TurnStatus.Interrupted);
        var interrupted = f.AddLead(p, "Interrupted", ["shared.txt", "partial.txt", "layered.txt", Path.Combine(other.Path, "outside.txt")], status: TurnStatus.Interrupted, hydra: false);
        var active = f.AddLead(p, "Parent", [], new Guid("ffffffff-0000-0000-0000-000000000001"));
        var running = f.AddHead(active, ["shared.txt", "running.txt"], HydraHeadInfo.Status.Running);
        var orphan = f.AddHead(active, ["orphan.txt"], HydraHeadInfo.Status.Running);
        var merged = f.AddHead(active, ["merged.txt"]);
        var activeOld = f.AddLead(p, "Active old", [], archived: true, hydra: false, status: TurnStatus.Interrupted, updated: SwiftDate.Now - TimeSpan.FromDays(4));
        f.AddHead(activeOld, ["active-old.txt"], HydraHeadInfo.Status.Failed);
        var later = f.AddLead(p, "Later", ["later-only.txt"], new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        f.AddHead(later, ["layered.txt"]);
        f.Start(lead, interrupted, unrelated, activeOld, later);
        f.H.Main.OnMain(() =>
        {
            f.Model.UpdateThread(running.Id, t => t with { Hydra = t.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
            f.Model.UpdateThread(orphan.Id, t => t with { ParentThreadID = Guid.NewGuid(), Title = "Orphan head", Hydra = t.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
            f.Model.UpdateThread(merged.Id, t => t with { Hydra = t.Hydra! with { MergedAt = SwiftDate.Now } });
            f.Runtime(activeOld).IsHydraMerging = true; // :763 active work keeps claims regardless of age/archive/Hydra.
        });
        var (reserved, blocking) = f.Owned(lead, p);
        var expected = new Dictionary<string,string> { ["partial.txt"] = "Interrupted", ["shared.txt"] = "Parent", ["running.txt"] = "Parent", ["orphan.txt"] = "Orphan head", ["active-old.txt"] = "Active old", ["layered.txt"] = "Later", ["later-only.txt"] = "Later" };
        Assert.Equal(expected.OrderBy(p => p.Key), reserved.OrderBy(p => p.Key));
        expected["layered.txt"] = "Interrupted"; expected.Remove("later-only.txt"); // A later NON-blocking reservation does not erase the prior block.
        Assert.Equal(expected.OrderBy(p => p.Key), blocking.OrderBy(p => p.Key));
        Assert.Null(f.Model.ExistingRuntime(ownHead.Id));
        Assert.Null(f.Model.ExistingRuntime(f.Threads.Single(t => t.Title == "Cold").Id));
    }

    [Theory]
    [InlineData("landing")]
    [InlineData("head-tool-cold")]
    [InlineData("head-tool-live")]
    [InlineData("lead-tool")]
    [Trait("Req", "PR-HYDRAMERGE-130")] // App/AppModel+HydraMerge.swift:575,590,599-609,653-654
    [Trait("Req", "PR-HYDRAMERGE-077")] // :653-654 own landing/head-tool/lead-tool writes override a blocking claim.
    [Trait("Req", "PR-HYDRAMERGE-078")] // App/AppModel+HydraMerge.swift:664-669
    public void Own_landing_and_tool_edits_override_a_claim_but_observed_changes_do_not_and_every_turn_and_head_is_returned(string source)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var other = f.AddProject("Other");
        var lead = f.AddLead(p, "Lead", ["observed.txt", "free.txt", "owned.txt", "z-free.txt", "a-free.txt", Path.Combine(other.Path, "z-outside.txt"), Path.Combine(other.Path, "a-outside.txt")]);
        var sibling = f.AddLead(p, "Sibling", ["observed.txt", "owned.txt"], status: TurnStatus.Interrupted, hydra: false);
        var head = f.AddHead(lead, source == "landing" ? ["owned.txt", "z-free.txt", "a-free.txt", "z-free.txt"] : ["z-free.txt", "a-free.txt", "z-free.txt"]);
        var emptyHead = f.AddHead(lead, ["gone.txt"], index: 2, task: "no remaining files");
        if (source.StartsWith("head-tool", StringComparison.Ordinal)) f.Tool(head, " owned.txt ", Path.Combine(other.Path, "outside-tool.txt"));
        if (source == "lead-tool") f.Tool(lead, " owned.txt ", "", "   ", Path.Combine(other.Path, "outside-tool.txt"));
        foreach (var path in new[] { "observed.txt", "free.txt", "owned.txt", "z-free.txt", "a-free.txt" }) File.WriteAllText(Path.Combine(p.Path, path), "edit\n");
        var doc = f.Documents[lead.Id];
        var older = doc.Turns[0] with { Id = Guid.NewGuid(), Index = 0, TouchedPaths = new ValueList<string>(["free.txt"]) };
        f.Documents[lead.Id] = doc with { Turns = new ValueList<TurnRecord>([older, doc.Turns[0]]) };
        f.Start(source == "head-tool-live" ? [lead, sibling, head] : [lead, sibling]);
        var work = f.Work(lead, p);
        Assert.Equal(["a-free.txt", "free.txt", "owned.txt", "z-free.txt"], HydraMergeProofFixture.Property<ValueList<string>>(work, "Paths"));
        Assert.Equal([older.Id, doc.Turns[0].Id], HydraMergeProofFixture.Property<ValueList<Guid>>(work, "TurnIDs"));
        Assert.Equal([head.Id, emptyHead.Id], HydraMergeProofFixture.Property<ValueList<Guid>>(work, "HeadIDs"));
        var outside = new[] { Path.Combine(other.Path, "a-outside.txt"), Path.Combine(other.Path, "z-outside.txt") }.ToList();
        if (source != "landing") outside.Add(Path.Combine(other.Path, "outside-tool.txt"));
        Assert.Equal(outside.Order(StringComparer.Ordinal), HydraMergeProofFixture.Property<ValueList<string>>(work, "Outside"));
        var heads = HydraMergeProofFixture.Property<System.Collections.IEnumerable>(work, "Heads").Cast<object>().ToArray();
        Assert.Equal(source == "landing" ? ["owned.txt", "z-free.txt", "a-free.txt"] : source.StartsWith("head-tool", StringComparison.Ordinal) ? ["z-free.txt", "a-free.txt", "owned.txt"] : ["z-free.txt", "a-free.txt"], HydraMergeProofFixture.Property<ValueList<string>>(heads[0], "Files"));
        Assert.Equal("task", HydraMergeProofFixture.Property<string>(heads[0], "Task"));
        Assert.Equal(1, HydraMergeProofFixture.Property<long>(heads[0], "Index"));
        Assert.Empty(HydraMergeProofFixture.Property<ValueList<string>>(heads[1], "Files"));
        Assert.Equal(5, work.GetType().GetProperties().Length); // Swift HydraWork has precisely five members, no held map.
    }

    [Fact]
    [Trait("Rule", "R-FS-1")] // App/AppModel+HydraMerge.swift:660-663
    [Trait("Req", "PR-HYDRAMERGE-077")] // :655-663 normal disk/ignored/build filtering; stream proof in TruncatedOutputTests.
    public void Final_filters_keep_deleted_tracked_files_and_directories_and_remove_ignored_build_and_never_tracked_missing_paths()
    {
        // AppModel+HydraMerge.swift:653-663. Successful shortened streams are covered by the Q2 caller proof.
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        File.WriteAllText(Path.Combine(p.Path, ".gitignore"), "ignored.txt\n");
        repo.Git("add", ".gitignore"); repo.Git("commit", "-q", "-m", "ignore");
        File.Delete(Path.Combine(p.Path, "a.txt"));
        File.WriteAllText(Path.Combine(p.Path, "ignored.txt"), "ignore\n");
        Directory.CreateDirectory(Path.Combine(p.Path, "newdir"));
        File.WriteAllText(Path.Combine(p.Path, "newdir", "child"), "directory\n");
        var lead = f.AddLead(p, "Lead", ["missing.txt", "ignored.txt", "a.txt", "newdir", "node_modules/output.txt"]);
        f.Start(lead);
        var work = f.Work(lead, p);
        Assert.Equal(["a.txt", "newdir"], HydraMergeProofFixture.Property<ValueList<string>>(work, "Paths"));
    }
}
