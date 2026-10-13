using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Home;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-fix-9")]
public sealed class DextraFixHomeBusyTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(MissionState.Planning, true)]
    [InlineData(MissionState.Working, true)]
    [InlineData(MissionState.Reviewing, true)]
    [InlineData(MissionState.Pausing, true)]
    [InlineData(MissionState.Paused, false)]
    public void Home_orbit_nodes_follow_the_same_Astur_presence_as_the_team(MissionState state, bool busy)
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            jobs.Set(HomeSamples.Job("Voiceover", state) with { Owner = ChannelA.Name });
            var team = new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA, ChannelB), [Task(ChannelA.Id, "Voiceover", state)], []) };
            var home = new AssistantHomeView(new AssistantHomeParts(jobs, new FakeDecisions(), new FakeGlobalControls())
            { Profiles = () => Team(ChannelA, ChannelB), Team = team });
            using var stage = new HomeStage(home);
            Assert.Equal(busy, home.HeaderMark.Nodes[0].Busy);
            Assert.Equal(busy, home.HeroMark.Nodes[0].Busy);
            Assert.False(home.HeaderMark.Nodes[1].Busy);
            team.Set(TeamSnapshot.Build(Team(ChannelA, ChannelB), [], []));
            stage.Layout();
            Assert.False(home.HeaderMark.Nodes[0].Busy);
            Assert.False(home.HeroMark.Nodes[0].Busy);
        });
    }

    [Fact]
    public void Team_changes_from_the_store_thread_refresh_the_marks_on_the_UI_thread()
    {
        headless.OnUiThread(() =>
        {
            var team = new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA), [], []) };
            var home = new AssistantHomeView(new AssistantHomeParts(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls())
            { Profiles = () => Team(ChannelA), Team = team });
            using var stage = new HomeStage(home);
            Assert.False(home.HeaderMark.Nodes[0].Busy);
            // Start under this headless session's locator scope, like the asynchronous team store writer.
            var change = System.Threading.Tasks.Task.Run(() => team.Set(TeamSnapshot.Build(Team(ChannelA), [Task(ChannelA.Id, "Voiceover", MissionState.Working)], [])));
            HeadlessSession.RunDispatcherUntil(change, TimeSpan.FromSeconds(5));
            Assert.False(change.IsFaulted, change.Exception?.ToString());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            stage.Layout();
            Assert.True(home.HeaderMark.Nodes[0].Busy);
            Assert.True(home.HeroMark.Nodes[0].Busy);
        });
    }

}
