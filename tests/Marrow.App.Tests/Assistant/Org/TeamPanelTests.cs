using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 9.4; 16.2 "AS-17: no external dependency"; ED-NEW-dextra-as17-5, -6): the Team tab. The heading with the size of the team and "Add an Astur", the chart, the line for
// no Asturs yet, the detail of the selected node, and a polite line for what Pause, Resume and Retire did. It reads one source, once for a burst of changes, and again every half minute.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-5")]
public sealed class TeamPanelTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static FakeTeamSource Source(params AssistantProfile[] asturs) => new() { Current = TeamSnapshot.Build(Team(asturs), [], []) };

    [Fact]
    public void The_heading_counts_the_team_and_Add_an_Astur_is_beside_it()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var added = 0;
            var panel = new TeamPanel(Source(ChannelA, ChannelB, Store), new TeamActions { Add = () => added++ });
            using var stage = new HomeStage(panel, 900, 900);

            Assert.Contains("Team", Texts(panel));
            Assert.Equal("4", panel.CountText);                                                     // Dextra and three Asturs
            Assert.Equal("4 assistants", AutomationProperties.GetName(panel.CountBox));
            Assert.Equal("Add an Astur", AutomationProperties.GetName(panel.Add));
            Assert.True(panel.Add.IsEnabled);
            Click(panel.Add);
            Assert.Equal(1, added);
            Assert.Equal(3, panel.Chart.Kids.Count);
            Assert.Equal("Team", AutomationProperties.GetName(panel));
        });
    }

    [Fact]
    public void With_no_Asturs_it_says_so_and_how_to_make_one_and_the_detail_is_Dextras()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var panel = new TeamPanel(Source(), new TeamActions { Add = () => { } });
            using var stage = new HomeStage(panel, 900, 900);

            Assert.True(panel.EmptyHintShown);
            Assert.Contains(OrgChartWords.NoAsturs, Texts(panel));
            Assert.Equal("1", panel.CountText);
            Assert.Equal("Dextra", panel.Detail.Field("Assistant"));
            Assert.Empty(panel.Chart.Kids);
        });
    }

    [Fact]
    public void Add_waits_with_its_reason_once_there_are_six_Asturs_and_without_a_host_it_is_disabled_rather_than_removed()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var six = new[] { ChannelA, ChannelB, Store, Astur("p", "Desk", AsturShape.Star), Astur("q", "Ledger", AsturShape.Clover), Astur("r", "Inbox", AsturShape.RoundedTriangle) };
            var full = new TeamPanel(Source(six), new TeamActions { Add = () => { } });
            using var stage = new HomeStage(full, 900, 900);

            Assert.False(full.Add.IsEnabled);
            Assert.Equal(OrgChartWords.AllSixMade, AutomationProperties.GetHelpText(full.Add));
            Assert.Equal(OrgChartWords.AllSixMade, ToolTip.GetTip(full.Add));
            Assert.True(full.Add.IsVisible && full.Add.Bounds.Width > 0);

            var bare = new TeamPanel(Source(ChannelA));
            using var stage2 = new HomeStage(bare, 900, 900);
            Assert.False(bare.Add.IsEnabled);
            Assert.Equal("", AutomationProperties.GetHelpText(bare.Add));
        });
    }

    [Fact]
    public void Choosing_a_node_shows_its_detail_and_the_chart_keeps_the_selection_when_the_team_is_read_again()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(ChannelA, Store);
            var panel = new TeamPanel(source);
            using var stage = new HomeStage(panel, 900, 900);

            Click(panel.Chart.Kids[1].Face);
            Assert.Equal("Store", panel.Detail.Field("Assistant"));
            source.Set(TeamSnapshot.Build(Team(ChannelA, Store), [Task("s", "Invoices", MissionState.Working)], []));
            stage.Layout();
            Assert.Equal("Store", panel.Detail.Field("Assistant"));
            Assert.Equal("s", panel.Chart.SelectedId);
            Assert.Equal("1 mission · 0 duties", panel.Detail.Field("Missions · duties"));    // the detail follows the new facts
        });
    }

    [Fact]
    public void A_burst_of_changes_reads_the_team_once_and_a_detached_panel_reads_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(ChannelA);
            var panel = new TeamPanel(source);
            using var stage = new HomeStage(panel, 900, 900);
            var reads = source.Reads;

            source.Raise();
            source.Raise();
            source.Raise();
            stage.Layout();
            Assert.Equal(reads + 1, source.Reads);

            stage.Window.Content = null;
            stage.Layout();
            var after = source.Reads;
            source.Raise();
            stage.Layout();
            Assert.Equal(after, source.Reads);
        });
    }

    [Fact]
    public void Todays_use_is_read_again_every_half_minute_while_the_panel_is_on_a_window()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(ChannelA);
            var panel = new TeamPanel(source);
            using var stage = new HomeStage(panel, 900, 900);
            var reads = source.Reads;

            stage.Advance(TimeSpan.FromSeconds(29));
            Assert.Equal(reads, source.Reads);
            stage.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(reads + 1, source.Reads);
            stage.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(reads + 2, source.Reads);
        });
    }

    [Fact]
    public void Pause_says_what_it_did_in_one_polite_live_region_and_the_same_words_twice_still_announce()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = TeamSnapshot.Build(Team(ChannelA), [Task("a", "Voiceover", MissionState.Working)], []) };
            var panel = new TeamPanel(source);
            using var stage = new HomeStage(panel, 900, 900);

            Click(panel.Chart.Kids[0].Face);
            Click(panel.Detail.Pause);
            Assert.Equal("Pausing a.", panel.Live.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(panel.Live));
            panel.Say("Pausing a.");
            Assert.Equal("Pausing a.", panel.Live.Text);
        });
    }

    [Fact]
    public void A_profile_file_nth_could_not_use_is_told_under_the_chart()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var panel = new TeamPanel(Source(ChannelA), problems: () => ["The profile of x could not be read: access denied.", "Old belongs to no Dextra nth knows, so it is left out."]);
            using var stage = new HomeStage(panel, 900, 900);

            Assert.Equal("The profile of x could not be read: access denied. Old belongs to no Dextra nth knows, so it is left out.", panel.TroubleText);
            Assert.Contains("The profile of x could not be read: access denied. Old belongs to no Dextra nth knows, so it is left out.", Texts(panel));
        });
    }

    [Fact]
    public void At_375_px_the_tab_is_the_list_and_nothing_runs_past_the_window()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var panel = new TeamPanel(Source(ChannelA, ChannelB, Store), new TeamActions { Add = () => { }, Edit = _ => { }, Talk = _ => { } });
            using var stage = new HomeStage(new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 375, 900);

            Assert.True(panel.Chart.Panel.IsList);
            Assert.All(Nodes<Control>(panel).Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c is not OrgConnectors), c => Assert.True(stage.Box(c).Right <= 375.5, c.GetType().Name + " " + stage.Box(c)));
        });
    }
}
