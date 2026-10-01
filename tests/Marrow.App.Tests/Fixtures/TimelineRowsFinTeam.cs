using Marrow.App;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// A lead thread with head threads under it, and the lead's runtime: ThreadRuntimePart12Harness's setup with the heads
// added, for the rows that read a head's record through the app model (the head badge, the finished-head pills).
internal static class TimelineRowsFinTeam
{
    internal static (AppModelHarness Store, AppModel App, ThreadRuntime Runtime) Make(params HydraHeadInfo[] heads)
    {
        Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Team", store.NewFolder("team"));
        var lead = AppModelSamples.Thread(project);
        var team = heads.Select(head => AppModelSamples.Thread(project) with { ParentThreadID = lead.Id, Hydra = head }).ToArray();
        store.WriteLibrary([project], [lead, .. team]);
        AppModelHarness.WriteDocument(lead.Id);
        var app = store.MakeModel();
        store.PrepareBootstrap(app);
        var runtime = store.Main.OnMain(() => new ThreadRuntime(lead.Id, app));
        return (store, app, runtime);
    }

    internal static HydraHeadInfo Head(long index, HydraHeadInfo.Status status) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = status };
}
