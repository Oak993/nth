using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :533-670 (what one pass over a checkout gathers: the
// unmerged turns, the eligible heads, the lead's own tool edits and the checkout sweep). Each test calls HydraWorkFor
// (private, no public surface observes its five-member result) the way the merge does, on real repositories.
public sealed class AppModelHydraMergeWorkTests
{
    private static ChatThread Native(HydraMergeProofFixture f, ChatThread head) => HydraMergeSpec.Change(f, head,
        t => t with { Hydra = t.Hydra! with { HydraHeadInfoKind = HydraHeadInfo.Kind.Native, Landing = null } });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-067")] // AppModel+HydraMerge.swift:534-535; ThreadRuntime.swift:2507-2509
    public void The_work_starts_from_the_unmerged_turns_recorded_paths_in_turn_order()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt", "b.txt"]);
        var first = f.Documents[lead.Id].Turns[0];
        var spent = first with { Id = Guid.NewGuid(), Index = 0, HydraMerged = true, TouchedPaths = new ValueList<string>(["x.txt"]) };
        var older = first with { Id = Guid.NewGuid(), Index = 2, TouchedPaths = null }; // recorded by an older build
        var again = first with { Id = Guid.NewGuid(), Index = 3, TouchedPaths = new ValueList<string>(["a.txt", "c.txt"]) };
        f.Documents[lead.Id] = f.Documents[lead.Id] with { Turns = new ValueList<TurnRecord>([spent, first, older, again]) };
        foreach (var name in new[] { "b.txt", "c.txt", "x.txt" }) File.WriteAllText(Path.Combine(p.Path, name), name + "\n");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "changed\n");
        f.Start(lead);

        var work = HydraMergeSpec.WorkAt(f, lead, p, p, p.Path);

        // The merged turn contributes nothing (x.txt), a turn with no recorded paths contributes nothing and still counts.
        Assert.Equal(["a.txt", "b.txt", "c.txt"], HydraMergeSpec.PathsOf(work));
        Assert.Equal([first.Id, older.Id, again.Id], HydraMergeSpec.TurnIDsOf(work));
        Assert.Empty(HydraMergeSpec.OutsideOf(work));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-068")] // AppModel+HydraMerge.swift:474-483
    public void A_head_counts_until_a_merge_marked_it_or_its_landing_was_kept_as_a_patch_or_failed()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", []);
        var marked = f.AddHead(lead, ["a-marked.txt"], index: 1);
        var patched = f.AddHead(lead, ["b-patched.txt"], landed: false, index: 2);
        var failed = f.AddHead(lead, ["c-failed.txt"], index: 3);
        var clean = f.AddHead(lead, ["d-clean.txt"], index: 4);
        var none = f.AddHead(lead, [], index: 5);
        var late = f.AddHead(lead, ["f-late.txt"], index: 6);
        HydraMergeSpec.Change(f, marked, t => t with { Hydra = t.Hydra! with { MergedAt = SwiftDate.Now } });
        HydraMergeSpec.Change(f, failed, t => t with { Hydra = t.Hydra! with { Landing = t.Hydra.Landing! with { Error = "the patch did not apply" } } });
        HydraMergeSpec.Change(f, none, t => t with { Hydra = t.Hydra! with { Landing = null } });
        // Every head finished long ago; the lead's last merged turn started after all of them (the old timestamp shortcut would drop them).
        var finished = SwiftDate.Now - TimeSpan.FromHours(2);
        foreach (var head in new[] { marked, patched, failed, clean, none, late })
            HydraMergeSpec.Change(f, head, t => t with { Hydra = t.Hydra! with { FinishedAt = finished } });
        var turn = f.Documents[lead.Id].Turns[0];
        f.Documents[lead.Id] = f.Documents[lead.Id] with { Turns = new ValueList<TurnRecord>([turn with { HydraMerged = true, StartedAt = SwiftDate.Now - TimeSpan.FromHours(1) }]) };
        foreach (var name in new[] { "a-marked.txt", "b-patched.txt", "c-failed.txt", "d-clean.txt", "f-late.txt" })
            File.WriteAllText(Path.Combine(p.Path, name), name + "\n");
        f.Start(lead);

        var work = HydraMergeSpec.WorkAt(f, lead, p, p, p.Path);

        // A head with no landing counts too; it just brings no files.
        Assert.Equal([clean.Id, none.Id, late.Id], HydraMergeSpec.HeadIDsOf(work));
        Assert.Equal(["d-clean.txt", "f-late.txt"], HydraMergeSpec.PathsOf(work));
        Assert.Empty(HydraMergeSpec.TurnIDsOf(work));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-069")] // AppModel+HydraMerge.swift:484-500; ED-138
    public void A_heads_paths_are_anchored_to_the_checkout_of_the_project_it_worked_in()
    {
        using var f = new HydraMergeProofFixture();
        var lp = f.AddProject("Lead project", origin: false);
        var op = f.AddProject("Other project", origin: false);
        var gone = AppModelSamples.Project("Gone", HydraMergeSpec.Foreign + @"\gone-project"); // not in the sidebar any more
        var copy = HydraMergeSpec.Foreign + @"\head-copy";
        var lead = f.AddLead(lp, "Lead", []);
        f.AddHead(lead, ["src/one.txt"], index: 1);
        f.AddHead(lead, ["two.txt"], index: 2, project: op);
        f.AddHead(lead, ["three.txt"], index: 3, project: gone);
        var copied = f.AddHead(lead, [], index: 4, copy: copy);
        var tooled = f.AddHead(lead, [], index: 5);
        f.Tool(copied, copy + @"\copied.txt");
        var unc = @"\\server\share\u.txt";
        var absolute = HydraMergeSpec.Foreign + @"\abs\a.txt";
        f.Tool(tooled, " trimmed.txt ", "", "   ", absolute, unc, "https://host.example/x.txt", "~/home.txt", @"\rooted.txt");
        foreach (var name in new[] { "src/one.txt", "three.txt", "copied.txt", "trimmed.txt", "rooted.txt" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(lp.Path, name))!);
            File.WriteAllText(Path.Combine(lp.Path, name), name + "\n");
        }
        File.WriteAllText(Path.Combine(op.Path, "two.txt"), "two\n");
        // Files of the same names in the other project: only a wrong anchoring would pick them up in its pass.
        foreach (var name in new[] { "src/one.txt", "three.txt", "copied.txt", "trimmed.txt" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(op.Path, name))!);
            File.WriteAllText(Path.Combine(op.Path, name), name + " in the other project\n");
        }
        f.Start(lead);

        var own = HydraMergeSpec.WorkAt(f, lead, lp, lp, lp.Path);
        var other = HydraMergeSpec.WorkAt(f, lead, op, lp, lp.Path);

        // Trimmed; relative paths go onto the lead's checkout for a head of its project and for a head whose project is gone;
        // a path in the head's own copy stands for the same path in that checkout; a bare leading backslash is joined on too.
        Assert.Equal(["copied.txt", "rooted.txt", "src/one.txt", "three.txt", "trimmed.txt"], HydraMergeSpec.PathsOf(own));
        // Absolute paths (a drive letter, a UNC share, a URL, a home path) are left exactly as written; the other project's head
        // is anchored in that project.
        Assert.Equal(new[] { absolute, unc, "https://host.example/x.txt", "~/home.txt", Path.Combine(op.Path, "two.txt") }.ToHashSet(),
            HydraMergeSpec.OutsideOf(own).ToHashSet());
        // The other project's pass finds its head's file there, and nothing the lead's checkout holds.
        Assert.Equal(["two.txt"], HydraMergeSpec.PathsOf(other));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-070")] // AppModel+HydraMerge.swift:501-508
    public void A_heads_landing_files_count_both_as_its_own_files_and_as_reported_paths()
    {
        using var f = new HydraMergeProofFixture();
        var lp = f.AddProject("Lead project", origin: false);
        var op = f.AddProject("Other project", origin: false);
        var lead = f.AddLead(lp, "Lead", []);
        var otherFile = Path.Combine(op.Path, "o.txt");
        f.AddHead(lead, ["z.txt", "a.txt", "z.txt", otherFile], index: 1);
        foreach (var name in new[] { "z.txt", "a.txt" }) File.WriteAllText(Path.Combine(lp.Path, name), name + "\n");
        File.WriteAllText(otherFile, "o\n");
        f.Start(lead);

        var own = HydraMergeSpec.WorkAt(f, lead, lp, lp, lp.Path);
        var other = HydraMergeSpec.WorkAt(f, lead, op, lp, lp.Path);

        // Inside this pass's checkout, in first-seen order and without repeats; the path in the other project is only reported.
        Assert.Equal(["a.txt", "z.txt"], HydraMergeSpec.PathsOf(own));
        Assert.Equal(["z.txt", "a.txt"], Assert.Single(HydraMergeSpec.HeadsOf(own)).Files);
        Assert.Equal([otherFile], HydraMergeSpec.OutsideOf(own));
        // In the other project's pass it is the other way round.
        Assert.Equal(["o.txt"], HydraMergeSpec.PathsOf(other));
        Assert.Equal(["o.txt"], Assert.Single(HydraMergeSpec.HeadsOf(other)).Files);
        Assert.Equal(2, HydraMergeSpec.OutsideOf(other).Length);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-071")] // AppModel+HydraMerge.swift:509-524
    public void A_heads_timeline_gives_its_tool_edits_read_live_or_decoded_cold_and_a_cold_head_stays_cold()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", []);
        // Native heads work in the lead's own checkout and land nothing: their timelines are the only source of their files.
        var cold = Native(f, f.AddHead(lead, [], index: 1, task: "cold"));
        var live = Native(f, f.AddHead(lead, [], index: 2, task: "live"));
        var missing = Native(f, f.AddHead(lead, [], index: 3, task: "missing document"));
        HydraMergeSpec.AddTool(f, cold, null, "c1.txt", "c2.txt");
        HydraMergeSpec.AddTool(f, cold, null);
        HydraMergeSpec.AddAssistant(f, cold, Guid.NewGuid(), "ignored.txt");
        HydraMergeSpec.AddTool(f, live, null, "l1.txt");
        f.Documents.Remove(missing.Id);
        foreach (var name in new[] { "c1.txt", "c2.txt", "l1.txt", "ignored.txt" }) File.WriteAllText(Path.Combine(p.Path, name), name + "\n");
        f.Start(lead, live);
        // The live head is read from its entries: its document is gone from the disk by now.
        File.Delete(Storage.ThreadURL(live.Id));

        var work = HydraMergeSpec.WorkAt(f, lead, p, p, p.Path);

        Assert.Equal(["c1.txt", "c2.txt", "l1.txt"], HydraMergeSpec.PathsOf(work));
        Assert.Equal(["cold: c1.txt,c2.txt", "live: l1.txt", "missing document: "],
            HydraMergeSpec.HeadsOf(work).Select(head => head.Task + ": " + string.Join(',', head.Files)));
        // A cold head was decoded off to the side and never brought back as a runtime; a missing document is no files, no error.
        Assert.Null(f.Model.ExistingRuntime(cold.Id));
        Assert.Null(f.Model.ExistingRuntime(missing.Id));
        Assert.NotNull(f.Model.ExistingRuntime(live.Id));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-072")] // AppModel+HydraMerge.swift:525; Hydra.swift:259-265
    public void A_heads_note_entry_has_the_roster_name_wrapped_with_a_round_number_the_raw_index_and_a_120_character_task()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", []);
        string[] expected = ["Hank", "Gus", "Hank 2", "Walter 2", "Walter 3"];
        long[] indexes = [0, 24, 25, 26, 51];
        for (var i = 0; i < indexes.Length; i++) f.AddHead(lead, [], index: indexes[i], task: i == 0 ? new string('q', 130) : i == 1 ? "  first line \nsecond line" : "task " + i);
        f.Start(lead);

        var heads = HydraMergeSpec.HeadsOf(HydraMergeSpec.WorkAt(f, lead, p, p, p.Path));

        Assert.Equal(expected, heads.Select(head => head.Name));
        Assert.Equal(indexes, heads.Select(head => head.Index));
        Assert.Equal([new string('q', 119) + "\u2026", "first line", "task 2", "task 3", "task 4"], heads.Select(head => head.Task));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-073")] // AppModel+HydraMerge.swift:531-543; ED-138
    public void The_leads_own_tool_edits_of_its_unmerged_turns_are_anchored_to_its_own_checkout()
    {
        using var f = new HydraMergeProofFixture();
        var lp = f.AddProject("Lead project", origin: false);
        var op = f.AddProject("Other project", origin: false);
        var lead = f.AddLead(lp, "Lead", []);
        var turn = f.Documents[lead.Id].Turns[0];
        var spent = turn with { Id = Guid.NewGuid(), Index = 0, HydraMerged = true };
        f.Documents[lead.Id] = f.Documents[lead.Id] with { Turns = new ValueList<TurnRecord>([spent, turn]) };
        var absolute = Path.Combine(lp.Path, "abs.txt");
        var elsewhere = Path.Combine(op.Path, "o.txt");
        HydraMergeSpec.AddTool(f, lead, spent.Id, "old.txt");
        HydraMergeSpec.AddTool(f, lead, turn.Id, " rel.txt ", "", "   ", absolute, "https://host.example/x.txt", "~/home.txt", elsewhere);
        HydraMergeSpec.AddTool(f, lead, null, "no-turn.txt");
        foreach (var name in new[] { "old.txt", "rel.txt", "abs.txt", "no-turn.txt" }) File.WriteAllText(Path.Combine(lp.Path, name), name + "\n");
        File.WriteAllText(Path.Combine(op.Path, "o.txt"), "o\n");
        File.WriteAllText(Path.Combine(op.Path, "rel.txt"), "same name in the other project\n");
        f.Start(lead);

        var own = HydraMergeSpec.WorkAt(f, lead, lp, lp, lp.Path);
        var other = HydraMergeSpec.WorkAt(f, lead, op, lp, lp.Path);

        // Only entries of the unmerged turns; trimmed; absolute (a drive, "~" or "://") as written, the rest on the lead's checkout.
        Assert.Equal(["abs.txt", "rel.txt"], HydraMergeSpec.PathsOf(own));
        Assert.Equal(new[] { "https://host.example/x.txt", "~/home.txt", elsewhere }.ToHashSet(), HydraMergeSpec.OutsideOf(own).ToHashSet());
        // In the other project's pass a relative edit is still the lead's checkout's file, not that project's.
        Assert.Equal(["o.txt"], HydraMergeSpec.PathsOf(other));
        Assert.Contains(Path.Combine(lp.Path, "rel.txt"), HydraMergeSpec.OutsideOf(other));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-076")] // AppModel+HydraMerge.swift:565-576
    public void The_leads_own_checkout_is_swept_for_what_changed_since_the_first_unmerged_turns_checkpoint()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var repo = f.Repos[p.Id];
        HydraMergeSpec.Commit(repo, ("b.txt", "b\n"), ("claimed.txt", "c\n"), ("nb.txt", "n\n"), ("gone.txt", "g\n"));
        var low = f.AddLead(p, "Low", [], new Guid("00000000-0000-0000-0000-000000000001"));
        // A sibling chat that has not finished claims claimed.txt and holds this job; a finished later job's head claims nb.txt
        // without holding it (the ordering settles that one): both stay out of the sweep.
        var sibling = f.AddLead(p, "Sibling", ["claimed.txt"], status: TurnStatus.Interrupted, hydra: false);
        var high = f.AddLead(p, "High", [], new Guid("00000000-0000-0000-0000-000000000002"));
        f.AddHead(high, ["nb.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "dirty before the checkpoint\n");
        var checkpoint = HydraMergeSpec.Checkpoint(p.Path);
        var turn = f.Documents[low.Id].Turns[0];
        // The first unmerged turn's checkpoint is the one used; a later turn's (here one that cannot be read) is not.
        var later = turn with { Id = Guid.NewGuid(), Index = 2, BaseCheckpoint = "0123456789012345678901234567890123456789" };
        f.Documents[low.Id] = f.Documents[low.Id] with { Turns = new ValueList<TurnRecord>([turn with { BaseCheckpoint = checkpoint }, later]) };
        // After the checkpoint: a file written through a shell, an edit, a deletion, two claimed files, and a second edit of the file
        // that was already dirty.
        File.WriteAllText(Path.Combine(p.Path, "shell.txt"), "written by a shell\n");
        File.WriteAllText(Path.Combine(p.Path, "b.txt"), "b changed\n");
        File.Delete(Path.Combine(p.Path, "gone.txt"));
        File.WriteAllText(Path.Combine(p.Path, "claimed.txt"), "changed\n");
        File.WriteAllText(Path.Combine(p.Path, "nb.txt"), "changed\n");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "dirty again\n");
        f.Start(low, high, sibling);

        var swept = HydraMergeSpec.PathsOf(HydraMergeSpec.WorkAt(f, low, p, p, p.Path, sweep: true));
        var notSwept = HydraMergeSpec.PathsOf(HydraMergeSpec.WorkAt(f, low, p, p, p.Path, sweep: false));

        // changed since the checkpoint, less what was already dirty then, less every path another chat reserves.
        Assert.Equal(["b.txt", "gone.txt", "shell.txt"], swept);
        Assert.Empty(notSwept);
    }

    [Theory]
    [InlineData("no-checkpoint")]
    [InlineData("unreadable-checkpoint")]
    [Trait("Req", "PR-HYDRAMERGE-076")] // AppModel+HydraMerge.swift:568-572 (no checkpoint, or a failed read, skips the sweep silently)
    public void Without_a_usable_checkpoint_the_sweep_is_skipped_silently(string kind)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", [], checkpoint: kind == "no-checkpoint" ? null : "0123456789012345678901234567890123456789");
        File.WriteAllText(Path.Combine(p.Path, "shell.txt"), "written by a shell\n");
        f.Start(lead);

        var work = HydraMergeSpec.WorkAt(f, lead, p, p, p.Path, sweep: true);

        Assert.Empty(HydraMergeSpec.PathsOf(work));
        Assert.Equal([f.Documents[lead.Id].Turns[0].Id], HydraMergeSpec.TurnIDsOf(work));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-076")] // AppModel+HydraMerge.swift:136-138 (sweepsCheckout: false for every other project)
    [Trait("Rule", "R-TEST-4")] // PR-HYDRAMERGE-017, the sweep-off clause only: App/AppModel+HydraMerge.swift:137 passes sweepsCheckout: false for every other project; the other clauses are in AppModelHydraMergePart1Tests.
    public void Other_projects_passes_never_sweep_their_checkout()
    {
        using var f = new HydraMergeProofFixture();
        var lp = f.AddProject("Lead project", origin: false);
        var op = f.AddProject("Other project", origin: false);
        var checkpoint = HydraMergeSpec.Checkpoint(lp.Path);
        var lead = f.AddLead(lp, "Lead", ["gone.txt"], checkpoint: checkpoint);
        File.WriteAllText(Path.Combine(op.Path, "o.txt"), "o\n");
        f.Tool(lead, Path.Combine(op.Path, "o.txt"));
        f.Start(lead);
        var calls = new List<ShellInvocation>();

        f.Run(lead, calls.Add);

        // The first turn's checkpoint was read in the lead's own checkout, and never in the other project's.
        bool ReadsCheckpoint(ShellInvocation call, string directory) => call.Directory == directory && call.Arguments.Contains(checkpoint);
        Assert.Contains(calls, call => ReadsCheckpoint(call, lp.Path));
        Assert.DoesNotContain(calls, call => ReadsCheckpoint(call, op.Path));
        Assert.Contains(calls, call => call.Directory == op.Path);
    }
}
