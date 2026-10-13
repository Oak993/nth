using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps"; ED-NEW-dextra-as15-2): a cap is the most a piece of work may spend: percent of any account's
// windows and/or dollars, per task, per assistant a day, or the API keys' dollars a day. The check is a function of what was used and what
// the next turn needs; the tightest window is the one reported; a limit the owner raised once is in it.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-2")]
public sealed class CostCapMathTests
{
    private static CostCap Five() => new("c", CostCapScope.Task) { Percent = 5 };

    private static CapUse Used(double fiveHour, double weekly = 0, string account = "claude:20x")
    {
        var use = new CapUse();
        use.Add(account, ReserveWindowKind.FiveHour, fiveHour);
        use.Add(account, ReserveWindowKind.Weekly, weekly);
        return use;
    }

    private static CapTurn Turn(double fiveHour, double weekly = 0, decimal dollars = 0, string account = "claude:20x") => new(account, "Claude 20x",
        new Dictionary<ReserveWindowKind, double> { [ReserveWindowKind.FiveHour] = fiveHour, [ReserveWindowKind.Weekly] = weekly }, dollars);

    [Fact]
    public void Between_turns_a_cap_holds_once_the_use_has_reached_it_and_not_before()
    {
        Assert.False(CostCapMath.Check(Five(), Used(4.99), null).Crossed);
        var reached = CostCapMath.Check(Five(), Used(5), null);
        Assert.True(reached.Crossed);
        Assert.Equal(new CapReach("claude:20x", "claude:20x", ReserveWindowKind.FiveHour, 5, 5, 0), reached.Reach);
        Assert.True(CostCapMath.Check(Five(), Used(6), null).Crossed);
    }

    [Fact]
    public void Before_a_turn_the_use_plus_the_turns_p90_must_stay_within_the_cap()
    {
        Assert.False(CostCapMath.Check(Five(), Used(3.7), Turn(1.3)).Crossed);            // exactly 5: within
        var over = CostCapMath.Check(Five(), Used(3.8), Turn(1.3));
        Assert.True(over.Crossed);
        Assert.Equal("Claude 20x", over.Reach!.AccountName);
        Assert.Equal(ReserveWindowKind.FiveHour, over.Reach.Window);
        Assert.Equal(3.8, over.Reach.Used);
        Assert.Equal(1.3, over.Reach.Needs);
        Assert.Equal(5, over.Reach.Limit);
        Assert.True(over.Reach.Room(beforeTurn: true) < 0);
    }

    [Fact]
    public void The_tightest_window_is_the_one_that_holds_it()
    {
        var verdict = CostCapMath.Check(Five(), Used(fiveHour: 4, weekly: 3), Turn(fiveHour: 2, weekly: 2.5));    // 6 and 5.5, both over; the 5-hour has less room
        Assert.Equal(ReserveWindowKind.FiveHour, verdict.Reach!.Window);
        var onlyWeekly = CostCapMath.Check(Five(), Used(fiveHour: 1, weekly: 4.5), Turn(fiveHour: 0.5, weekly: 1));
        Assert.Equal(ReserveWindowKind.Weekly, onlyWeekly.Reach!.Window);
    }

    [Fact]
    public void A_turn_can_only_pass_the_account_it_runs_on_but_between_turns_any_account_at_the_cap_holds()
    {
        var use = Used(5.5, account: "claude:20x");
        use.Add("codex:a", ReserveWindowKind.FiveHour, 0.5);
        Assert.False(CostCapMath.Check(Five(), use, Turn(1, account: "codex:a")).Crossed);   // on Codex A it has room, whatever Claude 20x used
        Assert.True(CostCapMath.Check(Five(), use, Turn(1, account: "claude:20x")).Crossed);
        Assert.True(CostCapMath.Check(Five(), use, null).Crossed);
    }

    [Fact]
    public void Between_turns_the_check_can_be_asked_of_the_account_the_work_is_running_on()
    {
        var use = Used(5.5, account: "claude:20x");
        use.Add("codex:a", ReserveWindowKind.FiveHour, 0.5);
        Assert.True(CostCapMath.Check(Five(), use, null, running: new CapAccount("claude:20x", false)).Crossed);
        Assert.False(CostCapMath.Check(Five(), use, null, running: new CapAccount("codex:a", false)).Crossed);    // what another account used is not what it spends now
        Assert.True(CostCapMath.Check(Five(), use, null).Crossed);                                                  // not asked: every account the work used counts, as before

        var spent = new CapUse();
        spent.AddDollars(3m);
        var dollars = new CostCap("d", CostCapScope.Task) { Dollars = 2 };
        Assert.True(CostCapMath.Check(dollars, spent, null, running: new CapAccount("openrouter:key", true)).Crossed);
        Assert.False(CostCapMath.Check(dollars, spent, null, running: new CapAccount("claude:20x", false)).Crossed);   // a subscription account spends no money
        Assert.True(CostCapMath.Check(dollars, spent, null).Crossed);
    }

    [Fact]
    public void What_the_owner_let_it_go_over_once_raises_that_windows_limit_only()
    {
        var allowance = new CapAllowance(new Dictionary<(string, ReserveWindowKind), double> { [("claude:20x", ReserveWindowKind.FiveHour)] = 2 }, 0);
        Assert.False(CostCapMath.Check(Five(), Used(5.5), Turn(1), allowance).Crossed);     // 6.5 against 7
        Assert.True(CostCapMath.Check(Five(), Used(5.5), Turn(2), allowance).Crossed);      // 7.5 against 7
        Assert.True(CostCapMath.Check(Five(), Used(0, weekly: 5.5), Turn(0, weekly: 1), allowance).Crossed);   // the weekly window got none
    }

    [Fact]
    public void A_cap_in_dollars_counts_the_money_and_a_turn_that_spends_none_cannot_pass_it()
    {
        var cap = new CostCap("d", CostCapScope.ApiDay) { Dollars = 2m };
        var use = new CapUse();
        use.AddDollars(1.8m);
        Assert.False(CostCapMath.Check(cap, use, Turn(0, dollars: 0.2m, account: "openrouter:key")).Crossed);
        var over = CostCapMath.Check(cap, use, Turn(0, dollars: 0.21m, account: "openrouter:key"));
        Assert.True(over.Crossed);
        Assert.True(over.Reach!.IsDollars);
        Assert.Equal(1.8, over.Reach.Used, 6);
        Assert.Equal(2, over.Reach.Limit);

        var spent = new CapUse();
        spent.AddDollars(2.5m);
        Assert.False(CostCapMath.Check(cap, spent, Turn(1)).Crossed);                       // a subscription turn costs no dollars
        Assert.True(CostCapMath.Check(cap, spent, null).Crossed);                           // but between turns the money is spent
        var more = new CapAllowance(new Dictionary<(string, ReserveWindowKind), double>(), 1m);
        Assert.False(CostCapMath.Check(cap, spent, null, more).Crossed);
    }

    [Fact]
    public void A_cap_with_both_percent_and_dollars_holds_at_whichever_comes_first()
    {
        var cap = new CostCap("b", CostCapScope.AssistantDay) { Percent = 10, Dollars = 1m };
        var use = Used(2);
        use.AddDollars(0.95m);
        var verdict = CostCapMath.Check(cap, use, Turn(1, dollars: 0.1m));
        Assert.True(verdict.Crossed);
        Assert.True(verdict.Reach!.IsDollars);
        Assert.False(CostCapMath.Check(cap, Used(2), Turn(1)).Crossed);
    }

    [Fact]
    public void The_cap_is_read_safely_a_percent_is_held_to_0_to_100_and_a_key_cap_is_dollars_alone()
    {
        Assert.Equal(100, new CostCap("a", CostCapScope.Task) { Percent = 140 }.Normalized().Percent);
        Assert.Equal(0, new CostCap("a", CostCapScope.Task) { Percent = -3 }.Normalized().Percent);
        Assert.Null(new CostCap("a", CostCapScope.Task) { Percent = double.NaN }.Normalized().Percent);
        Assert.Equal(0m, new CostCap("a", CostCapScope.Task) { Dollars = -4m }.Normalized().Dollars);
        var key = new CostCap("k", CostCapScope.ApiDay) { Percent = 10, Dollars = 5m }.Normalized();
        Assert.Null(key.Percent);
        Assert.Equal(5m, key.Dollars);
        Assert.False(new CostCap("n", CostCapScope.Task).Limits);
        Assert.False(new CostCap("k", CostCapScope.ApiDay) { Percent = 10 }.Limits);
        Assert.True(Five().Limits);
        Assert.False(CostCapMath.Check(new CostCap("n", CostCapScope.Task), Used(50), Turn(50)).Crossed);   // a cap that limits nothing holds nothing
    }

    [Fact]
    public void A_cap_covers_the_spends_it_is_about()
    {
        var mine = new CapSubject("dextra", "claude:20x", "Claude 20x", IsApiKey: false);
        var other = new CapSubject("channel-a", "claude:20x", "Claude 20x", IsApiKey: false);
        var key = new CapSubject("dextra", "openrouter:key", "OpenRouter", IsApiKey: true);
        Assert.True(Five().Covers(other));
        Assert.True(new CostCap("a", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "dextra" }.Covers(mine));
        Assert.False(new CostCap("a", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "dextra" }.Covers(other));
        Assert.True(new CostCap("a", CostCapScope.AssistantDay) { Percent = 15 }.Covers(other));                 // no one named: each assistant
        Assert.False(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m }.Covers(mine));                       // a subscription is not an API key
        Assert.True(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m }.Covers(key));
        Assert.False(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m, AccountNames = new Marrow.Core.Port.ValueList<string>(["Deepseek"]) }.Covers(key));
        Assert.True(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m, AccountNames = new Marrow.Core.Port.ValueList<string>(["  openrouter "]) }.Covers(key with { AccountName = "OPENROUTER" }));
        Assert.True(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m, AccountNames = new Marrow.Core.Port.ValueList<string>(["openrouter:key"]) }.Covers(key));
    }

    [Fact]
    public void Asking_to_go_over_once_adds_what_the_next_step_needs_rounded_up_and_at_least_one()
    {
        Assert.Equal(1, CostCapMath.AllowanceNeeded(new CapReach("a", "A", ReserveWindowKind.FiveHour, 4, 5, 1.3)));       // 0.3 over
        Assert.Equal(2, CostCapMath.AllowanceNeeded(new CapReach("a", "A", ReserveWindowKind.FiveHour, 4, 5, 3)));         // 2 over
        Assert.Equal(3, CostCapMath.AllowanceNeeded(new CapReach("a", "A", ReserveWindowKind.FiveHour, 4, 5, 3.2)));       // 2.2 over
        Assert.Equal(1, CostCapMath.AllowanceNeeded(new CapReach("a", "A", ReserveWindowKind.FiveHour, 5, 5, 0)));         // reached, nothing needed yet
    }
}
