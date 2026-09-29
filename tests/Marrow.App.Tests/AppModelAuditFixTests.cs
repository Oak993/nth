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

    // A folder as the Windows panel hands it over: a file URL such as file:///C:/x.
    private static SwiftURL FolderURL(string folder) => SwiftURL.Create(new Uri(folder).AbsoluteUri)!;
}
