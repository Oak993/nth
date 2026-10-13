using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.2 "Team: the org chart (section 9)", 9.4; ED-NEW-dextra-as17-6): the Board's Team tab is the org chart when the home is given a team source, the interim list of AS-8 when
// it is not, and "Show in Team" picks the tab and the node.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-6")]
public sealed class AssistantHomeTeamTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static AssistantHomeView Make(FakeTeamSource? team)
    {
        var parts = new AssistantHomeParts(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls())
        {
            Name = "Dextra",
            Profiles = () => Team(ChannelA, Store),
            Team = team,
            TeamActions = team is null ? null : new TeamActions { Talk = _ => { } },
        };
        return new AssistantHomeView(parts);
    }

    [Fact]
    public void With_a_team_source_the_Team_tab_is_the_org_chart_with_a_node_for_each_assistant()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Make(new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA, Store), [], []) });
            using var stage = new HomeStage(home, 1200, 900);

            var panel = Assert.IsType<TeamPanel>(home.Board.Team.Child);
            Assert.Equal(2, panel.Chart.Kids.Count);
            Assert.Equal("3", panel.CountText);
            Assert.Empty(home.Board.Team.Rows);                                                     // AS-8's interim list is not built
        });
    }

    [Fact]
    public void Without_a_team_source_the_Team_tab_keeps_the_interim_list_of_the_profiles()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Make(null);
            using var stage = new HomeStage(home, 1200, 900);

            Assert.IsNotType<TeamPanel>(home.Board.Team.Child);
            Assert.Equal(2, home.Board.Team.Rows.Count - 1);                                         // Dextra's row and one for each Astur
        });
    }

    [Fact]
    public void Show_in_Team_picks_the_Team_tab_and_selects_that_node()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Make(new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA, Store), [], []) });
            using var stage = new HomeStage(home, 1200, 900);
            Assert.Equal(BoardTab.Missions, home.Board.Tabs.Selected);

            home.ShowTeam("s");
            stage.Layout();

            Assert.Equal(BoardTab.Team, home.Board.Tabs.Selected);
            var panel = Assert.IsType<TeamPanel>(home.Board.Team.Child);
            Assert.Equal("s", panel.Chart.SelectedId);
            Assert.Equal("Store", panel.Detail.Field("Assistant"));
            Assert.True(home.Board.Team.IsVisible);
        });
    }

    [Fact]
    public void The_chart_is_only_shown_while_its_tab_is_picked()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Make(new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA), [], []) });
            using var stage = new HomeStage(home, 1200, 900);

            Assert.False(home.Board.Team.IsVisible);
            Assert.False(home.Board.Team.Child!.IsEffectivelyVisible);
            home.Board.Pick(BoardTab.Team);
            Assert.True(home.Board.Team.Child!.IsEffectivelyVisible);
        });
    }
}
