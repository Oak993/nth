using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Thread;

// MA-10 (step 3b, job 0): the seam the popover and the composer bind to for one thread's accounts, and the fake that honours it (pin, clear, order), over the real account store.
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ThreadAccountPortTests
{
    private static (AccountsRig Rig, FakeThreadAccountPort Port, Account A, Account B, Account C) Three()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var c = rig.ConnectAnother(ProviderKind.Claude, "Pro");
        return (rig, new FakeThreadAccountPort(rig, ProviderKind.Claude), a, b, c);
    }

    [Fact]
    public void The_accounts_are_the_connected_ones_of_the_service_in_the_stores_order_numbered_from_one()
    {
        var (rig, port, a, b, c) = Three();
        rig.Accounts.Add(ProviderKind.Claude, "Max"); // added but never signed in: not shown
        rig.Connect(ProviderKind.Codex, "Plus");
        Assert.Equal(ProviderKind.Claude, port.Service);
        Assert.Equal([a.Id, b.Id, c.Id], port.Accounts.Select(x => x.Id));
        Assert.Equal([1, 2, 3], port.Accounts.Select(x => x.Number));
        Assert.Equal(["Claude Max 1", "Claude Max 2", "Claude Pro"], port.Accounts.Select(x => x.Name));
        Assert.Equal(["M1", "M2", "P"], port.Accounts.Select(x => x.Mono));
    }

    [Fact]
    public void Reordering_the_store_changes_the_order_the_port_shows_and_says_so()
    {
        var (rig, port, a, b, c) = Three();
        var changed = 0;
        port.Changed += () => changed++;
        rig.Accounts.Reorder(ProviderKind.Claude, [c.Id, a.Id, b.Id]);
        port.Refresh();
        Assert.Equal([c.Id, a.Id, b.Id], port.Accounts.Select(x => x.Id));
        Assert.Equal([1, 2, 3], port.Accounts.Select(x => x.Number));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_custom_name_is_what_the_row_says_and_the_monogram_takes_its_initials()
    {
        var (rig, port, a, _, _) = Three();
        rig.Accounts.Rename(a.Id, "Work laptop");
        port.Refresh();
        var row = port.Accounts[0];
        Assert.Equal(("Work laptop", "WL", true), (row.Name, row.Mono, row.CustomName));
    }

    [Fact]
    public void With_no_pin_the_active_account_is_the_first_one_that_is_on_and_not_out()
    {
        var (rig, port, a, b, _) = Three();
        Assert.Equal(a.Id, port.ActiveAccountId);
        Assert.Null(port.PinnedAccountId);
        rig.Accounts.SetEnabled(a.Id, false);
        port.Refresh();
        Assert.Equal(b.Id, port.ActiveAccountId);
        rig.Usage[b.Id] = new AccountUsageLine(1, DateTimeOffset.UnixEpoch, 0.4, null, false, false);
        port.Refresh();
        Assert.Equal(port.Accounts[2].Id, port.ActiveAccountId);
    }

    [Fact]
    public async Task Pinning_an_account_makes_it_the_active_one_remembers_the_bring_and_says_so()
    {
        var (_, port, _, b, _) = Three();
        var changed = 0;
        port.Changed += () => changed++;
        Assert.Equal(PinResult.Pinned, await port.PinAsync(b.Id, Bring.Full));
        Assert.Equal(b.Id, port.PinnedAccountId);
        Assert.Equal(b.Id, port.ActiveAccountId);
        Assert.Equal([(b.Id, (Bring?)Bring.Full)], port.Pins);
        Assert.Equal(1, changed);
        Assert.Equal(PinResult.Pinned, await port.PinAsync(b.Id));
        Assert.Equal((b.Id, (Bring?)null), port.Pins[^1]);
    }

    [Fact]
    public async Task A_pin_the_account_cannot_take_is_refused_with_the_reason_and_changes_nothing()
    {
        var (rig, port, a, b, c) = Three();
        rig.Accounts.SetEnabled(b.Id, false);
        rig.Usage[c.Id] = new AccountUsageLine(0.3, null, 1, DateTimeOffset.UnixEpoch, false, false);
        port.Refresh();
        var changed = 0;
        port.Changed += () => changed++;
        Assert.Equal(PinResult.Disabled, await port.PinAsync(b.Id));
        Assert.Equal(PinResult.OutOfService, await port.PinAsync(c.Id));
        Assert.Equal(PinResult.UnknownAccount, await port.PinAsync(Guid.NewGuid()));
        var codex = rig.Connect(ProviderKind.Codex, "Plus");
        Assert.Equal(PinResult.UnknownAccount, await port.PinAsync(codex.Id));
        var notSignedIn = rig.Accounts.Add(ProviderKind.Claude, "Max");
        Assert.Equal(PinResult.UnknownAccount, await port.PinAsync(notSignedIn.Id));
        Assert.Null(port.PinnedAccountId);
        Assert.Equal(a.Id, port.ActiveAccountId);
        Assert.Empty(port.Pins);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Back_to_order_clears_the_pin_and_the_active_account_follows_the_order_again()
    {
        var (_, port, a, b, _) = Three();
        await port.PinAsync(b.Id);
        var changed = 0;
        port.Changed += () => changed++;
        port.BackToOrder();
        Assert.Null(port.PinnedAccountId);
        Assert.Equal(a.Id, port.ActiveAccountId);
        Assert.Equal(1, changed);
        port.BackToOrder(); // nothing pinned: nothing to say
        Assert.Equal(1, changed);
    }

    [Fact]
    public void The_thread_remembers_what_it_was_asked_and_use_settings_forgets_it()
    {
        var (rig, port, _, _, _) = Three();
        Assert.Empty(port.ThreadChoices);
        new DecisionPolicy(rig.Decisions).Record(port.ThreadId, [new(DecisionKind.OnLimit, DecisionValues.Switch), new(DecisionKind.Bring, DecisionValues.Full)], RememberScope.ThisThread);
        port.Refresh();
        Assert.Equal([new Choice(DecisionKind.OnLimit, DecisionValues.Switch), new Choice(DecisionKind.Bring, DecisionValues.Full)], port.ThreadChoices);
        var changed = 0;
        port.Changed += () => changed++;
        port.UseSettings();
        Assert.Empty(port.ThreadChoices);
        Assert.Null(rig.Decisions.GetThread(port.ThreadId, DecisionKind.OnLimit));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_row_carries_what_the_service_reported_and_what_follows_from_it()
    {
        var (rig, port, a, b, _) = Three();
        rig.Usage[a.Id] = new AccountUsageLine(0.62, DateTimeOffset.UnixEpoch, 0.3, null, false, false);
        rig.Usage[b.Id] = new AccountUsageLine(null, null, 1, DateTimeOffset.UnixEpoch, true, false);
        port.Refresh();
        var rows = port.Accounts;
        Assert.Equal(0.62, rows[0].Usage!.FiveHour);
        Assert.False(rows[0].IsOut);
        Assert.True(rows[1].IsOut);
        Assert.True(rows[1].NoFiveHour);
        Assert.Null(rows[2].Usage);
        Assert.False(rows[2].IsOut);
        Assert.All(rows, r => Assert.True(r.Enabled));
    }

    [Fact]
    public void A_local_entry_has_no_limit_and_is_never_out()
    {
        var rig = new AccountsRig();
        var local = rig.Connect(ProviderKind.Local);
        rig.Usage[local.Id] = new AccountUsageLine(1, null, 1, null, false, false);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Local);
        var row = Assert.Single(port.Accounts);
        Assert.True(row.NoLimit);
        Assert.False(row.IsOut);
        Assert.False(row.NoFiveHour);
    }
}
