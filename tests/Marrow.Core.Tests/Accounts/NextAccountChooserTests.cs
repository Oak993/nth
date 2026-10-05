using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Account popover and priority": row 1 is the default; when it runs out Marrow goes to row 2, then row 3; exhausted rows are skipped; disabled accounts are never used.
public sealed class NextAccountChooserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static (NextAccountChooser Chooser, InMemoryAccountStore Store, InMemoryAccountLimits Limits) Make(IEnumerable<AccountInfo>? accounts = null)
    {
        var store = new InMemoryAccountStore(accounts ?? HandshakeRig.DefaultAccounts());
        var limits = new InMemoryAccountLimits();
        foreach (var a in store.Accounts) limits.SetUsage(a.Id, new AccountUsage(10, null));
        return (new NextAccountChooser(store, limits, new Marrow.Testing.ManualTimeProvider(Now)), store, limits);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_next_account_is_the_next_one_in_priority_order_of_the_same_service()
    {
        var (chooser, _, _) = Make();
        var move = Assert.IsType<ChooserResult.Move>(chooser.Next("c1", null, null));
        Assert.Equal("c2", move.Target.Account.Id);
        Assert.Null(move.Target.StepIndex);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T2")]
    [Trait("Rule", "R-TEST-4")]
    public void Disabled_and_out_accounts_are_skipped()
    {
        var (chooser, store, limits) = Make();
        store.Update("c2", a => a with { Enabled = false });
        Assert.Equal("c3", Assert.IsType<ChooserResult.Move>(chooser.Next("c1", null, null)).Target.Account.Id);
        store.Update("c2", a => a with { Enabled = true });
        limits.SetUsage("c2", new AccountUsage(100, Now.AddHours(2), IsOut: true));
        Assert.Equal("c3", Assert.IsType<ChooserResult.Move>(chooser.Next("c1", null, null)).Target.Account.Id);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T3")]
    [Trait("Rule", "R-TEST-4")]
    public void An_account_past_its_reported_reset_time_counts_as_available()
    {
        var (chooser, _, limits) = Make();
        limits.SetUsage("c2", new AccountUsage(100, Now.AddMinutes(-1), IsOut: true));
        Assert.Equal("c2", Assert.IsType<ChooserResult.Move>(chooser.Next("c1", null, null)).Target.Account.Id);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T4")]
    [Trait("Rule", "R-TEST-4")]
    public void When_the_last_row_runs_out_the_highest_ranked_available_row_is_next()
    {
        var (chooser, _, limits) = Make();
        limits.SetUsage("c1", new AccountUsage(100, Now.AddHours(1), IsOut: true));
        Assert.Equal("c2", Assert.IsType<ChooserResult.Move>(chooser.Next("c3", null, null)).Target.Account.Id);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T5")]
    [Trait("Rule", "R-TEST-4")]
    public void With_the_whole_service_out_another_service_is_offered_but_never_taken_outside_a_pill()
    {
        var (chooser, _, limits) = Make();
        foreach (var id in new[] { "c1", "c2", "c3" }) limits.SetUsage(id, new AccountUsage(100, Now.AddHours(1), IsOut: true));
        var other = Assert.IsType<ChooserResult.OtherService>(chooser.Next("c1", null, null));
        Assert.Equal("x1", other.Target.Account.Id);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T6")]
    [Trait("Rule", "R-TEST-4")]
    public void With_nothing_available_the_choice_waits_for_the_earliest_reset()
    {
        var (chooser, _, limits) = Make();
        limits.SetUsage("c1", new AccountUsage(100, Now.AddHours(3), IsOut: true));
        limits.SetUsage("c2", new AccountUsage(100, Now.AddHours(1), IsOut: true));
        limits.SetUsage("c3", new AccountUsage(100, Now.AddHours(2), IsOut: true));
        limits.SetUsage("x1", new AccountUsage(100, Now.AddMinutes(5), IsOut: true));
        var wait = Assert.IsType<ChooserResult.WaitFor>(chooser.Next("c1", null, null));
        Assert.Equal(Now.AddHours(1), wait.EarliestReset);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T7")]
    [Trait("Rule", "R-TEST-4")]
    public void With_no_enabled_account_in_reach_there_is_nothing_to_wait_for()
    {
        var (chooser, store, _) = Make([new AccountInfo("c1", "claude", "Claude")]);
        store.Update("c1", a => a with { Enabled = false });
        Assert.IsType<ChooserResult.NoAccounts>(chooser.Next("c1", null, null));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T8")]
    [Trait("Rule", "R-TEST-4")]
    public void Earlier_back_is_the_highest_ranked_available_account_above_the_current_one()
    {
        var (chooser, _, limits) = Make();
        limits.SetUsage("c1", new AccountUsage(100, Now.AddHours(1), IsOut: true));
        Assert.Equal("c2", chooser.EarlierBack("c3", null, null)!.Account.Id);
        limits.SetUsage("c2", new AccountUsage(100, Now.AddHours(1), IsOut: true));
        Assert.Null(chooser.EarlierBack("c3", null, null));
        limits.SetUsage("c1", new AccountUsage(5, null));
        Assert.Equal("c1", chooser.EarlierBack("c3", null, null)!.Account.Id);
        Assert.Null(chooser.EarlierBack("c1", null, null));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T9")]
    [Trait("Rule", "R-TEST-4")]
    public void Earlier_back_never_proposes_a_disabled_account()
    {
        var (chooser, store, _) = Make();
        store.Update("c1", a => a with { Enabled = false });
        Assert.Equal("c2", chooser.EarlierBack("c3", null, null)!.Account.Id);
    }
}
