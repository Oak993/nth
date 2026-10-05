using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Pills", "Pills loop", "Enable/disable per account": steps cross services without asking; the loop goes to the highest-ranked available step; a step on a disabled account is skipped.
public sealed class NextAccountChooserPillTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Pill Four = new("Long haul",
    [
        new PillStep("c1", Model: "opus", Effort: "high", Fast: true),
        new PillStep("c2"),
        new PillStep("x1", Model: "gpt", Effort: "medium", Fast: false),
        new PillStep("d1"),
    ]);

    private static (NextAccountChooser Chooser, InMemoryAccountStore Store, InMemoryAccountLimits Limits) Make()
    {
        var store = new InMemoryAccountStore([.. HandshakeRig.DefaultAccounts(), new AccountInfo("d1", "deepseek", "DeepSeek")]);
        var limits = new InMemoryAccountLimits();
        foreach (var a in store.Accounts) limits.SetUsage(a.Id, new AccountUsage(10, null));
        return (new NextAccountChooser(store, limits, new Marrow.Testing.ManualTimeProvider(Now)), store, limits);
    }

    private static void Exhaust(InMemoryAccountLimits limits, string id, int hours) => limits.SetUsage(id, new AccountUsage(100, Now.AddHours(hours), IsOut: true));

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_moves_to_the_next_step_with_that_steps_model_effort_and_fast_mode()
    {
        var (chooser, _, _) = Make();
        var move = Assert.IsType<ChooserResult.Move>(chooser.Next("c2", Four, 1));
        Assert.Equal("x1", move.Target.Account.Id);
        Assert.Equal(2, move.Target.StepIndex);
        Assert.Equal(new StepSettings("gpt", "medium", false), move.Target.Settings);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T11")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_crosses_services_without_asking()
    {
        var (chooser, _, _) = Make();
        Assert.IsType<ChooserResult.Move>(chooser.Next("c2", Four, 1));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T12")]
    [Trait("Rule", "R-TEST-4")]
    public void A_step_on_a_disabled_or_out_account_is_skipped()
    {
        var (chooser, store, limits) = Make();
        store.Update("x1", a => a with { Enabled = false });
        Assert.Equal("d1", Assert.IsType<ChooserResult.Move>(chooser.Next("c2", Four, 1)).Target.Account.Id);
        store.Update("x1", a => a with { Enabled = true });
        Exhaust(limits, "x1", 2);
        Assert.Equal("d1", Assert.IsType<ChooserResult.Move>(chooser.Next("c2", Four, 1)).Target.Account.Id);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T13")]
    [Trait("Rule", "R-TEST-4")]
    public void When_the_last_step_runs_out_the_pill_loops_to_the_highest_available_step()
    {
        var (chooser, _, limits) = Make();
        Exhaust(limits, "d1", 1);
        Exhaust(limits, "c1", 3);
        Exhaust(limits, "c2", 3);
        var move = Assert.IsType<ChooserResult.Move>(chooser.Next("d1", Four, 3));
        Assert.Equal("x1", move.Target.Account.Id);
        Assert.Equal(2, move.Target.StepIndex);
        limits.SetUsage("c1", new AccountUsage(0, null));
        Assert.Equal(0, Assert.IsType<ChooserResult.Move>(chooser.Next("d1", Four, 3)).Target.StepIndex);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T14")]
    [Trait("Rule", "R-TEST-4")]
    public void With_no_step_available_the_pill_waits_for_the_earliest_reset()
    {
        var (chooser, _, limits) = Make();
        Exhaust(limits, "c1", 5);
        Exhaust(limits, "c2", 4);
        Exhaust(limits, "x1", 2);
        Exhaust(limits, "d1", 3);
        var wait = Assert.IsType<ChooserResult.WaitFor>(chooser.Next("d1", Four, 3));
        Assert.Equal(Now.AddHours(2), wait.EarliestReset);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_wait_ignores_a_disabled_steps_reset()
    {
        var (chooser, store, limits) = Make();
        foreach (var id in new[] { "c1", "c2", "x1", "d1" }) Exhaust(limits, id, 4);
        Exhaust(limits, "x1", 1);
        store.Update("x1", a => a with { Enabled = false });
        Assert.Equal(Now.AddHours(4), Assert.IsType<ChooserResult.WaitFor>(chooser.Next("d1", Four, 3)).EarliestReset);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T16")]
    [Trait("Rule", "R-TEST-4")]
    public void Earlier_back_in_a_pill_is_the_highest_ranked_available_earlier_step()
    {
        var (chooser, _, limits) = Make();
        Exhaust(limits, "c1", 1);
        Assert.Equal(1, chooser.EarlierBack("x1", Four, 2)!.StepIndex);
        limits.SetUsage("c1", new AccountUsage(0, null));
        var back = chooser.EarlierBack("x1", Four, 2)!;
        Assert.Equal(0, back.StepIndex);
        Assert.Equal(new StepSettings("opus", "high", true), back.Settings);
        Assert.Null(chooser.EarlierBack("c1", Four, 0));
    }
}
