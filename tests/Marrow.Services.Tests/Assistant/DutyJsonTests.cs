using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 15.3; ED-NEW-dextra-as10-5): the duties file. Every kind of duty round-trips exactly; the file
// carries a version; a field this build does not know survives; an entry that does not fit is kept as written and reported; a
// newer file is flagged; a name used twice in the file is not parsed at all.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-5")]
public sealed class DutyJsonTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static Duty Base(string id, DutyTrigger trigger) => new(id, "Duty " + id, "dextra", trigger, "Do it.") { Since = Since };

    public static TheoryData<Duty> Everything() => new()
    {
        Base("t-daily", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 5)), "Pakistan Standard Time")),
        Base("t-weekdays", new TimeTrigger(TimeSchedule.OnWeekdays(new TimeOnly(9, 0)), "UTC")),
        Base("t-weekly", new TimeTrigger(TimeSchedule.Weekly(WeekDays.Monday | WeekDays.Thursday, new TimeOnly(18, 30)), "UTC")),
        Base("t-monthly", new TimeTrigger(TimeSchedule.Monthly(31, new TimeOnly(9, 0)), "UTC")),
        Base("t-every", new TimeTrigger(TimeSchedule.Every(45), "UTC")),
        Base("t-once", new TimeTrigger(TimeSchedule.Once(new DateTime(2026, 11, 1, 9, 30, 0)), "UTC")),
        Base("t-cron", new TimeTrigger(TimeSchedule.FromCron("0 9 * * 1-5"), "UTC")),
        Base("folder", new FolderChangeTrigger(@"D:\Store\Invoices") { Pattern = "*.pdf", Events = FolderEvents.New | FolderEvents.Changed, Subfolders = true }),
        Base("page", new PageWatchTrigger("https://shop.example/espresso", 120)
        {
            Condition = PageCondition.PriceBelow, PriceBelow = 650.5m, StartMarker = "Price:", EndMarker = "In stock", Text = "x",
        }),
        Base("app", new AppEventTrigger(AppEventKind.AccountAbove) { Account = "Claude B", Window = "weekly", AbovePercent = 82.5 }),
        Base("manual", new ManualTrigger()),
        Base("everything", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(7, 0)), "UTC")) with
        {
            Skill = "digest", Workflow = "weekly-report", ProjectId = Guid.Parse("70000000-0000-4000-8000-000000000007"), BudgetUsd = 2.25m,
            ReserveText = "keep 30%", Report = ReportStyle.NotifyMe, Missed = MissedRunPolicy.Skip, Enabled = false, NeedsDesktop = true, CreatedBy = "dextra",
            Hours = new ActiveHours(new TimeOnly(22, 0), new TimeOnly(6, 0), WeekDays.Friday | WeekDays.Saturday),
            Scope = new DutyScope { AllowedClasses = new ValueList<ActionClass>([ActionClass.ReadWeb, ActionClass.ReadLocal]), GrantIds = new ValueList<Guid>([Guid.Parse("11111111-2222-4333-8444-555555555555")]) },
            Handled = Since.AddDays(3), LastRun = Since.AddDays(3).AddMinutes(1),
            History = new ValueList<DutyHistoryEntry>([
                new DutyHistoryEntry(Since.AddDays(3), DutyCause.CatchUp, DutyRunOutcome.NothingChanged) { MissionId = Guid.Parse("22222222-2222-4222-8222-222222222222"), Note = "quiet" },
                new DutyHistoryEntry(Since.AddDays(2), DutyCause.Time, DutyRunOutcome.Skipped) { Note = "still going", Count = 4 },
            ]),
        },
    };

    [Theory]
    [MemberData(nameof(Everything))]
    public void Every_kind_of_duty_round_trips_exactly(Duty duty)
    {
        var json = DutyJson.ToJson(duty);
        var read = DutyJson.ReadDuty(JsonNode.Parse(json.ToJsonString())!.AsObject(), out var problem);
        Assert.Null(problem);
        Assert.Equal(duty, read);
    }

    [Fact]
    public void The_document_carries_a_version_and_reads_back_with_its_duties_in_order()
    {
        var document = new DutyDocument([Base("a", new ManualTrigger()), Base("b", new ManualTrigger())]);
        var json = DutyJson.Write(document);
        Assert.Equal(DutyJson.CurrentVersion, json["version"]!.GetValue<int>());
        var read = DutyJson.Read(JsonNode.Parse(json.ToJsonString()));
        Assert.Equal(["a", "b"], read.Duties.Select(d => d.Id));
        Assert.False(read.Newer);
        Assert.Empty(read.Problems);
    }

    [Fact]
    public void A_missing_file_is_an_empty_document_and_a_file_without_a_version_is_the_current_one()
    {
        Assert.Empty(DutyJson.Read(null).Duties);
        var read = DutyJson.Read(JsonNode.Parse("""{ "duties": [] }"""));
        Assert.Equal(DutyJson.CurrentVersion, read.Version);
    }

    [Fact]
    public void Fields_a_newer_build_wrote_survive_a_load_and_a_save_on_the_duty_and_on_the_document()
    {
        var text = """
            { "version": 1, "futureDocument": { "a": [1, 2] }, "duties": [ __DUTY__ ] }
            """;
        var duty = DutyJson.ToJson(Base("a", new ManualTrigger()));
        duty["futureField"] = new JsonObject { ["nested"] = true };
        var read = DutyJson.Read(JsonNode.Parse(text.Replace("__DUTY__", duty.ToJsonString(), StringComparison.Ordinal)));
        var written = DutyJson.Write(read);
        Assert.True(written["futureDocument"]!["a"]![1]!.GetValue<int>() == 2);
        Assert.True(written["duties"]![0]!["futureField"]!["nested"]!.GetValue<bool>());
    }

    [Fact]
    public void An_entry_that_does_not_fit_is_kept_as_written_and_reported()
    {
        var good = DutyJson.ToJson(Base("good", new ManualTrigger()));
        var bad = new JsonObject { ["id"] = "bad", ["name"] = "x", ["assistant"] = "dextra", ["trigger"] = new JsonObject { ["kind"] = "teleport" }, ["since"] = Since.ToString("O", System.Globalization.CultureInfo.InvariantCulture) };
        var notAnObject = JsonValue.Create(7)!;
        var read = DutyJson.Read(new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(good, bad, notAnObject) });
        Assert.Equal(["good"], read.Duties.Select(d => d.Id));
        Assert.Equal(2, read.Unreadable.Count);
        Assert.Equal(2, read.Problems.Count);
        var written = DutyJson.Write(read);
        Assert.Equal(3, written["duties"]!.AsArray().Count);
        Assert.Equal("teleport", written["duties"]![1]!["trigger"]!["kind"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("report", "Loudly")]
    [InlineData("missed", "Maybe")]
    public void A_value_the_enum_does_not_have_makes_the_entry_unreadable_not_a_default(string field, string value)
    {
        var duty = DutyJson.ToJson(Base("a", new ManualTrigger()));
        duty[field] = value;
        var read = DutyJson.Read(new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(duty) });
        Assert.Empty(read.Duties);
        Assert.Single(read.Unreadable);
    }

    [Fact]
    public void A_duplicate_id_leaves_the_second_alone_and_a_newer_version_is_flagged()
    {
        var one = DutyJson.ToJson(Base("same", new ManualTrigger()));
        var two = DutyJson.ToJson(Base("same", new ManualTrigger()));
        var read = DutyJson.Read(new JsonObject { ["version"] = 2, ["duties"] = new JsonArray(one, two) });
        Assert.Single(read.Duties);
        Assert.Single(read.Unreadable);
        Assert.True(read.Newer);
        Assert.Contains(read.Problems, p => p.Contains("share the id", StringComparison.Ordinal));
        Assert.Contains(read.Problems, p => p.Contains("newer nth", StringComparison.Ordinal));
    }

    [Fact]
    public void More_duties_than_the_limit_are_read_up_to_it_and_the_rest_kept()
    {
        var entries = Enumerable.Range(0, DutyJson.DutyLimit + 5).Select(i => (JsonNode)DutyJson.ToJson(Base("d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), new ManualTrigger())));
        var read = DutyJson.Read(new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(entries.ToArray()) });
        Assert.Equal(DutyJson.DutyLimit, read.Duties.Count);
        Assert.Equal(5, read.Unreadable.Count);
        Assert.Equal(DutyJson.DutyLimit + 5, DutyJson.Write(read)["duties"]!.AsArray().Count);
    }

    [Fact]
    public void A_long_history_is_cut_to_fifty_when_it_is_read()
    {
        var duty = DutyJson.ToJson(Base("a", new ManualTrigger()));
        var history = new JsonArray();
        for (var i = 0; i < 70; i++) history.Add(new JsonObject { ["at"] = Since.AddHours(i).ToString("O", System.Globalization.CultureInfo.InvariantCulture), ["cause"] = "Time", ["outcome"] = "Done" });
        duty["history"] = history;
        var read = DutyJson.Read(new JsonObject { ["version"] = 1, ["duties"] = new JsonArray(duty) });
        Assert.Equal(Duty.HistoryLimit, read.Duties[0].History.Count);
    }

    [Fact]
    public void The_document_that_is_not_an_object_is_reported_and_kept()
    {
        var read = DutyJson.Read(JsonNode.Parse("[1,2]"));
        Assert.Empty(read.Duties);
        Assert.NotEmpty(read.Problems);
    }
}
