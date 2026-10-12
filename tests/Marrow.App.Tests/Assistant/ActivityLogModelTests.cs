using System.Text.Json.Nodes;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Activity view's state (DESIGN.md 12.5 and 3.3; unit AS-21, ED-NEW-dextra-as21-6). The chain is checked when
// the log opens; a filter, a mode, a fold or a selection changes only what it should and tells once; a slow answer that a newer
// change overtook is dropped; the first failing line is brought into sight whatever hides it; an export writes the lines that
// pass the filter. Work runs inline, so every call has finished when it returns. ActivityDataset's six actions are the data;
// the clock stands at 2026-10-13 12:00 UTC.
[Trait("Decision", "ED-NEW-dextra-as21-6")]
[Trait("Feature", "AS-21")]
public sealed class ActivityLogModelTests : IDisposable
{
    private readonly ActivityLogRig rig = ActivityDataset.Make();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => rig.Dispose();

    private ActivityLogModel Make(IActivityWork? work = null, ActivityLogSource? source = null, ActivityLabels? labels = null) =>
        new(source ?? rig.Source, work ?? new InlineActivityWork(), clock, labels);

    private static string Shape(IEnumerable<ActivityListItem> items) => string.Join(" | ", items.Select(i => i.Kind switch
    {
        ActivityListItemKind.DayHeader => $"day {i.Day:yyyy-MM-dd}",
        ActivityListItemKind.MissionHeader => $"mission '{i.Id}'",
        ActivityListItemKind.AgentHeader => $"agent '{i.Id}'",
        _ => $"#{i.Row!.Sequence}",
    }));

    [Fact]
    public async Task Opening_checks_the_chain_then_lists_the_log_newest_first()
    {
        using var model = Make();
        var phases = new List<(ActivityChange Change, ActivityLoadPhase Phase)>();
        model.Changed += change => phases.Add((change, model.Phase));
        Assert.Equal(ActivityLoadPhase.Idle, model.Phase);

        await model.OpenAsync();

        Assert.Equal((ActivityChange.Phase, ActivityLoadPhase.Checking), phases[0]); // the check is announced before it runs
        Assert.Equal(ActivityLoadPhase.Ready, model.Phase);
        Assert.Equal(AuditVerificationStatus.Verified, model.Verification!.Status);
        Assert.Equal(6, model.MatchCount);
        Assert.Equal("day 2026-10-12 | #6 | #5 | #4 | #3 | day 2026-10-11 | #2 | #1", Shape(model.Items));
        Assert.Contains(phases, p => (p.Change & ActivityChange.Items) != 0 && p.Phase == ActivityLoadPhase.Ready);
    }

    [Fact]
    public async Task A_log_that_cannot_be_read_says_why_and_lists_nothing()
    {
        using var model = Make(source: rig.Source with { PrintedDayFinals = () => throw new IOException("The disk went away.") });

        await model.OpenAsync();

        Assert.Equal(ActivityLoadPhase.Unavailable, model.Phase);
        Assert.Equal("The disk went away.", model.Failure);
        Assert.Null(model.Snapshot);
        Assert.Empty(model.Items);
    }

    [Fact]
    public async Task A_filter_lists_what_passes_and_the_same_filter_again_changes_nothing()
    {
        using var model = Make();
        await model.OpenAsync();
        var changes = new List<ActivityChange>();
        model.Changed += changes.Add;

        await model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" });

        Assert.Equal(4, model.MatchCount);
        Assert.Equal("day 2026-10-12 | #6 | #5 | day 2026-10-11 | #2 | #1", Shape(model.Items));
        Assert.Equal([ActivityChange.Filter, ActivityChange.Items], changes);
        changes.Clear();
        await model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" });
        Assert.Empty(changes);

        await model.UpdateFilterAsync(f => f with { Class = "Run" });
        Assert.Equal(2, model.MatchCount);
        await model.ClearFilterAsync();
        Assert.Equal(6, model.MatchCount);
        Assert.True(model.Filter.IsEmpty);
    }

    [Fact]
    public async Task The_lineage_lists_missions_and_a_fold_hides_what_is_under_it()
    {
        using var model = Make();
        await model.OpenAsync();

        await model.SetModeAsync(ActivityViewMode.Lineage);
        Assert.Equal("mission 'm-3' | agent 't-1' | #6 | mission 'm-2' | agent 't-3' | #3 | #4 | mission 'm-1' | agent 't-1' | #1 | agent 't-2' | #2 | mission '' | agent '' | #5", Shape(model.Items));

        await model.ToggleFoldAsync(ActivityListBuilder.MissionKey("m-2"));
        Assert.Equal(12, model.Items.Count);
        Assert.Contains(ActivityListBuilder.MissionKey("m-2"), model.Folded);
        await model.ToggleFoldAsync(ActivityListBuilder.MissionKey("m-2"));
        Assert.Equal(15, model.Items.Count);
        Assert.Empty(model.Folded);

        await model.SetModeAsync(ActivityViewMode.Log);
        Assert.Equal(8, model.Items.Count);
    }

    [Fact]
    public async Task A_missions_lineage_is_one_call_with_nothing_folded()
    {
        using var model = Make();
        await model.OpenAsync();
        await model.SetModeAsync(ActivityViewMode.Lineage);
        await model.ToggleFoldAsync(ActivityListBuilder.MissionKey("m-1"));

        await model.ShowMissionAsync("m-1");

        Assert.Equal(ActivityViewMode.Lineage, model.Mode);
        Assert.Equal("m-1", model.Filter.Mission);
        Assert.Empty(model.Folded);
        Assert.Equal(2, model.MatchCount);
        Assert.Equal("mission 'm-1' | agent 't-1' | #1 | agent 't-2' | #2", Shape(model.Items));
    }

    [Fact]
    public async Task A_selection_survives_a_fold_and_is_let_go_by_a_filter_that_hides_it()
    {
        using var model = Make();
        await model.OpenAsync();
        var row = model.Snapshot!.Rows[2]; // line 3, in mission m-2
        var changes = new List<ActivityChange>();
        model.Changed += changes.Add;

        model.Select(row);
        model.Select(row); // the same line again says nothing

        Assert.Same(row, model.Selected);
        Assert.Equal([ActivityChange.Selection], changes);
        await model.SetModeAsync(ActivityViewMode.Lineage);
        await model.ToggleFoldAsync(ActivityListBuilder.MissionKey("m-2"));
        Assert.Equal(-1, model.IndexOf(row)); // folded away, but still the selection
        Assert.Same(row, model.Selected);
        changes.Clear();

        await model.SetFilterAsync(ActivityFilter.ForMission("m-1"));

        Assert.Null(model.Selected);
        Assert.Contains(changes, c => (c & ActivityChange.Selection) != 0);
    }

    [Fact]
    public async Task The_first_failing_line_is_brought_into_sight_whatever_hides_it()
    {
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines[2] = lines[2].Replace("duty armed", "duty x", StringComparison.Ordinal); // line 5 of the log, in no mission
        rig.Rewrite("2026-10-12", lines);
        using var model = Make();
        await model.OpenAsync();
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 3, 5), model.Verification!.FirstFailure);
        await model.SetModeAsync(ActivityViewMode.Lineage);
        await model.SetFilterAsync(new ActivityFilter { Assistant = "astur-shop" }); // hides it
        await model.ToggleFoldAsync(ActivityListBuilder.MissionKey("")); // and so does this fold
        ActivityRow? revealed = null;
        model.RevealRequested += row => revealed = row;

        var shown = await model.ShowFailureAsync();

        Assert.True(shown);
        Assert.True(model.Filter.IsEmpty);
        Assert.Empty(model.Folded);
        Assert.Equal(5, model.Selected!.Sequence);
        Assert.Same(model.Selected, revealed);
        Assert.True(model.IndexOf(model.Selected) >= 0);
        Assert.Equal(ActivityTrust.NotVerified, model.Selected.Trust);
    }

    [Fact]
    public async Task A_failure_that_names_a_day_filters_to_that_day()
    {
        var printed = new Dictionary<DateOnly, string> { [new DateOnly(2026, 10, 12)] = new string('a', 64) };
        using var model = Make(source: rig.Source with { PrintedDayFinals = () => printed });
        await model.OpenAsync();
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 0, null), model.Verification!.FirstFailure);

        Assert.True(await model.ShowFailureAsync());

        Assert.Equal(new ActivityFilter { From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 12) }, model.Filter);
        Assert.Equal(4, model.MatchCount);
    }

    [Fact]
    public async Task There_is_nothing_to_show_when_the_log_verifies_or_names_no_line()
    {
        using (var verified = Make())
        {
            await verified.OpenAsync();
            Assert.False(await verified.ShowFailureAsync());
            Assert.Null(verified.Selected);
        }
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines.RemoveAt(3); // the end of the log is gone: the anchor names a line that is not there
        rig.Rewrite("2026-10-12", lines);
        using var cut = Make();
        await cut.OpenAsync();
        Assert.Null(cut.Verification!.FirstFailure);
        Assert.False(await cut.ShowFailureAsync());
        Assert.False(await Make().ShowFailureAsync()); // not opened yet
    }

    [Fact]
    public async Task A_refresh_reads_and_checks_the_log_again_and_keeps_the_selected_line()
    {
        using var model = Make();
        await model.OpenAsync();
        var before = model.Snapshot!.Rows[1];
        model.Select(before);
        rig.Add(result: "a new line");

        await model.RefreshAsync();

        Assert.Equal(7, model.Snapshot!.Rows.Count);
        Assert.Equal(7, model.Verification!.LinesVerified);
        Assert.Equal(7, model.MatchCount);
        Assert.NotSame(before, model.Selected); // a new read, a new row
        Assert.Equal(before.At, model.Selected!.At);
        Assert.Equal(before.Hmac, model.Selected.Hmac);
    }

    [Fact]
    public async Task A_hidden_view_lets_go_of_the_log_and_keeps_its_filter_and_opens_again_checked()
    {
        using var model = Make();
        await model.OpenAsync();
        await model.SetModeAsync(ActivityViewMode.Lineage);
        await model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" });
        model.Select(model.Snapshot!.Rows[0]);

        model.Release();

        Assert.Equal(ActivityLoadPhase.Idle, model.Phase);
        Assert.Null(model.Snapshot);
        Assert.Empty(model.Items);
        Assert.Null(model.Selected);
        Assert.Equal(ActivityViewMode.Lineage, model.Mode);
        Assert.Equal("dextra", model.Filter.Assistant);

        await model.OpenAsync();

        Assert.Equal(ActivityLoadPhase.Ready, model.Phase);
        Assert.Equal(AuditVerificationStatus.Verified, model.Verification!.Status);
        Assert.Equal(4, model.MatchCount);
    }

    [Fact]
    public async Task A_slow_answer_that_a_newer_change_overtook_is_dropped()
    {
        var work = new GatedActivityWork();
        using var model = Make(work);
        await model.OpenAsync();
        work.Gated = true;

        var first = model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" });
        var second = model.SetFilterAsync(new ActivityFilter { Assistant = "astur-shop" });
        Assert.Equal(2, work.Held);
        work.Release(1); // the newer answer arrives first
        work.Release(0); // the older one, too late
        await Task.WhenAll(first, second);

        Assert.Equal("astur-shop", model.Filter.Assistant);
        Assert.Equal(2, model.MatchCount);
        Assert.Equal("day 2026-10-12 | #4 | #3", Shape(model.Items));
    }

    [Fact]
    public async Task A_refresh_overtaken_by_a_release_leaves_the_view_empty()
    {
        var work = new GatedActivityWork { Gated = true };
        using var model = Make(work);

        var opening = model.OpenAsync();
        Assert.Equal(ActivityLoadPhase.Checking, model.Phase);
        model.Release();
        work.Release(0);
        await opening;

        Assert.Equal(ActivityLoadPhase.Idle, model.Phase);
        Assert.Null(model.Snapshot);
    }

    [Fact]
    public async Task An_export_writes_the_lines_that_pass_the_filter_with_the_chain_check_and_says_how_it_went()
    {
        using var model = Make();
        await model.OpenAsync();
        await model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" });
        var states = new List<ActivityExportPhase>();
        model.Changed += change => { if ((change & ActivityChange.Export) != 0) states.Add(model.Export.Phase); };
        var path = Path.Combine(rig.Temp.Folder, "export.json");

        var result = await model.ExportAsync(ActivityExportFormat.Json, path);

        Assert.Equal(ActivityExportPhase.Done, result.Phase);
        Assert.Equal(4, result.Rows);
        Assert.Equal(path, result.Path);
        Assert.Equal([ActivityExportPhase.Writing, ActivityExportPhase.Done], states);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal([1L, 2, 5, 6], json["lines"]!.AsArray().Select(l => l!["seq"]!.GetValue<long>()));
        Assert.Equal("Verified", json["verification"]!["status"]!.GetValue<string>());
        Assert.Equal("dextra", json["filter"]!["assistant"]!.GetValue<string>());
        Assert.Equal("2026-10-13T12:00:00.000Z", json["exportedAt"]!.GetValue<string>());

        var csv = Path.Combine(rig.Temp.Folder, "export.csv");
        await model.ClearFilterAsync();
        Assert.Equal(6, (await model.ExportAsync(ActivityExportFormat.Csv, csv)).Rows);
        Assert.Equal(7, File.ReadAllText(csv).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task An_export_that_cannot_be_written_says_why_and_never_throws()
    {
        using var model = Make();
        await model.OpenAsync();
        var path = Path.Combine(rig.Temp.Folder, "no-such-folder", "export.csv");

        var result = await model.ExportAsync(ActivityExportFormat.Csv, path);

        Assert.Equal(ActivityExportPhase.Failed, result.Phase);
        Assert.Equal(0, result.Rows);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Same(result, model.Export);
        await Assert.ThrowsAsync<ArgumentException>(() => model.ExportAsync(ActivityExportFormat.Csv, " "));
    }

    [Fact]
    public async Task An_export_of_a_log_that_fails_says_so_in_the_file_and_marks_the_lines_after_the_failure()
    {
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines[1] = lines[1].Replace("held", "held!", StringComparison.Ordinal);
        rig.Rewrite("2026-10-12", lines);
        using var model = Make();
        await model.OpenAsync();
        var path = Path.Combine(rig.Temp.Folder, "failed.json");

        await model.ExportAsync(ActivityExportFormat.Json, path);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("Failed", json["verification"]!["status"]!.GetValue<string>());
        Assert.Equal(2, json["verification"]!["firstFailure"]!["line"]!.GetValue<int>());
        Assert.Equal(["verified", "verified", "verified", "notVerified", "notVerified", "notVerified"],
            json["lines"]!.AsArray().Select(l => l!["trust"]!.GetValue<string>()));
    }

    [Fact]
    public void Labels_turn_ids_into_names_and_an_unknown_id_stays_as_it_is()
    {
        var labels = new ActivityLabels(Assistant: id => id == "dextra" ? "Dextra" : null, Mission: id => id == "m-1" ? "Shop app: dependency audit" : "");

        Assert.Equal("Dextra", labels.ForAssistant("dextra"));
        Assert.Equal("astur-shop", labels.ForAssistant("astur-shop"));
        Assert.Equal("Shop app: dependency audit", labels.ForMission("m-1"));
        Assert.Equal("m-2", labels.ForMission("m-2")); // an empty name is no name
        Assert.Equal("t-1", labels.ForAgent("t-1"));
        Assert.Equal("t-1", ActivityLabels.None.ForAgent("t-1"));
    }

    [Fact]
    public void The_model_follows_the_clock_for_today_and_for_the_name_it_offers()
    {
        using var model = Make();

        Assert.Equal(new DateOnly(2026, 10, 13), model.Today);
        Assert.Equal("nth-activity-2026-10-13.json", model.SuggestedFileName(ActivityExportFormat.Json));
        Assert.Equal("nth-activity-2026-10-13.csv", model.SuggestedFileName(ActivityExportFormat.Csv));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new DateOnly(2026, 10, 14), model.Today);
    }

    [Fact]
    public async Task A_disposed_model_refuses_new_work_and_a_filter_with_no_snapshot_lists_nothing()
    {
        var model = Make();
        await model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" }); // before opening: nothing to list yet
        Assert.Empty(model.Items);
        Assert.Equal(0, model.MatchCount);
        model.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.RefreshAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ExportAsync(ActivityExportFormat.Json, Path.Combine(rig.Temp.Folder, "x.json")));
    }
}
