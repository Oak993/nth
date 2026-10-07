using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Testing;
using System.Text;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountKeyMetadataRecoveryTests
{
    [Theory]
    [InlineData("At")]
    [InlineData("Usd")]
    public void An_incomplete_spend_entry_cannot_silently_reopen_a_cap_and_its_original_document_is_preserved(string missing)
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var id = accounts.ForService(ProviderKind.Deepseek)[0].Id;
        var spend = missing == "At" ? $"{{\"Key\":\"{id}\",\"Usd\":19}}" : $"{{\"Key\":\"{id}\",\"At\":\"2026-10-05T00:00:00Z\"}}";
        var bytes = Encoding.UTF8.GetBytes($"{{\"Version\":1,\"Orders\":{{}},\"Caps\":{{\"{id}\":17}},\"Spend\":[{spend}]}}");
        settings.Set(AccountKeyStore.SettingsKey, bytes);
        var store = new AccountKeyStore(accounts, settings, _ => "offline-key");
        Assert.True(store.SaveBlocked);
        Assert.Empty(store.Keys(ProviderKind.Deepseek.Id));
        Assert.Throws<InvalidOperationException>(() => store.SetCap(id, 20));
        Assert.Equal(bytes, Assert.IsType<byte[]>(settings.Object(AccountKeyStore.SettingsKey)));
    }

    [Fact]
    public void Unreadable_account_identities_cannot_be_used_to_replace_key_caps_or_spend_metadata()
    {
        var settings = new InMemorySettingsStore();
        var original = "{broken"u8.ToArray();
        settings.Set(AccountStore.SettingsKey, original);
        var recovered = AccountStore.LegacyReadOnly(settings);
        var id = recovered.ForService(ProviderKind.Deepseek)[0].Id;
        var store = new AccountKeyStore(recovered, settings, _ => "offline-key");
        Assert.True(store.SaveBlocked);
        Assert.Empty(store.Keys(ProviderKind.Deepseek.Id));
        Assert.Throws<InvalidOperationException>(() => store.SetCap(id, 20));
        Assert.Throws<InvalidOperationException>(() => store.Add(id.ToString("D"), 3, DateTimeOffset.UtcNow));
        Assert.Equal(original, Assert.IsType<byte[]>(settings.Object(AccountStore.SettingsKey)));
        Assert.Null(settings.Object(AccountKeyStore.SettingsKey));
    }
}
