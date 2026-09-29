using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;

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

    // A folder as the Windows panel hands it over: a file URL such as file:///C:/x.
    private static SwiftURL FolderURL(string folder) => SwiftURL.Create(new Uri(folder).AbsoluteUri)!;
}
