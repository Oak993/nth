using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-1")]
public sealed class DextraFixStartupTests
{
    internal static void DutyFile(AssistantTempRoot temp)
    {
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveDuties("dextra", DutyJson.Write(new DutyDocument([new Duty("d1", "Weekly", "dextra", new ManualTrigger(), "Write a note.")])));
    }

    [Fact]
    public void A_hidden_start_and_the_home_share_one_controller_and_live_view()
    {
        using var temp = new AssistantTempRoot();
        DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out _));
        var background = launch.Model.Dextra;
        var controller = launch.OnMain(() => launch.Model.AssistantHost.EnsureStarted());
        Assert.Same(background, controller);
        Assert.Null(launch.Model.AssistantHost.StartFault);
        Assert.NotSame(DextraLive.Idle, launch.Model.AssistantHost.Live);
    }

    [Fact]
    public void Turning_off_after_a_hidden_start_stops_the_controller_and_duties_and_releases_the_store()
    {
        using var temp = new AssistantTempRoot();
        DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out _));
        launch.OnMain(() => launch.Model.AssistantHost.Settings.Update(p => p with { On = false }));
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
        Assert.Null(launch.Model.AssistantHost.Controller);
        using var reopened = AssistantStore.Open(temp.Roots);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_hidden_start_respects_off_and_incomplete_setup(bool on, bool done)
    {
        using var temp = new AssistantTempRoot();
        DutyFile(temp);
        using var launch = new BackgroundLaunchRig(temp);
        launch.OnMain(() => launch.Model.AssistantHost.Settings.Value = launch.Model.AssistantHost.Settings.Value with { On = on, FirstRunDone = done });
        Assert.Null(launch.Run(new StartupMode(true, null, null), out _));
        Assert.Null(launch.Model.Dextra);
        Assert.Null(launch.Model.Duties);
        Assert.False(temp.Keys.HasKey());
    }
}
