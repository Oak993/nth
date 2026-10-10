using System.Linq;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B3 52bc7926), part 1 of 3: init (:120-147), bootstrap (:238-287),
// currentProject (:305-313), openThreadOnLaunch (:581-611), delete (:947-1002), createWorktree (:1125-1150),
// saveBeforeQuit (:1325-1345), and the collaborators those lines call. Every expectation is transcribed from the
// cited Swift and the PR records; none comes from running the port. Every model lives in an AppModelHarness
// (throwaway store root, home, defaults and Credential Manager, R-TEST-9); bootstrap runs only through
// AppModelHarness.Bootstrap, which makes sure no provider CLI starts and no API is reached (R-TEST-10).
// The failed-library-load message (OD-0008 A10, OD-0012 E4) has no Swift twin; these tests read it from
// AppModel.Alert, the port-only surface through which the error is shown.
public sealed class AppModelTests
{
    // OD-0012 E4, byte for byte (ASCII only).
    private const string LibraryLoadMessage =
        "Nth could not read library.json, so your projects and threads are hidden. The file will not be changed until it can be read.";

    // ===== PR-LAUNCH-035 / PR-LAUNCH-036: the load-time repairs =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-035")] // App/AppModel.swift:129-131
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:129-136; HangWatchdog.swift:5-7
    [Trait("Req", "PR-STORE-101")] // App/AppModel.swift:129-131
    public void Init_repairs_every_running_thread_to_interrupted_in_memory_only()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var running = AppModelSamples.Thread(project) with { LastStatus = TurnStatus.Running };
        var completed = AppModelSamples.Thread(project) with { LastStatus = TurnStatus.Completed };
        var failed = AppModelSamples.Thread(project) with { LastStatus = TurnStatus.Failed };
        var untouched = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [running, completed, failed, untouched]);
        var before = AppModelHarness.LibraryBytes();

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Equal(TurnStatus.Interrupted, model.Thread(running.Id)!.LastStatus);
            Assert.Equal(TurnStatus.Completed, model.Thread(completed.Id)!.LastStatus);
            Assert.Equal(TurnStatus.Failed, model.Thread(failed.Id)!.LastStatus);
            Assert.Null(model.Thread(untouched.Id)!.LastStatus);
            Assert.Equal(TurnStatus.Interrupted, model.Threads.Single(t => t.Id == running.Id).LastStatus);
        });
        // Not written until some later save: construction itself writes nothing.
        Assert.Equal(before, AppModelHarness.LibraryBytes());
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-036")] // App/AppModel.swift:132-136
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:132-136
    [Trait("Req", "PR-STORE-102")] // App/AppModel.swift:132-136
    public void Init_marks_every_running_head_stopped_and_stamps_it_with_the_launch_time()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var finishedLong = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var running = AppModelSamples.Thread(project) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) };
        var done = AppModelSamples.Thread(project) with
        {
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { FinishedAt = finishedLong },
        };
        h.WriteLibrary([project], [running, done]);
        h.Main.Advance(TimeSpan.FromHours(5)); // the launch happens well after the head last ran
        var launch = SwiftDate.Now;

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            var head = model.Thread(running.Id)!.Hydra!;
            Assert.Equal(HydraHeadInfo.Status.Stopped, head.HydraHeadInfoStatus);
            Assert.Equal(launch, head.FinishedAt);
            var other = model.Thread(done.Id)!.Hydra!;
            Assert.Equal(HydraHeadInfo.Status.Completed, other.HydraHeadInfoStatus);
            Assert.Equal(finishedLong, other.FinishedAt);
        });
    }

    // ===== PR-LAUNCH-033: a stale lastProjectID =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-033")] // App/AppModel.swift:143-145; App/AppSettings.swift:320-326
    public void Init_clears_a_stored_last_project_id_naming_no_project_by_removing_the_key()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(Guid.NewGuid()));
        h.Defaults.Writes.Clear();

        var model = h.MakeModel();

        h.Main.OnMain(() => Assert.Null(model.Settings.LastProjectID));
        Assert.Null(h.Defaults.Object("lastProjectID")); // removed, not written as an empty string
        Assert.Contains("lastProjectID", h.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-033")] // App/AppModel.swift:143-145
    public void Init_keeps_a_stored_last_project_id_that_names_a_project()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var stored = SwiftUUID.UuidString(project.Id);
        h.Defaults.Set("lastProjectID", stored);
        h.Defaults.Writes.Clear();

        var model = h.MakeModel();

        h.Main.OnMain(() => Assert.Equal(project.Id, model.Settings.LastProjectID));
        Assert.Equal(stored, h.Defaults.Object("lastProjectID"));
        Assert.DoesNotContain("lastProjectID", h.Defaults.Writes);
    }

    // ===== PR-LAUNCH-034: loading the library =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-034")] // App/AppModel.swift:125-127; Services/Store/Storage.swift:60-61
    public void Init_with_no_library_file_opens_an_empty_library_with_no_message()
    {
        using var h = new AppModelHarness();

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Empty(model.Projects);
            Assert.Empty(model.Threads);
            Assert.Null(model.Alert?.Message);
            Assert.Null(model.Alert);
        });
        Assert.False(File.Exists(Storage.LibraryURL)); // construction writes nothing
        // A missing file is no failed load: the ordinary quit-time save creates it.
        h.Main.OnMain(model.SaveBeforeQuit);
        Assert.True(File.Exists(Storage.LibraryURL));
    }

    [Theory]
    [Trait("Req", "PR-LAUNCH-034")] // App/AppModel.swift:126-129, :1304-1335; Services/Store/Storage.swift:60-63 (DEVIATION OD-0008 A10, OD-0012 E4)
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:1304-1323 (B3)
    [InlineData("{\"projects\":[{\"id\":")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void Init_with_an_undecodable_library_shows_the_error_and_no_save_overwrites_the_file(string contents)
    {
        using var h = new AppModelHarness();
        Directory.CreateDirectory(Storage.Root);
        var bytes = Encoding.UTF8.GetBytes(contents);
        File.WriteAllBytes(Storage.LibraryURL, bytes);

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Empty(model.Projects);
            Assert.Empty(model.Threads);
            Assert.Equal(LibraryLoadMessage, model.Alert?.Message);
        });
        // The throttled save (:1304-1314), which stamps lastLibrarySave at :1318 before this OD-0008 guard runs, still leaves the file alone...
        h.Main.OnMain(model.ScheduleSave);
        h.Main.Advance(TimeSpan.FromSeconds(1));
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(bytes, File.ReadAllBytes(Storage.LibraryURL));
        // ...and so does the synchronous quit-time save (:1332-1335).
        h.Main.OnMain(model.SaveBeforeQuit);
        Assert.Equal(bytes, File.ReadAllBytes(Storage.LibraryURL));
    }

    // ===== PR-LAUNCH-032: currentProject at launch =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-032")] // App/AppModel.swift:296-298
    public void Current_project_at_launch_is_the_stored_last_project_when_it_still_exists()
    {
        using var h = new AppModelHarness();
        var first = AppModelSamples.Project("First", h.NewFolder("a"));
        var last = AppModelSamples.Project("Last", h.NewFolder("b"));
        var newest = AppModelSamples.Thread(first) with { UpdatedAt = SwiftDate.Now };
        h.WriteLibrary([first, last], [newest]);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(last.Id));

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Null(model.SelectedThreadID);
            Assert.Equal(last.Id, model.CurrentProject!.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-032")] // App/AppModel.swift:299-302
    public void Current_project_falls_back_to_the_newest_thread_neither_archived_nor_in_a_panel()
    {
        using var h = new AppModelHarness();
        var p1 = AppModelSamples.Project("One", h.NewFolder("1"));
        var p2 = AppModelSamples.Project("Two", h.NewFolder("2"));
        var p3 = AppModelSamples.Project("Three", h.NewFolder("3"));
        var p4 = AppModelSamples.Project("Four", h.NewFolder("4"));
        var now = SwiftDate.Now;
        var older = AppModelSamples.Thread(p1) with { UpdatedAt = now.AddHours(-3) };
        var newestOpen = AppModelSamples.Thread(p2) with { UpdatedAt = now.AddHours(-2) };
        var archived = AppModelSamples.Thread(p3) with { UpdatedAt = now.AddHours(-1), IsArchived = true };
        var inPanel = AppModelSamples.Thread(p4) with { UpdatedAt = now, IsInPanel = true };
        h.WriteLibrary([p1, p2, p3, p4], [older, newestOpen, archived, inPanel]);

        var model = h.MakeModel();

        h.Main.OnMain(() => Assert.Equal(p2.Id, model.CurrentProject!.Id));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-032")] // App/AppModel.swift:297; App/AppSettings.swift:733-736
    public void Current_project_treats_a_stored_id_that_is_no_uuid_as_none()
    {
        using var h = new AppModelHarness();
        var p1 = AppModelSamples.Project("One", h.NewFolder("1"));
        var p2 = AppModelSamples.Project("Two", h.NewFolder("2"));
        var recent = AppModelSamples.Thread(p2) with { UpdatedAt = SwiftDate.Now };
        h.WriteLibrary([p1, p2], [recent]);
        h.Defaults.Set("lastProjectID", "not-a-uuid");

        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Null(model.Settings.LastProjectID);
            Assert.Equal(p2.Id, model.CurrentProject!.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-032")] // App/AppModel.swift:301-303
    public void Current_project_falls_back_to_the_first_project_and_is_nil_with_no_projects()
    {
        using (var h = new AppModelHarness())
        {
            var p1 = AppModelSamples.Project("One", h.NewFolder("1"));
            var p2 = AppModelSamples.Project("Two", h.NewFolder("2"));
            var archived = AppModelSamples.Thread(p2) with { IsArchived = true, UpdatedAt = SwiftDate.Now };
            h.WriteLibrary([p1, p2], [archived]);
            var model = h.MakeModel();
            h.Main.OnMain(() => Assert.Equal(p1.Id, model.CurrentProject!.Id));
        }
        using (var h = new AppModelHarness())
        {
            var model = h.MakeModel();
            h.Main.OnMain(() => Assert.Null(model.CurrentProject));
        }
    }

    // ===== PR-LAUNCH-030 / PR-LAUNCH-031: openThreadOnLaunch =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-030")] // App/AppModel.swift:575-598, :650-702
    public void Open_thread_on_launch_reuses_the_newest_unused_thread_and_refreshes_it_to_what_a_fresh_chat_opens_on()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var other = AppModelSamples.Project("Other", h.NewFolder("o"));
        var now = SwiftDate.Now;
        ChatThread Unused(DateTimeOffset created) => AppModelSamples.Thread(project) with
        {
            CreatedAt = created,
            UpdatedAt = created,
            ProviderSessionID = "session-1",
            ProviderResumeAt = "resume-1",
            Model = "gpt-old",
            Effort = "high",
            FastMode = true,
            HydraPairID = Guid.NewGuid(),
            HydraEnabled = false,
        };
        var olderUnused = Unused(now.AddHours(-5));
        var newestUnused = Unused(now.AddHours(-4));
        // Every one of these is newer, and every one fails the predicate at :577-578.
        var sent = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), LastStatus = TurnStatus.Completed };
        var named = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), HasCustomTitle = true };
        var retitled = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), Title = "Fix the build" };
        var archived = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), IsArchived = true };
        var settled = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), IsSettled = true };
        var helper = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-1), ParentThreadID = olderUnused.Id };
        var elsewhere = AppModelSamples.Thread(other) with { CreatedAt = now.AddHours(-1) };
        h.WriteLibrary([project, other], [olderUnused, newestUnused, sent, named, retitled, archived, settled, helper, elsewhere]);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(project.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.DefaultProvider = ProviderKind.Claude);
        var count = h.Main.OnMain(() => model.Threads.Count);

        h.Main.OnMain(model.OpenThreadOnLaunch);

        h.Main.OnMain(() =>
        {
            Assert.Equal(count, model.Threads.Count); // nothing inserted, the throwaway fresh thread included
            Assert.Equal(newestUnused.Id, model.SelectedThreadID);
            var reused = model.Thread(newestUnused.Id)!;
            // Nothing is installed (no provider was refreshed), so a fresh chat is on the default provider (:663-664)
            // with that provider's default model (:668), no effort (:669-670), fast mode off (:671), no pair and the
            // hydra default (Library.swift:199-202, :220).
            Assert.Equal(ProviderKind.Claude, reused.Provider);
            Assert.Equal(model.Providers.DefaultModel(ProviderKind.Claude)?.Id, reused.Model);
            Assert.Null(reused.Effort);
            Assert.False(reused.FastMode);
            Assert.Null(reused.HydraPairID);
            Assert.True(reused.HydraEnabled);
            // The provider changed, so its session went first (:588-591).
            Assert.Null(reused.ProviderSessionID);
            Assert.Null(reused.ProviderResumeAt);
            // The older unused row is left exactly as it was.
            Assert.Equal("session-1", model.Thread(olderUnused.Id)!.ProviderSessionID);
            Assert.Equal(ProviderKind.Codex, model.Thread(olderUnused.Id)!.Provider);
            // The selection's didSet remembers the project (:92-94).
            Assert.Equal(project.Id, model.Settings.LastProjectID);
        });
        // The update schedules the debounced save (:709), which writes the refreshed row.
        h.Main.Advance(TimeSpan.FromMilliseconds(400));
        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = AppModelHarness.LoadedLibrary().Threads.Single(t => t.Id == newestUnused.Id);
        Assert.Equal(ProviderKind.Claude, saved.Provider);
        Assert.Null(saved.ProviderSessionID);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-030")] // App/AppModel.swift:587-591
    public void Open_thread_on_launch_keeps_the_provider_session_when_the_provider_is_unchanged()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var unused = AppModelSamples.Thread(project) with { ProviderSessionID = "session-1", ProviderResumeAt = "resume-1", Effort = "high" };
        h.WriteLibrary([project], [unused]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.DefaultProvider = ProviderKind.Codex);

        h.Main.OnMain(model.OpenThreadOnLaunch);

        h.Main.OnMain(() =>
        {
            var reused = model.Thread(unused.Id)!;
            Assert.Equal(ProviderKind.Codex, reused.Provider);
            Assert.Equal("session-1", reused.ProviderSessionID);
            Assert.Equal("resume-1", reused.ProviderResumeAt);
            Assert.Null(reused.Effort); // still copied from the fresh thread
            Assert.Equal(unused.Id, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-031")] // App/AppModel.swift:599-601, :604-620
    public void Open_thread_on_launch_creates_and_selects_a_new_thread_when_none_is_reusable_and_never_accumulates()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var sent = AppModelSamples.Thread(project) with { LastStatus = TurnStatus.Completed };
        h.WriteLibrary([project], [sent]);
        var model = h.MakeModel();

        h.Main.OnMain(model.OpenThreadOnLaunch);

        var created = h.Main.OnMain(() =>
        {
            Assert.Equal(2, model.Threads.Count);
            var thread = model.SelectedThread!;
            Assert.NotEqual(sent.Id, thread.Id);
            Assert.Equal(project.Id, thread.ProjectID);
            Assert.Equal(ChatThread.Untitled, thread.Title);
            Assert.Equal("New thread", ChatThread.Untitled);
            return thread.Id;
        });
        // A second launch reuses that row instead of leaving another "New thread" behind (:572-573).
        h.Main.OnMain(model.OpenThreadOnLaunch);
        h.Main.OnMain(() =>
        {
            Assert.Equal(2, model.Threads.Count);
            Assert.Equal(created, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-031")] // App/AppModel.swift:576
    [Trait("Req", "PR-LAUNCH-069")] // App/AppModel.swift:296-304,574-575
    public void Open_thread_on_launch_with_no_project_does_nothing()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();

        h.Main.OnMain(model.OpenThreadOnLaunch);

        h.Main.OnMain(() =>
        {
            Assert.Empty(model.Threads);
            Assert.Null(model.SelectedThreadID);
        });
        Assert.False(File.Exists(Storage.LibraryURL)); // the empty-project launch path does not save an empty library
    }

    // ===== PR-LAUNCH-023: the switch note =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-023")] // App/AppModel.swift:72-78; Core/Support/SwitchLatency.swift:47-57
    [Trait("Decision", "ED-005")]
    public void Selecting_a_thread_notes_the_switch_with_no_visible_effect_in_a_shipped_build()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project) with { Title = "A title longer than twenty-four characters" };
        h.WriteLibrary([project], [thread]);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(project.Id));
        var model = h.MakeModel();
        h.Defaults.Writes.Clear();

        // To the thread, to the same thread again, and to nothing ("none", :77): each a no-op note (ED-005).
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);
        h.Main.OnMain(() => model.SelectedThreadID = null);

        h.Main.OnMain(() => Assert.Null(model.SelectedThreadID));
        // No defaults key is written by the note; lastProjectID already named this project, so nothing is (:292-295).
        Assert.Empty(h.Defaults.Writes);
    }

    // ===== PR-STORE-015 / PR-LAUNCH-076: scheduleSave is a throttle, not a debounce (B3) =====

    [Fact]
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:1304-1314
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:1304-1314
    public void ScheduleSave_a_burst_of_changes_with_no_prior_save_writes_once_400_ms_after_the_first_change()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();

        // Fresh model, no prior save: the first change's wait is the 400 ms floor (lastLibrarySave is nil, so
        // sinceLast reads as the 60 s fallback and 4000 ms - 60000 ms loses to 400 ms in the max).
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "A" }));
        h.Main.AdvanceMilliseconds(200);
        // A change while a save is already pending (saveTask != null) is a complete no-op (:1305): it neither
        // cancels nor restarts the wait, unlike the old cancel-and-restart debounce.
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "B" }));
        h.Main.AdvanceMilliseconds(150);
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "C" }));
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("P", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name); // still nothing at t=350 ms

        // If the wait had restarted at t=350 ms (the old debounce), this write would land at t=750 ms, not 400 ms.
        h.Main.AdvanceMilliseconds(50); // t=400 ms since the first change of the burst
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("C", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name); // carries every change made during the wait
    }

    // PR-LAUNCH-060's own verification_method: ten mutations in 100 ms produce exactly ONE write, 400 ms after the first.
    // The write count is read off lastLibrarySave (AppModel.swift:1318, stamped by every saveLibrary() call, scheduled or direct).
    [Fact]
    [Trait("Req", "PR-LAUNCH-060")] // AppModel.swift:1304-1314, :1316-1324; Storage.swift:340-351
    public void ScheduleSave_ten_changes_within_100_ms_write_once_400_ms_after_the_first_and_never_a_second_time()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        var lastSave = typeof(AppModel).GetField("lastLibrarySave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Null(lastSave.GetValue(model));

        for (var i = 0; i < 10; i++)
        {
            h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "N" + i }));
            h.Main.AdvanceMilliseconds(10);
        }
        h.Main.AdvanceMilliseconds(299); // t=399 ms since the first change: nothing yet, so the wait never restarted
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("P", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
        Assert.Null(lastSave.GetValue(model));

        h.Main.AdvanceMilliseconds(1); // t=400 ms: the one write, carrying the last of the ten changes
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("N9", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
        var firstStamp = lastSave.GetValue(model);
        Assert.NotNull(firstStamp);

        // No second write for the nine other calls: a call while a save is pending scheduled nothing extra.
        h.Main.AdvanceMilliseconds(10_000);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(firstStamp, lastSave.GetValue(model));
        Assert.Equal("N9", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    [Fact]
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:1304-1314, :1318
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:1304-1314, :1318
    [Trait("Req", "PR-LAUNCH-060")] // AppModel.swift:1307 (the 4 s minimum spacing clause)
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-LAUNCH-060: spacing only; the burst/one-write clause is ScheduleSave_ten_changes_within_100_ms_...
    public void ScheduleSave_soon_after_a_save_landed_waits_nearly_four_seconds_not_the_400_ms_floor()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();

        // First change: nothing saved yet, so the 400 ms floor applies and the write lands at t=400 ms, stamping
        // lastLibrarySave (:1318).
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "First" }));
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("First", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);

        // A change right after that save must wait close to 4 s (4000 ms - time since lastLibrarySave), not the
        // 400 ms floor: max(400 ms, 4000 ms - ~0 ms) = ~4000 ms.
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Second" }));
        h.Main.AdvanceMilliseconds(3800); // t=4200 ms: about 3.8 s after the change, short of the ~4 s wait
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("First", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name); // not written yet

        h.Main.AdvanceMilliseconds(200); // t=4400 ms: 4000 ms after the change scheduled at t=400 ms
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Second", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:1304-1314
    public void ScheduleSave_a_steady_stream_of_changes_is_never_starved()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();

        // A change every 100 ms: the old cancel-and-restart debounce (guard persistenceEnabled else return;
        // saveTask?.cancel(); saveTask = Task { sleep 400 ms; ... }) never goes quiet long enough to fire and
        // would leave a stream this dense completely unsaved.
        for (var i = 0; i < 5; i++)
        {
            h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Change " + i }));
            h.Main.AdvanceMilliseconds(100);
        }
        // t=500 ms: the first write already landed at t=400 ms, before this first burst even finished.
        DiskWriter.Shared.WaitForQueuedWrites();
        var firstSaved = Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name;
        Assert.NotEqual("P", firstSaved);

        // Keep changing every 100 ms; a second write must land about 4 s after the first one (t=400+4000=4400 ms),
        // proving the throttle re-arms instead of leaving the stream starved forever.
        for (var i = 5; i < 45; i++)
        {
            h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Change " + i }));
            h.Main.AdvanceMilliseconds(100);
        }
        // t=4500 ms.
        DiskWriter.Shared.WaitForQueuedWrites();
        var secondSaved = Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name;
        Assert.NotEqual(firstSaved, secondSaved); // a second save landed: the stream was not starved
    }

    // Review fix (finding #4, MEDIUM): no test showed that a DIRECT saveLibrary() call (reached from settle,
    // reopen or delete, not through the scheduler) resets the throttle the same way a scheduled save landing
    // does. saveLibrary() (App/AppModel.swift:1316-1318) stamps lastLibrarySave unconditionally, whether it
    // runs at the end of a scheduled wait (:1312) or is called directly (settle :815, reopen :834, delete
    // :965). This test uses Settle as the direct-save trigger; reopen and delete stamp lastLibrarySave through
    // the exact same saveLibrary() call and are not re-tested here for that reason.
    //
    // A subtlety this test has to route around: settle (like reopen and delete) reaches its trailing direct
    // saveLibrary() call (:815) only after updateThread (:714-721) has already mutated the thread and called
    // scheduleSave() itself (its own ordinary path, unconditional on any change). So settle always leaves a
    // saveTask already pending by the time its direct saveLibrary() call runs. If that pending task's wait
    // were still the first-ever-save 400 ms floor, the mutation right after Settle would ride that 400 ms
    // task, not a fresh ~4 s one, which would make this test indistinguishable from an ordinary throttle test
    // and prove nothing about the direct-save stamp. To isolate what the record actually claims, this test
    // first lets an ordinary scheduled save land (so lastLibrarySave is already recent when Settle runs);
    // updateThread's own scheduleSave call inside Settle then computes a ~4 s wait from that recent stamp,
    // and it is THAT pending task - armed by settle's internal update, timed from settle's direct save - that
    // the immediately following UpdateProject rides, proving the direct save reset the throttle to ~4 s.
    [Fact]
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:714-721 (updateThread schedules), :787-807 (Settle saves directly), :1316-1318
    public void ScheduleSave_soon_after_a_direct_save_settle_reopen_or_delete_waits_nearly_four_seconds_not_the_400_ms_floor()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();

        // Let one ordinary save land first, so lastLibrarySave is recent (not nil) when Settle runs below.
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Warm" }));
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Warm", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);

        // Settle's own updateThread call schedules a save computed from the just-landed lastLibrarySave
        // (~4 s, not the 400 ms floor); its trailing saveLibrary() call (:815) then writes the settled state
        // immediately and re-stamps lastLibrarySave, without touching that pending task.
        h.Main.OnMain(() => model.Settle(thread.Id, sounds: false));
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.True(AppModelHarness.LoadedLibrary().Threads.Single(t => t.Id == thread.Id).IsSettled);

        // A change right after Settle rides that already-pending ~4 s task (scheduleSave is a no-op while one
        // is pending), not a fresh 400 ms one.
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Renamed" }));
        h.Main.AdvanceMilliseconds(3800); // ~3.8 s after Settle: short of the ~4 s wait
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Warm", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name); // not written yet

        h.Main.AdvanceMilliseconds(200); // ~4000 ms after Settle's direct save
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Renamed", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    // ===== PR-LAUNCH-053 to PR-LAUNCH-056: saveBeforeQuit =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-054")] // App/AppModel.swift:1332-1335; Services/Store/Storage.swift:358-361
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1326-1344
    public void Save_before_quit_writes_the_whole_library_before_returning_and_drops_a_queued_write_of_it()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Renamed" }));
        // An older value of the same file queued behind it must not land after it.
        DiskWriter.Shared.EncodeAndWrite(new Library(), Storage.LibraryURL);

        h.Main.OnMain(model.SaveBeforeQuit);

        var saved = AppModelHarness.LoadedLibrary(); // read at once: no pump, no wait
        Assert.Equal("Renamed", Assert.Single(saved.Projects).Name);
        Assert.Equal(thread.Id, Assert.Single(saved.Threads).Id);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Renamed", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-053")] // App/AppModel.swift:1304-1331
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1326-1335
    public void Save_before_quit_latches_persistence_off_for_good_and_cancels_the_pending_save()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        // A change schedules the throttled save (first write, so the 400 ms floor applies); the quit comes 100 ms in.
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "Before quit" }));
        h.Main.AdvanceMilliseconds(100);

        h.Main.OnMain(model.SaveBeforeQuit);
        Assert.Equal("Before quit", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
        var sentinel = Encoding.UTF8.GetBytes("sentinel");
        File.WriteAllBytes(Storage.LibraryURL, sentinel);

        // The cancelled save never fires; later changes schedule nothing (:1305); a second quit does nothing (:1328).
        h.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Name = "After quit" }));
        h.Main.Advance(TimeSpan.FromSeconds(2));
        h.Main.OnMain(model.ScheduleSave);
        h.Main.Advance(TimeSpan.FromSeconds(2));
        h.Main.OnMain(model.SaveBeforeQuit);
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(sentinel, File.ReadAllBytes(Storage.LibraryURL));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-054")] // App/AppModel.swift:1335; Services/Store/Storage.swift:381-388
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1326-1344
    public void Save_before_quit_continues_when_the_library_cannot_be_written()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        File.Delete(Storage.LibraryURL);
        Directory.CreateDirectory(Storage.LibraryURL); // a folder where the file goes: the atomic write fails
        var queued = Guid.NewGuid();
        DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(queued), Storage.ThreadURL(queued));

        // A failed Windows atomic replacement retries on SwiftClock. The production clock is real time; use it
        // here because the test's synchronous main-loop call cannot advance TestRuntime.Time while DiskWriter waits.
        using (SwiftClock.Provider.Override(TimeProvider.System))
            h.Main.OnMain(model.SaveBeforeQuit); // no exception reaches the caller

        Assert.True(Directory.Exists(Storage.LibraryURL));
        Assert.True(File.Exists(Storage.ThreadURL(queued))); // and the rest of the quit ran (:1343)
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-055")] // App/AppModel.swift:1336-1342
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1336-1342
    public void Save_before_quit_writes_no_document_for_a_runtime_whose_thread_is_already_on_disk()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        h.Main.OnMain(() => Assert.Same(model.Runtime(thread.Id), model.ExistingRuntime(thread.Id)));
        Directory.CreateDirectory(Storage.ThreadsDirectory);
        var sentinel = Encoding.UTF8.GetBytes("sentinel");
        File.WriteAllBytes(Storage.ThreadURL(thread.Id), sentinel);

        h.Main.OnMain(model.SaveBeforeQuit);

        // Nothing was changed since the (never made) last load, so unsavedSnapshot() is nil and nothing is written.
        Assert.Equal(sentinel, File.ReadAllBytes(Storage.ThreadURL(thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-056")] // App/AppModel.swift:1343; Services/Store/Storage.swift:376-379
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1343-1344
    public void Save_before_quit_returns_only_once_every_queued_write_has_landed()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids) DiskWriter.Shared.EncodeAndWrite(new ThreadDocument(id), Storage.ThreadURL(id));

        h.Main.OnMain(model.SaveBeforeQuit);

        foreach (var id in ids) Assert.True(File.Exists(Storage.ThreadURL(id)));
    }

    // ===== PR-LAUNCH-039 to PR-LAUNCH-045, PR-HYDRAMERGE-003, PR-HYDRAMERGE-004: bootstrap =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-039")] // App/AppModel.swift:240-243, :441-445; Services/Store/Storage.swift:279-305
    public void Bootstrap_prewarms_the_twelve_newest_threads_neither_archived_nor_in_a_panel()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var now = SwiftDate.Now;
        // Fourteen eligible threads, newest first by updatedAt; creation order is deliberately the reverse.
        var open = Enumerable.Range(0, 14)
            .Select(i => AppModelSamples.Thread(project) with { UpdatedAt = now.AddMinutes(-10 * i), CreatedAt = now.AddDays(-1).AddMinutes(i) })
            .ToList();
        var archived = AppModelSamples.Thread(project) with { UpdatedAt = now.AddMinutes(5), IsArchived = true };
        var inPanel = AppModelSamples.Thread(project) with { UpdatedAt = now.AddMinutes(5), IsInPanel = true };
        var all = open.Concat([archived, inPanel]).ToList();
        h.WriteLibrary([project], Enumerable.Reverse(all));
        foreach (var thread in all) AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();

        h.Bootstrap(model);
        AppModelHarness.WaitForPrefetch();

        for (var i = 0; i < 12; i++) Assert.NotNull(DocumentPrefetch.Shared.Take(open[i].Id));
        Assert.Null(DocumentPrefetch.Shared.Take(open[12].Id));
        Assert.Null(DocumentPrefetch.Shared.Take(open[13].Id));
        Assert.Null(DocumentPrefetch.Shared.Take(archived.Id));
        Assert.Null(DocumentPrefetch.Shared.Take(inPanel.Id));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-039")] // App/AppModel.swift:441-445
    [Trait("Req", "PR-STORE-051")] // App/AppModel.swift:440-445
    public void Bootstrap_does_not_prewarm_a_thread_that_already_has_a_live_runtime()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var now = SwiftDate.Now;
        var live = AppModelSamples.Thread(project) with { UpdatedAt = now };
        var cold = AppModelSamples.Thread(project) with { UpdatedAt = now.AddMinutes(-1) };
        h.WriteLibrary([project], [live, cold]);
        AppModelHarness.WriteDocument(live.Id);
        AppModelHarness.WriteDocument(cold.Id);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Runtime(live.Id));
        h.Main.OnMain(() => model.WarmDocuments([live.Id])); // The all-live batch returns before touching prefetch.

        h.Bootstrap(model);
        AppModelHarness.WaitForPrefetch();

        Assert.Null(DocumentPrefetch.Shared.Take(live.Id));
        Assert.NotNull(DocumentPrefetch.Shared.Take(cold.Id));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-040")] // App/AppModel.swift:244; App/AppSettings.swift:461-482, :740
    public void Bootstrap_reads_the_stored_keys_after_construction_and_drops_the_plaintext_copy()
    {
        using var h = new AppModelHarness();
        // held by the in-memory Credential Manager; B3: the deleted CommandCodeKeychain wrapper is replaced by
        // APIKeychain.SetKey(key, ProviderKind), keyed by ProviderKind.Commandcode (PR-PROVAPI-192).
        Assert.True(APIKeychain.SetKey("cc-test-key", ProviderKind.Commandcode));
        h.Defaults.Set("commandcodeAPIKey", "cc-test-key");        // the plaintext copy earlier builds kept
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        // Not read before the first frame (:740): construction left the plaintext copy where it was.
        Assert.Equal("cc-test-key", h.Defaults.Object("commandcodeAPIKey"));

        h.Main.Loop.Run(() => model.Bootstrap(), AppModelHarness.Bound);

        Assert.Null(h.Defaults.Object("commandcodeAPIKey")); // :482
        h.Main.OnMain(() => Assert.Equal("cc-test-key", model.Settings.ApiKeyInput(ProviderKind.Commandcode)));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-041")] // App/AppModel.swift:245-247
    public void Bootstrap_has_the_login_environment_loaded()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();

        h.Bootstrap(model);

        Assert.True(LoginEnvironment.IsLoaded);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-042")] // App/AppModel.swift:247; App/AppModel+Hydra.swift:1064-1082
    public void Bootstrap_moves_a_head_whose_copy_is_gone_from_disk_back_to_the_checkout()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var gone = AppModelSamples.Thread(project) with
        {
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed, baseTree: "4b825dc642cb6eb9a060e54bf8d69288fbee4904"),
            WorktreePath = Path.Combine(h.Scratch, "copy-that-is-gone"),
            Branch = "hydra/gone",
        };
        var presentCopy = h.NewFolder("copy-on-disk");
        var present = AppModelSamples.Thread(project) with
        {
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed, baseTree: "4b825dc642cb6eb9a060e54bf8d69288fbee4904"),
            WorktreePath = presentCopy,
            Branch = "hydra/present",
        };
        h.WriteLibrary([project], [gone, present]);
        var model = h.MakeModel();

        h.Bootstrap(model);
        h.Main.Loop.RunUntil(() =>
        {
            if (model.Thread(gone.Id) is not { } current) return false;
            return current.WorktreePath is null && current.Branch is null && current.Hydra?.BaseTree is null;
        }, AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            var moved = model.Thread(gone.Id)!;
            Assert.Null(moved.WorktreePath);
            Assert.Null(moved.Branch);
            Assert.Null(moved.Hydra!.BaseTree);
            var kept = model.Thread(present.Id)!;
            Assert.Equal(presentCopy, kept.WorktreePath);
            Assert.Equal("hydra/present", kept.Branch);
            Assert.Equal("4b825dc642cb6eb9a060e54bf8d69288fbee4904", kept.Hydra!.BaseTree);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-043")] // App/AppModel+HydraMerge.swift:41-57; App/AppModel+Hydra.swift:132-134
    [Trait("Req", "PR-HYDRAMERGE-003")] // App/AppModel.swift:245-249
    public void Bootstrap_resumes_merges_by_loading_every_open_hydra_lead_updated_within_three_days()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var now = SwiftDate.Now;
        const double window = 3 * 24 * 3600;
        ChatThread Lead(double secondsAgo) => AppModelSamples.Thread(project) with { UpdatedAt = now.AddSeconds(-secondsAgo), HydraEnabled = true };
        var recent = Lead(3600);
        var justInside = Lead(window - 1);
        var atCutoff = Lead(window); // `updatedAt > cutoff` is strict
        var stale = Lead(window + 1);
        var archived = Lead(60) with { IsArchived = true };
        var hydraOff = Lead(60) with { HydraEnabled = false };
        var helper = Lead(60) with { ParentThreadID = recent.Id };
        h.WriteLibrary([project], [recent, justInside, atCutoff, stale, archived, hydraOff, helper]);
        h.Defaults.Set("hydraAutoMerge", true);
        var model = h.MakeModel();

        h.Bootstrap(model);

        h.Main.OnMain(() =>
        {
            Assert.NotNull(model.ExistingRuntime(recent.Id));
            Assert.NotNull(model.ExistingRuntime(justInside.Id));
            Assert.Null(model.ExistingRuntime(atCutoff.Id));
            Assert.Null(model.ExistingRuntime(stale.Id));
            Assert.Null(model.ExistingRuntime(archived.Id));
            Assert.Null(model.ExistingRuntime(hydraOff.Id));
            Assert.Null(model.ExistingRuntime(helper.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-043")] // App/AppModel+HydraMerge.swift:49
    public void Bootstrap_resumes_nothing_with_automatic_merge_off()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var lead = AppModelSamples.Thread(project) with { UpdatedAt = SwiftDate.Now, HydraEnabled = true };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => Assert.False(model.Settings.HydraAutoMerge)); // AppSettings.swift:763 default

        h.Bootstrap(model);

        h.Main.OnMain(() => Assert.Null(model.ExistingRuntime(lead.Id)));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-044")] // App/AppModel.swift:250-261
    [Trait("Req", "PR-HYDRAMERGE-004")] // App/AppModel.swift:254-261
    public void Bootstrap_starts_a_sweep_that_first_asks_again_300_seconds_later_and_then_every_300_seconds()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var first = AppModelSamples.Thread(project) with { UpdatedAt = SwiftDate.Now, HydraEnabled = true };
        var later = AppModelSamples.Thread(project) with { UpdatedAt = SwiftDate.Now, HydraEnabled = false };
        h.WriteLibrary([project], [first, later]);
        var model = h.MakeModel();
        h.Bootstrap(model); // automatic merge off: the launch sweep loads nothing (AppModel+HydraMerge.swift:49)
        h.Main.OnMain(() => Assert.Null(model.ExistingRuntime(first.Id)));
        h.Main.OnMain(() => model.Settings.HydraAutoMerge = true);

        h.Main.Advance(TimeSpan.FromSeconds(299));
        h.Main.OnMain(() => Assert.Null(model.ExistingRuntime(first.Id))); // it sleeps first
        h.Main.Advance(TimeSpan.FromSeconds(1));
        // The fired sweep round calls ResumeHydraMerges, which awaits Runtime(lead.Id).EnsureLoaded() for every
        // eligible lead (AppModel+HydraMerge.cs:57) before it moves on to the next one. AppModel.Runtime(id)
        // (AppModel.cs:465-466) creates and stores the ThreadRuntime synchronously, so ExistingRuntime(first.Id)
        // turns non-null the instant the sweep reaches it - waiting on that alone is satisfied before the load
        // it gates on finishes. EnsureLoaded's own load is a real TaskHandle.Detached hop (ThreadRuntime.cs:864-871,
        // R-ISO-2's @concurrent boundary): genuine ThreadPool scheduling the manual clock does not drive, so the
        // real fix is to wait for THAT to finish (IsLoadingHistory false) before moving on, not just for the
        // runtime object to exist: otherwise round 2's foreach reaches `first` again, finds its round-1 loadTask
        // still pending under real contention, and awaits that same unfinished task - which is what a bare
        // ExistingRuntime(first.Id)-is-not-null wait (an earlier version of this fix) still let happen, hitting
        // the full Bound under a loaded full-suite run despite passing 9/9 in isolation.
        h.Main.Loop.RunUntil(() => model.ExistingRuntime(first.Id) is { IsLoadingHistory: false }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.NotNull(model.ExistingRuntime(first.Id));
            Assert.Null(model.ExistingRuntime(later.Id));
        });

        // A lead that becomes eligible afterwards is picked up by the next round, 300 seconds after the last.
        h.Main.OnMain(() => model.UpdateThread(later.Id, t => t with { HydraEnabled = true }));
        h.Main.Advance(TimeSpan.FromSeconds(299));
        h.Main.OnMain(() => Assert.Null(model.ExistingRuntime(later.Id)));
        h.Main.Advance(TimeSpan.FromSeconds(1));
        h.Main.Loop.RunUntil(() => model.ExistingRuntime(later.Id) is { IsLoadingHistory: false }, AppModelHarness.Bound);
        h.Main.OnMain(() => Assert.NotNull(model.ExistingRuntime(later.Id)));
    }

    // Review fix (finding #3, HIGH): the PR-LAUNCH-085 trait this test used to carry proved nothing about the
    // record it named. AppModelHarness.PrepareBootstrap gives every CLI provider a missing custom binary path,
    // so IsInstalled reads false for both claude and codex here regardless of whether step nine's un-awaited
    // task ever ran, and this test's own assertions (old attachment gone, young one kept) never touch step
    // nine at all. Trait removed; AppModelBootstrapSweepTests now proves the installed updater sequence.
    [Fact]
    [Trait("Req", "PR-LAUNCH-045")] // App/AppModel.swift:283-286; Services/Store/Storage.swift:117-148
    [Trait("Req", "PR-STORE-072")] // App/AppModel.swift:274-287
    public void Bootstrap_ends_by_removing_attachment_files_no_thread_names_that_are_older_than_a_day()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        Directory.CreateDirectory(Storage.AttachmentsDirectory);
        var now = SwiftDate.Now;
        string Attachment(TimeSpan age)
        {
            var path = Path.Combine(Storage.AttachmentsDirectory, SwiftUUID.UuidString(Guid.NewGuid()) + ".png");
            File.WriteAllBytes(path, [1, 2, 3]);
            File.SetLastWriteTimeUtc(path, (now - age).UtcDateTime);
            return path;
        }
        var old = Attachment(TimeSpan.FromHours(25));
        var young = Attachment(TimeSpan.FromHours(23));
        var model = h.MakeModel();

        h.Bootstrap(model); // the sweep is awaited as bootstrap's tenth and last step (B3, PR-LAUNCH-045 b3_note)

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(young));
    }

    // ===== PR-LAUNCH-085: bootstrap's un-awaited CLI self-update step (B3, new) =====

    // R-TEST-10 / AppModelHarness.PrepareBootstrap gives every CLI provider a missing binary path, so neither
    // Claude nor Codex ever reads as installed in this harness: the ninth step's loop (AppModel.swift:279) skips
    // both without ProviderRegistry.UpdateCLIIfDue (or a real "claude"/"codex" process) ever being reached. That
    // leaves the installed/gated run of UpdateCLIIfDue itself to ProviderRegistryCLIUpdateTests.cs (A23), which
    // proves its gates and its run under scripted stand-ins; what this test proves at the AppModel layer is only
    // the not-installed path: step nine starts and finishes (doing nothing, here) without the step ten sweep -
    // awaited by Bootstrap() - ever being blocked by it, and that step nine never marks a provider as updating
    // when neither is installed. It cannot fail on a wrong or missing step nine, so no PR-LAUNCH-085 trait.
    //
    // The old assembly-boundary STOP is resolved: SetCLIUpdaterExecutableForTesting is now public.
    // AppModelBootstrapSweepTests uses blocked offline stand-ins to prove Claude then Codex, installed
    // gating, and completion of the orphan sweep while the first update is still held.
    // No Req trait (this no-installed-provider case alone cannot verify PR-LAUNCH-085); Rule R-TEST-10 instead, since
    // what it does verify is that this harness's not-installed path never reaches a real "claude"/"codex".
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public void Bootstrap_reaches_the_orphan_sweep_and_marks_no_provider_updating_when_no_cli_is_installed()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();

        h.Bootstrap(model);

        h.Main.OnMain(() =>
        {
            Assert.False(model.Providers.Status(ProviderKind.Claude).IsInstalled);
            Assert.False(model.Providers.Status(ProviderKind.Codex).IsInstalled);
            // The un-awaited task's loop (:279-281) never called UpdateCLIIfDue for either provider, so neither
            // was ever added to UpdatingCLIs (:510) - and Bootstrap() above already returned, proving step ten
            // (the sweep, asserted by the previous test) did not wait on step nine's task.
            Assert.Empty(model.Providers.UpdatingCLIs);
        });
    }

    // ===== PR-GIT-110: createWorktree =====

    [Fact]
    [Trait("Req", "PR-GIT-110")] // App/AppModel.swift:1117-1122
    public void Create_worktree_outside_a_repository_shows_worktree_unavailable_and_changes_nothing()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("plain"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();

        h.Main.Loop.Run(() => model.CreateWorktree(thread.Id), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal("Worktree unavailable", model.Alert!.Title);
            Assert.Equal("New worktrees need a git repository with at least one commit.", model.Alert!.Message);
            Assert.Null(model.Thread(thread.Id)!.WorktreePath);
            Assert.Null(model.Thread(thread.Id)!.Branch);
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-110")] // App/AppModel.swift:1119-1122
    public void Create_worktree_in_a_repository_with_no_commit_shows_worktree_unavailable()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.Empty(h.NewFolder("empty repo"));
        var project = AppModelSamples.Project("P", repository.Root);
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();

        h.Main.Loop.Run(() => model.CreateWorktree(thread.Id), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal("Worktree unavailable", model.Alert!.Title);
            Assert.Equal("New worktrees need a git repository with at least one commit.", model.Alert!.Message);
            Assert.Null(model.Thread(thread.Id)!.WorktreePath);
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-110")] // App/AppModel.swift:1123-1133 (DEVIATION OD-0012 F1: branch prefix marrow/)
    public void Create_worktree_names_the_folder_by_the_space_only_slug_and_the_branch_marrow_on_the_current_branch()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        repository.Git("switch", "-q", "-c", "topic");
        File.WriteAllBytes(Path.Combine(repository.Root, "b.txt"), Encoding.UTF8.GetBytes("b\n"));
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "second");
        var topicHead = repository.Git("rev-parse", "topic").TrimEnd('\n');
        // Only spaces are replaced (:1124): the dot, the underscore and the capitals are otherwise kept, then lowercased.
        var project = AppModelSamples.Project("My.App Beta_2", repository.Root);
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var suffix = SwiftString.Lowercased(SwiftUUID.UuidString(thread.Id)).Substring(0, 8);
        var expectedPath = SwiftPath.AppendingPathComponent(Storage.WorktreesDirectory, "my.app-beta_2-" + suffix);
        var expectedBranch = "marrow/" + suffix;

        h.Main.Loop.Run(() => model.CreateWorktree(thread.Id), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Null(model.Alert);
            Assert.Equal(expectedPath, model.Thread(thread.Id)!.WorktreePath);
            Assert.Equal(expectedBranch, model.Thread(thread.Id)!.Branch);
        });
        Assert.True(Directory.Exists(expectedPath));
        Assert.Equal(topicHead, repository.Git("rev-parse", expectedBranch).TrimEnd('\n')); // based on the current branch
        Assert.Equal(expectedBranch, AppModelGitRepo.At(expectedPath).Git("rev-parse", "--abbrev-ref", "HEAD").TrimEnd('\n'));
        // ChatThread.worktreePath and branch reach library.json through the debounced save.
        h.Main.Advance(TimeSpan.FromMilliseconds(400));
        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = AppModelHarness.LoadedLibrary().Threads.Single(t => t.Id == thread.Id);
        Assert.Equal(expectedPath, saved.WorktreePath);
        Assert.Equal(expectedBranch, saved.Branch);
    }

    [Fact]
    [Trait("Req", "PR-GIT-110")] // App/AppModel.swift:1129-1139
    public void Create_worktree_shows_the_git_refusal_under_could_not_create_a_worktree()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("P", repository.Root);
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var suffix = SwiftString.Lowercased(SwiftUUID.UuidString(thread.Id)).Substring(0, 8);
        repository.Git("branch", "marrow/" + suffix); // `worktree add -b` refuses a branch that exists
        var model = h.MakeModel();

        h.Main.Loop.Run(() => model.CreateWorktree(thread.Id), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            Assert.Equal("Could not create a worktree", model.Alert!.Title);
            Assert.False(string.IsNullOrEmpty(model.Alert!.Message));
            Assert.Null(model.Thread(thread.Id)!.WorktreePath);
            Assert.Null(model.Thread(thread.Id)!.Branch);
        });
    }

    // ===== PR-GIT-063: deleting one thread =====

    [Fact]
    [Trait("Req", "PR-GIT-063")] // App/AppModel.swift:946-956
    public void Delete_with_the_worktree_chosen_deletes_the_checkpoints_and_removes_the_worktree()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var worktree = Path.Combine(h.Scratch, "wt-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(worktree, "feature");
        var project = AppModelSamples.Project("P", repository.Root);
        var thread = AppModelSamples.Thread(project) with { WorktreePath = worktree, Branch = "feature" };
        var kept = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread, kept]);
        repository.AddCheckpoint(thread.Id);
        repository.AddCheckpoint(kept.Id);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Delete(thread.Id, removeWorktree: true));

        h.Main.OnMain(() => Assert.Null(model.Thread(thread.Id)));
        h.Main.Loop.RunUntil(() => !Directory.Exists(worktree), AppModelHarness.Bound);
        Assert.Empty(repository.Checkpoints(thread.Id)); // deleted before the worktree went
        Assert.Single(repository.Checkpoints(kept.Id));
        // saveLibrary() right away (:956), no debounce.
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(kept.Id, Assert.Single(AppModelHarness.LoadedLibrary().Threads).Id);
    }

    [Fact]
    [Trait("Req", "PR-GIT-063")] // App/AppModel.swift:950-953
    public void Delete_without_the_worktree_chosen_deletes_the_checkpoints_and_leaves_the_worktree()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var worktree = Path.Combine(h.Scratch, "wt-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(worktree, "feature");
        var project = AppModelSamples.Project("P", repository.Root);
        var thread = AppModelSamples.Thread(project) with { WorktreePath = worktree, Branch = "feature" };
        h.WriteLibrary([project], [thread]);
        repository.AddCheckpoint(thread.Id);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Delete(thread.Id));

        h.Main.Loop.RunUntil(() => repository.Checkpoints(thread.Id).Count == 0, AppModelHarness.Bound);
        h.Main.RunUntilIdle();
        Assert.True(Directory.Exists(worktree));
    }

    [Fact]
    [Trait("Req", "PR-GIT-063")] // App/AppModel.swift:950-952
    public void Delete_of_a_head_that_owned_its_copy_removes_the_copy_even_unasked()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var copy = Path.Combine(h.Scratch, "copy-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(copy, "hydra-copy");
        var project = AppModelSamples.Project("P", repository.Root);
        var head = AppModelSamples.Thread(project) with
        {
            WorktreePath = copy,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed, baseTree: "4b825dc642cb6eb9a060e54bf8d69288fbee4904"),
        };
        h.WriteLibrary([project], [head]);
        repository.AddCheckpoint(head.Id);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Delete(head.Id));

        h.Main.Loop.RunUntil(() => !Directory.Exists(copy), AppModelHarness.Bound);
        Assert.Empty(repository.Checkpoints(head.Id));
    }

    [Fact]
    [Trait("Req", "PR-GIT-063")] // App/AppModel.swift:941-942
    public void Delete_takes_the_helpers_first_each_without_its_worktree()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var parentTree = Path.Combine(h.Scratch, "parent-" + Guid.NewGuid().ToString("N")[..8]);
        var helperTree = Path.Combine(h.Scratch, "helper-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(parentTree, "parent-branch");
        repository.AddWorktree(helperTree, "helper-branch");
        var project = AppModelSamples.Project("P", repository.Root);
        var parent = AppModelSamples.Thread(project) with { WorktreePath = parentTree };
        var helper = AppModelSamples.Thread(project) with { WorktreePath = helperTree, ParentThreadID = parent.Id, IsInPanel = true };
        h.WriteLibrary([project], [parent, helper]);
        repository.AddCheckpoint(parent.Id);
        repository.AddCheckpoint(helper.Id);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Delete(parent.Id, removeWorktree: true));

        h.Main.OnMain(() =>
        {
            Assert.Null(model.Thread(parent.Id));
            Assert.Null(model.Thread(helper.Id));
        });
        h.Main.Loop.RunUntil(() => !Directory.Exists(parentTree) && repository.Checkpoints(helper.Id).Count == 0, AppModelHarness.Bound);
        h.Main.RunUntilIdle();
        Assert.Empty(repository.Checkpoints(parent.Id));
        Assert.True(Directory.Exists(helperTree));
    }

    [Fact]
    [Trait("Req", "PR-GIT-063")] // App/AppModel.swift:947
    public void Delete_of_a_thread_whose_project_is_gone_leaves_its_checkpoints()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("P", repository.Root);
        var orphan = AppModelSamples.Thread(project);
        h.WriteLibrary([], [orphan]); // its project is not in the library
        repository.AddCheckpoint(orphan.Id);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Delete(orphan.Id));

        h.Main.RunUntilIdle();
        h.Main.OnMain(() => Assert.Null(model.Thread(orphan.Id)));
        Assert.Single(repository.Checkpoints(orphan.Id));
    }

    // ===== PR-GIT-064: deleting every archived thread =====

    [Fact]
    [Trait("Req", "PR-GIT-064")] // App/AppModel.swift:957-993
    public void Delete_archived_threads_deletes_their_checkpoints_and_only_the_copies_heads_owned()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var userTree = Path.Combine(h.Scratch, "user-" + Guid.NewGuid().ToString("N")[..8]);
        var headCopy = Path.Combine(h.Scratch, "copy-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(userTree, "user-branch");
        repository.AddWorktree(headCopy, "copy-branch");
        var project = AppModelSamples.Project("P", repository.Root);
        var archived = AppModelSamples.Thread(project) with { IsArchived = true, WorktreePath = userTree };
        var archivedHead = AppModelSamples.Thread(project) with
        {
            IsArchived = true,
            WorktreePath = headCopy,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed, baseTree: "4b825dc642cb6eb9a060e54bf8d69288fbee4904"),
        };
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = archived.Id, IsInPanel = true };
        var open = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [archived, archivedHead, helper, open]);
        foreach (var thread in new[] { archived, archivedHead, helper, open })
        {
            repository.AddCheckpoint(thread.Id);
            AppModelHarness.WriteDocument(thread.Id);
        }
        var model = h.MakeModel();

        h.Main.OnMain(model.DeleteArchivedThreads);

        // The documents go at once (Storage.deleteDocuments, :979); the open thread's stays.
        Assert.False(File.Exists(Storage.ThreadURL(archived.Id)));
        Assert.False(File.Exists(Storage.ThreadURL(archivedHead.Id)));
        Assert.False(File.Exists(Storage.ThreadURL(helper.Id)));
        Assert.True(File.Exists(Storage.ThreadURL(open.Id)));
        h.Main.OnMain(() => Assert.Equal(open.Id, Assert.Single(model.Threads).Id));
        h.Main.Loop.RunUntil(
            () => !Directory.Exists(headCopy)
                  && repository.Checkpoints(archived.Id).Count == 0
                  && repository.Checkpoints(archivedHead.Id).Count == 0
                  && repository.Checkpoints(helper.Id).Count == 0,
            AppModelHarness.Bound);
        h.Main.RunUntilIdle();
        Assert.True(Directory.Exists(userTree)); // a user's own worktree stays on disk
        Assert.Single(repository.Checkpoints(open.Id));
        // Saved on the 400 ms schedule (:992).
        h.Main.Advance(TimeSpan.FromMilliseconds(400));
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(open.Id, Assert.Single(AppModelHarness.LoadedLibrary().Threads).Id);
    }
}
