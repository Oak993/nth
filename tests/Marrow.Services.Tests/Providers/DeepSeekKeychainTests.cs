using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/DeepSeekKeychain.swift (B2 1fbf81e), :1-16.
//
// DeepSeekKeychain delegates both calls to APIKeychain with the private account "DeepSeek API Key" (:6, :8-15).
// On Windows the item is a Credential Manager credential written by the secret store under the one service name the
// port uses in place of AppInfo.name (OD-0008 B3, ED-093, ED-120); these tests run it over the in-memory backend
// (Fixtures/InMemoryCredentialBackend.cs), with SecretStore.Shared and AppInfo.Running overridden for the test's own
// flow, as APIKeychainTests does. No real secret appears here; every value is made up.
public sealed class DeepSeekKeychainTests
{
    private const string Service = "Marrow";

    // DeepSeekKeychain.swift:6
    private const string Account = "DeepSeek API Key";

    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp(string name = Service) =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleDisplayName"] = name }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private static string Target(string account) => SecretStore.TargetName(Service, account);

    // A credential written straight into the backend, as a Keychain item holding these bytes would be found.
    private void Plant(string account, string text) =>
        Assert.Equal(0, backend.Write(new GenericCredential(
            Target(account), account, Encoding.UTF8.GetBytes(text), 1, 2, [])));

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:6, :13-15
    public void SetAPIKey_stores_the_key_under_the_account_DeepSeek_API_Key_in_the_one_service()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(DeepSeekKeychain.SetAPIKey("sk-deepseek-1"));

        // The account string byte for byte: "DeepSeek API Key" (:6).
        var accountBytes = new byte[]
        {
            0x44, 0x65, 0x65, 0x70, 0x53, 0x65, 0x65, 0x6B, 0x20, 0x41, 0x50, 0x49, 0x20, 0x4B, 0x65, 0x79,
        };
        Assert.Equal([Target(Account)], backend.TargetNames);
        var held = backend.Peek(Target(Account));
        Assert.NotNull(held);
        Assert.Equal(accountBytes, Encoding.UTF8.GetBytes(held.UserName));
        Assert.Equal(Encoding.UTF8.GetBytes("sk-deepseek-1"), held.Blob);
        Assert.StartsWith(Service + ":" + Account + ":", held.TargetName, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:6, :8-10
    public void ApiKey_reads_an_existing_key_stored_under_the_account_DeepSeek_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "sk-deepseek-2");

        Assert.Equal("sk-deepseek-2", DeepSeekKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:6, :8-10
    public void ApiKey_does_not_read_a_key_stored_under_any_other_account()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant("DeepSeek API key", "sk-other-1");
        Plant("DeepSeek", "sk-other-2");
        Plant("Meta API Key", "sk-other-3"); // MetaKeychain.swift:6

        Assert.Equal("sk-fallback", DeepSeekKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:8-10; APIKeychain.swift:13-17
    public void ApiKey_returns_the_fallback_unchanged_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.Operations.Clear();

        Assert.Equal("  sk-fallback \n", DeepSeekKeychain.ApiKey(fallback: "  sk-fallback \n"));
        Assert.Equal(["read " + Target(Account)], backend.Operations);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:8-15
    public void SetAPIKey_and_ApiKey_are_APIKeychain_with_the_account_DeepSeek_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(DeepSeekKeychain.SetAPIKey(" sk-deepseek-3\n"));
        Assert.Equal("sk-deepseek-3", APIKeychain.Key(@for: Account, fallback: "sk-fallback"));

        Assert.True(APIKeychain.SetKey("sk-deepseek-4", account: Account));
        Assert.Equal("sk-deepseek-4", DeepSeekKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:12-15; APIKeychain.swift:22-27
    public void SetAPIKey_with_an_empty_key_deletes_the_DeepSeek_item_and_returns_true()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "sk-deepseek-5");
        Plant("Meta API Key", "sk-meta-5"); // MetaKeychain.swift:6

        Assert.True(DeepSeekKeychain.SetAPIKey(" \t\n"));

        Assert.Null(backend.Peek(Target(Account)));
        Assert.Equal([Target("Meta API Key")], backend.TargetNames);
        Assert.Equal("sk-fallback", DeepSeekKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/DeepSeekKeychain.swift:12-15; APIKeychain.swift:34-39
    public void SetAPIKey_returns_false_when_the_store_refuses_the_add()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(DeepSeekKeychain.SetAPIKey("sk-deepseek-6"));

        backend.BeforeWrite = null;
        Assert.Empty(backend.TargetNames);
    }
}
