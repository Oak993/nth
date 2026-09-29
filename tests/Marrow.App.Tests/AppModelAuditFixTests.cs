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
}
