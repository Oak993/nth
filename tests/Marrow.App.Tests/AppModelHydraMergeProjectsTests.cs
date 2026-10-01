using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :125-139, :166-187, :221-225 (which projects a run
// visits, how they are named, and what a run books when no project failed). Real git in disposable repositories.
public sealed class AppModelHydraMergeProjectsTests
{
    private static readonly string[] Words =
    [
        "Writing the commit", "Writing the commit message", "Pushing the branch", "Opening the merge request", "Merging", "Bringing the checkout up to date",
    ];

    [Theory]
    [InlineData("inner", "Inner")]
    [InlineData("outer", "Outer")]
    [InlineData("tie", "TwinA")]
    [InlineData("nowhere", null)]
    [InlineData("own-project-from-copy", "none")]
    [InlineData("nested-own", "Outer")]
    [Trait("Req", "PR-HYDRAMERGE-016")] // AppModel+HydraMerge.swift:128-135
    public void An_outside_path_is_assigned_to_the_sidebar_project_with_the_longest_path_that_holds_it(string where, string? expected)
    {
        using var f = new HydraMergeProofFixture();
        var outer = HydraMergeSpec.AddRepositoryProject(f, "Outer", f.H.NewFolder("outer-root"));
        var inner = HydraMergeSpec.AddRepositoryProject(f, "Inner", Path.Combine(outer.Path, "inner"));
        // The lead's own project may itself lie inside another sidebar project's folder.
        var copy = where is "own-project-from-copy" or "nested-own";
        var own = where == "nested-own"
            ? HydraMergeSpec.AddRepositoryProject(f, "Own", Path.Combine(outer.Path, "own"), origin: true)
            : f.AddProject("Own", origin: copy);
        // Two sidebar entries for one folder tie on path length: max(by:) keeps the first of them (Swift's Sequence.max(by:)
        // replaces its result only for a strictly greater element), where the record says the last.
        var twin = HydraMergeSpec.AddRepositoryProject(f, "TwinA", f.H.NewFolder("twin"));
        HydraMergeSpec.AddFakeProject(f, "TwinB", twin.Path);
        ChatThread lead;
        var edited = where switch
        {
            "tie" => Path.Combine(twin.Path, "t.txt"),
            "inner" => Path.Combine(inner.Path, "x.txt"),
            "outer" => Path.Combine(outer.Path, "y.txt"),
            "own-project-from-copy" or "nested-own" => Path.Combine(own.Path, "z.txt"),
            _ => HydraMergeSpec.Foreign + @"\nowhere\f.txt",
        };
        if (copy)
        {
            // The lead works in a copy: its project's own folder is outside the copy but is the lead's own project, never a holder.
            lead = HydraMergeSpec.AddWorktreeLead(f, own, "Lead", ["gone.txt"]).Lead;
            File.WriteAllText(edited, "z\n");
        }
        else
        {
            lead = f.AddLead(own, "Lead", ["gone.txt"]);
            if (where != "nowhere") File.WriteAllText(edited, "x\n");
        }
        f.Tool(lead, edited);
        f.Start(lead);

        f.Run(lead);

        var records = HydraMergeSpec.Records(f, lead);
        if (expected == "none") Assert.Empty(records);
        else if (expected is null) Assert.Equal(HydraMergeRecord.Outcome.Stray, Assert.Single(records).HydraMergeRecordOutcome);
        else
        {
            // Only the holder with the longest path is visited: its checkout has no origin to push to.
            var record = Assert.Single(records);
            Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
            Assert.Equal(expected, record.Project);
            Assert.StartsWith("Hydra did not merge: no remote.", record.Detail, StringComparison.Ordinal);
        }
    }

    // One lead in "Own" that touched a.txt, and, when asked, a head of it that worked in a second project ("Second") on b.txt.
    private static (ChatThread Lead, Project Own, Project? Second) Team(HydraMergeProofFixture f, bool second, bool secondOrigin = true)
    {
        var own = f.AddProject("Own");
        var other = second ? f.AddProject("Second", origin: secondOrigin) : null;
        var lead = f.AddLead(own, "Lead", ["a.txt"]);
        f.AddHead(lead, ["a.txt"], index: 1, task: "Own part");
        File.WriteAllText(Path.Combine(own.Path, "a.txt"), "team edit\n");
        if (other is not null)
        {
            f.AddHead(lead, ["b.txt"], index: 2, task: "Second part", project: other);
            File.WriteAllText(Path.Combine(other.Path, "b.txt"), "b\n");
        }
        return (lead, own, other);
    }

    // The words the pill carried the first time a call of this kind ran in the project's checkout.
    private static (string? Stage, HydraMergeStage? Step) At(List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)> trail, Project project, string verb) =>
        trail.Where(entry => entry.Call.Directory == project.Path && HydraMergeSpec.Verb(entry.Call) == verb).Select(entry => (entry.Stage, entry.Step)).First();

    [Theory]
    [InlineData("single")]
    [InlineData("two-ok")]
    [InlineData("two-second-fails")]
    [Trait("Req", "PR-HYDRAMERGE-023")] // AppModel+HydraMerge.swift:166, :222-225, :227, :403
    public void Several_projects_in_one_run_prefix_every_stage_note_title_and_record_detail_with_the_project_name(string run)
    {
        using var f = new HydraMergeProofFixture();
        var (lead, own, second) = Team(f, run != "single", run != "two-second-fails");
        f.Start(lead);
        HydraMergeSpec.Forge(own.Path);
        if (second is not null && run == "two-ok") HydraMergeSpec.Forge(second.Path);
        var trail = new List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)>();

        f.Run(lead, HydraMergeSpec.Trail(f, lead, trail));

        var several = run != "single";
        string Prefix(Project project) => several ? project.Name + ": " : "";
        // The first stage is set before any project is known and is never prefixed.
        Assert.Equal("Gathering the team's files", trail[0].Stage);
        var ownAt = new[] { At(trail, own, "fetch"), At(trail, own, "commit-tree"), At(trail, own, "push"), At(trail, own, "tea pulls create") };
        Assert.Equal([Prefix(own) + Words[0], Prefix(own) + Words[1], Prefix(own) + Words[2], Prefix(own) + Words[3]], ownAt.Select(at => at.Stage));
        var notes = HydraMergeSpec.NoteEntries(f, lead);
        var merged = notes.First(note => HydraMergeSpec.Title(note).Contains("merged", StringComparison.Ordinal)); // the own project goes first
        Assert.Equal(Prefix(own) + "Hydra merged #7", HydraMergeSpec.Title(merged));
        var records = HydraMergeSpec.Records(f, lead);
        Assert.Equal(HydraMergeRecord.Outcome.Merged, records[0].HydraMergeRecordOutcome);
        if (run == "two-ok")
        {
            Assert.Equal(Prefix(second!) + Words[2], At(trail, second!, "push").Stage);
            Assert.Equal(Prefix(second!) + Words[5], trail.Last(entry => entry.Call.Directory == second!.Path && HydraMergeSpec.Verb(entry.Call) == "fetch").Stage);
            Assert.Equal(["Own: Hydra merged #7", "Second: Hydra merged #7"], notes.Select(HydraMergeSpec.Title));
        }
        if (run == "two-second-fails")
        {
            // The record's detail and the note's title both carry the second project's name.
            var failed = records[1];
            Assert.Equal("Second", failed.Project);
            Assert.Equal("Second: Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", failed.Detail);
            Assert.Equal("Second: Hydra did not merge: no remote.\nThe team's work is in the checkout; there is no origin to push it to.", notes[^1].Text);
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-025")] // AppModel+HydraMerge.swift:177-179
    public void A_run_with_no_failure_books_every_groups_turns_and_heads_under_one_timestamp()
    {
        using var f = new HydraMergeProofFixture();
        var (lead, own, second) = Team(f, second: true);
        f.Start(lead);
        HydraMergeSpec.Forge(own.Path);
        HydraMergeSpec.Forge(second!.Path);
        var before = f.H.Main.Time.GetUtcNow();

        f.Run(lead);

        Assert.Equal([HydraMergeRecord.Outcome.Merged, HydraMergeRecord.Outcome.Merged], HydraMergeSpec.Records(f, lead).Select(r => r.HydraMergeRecordOutcome));
        Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).Turns.Single().HydraMerged));
        var heads = f.Model.HydraTeam(lead.Id);
        Assert.Equal(2, heads.Count);
        Assert.All(heads, head => Assert.Equal(f.H.Main.Time.GetUtcNow(), head.Hydra!.MergedAt));
        Assert.Single(heads.Select(head => head.Hydra!.MergedAt).Distinct());
        Assert.Equal(before, f.H.Main.Time.GetUtcNow());
    }
}
