using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 12.6b "Layout, controls and access", 16 "controls in fixed slots", "inline ask", "heads line up";
// the canvas's rowEl and .tk CSS; ED-NEW-dextra-as8-4): a task row keeps its two control slots in place, shows them on hover, focus,
// touch and hold, names every control after its task, asks inline before a Stop that would cut a turn, and lines its heads up with it.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-4")]
public sealed class TaskRowTests(HeadlessSession headless)
{
    private static (TaskRow Row, FakeMissionJobs Jobs, Button Other, StackPanel Root) Make(MissionJob job)
    {
        var jobs = new FakeMissionJobs();
        jobs.Set(job);
        TaskRow? made = null;
        var row = new TaskRow(jobs, () => { if (made is { } r && jobs.Jobs.FirstOrDefault(j => j.Id == job.Id) is { } now) r.Show(now); });
        made = row;
        row.Show(job);
        var other = new Button { Content = "Elsewhere" };
        var root = new StackPanel { Orientation = Orientation.Vertical, Children = { row, other } };
        return (row, jobs, other, root);
    }

    private static double Shown(Control slots) => slots.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(slots.Opacity);

    private static IDisposable Motion(bool reduce) => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduce });

    [Fact]
    public void Slots_rest_hidden_and_show_on_focus_and_while_a_task_holds_them()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            using var pointer = TaskTouch.Coarse.Override(() => false);
            var task = Job("Fix the login timeout", MissionState.Working);
            var (row, jobs, other, root) = Make(task);
            using var stage = new HomeStage(root, 700);

            Assert.Equal(0, Shown(row.Slots));
            row.Expander.Focus(NavigationMethod.Tab);
            stage.Layout();
            Assert.Equal(1, Shown(row.Slots));
            other.Focus(NavigationMethod.Tab);
            stage.Layout();
            Assert.Equal(0, Shown(row.Slots));

            // a pausing task holds them shown, and so does an open heads panel
            jobs.Replace(Moved(task, MissionState.Pausing));
            row.Show(jobs.Jobs[0]);
            stage.Layout();
            Assert.Equal(1, Shown(row.Slots));
            jobs.Replace(Moved(task, MissionState.Paused));
            row.Show(jobs.Jobs[0]);
            Assert.Equal(0, Shown(row.Slots));
            Click(row.Expander);
            stage.Layout();
            Assert.True(row.Surface.Expanded);
            Assert.Equal(1, Shown(row.Slots));
        });
    }

    [Fact]
    public void With_touch_the_slots_are_always_shown_as_40_px_targets_8_px_apart_and_with_a_pointer_28_px()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            using (TaskTouch.Coarse.Override(() => true))
            {
                var (row, _, _, root) = Make(Job("Fix the login timeout", MissionState.Working));
                using var stage = new HomeStage(root, 700);
                Assert.Equal(1, Shown(row.Slots));
                Assert.Equal(new Size(40, 40), row.Slots.A.Bounds.Size);
                Assert.Equal(new Size(40, 40), row.Slots.B.Bounds.Size);
                Assert.Equal(8, row.Slots.B.Bounds.Left - row.Slots.A.Bounds.Right, 3);
            }
            using (TaskTouch.Coarse.Override(() => false))
            {
                var (row, _, _, root) = Make(Job("Fix the login timeout", MissionState.Working));
                using var stage = new HomeStage(root, 700);
                Assert.Equal(new Size(28, 28), row.Slots.A.Bounds.Size);
                Assert.Equal(new Size(28, 28), row.Slots.B.Bounds.Size);
                Assert.Equal(TaskMetrics.PointerGap, row.Slots.B.Bounds.Left - row.Slots.A.Bounds.Right, 3);
            }
        });
    }

    [Fact]
    public void A_slot_with_no_control_keeps_its_place_invisible_silent_and_out_of_the_tab_order()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            using var pointer = TaskTouch.Coarse.Override(() => false);
            var (row, _, _, root) = Make(Job("Tidy the README", MissionState.Done));
            using var stage = new HomeStage(root, 700);

            foreach (var slot in new[] { row.Slots.A, row.Slots.B })
            {
                Assert.Equal(SlotKind.None, slot.Kind);
                Assert.Equal(0, slot.Button.Opacity);
                Assert.False(slot.Button.IsTabStop);
                Assert.False(slot.Button.IsEnabled);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(slot.Button));
                Assert.Equal(new Size(28, 28), slot.Bounds.Size); // the place is kept
            }
        });
    }

    [Fact]
    public void Every_control_names_its_task_and_its_tip_is_the_short_word()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var task = Job("Next video: Why lenses fog up", MissionState.Working);
            var (row, jobs, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);

            Assert.Equal("Pause Next video: Why lenses fog up", AutomationProperties.GetName(row.Slots.A.Button));
            Assert.Equal("Stop Next video: Why lenses fog up", AutomationProperties.GetName(row.Slots.B.Button));
            Assert.Equal("Pause", ToolTip.GetTip(row.Slots.A.Button));
            Assert.Equal("Stop", ToolTip.GetTip(row.Slots.B.Button));

            jobs.Replace(Moved(task, MissionState.Paused));
            row.Show(jobs.Jobs[0]);
            Assert.Equal("Resume Next video: Why lenses fog up", AutomationProperties.GetName(row.Slots.A.Button));
            Assert.Equal("Resume", ToolTip.GetTip(row.Slots.A.Button));

            jobs.Replace(Moved(task, MissionState.Failed));
            row.Show(jobs.Jobs[0]);
            Assert.Equal("Restart from the brief: Next video: Why lenses fog up", AutomationProperties.GetName(row.Slots.A.Button));
        });
    }

    [Fact]
    public void The_expander_names_the_heads_or_the_one_agent_and_the_mark_is_out_of_the_tree()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var (alone, _, _, rootA) = Make(Job("Fix the login timeout", MissionState.Working));
            var (team, _, _, rootB) = Make(Job("Espresso machines under $800", MissionState.Working, 5, Head("Scout", HeadState.Working), Head("Writer", HeadState.Queued)));
            using var stage = new HomeStage(new StackPanel { Children = { rootA, rootB } }, 700);

            Assert.Equal("Show the agent of Fix the login timeout", AutomationProperties.GetName(alone.Expander));
            Assert.Equal("Show heads of Espresso machines under $800", AutomationProperties.GetName(team.Expander));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(alone.Mark));
            Assert.False(team.Expander.IsExpanded);
            Click(team.Expander);
            Assert.True(team.Expander.IsExpanded);
            Assert.Equal(["Scout", "Writer"], team.Heads.Lines.Select(l => l.NameText));
            Assert.Equal("Pause head Scout in Espresso machines under $800", AutomationProperties.GetName(team.Heads.Lines[0].Slots.A.Button));
        });
    }

    [Fact]
    public void The_state_label_is_text_out_of_the_tab_order_and_its_outgoing_word_leaves_the_tree_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: false);
            var task = Job("Fix the login timeout", MissionState.Working);
            var (row, jobs, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);

            Assert.False(row.Label.Focusable);
            Assert.False(row.Label.IsTabStop);
            Assert.Equal(AutomationControlType.Text, AutomationProperties.GetControlTypeOverride(row.Label));
            Assert.Equal(TaskMetrics.LabelWidth, row.Label.Bounds.Width);
            Assert.Equal("Working", row.Label.Text);
            Assert.Equal("Working", AutomationProperties.GetName(row.Label));

            jobs.Replace(Moved(task, MissionState.Paused));
            row.Show(jobs.Jobs[0]);
            Assert.Equal("Paused", row.Label.Text);
            Assert.Equal("Paused", AutomationProperties.GetName(row.Label));
            var words = row.Label.Words;
            Assert.Equal(2, words.Count); // the old word rolls out by a transform
            Assert.True(words[0].Hidden);
            Assert.Equal("Working", words[0].Word);
            Assert.False(words[1].Hidden);
        });
    }

    [Fact]
    public void A_state_change_never_changes_the_row_size()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: false);
            using var pointer = TaskTouch.Coarse.Override(() => false);
            var task = Job("Fix the login timeout", MissionState.Working) with { ChangedFiles = 3, Branch = "dextra/fix-login" };
            var (row, jobs, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);
            var size = row.Bounds.Size;
            var label = stage.Box(row.Label);
            var slots = stage.Box(row.Slots);

            foreach (var state in new[] { MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Failed, MissionState.NeedsYou, MissionState.Done, MissionState.Working })
            {
                jobs.Replace(Moved(jobs.Jobs[0], state));
                row.Show(jobs.Jobs[0]);
                stage.Layout();
                Assert.Equal(size, row.Bounds.Size);
                Assert.Equal(label, stage.Box(row.Label));
                Assert.Equal(slots, stage.Box(row.Slots));
            }
        });
    }

    [Fact]
    public void A_stop_that_would_cut_a_turn_asks_inline_with_focus_on_Stop_now_and_Escape_keeps_it_running()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var task = Job("Reply to Sam", MissionState.Working) with { Risk = "It is mid-way through sending the reply." };
            var (row, jobs, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);
            var said = new List<string>();
            row.Surface.Said += said.Add;

            row.Slots.B.Button.Focus(NavigationMethod.Tab);
            Click(row.Slots.B.Button);
            stage.Layout();
            Assert.Empty(jobs.Calls); // nothing stopped yet
            Assert.Equal(AskKind.Stop, row.Ask.Kind);
            Assert.True(row.Ask.IsOpen);
            Assert.Equal("Confirm stop", AutomationProperties.GetName(row.Ask));
            Assert.Equal("Stop now? It is mid-way through sending the reply. Pause finishes this turn first and loses nothing.", row.Ask.Sentence);
            Assert.Equal([AskAnswer.StopNow, AskAnswer.PauseInstead, AskAnswer.KeepRunning], row.Ask.Buttons.Keys);
            Assert.Same(row.Ask.Buttons[AskAnswer.StopNow], stage.Focused);
            Assert.Equal("Stop now? It is mid-way through sending the reply. Pause finishes this turn first and loses nothing.", said[^1]);

            stage.Press(Key.Escape);
            Assert.Equal(AskKind.None, row.Ask.Kind);
            Assert.Empty(jobs.Calls);
            Assert.Same(row.Slots.B.Button, stage.Focused); // back to the Stop that opened it

            Click(row.Slots.B.Button);
            stage.Layout();
            Click(row.Ask.Buttons[AskAnswer.StopNow]);
            Assert.Equal("Stop " + task.Id.ToString("N")[..4], Assert.Single(jobs.Calls));
        });
    }

    [Fact]
    public void A_stop_with_nothing_at_risk_stops_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var task = Job("Tidy the README", MissionState.Paused);
            var (row, jobs, _, root) = Make(Moved(task, MissionState.Paused));
            using var stage = new HomeStage(root, 700);

            Click(row.Slots.B.Button);
            Assert.Equal(AskKind.None, row.Ask.Kind);
            Assert.Equal("Stop " + task.Id.ToString("N")[..4], Assert.Single(jobs.Calls));
        });
    }

    [Fact]
    public void A_pausing_task_asks_Stop_now_or_Keep_pausing()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var task = Moved(Job("Reply to Sam", MissionState.Working), MissionState.Pausing) with { Risk = "It is writing the reply." };
            var (row, _, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);

            Click(row.Slots.B.Button);
            stage.Layout();
            Assert.Equal(AskKind.Stop, row.Ask.Kind);
            Assert.Equal("Stop now? It is writing the reply. It is already pausing and finishes the turn first. Stop now cuts it.", row.Ask.Sentence);
            Assert.Equal([AskAnswer.StopNow, AskAnswer.KeepPausing], row.Ask.Buttons.Keys);
        });
    }

    [Fact]
    public void A_heads_stop_that_others_wait_on_asks_under_its_own_line()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var writer = Head("Writer", HeadState.Working, "drafting the summary") with { Waiters = new ValueList<string>(new[] { "Checker" }) };
            var task = Job("Espresso machines under $800", MissionState.Working, 5, writer, Head("Checker", HeadState.Queued));
            var (row, jobs, _, root) = Make(task);
            using var stage = new HomeStage(root, 700);
            Click(row.Expander);
            stage.Layout();
            var line = row.Heads.Line(writer.Thread)!;

            Click(line.Slots.B.Button);
            stage.Layout();
            Assert.Equal((AskKind.Stop, (Guid?)writer.Thread), row.Surface.Asked);
            Assert.Equal(AskKind.None, row.Ask.Kind); // not the task's own ask
            Assert.Equal(AskKind.Stop, line.Ask.Kind);
            Assert.Equal("Confirm stop of Writer", AutomationProperties.GetName(line.Ask));
            Assert.StartsWith("Stop Writer now? Checker waits on it.", line.Ask.Sentence, StringComparison.Ordinal);
            Assert.Same(line.Ask.Buttons[AskAnswer.StopNow], stage.Focused);

            Click(line.Ask.Buttons[AskAnswer.StopNow]);
            Assert.Equal("StopHead " + task.Id.ToString("N")[..4], Assert.Single(jobs.Calls));
        });
    }

    [Fact]
    public void The_still_pausing_note_offers_Stop_instead_and_never_takes_focus()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var task = Moved(Job("Reply to Sam", MissionState.Working), MissionState.Pausing) with { StillPausing = "Still pausing. It finishes writing the reply first." };
            var (row, jobs, other, root) = Make(task);
            using var stage = new HomeStage(root, 700);
            other.Focus(NavigationMethod.Tab);
            row.Show(jobs.Jobs[0]);
            stage.Layout();

            Assert.Equal(AskKind.StillPausing, row.Ask.Kind);
            Assert.Equal("Still pausing. It finishes writing the reply first.", row.Ask.Sentence);
            Assert.Equal([AskAnswer.StopInstead], row.Ask.Buttons.Keys);
            Assert.Same(other, stage.Focused);

            Click(row.Ask.Buttons[AskAnswer.StopInstead]);
            Assert.Equal("Stop " + task.Id.ToString("N")[..4], Assert.Single(jobs.Calls));
        });
    }

    [Fact]
    public void Revert_is_offered_only_on_a_stopped_task_with_kept_changes()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var working = Job("Fix the login timeout", MissionState.Working) with { ChangedFiles = 3 };
            var (row, _, _, root) = Make(working);
            using var stage = new HomeStage(root, 700);
            Assert.Equal(SlotKind.Stop, row.Slots.B.Kind);
            row.Surface.Open(AskKind.Revert, null, null);
            Assert.Equal(AskKind.None, row.Ask.Kind); // refused: not stopped
            Click(row.Expander);
            stage.Layout();
            Assert.False(row.Heads.RevertShown.IsVisible);

            var stopped = Moved(working, MissionState.Stopped);
            row.Show(stopped);
            stage.Layout();
            Assert.Equal(SlotKind.Revert, row.Slots.B.Kind);
            Assert.True(row.Slots.B.IsActionable);
            Assert.True(row.Heads.RevertShown.IsVisible);

            var nothingKept = Moved(working with { ChangedFiles = 0 }, MissionState.Stopped);
            row.Show(nothingKept);
            stage.Layout();
            Assert.False(row.Slots.B.IsActionable);
            Assert.False(row.Heads.RevertShown.IsVisible);
            row.Surface.Open(AskKind.Revert, null, null);
            Assert.Equal(AskKind.None, row.Ask.Kind);
        });
    }

    [Fact]
    public void After_a_revert_focus_lands_on_Restart()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            var stopped = Moved(Job("Fix the login timeout", MissionState.Working) with { ChangedFiles = 3, Branch = "dextra/fix-login" }, MissionState.Stopped);
            var (row, jobs, _, root) = Make(stopped);
            jobs.Apply = (job, kind) => kind == SlotKind.Revert
                ? job with { Reverted = true, Slots = MissionStates.Slots(MissionState.Stopped, true, true) }
                : job;
            using var stage = new HomeStage(root, 700);

            row.Slots.B.Button.Focus(NavigationMethod.Tab);
            Click(row.Slots.B.Button);
            stage.Layout();
            Assert.Equal(AskKind.Revert, row.Ask.Kind);
            Assert.Equal("Revert 3 changed files?", row.Ask.Sentence[..23]);
            Assert.Same(row.Ask.Buttons[AskAnswer.KeepChanges], stage.Focused);

            Click(row.Ask.Buttons[AskAnswer.Revert]);
            stage.Layout();
            Assert.Equal("Revert " + stopped.Id.ToString("N")[..4], Assert.Single(jobs.Calls));
            Assert.Equal(SlotKind.Restart, row.Slots.A.Kind);
            Assert.False(row.Slots.B.IsActionable);
            Assert.Same(row.Slots.A.Button, stage.Focused);
        });
    }

    [Theory]
    [InlineData(1500)]
    [InlineData(1024)]
    [InlineData(750)]
    [InlineData(375)]
    public void Heads_line_up_with_their_task_label_and_slots_at_every_width(double width)
    {
        headless.OnUiThread(() =>
        {
            using var motion = Motion(reduce: true);
            using var pointer = TaskTouch.Coarse.Override(() => false);
            var task = Job("Espresso machines under $800", MissionState.Working, 5, Head("Scout", HeadState.Working, "comparing prices"), Head("Writer", HeadState.Paused));
            var (row, _, _, root) = Make(task);
            row.Narrow = width < TaskMetrics.NarrowWidth;
            using var stage = new HomeStage(root, width);
            Click(row.Expander);
            stage.Layout();

            var label = stage.Box(row.Label);
            var slots = stage.Box(row.Slots);
            Assert.Equal(row.Narrow ? TaskMetrics.NarrowLabelWidth : TaskMetrics.LabelWidth, label.Width);
            Assert.Equal(2, row.Heads.Lines.Count);
            foreach (var line in row.Heads.Lines)
            {
                var headLabel = stage.Box(line.Label);
                var headSlots = stage.Box(line.Slots);
                Assert.Equal(label.Width, headLabel.Width);
                Assert.Equal(label.Right, headLabel.Right, 0.5);
                Assert.Equal(slots.Left, headSlots.Left, 0.5);
                Assert.Equal(slots.Right, headSlots.Right, 0.5);
            }
            Assert.True(slots.Right <= stage.Box(row).Right);
        });
    }
}
