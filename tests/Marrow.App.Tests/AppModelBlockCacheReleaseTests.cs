using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests;

// ED-NEW-perf-memory-3: the timeline's block cache of a thread holds the runtime's own entry list and the built blocks. A cache left
// behind after the runtime is released (or the thread deleted) keeps the whole transcript alive, so the app must drop it with the runtime.
public sealed class AppModelBlockCacheReleaseTests
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:490-506 (an idle runtime is released); the cache goes with it
    public void Releasing_an_idle_runtime_drops_its_timeline_block_cache()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => TimelineBlockCache.Cache(thread.Id));
        Assert.True(TimelineBlockCache.Holds(thread.Id));

        h.Main.OnMain(() => Assert.True(model.ReleaseIdleRuntime(thread.Id)));

        Assert.False(TimelineBlockCache.Holds(thread.Id));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // a runtime that is kept (here: selected) keeps its warm cache
    public void A_runtime_that_is_kept_keeps_its_timeline_block_cache()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => { model.SelectedThreadID = thread.Id; TimelineBlockCache.Cache(thread.Id); });

        h.Main.OnMain(() => Assert.False(model.ReleaseIdleRuntime(thread.Id)));

        Assert.True(TimelineBlockCache.Holds(thread.Id));
        h.Main.OnMain(() => TimelineBlockCache.Forget(thread.Id));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-062")] // App/AppModel.swift:1068-1078 (discarding a deleted thread's state)
    public void Deleting_a_thread_drops_its_timeline_block_cache_and_forgetting_one_is_exact()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("p"));
        var thread = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread, other]);
        AppModelHarness.WriteDocument(thread.Id);
        AppModelHarness.WriteDocument(other.Id);
        var model = h.MakeModel();
        h.Main.OnMain(() => { TimelineBlockCache.Cache(thread.Id); TimelineBlockCache.Cache(other.Id); });

        h.Main.OnMain(() => model.Delete(thread.Id));

        Assert.False(TimelineBlockCache.Holds(thread.Id));
        Assert.True(TimelineBlockCache.Holds(other.Id)); // only the deleted thread's cache goes
        h.Main.OnMain(() => TimelineBlockCache.Forget(other.Id));
        Assert.False(TimelineBlockCache.Holds(other.Id));
        h.Main.OnMain(() => TimelineBlockCache.Forget(other.Id)); // forgetting a cache that is not there changes nothing
    }
}
