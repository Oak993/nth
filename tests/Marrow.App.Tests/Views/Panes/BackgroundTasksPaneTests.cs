using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using static Marrow.App.Tests.Fixtures.FakeJobSource;

namespace Marrow.App.Tests.Views.Panes;

// Web surfaces W5 (DESIGN 3.10, 12 "Background tasks ticking: 4 Hz, no layout pass", 2.6 motion, 10 accessibility; the approved canvas
// "background tasks"; ED-NEW-web-w5-1): the pane lists every job live, a tick only changes words (nothing asks for a layout), cards glide
// to their slots on interruptible springs and fade in and out, Stop and Pause are wired through the list, and the pane holds the
// 4 Hz ticker only while it is on screen.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class BackgroundTasksPaneTests(HeadlessSession headless)
{
    private static DateTimeOffset Now => SwiftClock.Provider.Current.GetUtcNow();

    private static readonly Guid ThreadA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ThreadB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static void Seed(JobPaneRig rig)
    {
        rig.Source.Jobs.Add(Job("head", started: Now.AddSeconds(-725), title: "Port settings tests · group A") with
        {
            Subject = "Otto", Tokens = 165_400, ToolUses = 132, Activity = "Reading", TranscriptThreadId = Guid.NewGuid(),
        });
        rig.Source.Jobs.Add(Job("tactus", BackgroundJobKind.Tactus, Now.AddSeconds(-161), title: "Fill the Q3 sheet") with
        {
            Subject = "Excel", Tokens = 41_200, ToolUses = 38, Activity = "Typing in C7", CanPause = true, TranscriptThreadId = Guid.NewGuid(),
        });
        rig.Source.Jobs.Add(Job("dev", BackgroundJobKind.Terminal, Now.AddSeconds(-1084), title: "npm run dev") with
        {
            KindLabel = "Dev server", Subject = "localhost:5173", Activity = "Running", DevServerUrl = "http://localhost:5173/",
        });
        rig.Source.Jobs.Add(Job("download", BackgroundJobKind.Download, Now.AddSeconds(-213), title: "Qwen 3 8B (Q4)") with
        {
            KindLabel = "Download", Subject = "3.1 of 5.0 GB", Progress = 0.62, CanPause = true,
        });
        rig.Source.Jobs.Add(Job("helper", BackgroundJobKind.Helper, Now.AddMinutes(-9), state: BackgroundJobState.Done, title: "Review the diff") with
        {
            FinishedAt = Now.AddMinutes(-4),
        });
    }

    // ---- the cards ----

    [Fact]
    public void Every_kind_of_task_is_listed_as_the_canvas_draws_it_running_first_by_start_then_the_finished_ones() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        Assert.Equal(["dev", "head", "download", "tactus", "helper"], rig.Stack.Cards.Select(card => card.JobId));

        var head = rig.Card("head");
        Assert.Equal("Port settings tests · group A", head.TitleBlock.Text);
        Assert.Equal("Hydra head · Otto", head.KindText.Text);
        Assert.Equal("12m 05s", head.ElapsedText.Text);
        Assert.Equal("165.4k tokens", head.TokensText.Text);
        Assert.Equal("132 tool uses", head.ToolsText.Text);
        Assert.Equal("Reading", head.ActivityText.Text);
        Assert.True(head.ActivityText.IsShining);
        Assert.True(head.StopButton.IsVisible);
        Assert.False(head.PauseButton.IsVisible);
        Assert.True(head.TranscriptButton.IsVisible);
        Assert.False(head.PreviewButton.IsVisible);
        Assert.False(head.PercentText.IsVisible);
        Assert.False(head.ProgressBar.IsVisible);

        var tactus = rig.Card("tactus");
        Assert.True(tactus.PauseButton.IsVisible && tactus.StopButton.IsVisible);
        Assert.Equal("Tactus · Excel", tactus.KindText.Text);
        Assert.Equal("Typing in C7", tactus.ActivityText.Text);

        var dev = rig.Card("dev");
        Assert.Equal("Dev server · localhost:5173", dev.KindText.Text);
        Assert.Equal("18m 04s", dev.ElapsedText.Text);
        Assert.False(dev.TokensText.IsVisible || dev.ToolsText.IsVisible);
        Assert.True(dev.PreviewButton.IsVisible);
        Assert.False(dev.TranscriptButton.IsVisible);
        Assert.False(dev.PauseButton.IsVisible);

        var download = rig.Card("download");
        Assert.Equal("Download · 3.1 of 5.0 GB", download.KindText.Text);
        Assert.True(download.ProgressBar.IsVisible);
        Assert.Equal(0.62, download.ProgressBar.Value, 3);
        Assert.Equal("62 %", download.PercentText.Text);
        Assert.True(download.PauseButton.IsVisible && download.StopButton.IsVisible);
        Assert.False(download.ActivityText.IsVisible);

        var done = rig.Card("helper");
        Assert.True(done.IsFinishedLook);
        Assert.Equal("Done 4 min ago", done.EndedText.Text);
        Assert.True(done.EndedText.IsVisible);
        Assert.False(done.ElapsedText.IsVisible || done.ActivityText.IsVisible || done.StopButton.IsVisible || done.PauseButton.IsVisible);
        Assert.False(done.TranscriptButton.IsVisible);
        Assert.False(rig.View.IsEmptyShown);
    });

    [Fact]
    public void A_tick_changes_the_words_and_asks_for_no_layout_anywhere() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        var measures = rig.Stack.Measures;
        var head = rig.Card("head");
        var fixedWidths = new[] { head.ElapsedText, head.TokensText, head.ToolsText, rig.Card("download").PercentText }.Select(text => text.Bounds.Width).ToArray();

        rig.Source.Replace("head", job => job with { Tokens = 170_000, ToolUses = 133 });
        rig.Source.Replace("download", job => job with { Progress = 0.65, Subject = "3.2 of 5.0 GB" });
        // The ticker's own path: the clock moves, the timer fires, the main actor refreshes. No layout runs after it.
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(250));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("12m 05s", head.ElapsedText.Text); // 250 ms on: the same second
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(750));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("12m 06s", head.ElapsedText.Text);
        Assert.Equal("170.0k tokens", head.TokensText.Text);
        Assert.Equal("133 tool uses", head.ToolsText.Text);
        Assert.Equal("65 %", rig.Card("download").PercentText.Text);
        Assert.Equal("Download · 3.2 of 5.0 GB", rig.Card("download").KindText.Text);
        Assert.Equal(0.65, rig.Card("download").ProgressBar.Value, 3);

        // Nothing is waiting to be measured or arranged: every live text, every card, the stack and the pane are still valid.
        foreach (var card in rig.Stack.Cards)
        {
            Assert.True(card.IsMeasureValid && card.IsArrangeValid, card.JobId);
            // (A hidden control is never measured, so only the shown ones are asked.)
            foreach (var live in new[] { card.ElapsedText, card.TokensText, card.ToolsText, card.PercentText, card.EndedText, card.KindText }.Where(live => live.IsVisible))
                Assert.True(live.IsMeasureValid && live.IsArrangeValid, card.JobId + " " + live.Text);
            Assert.True(card.MetricsLine.IsMeasureValid && card.MetricsLine.IsArrangeValid, card.JobId);
        }
        Assert.True(rig.Stack.IsMeasureValid && rig.Stack.IsArrangeValid && rig.View.IsMeasureValid && rig.View.IsArrangeValid);
        Assert.Equal(measures, rig.Stack.Measures);
        rig.Layout();
        Assert.Equal(measures, rig.Stack.Measures);
        Assert.Equal(fixedWidths, new[] { head.ElapsedText, head.TokensText, head.ToolsText, rig.Card("download").PercentText }.Select(text => text.Bounds.Width));
    });

    [Fact]
    public void Live_text_never_changes_its_size_whatever_it_says() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        var elapsed = rig.Card("head").ElapsedText;
        var size = elapsed.DesiredSize;
        foreach (var text in new[] { "0m 00s", "59m 59s", "1h 04m", "99h 59m", "" })
        {
            elapsed.Text = text;
            Assert.True(elapsed.IsMeasureValid && elapsed.IsArrangeValid, text);
            rig.Layout();
            Assert.Equal(size, elapsed.DesiredSize);
        }
        var kind = rig.Card("head").KindText;
        var kindSize = kind.Bounds.Size;
        kind.Text = "A much longer line than the card is wide, which is trimmed and never wraps or grows the card around it";
        rig.Layout();
        Assert.Equal(kindSize, kind.Bounds.Size);
    });

    [Fact]
    public void The_activity_takes_what_is_left_of_the_first_line_or_its_own_line_and_never_decides_where_things_wrap() => headless.OnUiThread(() =>
    {
        foreach (var width in new[] { 280.0, 320.0, 360.0, 560.0 })
        {
            using var rig = new JobPaneRig(width: width);
            Seed(rig);
            rig.Refresh();
            var head = rig.Card("head");
            var before = head.MetricsLine.Bounds.Height;
            var placed = new Control[] { head.ElapsedText, head.TokensText, head.ToolsText, head.ActivityText }.Select(control => control.Bounds).ToArray();
            rig.Source.Replace("head", job => job with { Activity = "Running a command with an unusually long description of what it does" });
            rig.Refresh();
            Assert.Equal(before, head.MetricsLine.Bounds.Height);
            Assert.Equal(placed.Take(3), new Control[] { head.ElapsedText, head.TokensText, head.ToolsText }.Select(control => control.Bounds));
            Assert.True(head.ActivityText.Bounds.Right <= head.MetricsLine.Bounds.Width + 0.5, $"{width}: the activity stays inside the card");
            // No item overlaps another.
            var rects = new Control[] { head.ElapsedText, head.TokensText, head.ToolsText, head.ActivityText }.Select(control => control.Bounds).ToArray();
            for (var i = 0; i < rects.Length; i++)
                for (var j = i + 1; j < rects.Length; j++)
                    Assert.False(rects[i].Intersects(rects[j]), $"{width}: {i} and {j} overlap");
        }
    });

    [Fact]
    public void Cards_stack_in_order_with_6_DIP_between_them_and_none_overlaps_at_rest() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        rig.Settle();
        var cards = rig.Stack.Cards;
        double? previousBottom = null;
        foreach (var card in cards)
        {
            var bounds = rig.BoundsOf(card);
            if (previousBottom is { } bottom) Assert.Equal(bottom + 6, bounds.Y, 1);
            Assert.Equal(rig.Stack.Bounds.Width, bounds.Width, 1);
            previousBottom = bounds.Bottom;
        }
        Assert.Equal(previousBottom!.Value, rig.Stack.Bounds.Height, 1);
    });

    // ---- the actions ----

    [Fact]
    public void Stop_asks_the_list_and_the_card_reads_Stopping_with_its_buttons_off_until_the_source_says_it_is_over() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        var head = rig.Card("head");
        Assert.Equal("Stop Port settings tests · group A", Avalonia.Automation.AutomationProperties.GetName(head.StopButton));
        JobPaneRig.Click(head.StopButton);
        rig.Layout();
        Assert.Equal(["stop head"], rig.Calls);
        Assert.Equal(["head"], rig.Source.Stopped);
        Assert.False(head.StopButton.IsEnabled);
        Assert.True(head.StopButton.IsVisible);
        Assert.Equal("Stopping…", head.ActivityText.Text);
        Assert.False(head.ActivityText.IsShining);

        rig.Source.Replace("head", job => job with { State = BackgroundJobState.Stopped, FinishedAt = Now, CanStop = false });
        rig.Refresh();
        Assert.True(rig.Card("head").IsFinishedLook);
        Assert.False(rig.Card("head").StopButton.IsVisible);
        Assert.Equal("Stopped just now", rig.Card("head").EndedText.Text);
        // Finished cards come after the running ones, the one that ended last first.
        Assert.Equal(["head", "helper"], rig.Stack.Cards.Select(card => card.JobId).TakeLast(2));
    });

    [Fact]
    public void Pause_becomes_Resume_and_the_activity_reads_Paused() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        var download = rig.Card("download");
        Assert.Equal("Pause Qwen 3 8B (Q4)", Avalonia.Automation.AutomationProperties.GetName(download.PauseButton));
        JobPaneRig.Click(download.PauseButton);
        rig.Layout();
        Assert.Equal(["download"], rig.Source.Paused);
        Assert.Equal("Resume Qwen 3 8B (Q4)", Avalonia.Automation.AutomationProperties.GetName(download.PauseButton));
        Assert.Equal("Paused", download.ActivityText.Text);
        Assert.True(download.ActivityText.IsVisible);
        Assert.False(download.ActivityText.IsShining);
        Assert.True(download.PercentText.IsVisible); // the figure stays

        JobPaneRig.Click(download.PauseButton);
        rig.Layout();
        Assert.Equal(["download"], rig.Source.Resumed);
        Assert.Equal("Pause Qwen 3 8B (Q4)", Avalonia.Automation.AutomationProperties.GetName(download.PauseButton));
        Assert.False(download.ActivityText.IsVisible);
    });

    [Fact]
    public void A_refused_stop_leaves_the_card_as_it_was() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Source.Accept = false;
        rig.Refresh();
        var head = rig.Card("head");
        JobPaneRig.Click(head.StopButton);
        rig.Layout();
        Assert.True(head.StopButton.IsEnabled);
        Assert.Equal("Reading", head.ActivityText.Text);
        Assert.True(head.ActivityText.IsShining);
    });

    [Fact]
    public void View_transcript_and_Open_preview_reach_their_commands() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        JobPaneRig.Click(rig.Card("head").TranscriptButton);
        JobPaneRig.Click(rig.Card("dev").PreviewButton);
        Assert.Equal(["transcript head", "preview http://localhost:5173/"], rig.Calls);
    });

    // ---- the filter, the empty word and Clear done ----

    [Fact]
    public void The_filter_shows_this_threads_tasks_and_the_app_wide_ones_or_all_of_them_and_the_choice_is_kept() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig(thread: ThreadA, scope: BackgroundJobScope.ThisThread);
        rig.Source.Jobs.Add(Job("a", thread: ThreadA, started: Now.AddMinutes(-3)));
        rig.Source.Jobs.Add(Job("b", thread: ThreadB, started: Now.AddMinutes(-2)));
        rig.Source.Jobs.Add(Job("wide", BackgroundJobKind.Download, Now.AddMinutes(-1)));
        rig.Refresh();
        Assert.Equal(["a", "wide"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.True(rig.View.ThisThreadButton.IsChecked);
        Assert.False(rig.View.AllThreadsButton.IsChecked);

        JobPaneRig.Click(rig.View.AllThreadsButton);
        rig.Layout();
        rig.Settle();
        Assert.Equal(["a", "b", "wide"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.Equal(BackgroundJobScope.AllThreads, BackgroundTasksPane.Scope);
        Assert.True(rig.View.AllThreadsButton.IsChecked);
        Assert.False(rig.View.ThisThreadButton.IsChecked);

        JobPaneRig.Click(rig.View.AllThreadsButton); // the chosen segment stays chosen
        Assert.True(rig.View.AllThreadsButton.IsChecked);
        JobPaneRig.Click(rig.View.ThisThreadButton);
        rig.Layout();
        rig.Settle();
        Assert.Equal(["a", "wide"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.Equal(BackgroundJobScope.ThisThread, BackgroundTasksPane.Scope);
    });

    [Fact]
    public void With_nothing_to_show_the_pane_says_so_and_Clear_done_appears_with_the_first_finished_task() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        var has = new List<bool>();
        rig.View.HasFinishedChanged += has.Add;
        rig.Refresh();
        Assert.True(rig.View.IsEmptyShown);
        Assert.Empty(rig.Stack.Cards);

        rig.Source.Jobs.Add(Job("run"));
        rig.Source.Jobs.Add(Job("old", state: BackgroundJobState.Done));
        rig.Refresh();
        Assert.False(rig.View.IsEmptyShown);
        Assert.True(rig.View.HasFinished);
        Assert.Equal([true], has);

        rig.View.ClearDone();
        rig.Layout();
        Assert.Equal(["old"], rig.Source.Dismissed);
        rig.Settle();
        Assert.Equal(["run"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.False(rig.View.HasFinished);
        Assert.Equal([true, false], has);
    });

    // ---- the ticker belongs to the pane on screen ----

    [Fact]
    public void The_pane_holds_the_4_Hz_ticker_only_while_it_is_on_screen() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Assert.Equal(1, rig.Jobs.Leases);
        Assert.Equal(1, rig.Runtime.Time.ActiveTimers);
        rig.Window.Content = null;
        rig.Layout();
        Assert.Equal(0, rig.Jobs.Leases);
        Assert.Equal(0, rig.Runtime.Time.ActiveTimers);
        rig.Window.Content = rig.View;
        rig.Layout();
        Assert.Equal(1, rig.Jobs.Leases);
        Assert.Equal(1, rig.Runtime.Time.ActiveTimers);
    });

    [Fact]
    public void Ticks_keep_the_list_current_without_anyone_asking() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        rig.Refresh();
        Assert.Empty(rig.Stack.Cards);
        rig.Source.Jobs.Add(Job("new"));
        rig.Tick();
        Assert.Equal(["new"], rig.Stack.Cards.Select(card => card.JobId));
        rig.Source.Replace("new", job => job with { State = BackgroundJobState.Done, FinishedAt = Now });
        rig.Tick();
        Assert.True(rig.Card("new").IsFinishedLook);
        rig.Runtime.Advance(BackgroundJobText.DoneLifetime);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        rig.Settle();
        Assert.Empty(rig.Stack.Cards);
        Assert.True(rig.View.IsEmptyShown);
    });

    // ---- motion ----

    private static double VisualY(JobPaneRig rig, string id) => rig.BoundsOf(rig.Card(id)).Y + rig.Stack.LookOf(id)!.Value.Offset;

    [Fact]
    public void A_new_card_fades_and_scales_in_while_the_others_glide_to_their_new_slots_and_everything_lands_on_the_layout() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        rig.Source.Jobs.Add(Job("b", started: Now.AddMinutes(-20)));
        rig.Source.Jobs.Add(Job("c", started: Now.AddMinutes(-10)));
        rig.Refresh();
        rig.Settle();
        var restB = rig.BoundsOf(rig.Card("b")).Y;

        rig.Source.Jobs.Add(Job("a", started: Now.AddMinutes(-30))); // the oldest start: it joins at the top
        rig.Refresh();
        var fresh = rig.Stack.LookOf("a")!.Value;
        Assert.Equal(0, fresh.Opacity, 3);
        Assert.Equal(JobStack.OpenScale, fresh.Scale, 3);
        Assert.Equal(0, fresh.Offset, 3); // it appears at its slot
        var gliding = rig.Stack.LookOf("b")!.Value;
        Assert.True(gliding.Offset < -10, $"b starts where it was: {gliding.Offset}");
        Assert.Equal(restB, VisualY(rig, "b"), 0.5); // and has not jumped

        var previous = VisualY(rig, "b");
        var frames = 0;
        while (rig.Stack.IsMoving && frames++ < 300)
        {
            rig.Frame();
            var now = VisualY(rig, "b");
            Assert.True(now >= previous - 0.5 || now >= rig.BoundsOf(rig.Card("b")).Y - 6, "b only moves toward its slot (a spring may overshoot a little)");
            previous = now;
        }
        Assert.InRange(frames, 5, 80);
        foreach (var id in new[] { "a", "b", "c" })
        {
            var look = rig.Stack.LookOf(id)!.Value;
            Assert.Equal((1.0, 1.0, 0.0), (look.Opacity, look.Scale, look.Offset));
        }
        Assert.Equal(["a", "b", "c"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.True(rig.BoundsOf(rig.Card("a")).Bottom + 6 <= rig.BoundsOf(rig.Card("b")).Y + 0.5);
    });

    [Fact]
    public void A_card_that_goes_fades_and_scales_out_while_the_others_close_the_gap_and_is_then_removed() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        foreach (var (id, minutes) in new[] { ("a", 30), ("b", 20), ("c", 10) }) rig.Source.Jobs.Add(Job(id, started: Now.AddMinutes(-minutes)));
        rig.Refresh();
        rig.Settle();
        var leaving = rig.Card("b");
        var restC = rig.BoundsOf(rig.Card("c")).Y;

        rig.Source.Jobs.RemoveAt(1);
        rig.Refresh();
        Assert.Equal(["a", "c"], rig.Stack.Cards.Select(card => card.JobId));
        Assert.Contains(leaving, rig.Stack.Children); // still there, fading
        Assert.False(leaving.IsHitTestVisible);
        Assert.Equal(restC, VisualY(rig, "c"), 0.5);
        rig.Frame();
        rig.Frame();
        Assert.InRange(leaving.Opacity, 0.01, 0.99);
        rig.Settle();
        Assert.DoesNotContain(leaving, rig.Stack.Children);
        Assert.Null(rig.Stack.CardOf("b"));
        Assert.Equal(rig.BoundsOf(rig.Card("a")).Bottom + 6, rig.BoundsOf(rig.Card("c")).Y, 1);
    });

    [Fact]
    public void A_change_in_the_middle_of_a_glide_bends_the_motion_and_no_card_ever_jumps() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        foreach (var (id, minutes) in new[] { ("b", 20), ("c", 10) }) rig.Source.Jobs.Add(Job(id, started: Now.AddMinutes(-minutes)));
        rig.Refresh();
        rig.Settle();

        rig.Source.Jobs.Add(Job("a", started: Now.AddMinutes(-30)));
        rig.Refresh();
        for (var i = 0; i < 5; i++) rig.Frame();
        Assert.True(rig.Stack.IsMoving);
        var beforeB = VisualY(rig, "b");
        var beforeC = VisualY(rig, "c");

        rig.Source.Jobs.Add(Job("z", started: Now.AddMinutes(-40))); // another arrives at the very top, mid-glide
        rig.Refresh();
        Assert.Equal(beforeB, VisualY(rig, "b"), 0.75);
        Assert.Equal(beforeC, VisualY(rig, "c"), 0.75);
        rig.Settle();
        Assert.Equal(["z", "a", "b", "c"], rig.Stack.Cards.Select(card => card.JobId));
        foreach (var id in new[] { "z", "a", "b", "c" })
            Assert.Equal(0, rig.Stack.LookOf(id)!.Value.Offset, 3);
    });

    [Fact]
    public void A_width_change_moves_the_cards_with_it_and_springs_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig(width: 420);
        Seed(rig);
        rig.Refresh();
        rig.Settle();
        rig.Window.Width = 290;
        rig.Layout();
        Assert.False(rig.Stack.IsMoving);
        foreach (var card in rig.Stack.Cards) Assert.Equal(0, rig.Stack.LookOf(card.JobId)!.Value.Offset, 3);
    });

    [Fact]
    public void With_Reduce_Motion_cards_ease_in_for_under_a_fifth_of_a_second_with_no_scale_and_no_overshoot() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig(reduceMotion: true);
        rig.Source.Jobs.Add(Job("b", started: Now.AddMinutes(-20)));
        rig.Refresh();
        rig.Settle();
        rig.Source.Jobs.Add(Job("a", started: Now.AddMinutes(-30)));
        rig.Refresh();
        var seenScales = new List<double>();
        var seenOffsets = new List<double>();
        var seenOpacity = new List<double>();
        var frames = 0;
        while (rig.Stack.IsMoving && frames++ < 100)
        {
            rig.Frame();
            seenScales.Add(rig.Stack.LookOf("a")!.Value.Scale);
            seenOffsets.Add(rig.Stack.LookOf("b")!.Value.Offset);
            seenOpacity.Add(rig.Stack.LookOf("a")!.Value.Opacity);
        }
        Assert.InRange(frames, 1, 14); // 0.18 s at 60 Hz
        Assert.All(seenScales, scale => Assert.Equal(1, scale, 6));
        Assert.True(seenOffsets.All(offset => offset <= 0.0001), "b eases to its slot and never passes it");
        Assert.True(seenOpacity.Zip(seenOpacity.Skip(1)).All(pair => pair.Second >= pair.First - 1e-9), "the fade is monotonic");
    });

    [Fact]
    public void Pressing_settles_every_card_in_flight_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        rig.Source.Jobs.Add(Job("b", started: Now.AddMinutes(-20)));
        rig.Refresh();
        rig.Settle();
        rig.Source.Jobs.Add(Job("a", started: Now.AddMinutes(-30)));
        rig.Refresh();
        rig.Frame();
        Assert.True(rig.Stack.IsMoving);
        rig.Stack.SettleAll();
        Assert.False(rig.Stack.IsMoving);
        Assert.Equal(1, rig.Stack.LookOf("a")!.Value.Opacity, 6);
        Assert.Equal(0, rig.Stack.LookOf("b")!.Value.Offset, 6);
    });

    private static double DrawnY(JobPaneRig rig, JobCard card) => card.Bounds.Y + rig.Stack.LookOf(card.JobId)!.Value.Offset;

    [Fact]
    public void A_fuzz_of_jobs_coming_going_and_changing_never_makes_a_card_jump_or_overlap_another_at_rest() => headless.OnUiThread(() =>
    {
        var random = new Random(20261012);
        using var rig = new JobPaneRig(width: 340, height: 700);
        var next = 0;
        var clock = Now;
        string NewId() => "job" + (next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        BackgroundJob Make()
        {
            var id = NewId();
            var started = clock.AddSeconds(-random.Next(1, 4000));
            return random.Next(5) switch
            {
                0 => Job(id, BackgroundJobKind.Download, started) with { KindLabel = "Download", Subject = "1.0 of 5.0 GB", Progress = random.NextDouble(), CanPause = true },
                1 => Job(id, BackgroundJobKind.Terminal, started) with { KindLabel = "Dev server", Subject = "localhost:5173", DevServerUrl = "http://localhost:5173/", Activity = "Running" },
                2 => Job(id, BackgroundJobKind.Helper, started, state: BackgroundJobState.Done) with { FinishedAt = clock.AddSeconds(-random.Next(1, 500)) },
                _ => Job(id, BackgroundJobKind.HydraHead, started) with { Tokens = random.Next(0, 900_000), ToolUses = random.Next(0, 300), Activity = new string('a', random.Next(3, 70)), TranscriptThreadId = Guid.NewGuid() },
            };
        }
        for (var i = 0; i < 4; i++) rig.Source.Jobs.Add(Make());
        rig.Refresh();
        rig.Settle();

        for (var step = 0; step < 250; step++)
        {
            var before = rig.Stack.Cards.ToDictionary(card => card.JobId, card => DrawnY(rig, card));
            var resized = false;
            switch (random.Next(7))
            {
                case 0 or 1: rig.Source.Jobs.Add(Make()); break;
                case 2 when rig.Source.Jobs.Count > 0: rig.Source.Jobs.RemoveAt(random.Next(rig.Source.Jobs.Count)); break;
                case 3 when rig.Source.Jobs.Count > 0:
                    var at = random.Next(rig.Source.Jobs.Count);
                    if (rig.Source.Jobs[at].IsLive) rig.Source.Jobs[at] = rig.Source.Jobs[at] with { State = BackgroundJobState.Done, FinishedAt = clock, CanStop = false, Activity = null };
                    break;
                case 4 when rig.Source.Jobs.Count > 0:
                    var edit = random.Next(rig.Source.Jobs.Count);
                    if (rig.Source.Jobs[edit].IsLive) rig.Source.Jobs[edit] = rig.Source.Jobs[edit] with { Activity = rig.Source.Jobs[edit].Activity is null ? null : new string('b', random.Next(3, 70)), Tokens = rig.Source.Jobs[edit].Tokens is null ? null : random.Next(0, 900_000) };
                    break;
                case 5:
                    rig.Window.Width = 300 + random.Next(0, 200);
                    resized = true;
                    break;
                default: break;
            }
            rig.Refresh();
            if (!resized)
                foreach (var card in rig.Stack.Cards.Where(card => before.ContainsKey(card.JobId)))
                    Assert.True(Math.Abs(DrawnY(rig, card) - before[card.JobId]) <= 0.75, $"step {step}: {card.JobId} jumped from {before[card.JobId]:0.0} to {DrawnY(rig, card):0.0}");
            for (var frame = random.Next(0, 9); frame > 0; frame--) rig.Frame();
        }

        rig.Settle();
        var cards = rig.Stack.Cards;
        Assert.Equal(rig.Jobs.For(null, BackgroundJobScope.AllThreads).Select(job => job.Id), cards.Select(card => card.JobId));
        double? bottom = null;
        foreach (var card in cards)
        {
            var look = rig.Stack.LookOf(card.JobId)!.Value;
            Assert.Equal((1.0, 1.0, 0.0), (look.Opacity, look.Scale, look.Offset));
            if (bottom is { } edge) Assert.Equal(edge + 6, card.Bounds.Y, 1);
            bottom = card.Bounds.Bottom;
        }
        Assert.Equal(cards.Count, rig.Stack.Children.Count); // no card left behind
    });

    [Fact]
    public void Sixty_tasks_ticking_for_25_seconds_measure_and_arrange_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig(width: 400, height: 900);
        for (var i = 0; i < 60; i++)
            rig.Source.Jobs.Add(Job("j" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), started: Now.AddSeconds(-i * 7 - 10)) with { Tokens = i * 1000, ToolUses = i, Activity = "Reading" });
        rig.Refresh();
        rig.Settle();
        var measures = rig.Stack.Measures;
        for (var tick = 0; tick < 100; tick++)
        {
            rig.Source.Replace("j" + (tick % 60).ToString(System.Globalization.CultureInfo.InvariantCulture), job => job with { Tokens = job.Tokens + 1234, ToolUses = job.ToolUses + 1 });
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(250));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(rig.Stack.IsMeasureValid && rig.Stack.IsArrangeValid, "tick " + tick);
        }
        Assert.Equal(measures, rig.Stack.Measures);
        Assert.False(rig.Stack.IsMoving);
        Assert.Equal("0m 35s", rig.Card("j0").ElapsedText.Text); // 10 s at the start plus 25 s of ticks
    });

    // ---- accessibility, look ----

    [Fact]
    public void Every_control_has_a_name_that_names_its_task_and_cards_are_named_by_task_and_kind() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        var head = rig.Card("head");
        Assert.Equal("Port settings tests · group A, Hydra head", Avalonia.Automation.AutomationProperties.GetName(head));
        Assert.Equal("Running", Avalonia.Automation.AutomationProperties.GetHelpText(head));
        foreach (var card in rig.Stack.Cards)
            foreach (var button in new[] { card.PauseButton, card.StopButton, card.TranscriptButton, card.PreviewButton }.Where(button => button.IsVisible))
            {
                var name = Avalonia.Automation.AutomationProperties.GetName(button);
                Assert.Contains(card.Job.Title, name, StringComparison.Ordinal);
            }
        Assert.Equal("This thread", Avalonia.Automation.AutomationProperties.GetName(rig.View.ThisThreadButton));
        Assert.Equal("All threads", Avalonia.Automation.AutomationProperties.GetName(rig.View.AllThreadsButton));
        Assert.Equal("Background tasks", Avalonia.Automation.AutomationProperties.GetName(rig.Stack));
        // The live texts read as text, without announcing every tick.
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(head.ElapsedText);
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Text, peer.GetAutomationControlType());
        Assert.Equal("12m 05s", peer.GetName());
    });

    // A card's words keep 4.5:1 on the card in both themes (DESIGN 10); a finished card recedes by its tint, not by dimming its words.
    private static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    private static double Luma(Color c)
    {
        static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    [Theory]
    [InlineData(false, 255, 255, 255)] // a white window
    [InlineData(false, 242, 242, 245)] // a light grey window
    [InlineData(true, 0, 0, 0)] // a black window
    [InlineData(true, 30, 30, 30)] // the dark window's background
    [InlineData(true, 42, 42, 48)] // the brightest the dark glass lets its body get
    public void A_cards_words_keep_4_5_to_1_on_it_in_light_and_dark(bool dark, byte r, byte g, byte b) => headless.OnUiThread(() =>
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var stops = ((LinearGradientBrush)PaneTokens.CardFill(theme, expanded: false)).GradientStops.Select(stop => stop.Color).OrderBy(Luma).ToList();
        var pane = Over(dark ? stops[^1] : stops[0], Color.FromRgb(r, g, b));
        foreach (var (name, fill) in new[] { ("running", JobTokens.Fill), ("done", JobTokens.DoneFill) })
        {
            var card = Over(fill.Resolve(theme), pane);
            foreach (var (word, colour) in new[] { ("title", PaneTokens.Text1), ("metrics", PaneTokens.Text2), ("kind", PaneTokens.Text3), ("done title", PaneTokens.Text2) })
                Assert.True(JobTokens.Contrast(colour.Resolve(theme), card) >= 4.5, $"{(dark ? "dark" : "light")} {name} {word} on ({r},{g},{b}): {JobTokens.Contrast(colour.Resolve(theme), card):0.00}");
        }
    });

    [Theory]
    [InlineData(true, 0x4B, 0x9F, 0xFC)] // the canvas's blue
    [InlineData(true, 0x00, 0x00, 0x00)] // a black accent on dark
    [InlineData(true, 0x2A, 0x2A, 0xFF)] // a deep blue on dark
    [InlineData(false, 0x4B, 0x9F, 0xFC)] // the canvas's blue on light is too pale for small text
    [InlineData(false, 0xFF, 0xFF, 0xFF)] // a white accent on light
    [InlineData(false, 0xFF, 0xE0, 0x00)] // a yellow accent on light
    public void The_text_actions_are_the_accent_lifted_until_they_read_at_4_5_to_1(bool dark, byte r, byte g, byte b) => headless.OnUiThread(() =>
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var lifted = JobTokens.ActionColor(theme, Color.FromRgb(r, g, b));
        Assert.True(JobTokens.Contrast(lifted, JobTokens.NominalCard(theme)) >= 4.5, $"{lifted}");
        // An accent that already reads is left alone.
        var reads = dark ? Color.FromRgb(0xE0, 0xE8, 0xFF) : Color.FromRgb(0x10, 0x2A, 0x80);
        Assert.Equal(reads, JobTokens.ActionColor(theme, reads));
        Assert.True(JobTokens.Contrast(JobTokens.ActionColor(theme), JobTokens.NominalCard(theme)) >= 4.5);
    });

    [Fact]
    public void The_stop_pause_and_play_glyphs_draw_inside_their_box() => headless.OnUiThread(() =>
    {
        foreach (var kind in Enum.GetValues<JobGlyphKind>())
        {
            var glyph = new JobGlyph { Kind = kind, Foreground = Brushes.White };
            glyph.Measure(new Size(40, 40));
            glyph.Arrange(new Rect(0, 0, glyph.DesiredSize.Width, glyph.DesiredSize.Height));
            Assert.Equal(new Size(JobTokens.GlyphSize, JobTokens.GlyphSize), glyph.Bounds.Size);
        }
    });

    [Fact]
    public void The_progress_line_is_3_DIP_high_and_clamps_its_value() => headless.OnUiThread(() =>
    {
        var line = new ProgressLine { Width = 100 };
        line.Measure(new Size(100, 100));
        Assert.Equal(3, line.DesiredSize.Height);
        line.Value = 5;
        Assert.Equal(1, line.Value);
        line.Value = -1;
        Assert.Equal(0, line.Value);
        line.Value = double.NaN;
        Assert.Equal(0, line.Value);
    });

    [Fact]
    public void The_activity_label_is_the_apps_light_through_text() => headless.OnUiThread(() =>
    {
        using var rig = new JobPaneRig();
        Seed(rig);
        rig.Refresh();
        Assert.IsAssignableFrom<NthShineText>(rig.Card("head").ActivityText);
        Assert.Equal(TextTrimming.CharacterEllipsis, rig.Card("head").ActivityText.TextTrimming);
        Assert.Equal(TextWrapping.NoWrap, rig.Card("head").ActivityText.TextWrapping);
    });
}
