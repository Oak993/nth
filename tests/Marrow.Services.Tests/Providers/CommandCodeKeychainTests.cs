using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeKeychain.swift (B2 1fbf81e), :1-18.
//
// CommandCodeKeychain keeps the optional Command Code Studio API key through the shared APIKeychain under the account
// "Command Code API Key" (:8), reading with apiKey(fallback:) (:10-12) and writing with setAPIKey (:14-17). On Windows
// the item is a Credential Manager credential written by the secret store (OD-0008 B3); these tests run it over the
// in-memory backend (Fixtures/InMemoryCredentialBackend.cs) with SecretStore.Shared and AppInfo.Running overridden for
// the test's own flow. The service is the port's AppInfo name, which stands for AppInfo.name (APIKeychain.swift:31).
// Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up.
//
// Not tested here, because it is not this file's behaviour: the failure text under the key field and the removal of
// the legacy defaults key "commandcodeAPIKey" (AppSettings.swift:399-408, OD-0008 A6, OD-0012 E5) belong to the
// AppSettings unit; the capture-run guard (APIKeychain.swift:14) is not ported (dead_code.md entry 44, ED-061).
public sealed class CommandCodeKeychainTests
{
    private const string Service = "Marrow";

    // CommandCodeKeychain.swift:8, verbatim.
    private const string Account = "Command Code API Key";

    // The three other API-key accounts in the same service (PR-SETTINGS-272 expected_result).
    private const string DeepSeekAccount = "DeepSeek API Key"; // Services/Providers/DeepSeekKeychain.swift:6
    private const string MetaAccount = "Meta API Key"; // Services/Providers/MetaKeychain.swift:6
    private const string ZaiAccount = "Z.ai API Key"; // Services/Providers/ZaiKeychain.swift:6

    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp() =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleDisplayName"] = Service }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private static string Target(string account) => SecretStore.TargetName(Service, account);

    // A credential written straight into the backend, as a Keychain item holding these bytes would be found.
    private void Plant(string account, string text) =>
        Assert.Equal(0, backend.Write(new GenericCredential(
            Target(account), account, Encoding.UTF8.GetBytes(text), 1, 2, [])));

    // ---- setAPIKey (:14-17) ----------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:8, :14-17
    public void SetAPIKey_stores_the_trimmed_key_as_one_credential_under_the_account_Command_Code_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        // APIKeychain.swift:23 trims whitespace and newlines; :28 stores the UTF-8 bytes.
        Assert.True(CommandCodeKeychain.SetAPIKey(" \tcc-studio-key-1\r\n"));

        Assert.Equal([Target(Account)], backend.TargetNames);
        var held = backend.Peek(Target(Account));
        Assert.NotNull(held);
        Assert.Equal("Command Code API Key", held.UserName);
        Assert.Equal(Encoding.UTF8.GetBytes("cc-studio-key-1"), held.Blob);
        Assert.Equal(1, held.Type);
        Assert.Equal(2, held.Persist);
        Assert.Equal(SecretStore.TargetName("Marrow", "Command Code API Key"), held.TargetName);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:14-17; APIKeychain.swift:34-39
    public void SetAPIKey_replaces_an_existing_key_by_delete_then_add()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "cc-old-2");
        backend.Operations.Clear();

        Assert.True(CommandCodeKeychain.SetAPIKey("cc-new-2"));

        var deleteAt = backend.Operations.IndexOf("delete " + Target(Account));
        var writeAt = backend.Operations.IndexOf("write " + Target(Account));
        Assert.True(deleteAt >= 0, "the existing item is deleted (APIKeychain.swift:34)");
        Assert.True(writeAt > deleteAt, "the add follows the delete (APIKeychain.swift:35-39)");
        Assert.Equal(Encoding.UTF8.GetBytes("cc-new-2"), backend.Peek(Target(Account))!.Blob);
        Assert.Equal("cc-new-2", CommandCodeKeychain.ApiKey(fallback: "cc-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:14-17; APIKeychain.swift:22-27
    public void SetAPIKey_with_an_empty_or_blank_key_deletes_the_credential_and_counts_as_stored()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "cc-stored-3");
        backend.Operations.Clear();

        Assert.True(CommandCodeKeychain.SetAPIKey(" \n\t "));

        Assert.Null(backend.Peek(Target(Account)));
        Assert.Contains("delete " + Target(Account), backend.Operations);
        Assert.DoesNotContain(backend.Operations, op => op.StartsWith("write ", StringComparison.Ordinal));

        // An empty key with nothing stored is also a success.
        Assert.True(CommandCodeKeychain.SetAPIKey(""));
        Assert.Empty(backend.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:14-17; APIKeychain.swift:34-39
    public void SetAPIKey_returns_false_when_the_store_refuses_the_key_and_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "cc-old-4");
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(CommandCodeKeychain.SetAPIKey("cc-new-4"));

        backend.BeforeWrite = null;
        // The delete (APIKeychain.swift:34) already ran; OD-0008 A6: no copy of the key is kept anywhere.
        Assert.Empty(backend.TargetNames);
        Assert.Equal("", CommandCodeKeychain.ApiKey(fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:8, :14-17
    public void SetAPIKey_leaves_the_three_other_API_key_accounts_alone()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, "ds-5");
        Plant(MetaAccount, "meta-5");
        Plant(ZaiAccount, "zai-5");

        Assert.True(CommandCodeKeychain.SetAPIKey("cc-5"));
        Assert.Equal(4, backend.TargetNames.Count);
        Assert.True(CommandCodeKeychain.SetAPIKey(""));

        Assert.Equal(3, backend.TargetNames.Count);
        Assert.Equal(Encoding.UTF8.GetBytes("ds-5"), backend.Peek(Target(DeepSeekAccount))!.Blob);
        Assert.Equal(Encoding.UTF8.GetBytes("meta-5"), backend.Peek(Target(MetaAccount))!.Blob);
        Assert.Equal(Encoding.UTF8.GetBytes("zai-5"), backend.Peek(Target(ZaiAccount))!.Blob);
    }

    // ---- apiKey(fallback:) (:10-12) --------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:8, :10-12; APIKeychain.swift:13-17
    public void ApiKey_returns_the_key_stored_under_Command_Code_API_Key_trimmed()
    {
        using var app = UseApp();
        using var store = UseStore();
        // A stored value is trimmed on read (APIKeychain.swift:54-55).
        Plant("Command Code API Key", "  cc-stored-6\n");
        Plant(MetaAccount, "meta-6");

        Assert.Equal("cc-stored-6", CommandCodeKeychain.ApiKey(fallback: "cc-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:10-12; APIKeychain.swift:15-16
    public void ApiKey_returns_the_fallback_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();
        // Another account's key is not the Command Code key.
        Plant(DeepSeekAccount, "ds-7");

        // AppSettings.swift:447 reads with the empty fallback.
        Assert.Equal("", CommandCodeKeychain.ApiKey(fallback: ""));
        Assert.Equal(" cc-fallback-7 ", CommandCodeKeychain.ApiKey(fallback: " cc-fallback-7 "));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:10-12; APIKeychain.swift:15-16, :54
    public void ApiKey_returns_the_fallback_when_the_stored_value_is_blank()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, " \r\n ");

        Assert.Equal("cc-fallback-8", CommandCodeKeychain.ApiKey(fallback: "cc-fallback-8"));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/CommandCodeKeychain.swift:10-12; APIKeychain.swift:42-56
    public void ApiKey_reads_only_the_Command_Code_credential_and_writes_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(Account, "cc-stored-9");
        backend.Operations.Clear();

        CommandCodeKeychain.ApiKey(fallback: "");

        Assert.Equal(["read " + Target("Command Code API Key")], backend.Operations);
    }
}
