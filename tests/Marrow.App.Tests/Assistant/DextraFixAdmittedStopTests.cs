using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-fix-5")]
public sealed class DextraFixAdmittedStopTests
{
    [Fact]
    public async Task Stop_after_lead_admission_releases_the_placeholder_slot()
    {
        using var temp = new AssistantTempRoot();
        var rig = new MissionRig(temp.Folder);
        rig.Gate.Changed += () => { if (rig.Gate.Running > 0) rig.Runner.FlagStop(); };
        var start = rig.Runner.Start(rig.Request());
        await start.Launched;
        Assert.Empty(rig.Agents.Chats);
        rig.Runner.Stop(start.Record.Id);
        Assert.Equal(0, rig.Gate.Running);
    }

    [Fact]
    public async Task Stop_after_reviewer_admission_releases_the_placeholder_slot()
    {
        using var temp = new AssistantTempRoot();
        var rig = new MissionRig(temp.Folder);
        var start = rig.Runner.Start(rig.Request());
        await start.Launched;
        var lead = rig.Runner.Find(start.Record.Id)!.LeadThread!.Value;
        rig.Gate.Changed += () => { if (rig.Gate.Running > 0) rig.Runner.FlagStop(); };
        rig.Agents.Finish(lead, "The work is done.");
        rig.Runner.OnTurnFinished(lead, TurnStatus.Completed, false);
        Assert.Single(rig.Agents.Chats);
        rig.Runner.Stop(start.Record.Id);
        Assert.Equal(0, rig.Gate.Running);
    }
}
