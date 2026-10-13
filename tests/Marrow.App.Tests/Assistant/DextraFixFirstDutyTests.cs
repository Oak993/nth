using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-2")]
public sealed class DextraFixFirstDutyTests
{
    [Fact]
    public void Starting_Dextra_with_no_duty_file_starts_the_duty_tools_and_morning_digest_once()
    {
        using var temp = new AssistantTempRoot();
        using var launch = new BackgroundLaunchRig(temp);
        Assert.False(BackgroundStart.HasDuties(temp.Roots));
        var controller = launch.OnMain(() => launch.Model.AssistantHost.EnsureStarted());
        Assert.NotNull(controller);
        var duties = launch.Model.Duties;
        Assert.NotNull(duties);
        Assert.NotNull(launch.Model.AssistantHost.Digests);
        Assert.Contains(duties.Scheduler.Duties, d => d.Duty.Id == DigestDuty.Id);
        Assert.True(BackgroundStart.HasDuties(temp.Roots));
        Assert.Same(controller, launch.OnMain(() => launch.Model.AssistantHost.EnsureStarted()));
        Assert.Same(duties, launch.Model.Duties);
        Assert.Same(duties, launch.Run(StartupMode.Normal, out _));
        Assert.False(duties.Switches.KeepWorkingWhenClosed);
    }
}
