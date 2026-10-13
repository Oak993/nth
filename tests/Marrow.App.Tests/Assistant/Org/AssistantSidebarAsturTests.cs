using Avalonia;
using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1 "The Asturs are nested under it"; ED-NEW-dextra-as17-7): the Assistant row holds the Asturs' tree between itself and the Missions group, binds it again
// whenever the controller starts or stops, and gives it the window's motion budget's calm.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-7")]
public sealed class AssistantSidebarAsturTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static (AssistantSidebarRow Row, FakeTeamSource Team, List<DextraLive> Bound) Make(AsturTreeActions? asturs = null, params MissionJob[] missions)
    {
        var team = new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA, Store), [Task("a", "Voiceover", MissionState.Working)], []) };
        var bound = new List<DextraLive>();
        var row = new AssistantSidebarRow("Dextra", new AssistantRowActions
        {
            Team = live => { bound.Add(live); return team; },
            Asturs = asturs ?? new AsturTreeActions(),
        });
        var jobs = new FakeMissionJobs();
        jobs.Set(missions);
        row.Bind(new DextraLive(jobs, new FakeDecisions(), new FakeGlobalControls()));
        return (row, team, bound);
    }

    [Fact]
    public void The_Asturs_hang_under_the_Assistant_row_and_above_the_Missions_group()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (row, _, _) = Make();
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            Assert.True(row.Asturs.IsVisible);
            Assert.Equal(2, row.Asturs.Rows.Count);
            Assert.True(stage.Box(row.Button).Bottom <= stage.Box(row.Asturs).Y + .5);
            Assert.Equal(["Channel A", "Store"], row.Asturs.Rows.Select(r => r.NameText));
        });
    }

    [Fact]
    public void The_Missions_group_comes_below_the_Asturs_when_both_are_there()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (row, _, _) = Make(null, HomeSamples.Job("Fix the login timeout", MissionState.Working));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            Assert.True(row.MissionsGroup.IsVisible);
            Assert.True(stage.Box(row.Asturs).Bottom <= stage.Box(row.MissionsGroup).Y + .5);
            Assert.True(stage.Box(row.Button).Bottom <= stage.Box(row.Asturs).Y + .5);
        });
    }

    [Fact]
    public void Binding_the_row_again_makes_the_tree_read_the_new_sources_and_the_old_team_is_let_go()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (row, _, bound) = Make();
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);
            Assert.Single(bound);

            var another = new DextraLive(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls());
            row.Bind(another);
            stage.Layout();
            Assert.Equal(2, bound.Count);
            Assert.Same(another, bound[1]);
        });
    }

    [Fact]
    public void With_no_team_source_the_row_has_no_Asturs_group()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var row = new AssistantSidebarRow("Dextra");
            row.Bind(new DextraLive(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            Assert.False(row.Asturs.IsVisible);
            Assert.Empty(row.Asturs.Rows);
        });
    }

    [Fact]
    public void The_Assistant_rows_press_still_opens_the_home_and_an_Astur_rows_press_opens_the_Astur()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var opened = new List<string>();
            var row = new AssistantSidebarRow("Dextra", new AssistantRowActions
            {
                Open = () => opened.Add("home"),
                Team = _ => new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA), [], []) },
                Asturs = new AsturTreeActions { Open = opened.Add },
            });
            row.Bind(new DextraLive(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            Click(row.Button);
            Click(row.Asturs.Rows[0].Button);
            Assert.Equal(["home", "a"], opened);
        });
    }
}
