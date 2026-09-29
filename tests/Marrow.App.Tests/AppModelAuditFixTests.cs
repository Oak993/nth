using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

// Audit fixes for App.AppModel (App/AppModel.swift, B3 52bc792): one test per confirmed difference.
public sealed class AppModelAuditFixTests
{
    [Fact]
    [Trait("Req", "PR-STORE-171")] // AppModel.swift:54-60
    public void A_repeated_thread_id_resolves_to_the_first_position_in_the_thread_index()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("dup-id"));
        var first = AppModelSamples.Thread(project) with { Title = "first" };
        var second = first with { Title = "second" };
        h.WriteLibrary([project], [first]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.InsertThread(second);
            model.UpdateThread(first.Id, thread => thread with { Title = "changed" });

            Assert.Equal(["changed", "second"], model.Threads.Select(thread => thread.Title).ToList());
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-171")] // AppModel.swift:189-210
    public void A_live_parent_keeps_its_empty_child_cell_so_the_observer_hears_of_its_first_child()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("child-cell"));
        var parent = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [parent, other]);
        var model = h.MakeModel();
        var heard = 0;

        h.Main.OnMain(() =>
        {
            var (before, tracker) = ObservationTracking.Track(() => model.Children(parent.Id), () => heard++);
            Assert.Empty(before);

            model.UpdateThread(other.Id, thread => thread with { Title = "unrelated" });
            Assert.Equal(0, heard);

            model.InsertThread(AppModelSamples.Thread(project) with { ParentThreadID = parent.Id });
            Assert.Equal(1, heard);
            GC.KeepAlive(tracker);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-060")] // AppModel.swift:357-371
    public void The_activity_day_is_local_midnight_with_the_offset_in_force_at_that_midnight()
    {
        using var h = new AppModelHarness();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var project = AppModelSamples.Project("P", h.NewFolder("activity-day"));
        // 16:00 EDT on 2026-03-08, the day the clocks went forward: local midnight was still on EST (UTC-5).
        var updated = new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero);
        var first = AppModelSamples.Thread(project) with { UpdatedAt = updated };
        var second = AppModelSamples.Thread(project) with { UpdatedAt = updated };
        h.WriteLibrary([project], [first, second]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            using var scope = SwiftFormatters.CurrentTimeZone.Override(zone);
            Assert.True(model.MoveInActivity(second.Id, first.Id, placeAfter: false, [first.Id, second.Id]));

            var expected = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));
            Assert.Equal(expected.UtcDateTime, model.Thread(first.Id)!.ActivityOrderDay!.Value.UtcDateTime);
            Assert.Equal(expected.UtcDateTime, model.Thread(second.Id)!.ActivityOrderDay!.Value.UtcDateTime);
        });
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-044")] // AppModel.swift:526-529, :535-547
    public void Choosing_a_project_folder_adds_the_projects_and_starts_a_selected_thread_in_the_last_one()
    {
        using var h = new AppModelHarness();
        var first = h.NewFolder("chosen-a");
        var last = h.NewFolder("chosen-b");
        var model = h.MakeModel();
        var runner = new RecordingOpenPanelRunner([FolderURL(first), FolderURL(last)]);

        h.Main.OnMain(() =>
        {
            using var scope = OpenPanel.Runner.Override(runner);
            model.ChooseProjectFolder();

            Assert.Equal(2, model.Projects.Count);
            var thread = Assert.Single(model.Threads);
            Assert.Equal(model.Projects[1].Id, thread.ProjectID);
            Assert.True(SwiftPath.SamePath(last, model.Projects[1].Path));
            Assert.Equal(thread.Id, model.SelectedThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // AppModel.swift:535-547
    public void The_folder_panel_allows_several_folders_adds_each_and_returns_the_last_added()
    {
        using var h = new AppModelHarness();
        var first = h.NewFolder("panel-a");
        var last = h.NewFolder("panel-b");
        var model = h.MakeModel();
        var runner = new RecordingOpenPanelRunner([FolderURL(first), FolderURL(last)]);

        h.Main.OnMain(() =>
        {
            using var scope = OpenPanel.Runner.Override(runner);
            var added = model.AddProjectsFromPanel();

            Assert.Equal((true, false, true, "Add project", "Choose a folder for your agents to work in."), Assert.Single(runner.Shown));
            Assert.Equal(2, model.Projects.Count);
            Assert.Equal(model.Projects[1], added);
            Assert.Empty(model.Threads);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-030")] // AppModel.swift:660, :673
    public void A_new_thread_replaces_a_default_provider_that_is_not_installed_with_the_first_installed_and_enabled_one()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("provider-choice"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Codex, false);
            model.Settings.DefaultProvider = ProviderKind.Codex;

            Assert.Equal(ProviderKind.Claude, model.NewThread(project)!.Provider);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-094")] // AppModel.swift:667-671, :703-709
    public void A_fresh_thread_starts_on_the_pair_of_the_most_recent_non_helper_thread_when_no_pair_was_entered()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("recent-pair"));
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "lead-model", OrchestratorEffort = "high" };
        var older = AppModelSamples.Thread(project) with { UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var recent = AppModelSamples.Thread(project) with { UpdatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), HydraPairID = null };
        var recentHelper = AppModelSamples.Thread(project) with { UpdatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), ParentThreadID = older.Id, HydraPairID = Guid.NewGuid() };
        recent = recent with { HydraPairID = pair.Id, HydraEnabled = true };
        h.WriteLibrary([project], [older, recent, recentHelper]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Codex, HydraCookbookResolveFixture.Model("lead-model", "Lead", ["low", "high"]));
            model.Settings.AddHydraPair(pair);
            model.Settings.HydraEnabled = true;
            model.Settings.LastHydraPairID = null;

            var made = model.NewThread(project)!;

            Assert.Equal(pair.Id, made.HydraPairID);
            Assert.True(made.HydraEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-094")] // AppModel.swift:667-702
    public void A_thread_started_on_a_projects_pair_takes_the_pairs_lead_model_and_effort()
    {
        using var h = new AppModelHarness();
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "lead-model", OrchestratorEffort = "high" };
        var project = AppModelSamples.Project("P", h.NewFolder("project-pair")) with { HydraPairID = pair.Id };
        h.WriteLibrary([project], []);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Codex, true);
            HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Codex, HydraCookbookResolveFixture.Model("lead-model", "Lead", ["low", "high"]), HydraCookbookResolveFixture.Model("other", "Other", ["low"]));
            model.Settings.AddHydraPair(pair);
            model.Settings.HydraEnabled = true;

            var made = model.NewThread(project)!;

            Assert.Equal("lead-model", made.Model);
            Assert.Equal("high", made.Effort);
            Assert.Equal(pair.Id, made.HydraPairID);
            Assert.True(made.HydraEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-094")] // AppModel.swift:701-702
    public void A_thread_made_from_a_chat_that_leads_a_pair_carries_the_pair_along()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("carried-pair"));
        var pair = new HydraPair(ProviderKind.Codex);
        var current = AppModelSamples.Thread(project) with { HydraPairID = pair.Id, HydraEnabled = true };
        h.WriteLibrary([project], [current]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Codex, true);
            model.Settings.AddHydraPair(pair);
            model.SelectedThreadID = current.Id;

            Assert.Equal(pair.Id, model.NewThread(project)!.HydraPairID);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-060")] // AppModel.swift:705-712
    public void An_update_that_changes_nothing_writes_no_library_and_leaves_the_threads_alone()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("no-op-update"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var before = h.Main.OnMain(() => model.Threads);

        h.Main.OnMain(() => model.UpdateThread(thread.Id, current => current with { }));
        h.Main.Advance(TimeSpan.FromSeconds(5));
        DiskWriter.Shared.WaitForQueuedWrites();

        // SaveLibrary stamps lastLibrarySave on every save, scheduled or direct (AppModel.swift:1318): none was scheduled.
        var lastSave = typeof(AppModel).GetField("lastLibrarySave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Null(lastSave.GetValue(model));
        Assert.Same(before, h.Main.OnMain(() => model.Threads));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-021")] // AppModel.swift:720-741
    public void Moving_a_thread_to_another_project_clears_its_folder_state_opens_the_project_and_asks_for_the_row()
    {
        using var h = new AppModelHarness();
        var source = AppModelSamples.Project("Source", h.NewFolder("move-source"));
        var target = AppModelSamples.Project("Target", h.NewFolder("move-target")) with { IsExpanded = false };
        var thread = AppModelSamples.Thread(source) with
        {
            WorktreePath = "old-worktree",
            Branch = "old-branch",
            ProviderSessionID = "old-session",
            ProviderResumeAt = "old-resume",
        };
        h.WriteLibrary([source, target], [thread]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.LastProjectID = source.Id;
            model.MoveThread(thread.Id, target);

            var moved = model.Thread(thread.Id)!;
            Assert.Equal(target.Id, moved.ProjectID);
            Assert.Null(moved.WorktreePath);
            Assert.Null(moved.Branch);
            Assert.Null(moved.ProviderSessionID);
            Assert.Null(moved.ProviderResumeAt);
            Assert.True(model.Project(target.Id)!.IsExpanded);
            Assert.Equal(target.Id, model.Settings.LastProjectID);
            Assert.Equal(thread.Id, model.SidebarRevealThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-085")] // AppModel.swift:766-781
    public void Archiving_a_thread_releases_its_Hydra_copy()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("archive-copy"));
        var copy = h.NewFolder("head-copy");
        var lead = AppModelSamples.Thread(project);
        var head = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped, baseTree: "tree"),
            WorktreePath = copy,
            Branch = "head-branch",
        };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();

        h.Main.OnMain(() => model.Archive(head.Id));

        var archived = model.Thread(head.Id)!;
        Assert.True(archived.IsArchived);
        Assert.Null(archived.WorktreePath);
        Assert.Null(archived.Branch);
        Assert.Null(archived.Hydra!.BaseTree);
        h.Main.Loop.RunUntil(() => !Directory.Exists(copy), AppModelHarness.Bound); // the copy's folder goes with the release
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-081")] // AppModel.swift:839-851
    public void A_settled_thread_that_starts_a_turn_reopens_inside_the_settle_flight_animation()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("reopen-animated"));
        var settled = AppModelSamples.Thread(project) with { IsSettled = true, SettledAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        h.WriteLibrary([project], [settled]);
        var model = h.MakeModel();
        Animation? seen = null;

        h.Main.OnMain(() =>
        {
            var (_, tracker) = ObservationTracking.Track(() => model.Thread(settled.Id)!.IsSettled, () => seen = Transaction.Current);
            model.ReopenIfSettled(settled.Id);

            Assert.False(model.Thread(settled.Id)!.IsSettled);
            Assert.NotNull(seen);
            Assert.Equal(Chrome.SettleFlight, seen);
            tracker.Cancel();
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-112")] // AppModel.swift:855-863
    public void The_finish_title_and_symbol_have_no_helper_case()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("finish-labels"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id };
        var settledHelper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, IsSettled = true };
        h.WriteLibrary([project], [parent, helper, settledHelper]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.SelectedThreadID = helper.Id;
            Assert.Equal("Settle thread", model.FinishActionTitle);
            Assert.Equal("checkmark.circle", model.FinishActionSymbol);
            model.SelectedThreadID = settledHelper.Id;
            Assert.Equal("Reopen thread", model.FinishActionTitle);
            Assert.Equal("arrow.uturn.backward", model.FinishActionSymbol);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-083")] // AppModel.swift:865-882
    public void Finish_on_an_already_settled_thread_reopens_it_when_no_row_takes_the_request()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("finish-reopen"));
        var settled = AppModelSamples.Thread(project) with { IsSettled = true, SettledAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        h.WriteLibrary([project], [settled]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.SelectedThreadID = settled.Id;
            model.FinishSelectedThread();

            var request = Assert.IsType<AppModel.FinishRequest>(model.AppModelFinishRequest);
            Assert.Equal(settled.Id, request.ThreadID);
            Assert.True(request.Reopens);
            Assert.True(model.Thread(settled.Id)!.IsSettled);
        });
        h.Main.Advance(TimeSpan.FromMilliseconds(100));

        Assert.False(h.Main.OnMain(() => model.Thread(settled.Id)!.IsSettled));
        Assert.Null(h.Main.OnMain(() => model.AppModelFinishRequest));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-083")] // AppModel.swift:878-889
    public void The_settle_shortcut_returns_at_once_for_a_helper()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("settle-helper"));
        var parent = AppModelSamples.Thread(project);
        var helper = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id };
        h.WriteLibrary([project], [parent, helper]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = helper.Id;
            model.SettleSelectedThread();

            Assert.Null(model.AppModelFinishRequest);
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-083")] // AppModel.swift:887-921
    public void A_settle_or_archive_request_no_row_takes_up_is_carried_out_after_80_milliseconds()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("fallback"));
        var toSettle = AppModelSamples.Thread(project);
        var toArchive = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [toSettle, toArchive]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.SettleSound = false);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = toSettle.Id;
            model.SettleSelectedThread();
            Assert.NotNull(model.AppModelFinishRequest);
            Assert.False(model.Thread(toSettle.Id)!.IsSettled);
        });
        h.Main.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(h.Main.OnMain(() => model.Thread(toSettle.Id)!.IsSettled));
        Assert.Null(h.Main.OnMain(() => model.AppModelFinishRequest));

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = toArchive.Id;
            model.AskToArchiveSelectedThread();
            Assert.NotNull(model.AppModelArchiveRequest);
            Assert.False(model.Thread(toArchive.Id)!.IsArchived);
        });
        h.Main.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(h.Main.OnMain(() => model.Thread(toArchive.Id)!.IsArchived));
        Assert.Null(h.Main.OnMain(() => model.AppModelArchiveRequest));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-094")] // AppModel.swift:902-912
    public void Asking_to_archive_an_empty_loaded_thread_deletes_it_without_a_question()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("empty-thread"));
        var empty = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [empty, other]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(empty.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = empty.Id;
            model.AskToArchiveSelectedThread();

            Assert.Null(model.AppModelArchiveRequest);
            Assert.Null(model.Thread(empty.Id));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-112")] // AppModel.swift:864-866
    public void Finishing_with_archive_chosen_archives_inside_the_panel_slide_animation()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("finish-archive"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        Animation? seen = null;

        h.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            model.SelectedThreadID = thread.Id;
            var (_, tracker) = ObservationTracking.Track(() => model.Thread(thread.Id)!.IsArchived, () => seen = Transaction.Current);
            model.FinishSelectedThread();

            Assert.True(model.Thread(thread.Id)!.IsArchived);
            Assert.Equal(Chrome.PanelSlide, seen);
            tracker.Cancel();
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-090")] // AppModel.swift:947-953
    public void Deleting_an_unfinished_Hydra_head_reports_it_stopped_to_its_lead()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("delete-head"));
        // A lead whose last turn was stopped takes no report flush, so the report stays where the test can read it.
        var lead = AppModelSamples.Thread(project) with { LastStatus = TurnStatus.Interrupted };
        var head = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running),
        };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            // A head at work when the app last quit comes back stopped (AppModel.swift:134-137), so this one is set running now.
            model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
            model.Delete(head.Id);

            var leadRuntime = Assert.IsType<ThreadRuntime>(model.ExistingRuntime(lead.Id));
            var pending = (System.Collections.IList)typeof(ThreadRuntime)
                .GetField("hydraPendingReports", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(leadRuntime)!;
            var report = Assert.IsType<HydraReport>(Assert.Single(pending));
            Assert.Equal(HydraHeadInfo.Status.Stopped, report.Status);
        });
    }

    // A folder as the Windows panel hands it over: a file URL such as file:///C:/x.
    private static SwiftURL FolderURL(string folder) => SwiftURL.Create(new Uri(folder).AbsoluteUri)!;
}
