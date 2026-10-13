using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 2, 12.4 "Reader and actor split"; ED-NEW-dextra-as19-4): the model that writes a teach draft up is a reader mission
// on the brain's account: its brief is the draft prompt, its agents only read, its report is the answer, and it is an ordinary task on the board
// that Stop all reaches. Ended any way but Done, out of time, or cancelled, there is no answer and no mission is left running.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-4")]
public sealed class MissionDraftModelTests
{
    private static MissionDraftModel Model(DextraTaintRig rig, TimeProvider? time = null, TimeSpan? timeout = null, bool project = true) =>
        new(rig.Controller, new MissionDraftModelOptions
        {
            AssistantId = "dextra",
            Provider = ProviderKind.Claude,
            Model = "model-a",
            OnMain = work => work(),
            Project = () => project ? rig.Project : null,
            Time = time ?? TimeProvider.System,
            Timeout = timeout ?? TimeSpan.FromMinutes(10),
        });

    private static MissionRecord OnlyMission(DextraTaintRig rig)
    {
        var record = rig.Controller.Runner.Missions.Single();
        MissionRig.Until(() => rig.Now(record).LeadThread is not null, "the lead of the drafting mission");
        return rig.Now(record);
    }

    [Fact]
    public async Task The_prompt_goes_to_a_reader_mission_whose_report_is_the_answer()
    {
        using var rig = new DextraTaintRig();
        var task = Model(rig).Complete("PROMPT: write the skill up", CancellationToken.None);
        var record = OnlyMission(rig);
        Assert.True(record.Readers);
        Assert.Equal(MissionOrigin.Owner, record.Mission.Origin);
        Assert.Equal("model-a", record.Model);
        var lead = record.LeadThread!.Value;
        rig.Chat(lead, record, "Lead");
        var launch = rig.Agents.Lead(record).Launch;
        Assert.True(launch.Reader);
        Assert.Contains("PROMPT: write the skill up", launch.Brief, StringComparison.Ordinal);
        rig.Agents.Finish(lead, "{\"name\":\"send-report\"}");
        rig.Controller.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        Assert.Equal("{\"name\":\"send-report\"}", await task);
        Assert.Equal(MissionState.Done, rig.Now(record).Mission.State);
    }

    [Fact]
    public async Task A_mission_that_is_stopped_gives_no_answer()
    {
        using var rig = new DextraTaintRig();
        var task = Model(rig).Complete("prompt", CancellationToken.None);
        var record = OnlyMission(rig);
        rig.Chat(record.LeadThread!.Value, record, "Lead");
        Assert.Null(rig.Controller.Controls.Stop(record.Id));
        Assert.Null(await task);
    }

    [Fact]
    public async Task Cancelling_stops_the_mission_and_the_wait_ends()
    {
        using var rig = new DextraTaintRig();
        using var cancel = new CancellationTokenSource();
        var task = Model(rig).Complete("prompt", cancel.Token);
        var record = OnlyMission(rig);
        rig.Chat(record.LeadThread!.Value, record, "Lead");
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        MissionRig.Until(() => rig.Now(record).Mission.State is MissionState.Stopped or MissionState.Stopping, "the drafting mission to stop");
    }

    [Fact]
    public async Task A_mission_that_takes_too_long_is_stopped_and_gives_no_answer()
    {
        using var rig = new DextraTaintRig();
        var clock = new ManualTimeProvider();
        var task = Model(rig, clock, TimeSpan.FromMinutes(3)).Complete("prompt", CancellationToken.None);
        var record = OnlyMission(rig);
        rig.Chat(record.LeadThread!.Value, record, "Lead");
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(await task);
        MissionRig.Until(() => rig.Now(record).Mission.State is MissionState.Stopped or MissionState.Stopping, "the drafting mission to stop");
    }

    [Fact]
    public async Task With_no_project_to_work_in_nothing_is_started()
    {
        using var rig = new DextraTaintRig();
        Assert.Null(await Model(rig, project: false).Complete("prompt", CancellationToken.None));
        Assert.Empty(rig.Controller.Runner.Missions);
    }
}
