using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.1 "Sidebar": the pinned row with Armilla at 24 px, one live status line, a badge for open decisions,
// the collapsed Missions group, the right-click menu; ED-NEW-dextra-as8-8): the row reads the same sources as the home, so they never
// disagree.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-8")]
public sealed class AssistantSidebarRowTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static (AssistantSidebarRow Row, FakeMissionJobs Jobs, FakeDecisions Decisions, FakeGlobalControls Controls) Make(AssistantRowActions? actions = null, params MissionJob[] jobs)
    {
        var source = new FakeMissionJobs();
        source.Set(jobs);
        var decisions = new FakeDecisions();
        var controls = new FakeGlobalControls();
        var row = new AssistantSidebarRow("Dextra", actions);
        row.Bind(new DextraLive(source, decisions, controls));
        return (row, source, decisions, controls);
    }

    [Fact]
    public void The_row_shows_the_mark_the_name_one_status_line_and_a_badge_for_open_decisions()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (row, _, decisions, _) = Make(null, Job("Fix the login timeout", MissionState.Working), Job("Espresso", MissionState.NeedsYou));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 400);

            Assert.Equal(24, row.Mark.MarkSize);
            Assert.Equal(ArmillaKind.Row, row.Mark.Kind);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(row.Mark));
            Assert.Equal(AssistantSidebarRow.RowHeight, row.Button.Bounds.Height);
            Assert.Equal("Dextra", row.NameText.Text);
            Assert.Equal("2 missions · 1 need you", row.StatusText.Text);
            Assert.Equal(ArmillaState.NeedsYou, row.Mark.State);
            Assert.False(row.Badge.IsVisible);
            Assert.Equal("Dextra, 2 missions · 1 need you", AutomationProperties.GetName(row.Button));

            decisions.Add(Reversible("Sign in to the shop's dashboard"));
            stage.Layout();
            Assert.True(row.Badge.IsVisible);
            Assert.Equal("1", row.BadgeText.Text);
            Assert.Equal("1 decision open", AutomationProperties.GetHelpText(row.Button));
        });
    }

    [Fact]
    public void A_note_replaces_the_status_while_Dextra_is_off_or_not_set_up()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            string? note = "Set up in a minute";
            var (row, _, _, _) = Make(new AssistantRowActions { Note = () => note });
            using var stage = new HomeStage(row, 300, 400);
            Assert.Equal("Set up in a minute", row.StatusText.Text);
            note = null;
            row.Refresh();
            Assert.Equal("Ready", row.StatusText.Text);
        });
    }

    [Fact]
    public void A_click_opens_the_home_and_the_selected_row_takes_the_sidebars_selected_weight()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var opened = 0;
            var (row, _, _, _) = Make(new AssistantRowActions { Open = () => opened++ });
            using var stage = new HomeStage(row, 300, 400);

            Click(row.Button);
            Assert.Equal(1, opened);
            Assert.Equal(FontWeight.Medium, row.NameText.FontWeight);
            row.IsSelected = true;
            Assert.Equal(FontWeight.SemiBold, row.NameText.FontWeight);
        });
    }

    [Fact]
    public void The_menu_holds_Open_Talk_Stop_all_Pause_all_Pause_duties_and_Settings()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var said = new List<string>();
            var (bare, _, _, _) = Make(new AssistantRowActions { Open = () => { }, Settings = () => { } });
            Assert.Equal(["Open", "Stop all", "Pause all", "Settings"], bare.MenuItems().Select(i => (string)i.Header!));

            var (row, _, _, controls) = Make(new AssistantRowActions { Open = () => { }, Talk = () => { }, PauseDuties = () => { }, Settings = () => { }, Said = said.Add });
            Assert.Equal(["Open", "Talk", "Stop all", "Pause all", "Pause duties for 1 hour", "Settings"], row.MenuItems().Select(i => (string)i.Header!));
            row.MenuItems()[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(["StopAll Row"], controls.Calls); // Stop all never asks
            Assert.Equal(controls.Stopped!.Strip, said[^1]);

            controls.Toggle = new GlobalToggle(GlobalToggleKind.ResumeAll, false, true);
            var resume = row.MenuItems()[3];
            Assert.Equal("Resume all", resume.Header);
            Assert.True(resume.IsEnabled);
        });
    }

    [Fact]
    public void The_Missions_group_lists_live_lead_chats_then_todays_finished_ones_collapsed_until_opened()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var collapsed = true;
            var opened = new List<Guid>();
            var older = Job("Write the release notes", MissionState.Working, minutesAgo: 30);
            var newer = Job("Fix the login timeout", MissionState.Paused, minutesAgo: 3);
            var done = Job("Tidy the README", MissionState.Done, minutesAgo: 1);
            var yesterday = Job("Old task", MissionState.Done, minutesAgo: 60 * 30) with { Ended = Now.AddDays(-1) };
            var (row, _, _, _) = Make(new AssistantRowActions
            {
                Collapsed = () => collapsed,
                SetCollapsed = c => collapsed = c,
                OpenChat = opened.Add,
            }, older, newer, done, yesterday);
            using var stage = new HomeStage(row, 300, 400);

            Assert.True(row.MissionsGroup.IsVisible);
            Assert.False(row.MissionsOpen);
            Assert.Equal("Missions  3", row.MissionsLabel.Text);
            Assert.Equal(["Fix the login timeout, Paused", "Write the release notes, Working", "Tidy the README, Done"], row.MissionButtons.Select(b => AutomationProperties.GetName(b)));

            Click(row.MissionsToggle);
            Assert.True(row.MissionsOpen);
            Assert.False(collapsed); // remembered
            Click(row.MissionButtons[0]);
            Assert.Equal([newer.LeadThread!.Value], opened);

            var (none, _, _, _) = Make(null, Job("Plain", MissionState.Working) with { LeadThread = null });
            Assert.False(none.MissionsGroup.IsVisible);
        });
    }
}
