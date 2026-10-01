using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :143-165, :176-187, :189-210 (what lies under no project).
public sealed class AppModelHydraMergeStrayTests
{
    [Theory]
    [InlineData("scratch", false)]
    [InlineData("own-project", false)]
    [InlineData("other-project", false)]
    [InlineData("foreign", true)]
    [Trait("Req", "PR-HYDRAMERGE-018")] // AppModel+HydraMerge.swift:143, :194-209
    public void A_path_is_stray_only_when_it_is_no_scratch_location_and_no_sidebar_project_holds_it(string kind, bool stray)
    {
        using var f = new HydraMergeProofFixture();
        var real = f.AddProject("Real");
        var leadProject = real;
        string outside;
        switch (kind)
        {
            case "scratch": // :197 the temporary folder is nobody's project
                outside = Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "hydramerge-spec-throwaway.txt");
                break;
            case "own-project":
                // The lead works in a copy of a project whose own folder is a sidebar project too (:127-130 tests the
                // full projects list, the lead's own project included, not only the other ones).
                leadProject = HydraMergeSpec.AddFakeProject(f, "Own", HydraMergeSpec.Foreign + @"\own-project");
                outside = HydraMergeSpec.Foreign + @"\own-project\notes.txt";
                break;
            case "other-project":
                HydraMergeSpec.AddFakeProject(f, "Other", HydraMergeSpec.Foreign + @"\other");
                outside = HydraMergeSpec.Foreign + @"\other\x.txt";
                break;
            default:
                outside = HydraMergeSpec.Foreign + @"\elsewhere\x.txt";
                break;
        }
        var lead = f.AddLead(leadProject, "Lead", ["gone.txt"]);
        if (kind == "own-project") lead = HydraMergeSpec.Change(f, lead, t => t with { WorktreePath = real.Path });
        f.Tool(lead, outside);
        f.Start(lead);

        f.Run(lead);

        var records = HydraMergeSpec.Records(f, lead);
        if (stray)
        {
            var record = Assert.Single(records);
            Assert.Equal(HydraMergeRecord.Outcome.Stray, record.HydraMergeRecordOutcome);
            Assert.Equal(1, record.Files);
        }
        else
        {
            Assert.Empty(records);
            Assert.Empty(HydraMergeSpec.NoteEntries(f, lead));
        }
        // Nothing stray: the turns are spent (:150-160). With a stray the work is offered again (:162-164).
        Assert.Equal(!stray, f.H.Main.OnMain(() => f.Runtime(lead).Turns.Single().HydraMerged));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(12)]
    [Trait("Req", "PR-HYDRAMERGE-020")] // AppModel+HydraMerge.swift:144-147
    public void The_stray_body_names_at_most_six_paths_in_backticks_and_says_how_many_more(int count)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["gone.txt"]);
        var paths = HydraMergeSpec.Outside(count);
        f.Tool(lead, paths.Reverse().ToArray()); // the merge sorts what it found, so the first six are the six smallest
        f.Start(lead);

        f.Run(lead);

        var expected = HydraMergeSpec.StrayBody(paths);
        Assert.Equal(count == 1
            ? "1 file was changed outside every project: `" + paths[0] + "`. Add the folder that holds them to the sidebar and ask for the merge again."
            : count + " files were changed outside every project: " + string.Join(", ", paths.Take(6).Select(path => "`" + path + "`")) +
              (count > 6 ? " and " + (count - 6) + " more" : "") + ". Add the folder that holds them to the sidebar and ask for the merge again.", expected);
        Assert.Equal(expected, Assert.Single(HydraMergeSpec.Records(f, lead)).Detail);
        Assert.Equal("Hydra did not merge: the team's files are outside every project.\n" + expected, Assert.Single(HydraMergeSpec.NoteEntries(f, lead)).Text);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-022")] // AppModel+HydraMerge.swift:147-165
    public void With_no_group_but_strays_a_stray_record_and_note_are_made_and_nothing_is_marked_merged()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["gone.txt"]);
        var head = f.AddHead(lead, ["gone.txt"]);
        var paths = HydraMergeSpec.Outside(8);
        f.Tool(lead, paths);
        f.Start(lead);
        string? pill = null;

        f.Run(lead, call => pill ??= f.Runtime(lead).HydraMergeNoteID);

        var body = HydraMergeSpec.StrayBody(paths);
        var record = Assert.Single(HydraMergeSpec.Records(f, lead));
        Assert.Equal(HydraMergeRecord.Outcome.Stray, record.HydraMergeRecordOutcome);
        Assert.Null(record.Project);
        Assert.Null(record.Label);
        Assert.Null(record.Url);
        Assert.Equal(8, record.Files); // the stray count, not the six that are named
        Assert.Equal(body, record.Detail);
        Assert.Equal(f.H.Main.Time.GetUtcNow(), record.At);
        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        Assert.Equal("Hydra did not merge: the team's files are outside every project.\n" + body, note.Text);
        Assert.Equal(pill, note.ID);
        // The work is offered again: neither the turn nor the head is booked.
        Assert.False(f.H.Main.OnMain(() => f.Runtime(lead).Turns.Single().HydraMerged));
        Assert.Null(f.Model.Thread(head.Id)!.Hydra!.MergedAt);
    }

    [Theory]
    [InlineData(1, "Hydra left a file outside every project.")]
    [InlineData(3, "Hydra left 3 files outside every project.")]
    [Trait("Req", "PR-HYDRAMERGE-026")] // AppModel+HydraMerge.swift:176-183
    public void A_successful_run_with_strays_posts_a_second_note_with_a_row_of_its_own(int count, string title)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var head = f.AddHead(lead, ["a.txt"]);
        var paths = HydraMergeSpec.Outside(count);
        f.Tool(lead, paths);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        f.Forge(p);
        string? pill = null;

        f.Run(lead, call => pill ??= f.Runtime(lead).HydraMergeNoteID);

        var records = HydraMergeSpec.Records(f, lead);
        Assert.Equal([HydraMergeRecord.Outcome.Merged, HydraMergeRecord.Outcome.Stray], records.Select(r => r.HydraMergeRecordOutcome));
        var stray = records[1];
        Assert.Null(stray.Project);
        Assert.Null(stray.Label);
        Assert.Null(stray.Url);
        Assert.Equal(count, stray.Files);
        Assert.Equal(HydraMergeSpec.StrayBody(paths), stray.Detail);
        var notes = HydraMergeSpec.NoteEntries(f, lead);
        Assert.Equal(2, notes.Length);
        Assert.StartsWith("Hydra merged #7\n", notes[0].Text, StringComparison.Ordinal);
        Assert.Equal(title + "\n" + HydraMergeSpec.StrayBody(paths), notes[1].Text);
        // The first note spent the pill's id, so the second is a row of its own.
        Assert.Equal(pill, notes[0].ID);
        Assert.NotEqual(pill, notes[1].ID);
        // The projects' shares went out: the work is booked even though strays stayed behind (:177-179).
        Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).Turns.Single().HydraMerged));
        Assert.NotNull(f.Model.Thread(head.Id)!.Hydra!.MergedAt);
        Assert.DoesNotContain(records, r => r.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Settled);
    }
}
