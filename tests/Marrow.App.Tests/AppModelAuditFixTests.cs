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
}
