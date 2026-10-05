using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Testing;
using System.Text;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountKeyStoreTests
{
    [Fact]
    public void Keys_project_the_real_account_identity_order_allowed_state_and_vault_secret_without_saving_the_secret()
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var first = accounts.ForService(ProviderKind.Deepseek)[0];
        var second = accounts.Add(ProviderKind.Deepseek);
        accounts.Rename(first.Id, "Key One");
        accounts.Rename(second.Id, "Key Two");
        accounts.Reorder(ProviderKind.Deepseek, [second.Id, first.Id]);
        accounts.SetEnabled(first.Id, false);
        var joined = new AccountKeyStore(accounts, settings, id => id == second.Id ? "secret-only-in-vault" : null);
        joined.SetCap(second.Id, 17);
        var keys = joined.Keys(ProviderKind.Deepseek.Id);
        Assert.Equal([second.Id.ToString("D"), first.Id.ToString("D")], keys.Select(key => key.Id));
        Assert.Equal("Key Two", keys[0].Nickname);
        Assert.Equal(17, keys[0].MonthlyCapUsd);
        Assert.True(keys[0].Allowed);
        Assert.False(keys[1].Allowed);
        Assert.Equal("secret-only-in-vault", joined.Secret(keys[0].Id));
        Assert.DoesNotContain("secret-only-in-vault", Encoding.UTF8.GetString(Assert.IsType<byte[]>(settings.Object(AccountKeyStore.SettingsKey))), StringComparison.Ordinal);
    }

    [Fact]
    public void Order_caps_and_reported_dollar_spend_survive_restart_and_month_boundaries_remain_the_pools_responsibility()
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var account = accounts.ForService(ProviderKind.OpenRouter)[0];
        var joined = new AccountKeyStore(accounts, settings, _ => "offline-key");
        var before = new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(3));
        var after = new DateTimeOffset(2026, 10, 1, 0, 1, 0, TimeSpan.FromHours(3));
        joined.SetOrder(ProviderKind.OpenRouter.Id, KeyOrderMode.MostCreditsFirst);
        joined.SetCap(account.Id, 20);
        joined.Add(account.Id.ToString("D"), 5, before);
        joined.Add(account.Id.ToString("D"), 2, after);
        var restored = new AccountKeyStore(new(settings), settings, _ => "offline-key");
        Assert.Equal(KeyOrderMode.MostCreditsFirst, restored.Order(ProviderKind.OpenRouter.Id));
        Assert.Equal(20, Assert.Single(restored.Keys(ProviderKind.OpenRouter.Id)).MonthlyCapUsd);
        Assert.Equal(7, restored.SpendSince(account.Id.ToString("D"), DateTimeOffset.MinValue));
        Assert.Equal(2, restored.SpendSince(account.Id.ToString("D"), new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(3))));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_caps_and_costs_are_rejected_without_writing_metadata(double value)
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var account = accounts.ForService(ProviderKind.Deepseek)[0];
        var joined = new AccountKeyStore(accounts, settings, _ => "offline-key");
        var writes = settings.Writes.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => joined.SetCap(account.Id, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => joined.Add(account.Id.ToString("D"), value, DateTimeOffset.UtcNow));
        Assert.Equal(writes, settings.Writes.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Version\":2,\"Orders\":{},\"Caps\":{},\"Spend\":[]}")]
    public void Unreadable_or_newer_metadata_is_preserved_and_no_key_rotation_or_edit_can_replace_it(string json)
    {
        var settings = new InMemorySettingsStore();
        var bytes = Encoding.UTF8.GetBytes(json);
        settings.Set(AccountKeyStore.SettingsKey, bytes);
        var accounts = new AccountStore(settings);
        var account = accounts.ForService(ProviderKind.Deepseek)[0];
        var joined = new AccountKeyStore(accounts, settings, _ => "offline-key");
        Assert.True(joined.SaveBlocked);
        Assert.Empty(joined.Keys(ProviderKind.Deepseek.Id));
        Assert.Throws<InvalidOperationException>(() => joined.SetCap(account.Id, 17));
        Assert.Throws<InvalidOperationException>(() => joined.SetOrder(ProviderKind.Deepseek.Id, KeyOrderMode.SpreadEvenly));
        Assert.Equal(bytes, Assert.IsType<byte[]>(settings.Object(AccountKeyStore.SettingsKey)));
    }
}
