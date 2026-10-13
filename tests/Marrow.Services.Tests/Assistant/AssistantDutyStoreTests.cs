using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 15.3; ED-NEW-dextra-as10-5): the duties on the real assistant store's files. They land in
// assistants/<id>/duties.json written temp-then-rename, come back through a reopen, and a file that exists but cannot be read, or
// that a newer nth wrote, is never written over. What a watcher saw last has its own file per duty, under a valid id only.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-5")]
public sealed class AssistantDutyStoreTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private string DutiesFile(string assistant) => Path.Combine(temp.Store, "assistants", assistant, "duties.json");

    [Fact]
    public void Duties_round_trip_through_a_reopen_and_land_where_the_design_puts_them()
    {
        var duty = SchedulerRig.Daily("d1", 8) with { BudgetUsd = 1.5m };
        using (var store = AssistantStore.Open(temp.Roots))
        {
            new AssistantDutyStore(store).Save("dextra", new DutyDocument([duty]));
        }
        Assert.True(File.Exists(DutiesFile("dextra")));
        using var reopened = AssistantStore.Open(temp.Roots);
        var file = new AssistantDutyStore(reopened).Load("dextra");
        Assert.True(file.WritesAllowed);
        Assert.Equal(duty, Assert.Single(file.Document.Duties));
    }

    [Fact]
    public void No_file_is_an_empty_writable_start()
    {
        using var store = AssistantStore.Open(temp.Roots);
        var file = new AssistantDutyStore(store).Load("astur-new");
        Assert.True(file.WritesAllowed);
        Assert.Empty(file.Document.Duties);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_read_only_and_stays_as_it_is()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DutiesFile("dextra"))!);
        File.WriteAllText(DutiesFile("dextra"), "{ this is not json");
        using var store = AssistantStore.Open(temp.Roots);
        var file = new AssistantDutyStore(store).Load("dextra");
        Assert.False(file.WritesAllowed);
        Assert.Empty(file.Document.Duties);
        Assert.Contains("could not be read", Assert.Single(file.Document.Problems), StringComparison.Ordinal);
        Assert.Equal("{ this is not json", File.ReadAllText(DutiesFile("dextra")));
    }

    [Fact]
    public void A_file_with_a_repeated_name_is_unreadable_not_half_read()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DutiesFile("dextra"))!);
        File.WriteAllText(DutiesFile("dextra"), """{ "version": 1, "version": 2, "duties": [] }""");
        using var store = AssistantStore.Open(temp.Roots);
        Assert.False(new AssistantDutyStore(store).Load("dextra").WritesAllowed);
    }

    [Fact]
    public void A_file_from_a_newer_nth_is_read_but_not_writable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DutiesFile("dextra"))!);
        File.WriteAllText(DutiesFile("dextra"), new JsonObject { ["version"] = 9, ["duties"] = new JsonArray(DutyJson.ToJson(SchedulerRig.Daily("d1", 8))) }.ToJsonString());
        using var store = AssistantStore.Open(temp.Roots);
        var file = new AssistantDutyStore(store).Load("dextra");
        Assert.False(file.WritesAllowed);
        Assert.Single(file.Document.Duties);
    }

    [Fact]
    public void What_a_watcher_saw_is_kept_per_duty_and_forgotten_with_it()
    {
        using var store = AssistantStore.Open(temp.Roots);
        var duties = new AssistantDutyStore(store);
        Assert.Null(duties.LoadWatch("dextra", "d1"));
        duties.SaveWatch("dextra", "d1", new JsonObject { ["version"] = 1, ["files"] = new JsonObject { ["a.pdf"] = "1:2" } });
        Assert.Equal("1:2", duties.LoadWatch("dextra", "d1")!["files"]!["a.pdf"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(temp.Store, "assistants", "dextra", "watch", "d1.json")));
        duties.DeleteWatch("dextra", "d1");
        Assert.Null(duties.LoadWatch("dextra", "d1"));
        duties.DeleteWatch("dextra", "d1"); // forgetting what is already gone is fine
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData("")]
    public void A_duty_id_that_could_leave_its_folder_is_refused(string id)
    {
        using var store = AssistantStore.Open(temp.Roots);
        Assert.Throws<ArgumentException>(() => store.SaveWatch("dextra", id, new JsonObject()));
        Assert.Throws<ArgumentException>(() => store.Watch("dextra", id));
    }

    [Fact]
    public void The_scheduler_over_the_real_store_keeps_its_duties_through_a_restart()
    {
        using var rig = new SchedulerRig();
        string id;
        using (var store = AssistantStore.Open(temp.Roots))
        {
            using var scheduler = new AssistantScheduler(new AssistantSchedulerOptions
            {
                Store = new AssistantDutyStore(store),
                Launcher = rig.Launcher,
                AssistantIds = () => ["dextra"],
                Time = rig.Clock,
            });
            scheduler.Start();
            id = scheduler.Add(SchedulerRig.Daily("", 8) with { Id = "" }).Duty!.Id;
            rig.Clock.Advance(TimeSpan.FromHours(3));
            scheduler.MissionEnded(rig.Launcher.LastMission!.Value, DutyRunOutcome.Done, "ok");
        }
        using var reopened = AssistantStore.Open(temp.Roots);
        using var again = new AssistantScheduler(new AssistantSchedulerOptions
        {
            Store = new AssistantDutyStore(reopened),
            Launcher = rig.Launcher,
            AssistantIds = () => ["dextra"],
            Time = rig.Clock,
        });
        again.Start();
        var duty = again.Find(id)!;
        Assert.Equal(DutyRunOutcome.Done, Assert.Single(duty.History).Outcome);
        Assert.Single(rig.Launcher.Fires);
    }
}
