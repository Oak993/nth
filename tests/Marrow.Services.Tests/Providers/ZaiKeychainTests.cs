using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ZaiKeychain.swift (B2 1fbf81e), :1-16.
//
// ZaiKeychain keeps the Z.ai API key under the account "Z.ai API Key" (:6) through APIKeychain (:9, :14), which
// stores it as a generic password under the one service AppInfo.name (APIKeychain.swift:29-33). On Windows the item
// is a Credential Manager credential written by the secret store (OD-0008 B3); these tests run it over the in-memory
// backend (Fixtures/InMemoryCredentialBackend.cs), with SecretStore.Shared and AppInfo.Running overridden for the
// test's own flow. Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up.
public sealed class ZaiKeychainTests
{
    private const string Service = "Marrow";

    // ZaiKeychain.swift:6, written out byte for byte: Z, a, full stop, i, space, API, space, Key.
    private const string Account = "Z.ai API Key";

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
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:6, :12-15
    public void SetAPIKey_writes_one_credential_under_the_account_Z_ai_API_Key_in_the_app_service()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(ZaiKeychain.SetAPIKey("zai-made-up-1"));

        Assert.Equal([Target("Z.ai API Key")], backend.TargetNames);
        var held = backend.Peek(Target("Z.ai API Key"));
        Assert.NotNull(held);
        Assert.Equal(Encoding.UTF8.GetBytes("Z.ai API Key"), Encoding.UTF8.GetBytes(held.UserName));
        Assert.Equal(Encoding.UTF8.GetBytes("zai-made-up-1"), held.Blob);
        Assert.Equal("zai-made-up-1", SecretStore.Shared.Current.Read(Service, "Z.ai API Key"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:6, :8-10
    public void ApiKey_reads_the_value_stored_under_the_account_Z_ai_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "zai-made-up-2");

        Assert.Equal("zai-made-up-2", ZaiKeychain.ApiKey(fallback: "zai-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:6, :8-10
    public void ApiKey_ignores_keys_stored_under_any_other_spelling_or_casing_of_the_account()
    {
        using var app = UseApp();
        using var store = UseStore();
        // Near misses of "Z.ai API Key" (:6): casing, the missing dot, and the other providers' accounts
        // (DeepSeekKeychain.swift:6, MetaKeychain.swift:6).
        Plant("z.ai API Key", "wrong-1");
        Plant("Z.AI API Key", "wrong-2");
        Plant("Zai API Key", "wrong-3");
        Plant("Z.ai API key", "wrong-4");
        Plant("DeepSeek API Key", "wrong-5");
        Plant("Meta API Key", "wrong-6");

        Assert.Equal("zai-fallback", ZaiKeychain.ApiKey(fallback: "zai-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:8-10; APIKeychain.swift:13-17
    public void ApiKey_returns_the_fallback_unchanged_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.Equal("  zai-fallback \n", ZaiKeychain.ApiKey(fallback: "  zai-fallback \n"));
        Assert.Equal("", ZaiKeychain.ApiKey(fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:8-15
    public void A_key_set_through_SetAPIKey_is_the_key_ApiKey_returns_trimmed()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(ZaiKeychain.SetAPIKey("  zai-made-up-3 "));

        // APIKeychain.swift:23 trims on write, :54-55 on read.
        Assert.Equal("zai-made-up-3", ZaiKeychain.ApiKey(fallback: "zai-fallback"));
        Assert.Equal("zai-made-up-3", APIKeychain.Key(@for: "Z.ai API Key", fallback: "zai-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:12-15; APIKeychain.swift:24-27, :58-65
    public void SetAPIKey_with_an_empty_key_removes_the_Z_ai_credential_and_returns_true()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "zai-made-up-4");
        Plant("Meta API Key", "meta-made-up");

        Assert.True(ZaiKeychain.SetAPIKey("   "));

        Assert.Null(backend.Peek(Target(Account)));
        Assert.NotNull(backend.Peek(Target("Meta API Key")));
        Assert.Equal("zai-fallback", ZaiKeychain.ApiKey(fallback: "zai-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/ZaiKeychain.swift:12-15; APIKeychain.swift:34-39
    public void SetAPIKey_returns_false_when_the_store_refuses_the_add()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(ZaiKeychain.SetAPIKey("zai-made-up-5"));

        Assert.Null(backend.Peek(Target(Account)));
    }
}
