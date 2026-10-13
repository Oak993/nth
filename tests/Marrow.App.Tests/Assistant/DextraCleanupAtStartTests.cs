using System.Collections.Concurrent;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: final security gate N2 (ED-NEW-secfinal-23). A private Command Code answer folder that could not be removed was retried only at the
// next Command Code answer. Dextra's start now also runs that retry, once per start, on a worker thread: a start that runs file work on the main
// thread would stall the first frame (ED-NEW-dextra-open-6), so the call is made from the thread pool and never from MainActor.
[Trait("Decision", "ED-NEW-secfinal-23")]
public sealed class DextraCleanupAtStartTests
{
    [Fact]
    public void Dextra_start_retries_a_failed_private_answer_cleanup_once_and_off_the_main_thread()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        var calls = new ConcurrentBag<bool>();
        using var ran = new ManualResetEventSlim();
        var seams = new BackgroundStartSeams
        {
            Roots = () => temp.Roots, Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
            CommandCodeCleanup = () =>
            {
                calls.Add(MainActor.IsIsolated);
                ran.Set();
            },
        };

        Assert.NotNull(launch.Main.Loop.Run(() => launch.Model.AssistantHost.EnsureStartedAsync(seams)));

        Assert.True(ran.Wait(TimeSpan.FromSeconds(20)), "Dextra's start did not retry the private answer cleanup");
        Assert.False(Assert.Single(calls), "the cleanup retry ran on the main thread");
        launch.OnMain(launch.Model.AssistantHost.Stop);
    }
}
