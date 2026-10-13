using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.2 "Home view" and "Narrow windows", 6 the Result list, 12.6 "Stop all never asks", 12.6b the
// global toggle, 16 "a polite live region"; ED-NEW-dextra-as8-7): the home's header names Dextra and its state, Stop all stops at
// once, the toggle flips to Resume all, every change is said in one polite live region, and below 900 px the Board stacks.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-7")]
public sealed class AssistantHomeViewTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static (AssistantHomeView Home, FakeMissionJobs Jobs, FakeGlobalControls Controls) Make(Func<AssistantHomeParts, AssistantHomeParts>? change = null, params MissionJob[] jobs)
    {
        var source = new FakeMissionJobs();
        source.Set(jobs);
        var controls = new FakeGlobalControls();
        var parts = new AssistantHomeParts(source, new FakeDecisions(), controls) { Name = DextraHost.Name };
        return (new AssistantHomeView(change?.Invoke(parts) ?? parts), source, controls);
    }

    [Fact]
    public void The_header_names_Dextra_and_its_state_and_the_Result_list_is_headed_Result()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, _, _) = Make(null, Job("Fix the login timeout", MissionState.Working), Job("Write the notes", MissionState.Working), Job("Espresso", MissionState.NeedsYou));
            using var stage = new HomeStage(home, 1200, 900);

            Assert.Contains("Dextra", Texts(home));
            Assert.Equal("3 missions · 1 need you", home.StateLine);
            Assert.Equal(ArmillaState.NeedsYou, home.HeaderMark.State);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(home.HeaderMark));
            Assert.Equal("Result", home.Result.Heading.Text);
            Assert.Equal(2, AutomationProperties.GetHeadingLevel(home.Result.Heading));
            Assert.Equal("Dextra home", AutomationProperties.GetName(home));
        });
    }

    [Fact]
    public void Stop_all_stops_at_once_says_what_it_stopped_and_opens_the_Result_list()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, _, controls) = Make(null, Job("Fix the login timeout", MissionState.Working));
            using var stage = new HomeStage(home, 1200, 900);
            Assert.False(home.Result.IsOpen);

            Click(home.StopAllButton);
            stage.Layout();
            Assert.Equal(["StopAll Header"], controls.Calls); // no ask, no dialog
            Assert.Equal(controls.Stopped!.Strip, home.Live.Text);
            Assert.True(home.Strip.IsOpen);
            Assert.True(home.Result.IsOpen);
            Assert.StartsWith("Stopped", home.StateLine, StringComparison.Ordinal);
            Assert.Equal("Stop all", AutomationProperties.GetName(home.StopAllButton));
        });
    }

    [Fact]
    public void The_toggle_reads_Pause_all_and_flips_to_Resume_all_when_every_live_task_is_held()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, _, controls) = Make(null, Job("Fix the login timeout", MissionState.Working));
            using var stage = new HomeStage(home, 1200, 900);

            Assert.Equal(SlotKind.Pause, home.Toggle.Kind);
            Assert.Equal("Pause all", AutomationProperties.GetName(home.Toggle.Button));
            Click(home.Toggle.Button);
            Assert.Equal("PauseAll Pause all button", controls.Calls[^1]);
            Assert.Equal("Pausing 2 tasks.", home.Live.Text);

            controls.Toggle = new GlobalToggle(GlobalToggleKind.ResumeAll, false, true);
            controls.Raise();
            stage.Layout();
            Assert.Equal(SlotKind.Resume, home.Toggle.Kind);
            Assert.Equal("Resume all", AutomationProperties.GetName(home.Toggle.Button));
            Click(home.Toggle.Button);
            Assert.Equal("ResumeAll", controls.Calls[^1]);
            Assert.Equal("Resumed 2 tasks.", home.Live.Text);
        });
    }

    [Fact]
    public void One_polite_live_region_says_each_change_even_the_same_words_twice()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var task = Job("Fix the login timeout", MissionState.Working);
            var (home, jobs, _) = Make(null, task);
            using var stage = new HomeStage(home, 1200, 900);
            var said = new List<string>();
            home.Live.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty && home.Live.Text is { Length: > 0 } t) said.Add(t); };

            jobs.Replace(Moved(task, MissionState.Pausing));
            stage.Layout();
            Assert.Equal("Pausing: Fix the login timeout", home.Live.Text);

            home.Announce("Resumed 2 tasks.");
            home.Announce("Resumed 2 tasks.");
            Assert.Equal(["Pausing: Fix the login timeout", "Resumed 2 tasks.", "Resumed 2 tasks."], said);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(home.Live));
        });
    }

    [Fact]
    public void Below_900_px_the_Board_stacks_under_the_conversation()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (home, _, _) = Make(null, Job("Fix the login timeout", MissionState.Working));
            using var stage = new HomeStage(home, 1200, 900);

            Assert.False(home.Stacked);
            var talk = stage.Box(home.ConversationColumn);
            var board = stage.Box(home.BoardColumn);
            Assert.Equal(400, board.Width, 0.5);
            Assert.True(board.Left >= talk.Right);

            stage.Resize(800, 1200);
            Assert.True(home.Stacked);
            talk = stage.Box(home.ConversationColumn);
            board = stage.Box(home.BoardColumn);
            Assert.True(board.Top >= talk.Bottom);
            Assert.Equal(talk.Width, board.Width, 0.5);

            stage.Resize(1200, 900);
            Assert.False(home.Stacked);
        });
    }

    [Fact]
    public void The_menu_offers_Reserves_Activity_log_and_Settings_and_Duties_and_Memory_only_when_given()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var opened = new List<string>();
            var (home, _, _) = Make(p => p with { Menu = new HomeMenu(ActivityLog: () => opened.Add("log"), Settings: () => opened.Add("settings")) });
            using var stage = new HomeStage(home, 1200, 900);

            var items = home.MenuItems();
            Assert.Equal(["Reserves", "Activity log", "Settings"], items.Select(i => (string)i.Header!));
            Assert.False(items[0].IsEnabled); // no reserve yet: shown, not offered
            Assert.True(items[1].IsEnabled);
            Assert.Equal("More: Reserves, Activity log, Settings", AutomationProperties.GetName(home.MenuButton));
            items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(["log"], opened);

            var (full, _, _) = Make(p => p with { Menu = new HomeMenu(Duties: () => { }, Memory: () => { }) });
            Assert.Equal(["Duties", "Memory", "Reserves", "Activity log", "Settings"], full.MenuItems().Select(i => (string)i.Header!));
        });
    }

    [Fact]
    public void The_hero_shows_while_the_conversation_is_empty_and_its_suggestions_send_through_the_home_thread()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var empty = true;
            var said = new List<string>();
            var card = new TextBlock { Text = "first run" };
            Control? firstRun = card;
            var (home, _, _) = Make(p => p with { ConversationEmpty = () => empty, Say = said.Add, FirstRun = () => firstRun });
            using var stage = new HomeStage(home, 1200, 900);

            Assert.True(home.HeroShown);
            Assert.Equal("Ask for anything: work, life or the world", home.Caption);
            Assert.True(card.IsEffectivelyVisible);
            var chip = Nodes<Button>(home.Chips).First(b => AutomationProperties.GetName(b) == "What needs me?");
            Click(chip);
            Assert.Equal(["What needs me?"], said);

            empty = false;
            firstRun = null;
            home.RefreshConversation();
            stage.Layout();
            Assert.False(home.HeroShown);
            Assert.False(Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(card)); // the card left the home
            Assert.Null(card.Parent);
        });
    }
}
