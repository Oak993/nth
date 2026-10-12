// PORT-ONLY: what the Tactus pill says (OD-0145, ED-NEW-pcctl-24; DESIGN.md 4.5 "States", 10.1, 10.3, 10.4, 10.5 "Text
// priority", 10.6 "Accessibility"): every state's two lines, its glyph and mark, its group, Stop, the badge, the hint, the
// rows and the announcement, and the priority between them, from facts alone.
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillStatesTests
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");
    private static readonly PillApp Outlook = new("outlook", "Outlook", "Writing the reply");
    private static readonly PillApp Chrome = new("chrome", "Chrome", "Opening the report");
    private static readonly PillAsk Approval = new("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "Claude Opus");

    private static PillView View(PillFacts facts, double now = 10) => PillStates.Compute(facts, now);

    [Fact]
    public void Working_names_the_apps_and_the_latest_step_with_Pause_all_and_Stop()
    {
        var view = View(new PillFacts { Apps = [Excel, Outlook], LatestApp = "outlook" });

        Assert.Equal("Using Excel and Outlook", view.Title);
        Assert.Equal("Outlook: Writing the reply", view.Line);
        Assert.Equal(PillGlyph.Live, view.Glyph);
        Assert.Equal(TactusMarkState.Working, view.Mark);
        Assert.Equal(PillGroupKind.Run, view.Group);
        Assert.True(view.ShowsStop);
        Assert.True(view.Working);
        Assert.False(view.Ask);
        Assert.Equal(0, view.Badge);
        Assert.Equal("Tactus working", view.Announcement);
        Assert.Equal("Pause all, Ctrl+Alt+Space", view.PauseName);
        Assert.Equal("Resume all, Ctrl+Alt+Space", view.ResumeName);
        Assert.Equal("Stop all, Ctrl+Alt+Esc", view.StopName);
        Assert.Equal("Ctrl+Alt+Space pauses. Ctrl+Alt+Esc stops.", view.Hint);
        Assert.False(view.HintShown);
        Assert.False(view.RowsShown);
        Assert.False(view.Stopped);
    }

    [Fact]
    public void Names_join_like_the_canvas()
    {
        Assert.Equal("", PillStates.Names([]));
        Assert.Equal("Excel", PillStates.Names([Excel]));
        Assert.Equal("Excel and Outlook", PillStates.Names([Excel, Outlook]));
        Assert.Equal("Excel, Outlook and Chrome", PillStates.Names([Excel, Outlook, Chrome]));
        Assert.Equal("Using Excel, Outlook and Chrome", View(new PillFacts { Apps = [Excel, Outlook, Chrome] }).Title);
        // without a latest app the first working app's step is the line
        Assert.Equal("Excel: Filling row 12 of 40", View(new PillFacts { Apps = [Excel, Outlook] }).Line);
    }

    [Fact]
    public void Without_a_pause_chord_the_hint_and_names_drop_it()
    {
        var view = View(new PillFacts { Apps = [Excel], PauseChord = "" });

        Assert.Equal("Ctrl+Alt+Esc stops all", view.Hint);
        Assert.Equal("Pause all", view.PauseName);
        Assert.Equal("Stop all, Ctrl+Alt+Esc", view.StopName);
    }

    [Fact]
    public void An_ask_names_its_app_says_who_keeps_working_and_counts_the_rest()
    {
        var one = View(new PillFacts { Apps = [Excel, Outlook with { WaitsOnAsk = true }], Asks = [Approval] });
        Assert.Equal("Needs your OK. Outlook", one.Title);
        Assert.Equal("Click to answer. Excel keeps working", one.Line);
        Assert.True(one.Ask);
        Assert.False(one.Working);
        Assert.Equal(1, one.Badge);
        Assert.Equal(PillGroupKind.Run, one.Group);
        Assert.True(one.ShowsStop);
        Assert.Equal("Tactus: 1 waiting for you", one.Announcement);

        var question = Approval with { Id = "q1", Kind = PillAskKind.Question, AppName = "Excel" };
        var two = View(new PillFacts { Apps = [Excel with { WaitsOnAsk = true }, Outlook with { WaitsOnAsk = true }], Asks = [Approval, question] });
        Assert.Equal("Needs your OK. Outlook +1", two.Title);
        Assert.Equal("Click to answer", two.Line);
        Assert.Equal(2, two.Badge);

        var both = View(new PillFacts { Apps = [Excel, Chrome, Outlook with { WaitsOnAsk = true }], Asks = [Approval] });
        Assert.Equal("Click to answer. Excel and Chrome keep working", both.Line);

        Assert.Equal("Waiting for your answer. Excel", PillStates.AskTitle(question));
        var provider = new PillAsk("p1", PillAskKind.Provider, "", "Release notes", "GPT-6.1", "Run a command");
        Assert.Equal("Run a command. Release notes", PillStates.AskTitle(provider));
        Assert.Equal("Tactus needs your OK for Outlook", PillStates.NewAskNotice(Approval));
        Assert.Equal("Tactus is waiting for your answer in Excel", PillStates.NewAskNotice(question));

        var withdrawn = View(new PillFacts { Apps = [Excel], Asks = [Approval with { Withdrawn = true }] });
        Assert.Equal("Your draft is kept · click to open", withdrawn.Line);
    }

    [Fact]
    public void The_card_shows_the_open_ask_with_its_pager_and_no_Stop_or_hint()
    {
        var question = Approval with { Id = "q1", Kind = PillAskKind.Question, AppName = "Excel", Chat = "Budget", Model = "Claude Sonnet" };
        var view = View(new PillFacts { Apps = [Excel, Outlook], Asks = [Approval, question], Card = true, OpenAsk = 1, RowsOpen = true });

        Assert.Equal("Waiting for your answer. Excel", view.Title);
        Assert.Equal("Budget · Claude Sonnet", view.Line);
        Assert.Equal(PillGroupKind.Card, view.Group);
        Assert.False(view.ShowsStop);
        Assert.Equal("2 of 2", view.Pager);
        Assert.Equal("Waiting for your answer. Excel. 2 waiting", view.Announcement);
        Assert.False(view.HintShown);
        Assert.False(view.RowsShown);

        var provider = new PillAsk("p1", PillAskKind.Provider, "", "Release notes", "GPT-6.1", "Run a command");
        var single = View(new PillFacts { Apps = [Excel], Asks = [provider], Card = true });
        Assert.Equal("GPT-6.1 · review it in the chat", single.Line);
        Assert.Null(single.Pager);
    }

    [Fact]
    public void Stopped_shows_for_two_seconds_without_buttons_or_Stop()
    {
        var facts = new PillFacts { Apps = [Excel], Stopped = new PillStop(9) };
        var view = View(facts, 10);

        Assert.Equal("Stopped", view.Title);
        Assert.Equal("Ask in chat to continue", view.Line);
        Assert.Equal(PillGroupKind.None, view.Group);
        Assert.False(view.ShowsStop);
        Assert.Equal(PillGlyph.None, view.Glyph);
        Assert.Equal(TactusMarkState.Ready, view.Mark);
        Assert.Equal("Tactus stopped", view.Announcement);
        Assert.True(view.Stopped);
        Assert.False(view.HintShown);

        Assert.Equal("Esc pressed 3 times. Ask in chat to continue.", View(facts with { Stopped = new PillStop(9, EscThreeTimes: true) }, 10).Line);
        // 2 s later the apps that still run show again
        Assert.Equal("Using Excel", View(facts, 11.01).Title);
        // no live app at all is Stopped too
        Assert.Equal("Stopped", View(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Stopped }] }).Title);
    }

    [Fact]
    public void Stop_key_taken_comes_before_Stopped_and_the_ask()
    {
        var view = View(new PillFacts { Apps = [Excel], Asks = [Approval], Stopped = new PillStop(9.5), Notices = [PillNotice.StopKeyTaken(1)] });

        Assert.Equal("Stop key is taken by another app", view.Title);
        Assert.Equal("Excel: Filling row 12 of 40", view.Line);
        Assert.Equal(PillGroupKind.Settings, view.Group);
        Assert.True(view.ShowsStop);
    }

    [Fact]
    public void Pausing_says_what_it_finishes_then_after_this_step_then_offers_Stop_this_app()
    {
        var facts = new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Pausing, PhaseSince = 10, Busy = "dragging in Paint" }] };

        var soon = View(facts, 11);
        Assert.Equal("Pausing", soon.Title);
        Assert.Equal("Finishing dragging in Paint", soon.Line);
        Assert.Equal(PillGlyph.Pausing, soon.Glyph);
        Assert.Equal(TactusMarkState.Paused, soon.Mark);
        Assert.Equal(PillGroupKind.Paused, soon.Group);
        Assert.Equal("Tactus pausing", soon.Announcement);

        Assert.Equal("After this step: dragging in Paint", View(facts, 12).Line);
        Assert.Equal(PillGroupKind.Paused, View(facts, 14.9).Group);
        Assert.Equal(PillGroupKind.PausingStuck, View(facts, 15).Group);
        Assert.Equal("Finishing this step", View(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Pausing, PhaseSince = 10 }] }, 13).Line);
    }

    [Fact]
    public void All_paused_names_the_app_who_holds_the_steps_then_the_minutes_and_shows_the_hint()
    {
        var one = new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Paused, PhaseSince = 10 }] };
        var view = View(one, 20);

        Assert.Equal("Paused in Excel", view.Title);
        Assert.Equal("Excel holds the next steps", view.Line);
        Assert.Equal(PillGlyph.Pause, view.Glyph);
        Assert.Equal(TactusMarkState.Paused, view.Mark);
        Assert.Equal(PillGroupKind.Paused, view.Group);
        Assert.True(view.ShowsStop);
        Assert.Equal("Ctrl+Alt+Space resumes. Ctrl+Alt+Esc stops.", view.Hint);
        Assert.True(view.HintShown);
        // drawn as the canvas draws it: key chips, the verb, a dot, the stop chord's chips
        Assert.Equal(
            new[] { "Key:Ctrl", "Key:Alt", "Key:Space", "Text: resumes", "Dot:·", "Key:Ctrl", "Key:Alt", "Key:Esc", "Text: stops" },
            view.HintPieces.Select(p => $"{p.Kind}:{p.Text}").ToArray());
        var working = View(new PillFacts { Apps = [Excel] }).HintPieces[3];
        Assert.Equal(" pauses", working.Text);
        Assert.Equal(PillHintPartKind.Text, working.Kind);
        Assert.Equal("Tactus paused", view.Announcement);
        Assert.Equal("Paused 1 min", View(one, 70).Line);
        Assert.False(View(one with { Dragging = true }, 20).HintShown);

        var two = View(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Paused, PhaseSince = 10 }, Outlook with { Phase = PillAppPhase.Paused, PhaseSince = 12 }] }, 20);
        Assert.Equal("Paused", two.Title);
        Assert.Equal("Excel and Outlook hold the next steps", two.Line);
    }

    [Fact]
    public void Some_paused_names_who_works_and_who_is_paused()
    {
        var view = View(new PillFacts { Apps = [Excel, Outlook with { Phase = PillAppPhase.Paused }] });

        Assert.Equal("Using Excel. Outlook paused", view.Title);
        Assert.Equal("Filling row 12 of 40", view.Line);
        Assert.Equal(PillGroupKind.Run, view.Group);
        Assert.Equal(PillGlyph.Live, view.Glyph);
        Assert.Equal("Tactus paused in Outlook", view.Announcement);
    }

    [Fact]
    public void Typing_waits_and_names_the_field()
    {
        var view = View(new PillFacts { Apps = [Outlook], Typing = new PillTyping("Subject", "Outlook") });

        Assert.Equal("Waiting while you type", view.Title);
        Assert.Equal("Subject (Outlook)", view.Line);
        Assert.Equal(PillGroupKind.Run, view.Group);
        Assert.True(view.ShowsStop);
    }

    [Fact]
    public void The_notices_carry_DESIGN_s_texts()
    {
        PillView With(PillNotice notice, double now = 10) => View(new PillFacts { Apps = [Excel], Notices = [notice] }, now);

        var restarting = With(PillNotice.InputRestarting(9));
        Assert.Equal("Real mouse and keyboard off for a moment", restarting.Title);
        Assert.Equal("Restarting the input check", restarting.Line);
        Assert.Equal(PillGroupKind.Run, restarting.Group);

        var hands = With(PillNotice.HandsOff("Drag the chart in Excel", "Tactus needs the mouse for one step: drag the chart in Excel.", 1, running: true, releaseFirst: false, 9));
        Assert.Equal("Hands off for a moment", hands.Title);
        Assert.Equal("Drag the chart in Excel +1 more", hands.Line);
        Assert.Equal("Tactus needs the mouse for one step: drag the chart in Excel. Esc stops this step", hands.Hint);
        Assert.Equal(
            new[] { "Text:Tactus needs the mouse for one step: drag the chart in Excel. ", "Key:Esc", "Text: stops this step" },
            hands.HintPieces.Select(p => $"{p.Kind}:{p.Text}").ToArray());
        Assert.True(hands.HintShown);
        Assert.Equal(PillGroupKind.HandsOff, hands.Group);
        Assert.Equal("Release the mouse first", With(PillNotice.HandsOff("Drag", "S.", 0, false, releaseFirst: true, 9)).Line);
        Assert.Equal("S.", With(PillNotice.HandsOff("Drag", "S.", 0, running: false, false, 9)).Hint);

        Assert.Equal("Waiting until you leave the admin window", With(PillNotice.ProtectedForeground(admin: true, 9)).Title);
        Assert.Equal(PillGroupKind.NotNow, With(PillNotice.ProtectedForeground(admin: false, 9)).Group);
        Assert.Equal("Waiting until you leave the full-screen app", With(PillNotice.ProtectedForeground(admin: false, 9)).Title);

        var kept = With(PillNotice.KeptKeys(6, 9));
        Assert.Equal("Tactus kept 6 keys you typed", kept.Title);
        Assert.Equal("Excel: Filling row 12 of 40", kept.Line);
        Assert.Equal(PillGroupKind.PutBack, kept.Group);
        Assert.Equal("Tactus kept 1 key you typed", With(PillNotice.KeptKeys(1, 9)).Title);
        Assert.Equal("Click where you were working to keep typing", With(PillNotice.KeysHeld(9)).Title);

        var expired = With(PillNotice.Expired("Send", "Outlook", 9));
        Assert.Equal("Send in Outlook expired", expired.Title);
        Assert.Equal("Nothing was sent. Ask in chat to try again", expired.Line);
        Assert.Equal(PillGroupKind.StopOnly, expired.Group);
        Assert.Equal(PillGlyph.None, expired.Glyph);
        Assert.True(expired.ShowsStop);
        Assert.Equal("Using Excel", With(PillNotice.Expired("Send", "Outlook", 9), 11).Title);

        Assert.Equal("Excel opened Save As", With(PillNotice.OpenedWindow("Excel", "Save As", 9)).Title);
        Assert.Equal("Used the mouse for one click in Paint", With(PillNotice.Borrowed("Used the mouse for one click in Paint", 9.5)).Title);
        Assert.Equal("Using Excel", With(PillNotice.Borrowed("Used the mouse for one click in Paint", 9), 10).Title);

        var done = With(PillNotice.Done("Moved the chart", 9));
        Assert.Equal("Done, all yours", done.Title);
        Assert.Equal("Moved the chart", done.Line);
        Assert.Equal("Using Excel", With(PillNotice.Done("Moved the chart", 9), 10.3).Title);
    }

    [Fact]
    public void Notices_rank_by_DESIGN_s_priority_and_come_before_the_ask_but_Pause_key_taken_after()
    {
        var notices = new[]
        {
            PillNotice.Done("Moved the chart", 9.5),
            PillNotice.HandsOff("Drag the chart in Excel", "S.", 0, false, false, 9),
            PillNotice.OpenedWindow("Excel", "Save As", 9.8),
        };
        Assert.Equal("Hands off for a moment", View(new PillFacts { Apps = [Excel], Notices = notices }).Title);
        Assert.Equal("Real mouse and keyboard off for a moment", View(new PillFacts { Apps = [Excel], Notices = [.. notices, PillNotice.InputRestarting(1)] }).Title);
        Assert.Equal("Hands off for a moment", View(new PillFacts { Apps = [Excel], Asks = [Approval], Notices = notices }).Title);

        var pauseKey = new PillFacts { Apps = [Excel], Notices = [PillNotice.PauseKeyTaken(9)] };
        var taken = View(pauseKey);
        Assert.Equal("Pause key is taken by another app", taken.Title);
        Assert.Equal("Excel: Filling row 12 of 40", taken.Line);
        Assert.Equal(PillGroupKind.Settings, taken.Group);
        Assert.Equal("Needs your OK. Outlook", View(pauseKey with { Asks = [Approval] }).Title);
        // a later notice of the same rank wins
        var later = View(new PillFacts { Apps = [Excel], Notices = [PillNotice.KeysHeld(9), PillNotice.KeptKeys(2, 9.5)] });
        Assert.Equal("Tactus kept 2 keys you typed", later.Title);
    }

    [Fact]
    public void The_rows_list_every_app_with_its_line_and_open_with_the_hint()
    {
        var facts = new PillFacts
        {
            Apps = [Excel, Outlook with { Phase = PillAppPhase.Paused }, Chrome with { Phase = PillAppPhase.Pausing, Busy = "typing the URL" }, new PillApp("paint", "Paint", "Drawing", PillAppPhase.Stopped)],
            RowsOpen = true,
        };
        var view = View(facts);

        Assert.True(view.RowsShown);
        Assert.True(view.HintShown);
        Assert.Equal(4, view.Rows.Count);
        Assert.Equal(new PillRowView("excel", "Excel", "Filling row 12 of 40", false, false, false, "Pause Excel", "Stop Excel"), view.Rows[0]);
        Assert.Equal(new PillRowView("outlook", "Outlook", "Paused", true, false, false, "Resume Outlook", "Stop Outlook"), view.Rows[1]);
        Assert.Equal("Pausing after typing the URL", view.Rows[2].Line);
        Assert.True(view.Rows[2].Paused);
        Assert.Equal("Stopped by you", view.Rows[3].Line);
        Assert.True(view.Rows[3].Off);

        Assert.False(View(facts with { Dragging = true }).RowsShown);
        Assert.False(View(facts with { Dragging = true }).HintShown);
        Assert.False(View(facts with { RowsOpen = false }).RowsShown);
        Assert.True(View(new PillFacts { Apps = [Excel with { Behind = true }] }).Rows[0].Behind);
        Assert.False(View(new PillFacts { Apps = [Excel with { Behind = true, Phase = PillAppPhase.Paused }] }).Rows[0].Behind);
    }
}
