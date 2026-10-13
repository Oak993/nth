using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-3")]
public sealed class DextraFixInterruptedTests
{
    [Theory]
    [InlineData(MissionState.Planning, MissionState.Paused)]
    [InlineData(MissionState.Working, MissionState.Paused)]
    [InlineData(MissionState.Reviewing, MissionState.Paused)]
    [InlineData(MissionState.NeedsYou, MissionState.Paused)]
    [InlineData(MissionState.Stopping, MissionState.Stopped)]
    public void Hidden_launch_applies_the_interrupted_mission_rule(MissionState saved, MissionState expected)
    {
        using var temp = new AssistantTempRoot();
        DextraFixStartupTests.DutyFile(temp);
        var record = new MissionRecord(new Mission(Guid.NewGuid(), "Interrupted", "dextra", MissionOrigin.Owner, saved));
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveMission(MissionJournal.StoreId(record.Id), MissionJournal.ToJson(record));
        using var launch = new BackgroundLaunchRig(temp);
        Assert.NotNull(launch.Run(new StartupMode(true, null, null), out _));
        var restored = launch.Model.Dextra!.Runner.Find(record.Id)!;
        Assert.Equal(expected, restored.Mission.State);
        Assert.Equal(expected == MissionState.Paused, restored.Interrupted);
    }
}
