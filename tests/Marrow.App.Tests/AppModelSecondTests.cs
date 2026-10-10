using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B2 1fbf81e), part 2 of 3. Values and ordering below come from the
// packet's exact records and cited B2 excerpts. Each test uses AppModelHarness so storage, defaults and time are
// throwaway and deterministic.
public sealed class AppModelSecondTests
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-078")] // App/AppModel.swift:72-98
    public void Reassigning_the_selected_thread_restores_its_project_as_the_last_project()
    {
        using var h = new AppModelHarness();
        var firstProject = AppModelSamples.Project("First", h.NewFolder("first"));
        var otherProject = AppModelSamples.Project("Other", h.NewFolder("other"));
        var thread = AppModelSamples.Thread(firstProject);
        h.WriteLibrary([firstProject, otherProject], [thread]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = thread.Id;
            model.Settings.LastProjectID = otherProject.Id;
            model.SelectedThreadID = thread.Id;

            Assert.Equal(firstProject.Id, model.Settings.LastProjectID);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-081")] // App/AppModel.swift:1100-1114
    public void Thread_selection_shortcuts_wrap_offsets_and_ignore_out_of_range_numbers()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("selection shortcuts"));
        var first = AppModelSamples.Thread(project) with { CreatedAt = SwiftDate.Now.AddMinutes(-2) };
        var second = AppModelSamples.Thread(project) with { CreatedAt = SwiftDate.Now.AddMinutes(-1) };
        var third = AppModelSamples.Thread(project) with { CreatedAt = SwiftDate.Now };
        h.WriteLibrary([project], [first, second, third]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.SelectThread(0);
            Assert.Equal(third.Id, model.SelectedThreadID);
            model.SelectThread(-1);
            Assert.Equal(first.Id, model.SelectedThreadID);
            model.SelectThreadNumber(3);
            Assert.Equal(first.Id, model.SelectedThreadID);
            model.SelectThreadNumber(4);
            Assert.Equal(first.Id, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-053")] // App/AppModel.swift:1003-1005, :417-419
    public void Subagent_lookup_returns_the_parent_panel_subagent()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("panel subagent"));
        var parent = AppModelSamples.Thread(project);
        var panel = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = true };
        var archivedPanel = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = true, IsArchived = true };
        h.WriteLibrary([project], [parent, panel, archivedPanel]);
        var model = h.MakeModel();

        h.Main.OnMain(() => Assert.Equal(panel.Id, model.Subagent(parent.Id)?.Id));
    }

    // Review fix (finding #1, HIGH): this test used to encode the B2 cancel-and-restart 400 ms debounce
    // (App/AppModel.swift:1289-1306 at 1fbf81e), where the second change at t=250 would cancel the pending
    // task and restart the wait, landing the write at t=650. B3's scheduleSave (App/AppModel.swift:1304-1314)
    // is a throttle, not a debounce: `guard persistenceEnabled, saveTask == nil else { return }` means a
    // change while a save is already pending is a complete no-op, so the burst writes once, 400 ms after the
    // FIRST change of the burst, carrying whatever value is current in memory at that moment (the second
    // change's, since it already landed on `projects` even though it scheduled nothing). Re-derived from B3,
    // not weakened: this is the same 400 ms literal and the same two-change burst shape as the retired B2
    // test, with the timing and the resulting value corrected to match scheduleSave's actual B3 behavior.
    [Fact]
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:1304-1314; Services/Store/Storage.swift:345-356
    public void Library_save_throttle_writes_once_400ms_after_the_first_change_of_a_burst_carrying_the_last_value()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var original = AppModelHarness.LibraryBytes();
        var model = h.MakeModel();

        h.Main.OnMain(() => model.UpdateProject(project.Id, value => value with { Name = "First" }));
        h.Main.AdvanceMilliseconds(250);
        // The change at t=250 lands on `projects` immediately but schedules nothing new: saveTask is already
        // non-null from the t=0 change, so scheduleSave's guard returns early (:1305), unlike the B2
        // cancel-and-restart debounce this test used to encode.
        h.Main.OnMain(() => model.UpdateProject(project.Id, value => value with { Name = "Last" }));
        h.Main.AdvanceMilliseconds(149); // t=399: one ms short of 400 ms since the FIRST change
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(original, AppModelHarness.LibraryBytes());

        h.Main.AdvanceMilliseconds(1); // t=400: the task scheduled at t=0 fires and saves the current state
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal("Last", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    [Fact]
    [Trait("Req", "PR-STORE-015")] // App/AppModel.swift:1289-1306; Services/Store/Storage.swift:60-63,345-356
    // Owner rulings exercised: OD-0008 A10 and OD-0012 E4.
    public void A_failed_library_decode_is_not_replaced_by_a_debounced_save()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var unreadable = System.Text.Encoding.UTF8.GetBytes("{not a library}");
        File.WriteAllBytes(Storage.LibraryURL, unreadable);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.ScheduleSave());
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();

        Assert.Equal(unreadable, File.ReadAllBytes(Storage.LibraryURL));
        h.Main.OnMain(() => Assert.Equal(
            "nth could not read library.json, so your projects and threads are hidden. The file will not be changed until it can be read.",
            model.Alert!.Message));
    }

    [Fact]
    [Trait("Req", "PR-STORE-016")] // App/AppModel.swift:1308-1326
    // Owner rulings exercised: OD-0008 A10 and OD-0012 E4.
    public void Quit_does_not_replace_a_library_that_failed_to_decode()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var unreadable = System.Text.Encoding.UTF8.GetBytes("not JSON");
        File.WriteAllBytes(Storage.LibraryURL, unreadable);
        var model = h.MakeModel();

        h.Main.OnMain(model.SaveBeforeQuit);

        Assert.Equal(unreadable, File.ReadAllBytes(Storage.LibraryURL));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-076")] // App/AppModel.swift:1289-1297
    public void A_change_newer_than_the_last_debounced_save_is_absent_after_a_crash_reload()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        h.WriteLibrary([project], []);
        var firstProcess = h.MakeModel();

        h.Main.OnMain(() => firstProcess.UpdateProject(project.Id, value => value with { Name = "Quiet" }));
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        h.Main.OnMain(() => firstProcess.UpdateProject(project.Id, value => value with { Name = "Too recent" }));

        // A new model reads the last disk snapshot without calling the graceful quit path.
        var relaunched = h.MakeModel();
        h.Main.OnMain(() => Assert.Equal("Quiet", relaunched.Projects.Single().Name));
        Assert.Equal("Quiet", Assert.Single(AppModelHarness.LoadedLibrary().Projects).Name);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-061")] // App/AppModel.swift:470-488
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:490-506
    [Trait("Req", "PR-STORE-029")] // App/AppModel.swift:500-505; Services/Store/Storage.swift:74
    public void An_idle_runtime_waits_600_seconds_rechecks_a_running_turn_and_saves_before_release()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var leaving = AppModelSamples.Thread(project);
        var selected = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [leaving, selected]);
        var savedFollowUp = new FollowUpPrompt { Text = "Saved before release" };
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(leaving.Id)
            {
                FollowUps = new ValueList<FollowUpPrompt>([savedFollowUp]),
            },
            Storage.ThreadURL(leaving.Id));
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(leaving.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        // A usage event dirties the loaded document without scheduling its own save (PR-RUNTIME-093).
        // The release path must enqueue that revision after the backing file is removed.
        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens = 321, WindowTokens = 1_024 })));
        File.Delete(Storage.ThreadURL(leaving.Id));

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = leaving.Id;
            runtime.Rehearse(new ProviderEvent.TurnStarted(null));
            model.SelectedThreadID = selected.Id;
        });
        h.Main.Advance(TimeSpan.FromSeconds(600));
        h.Main.OnMain(() => Assert.Same(runtime, model.ExistingRuntime(leaving.Id)));

        h.Main.Advance(TimeSpan.FromSeconds(599));
        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null)));
        h.Main.Advance(TimeSpan.FromSeconds(1));
        h.Main.OnMain(() => Assert.Null(model.ExistingRuntime(leaving.Id)));

        // A read blocks behind the release save and sees the dirty runtime snapshot after the file was removed.
        var persisted = Storage.ReadDocument(leaving.Id)!.Value.Document;
        Assert.Equal("Saved before release", Assert.Single(persisted.FollowUps).Text);
        Assert.Equal(321, persisted.Usage!.UsedTokens);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-061")] // App/AppModel.swift:470-488
    public void Reselecting_a_thread_cancels_its_pending_600_second_stop()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var leaving = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [leaving, other]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(leaving.Id));

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = leaving.Id;
            model.SelectedThreadID = other.Id;
        });
        h.Main.Advance(TimeSpan.FromSeconds(599));
        h.Main.OnMain(() => model.SelectedThreadID = leaving.Id);
        h.Main.Advance(TimeSpan.FromSeconds(600));

        h.Main.OnMain(() => Assert.Same(runtime, model.ExistingRuntime(leaving.Id)));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:490-498
    public void An_idle_runtime_is_retained_when_selected_or_shown_under_the_selected_parent()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project);
        var child = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsInPanel = true };
        h.WriteLibrary([project], [parent, child]);
        var model = h.MakeModel();
        var parentRuntime = h.Main.OnMain(() => model.Runtime(parent.Id));
        var childRuntime = h.Main.OnMain(() => model.Runtime(child.Id));
        h.Main.OnMain(() => model.SelectedThreadID = parent.Id);

        h.Main.OnMain(() =>
        {
            Assert.False(model.ReleaseIdleRuntime(parent.Id));
            Assert.False(model.ReleaseIdleRuntime(child.Id));
            Assert.Same(parentRuntime, model.ExistingRuntime(parent.Id));
            Assert.Same(childRuntime, model.ExistingRuntime(child.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:494-498
    public void An_idle_runtime_with_an_unsent_composer_draft_is_not_released()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "Unsent" });

        h.Main.OnMain(() => Assert.False(model.ReleaseIdleRuntime(thread.Id)));
        h.Main.OnMain(() => Assert.Same(runtime, model.ExistingRuntime(thread.Id)));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:494-498
    public void An_idle_runtime_with_a_pending_auto_continue_is_not_released()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));

        h.Main.OnMain(() =>
        {
            model.Settings.AutoContinueAfterLimit = true;
            model.AutoContinue.NoteLimit(thread.Id, SwiftDate.Now.AddHours(1));
            Assert.True(model.AutoContinue.TurnFinished(thread.Id, TurnStatus.Failed, continues: false));
        });
        h.Main.OnMain(() =>
        {
            Assert.True(model.AutoContinue.ResumesAt.ContainsKey(thread.Id));
            Assert.False(model.ReleaseIdleRuntime(thread.Id));
            Assert.Same(runtime, model.ExistingRuntime(thread.Id));
            model.AutoContinue.Cancel(thread.Id);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:490-506, runningDroppyHeads(of:)
    public void An_idle_runtime_is_retained_while_a_delegated_head_is_still_running()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project);
        var runningHead = AppModelSamples.Thread(project) with
        {
            ParentThreadID = parent.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        h.WriteLibrary([project], [parent, runningHead]);
        AppModelHarness.WriteDocument(parent.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(parent.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        // AppModel startup marks persisted running heads stopped; restore a live state after initialization.
        h.Main.OnMain(() => model.UpdateThread(runningHead.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
        }));

        h.Main.OnMain(() =>
        {
            Assert.Equal(1L, model.RunningDroppyHeads(parent.Id));
            Assert.False(model.ReleaseIdleRuntime(parent.Id));
            Assert.Same(runtime, model.ExistingRuntime(parent.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // AppModel.swift:490-506, runningDroppyHeads(of:)
    public void A_running_native_head_does_not_count_as_a_running_droppy_head_for_idle_release()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project);
        var runningNativeHead = AppModelSamples.Thread(project) with
        {
            ParentThreadID = parent.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with
            {
                HydraHeadInfoKind = HydraHeadInfo.Kind.Native,
            },
        };
        h.WriteLibrary([project], [parent, runningNativeHead]);
        AppModelHarness.WriteDocument(parent.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(parent.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => model.UpdateThread(runningNativeHead.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
        }));

        h.Main.OnMain(() =>
        {
            Assert.Equal(1L, model.RunningHydraHeads(parent.Id));
            Assert.Equal(0L, model.RunningDroppyHeads(parent.Id));
            Assert.True(model.ReleaseIdleRuntime(parent.Id));
            Assert.Null(model.ExistingRuntime(parent.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-077")] // App/AppModel.swift:79-87,1226-1238
    [Trait("Req", "PR-LAUNCH-078")] // App/AppModel.swift:88-96,306-309,449-463
    public void Selecting_a_thread_reads_its_children_remembers_the_project_and_warms_its_neighbours()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var previousProject = AppModelSamples.Project("Previous", h.NewFolder("previous"));
        var baseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var previous = AppModelSamples.Thread(project) with { CreatedAt = baseDate.AddMinutes(1) };
        var selected = AppModelSamples.Thread(project) with
        {
            CreatedAt = baseDate,
            HasUnread = true,
            SortOrder = 0,
        };
        var next = AppModelSamples.Thread(project) with { CreatedAt = baseDate.AddMinutes(-1), SortOrder = 1 };
        var panelChild = AppModelSamples.Thread(project) with
        {
            ParentThreadID = selected.Id,
            IsInPanel = true,
            HasUnread = true,
        };
        var previousProjectThread = AppModelSamples.Thread(previousProject);
        h.WriteLibrary([project, previousProject], [previous, selected, next, panelChild, previousProjectThread]);
        AppModelHarness.WriteDocument(previous.Id);
        AppModelHarness.WriteDocument(next.Id);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(previousProject.Id));
        var model = h.MakeModel();
        h.Defaults.Writes.Clear();
        var libraryBeforeSelection = AppModelHarness.LibraryBytes();

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = selected.Id;
            Assert.True(model.Thread(selected.Id)!.HasUnread);
            Assert.True(model.Thread(panelChild.Id)!.HasUnread);
            Assert.Equal(libraryBeforeSelection, AppModelHarness.LibraryBytes());
        });
        h.Main.OnMain(() =>
        {
            Assert.False(model.Thread(selected.Id)!.HasUnread);
            Assert.False(model.Thread(panelChild.Id)!.HasUnread);
            Assert.Equal(project.Id, model.Settings.LastProjectID);
            Assert.True(model.Thread(previous.Id) is not null);
            Assert.True(model.Thread(next.Id) is not null);
        });
        Assert.Contains("lastProjectID", h.Defaults.Writes);
        AppModelHarness.WaitForPrefetch();
        Assert.NotNull(DocumentPrefetch.Shared.Take(previous.Id));
        Assert.NotNull(DocumentPrefetch.Shared.Take(next.Id));
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = AppModelHarness.LoadedLibrary();
        Assert.False(saved.Threads.Single(thread => thread.Id == selected.Id).HasUnread);
        Assert.False(saved.Threads.Single(thread => thread.Id == panelChild.Id).HasUnread);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-027")] // App/AppModel.swift:314-333,377-380
    public void Project_threads_apply_open_settled_pin_place_creation_and_top_level_rules_in_order()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pinnedUnplaced = AppModelSamples.Thread(project) with { IsPinned = true, CreatedAt = at };
        var pinnedPlaced = AppModelSamples.Thread(project) with { IsPinned = true, SortOrder = 0, CreatedAt = at.AddMinutes(90) };
        var pinnedLater = AppModelSamples.Thread(project) with { IsPinned = true, SortOrder = 2, CreatedAt = at.AddMinutes(70) };
        var unplacedNew = AppModelSamples.Thread(project) with { CreatedAt = at.AddMinutes(60) };
        var unplacedOld = AppModelSamples.Thread(project) with { CreatedAt = at.AddMinutes(50) };
        var placedFirst = AppModelSamples.Thread(project) with { SortOrder = 0, CreatedAt = at.AddMinutes(99) };
        var placedLast = AppModelSamples.Thread(project) with { SortOrder = 2, CreatedAt = at.AddMinutes(30) };
        var child = AppModelSamples.Thread(project) with { ParentThreadID = unplacedNew.Id, CreatedAt = at.AddMinutes(200) };
        var orphan = AppModelSamples.Thread(project) with { ParentThreadID = Guid.NewGuid(), CreatedAt = at.AddMinutes(100) };
        var archived = AppModelSamples.Thread(project) with { IsArchived = true, CreatedAt = at.AddMinutes(500) };
        var panelled = AppModelSamples.Thread(project) with { IsInPanel = true, CreatedAt = at.AddMinutes(400) };
        var settledFallback = AppModelSamples.Thread(project) with
        {
            IsSettled = true,
            UpdatedAt = at.AddMinutes(500),
            SettledAt = null,
        };
        var settledRecent = AppModelSamples.Thread(project) with
        {
            IsSettled = true,
            UpdatedAt = at,
            SettledAt = at.AddMinutes(400),
        };
        var settledOld = AppModelSamples.Thread(project) with
        {
            IsSettled = true,
            SettledAt = at.AddMinutes(300),
        };
        h.WriteLibrary([project],
        [
            pinnedUnplaced, pinnedPlaced, pinnedLater, unplacedNew, unplacedOld, placedFirst, placedLast,
            child, orphan, archived, panelled, settledFallback, settledRecent, settledOld,
        ]);
        var model = h.MakeModel();

        var ids = h.Main.OnMain(() => model.ThreadsIn(project).Select(thread => thread.Id).ToArray());

        Assert.Equal(
        [
            pinnedUnplaced.Id, pinnedPlaced.Id, pinnedLater.Id,
            orphan.Id, unplacedNew.Id, unplacedOld.Id, placedFirst.Id, placedLast.Id,
            settledFallback.Id, settledRecent.Id, settledOld.Id,
        ], ids);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-059")] // App/AppModel.swift:337-352
    public void Project_reorder_rewrites_each_open_place_and_inherits_the_targets_pin()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var target = AppModelSamples.Thread(project) with { IsPinned = true, SortOrder = 0 };
        var moving = AppModelSamples.Thread(project) with { SortOrder = 1 };
        var tail = AppModelSamples.Thread(project) with { SortOrder = 2 };
        var settled = AppModelSamples.Thread(project) with { IsSettled = true, SortOrder = 50 };
        h.WriteLibrary([project], [target, moving, tail, settled]);
        var model = h.MakeModel();

        var changed = h.Main.OnMain(() => model.MoveThread(moving.Id, target.Id, placeAfter: false));

        Assert.True(changed);
        h.Main.OnMain(() =>
        {
            Assert.True(model.Thread(moving.Id)!.IsPinned);
            Assert.Equal(0d, model.Thread(moving.Id)!.SortOrder);
            Assert.Equal(1d, model.Thread(target.Id)!.SortOrder);
            Assert.Equal(2d, model.Thread(tail.Id)!.SortOrder);
            Assert.Equal(50d, model.Thread(settled.Id)!.SortOrder);
            Assert.False(model.MoveThread(moving.Id, target.Id, placeAfter: false));
            Assert.False(model.MoveThread(moving.Id, settled.Id, placeAfter: false));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-060")] // App/AppModel.swift:356-370
    public void Activity_reorder_places_each_peer_and_uses_each_threads_own_local_updated_day()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var first = AppModelSamples.Thread(project) with
        {
            IsPinned = true,
            UpdatedAt = new DateTimeOffset(2026, 4, 3, 1, 0, 0, TimeSpan.Zero),
        };
        var second = AppModelSamples.Thread(project) with
        {
            UpdatedAt = new DateTimeOffset(2026, 4, 4, 21, 0, 0, TimeSpan.Zero),
        };
        var moving = AppModelSamples.Thread(project) with
        {
            UpdatedAt = new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero),
        };
        var outside = AppModelSamples.Thread(project) with { ActivityOrder = 99 };
        h.WriteLibrary([project], [first, second, moving, outside]);
        var model = h.MakeModel();

        var changed = h.Main.OnMain(() => model.MoveInActivity(
            moving.Id, first.Id, placeAfter: false, [first.Id, second.Id, moving.Id]));

        Assert.True(changed);
        h.Main.OnMain(() =>
        {
            Assert.Equal(0d, model.Thread(moving.Id)!.ActivityOrder);
            Assert.Equal(1d, model.Thread(first.Id)!.ActivityOrder);
            Assert.Equal(2d, model.Thread(second.Id)!.ActivityOrder);
            Assert.Equal(LocalStartOfDay(moving.UpdatedAt), model.Thread(moving.Id)!.ActivityOrderDay);
            Assert.Equal(LocalStartOfDay(first.UpdatedAt), model.Thread(first.Id)!.ActivityOrderDay);
            Assert.Equal(LocalStartOfDay(second.UpdatedAt), model.Thread(second.Id)!.ActivityOrderDay);
            Assert.True(model.Thread(first.Id)!.IsPinned);
            Assert.Equal(99d, model.Thread(outside.Id)!.ActivityOrder);
            Assert.False(model.MoveInActivity(moving.Id, first.Id, placeAfter: false, [moving.Id, first.Id, second.Id]));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-081")] // App/AppModel.swift:622-637
    [Trait("Req", "PR-LAUNCH-082")] // App/AppModel.swift:625-645
    public void Forwarding_keeps_the_exact_continued_prefix_custom_title_and_project_state()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p")) with { IsExpanded = false };
        var source = AppModelSamples.Thread(project) with
        {
            Title = "Continued: Source title",
            HasCustomTitle = true,
            Model = "source-model",
            Effort = "high",
        };
        h.WriteLibrary([project], [source]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(source.Id)
            {
                Items = new ValueList<TimelineItem>(
                [
                    new TimelineItem(
                        turnID: null,
                        content: new TimelineItem.Content.User(new UserMessage { Text = "Earlier question" })),
                ]),
            },
            Storage.ThreadURL(source.Id));
        var before = AppModelHarness.LibraryBytes();
        var model = h.MakeModel();

        var forwarded = h.Main.Loop.Run(() => model.ForwardThread(source.Id), AppModelHarness.Bound);

        Assert.NotNull(forwarded);
        Assert.Equal("Continued: Continued: Source title", forwarded!.Title);
        Assert.True(forwarded.HasCustomTitle);
        Assert.Equal(source.Provider, forwarded.Provider);
        Assert.Equal(source.Model, forwarded.Model);
        Assert.Equal(source.Effort, forwarded.Effort);
        h.Main.OnMain(() =>
        {
            Assert.Equal(forwarded.Id, model.SelectedThreadID);
            Assert.True(model.Project(project.Id)!.IsExpanded);
            Assert.Equal(project.Id, model.Settings.LastProjectID);
            Assert.Equal(source.Id, model.Runtime(forwarded.Id).Continuation!.SourceThreadID);
            Assert.Equal(source.Title, model.Runtime(forwarded.Id).Continuation!.SourceTitle);
            Assert.Equal("User: Earlier question", model.Runtime(forwarded.Id).Continuation!.Context);
        });
        h.Main.AdvanceMilliseconds(399);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Equal(before, AppModelHarness.LibraryBytes());
        h.Main.AdvanceMilliseconds(1);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.Contains(AppModelHarness.LoadedLibrary().Threads, thread => thread.Id == forwarded.Id);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-082")] // App/AppModel.swift:625-629
    public void Forwarding_a_missing_source_returns_without_mutating_the_library_or_selection()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var existing = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [existing]);
        var original = AppModelHarness.LibraryBytes();
        var model = h.MakeModel();

        var forwarded = h.Main.Loop.Run(() => model.ForwardThread(Guid.NewGuid()), AppModelHarness.Bound);

        Assert.Null(forwarded);
        h.Main.OnMain(() =>
        {
            Assert.Null(model.SelectedThreadID);
            Assert.Equal(existing.Id, Assert.Single(model.Threads).Id);
        });
        Assert.Equal(original, AppModelHarness.LibraryBytes());
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-092")] // App/AppModel.swift:748-755; Core/Support/Text.swift:132
    public void Rename_trims_whitespace_and_newlines_rejects_empty_text_and_marks_a_custom_title()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project) with { Title = "Original" };
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Rename(thread.Id, " \t  Renamed  \r\n"));
        h.Main.OnMain(() =>
        {
            Assert.Equal("Renamed", model.Thread(thread.Id)!.Title);
            Assert.True(model.Thread(thread.Id)!.HasCustomTitle);
            model.Rename(thread.Id, " \t\r\n  ");
            Assert.Equal("Renamed", model.Thread(thread.Id)!.Title);
            Assert.True(model.Thread(thread.Id)!.HasCustomTitle);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-112")] // App/AppModel.swift:421-423; Core/Models/Library.swift:150-160,250
    public void Folding_toggles_the_persisted_expands_helpers_field_from_its_folded_default()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();

        h.Main.OnMain(() => Assert.True(model.Thread(thread.Id)!.FoldsHelpers));
        h.Main.OnMain(() => model.ToggleHelpersFold(thread.Id));
        h.Main.OnMain(() =>
        {
            Assert.True(model.Thread(thread.Id)!.ExpandsHelpers);
            Assert.False(model.Thread(thread.Id)!.FoldsHelpers);
        });
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.True(Assert.Single(AppModelHarness.LoadedLibrary().Threads).ExpandsHelpers);
        h.Main.OnMain(() => model.ToggleHelpersFold(thread.Id));
        h.Main.OnMain(() => Assert.False(model.Thread(thread.Id)!.ExpandsHelpers));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-080")] // App/AppModel.swift:786-807
    public void Settling_clears_the_threads_pin_and_unread_mark_and_persists_helpers_with_one_timestamp()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var parent = AppModelSamples.Thread(project) with { IsPinned = true, HasUnread = true };
        var helper = AppModelSamples.Thread(project) with
        {
            ParentThreadID = parent.Id,
            IsPinned = true,
            HasUnread = true,
        };
        h.WriteLibrary([project], [parent, helper]);
        var model = h.MakeModel();
        h.Main.AdvanceMilliseconds(50);
        var settledAt = h.Main.OnMain(() => SwiftDate.Now);

        h.Main.OnMain(() => model.Settle(parent.Id, sounds: false));
        h.Main.OnMain(() =>
        {
            var changed = model.Thread(parent.Id)!;
            Assert.True(changed.IsSettled);
            Assert.Equal(settledAt, changed.SettledAt);
            Assert.False(changed.IsPinned);
            Assert.False(changed.HasUnread);
            var changedHelper = model.Thread(helper.Id)!;
            Assert.True(changedHelper.IsSettled);
            Assert.Equal(settledAt, changedHelper.SettledAt);
            Assert.True(changedHelper.IsPinned);
            Assert.True(changedHelper.HasUnread);
        });
        DiskWriter.Shared.WaitForQueuedWrites();
        var saved = AppModelHarness.LoadedLibrary();
        var savedParent = saved.Threads.Single(value => value.Id == parent.Id);
        var savedHelper = saved.Threads.Single(value => value.Id == helper.Id);
        Assert.True(savedParent.IsSettled);
        Assert.True(savedHelper.IsSettled);
        var encodedTimestamp = FoundationISO8601.StorageDate(FoundationISO8601.Format(settledAt));
        Assert.NotNull(encodedTimestamp);
        Assert.Equal(encodedTimestamp, savedParent.SettledAt);
        Assert.Equal(savedParent.SettledAt, savedHelper.SettledAt);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-082")] // App/AppModel.swift:810-826
    public void Reopening_clears_settled_activity_place_and_every_settled_child_timestamp()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var old = new DateTimeOffset(2025, 12, 1, 8, 0, 0, TimeSpan.Zero);
        var settledAt = old.AddHours(1);
        var parent = AppModelSamples.Thread(project) with
        {
            IsSettled = true,
            SettledAt = settledAt,
            UpdatedAt = old,
            ActivityOrder = 3,
            ActivityOrderDay = old.Date,
        };
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsSettled = true, SettledAt = settledAt };
        var panelledChild = AppModelSamples.Thread(project) with
        {
            ParentThreadID = parent.Id,
            IsInPanel = true,
            IsSettled = true,
            SettledAt = settledAt,
            UpdatedAt = old.AddMinutes(2),
        };
        h.WriteLibrary([project], [parent, helper, panelledChild]);
        var model = h.MakeModel();
        h.Main.AdvanceMilliseconds(100);
        var reopenedAt = h.Main.OnMain(() => SwiftDate.Now);

        h.Main.OnMain(() => model.Reopen(parent.Id));
        h.Main.OnMain(() =>
        {
            var reopened = model.Thread(parent.Id)!;
            Assert.False(reopened.IsSettled);
            Assert.Null(reopened.SettledAt);
            Assert.Equal(reopenedAt, reopened.UpdatedAt);
            Assert.Null(reopened.ActivityOrder);
            Assert.Null(reopened.ActivityOrderDay);
            Assert.False(model.Thread(helper.Id)!.IsSettled);
            Assert.Null(model.Thread(helper.Id)!.SettledAt);
            Assert.False(model.Thread(panelledChild.Id)!.IsSettled);
            Assert.Null(model.Thread(panelledChild.Id)!.SettledAt);
            Assert.Equal(old.AddMinutes(2), model.Thread(panelledChild.Id)!.UpdatedAt);
        });
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.False(AppModelHarness.LoadedLibrary().Threads.Single(value => value.Id == parent.Id).IsSettled);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-085")] // App/AppModel.swift:756-772
    public void Archiving_recursively_archives_helpers_clears_pin_and_settled_state_and_selects_a_neighbour()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = AppModelSamples.Thread(project) with
        {
            CreatedAt = at.AddMinutes(2),
            IsPinned = true,
            IsSettled = true,
            SettledAt = at,
            HasUnread = true,
        };
        var helper = AppModelSamples.Thread(project) with
        {
            ParentThreadID = parent.Id,
            CreatedAt = at.AddMinutes(1),
            IsPinned = true,
            IsSettled = true,
            SettledAt = at,
            HasUnread = true,
        };
        var neighbour = AppModelSamples.Thread(project) with { CreatedAt = at };
        h.WriteLibrary([project], [parent, helper, neighbour]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.SelectedThreadID = parent.Id);
        h.Main.RunUntilIdle(); // Selecting reads the parent and helpers before this test restores their unread state.
        h.Main.OnMain(() =>
        {
            model.UpdateThread(parent.Id, thread => thread with { HasUnread = true });
            model.UpdateThread(helper.Id, thread => thread with { HasUnread = true });
        });
        var original = AppModelHarness.LibraryBytes();

        h.Main.OnMain(() => model.Archive(parent.Id));
        h.Main.OnMain(() =>
        {
            Assert.True(model.Thread(parent.Id)!.IsArchived);
            Assert.False(model.Thread(parent.Id)!.IsPinned);
            Assert.False(model.Thread(parent.Id)!.IsSettled);
            Assert.Null(model.Thread(parent.Id)!.SettledAt);
            Assert.True(model.Thread(parent.Id)!.HasUnread);
            Assert.True(model.Thread(helper.Id)!.IsArchived);
            Assert.False(model.Thread(helper.Id)!.IsPinned);
            Assert.False(model.Thread(helper.Id)!.IsSettled);
            Assert.Null(model.Thread(helper.Id)!.SettledAt);
            Assert.True(model.Thread(helper.Id)!.HasUnread);
            Assert.Equal(neighbour.Id, model.SelectedThreadID);
        });
        Assert.Equal(original, AppModelHarness.LibraryBytes());
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();
        Assert.True(AppModelHarness.LoadedLibrary().Threads.Single(value => value.Id == parent.Id).IsArchived);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-090")] // App/AppModel.swift:934-959
    public void Deleting_a_thread_recursively_removes_children_selects_a_neighbour_and_saves_immediately()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("repo"));
        var project = AppModelSamples.Project("P", repository.Root);
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var thread = AppModelSamples.Thread(project) with { CreatedAt = at.AddMinutes(2) };
        var child = AppModelSamples.Thread(project) with
        {
            ParentThreadID = thread.Id,
            IsInPanel = true,
            CreatedAt = at.AddMinutes(1),
        };
        var neighbour = AppModelSamples.Thread(project) with { CreatedAt = at };
        h.WriteLibrary([project], [thread, child, neighbour]);
        AppModelHarness.WriteDocument(thread.Id);
        AppModelHarness.WriteDocument(child.Id);
        repository.AddCheckpoint(thread.Id);
        repository.AddCheckpoint(child.Id);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.SelectedThreadID = thread.Id);

        h.Main.OnMain(() => model.Delete(thread.Id));
        DiskWriter.Shared.WaitForQueuedWrites();

        h.Main.OnMain(() =>
        {
            Assert.Null(model.Thread(thread.Id));
            Assert.Null(model.Thread(child.Id));
            Assert.Equal(neighbour.Id, model.SelectedThreadID);
        });
        Assert.False(File.Exists(Storage.ThreadURL(thread.Id)));
        Assert.False(File.Exists(Storage.ThreadURL(child.Id)));
        Assert.Equal(neighbour.Id, Assert.Single(AppModelHarness.LoadedLibrary().Threads).Id);
        h.Main.Loop.RunUntil(
            () => repository.Checkpoints(thread.Id).Count == 0 && repository.Checkpoints(child.Id).Count == 0,
            AppModelHarness.Bound);
        Assert.Empty(repository.Checkpoints(thread.Id));
        Assert.Empty(repository.Checkpoints(child.Id));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-131")] // App/AppModel.swift:558-567
    // Owner rulings exercised: OD-0008 A9 and OD-0012 E2.
    public void Confirmed_project_removal_discards_its_threads_and_clears_selection_and_last_project()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("removed-repo"));
        var worktreePath = Path.Combine(h.Scratch, "removed-worktree-" + Guid.NewGuid().ToString("N")[..8]);
        repository.AddWorktree(worktreePath, "removed-project-feature");
        var removed = AppModelSamples.Project("Remove", repository.Root);
        var kept = AppModelSamples.Project("Keep", h.NewFolder("kept"));
        var removedThread = AppModelSamples.Thread(removed) with
        {
            WorktreePath = worktreePath,
            Branch = "removed-project-feature",
        };
        var removedChild = AppModelSamples.Thread(removed) with { ParentThreadID = removedThread.Id, IsInPanel = true };
        var keptThread = AppModelSamples.Thread(kept);
        h.WriteLibrary([removed, kept], [removedThread, removedChild, keptThread]);
        AppModelHarness.WriteDocument(removedThread.Id);
        AppModelHarness.WriteDocument(removedChild.Id);
        AppModelHarness.WriteDocument(keptThread.Id);
        repository.AddCheckpoint(removedThread.Id);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(removed.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() => model.SelectedThreadID = removedThread.Id);

        h.Main.OnMain(() => model.RemoveProject(removed));
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();

        h.Main.OnMain(() =>
        {
            Assert.Null(model.Project(removed.Id));
            Assert.Null(model.Thread(removedThread.Id));
            Assert.Null(model.Thread(removedChild.Id));
            Assert.NotNull(model.Project(kept.Id));
            Assert.Equal(keptThread.Id, Assert.Single(model.Threads).Id);
            Assert.Null(model.SelectedThreadID);
            Assert.Null(model.Settings.LastProjectID);
        });
        Assert.False(File.Exists(Storage.ThreadURL(removedThread.Id)));
        Assert.False(File.Exists(Storage.ThreadURL(removedChild.Id)));
        Assert.True(File.Exists(Storage.ThreadURL(keptThread.Id)));
        Assert.Equal(kept.Id, Assert.Single(AppModelHarness.LoadedLibrary().Projects).Id);
        Assert.True(Directory.Exists(worktreePath));
        Assert.Single(repository.Checkpoints(removedThread.Id));
    }

    private static DateTimeOffset LocalStartOfDay(DateTimeOffset date)
    {
        var local = TimeZoneInfo.ConvertTime(date, TimeZoneInfo.Local);
        return new DateTimeOffset(
            local.Year,
            local.Month,
            local.Day,
            0,
            0,
            0,
            TimeZoneInfo.Local.GetUtcOffset(local.DateTime));
    }
}
