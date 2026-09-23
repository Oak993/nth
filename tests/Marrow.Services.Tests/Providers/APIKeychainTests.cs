using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/APIKeychain.swift (B2 1fbf81e), :1-66.
//
// APIKeychain stores each native provider's API key as a generic password under the one service AppInfo.name
// (:29-33), one account per provider. On Windows the item is a Credential Manager credential written by the secret
// store (OD-0008 B3, ED-093, ED-120); these tests run it over the in-memory backend (Fixtures/
// InMemoryCredentialBackend.cs), with SecretStore.Shared and AppInfo.Running overridden for the test's own flow.
// The service is the port's AppInfo name, the one name the port uses in place of AppInfo.name.
// Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up.
//
// The capture-run guard (:14 `guard !CaptureRun.isEnabled else { return "" }`) is not ported: CaptureRun is not
// ported (ED-001) and the guard is dead-code register entry 44 (porting/dead_code.md, ED-061), so no test drives it.
public sealed class APIKeychainTests
{
    private const string Service = "Marrow";

    // The four account strings of the provider helpers (PR-PROVREG-088).
    private const string DeepSeekAccount = "DeepSeek API Key"; // Services/Providers/DeepSeekKeychain.swift:6
    private const string MetaAccount = "Meta API Key"; // Services/Providers/MetaKeychain.swift:6
    private const string ZaiAccount = "Z.ai API Key"; // Services/Providers/ZaiKeychain.swift:6
    private const string CommandCodeAccount = "Command Code API Key"; // Services/Providers/CommandCodeKeychain.swift:8

    private readonly InMemoryCredentialBackend backend = new();

    private static IDisposable UseApp(string name = Service) =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleDisplayName"] = name }, isDevelopment: false));

    private IDisposable UseStore() => SecretStore.Shared.Override(new SecretStore(backend));

    private static string Target(string account) => SecretStore.TargetName(Service, account);

    // A credential written straight into the backend, as a Keychain item holding these bytes would be found.
    private void Plant(string account, byte[] blob) =>
        Assert.Equal(0, backend.Write(new GenericCredential(Target(account), account, blob, 1, 2, [])));

    private void Plant(string account, string text) => Plant(account, Encoding.UTF8.GetBytes(text));

    // ---- PR-PROVAPI-001: key(for:fallback:) --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:13-17
    public void Key_returns_the_stored_value_rather_than_the_fallback()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, "sk-stored-1");

        Assert.Equal("sk-stored-1", APIKeychain.Key(@for: DeepSeekAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:13-17
    public void Key_returns_the_fallback_unchanged_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.Equal("  sk-fallback \n", APIKeychain.Key(@for: MetaAccount, fallback: "  sk-fallback \n"));
        Assert.Equal("", APIKeychain.Key(@for: MetaAccount, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:13-17, :51
    public void Key_returns_the_fallback_when_the_read_is_refused()
    {
        using var app = UseApp();
        var refusing = new RefusingReadCredentialBackend(backend);
        using var store = SecretStore.Shared.Override(new SecretStore(refusing));
        Plant(ZaiAccount, "sk-stored-2");

        Assert.Equal("sk-fallback", APIKeychain.Key(@for: ZaiAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:13-17, :42-56
    public void Key_reads_the_store_once_and_writes_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(CommandCodeAccount, "sk-stored-3");
        backend.Operations.Clear();

        APIKeychain.Key(@for: CommandCodeAccount, fallback: "sk-fallback");

        Assert.Equal(["read " + Target(CommandCodeAccount)], backend.Operations);
    }

    // ---- PR-PROVAPI-002: setKey with an empty or whitespace-only value -------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22-27
    public void SetKey_with_a_whitespace_only_value_deletes_the_item_and_returns_true()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, "sk-stored-4");
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey(" \t\r\n ", account: DeepSeekAccount));

        Assert.Null(backend.Peek(Target(DeepSeekAccount)));
        Assert.Empty(backend.TargetNames);
        Assert.Contains("delete " + Target(DeepSeekAccount), backend.Operations);
        Assert.DoesNotContain(backend.Operations, op => op.StartsWith("write ", StringComparison.Ordinal));
        Assert.Equal("sk-fallback", APIKeychain.Key(@for: DeepSeekAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22-27
    public void SetKey_with_an_empty_value_and_no_item_returns_true_and_writes_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey("", account: MetaAccount));

        Assert.Empty(backend.TargetNames);
        Assert.DoesNotContain(backend.Operations, op => op.StartsWith("write ", StringComparison.Ordinal));
        Assert.Contains("delete " + Target(MetaAccount), backend.Operations);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22-27
    public void SetKey_with_an_empty_value_leaves_other_accounts_in_the_service_alone()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, "sk-stored-5");
        Plant(MetaAccount, "sk-stored-6");

        Assert.True(APIKeychain.SetKey("\n", account: ZaiAccount));

        Assert.Null(backend.Peek(Target(ZaiAccount)));
        Assert.Equal("sk-stored-6", APIKeychain.Key(@for: MetaAccount, fallback: ""));
    }

    // ---- PR-PROVAPI-003: setKey stores the trimmed key, delete-then-add ------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:22-39
    public void SetKey_stores_the_UTF8_bytes_of_the_trimmed_key_as_one_generic_credential()
    {
        using var app = UseApp();
        using var store = UseStore();
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE, so the stored bytes are UTF-8, not ASCII or UTF-16.
        const string key = "\t sk-caf\U000000E9-7 \r\n";

        Assert.True(APIKeychain.SetKey(key, account: DeepSeekAccount));

        var held = backend.Peek(Target(DeepSeekAccount));
        Assert.NotNull(held);
        Assert.Equal(Target(DeepSeekAccount), held.TargetName);
        Assert.Equal(DeepSeekAccount, held.UserName);
        Assert.Equal(new byte[] { 0x73, 0x6B, 0x2D, 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x2D, 0x37 }, held.Blob);
        Assert.Equal(1, held.Type);
        Assert.Equal(2, held.Persist);
        Assert.Empty(held.Attributes);
        Assert.Equal([Target(DeepSeekAccount)], backend.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:34-39
    public void SetKey_deletes_the_existing_item_before_it_adds_the_new_one()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, "sk-old-8");
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey("sk-new-8", account: MetaAccount));

        var target = Target(MetaAccount);
        var deleteAt = backend.Operations.IndexOf("delete " + target);
        var writeAt = backend.Operations.IndexOf("write " + target);
        Assert.True(deleteAt >= 0, "the existing item is deleted (SecItemDelete, :34)");
        Assert.True(writeAt > deleteAt, "the add comes after the delete (SecItemAdd, :39)");
        Assert.Single(backend.Operations, op => op == "write " + target);
        Assert.Equal("sk-new-8", APIKeychain.Key(@for: MetaAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:34-39
    public void SetKey_returns_false_when_the_add_is_refused_and_the_account_then_holds_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, "sk-old-9");
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(APIKeychain.SetKey("sk-new-9", account: ZaiAccount));

        backend.BeforeWrite = null;
        // The delete at :34 already ran, so nothing is left; OD-0008 A6: no copy of the key is written anywhere.
        Assert.Empty(backend.TargetNames);
        Assert.Equal("sk-fallback", APIKeychain.Key(@for: ZaiAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:39
    public void SetKey_returns_false_when_the_add_of_a_new_item_is_refused()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(APIKeychain.SetKey("sk-new-10", account: CommandCodeAccount));

        Assert.Empty(backend.TargetNames);
    }

    // ---- PR-PROVAPI-004: read trims and rejects a blank value ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:42-56
    public void A_stored_value_is_returned_trimmed_of_whitespace_and_newlines()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, " \t sk-stored-11 \r\n\n");

        Assert.Equal("sk-stored-11", APIKeychain.Key(@for: DeepSeekAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:51-54, :15-16
    public void A_whitespace_only_stored_value_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, " \n\t\r ");

        Assert.Equal("sk-fallback", APIKeychain.Key(@for: MetaAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:51-53, :15-16
    public void A_stored_value_that_is_not_UTF8_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, new byte[] { 0x73, 0x6B, 0xFF, 0xFE, 0x41 });

        Assert.Equal("sk-fallback", APIKeychain.Key(@for: ZaiAccount, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:51, :15-16
    public void A_missing_item_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, "sk-other-12");

        Assert.Equal("sk-fallback", APIKeychain.Key(@for: CommandCodeAccount, fallback: "sk-fallback"));
    }

    // ---- PR-PROVREG-088: four accounts under one service -----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:29-33
    public void The_four_provider_keys_are_four_credentials_under_one_service_told_apart_only_by_account()
    {
        using var app = UseApp();
        using var store = UseStore();
        string[] accounts = [DeepSeekAccount, MetaAccount, ZaiAccount, CommandCodeAccount];

        for (var i = 0; i < accounts.Length; i++)
            Assert.True(APIKeychain.SetKey("sk-provider-" + i, account: accounts[i]));

        Assert.Equal(accounts.Select(Target).Order(StringComparer.Ordinal), backend.TargetNames);
        for (var i = 0; i < accounts.Length; i++)
        {
            var held = backend.Peek(Target(accounts[i]));
            Assert.NotNull(held);
            Assert.StartsWith(Service + ":" + accounts[i] + ":", held.TargetName, StringComparison.Ordinal);
            Assert.Equal(accounts[i], held.UserName);
            Assert.Equal(Encoding.UTF8.GetBytes("sk-provider-" + i), held.Blob);
            Assert.Equal("sk-provider-" + i, APIKeychain.Key(@for: accounts[i], fallback: ""));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:31, :45, :61
    public void The_service_is_the_running_app_name()
    {
        using var app = UseApp("Marrow Test Name");
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-service-13", account: DeepSeekAccount));

        var target = SecretStore.TargetName("Marrow Test Name", DeepSeekAccount);
        Assert.Equal([target], backend.TargetNames);
        Assert.Equal("sk-service-13", APIKeychain.Key(@for: DeepSeekAccount, fallback: ""));
        Assert.True(APIKeychain.SetKey("", account: DeepSeekAccount));
        Assert.Empty(backend.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:29-39
    public void Every_write_is_a_delete_then_an_add_and_leaves_an_MCP_secret_in_the_same_service_alone()
    {
        using var app = UseApp();
        using var store = UseStore();
        // MCP secrets share the service under accounts 'MCP <server> <field>' (PR-PROVAPI-003 persistence).
        Plant("MCP github token", "mcp-value-14");

        Assert.True(APIKeychain.SetKey("sk-first-14", account: CommandCodeAccount));
        backend.Operations.Clear();
        Assert.True(APIKeychain.SetKey("sk-second-14", account: CommandCodeAccount));

        var target = Target(CommandCodeAccount);
        Assert.True(backend.Operations.IndexOf("delete " + target) >= 0);
        Assert.True(backend.Operations.IndexOf("write " + target) > backend.Operations.IndexOf("delete " + target));
        Assert.Equal("sk-second-14", APIKeychain.Key(@for: CommandCodeAccount, fallback: ""));
        Assert.Equal(Encoding.UTF8.GetBytes("mcp-value-14"), backend.Peek(Target("MCP github token"))!.Blob);
    }
}
