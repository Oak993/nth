using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/APIKeychain.swift (B3 52bc792), :1-84.
//
// APIKeychain stores each of four native providers' API keys as a generic password under the one service
// AppInfo.name (:45-49), one account per provider, resolved from a ProviderKind by the private account(for:) table
// (:25-34, PR-PROVAPI-192). A provider not in that table (any provider but DeepSeek, Meta, Z.ai and Command Code)
// has no account: Key returns the fallback and SetKey returns false, neither one touching the store (:11, :21).
// On Windows the item is a Credential Manager credential written by the secret store (OD-0008 B3, ED-093, ED-120);
// these tests run it over the in-memory backend (Fixtures/InMemoryCredentialBackend.cs), with SecretStore.Shared and
// AppInfo.Running overridden for the test's own flow.
// The service is the port's AppInfo name, the one name the port uses in place of AppInfo.name.
// Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up.
//
// The capture-run guard (:12 `guard !CaptureRun.isEnabled else { return "" }`) is not ported: CaptureRun is not
// ported (ED-001) and the guard is dead-code register entry 44 (porting/dead_code.md, ED-061), so no test drives it.
//
// B3: the four per-provider wrapper types (DeepSeekKeychain, MetaKeychain, ZaiKeychain, CommandCodeKeychain) are
// deleted; the account-string assertions their own tests carried (PR-PROVAPI-005 to -007, PR-SETTINGS-272) move
// here, re-derived against the ProviderKind-keyed API. B3 also replaces the delete-then-add write (SetReplacing)
// with an update-in-place write (SetUpdating, PR-PROVAPI-003): a refused update keeps the previously stored key
// instead of leaving the account empty.
public sealed class APIKeychainTests
{
    private const string Service = "Nth"; // OD-0135: AppInfo.name, the service of every provider key

    // The four account strings of APIKeychain.account(for:) (:25-34, PR-PROVREG-088).
    private const string DeepSeekAccount = "DeepSeek API Key";        // APIKeychain.swift:28
    private const string MetaAccount = "Meta API Key";                // APIKeychain.swift:29
    private const string ZaiAccount = "Z.ai API Key";                 // APIKeychain.swift:30
    private const string CommandCodeAccount = "Command Code API Key"; // APIKeychain.swift:31

    // A provider with no entry in account(for:) (:32 `default: nil`); Claude is one of many (PR-PROVAPI-192).
    private const ProviderKind UnmappedProvider = ProviderKind.Claude;

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

    // ---- PR-PROVAPI-001: Key(for:fallback:) --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:10-14
    public void Key_returns_the_stored_value_rather_than_the_fallback()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, "sk-stored-1");

        Assert.Equal("sk-stored-1", APIKeychain.Key(ProviderKind.Deepseek, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:10-14
    public void Key_returns_the_fallback_unchanged_when_nothing_is_stored()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.Equal("  sk-fallback \n", APIKeychain.Key(ProviderKind.Meta, fallback: "  sk-fallback \n"));
        Assert.Equal("", APIKeychain.Key(ProviderKind.Meta, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:10-14, :69
    public void Key_returns_the_fallback_when_the_read_is_refused()
    {
        using var app = UseApp();
        var refusing = new RefusingReadCredentialBackend(backend);
        using var store = SecretStore.Shared.Override(new SecretStore(refusing));
        Plant(ZaiAccount, "sk-stored-2");

        Assert.Equal("sk-fallback", APIKeychain.Key(ProviderKind.Zai, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-001")] // Services/Providers/APIKeychain.swift:10-14, :60-74
    public void Key_reads_the_store_once_and_writes_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(CommandCodeAccount, "sk-stored-3");
        backend.Operations.Clear();

        APIKeychain.Key(ProviderKind.Commandcode, fallback: "sk-fallback");

        Assert.Equal(["read " + Target(CommandCodeAccount)], backend.Operations);
    }

    // ---- PR-PROVAPI-192: the account table; an unmapped provider touches nothing -------------------------------

    [Theory]
    [InlineData(ProviderKind.Deepseek, DeepSeekAccount)]
    [InlineData(ProviderKind.Meta, MetaAccount)]
    [InlineData(ProviderKind.Zai, ZaiAccount)]
    [InlineData(ProviderKind.Commandcode, CommandCodeAccount)]
    [Trait("Req", "PR-PROVAPI-192")] // Services/Providers/APIKeychain.swift:25-34
    public void Exactly_the_four_native_providers_map_to_their_legacy_account_strings(ProviderKind provider, string account)
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-mapped-" + account, provider));

        var held = backend.Peek(Target(account));
        Assert.NotNull(held);
        Assert.Equal(account, held.UserName);
        Assert.Equal("sk-mapped-" + account, APIKeychain.Key(provider, fallback: ""));
    }

    // The four deleted per-provider wrapper types' own tests (DeepSeekKeychainTests, MetaKeychainTests,
    // ZaiKeychainTests, CommandCodeKeychainTests) carried these account-string records; B3 deletes the wrappers
    // (:25-34), so the assertions move here, re-derived against the ProviderKind-keyed APIKeychain.

    [Fact]
    [Trait("Req", "PR-PROVAPI-005")] // Services/Providers/APIKeychain.swift:28 (was DeepSeekKeychain.swift:6)
    public void DeepSeek_stores_its_key_under_the_account_DeepSeek_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-deepseek-mapped", ProviderKind.Deepseek));

        var held = backend.Peek(Target(DeepSeekAccount));
        Assert.NotNull(held);
        Assert.Equal(DeepSeekAccount, held.UserName);
        Assert.Equal("sk-deepseek-mapped", APIKeychain.Key(ProviderKind.Deepseek, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-006")] // Services/Providers/APIKeychain.swift:29 (was MetaKeychain.swift:6)
    public void Meta_stores_its_key_under_the_account_Meta_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-meta-mapped", ProviderKind.Meta));

        var held = backend.Peek(Target(MetaAccount));
        Assert.NotNull(held);
        Assert.Equal(MetaAccount, held.UserName);
        Assert.Equal("sk-meta-mapped", APIKeychain.Key(ProviderKind.Meta, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-007")] // Services/Providers/APIKeychain.swift:30 (was ZaiKeychain.swift:6)
    public void Zai_stores_its_key_under_the_account_Z_ai_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-zai-mapped", ProviderKind.Zai));

        var held = backend.Peek(Target(ZaiAccount));
        Assert.NotNull(held);
        Assert.Equal(ZaiAccount, held.UserName);
        Assert.Equal("sk-zai-mapped", APIKeychain.Key(ProviderKind.Zai, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-272")] // Services/Providers/APIKeychain.swift:31 (was CommandCodeKeychain.swift:8)
    public void CommandCode_stores_its_key_under_the_account_Command_Code_API_Key()
    {
        using var app = UseApp();
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-commandcode-mapped", ProviderKind.Commandcode));

        var held = backend.Peek(Target(CommandCodeAccount));
        Assert.NotNull(held);
        Assert.Equal(CommandCodeAccount, held.UserName);
        Assert.Equal("sk-commandcode-mapped", APIKeychain.Key(ProviderKind.Commandcode, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-192")] // Services/Providers/APIKeychain.swift:11, :21, :26-33
    public void An_unmapped_provider_reads_the_fallback_without_touching_the_store()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.Operations.Clear();

        Assert.Equal("sk-fallback", APIKeychain.Key(UnmappedProvider, fallback: "sk-fallback"));

        Assert.Empty(backend.Operations);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-192")] // Services/Providers/APIKeychain.swift:20-23, :26-33
    public void An_unmapped_provider_write_returns_false_without_touching_the_store()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.Operations.Clear();

        Assert.False(APIKeychain.SetKey("sk-should-not-be-stored", UnmappedProvider));

        Assert.Empty(backend.Operations);
        Assert.Empty(backend.TargetNames);
    }

    // ---- PR-PROVAPI-002/PR-PROVAPI-003: SetKey trims, empty deletes, otherwise updates in place -----------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22, :39-43
    public void SetKey_with_a_whitespace_only_value_deletes_the_item_and_returns_true()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, "sk-stored-4");
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey(" \t\r\n ", ProviderKind.Deepseek));

        Assert.Null(backend.Peek(Target(DeepSeekAccount)));
        Assert.Empty(backend.TargetNames);
        Assert.Contains("delete " + Target(DeepSeekAccount), backend.Operations);
        Assert.DoesNotContain(backend.Operations, op => op.StartsWith("write ", StringComparison.Ordinal));
        Assert.Equal("sk-fallback", APIKeychain.Key(ProviderKind.Deepseek, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22, :39-43
    public void SetKey_with_an_empty_value_and_no_item_returns_true_and_writes_nothing()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey("", ProviderKind.Meta));

        Assert.Empty(backend.TargetNames);
        Assert.Contains("delete " + Target(MetaAccount), backend.Operations);
        Assert.DoesNotContain(backend.Operations, op => op.StartsWith("write ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-002")] // Services/Providers/APIKeychain.swift:22, :39-43
    public void SetKey_with_an_empty_value_leaves_other_accounts_in_the_service_alone()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, "sk-stored-5");
        Plant(MetaAccount, "sk-stored-6");

        Assert.True(APIKeychain.SetKey("\n", ProviderKind.Zai));

        Assert.Null(backend.Peek(Target(ZaiAccount)));
        Assert.Equal("sk-stored-6", APIKeychain.Key(ProviderKind.Meta, fallback: ""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:22, :38-49
    public void SetKey_stores_the_UTF8_bytes_of_the_trimmed_key_as_one_generic_credential()
    {
        using var app = UseApp();
        using var store = UseStore();
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE, so the stored bytes are UTF-8, not ASCII or UTF-16.
        const string key = "\t sk-caf\U000000E9-7 \r\n";

        Assert.True(APIKeychain.SetKey(key, ProviderKind.Deepseek));

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
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:22, :50-52 (B3: update, not delete-then-add)
    public void SetKey_replaces_an_existing_key_by_updating_in_place_with_no_delete()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, "sk-old-8");
        backend.Operations.Clear();

        Assert.True(APIKeychain.SetKey("sk-new-8", ProviderKind.Meta));

        var target = Target(MetaAccount);
        // B3 (PR-PROVAPI-003): SecItemUpdate first, no SecItemDelete; B2 deleted then re-added.
        Assert.DoesNotContain(backend.Operations, op => op == "delete " + target);
        Assert.Single(backend.Operations, op => op == "write " + target);
        Assert.Equal("sk-new-8", APIKeychain.Key(ProviderKind.Meta, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:50-52 (B3: a refused update keeps the old key)
    public void SetKey_returns_false_when_an_update_is_refused_and_the_old_key_is_kept()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, "sk-old-9");
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(APIKeychain.SetKey("sk-new-9", ProviderKind.Zai));

        backend.BeforeWrite = null;
        // B3 (PR-PROVAPI-003): unlike B2's delete-then-add, a refused update never removed the old item.
        Assert.Equal([Target(ZaiAccount)], backend.TargetNames);
        Assert.Equal("sk-old-9", APIKeychain.Key(ProviderKind.Zai, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-003")] // Services/Providers/APIKeychain.swift:52-57
    public void SetKey_returns_false_when_the_add_of_a_new_item_is_refused()
    {
        using var app = UseApp();
        using var store = UseStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(APIKeychain.SetKey("sk-new-10", ProviderKind.Commandcode));

        Assert.Empty(backend.TargetNames);
    }

    // ---- PR-PROVAPI-004: read trims and rejects a blank value ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:60-74
    public void A_stored_value_is_returned_trimmed_of_whitespace_and_newlines()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(DeepSeekAccount, " \t sk-stored-11 \r\n\n");

        Assert.Equal("sk-stored-11", APIKeychain.Key(ProviderKind.Deepseek, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:69-72, :13-14
    public void A_whitespace_only_stored_value_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, " \n\t\r ");

        Assert.Equal("sk-fallback", APIKeychain.Key(ProviderKind.Meta, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:69-71, :13-14
    public void A_stored_value_that_is_not_UTF8_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(ZaiAccount, new byte[] { 0x73, 0x6B, 0xFF, 0xFE, 0x41 });

        Assert.Equal("sk-fallback", APIKeychain.Key(ProviderKind.Zai, fallback: "sk-fallback"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-004")] // Services/Providers/APIKeychain.swift:69, :13-14
    public void A_missing_item_reads_as_absent_so_the_fallback_is_returned()
    {
        using var app = UseApp();
        using var store = UseStore();
        Plant(MetaAccount, "sk-other-12");

        Assert.Equal("sk-fallback", APIKeychain.Key(ProviderKind.Commandcode, fallback: "sk-fallback"));
    }

    // ---- PR-PROVREG-088: four accounts under one service -----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:25-34
    public void The_four_provider_keys_are_four_credentials_under_one_service_told_apart_only_by_account()
    {
        using var app = UseApp();
        using var store = UseStore();
        (ProviderKind Provider, string Account)[] providers =
        [
            (ProviderKind.Deepseek, DeepSeekAccount), (ProviderKind.Meta, MetaAccount),
            (ProviderKind.Zai, ZaiAccount), (ProviderKind.Commandcode, CommandCodeAccount),
        ];

        for (var i = 0; i < providers.Length; i++)
            Assert.True(APIKeychain.SetKey("sk-provider-" + i, providers[i].Provider));

        Assert.Equal(providers.Select(p => Target(p.Account)).Order(StringComparer.Ordinal), backend.TargetNames);
        for (var i = 0; i < providers.Length; i++)
        {
            var (provider, account) = providers[i];
            var held = backend.Peek(Target(account));
            Assert.NotNull(held);
            Assert.StartsWith(Service + ":" + account + ":", held.TargetName, StringComparison.Ordinal);
            Assert.Equal(account, held.UserName);
            Assert.Equal(Encoding.UTF8.GetBytes("sk-provider-" + i), held.Blob);
            Assert.Equal("sk-provider-" + i, APIKeychain.Key(provider, fallback: ""));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:31, :47, :63
    public void The_service_is_the_running_app_name()
    {
        using var app = UseApp("Marrow Test Name");
        using var store = UseStore();

        Assert.True(APIKeychain.SetKey("sk-service-13", ProviderKind.Deepseek));

        var target = SecretStore.TargetName("Marrow Test Name", DeepSeekAccount);
        Assert.Equal([target], backend.TargetNames);
        Assert.Equal("sk-service-13", APIKeychain.Key(ProviderKind.Deepseek, fallback: ""));
        Assert.True(APIKeychain.SetKey("", ProviderKind.Deepseek));
        Assert.Empty(backend.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-088")] // Services/Providers/APIKeychain.swift:25-49
    public void Writing_a_provider_key_leaves_an_MCP_secret_in_the_same_service_alone()
    {
        using var app = UseApp();
        using var store = UseStore();
        // MCP secrets share the service under accounts 'MCP <server> <field>' (PR-PROVAPI-003 persistence).
        Plant("MCP github token", "mcp-value-14");

        Assert.True(APIKeychain.SetKey("sk-first-14", ProviderKind.Commandcode));
        Assert.True(APIKeychain.SetKey("sk-second-14", ProviderKind.Commandcode));

        Assert.Equal("sk-second-14", APIKeychain.Key(ProviderKind.Commandcode, fallback: ""));
        Assert.Equal(Encoding.UTF8.GetBytes("mcp-value-14"), backend.Peek(Target("MCP github token"))!.Blob);
    }
}
