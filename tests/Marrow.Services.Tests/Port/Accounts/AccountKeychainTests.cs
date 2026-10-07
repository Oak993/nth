using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-SEC-1")]
public sealed class AccountKeychainTests
{
    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.OpenRouter)]
    [InlineData(ProviderKind.Commandcode)]
    public void Legacy_key_stays_at_its_original_target_and_new_accounts_have_independent_protected_keys(ProviderKind service)
    {
        using var secrets = SecretStore.Shared.Override(new SecretStore(new InMemoryCredentialBackend()));
        var settings = new InMemorySettingsStore();
        Assert.True(APIKeychain.SetKey("legacy-test-key", service));
        var store = new AccountStore(settings);
        var legacy = store.ForService(service)[0];
        var first = new Account { Id = Guid.NewGuid(), Service = service };
        var second = new Account { Id = Guid.NewGuid(), Service = service };
        Assert.Equal("legacy-test-key", APIKeychain.Key(legacy, ""));
        Assert.Equal("", APIKeychain.Key(first, ""));
        Assert.True(APIKeychain.SetKey("first-test-key", first));
        Assert.True(APIKeychain.SetKey("second-test-key", second));
        Assert.Equal("first-test-key", APIKeychain.Key(first, ""));
        Assert.Equal("second-test-key", APIKeychain.Key(second, ""));
        Assert.Equal("legacy-test-key", APIKeychain.Key(service, ""));
        Assert.True(APIKeychain.SetKey("", first));
        Assert.Equal("", APIKeychain.Key(first, ""));
        Assert.Equal("second-test-key", APIKeychain.Key(second, ""));
        Assert.Equal("legacy-test-key", APIKeychain.Key(legacy, ""));
        Assert.DoesNotContain("test-key", Encoding.UTF8.GetString((byte[])settings.Object(AccountStore.SettingsKey)!), StringComparison.Ordinal);
    }

    [Fact]
    public void Same_account_id_under_different_services_cannot_share_a_key()
    {
        using var secrets = SecretStore.Shared.Override(new SecretStore(new InMemoryCredentialBackend()));
        var account = new Account { Id = Guid.NewGuid(), Service = ProviderKind.Deepseek };
        Assert.True(APIKeychain.SetKey("synthetic", account));
        Assert.Equal("", APIKeychain.Key(account with { Service = ProviderKind.Zai }, ""));
    }

    [Fact]
    public void Refused_key_storage_keeps_the_previous_key_and_writes_no_plaintext_fallback()
    {
        var backend = new InMemoryCredentialBackend();
        using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var account = new Account { Id = Guid.NewGuid(), Service = ProviderKind.OpenRouter };
        Assert.True(APIKeychain.SetKey("previous-synthetic", account));
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;
        Assert.False(APIKeychain.SetKey("synthetic", account));
        Assert.Equal("previous-synthetic", APIKeychain.Key(account, ""));
        Assert.False(APIKeychain.SetKey("synthetic", account with { Service = ProviderKind.Claude }));
    }
}
