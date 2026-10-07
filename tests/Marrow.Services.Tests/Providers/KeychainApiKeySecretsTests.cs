using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (MA-6): several keys per provider over APIKeychain. The first key keeps the legacy account; the others are suffixed.
public sealed class KeychainApiKeySecretsTests
{
    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp() =>
        AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = "Marrow" }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T125")]
    [Trait("Rule", "R-TEST-4")]
    public void The_first_key_keeps_the_account_older_builds_used_and_further_keys_get_their_own()
    {
        var secrets = new KeychainApiKeySecrets(ProviderKind.Deepseek, "k1");
        Assert.Equal("DeepSeek API Key", secrets.Account("k1"));
        Assert.Equal("DeepSeek API Key [k2]", secrets.Account("k2"));
        Assert.Equal("OpenRouter API Key [k3]", new KeychainApiKeySecrets(ProviderKind.OpenRouter, "k1").Account("k3"));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T126")]
    [Trait("Rule", "R-TEST-4")]
    public void Keys_are_written_read_and_deleted_one_by_one_and_the_first_is_the_one_APIKeychain_reads()
    {
        using var app = UseApp();
        using var store = UseStore();
        var secrets = new KeychainApiKeySecrets(ProviderKind.Zai, "k1");
        Assert.True(secrets.Write("k1", "sk-one"));
        Assert.True(secrets.Write("k2", "sk-two"));
        Assert.Equal("sk-one", APIKeychain.Key(ProviderKind.Zai, ""));
        Assert.Equal(("sk-one", "sk-two"), (secrets.Read("k1"), secrets.Read("k2")));
        secrets.Delete("k2");
        Assert.Null(secrets.Read("k2"));
        Assert.Equal("sk-one", secrets.Read("k1"));
        Assert.True(secrets.Write("k1", ""));
        Assert.Null(secrets.Read("k1"));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T127")]
    [Trait("Rule", "R-TEST-4")]
    public void A_provider_with_no_account_has_no_secrets_and_touches_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        var secrets = new KeychainApiKeySecrets(ProviderKind.Claude, "k1");
        Assert.Null(secrets.Account("k1"));
        Assert.False(secrets.Write("k1", "sk-x"));
        Assert.Null(secrets.Read("k1"));
        secrets.Delete("k1");
        Assert.Empty(backend.Operations);
    }
}
