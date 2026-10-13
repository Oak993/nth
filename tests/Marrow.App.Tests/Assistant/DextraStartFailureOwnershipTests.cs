using System.Reflection;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-11")]
public sealed class DextraStartFailureOwnershipTests
{
    [Fact]
    public void A_retry_from_a_failure_observer_keeps_its_initialization_and_gets_one_failure_feed_line()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        using var feed = new FeedCollector(launch.Model.AssistantEvents);
        var host = launch.Model.AssistantHost;
        var failed = false;
        var retryKept = false;
        Task<AssistantController?>? retry = null;
        launch.OnMain(() => host.Changed += () =>
        {
            if (launch.Model.Dextra is not null && !failed)
            {
                failed = true;
                throw new IOException("fixture publication failure");
            }
            if (!failed || launch.Model.Dextra is not null) return;
            retry ??= host.EnsureStartedAsync(launch.Seams);
            retryKept = ReferenceEquals(retry, typeof(DextraHost).GetField("initialization", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host));
        });
        var start = launch.OnMain(() => host.EnsureStartedAsync(launch.Seams, restoringDuties: true));
        Assert.Null(launch.Main.Loop.Run(() => start));
        Assert.NotNull(retry);
        launch.Main.Loop.Run(() => retry);
        launch.Run(() => launch.Model.AssistantEvents.FlushAsync());
        Assert.True(retryKept);
        Assert.Equal("Dextra's duties did not start: fixture publication failure", Assert.Single(feed.Lines).Summary);
    }

    [Fact]
    public void A_worker_failure_after_stop_has_no_fault_state_or_failure_feed_for_the_stopped_generation()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        using var feed = new FeedCollector(launch.Model.AssistantEvents);
        var seams = new BackgroundStartSeams
        {
            Roots = () => temp.Roots, Keys = () => temp.Keys, StopKey = launch.Key, Tray = launch.Tray, Host = launch.Seams.Host,
            ControllerBuilt = _ =>
            {
                Marrow.Core.Port.MainActor.Post(launch.Model.AssistantHost.Stop);
                throw new IOException("stale failure");
            },
        };
        var pending = launch.OnMain(() => launch.Model.AssistantHost.EnsureStartedAsync(seams, restoringDuties: true));
        Assert.Null(launch.Main.Loop.Run(() => pending));
        launch.Run(() => launch.Model.AssistantEvents.FlushAsync());
        Assert.Null(launch.Model.AssistantHost.StartFault);
        Assert.Empty(feed.Lines);
    }
}
