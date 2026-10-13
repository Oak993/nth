using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4, 12.5, 15.1; ED-NEW-dextra-as10-13): what nth does at launch for its duties, window open or not.
// It looks for a duties file before anything else and starts nothing for an owner with none; it starts Dextra's controller over the
// store with the audit log and the duty host when there are some; and it fails closed, starting nothing and saying why, when the store
// or the activity log is held by another writer, because a duty run has to be written down.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-13")]
public sealed class BackgroundStartTests
{
    private static Duty Weekly() => new("d1", "Weekly note", "dextra", new ManualTrigger(), "Write the weekly note.");

    private static void WriteDuties(AssistantTempRoot temp, params string[] assistants)
    {
        using var store = AssistantStore.Open(temp.Roots);
        foreach (var id in assistants) store.SaveDuties(id, DutyJson.Write(new DutyDocument([Weekly() with { AssistantId = id }])));
    }

    [Fact]
    public void An_owner_with_no_duties_file_gets_nothing_started_and_nothing_opened()
    {
        using var temp = new AssistantTempRoot();
        Assert.False(BackgroundStart.HasDuties(temp.Roots));
        Assert.False(Directory.Exists(temp.Roots.AssistantRoot));
        Assert.False(Directory.Exists(temp.Roots.AuditDirectory));
    }

    [Fact]
    public void An_empty_duties_file_counts_as_none_and_a_real_one_counts()
    {
        using var temp = new AssistantTempRoot();
        var folder = Path.Combine(temp.Roots.AssistantRoot, "assistants", "dextra");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "duties.json"), "{}");
        Assert.False(BackgroundStart.HasDuties(temp.Roots));
        File.WriteAllText(Path.Combine(folder, "duties.json"), "{\"version\":1,\"duties\":[]}");
        Assert.True(BackgroundStart.HasDuties(temp.Roots));
    }

    [Fact]
    public void The_assistants_with_duties_are_the_folders_that_hold_a_duties_file_in_name_order()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, "dextra", "armilla-2");
        using var store = AssistantStore.Open(temp.Roots);
        Directory.CreateDirectory(Path.Combine(store.Root, "assistants", "no-duties"));
        Assert.Equal(["armilla-2", "dextra"], BackgroundStart.AssistantsWithDuties(store));
    }

    [Fact]
    public void A_profile_gives_its_name_and_parent_and_an_assistant_with_none_runs_as_Dextra()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, "dextra", "scout");
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveProfile("scout", new JsonObject { ["name"] = "Scout", ["parent"] = "dextra" });
        var profiles = BackgroundStart.Profiles(store, ["dextra", "scout"]);
        Assert.Equal(("dextra", AssistantNames.Assistant, (string?)null), (profiles[0].Id, profiles[0].Name, profiles[0].ParentId));
        Assert.Equal(("scout", "Scout", "dextra"), (profiles[1].Id, profiles[1].Name, profiles[1].ParentId));
        store.SaveProfile("scout", new JsonObject { ["name"] = "Scout", ["parent"] = "someone-gone" });
        Assert.Null(BackgroundStart.Profiles(store, ["dextra", "scout"])[1].ParentId);
    }

    [Fact]
    public void A_launch_with_duties_opens_the_store_and_the_activity_log_starts_Dextra_and_starts_the_duties_hidden_or_not()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, "dextra");
        using var launch = new BackgroundLaunchRig(temp);
        var host = launch.Run(new StartupMode(true, null, null), out var problem);
        Assert.Null(problem);
        Assert.NotNull(host);
        Assert.Same(host, launch.Model.Duties);
        Assert.NotNull(launch.Model.Dextra);
        Assert.NotNull(launch.Model.DextraStore);
        Assert.Equal("d1", Assert.Single(host.Scheduler.Duties).Duty.Id);
        Assert.True(Directory.Exists(temp.Roots.AuditDirectory)); // the activity log was opened: a run has to be written down
        Assert.True(temp.Keys.HasKey());
        Assert.Equal(0, launch.Quits); // a hidden start for the schedule stays; only a start for one duty quits when it is done

        // A second call finds the duties running and starts nothing more.
        Assert.Same(host, launch.Run(new StartupMode(true, null, null), out _));
    }

    [Fact]
    public void A_store_that_another_writer_holds_starts_nothing_and_says_why()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, "dextra");
        using var holder = AssistantStore.Open(temp.Roots);
        using var launch = new BackgroundLaunchRig(temp);
        var host = launch.Run(StartupMode.Normal, out var problem);
        Assert.Null(host);
        Assert.StartsWith("Dextra's duties did not start:", problem, StringComparison.Ordinal);
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
    }

    [Fact]
    public void An_activity_log_that_another_writer_holds_starts_nothing_and_says_why()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, "dextra");
        using var holder = AuditLog.Open(temp.Roots, temp.Keys);
        using var launch = new BackgroundLaunchRig(temp);
        var host = launch.Run(StartupMode.Normal, out var problem);
        Assert.Null(host);
        Assert.StartsWith("Dextra's duties did not start:", problem, StringComparison.Ordinal);
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
        Assert.Null(launch.Model.DextraStore); // the store it opened for the try was let go again
    }
}
