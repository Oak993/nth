using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-11")]
public sealed class DextraStartFeedTests
{
    [Fact]
    public void A_real_foreground_start_without_restoring_duties_also_reaches_the_feed()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        using var holder = AssistantStore.Open(temp.Roots);
        using var feed = new FeedCollector(launch.Model.AssistantEvents);
        launch.OnMain(() => Assert.Null(launch.Model.AssistantHost.EnsureStarted()));
        launch.Main.Loop.RunUntil(() => launch.Model.AssistantHost.StartFault is not null, TimeSpan.FromSeconds(20));
        launch.Run(() => launch.Model.AssistantEvents.FlushAsync());
        var line = Assert.Single(feed.Lines);
        Assert.Equal(launch.Model.AssistantHost.StartFault, line.Summary);
        Assert.StartsWith("Dextra did not start: ", line.Summary, StringComparison.Ordinal);
        Assert.Equal(FeedEventKind.WatcherChanged, line.Kind);
        Assert.Equal(FeedSeverity.Error, line.Severity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_failed_production_background_start_reaches_the_feed_once_hidden_or_foreground(bool hidden)
    {
        using var temp = new AssistantTempRoot();
        DextraFixStartupTests.DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        using var holder = AssistantStore.Open(temp.Roots);
        using var feed = new FeedCollector(launch.Model.AssistantEvents);
        var faultOnMain = false;
        launch.OnMain(() =>
        {
            launch.Model.AssistantHost.Changed += () =>
            {
                if (launch.Model.AssistantHost.StartFault is not null) faultOnMain = MainActor.IsIsolated;
            };
            Assert.Null(BackgroundStart.Run(launch.Model, hidden ? new StartupMode(true, null, null) : StartupMode.Normal,
                () => hidden, () => { }, () => { }, out var synchronousProblem, launch.Seams));
            Assert.Null(synchronousProblem);
        });
        launch.Main.Loop.RunUntil(() => launch.Model.AssistantHost.StartFault is not null, TimeSpan.FromSeconds(20));
        launch.Run(() => launch.Model.AssistantEvents.FlushAsync());
        var line = Assert.Single(feed.Lines);
        Assert.Equal(launch.Model.AssistantHost.StartFault, line.Summary);
        Assert.StartsWith("Dextra's duties did not start: ", line.Summary, StringComparison.Ordinal);
        Assert.Equal(FeedEventKind.WatcherChanged, line.Kind);
        Assert.Equal(FeedSeverity.Error, line.Severity);
        Assert.True(faultOnMain);
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
    }
}
