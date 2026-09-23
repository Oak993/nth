using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/MetaKeychain.swift (B2 1fbf81e), :1-16.
//
// MetaKeychain keeps Meta's MODEL_API_KEY under the account "Meta API Key" (:6) through APIKeychain (:8-15), which
// stores one generic password per account under the one service AppInfo.name (APIKeychain.swift:29-33). On Windows the
// item is a Credential Manager credential written by the secret store (OD-0008 B3); these tests run it over the
// in-memory backend (Fixtures/InMemoryCredentialBackend.cs), with SecretStore.Shared and AppInfo.Running overridden for
// the test's own flow. Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up.
public sealed class MetaKeychainTests
{
    private const string Service = "Marrow";

    // MetaKeychain.swift:6 `private static let account = "Meta API Key"`.
    private const string Account = "Meta API Key";

    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp(string name = Service) =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleDisplayName"] = name }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private static string Target(string account) => SecretStore.TargetName(Service, account);

    // A credential written straight into the backend, as an existing Keychain item holding this key would be found.
    private void Plant(string account, string text) =>
        Assert.Equal(0, backend.Write(new GenericCredential(
            Target(account), account, Encoding.UTF8.GetBytes(text), 1, 2, [])));

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:6, :13-15
    public void SetAPIKey_stores_the_key_under_the_account_Meta_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(MetaKeychain.SetAPIKey("sk-meta-1"));

        Assert.Equal([SecretStore.TargetName(Service, "Meta API Key")], backend.TargetNames);
        var held = backend.Peek(SecretStore.TargetName(Service, "Meta API Key"));
        Assert.NotNull(held);
        // The account string byte for byte: "Meta API Key".
        Assert.Equal(
            new byte[] { 0x4D, 0x65, 0x74, 0x61, 0x20, 0x41, 0x50, 0x49, 0x20, 0x4B, 0x65, 0x79 },
            Encoding.UTF8.GetBytes(held.UserName));
        Assert.Equal(Encoding.UTF8.GetBytes("sk-meta-1"), held.Blob);
        Assert.Equal(1, held.Type);
        Assert.Equal(2, held.Persist);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:6, :8-10
    public void ApiKey_reads_an_existing_key_stored_under_Meta_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "sk-meta-existing");

        Assert.Equal("sk-meta-existing", MetaKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:6, :8-10
    public void ApiKey_ignores_keys_stored_under_other_accounts_and_returns_the_fallback()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant("DeepSeek API Key", "sk-deepseek"); // Services/Providers/DeepSeekKeychain.swift:6
        Plant("Z.ai API Key", "sk-zai"); // Services/Providers/ZaiKeychain.swift:6
        Plant("Meta", "sk-other");

        Assert.Equal("sk-fallback", MetaKeychain.ApiKey(fallback: "sk-fallback"));
        Assert.Equal("", MetaKeychain.ApiKey(fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:6, :8-15
    public void SetAPIKey_then_ApiKey_round_trips_the_trimmed_key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(MetaKeychain.SetAPIKey("  sk-meta-2 \n"));

        Assert.Equal("sk-meta-2", MetaKeychain.ApiKey(fallback: "sk-fallback"));
        Assert.Equal(Encoding.UTF8.GetBytes("sk-meta-2"), backend.Peek(Target(Account))!.Blob);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:6, :13-15; APIKeychain.swift:22-27
    public void SetAPIKey_with_an_empty_key_deletes_only_the_Meta_item_and_returns_true()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "sk-meta-3");
        Plant("DeepSeek API Key", "sk-deepseek"); // Services/Providers/DeepSeekKeychain.swift:6
        backend.Operations.Clear();

        Assert.True(MetaKeychain.SetAPIKey(" \t "));

        Assert.Null(backend.Peek(Target(Account)));
        Assert.Contains("delete " + Target(Account), backend.Operations);
        Assert.Equal([Target("DeepSeek API Key")], backend.TargetNames);
        Assert.Equal("sk-fallback", MetaKeychain.ApiKey(fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/MetaKeychain.swift:12-15; APIKeychain.swift:35-39
    public void SetAPIKey_returns_false_when_the_store_refuses_the_add()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(MetaKeychain.SetAPIKey("sk-meta-4"));

        Assert.Empty(backend.TargetNames);
        Assert.Equal("sk-fallback", MetaKeychain.ApiKey(fallback: "sk-fallback"));
    }
}
