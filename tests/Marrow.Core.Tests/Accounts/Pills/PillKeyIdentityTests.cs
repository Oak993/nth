using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Pills;

[Trait("Rule", "R-TEST-4")]
public sealed class PillKeyIdentityTests
{
    [Fact]
    public void A_pill_key_step_chooses_its_actual_key_account_and_checks_that_keys_limit()
    {
        var store = new InMemoryAccountStore([
            new("source", ProviderKind.Claude.Id, "Source"),
            new("anchor", ProviderKind.OpenRouter.Id, "Anchor"),
            new("key", ProviderKind.OpenRouter.Id, "Key")]);
        var limits = new InMemoryAccountLimits();
        var clock = new ManualTimeProvider();
        limits.SetUsage("anchor", new(100, clock.GetUtcNow().AddHours(1)));
        var pill = new Pill("Relay", [new("source"), new("anchor", KeyId: "key")]);
        var target = Assert.IsType<ChooserResult.Move>(new NextAccountChooser(store, limits, clock).Next("source", pill, 0)).Target;
        Assert.Equal("key", target.Account.Id);
        Assert.Equal(1, target.StepIndex);
        Assert.Equal("key", target.Step!.KeyId);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("out")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public void An_unavailable_or_foreign_pill_key_is_skipped_without_falling_back_to_the_anchor_account(string problem)
    {
        var store = new InMemoryAccountStore([
            new("source", ProviderKind.Claude.Id, "Source"),
            new("anchor", ProviderKind.OpenRouter.Id, "Anchor"),
            new("key", problem == "foreign" ? ProviderKind.Deepseek.Id : ProviderKind.OpenRouter.Id, "Key", Enabled: problem != "off"),
            new("last", ProviderKind.Codex.Id, "Last")]);
        var limits = new InMemoryAccountLimits();
        var clock = new ManualTimeProvider();
        if (problem == "out") limits.SetUsage("key", new(100, clock.GetUtcNow().AddHours(1)));
        var pill = new Pill("Relay", [new("source"), new("anchor", KeyId: problem == "missing" ? "gone" : "key"), new("last")]);
        var target = Assert.IsType<ChooserResult.Move>(new NextAccountChooser(store, limits, clock).Next("source", pill, 0)).Target;
        Assert.Equal("last", target.Account.Id);
        Assert.Equal(2, target.StepIndex);
    }

    [Fact]
    public void A_key_steps_actual_account_persists_as_the_new_thread_and_restart_relay_identity()
    {
        var pills = new PillStore();
        var pill = pills.Create("Keys", [new("anchor", KeyId: "key"), new("later")]);
        pills.SetUseForNewThreads(pill.Id);
        var saved = pills.NewThreadRelay(_ => true);
        Assert.Equal(new PillThreadState(pill.Id, 0, "key"), saved);
        Assert.Equal(PillResumeReason.Exact, PillRelayResolver.Resolve(pills.Pills, saved!).Reason);
        pills.MoveStep(pill.Id, 0, 1);
        var resumed = PillRelayResolver.Resolve(pills.Pills, saved!);
        Assert.Equal(PillResumeReason.Moved, resumed.Reason);
        Assert.Equal(1, resumed.StepIndex);
    }
}
