using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 6 and 8a, "Update", unit U2 (ED-NEW-inst-u2-1). Update's four states as
// the canvas has them: Ask (the new version in bold, what's new when there are notes, the footnote, Not now and Update),
// Busy (the running threads, "Stop them and update" and "Update when they finish", one thread said in the singular),
// Waiting (only "Stop waiting", which goes back to Ask) and Updating (the steps of the run and the bar, no buttons); a
// button's new words cross-fade in place and the buttons glide at most 3 DIP a frame when one leaves.
[Collection("SetupHeadless")]
public sealed class UpdateScreenTests(SetupHeadlessSession headless)
{
    private static readonly string[] Notes = ["Threads open faster on large projects", "Voice input understands more accents"];

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Ask_says_both_versions_with_the_new_one_in_bold_and_lists_whats_new()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);

            Assert.Equal(UpdateState.Ask, update.State);
            Assert.Equal("Update nth", update.TitleBlock.Text);
            Assert.Equal("nth 1.0.0 is installed. This updates it to 1.1.0.", AutomationProperties.GetName(update.SubtitleBlock));
            var runs = update.SubtitleBlock.Inlines!.OfType<Run>().ToList();
            Assert.Equal("nth 1.0.0 is installed. This updates it to 1.1.0.", string.Concat(runs.Select(r => r.Text)));
            var bold = Assert.Single(runs, r => r.FontWeight == FontWeight.SemiBold);
            Assert.Equal("1.1.0", bold.Text);
            Assert.True(update.Notes.IsVisible);
            Assert.Equal("What’s new in 1.1.0", update.Notes.Heading.Text);
            Assert.Equal(Notes, update.Notes.Lines);
            Assert.Equal("Your chats and settings stay as they are.", update.Footnote.Text);
            Assert.Equal(("Not now", "Update"), (update.Later.Label, update.Go.Label));
            Assert.Same(update.Go, update.DefaultButton);
            Assert.False(update.Steps.IsVisible);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void With_no_release_notes_the_card_is_not_shown()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update([]);
            Assert.False(update.Notes.IsVisible);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void With_nth_open_the_footnote_says_it_saves_and_closes_for_a_moment()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            update.NthOpen = true;
            update.ShowAsk();
            rig.Run(1);
            Assert.Equal("Your chats and settings stay as they are. nth is open: it saves your work and closes for a moment.", update.Footnote.Text);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(1, "1 thread is running in nth. Updating now would stop it.", "Stop it and update", "Update when it finishes")]
    [InlineData(2, "2 threads are running in nth. Updating now would stop them.", "Stop them and update", "Update when they finish")]
    [Trait("Decision", "OD-0072")]
    public void Busy_lists_the_running_threads_and_offers_stop_or_wait(int count, string subtitle, string stop, string wait)
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            var running = new[] { ("Refactor the sign-in module", 4), ("Write the release notes", 1) }.Take(count).ToList();
            var stops = 0;
            var waits = 0;
            update.StopRequested += () => stops++;
            update.WaitRequested += () => waits++;

            update.ShowBusy(running);
            rig.Run(1.2);
            Assert.Equal(UpdateState.Busy, update.State);
            Assert.Equal("nth is still working", update.TitleBlock.Text);
            Assert.Equal(subtitle, update.SubtitleBlock.Text);
            Assert.False(update.Notes.IsVisible);
            Assert.True(update.Busy.IsVisible);
            Assert.Equal("Running in nth now", update.Busy.Heading.Text);
            Assert.Equal(running.Select(r => SetupStrings.UpdateBusyRow(r.Item1, r.Item2)), update.Busy.Lines);
            Assert.Equal("Nothing has changed yet. Your chats and settings stay as they are either way.", update.Footnote.Text);
            Assert.Equal((stop, wait), (update.Later.Label, update.Go.Label));

            update.Later.Invoke();
            update.Go.Invoke();
            Assert.Equal((1, 1), (stops, waits));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Waiting_shows_only_Stop_waiting_which_goes_back_to_Ask()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            update.ShowBusy([("Refactor the sign-in module", 4), ("Write the release notes", 1)]);
            rig.Run(1.2);
            update.ShowWaiting(2);
            rig.Run(1.2);

            Assert.Equal(UpdateState.Waiting, update.State);
            Assert.Equal("Update waits for nth", update.TitleBlock.Text);
            Assert.Equal("It starts by itself when they finish.", update.SubtitleBlock.Text);
            Assert.Equal("Stop waiting", update.Later.Label);
            Assert.True(update.Later.IsVisible);
            Assert.False(update.Go.IsVisible);
            Assert.Equal([SetupStrings.StepWaiting(2), SetupStrings.StepClosing, SetupStrings.StepCopyingNew, SetupStrings.StepFinishing], update.Steps.Rows.Select(r => r.Label).ToArray());
            Assert.True(update.Bar.IsVisible);

            update.Later.Invoke();
            rig.Run(1.2);
            Assert.Equal(UpdateState.Ask, update.State);
            Assert.Equal(("Not now", "Update"), (update.Later.Label, update.Go.Label));
            Assert.True(update.Go.IsVisible);
            Assert.False(update.Steps.IsVisible);
            Assert.Equal("nth 1.0.0 is installed. This updates it to 1.1.0.", AutomationProperties.GetName(update.SubtitleBlock));
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(UpdateRun.Closed, 0)]
    [InlineData(UpdateRun.Open, 0)]
    [InlineData(UpdateRun.Stop, 2)]
    [Trait("Decision", "OD-0072")]
    public void Updating_shows_the_runs_steps_and_the_bar_with_no_buttons(UpdateRun run, int threads)
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            update.ShowUpdating(run, threads);
            rig.Run(1.2);

            Assert.Equal(UpdateState.Updating, update.State);
            Assert.Equal("Updating to 1.1.0", update.TitleBlock.Text);
            Assert.False(update.Later.IsVisible);
            Assert.False(update.Go.IsVisible);
            Assert.False(update.Notes.IsVisible);
            Assert.True(update.Bar.IsVisible);
            Assert.Equal(UpdateScreen.UpdateSteps(run, threads, null).Select(s => s.Label), update.Steps.Rows.Select(r => r.Label));
            var first = run switch { UpdateRun.Open => SetupStrings.StepClosing, UpdateRun.Stop => SetupStrings.StepStopping(2), _ => SetupStrings.StepChecking };
            Assert.Equal(first, update.Steps.Rows[0].Label);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void New_button_words_cross_fade_in_place_and_the_buttons_glide()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            var probe = new FrameProbe(rig, update.View, [update.TitleBlock, update.SubtitleBlock, update.Notes, update.Busy, update.Footnote, update.Steps, update.Bar], [update.Later, update.Go]) { ButtonsFixed = false };
            var lastLater = update.Later.LabelOpacity;

            update.ShowBusy([("Refactor the sign-in module", 4), ("Write the release notes", 1)]);
            probe.Run(1.2, _ =>
            {
                Assert.InRange(Math.Abs(update.Later.LabelOpacity - lastLater), 0, .35);
                lastLater = update.Later.LabelOpacity;
            });
            update.ShowWaiting(2);
            probe.Run(.2);
            update.ShowAsk();
            probe.Run(1.4);
            AssertButtonsGlide(probe, update);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Not_now_closes_and_Update_with_no_handler_hands_the_run_to_the_gate()
    {
        headless.OnUiThread(() =>
        {
            var (rig, update) = Update(Notes);
            update.Later.Invoke();
            rig.Settle();
            Assert.False(rig.Window.IsVisible);

            var rig2 = new WindowRig();
            var session = rig2.Session(SetupScreen.Update, installed: "1.0.0");
            session.Start();
            // ED-NEW-inst-merge-3: Update installs only once the signature has answered (ED-NEW-inst-cx-1)
            session.SignatureChecked(new Marrow.Setup.Platform.SignatureResult(Marrow.Setup.Platform.SignatureState.Unsigned, null, 0));
            rig2.Window.Show();
            rig2.Settle();
            Assert.IsType<UpdateScreen>(session.Current).Go.Invoke();
            rig2.Settle();
            Assert.True(session.InstallChosen);
            Assert.False(rig2.Window.IsVisible);
        });
    }

    // A button drawn above the design's gone level (Reveal.Gone, the canvas's EPS) in either frame keeps its width and moves
    // at most 3 DIP a frame; a button whose words change fades out to that level first and takes its new width and place there.
    private static void AssertButtonsGlide(FrameProbe probe, UpdateScreen update)
    {
        for (var i = 1; i < probe.Frames.Count; i++)
            foreach (var button in new[] { update.Later, update.Go })
            {
                var a = probe.Frames[i - 1][button];
                var b = probe.Frames[i][button];
                if ((a.Opacity <= Reveal.Gone && b.Opacity <= Reveal.Gone) || a.Box.Height <= 0 || b.Box.Height <= 0) continue;
                Assert.True(Math.Abs(b.Box.X - a.Box.X) <= FrameProbe.MaxStep + 1e-6, $"{button.Label} jumped {Math.Abs(b.Box.X - a.Box.X):F2} DIP in frame {i} (opacity {a.Opacity:F3} to {b.Opacity:F3})");
                Assert.True(Math.Abs(b.Box.Width - a.Box.Width) <= .5, $"{button.Label} changed width from {a.Box.Width:F1} to {b.Box.Width:F1} in frame {i} (opacity {a.Opacity:F3} to {b.Opacity:F3})");
            }
    }

    private static (WindowRig Rig, UpdateScreen Update) Update(IReadOnlyList<string> notes)
    {
        var rig = new WindowRig();
        var session = rig.Session(SetupScreen.Update, installed: "1.0.0");
        session.ReleaseNotes = notes;
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(1.4);
        return (rig, Assert.IsType<UpdateScreen>(session.Current));
    }
}
