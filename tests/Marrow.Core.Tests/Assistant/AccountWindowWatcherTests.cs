using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// AS-2 (DESIGN.md 5.1 table, MultiAccountRouting row): an account window's percent changed by a point or more, a limit was
// reached, a window reset. Expected lines are written out here.
public sealed class AccountWindowWatcherTests
{
    private static readonly DateTimeOffset Now = FeedSamples.Start;

    private static AccountWindowReading Window(double percent, DateTimeOffset? resets = null, string account = "acct-1", string id = "five-hour") =>
        new(account, "Claude Max", id, "5-hour window", percent, resets ?? Now.AddHours(3));

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void The_first_reading_of_a_window_only_sets_the_baseline()
    {
        var watcher = new AccountWindowWatcher();
        Assert.Empty(watcher.Observe([Window(40)], Now));
    }

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    [InlineData(40.0, 40.9, false)]
    [InlineData(40.0, 41.0, true)]
    [InlineData(40.0, 43.5, true)]
    [InlineData(40.0, 39.0, true)]
    [InlineData(40.0, 39.5, false)]
    public void A_change_of_a_point_or_more_is_an_event_and_less_is_not(double before, double after, bool expected)
    {
        var watcher = new AccountWindowWatcher();
        watcher.Observe([Window(before)], Now);
        var drafts = watcher.Observe([Window(after)], Now.AddSeconds(60));
        Assert.Equal(expected, drafts.Count == 1);
        if (expected) Assert.Equal((FeedEventKind.AccountWindow, FeedSeverity.Quiet), (drafts[0].Kind, drafts[0].Severity));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_percent_change_names_the_account_the_window_and_the_step()
    {
        var watcher = new AccountWindowWatcher();
        watcher.Observe([Window(59.6)], Now);
        var draft = Assert.Single(watcher.Observe([Window(62.2)], Now.AddSeconds(60)));
        Assert.Equal("Claude Max: 5-hour window at 62%, up 3%", draft.Lead);
        Assert.Null(draft.Chat);
        Assert.Null(draft.ThreadId);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void Windows_and_accounts_are_watched_apart()
    {
        var watcher = new AccountWindowWatcher();
        watcher.Observe([Window(40, id: "five-hour"), Window(10, id: "weekly"), Window(70, account: "acct-2")], Now);
        var drafts = watcher.Observe([Window(41, id: "five-hour"), Window(10, id: "weekly"), Window(70, account: "acct-2")], Now.AddSeconds(60));
        Assert.Single(drafts);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_window_whose_percent_falls_after_its_reset_time_moved_on_is_a_reset()
    {
        var watcher = new AccountWindowWatcher();
        watcher.Observe([Window(94, Now.AddMinutes(10))], Now);
        var draft = Assert.Single(watcher.Observe([Window(2, Now.AddHours(5).AddMinutes(10))], Now.AddMinutes(11)));
        Assert.Equal((FeedEventKind.AccountWindowReset, FeedSeverity.Info), (draft.Kind, draft.Severity));
        Assert.Equal("Claude Max: 5-hour window reset, from 94% to 2%", draft.Lead);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_percent_that_only_decays_while_the_reset_time_stays_is_not_a_reset()
    {
        var watcher = new AccountWindowWatcher();
        var resets = Now.AddDays(3);
        watcher.Observe([Window(60, resets)], Now);
        var draft = Assert.Single(watcher.Observe([Window(55, resets)], Now.AddMinutes(30)));
        Assert.Equal(FeedEventKind.AccountWindow, draft.Kind);
        Assert.Equal("Claude Max: 5-hour window at 55%, down 5%", draft.Lead);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_window_reaching_100_percent_is_a_limit_event_and_is_reported_once()
    {
        var watcher = new AccountWindowWatcher();
        var resets = Now.AddHours(2).AddMinutes(5);
        watcher.Observe([Window(97, resets)], Now);
        var draft = Assert.Single(watcher.Observe([Window(100, resets)], Now));
        Assert.Equal((FeedEventKind.AccountLimitReached, FeedSeverity.Info), (draft.Kind, draft.Severity));
        Assert.Equal("Claude Max: usage limit reached, resets in 2 h 5 min", draft.Lead);
        Assert.Empty(watcher.Observe([Window(100, resets)], Now.AddMinutes(1)));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_provider_report_of_the_same_limit_is_not_a_second_event_but_a_new_reset_time_is()
    {
        var watcher = new AccountWindowWatcher();
        var resets = Now.AddHours(3);
        Assert.NotNull(watcher.NoteLimit("acct-1", "Claude Max", resets, Now));
        Assert.Null(watcher.NoteLimit("acct-1", "Claude Max", resets.AddSeconds(20), Now.AddSeconds(5)));
        Assert.Null(watcher.NoteLimit("acct-1", "Claude Max", null, Now.AddSeconds(5)));
        Assert.NotNull(watcher.NoteLimit("acct-1", "Claude Max", resets.AddHours(5), Now.AddMinutes(1)));
        Assert.NotNull(watcher.NoteLimit("acct-2", "Codex", resets, Now));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_limit_with_no_reset_time_is_quiet_for_half_an_hour_and_a_reset_lets_the_next_limit_through()
    {
        var watcher = new AccountWindowWatcher();
        var first = watcher.NoteLimit("acct-1", "Codex", null, Now);
        Assert.Equal("Codex: usage limit reached", first!.Lead);
        Assert.Null(watcher.NoteLimit("acct-1", "Codex", null, Now.AddMinutes(29)));
        Assert.NotNull(watcher.NoteLimit("acct-1", "Codex", null, Now.AddMinutes(31)));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void After_a_reset_the_same_window_can_report_a_limit_again()
    {
        var watcher = new AccountWindowWatcher();
        var resets = Now.AddMinutes(30);
        watcher.Observe([Window(99, resets)], Now);
        Assert.Single(watcher.Observe([Window(100, resets)], Now.AddMinutes(1)));
        Assert.Single(watcher.Observe([Window(0, Now.AddHours(5).AddMinutes(30))], Now.AddMinutes(31)));
        watcher.Observe([Window(99, Now.AddHours(5).AddMinutes(30))], Now.AddHours(2));
        var again = Assert.Single(watcher.Observe([Window(100, Now.AddHours(5).AddMinutes(30))], Now.AddHours(3)));
        Assert.Equal(FeedEventKind.AccountLimitReached, again.Kind);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    public void A_reading_that_is_not_a_number_is_ignored()
    {
        var watcher = new AccountWindowWatcher();
        watcher.Observe([Window(40)], Now);
        Assert.Empty(watcher.Observe([Window(double.NaN), Window(double.PositiveInfinity)], Now.AddMinutes(1)));
        Assert.Single(watcher.Observe([Window(45)], Now.AddMinutes(2)));
    }

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-7")]
    [InlineData(-5, "under a minute")]
    [InlineData(30, "under a minute")]
    [InlineData(60, "1 min")]
    [InlineData(2_700, "45 min")]
    [InlineData(7_500, "2 h 5 min")]
    [InlineData(172_800, "2 days")]
    public void A_wait_is_said_in_minutes_hours_or_days(int seconds, string expected) =>
        Assert.Equal(expected, AccountWindowWatcher.Relative(TimeSpan.FromSeconds(seconds)));
}
