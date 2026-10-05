using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountDecisionStoreTests
{
    [Fact]
    public void OD0116_starts_unset_and_one_remember_scope_survives_reload_without_enabling_other_threads()
    {
        var settings = new InMemorySettingsStore();
        var store = new AccountDecisionStore(settings);
        var thread = Guid.NewGuid();
        Assert.All(Enum.GetValues<DecisionKind>(), kind => Assert.Null(store.GetSetting(kind)));
        Assert.True(new DecisionPolicy(store).Resolve(thread, DecisionKind.OnLimit).FirstTime);
        new DecisionPolicy(store).Record(thread, [new(DecisionKind.OnLimit, DecisionValues.Switch), new(DecisionKind.Bring, DecisionValues.Full)], RememberScope.ThisThread);
        var restored = new AccountDecisionStore(settings);
        Assert.Equal(DecisionValues.Switch, restored.GetThread(thread, DecisionKind.OnLimit));
        Assert.Equal(DecisionValues.Full, restored.GetThread(thread, DecisionKind.Bring));
        Assert.Null(restored.GetThread(Guid.NewGuid(), DecisionKind.OnLimit));
        Assert.Null(restored.GetSetting(DecisionKind.OnLimit));
        restored.ClearThread(thread);
        Assert.Null(new AccountDecisionStore(settings).GetThread(thread, DecisionKind.OnLimit));
    }

    [Fact]
    public void Corrupt_preferences_and_invalid_choices_are_preserved_and_fail_closed()
    {
        var settings = new InMemorySettingsStore();
        var bytes = "invalid"u8.ToArray();
        settings.Set(AccountDecisionStore.SettingsKey, bytes);
        Assert.Throws<InvalidDataException>(() => new AccountDecisionStore(settings));
        Assert.Same(bytes, settings.Object(AccountDecisionStore.SettingsKey));
        var store = new AccountDecisionStore(new InMemorySettingsStore());
        Assert.Throws<ArgumentException>(() => store.SetSetting(DecisionKind.Bring, DecisionValues.Switch));
        Assert.Null(store.GetSetting(DecisionKind.Bring));
    }

    [Fact]
    public void Once_is_not_saved_and_all_threads_is_saved_as_a_settings_choice()
    {
        var settings = new InMemorySettingsStore();
        var store = new AccountDecisionStore(settings);
        var thread = Guid.NewGuid();
        var policy = new DecisionPolicy(store);
        policy.Record(thread, [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime);
        Assert.Null(new AccountDecisionStore(settings).GetSetting(DecisionKind.OnLimit));
        Assert.Null(new AccountDecisionStore(settings).GetThread(thread, DecisionKind.OnLimit));
        policy.Record(thread, [new(DecisionKind.OnLimit, DecisionValues.Ask)], RememberScope.AllThreads);
        var reopened = new AccountDecisionStore(settings);
        Assert.Equal(DecisionValues.Ask, reopened.GetSetting(DecisionKind.OnLimit));
        Assert.True(new DecisionPolicy(reopened).Resolve(Guid.NewGuid(), DecisionKind.OnLimit).MustAsk);
        Assert.False(new DecisionPolicy(reopened).Resolve(thread, DecisionKind.OnLimit).FirstTime);
    }

    [Fact]
    public void Selecting_a_pill_supplies_routing_consent_for_that_thread_without_changing_settings()
    {
        var stored = new AccountDecisionStore(new InMemorySettingsStore());
        var thread = Guid.NewGuid();
        Pill? pill = new Pill("Work", [new("a"), new("b")]);
        var consent = new PillDecisionStore(stored, thread, () => pill);
        var policy = new DecisionPolicy(consent);
        Assert.Equal(DecisionValues.Switch, policy.Resolve(thread, DecisionKind.OnLimit).Value);
        Assert.Equal(DecisionValues.MarrowSummary, policy.Resolve(thread, DecisionKind.Bring).Value);
        Assert.Null(consent.GetThread(Guid.NewGuid(), DecisionKind.OnLimit));
        Assert.Null(stored.GetSetting(DecisionKind.OnLimit));
        pill = null;
        Assert.True(policy.Resolve(thread, DecisionKind.OnLimit).FirstTime);
    }
}
