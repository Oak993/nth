using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 5.1, 7.2 "App event"; ED-NEW-dextra-as10-11): the feed's lines become the few events a duty can wait
// for. The usage lines are read back from the real AccountWindowWatcher's own words, so a change in its wording fails here and not
// silently in a duty. A mission's chat events carry its mission so the scheduler can keep a duty from feeding itself.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-11")]
public sealed class DutyEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static FeedEvent Line(FeedDraft draft, long sequence = 1) =>
        new(sequence, draft.Time, draft.Kind, draft.Severity, draft.Lead, draft.ThreadId, draft.MissionId, draft.AssistantId);

    private static AccountWindowReading Reading(string account, string window, double percent, DateTimeOffset? resets = null) =>
        new("key-" + account, account, window, window, percent, resets);

    [Fact]
    public void A_window_at_a_share_is_an_account_above_event_with_the_account_and_the_share_read_back()
    {
        using var watcher = new AccountWindowWatcher();
        watcher.Observe([Reading("Claude B", "weekly", 70)], Now);
        var drafts = watcher.Observe([Reading("Claude B", "weekly", 85)], Now.AddMinutes(5));
        var e = DutyEvents.FromFeed(Line(Assert.Single(drafts)));
        Assert.NotNull(e);
        Assert.Equal(AppEventKind.AccountAbove, e.Kind);
        Assert.Equal("Claude B", e.Account);
        Assert.Equal("weekly", e.Window);
        Assert.Equal(85, e.Percent);
    }

    [Fact]
    public void A_window_that_reset_is_a_reset_event_for_that_account_and_window()
    {
        using var watcher = new AccountWindowWatcher();
        var later = Now.AddHours(4);
        watcher.Observe([Reading("Codex A", "weekly", 92, Now.AddMinutes(1))], Now);
        var drafts = watcher.Observe([Reading("Codex A", "weekly", 3, later)], Now.AddMinutes(30));
        var e = DutyEvents.FromFeed(Line(Assert.Single(drafts)));
        Assert.NotNull(e);
        Assert.Equal(AppEventKind.WindowReset, e.Kind);
        Assert.Equal("Codex A", e.Account);
        Assert.Equal("weekly", e.Window);
    }

    [Fact]
    public void A_limit_reached_is_an_account_above_event_at_a_hundred()
    {
        using var watcher = new AccountWindowWatcher();
        var draft = watcher.NoteLimit("key-codex", "Codex A", Now.AddHours(2), Now)!;
        var e = DutyEvents.FromFeed(Line(draft));
        Assert.NotNull(e);
        Assert.Equal((AppEventKind.AccountAbove, "Codex A", 100.0), (e.Kind, e.Account, e.Percent));
    }

    [Fact]
    public void A_finished_turn_and_a_failed_head_are_events_and_a_head_that_finished_well_is_not()
    {
        var mission = Guid.NewGuid();
        var turn = DutyEvents.FromFeed(new FeedEvent(1, Now, FeedEventKind.TurnFinished, FeedSeverity.Info, "Fix the parser: finished", Guid.NewGuid(), mission, null));
        Assert.Equal((AppEventKind.ChatFinished, (Guid?)mission), (turn!.Kind, turn.Mission));
        var failed = DutyEvents.FromFeed(new FeedEvent(2, Now, FeedEventKind.HeadFinished, FeedSeverity.Error, "Subtitles failed", Guid.NewGuid(), null, null));
        Assert.Equal(AppEventKind.HeadFailed, failed!.Kind);
        Assert.Null(DutyEvents.FromFeed(new FeedEvent(3, Now, FeedEventKind.HeadFinished, FeedSeverity.Info, "Subtitles finished", Guid.NewGuid(), null, null)));
    }

    [Theory]
    [InlineData(FeedEventKind.TokenSpend)]
    [InlineData(FeedEventKind.Approval)]
    [InlineData(FeedEventKind.DutyFired)]
    [InlineData(FeedEventKind.WatcherChanged)]
    [InlineData(FeedEventKind.MissionState)]
    public void Other_lines_are_not_events_and_a_duty_cannot_feed_itself_through_its_own_feed_lines(FeedEventKind kind) =>
        Assert.Null(DutyEvents.FromFeed(new FeedEvent(1, Now, kind, FeedSeverity.Info, "Claude B: weekly at 90%, up 5%")));

    [Fact]
    public void A_usage_line_with_other_words_is_no_event_not_a_guess()
    {
        Assert.Null(DutyEvents.FromFeed(new FeedEvent(1, Now, FeedEventKind.AccountWindow, FeedSeverity.Quiet, "Claude B weekly is high")));
        Assert.Null(DutyEvents.FromFeed(new FeedEvent(1, Now, FeedEventKind.AccountWindowReset, FeedSeverity.Info, "something reset")));
    }

    [Fact]
    public void A_mission_that_is_done_is_the_mission_finished_event_with_its_id()
    {
        var record = new MissionRecord(new Mission(Guid.NewGuid(), "Fix the login", "dextra", MissionOrigin.Owner, MissionState.Done));
        var e = DutyEvents.MissionFinished(record);
        Assert.Equal((AppEventKind.MissionFinished, (Guid?)record.Id, "“Fix the login” finished"), (e.Kind, e.Mission, e.Summary));
    }
}
