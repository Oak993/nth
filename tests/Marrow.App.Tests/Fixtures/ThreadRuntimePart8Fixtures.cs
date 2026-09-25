using Marrow.App;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Fixtures;

public sealed record ThreadRuntimePart8Fixture(Project Project, ChatThread Thread, AppModel Model, ThreadRuntime Runtime);

public static class ThreadRuntimePart8Fixtures
{
    public static ThreadRuntimePart8Fixture Create(
        AppModelHarness harness,
        ProviderKind provider = ProviderKind.Codex,
        ThreadDocument? document = null)
    {
        var project = AppModelSamples.Project("Runtime part 8", harness.NewFolder("runtime-p8"));
        var thread = AppModelSamples.Thread(project) with { Provider = provider };
        harness.WriteLibrary([project], [thread]);

        if (document is not null)
        {
            DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(thread.Id));
            var warm = DocumentPrefetch.Shared.Warm([thread.Id], urgent: true);
            if (warm is not null && !warm.Completion.Wait(AppModelHarness.Bound))
                throw new TimeoutException("The thread document did not prefetch.");
        }

        var model = harness.MakeModel();
        var runtime = harness.Main.OnMain(() => model.Runtime(thread.Id));
        harness.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        return new ThreadRuntimePart8Fixture(project, thread, model, runtime);
    }
}
