using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 9.4 "Clicking a node shows its detail: duties, memory count, grants, reserve, and buttons for Talk, Pause and Settings"; ED-NEW-dextra-as17-5): the detail panel under
// the chart. Fixed fields in the canvas's words, buttons that are disabled and never removed, Pause that reads Resume once everything it can reach is paused, Retire that asks first and inline.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-5")]
public sealed class OrgDetailTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static TeamMember Member(AssistantProfile profile, IReadOnlyList<(string AssistantId, MissionJob Job)>? jobs = null, Func<string, TeamFacts>? facts = null, TeamUsage? usage = null) =>
        TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), jobs ?? [], [], usage, false, facts).Find(profile.Id)!;

    private static (OrgDetail Detail, FakeTeamSource Source, List<string> Said, List<string> Acts) Make(TeamActions? actions = null)
    {
        var source = new FakeTeamSource();
        var said = new List<string>();
        var acts = new List<string>();
        var detail = new OrgDetail(source, actions ?? new TeamActions
        {
            Talk = id => acts.Add("Talk " + id),
            Edit = id => acts.Add("Edit " + id),
            Retire = id => { acts.Add("Retire " + id); return System.Threading.Tasks.Task.FromResult<string?>(null); },
        }, said.Add);
        return (detail, source, said, acts);
    }

    [Fact]
    public void The_fields_are_the_canvas_ones_in_the_canvas_words_with_the_reserve_added()
    {
        headless.OnUiThread(() =>
        {
            var facts = (string id) => id == "a" ? new TeamFacts(2, 64, 3, "Keeps 30% on Claude 20x (your standing rule).") : TeamFacts.None;
            var jobs = new[] { Task("a", "Voiceover", MissionState.Working), Task("a", "Stats", MissionState.Paused) };
            var (detail, _, _, _) = Make();
            detail.Show(Member(ChannelA, jobs, facts, Usage(Line(ChannelA, 5.7, .38))), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Assert.Equal(["Assistant", "Area", "Missions · duties", "Memory notes · grants", "Today's usage", "Reserve"], detail.FieldLabels);
            Assert.Equal("Channel A", detail.Field("Assistant"));
            Assert.Equal("Channel A looks after its own area.", detail.Field("Area"));
            Assert.Equal("2 missions · 2 duties", detail.Field("Missions · duties"));
            Assert.Equal("64 notes · 3 grants", detail.Field("Memory notes · grants"));
            Assert.Equal("38% of its daily slice", detail.Field("Today's usage"));
            Assert.Equal("Up to 15% of any account a day. Keeps 30% on Claude 20x (your standing rule).", detail.Field("Reserve"));
            Assert.Contains("Missions · duties", Texts(detail));
            Assert.Contains("Today's usage", Texts(detail));
        });
    }

    [Fact]
    public void Dextra_has_no_slice_of_its_own_and_no_Retire()
    {
        headless.OnUiThread(() =>
        {
            var (detail, _, _, _) = Make();
            var dextra = TeamSnapshot.Build(Team(ChannelA), [], [], Usage(Line(ChannelA, 3, .2)), false).Dextra!;
            detail.Show(dextra, still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Assert.Equal("Dextra", detail.Field("Assistant"));
            Assert.StartsWith("Everything that spans areas", detail.Field("Area"), StringComparison.Ordinal);
            Assert.Equal("No slice of its own: your reserves apply", detail.Field("Reserve"));
            Assert.Equal("3% of the busiest window today, all together", detail.Field("Today's usage"));
            Assert.False(detail.Retire.IsVisible);
            Assert.True(detail.Talk.IsVisible);
        });
    }

    [Fact]
    public void Talk_and_Settings_name_who_they_act_on_and_call_the_host_with_that_assistant()
    {
        headless.OnUiThread(() =>
        {
            var (detail, _, _, acts) = Make();
            detail.Show(Member(ChannelB), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Assert.Equal("Talk to Channel B", AutomationProperties.GetName(detail.Talk));
            Assert.Equal("Open Channel B's settings", AutomationProperties.GetName(detail.Settings));
            Assert.Equal("Retire Channel B", AutomationProperties.GetName(detail.Retire));
            Assert.Equal("Details of Channel B", AutomationProperties.GetName(detail.Child!));
            Click(detail.Talk);
            Click(detail.Settings);
            Assert.Equal(["Talk b", "Edit b"], acts);
        });
    }

    [Fact]
    public void A_button_with_nothing_to_do_is_disabled_and_stays_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var (detail, _, _, _) = Make(new TeamActions());
            detail.Show(Member(ChannelA), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Assert.All(new[] { detail.Talk, detail.Pause, detail.Settings, detail.Retire }, b => Assert.False(b.IsEnabled));
            Assert.All(new[] { detail.Talk, detail.Pause, detail.Settings, detail.Retire }, b => Assert.True(b.IsVisible && b.Bounds.Width > 0));
        });
    }

    [Fact]
    public void Pause_reads_Resume_once_everything_it_can_reach_is_paused_and_the_focus_stays_on_the_button()
    {
        headless.OnUiThread(() =>
        {
            var (detail, source, said, _) = Make();
            var working = new[] { Task("a", "Voiceover", MissionState.Working) };
            detail.Show(Member(ChannelA, working), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Assert.Equal("Pause", detail.Pause.Title);
            Assert.Equal("Pause Channel A's tasks", AutomationProperties.GetName(detail.Pause));
            detail.Pause.Focus();
            Click(detail.Pause);
            Assert.Equal(["Pause a"], source.Calls);
            Assert.Equal(["Pausing a."], said);

            detail.Show(Member(ChannelA, [Task("a", "Voiceover", MissionState.Paused)]), still: true);
            Assert.Equal("Resume", detail.Pause.Title);
            Assert.Equal("Resume Channel A's tasks", AutomationProperties.GetName(detail.Pause));
            Assert.True(detail.Pause.IsEnabled);
            Assert.True(detail.Pause.IsFocused);
            Click(detail.Pause);
            Assert.Equal(["Pause a", "Resume a"], source.Calls);

            detail.Show(Member(ChannelA), still: true);
            Assert.Equal("Pause", detail.Pause.Title);
            Assert.False(detail.Pause.IsEnabled);                                                  // nothing to pause or resume: disabled, not removed
        });
    }

    [Fact]
    public void Retire_asks_first_inline_with_the_focus_on_Keep_it_and_Escape_closes_it_and_gives_the_focus_back()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (detail, _, _, acts) = Make();
            detail.Show(Member(ChannelA), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            detail.Retire.Focus();
            Click(detail.Retire);
            stage.Layout();
            Assert.True(detail.AskOpen);
            Assert.Same(detail.Keep, stage.Focused);                                               // the safe answer has the focus
            Assert.Equal([], acts);
            Assert.Equal("Confirm retiring Channel A", AutomationProperties.GetName(detail.AskPanel));

            stage.Press(Key.Escape);
            Assert.False(detail.AskOpen);
            Assert.Same(detail.Retire, stage.Focused);
            Assert.Equal([], acts);

            Click(detail.Retire);
            stage.Layout();
            Click(detail.Keep);
            Assert.False(detail.AskOpen);
            Assert.Same(detail.Retire, stage.Focused);
            Assert.Equal([], acts);
        });
    }

    [Fact]
    public void Confirming_retires_the_Astur_through_the_controller_and_says_so()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (detail, _, said, acts) = Make();
            detail.Show(Member(ChannelA), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Click(detail.Retire);
            stage.Layout();
            Click(detail.Confirm);
            stage.Layout();

            Assert.Equal(["Retire a"], acts);
            Assert.False(detail.AskOpen);
            Assert.Equal(["Channel A is retired. Its folder and notes stay."], said);
        });
    }

    [Fact]
    public void A_refusal_is_told_in_the_controllers_words_and_the_ask_closes_with_the_focus_on_Retire()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (detail, _, said, _) = Make(new TeamActions { Retire = _ => System.Threading.Tasks.Task.FromResult<string?>("It has missions at work. Stop them first.") });
            detail.Show(Member(ChannelA), still: true);
            using var stage = new HomeStage(detail, 700, 600);

            Click(detail.Retire);
            stage.Layout();
            Click(detail.Confirm);
            stage.Layout();

            Assert.Equal(["It has missions at work. Stop them first."], said);
            Assert.False(detail.AskOpen);
            Assert.Same(detail.Retire, stage.Focused);
            Assert.True(detail.Confirm.IsEnabled && detail.Keep.IsEnabled);
        });
    }

    [Fact]
    public void Showing_another_assistant_closes_an_open_ask_and_changes_the_words_in_place()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var (detail, _, _, _) = Make();
            detail.Show(Member(ChannelA), still: true);
            using var stage = new HomeStage(detail, 700, 600);
            Click(detail.Retire);
            stage.Layout();

            detail.Show(Member(Store), still: true);
            stage.Layout();

            Assert.False(detail.AskOpen);
            Assert.Equal("Store", detail.Field("Assistant"));
            Assert.Equal("Retire Store", AutomationProperties.GetName(detail.Retire));
        });
    }

    [Fact]
    public void The_same_assistant_with_the_same_facts_changes_nothing_and_another_one_comes_in_on_a_short_rise_unless_motion_is_reduced()
    {
        headless.OnUiThread(() =>
        {
            var (detail, _, _, _) = Make();
            var a = Member(ChannelA);
            detail.Show(a, still: false);
            using var stage = new HomeStage(detail, 700, 600);
            var words = detail.Field("Area");

            detail.Show(a, still: false);
            detail.Show(Member(ChannelB), still: false);
            Assert.NotNull(detail.FieldsPanel.Transitions);                                        // opacity eases in
            Assert.True(detail.FieldsPanel.RenderTransform is TranslateTransform { Transitions.Count: 1 });
            Assert.NotEqual(words, detail.Field("Area"));

            using var still = Still();
            detail.Show(Member(Store), still: true);
            Assert.Equal("Store looks after its own area.", detail.Field("Area"));
        });
    }

    [Fact]
    public void At_375_px_with_a_long_area_the_detail_wraps_and_never_runs_past_the_window()
    {
        headless.OnUiThread(() =>
        {
            var long1 = Astur("a", "Channel A", AsturShape.Squircle) with { Area = string.Join(' ', Enumerable.Repeat("scripts, voiceovers, edits, uploads and its numbers,", 8)) };
            var member = TeamSnapshot.Build([Dextra, long1], [], []).Find("a")!;
            var (detail, _, _, _) = Make();
            detail.Show(member, still: true);
            using var stage = new HomeStage(new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 375, 700);

            Assert.True(detail.Bounds.Width <= 375.5);
            Assert.All(Nodes<Control>(detail).Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0), c => Assert.True(stage.Box(c).Right <= 375.5, c.GetType().Name + " " + stage.Box(c)));
        });
    }
}
