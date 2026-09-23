using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPKeychain.cs, the port of DroppyCode/Services/MCP/MCPKeychain.swift
// (B2 1fbf81e), :1-68. Every expectation is transcribed from that file and the records PR-MCP-095 to PR-MCP-099 and
// PR-MCP-228, never from the C# implementation.
//
// The Swift helper talks to the Keychain through SecItem calls with kSecClassGenericPassword, kSecAttrService
// AppInfo.name and kSecAttrAccount "MCP " + server + " " + field (:7-9, :13-19, :37-41, :53-57). On Windows those
// calls are the secret store (R-SEC-1, OD-0008 B3, ED-093): SecretStore.Shared.Current, reached here through an
// in-memory Credential Manager backend, with the service AppInfo.Current.Name (Core/Support/Text.swift:3-9). So an
// item written by MCPKeychain is observed through the store itself under the literal account text these tests
// spell out, and the store's own tests (Port/SecretStoreTests.cs) prove the Credential Manager encoding.
//
// Not tested, by design: the capture-run short circuit of :12 (guard !CaptureRun.isEnabled else { return nil }).
// CaptureRun is not ported (ED-001) and dead_code.md entry 44 records that this guard is capture-mode behaviour,
// user-unreachable and not to be ported as runtime behaviour; PR-MCP-096's windows_path says the same.
//
// DEVIATION OD-0008 A12 (BLK-0012), carried by PR-MCP-099 and PR-MCP-228: upstream discards every SecItemDelete
// status (:58, :61-67). The port checks it, so Delete and DeleteAll return a Boolean: true when every deletion
// removed the item or found none (an item that did not exist is not a failure, ENGINEERING DECISION in the
// records' deviation), false when the store refused one. DeleteAll still walks every declared field after a
// refusal: the Swift loop has no early exit (:64-66) and the ruling changes only what is reported. The report
// text "Disconnected, but Credential Manager did not remove this server's stored values." (OD-0012 E6) belongs to
// the disconnect in MCPStore (PR-MCP-061), not to this file, so it is not asserted here.
//
// Every secret value below is made up.
public sealed class MCPKeychainTests
{
    private readonly InMemoryCredentialBackend memory = new();
    private readonly RefusingCredentialBackend backend;
    private readonly SecretStore store;

    public MCPKeychainTests()
    {
        backend = new RefusingCredentialBackend(memory);
        store = new SecretStore(backend);
    }

    // kSecAttrService as String: AppInfo.name (:15, :39, :55).
    private static string Service => AppInfo.Current.Name;

    // Each test binds the shared store for its own async flow (Ambient<T>.Override), never the real one.
    private IDisposable UseStore() => SecretStore.Shared.Override(store);

    private string Target(string account) => SecretStore.TargetName(Service, account);

    private byte[]? Stored(string account) =>
        store.CopyMatching(Service, account, out var data) == SecretStoreStatus.Success ? data : null;

    private void Seed(string account, string value) =>
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, account, Encoding.UTF8.GetBytes(value)));

    private List<string> DeletesOf(params string[] accounts)
    {
        var targets = accounts.Select(Target).ToList();
        return memory.Operations
            .Where(operation => operation.StartsWith("delete ", StringComparison.Ordinal))
            .Select(operation => operation["delete ".Length..])
            .Where(target => targets.Contains(target, StringComparer.Ordinal))
            .ToList();
    }

    // A trimmed shape of the GitHub catalog entry, Core/Models/MCPCatalog.swift:5-21 (one secret field "token").
    private static MCPCatalogEntry GitHub(params MCPField[] fields) =>
        new("github", "GitHub", "GitHub", "Issues, pull requests and code across your repositories",
            MCPCategory.Developer, "mcp-github", 0x24292F,
            new MCPTransport.Http("https://api.githubcopilot.com/mcp/",
                new Dictionary<string, string> { ["Authorization"] = "Bearer {token}" }),
            "https://github.com/github/github-mcp-server")
        { IsMonochrome = true, Fields = fields };

    // A custom server's entry as MCPCustomServer.catalogEntry() builds it (Core/Models/MCPCustom.swift:93-117):
    // every variable becomes a field with label = key and placeholder "", kind .secret or .text; vendor "Custom",
    // category .developer, asset "", colorValue 0, monochrome, docsURL "", env holding "{key}" placeholders. Its id
    // has the "custom-" prefix of makeID (:62-76), so no catalog entry carries it.
    private static MCPCatalogEntry CustomServer() =>
        new("custom-my-notes", "My Notes", "Custom", "Runs on this Mac: notes-server --stdio.",
            MCPCategory.Developer, "", 0, new MCPTransport.Stdio("notes-server", ["--stdio"]), "")
        {
            IsMonochrome = true,
            Fields =
            [
                new("API_TOKEN", "API_TOKEN", "", MCPField.Kind.Secret),
                new("BASE_URL", "BASE_URL", "", MCPField.Kind.Text),
                new("REGION", "REGION", "", MCPField.Kind.Secret),
            ],
            Env = new Dictionary<string, string>
            {
                ["API_TOKEN"] = "{API_TOKEN}", ["BASE_URL"] = "{BASE_URL}", ["REGION"] = "{REGION}",
            },
        };

    // ---- PR-MCP-095: the account string and the one service ----

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    [Trait("Rule", "R-SEC-1")]
    public void Set_stores_one_item_under_the_app_name_service_and_the_account_MCP_space_server_space_field()
    {
        // :8 "MCP " + server + " " + field; :15/:39 kSecAttrService AppInfo.name; :40 the account.
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("ghp-made-up-value", server: "github", field: "token"));

        Assert.Equal([Target("MCP github token")], memory.TargetNames);
        var held = memory.Peek(Target("MCP github token"))!;
        // ED-093: UserName is the account verbatim. The bytes: "MCP", one space, the id, one space, the key.
        Assert.Equal("MCP github token", held.UserName);
        Assert.Equal(new byte[] { 0x4D, 0x43, 0x50, 0x20, 0x67, 0x69, 0x74, 0x68, 0x75, 0x62, 0x20, 0x74, 0x6F, 0x6B, 0x65, 0x6E },
            Encoding.UTF8.GetBytes(held.UserName));
        Assert.Equal("ghp-made-up-value", store.Read(Service, "MCP github token"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    public void Value_reads_the_item_stored_under_the_account_MCP_space_server_space_field()
    {
        // :13-19: the read query is the same service and account.
        using var scope = UseStore();
        Seed("MCP custom-my-notes API_TOKEN", "made-up-token");

        Assert.Equal("made-up-token", MCPKeychain.Value(server: "custom-my-notes", field: "API_TOKEN"));
        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "REGION"));
        Assert.Null(MCPKeychain.Value(server: "custom-my-notes2", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    public void Delete_removes_the_item_stored_under_the_account_MCP_space_server_space_field()
    {
        // :52-59: the delete query is the same service and account.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "made-up-token");
        Seed("MCP notes REGION", "eu");

        MCPKeychain.Delete(server: "notes", field: "API_TOKEN");

        Assert.Null(store.Read(Service, "MCP notes API_TOKEN"));
        Assert.Equal("eu", store.Read(Service, "MCP notes REGION"));
        Assert.Equal([Target("MCP notes REGION")], memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    public void The_parts_are_joined_without_escaping_so_a_space_in_the_server_id_makes_the_account_ambiguous()
    {
        // :8 plain concatenation: ("my server", "key") and ("my", "server key") are both "MCP my server key".
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("first", server: "my server", field: "key"));

        Assert.Equal("first", MCPKeychain.Value(server: "my", field: "server key"));
        Assert.Equal("first", store.Read(Service, "MCP my server key"));
        Assert.True(MCPKeychain.Set("second", server: "my", field: "server key"));
        Assert.Equal("second", MCPKeychain.Value(server: "my server", field: "key"));
        Assert.Single(memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    public void The_OAuth_field_names_oauth_and_oauth_client_address_their_own_accounts()
    {
        // MCPOAuth.swift:712 and :397 write field "oauth" and "oauth-client" through this helper; :8 gives the
        // accounts "MCP <id> oauth" and "MCP <id> oauth-client".
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("{\"access_token\":\"made-up\"}", server: "gitlab", field: "oauth"));
        Assert.True(MCPKeychain.Set("{\"client_id\":\"made-up\"}", server: "gitlab", field: "oauth-client"));

        Assert.Equal("{\"access_token\":\"made-up\"}", store.Read(Service, "MCP gitlab oauth"));
        Assert.Equal("{\"client_id\":\"made-up\"}", store.Read(Service, "MCP gitlab oauth-client"));
        Assert.Equal(2, memory.TargetNames.Count);
    }

    [Fact]
    [Trait("Req", "PR-MCP-095")]
    [Trait("Decision", "ED-093")]
    public void Field_keys_that_differ_only_in_case_are_separate_items()
    {
        // Keychain account matching is exact (ED-093); custom variable keys are user-written (MCPStore.swift:217).
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("lower", server: "notes", field: "token"));
        Assert.True(MCPKeychain.Set("upper", server: "notes", field: "Token"));

        Assert.Equal("lower", MCPKeychain.Value(server: "notes", field: "token"));
        Assert.Equal("upper", MCPKeychain.Value(server: "notes", field: "Token"));
        Assert.Equal(2, memory.TargetNames.Count);
    }

    // ---- PR-MCP-096: reading trims and treats blank as absent ----

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_returns_the_stored_value_trimmed_of_whitespace_and_newlines()
    {
        // :25 return value.trimmed; Text.swift:132 trims .whitespacesAndNewlines. Interior spaces stay.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", " \t made up token \r\n");

        Assert.Equal("made up token", MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_of_a_whitespace_only_stored_value_is_null()
    {
        // :24 !value.trimmed.isEmpty else { return nil }.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "  \n\t \r\n");

        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_of_an_empty_stored_value_is_null()
    {
        // :24 as above, with nothing to trim.
        using var scope = UseStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes API_TOKEN", []));

        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_of_an_account_that_holds_nothing_is_null()
    {
        // :21 SecItemCopyMatching(...) == errSecSuccess fails, so nil.
        using var scope = UseStore();

        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_of_bytes_that_are_not_valid_UTF8_is_null()
    {
        // :23 String(data: data, encoding: .utf8) fails for 0xFF, so nil.
        using var scope = UseStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes API_TOKEN", [0x61, 0xFF, 0x62]));

        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_decodes_the_stored_bytes_as_UTF8()
    {
        // :23 String(data:encoding: .utf8): C3 A9 is U+00E9.
        using var scope = UseStore();
        Assert.Equal(SecretStoreStatus.Success,
            store.Add(Service, "MCP notes API_TOKEN", [0x63, 0x61, 0x66, 0xC3, 0xA9, 0x2D, 0x6B, 0x65, 0x79]));

        Assert.Equal("caf\U000000E9-key", MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-096")]
    public void Value_from_a_store_that_refuses_the_read_is_null_not_an_error()
    {
        // :21 any status but errSecSuccess is nil: a locked or unavailable store surfaces as a missing value.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "made-up-token");
        backend.RefuseReads = true;

        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    // ---- PR-MCP-097: an empty or blank write deletes and reports success ----

    [Fact]
    [Trait("Req", "PR-MCP-097")]
    public void Set_of_an_empty_value_deletes_the_item_and_returns_true()
    {
        // :31-35: trimmed is empty, so delete(server:field:) and return true.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "made-up-token");

        Assert.True(MCPKeychain.Set("", server: "notes", field: "API_TOKEN"));

        Assert.Empty(memory.TargetNames);
        Assert.Null(store.Read(Service, "MCP notes API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-097")]
    public void Set_of_a_whitespace_only_value_deletes_the_item_and_returns_true()
    {
        // :31 value.trimmed of " \t\r\n " is empty.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "made-up-token");

        Assert.True(MCPKeychain.Set(" \t\r\n ", server: "notes", field: "API_TOKEN"));

        Assert.Empty(memory.TargetNames);
        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-097")]
    public void Set_of_an_empty_value_with_nothing_stored_returns_true_and_writes_nothing()
    {
        // :32-34: deleting a missing item is a no-op and the call still returns true; no item is created.
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("   ", server: "notes", field: "API_TOKEN"));

        Assert.Empty(memory.TargetNames);
        Assert.DoesNotContain(memory.Operations, operation => operation.StartsWith("write ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-MCP-097")]
    public void Set_of_an_empty_value_leaves_the_other_fields_of_the_server()
    {
        // :33 deletes only (server, field).
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "made-up-token");
        Seed("MCP notes REGION", "eu");

        Assert.True(MCPKeychain.Set("", server: "notes", field: "API_TOKEN"));

        Assert.Equal("eu", MCPKeychain.Value(server: "notes", field: "REGION"));
        Assert.Equal([Target("MCP notes REGION")], memory.TargetNames);
    }

    // ---- PR-MCP-098: update in place first, add only when nothing exists ----

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_stores_the_trimmed_value_as_UTF8_bytes()
    {
        // :31 let trimmed = value.trimmed; :36 let data = Data(trimmed.utf8).
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("\t caf\U000000E9-key \n", server: "notes", field: "API_TOKEN"));

        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x2D, 0x6B, 0x65, 0x79 }, Stored("MCP notes API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_with_nothing_stored_adds_the_item_and_returns_true()
    {
        // :42-44 SecItemUpdate reports errSecItemNotFound; :45-49 SecItemAdd succeeds, so true.
        using var scope = UseStore();

        Assert.True(MCPKeychain.Set("made-up-token", server: "notes", field: "API_TOKEN"));

        Assert.Equal([Target("MCP notes API_TOKEN")], memory.TargetNames);
        Assert.Equal("made-up-token", MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_over_an_existing_item_updates_it_in_place_and_returns_true()
    {
        // :42-43 SecItemUpdate succeeds: true, one item, the new bytes.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "old-made-up-token");

        Assert.True(MCPKeychain.Set("new-made-up-token", server: "notes", field: "API_TOKEN"));

        Assert.Equal([Target("MCP notes API_TOKEN")], memory.TargetNames);
        Assert.Equal("new-made-up-token", MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
        Assert.Equal("new-made-up-token"u8.ToArray(), Stored("MCP notes API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_whose_update_is_refused_returns_false_and_keeps_the_old_value()
    {
        // :44 guard updated == errSecItemNotFound else { return false }: any other failing status is false, and the
        // add is not tried, so the item keeps its old bytes.
        using var scope = UseStore();
        Seed("MCP notes API_TOKEN", "old-made-up-token");
        memory.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(MCPKeychain.Set("new-made-up-token", server: "notes", field: "API_TOKEN"));

        memory.BeforeWrite = null;
        Assert.Equal("old-made-up-token", MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
        Assert.Equal([Target("MCP notes API_TOKEN")], memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_with_nothing_stored_whose_add_is_refused_returns_false_and_stores_nothing()
    {
        // :44 errSecItemNotFound goes on to :49 return SecItemAdd(...) == errSecSuccess, which fails: false.
        using var scope = UseStore();
        memory.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(MCPKeychain.Set("made-up-token", server: "notes", field: "API_TOKEN"));

        memory.BeforeWrite = null;
        Assert.Empty(memory.TargetNames);
        Assert.Null(MCPKeychain.Value(server: "notes", field: "API_TOKEN"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-098")]
    public void Set_keeps_the_other_fields_and_servers_untouched()
    {
        // :37-41 the query names one account; the update carries only the data.
        using var scope = UseStore();
        Seed("MCP notes REGION", "eu");
        Seed("MCP other API_TOKEN", "other-made-up-token");

        Assert.True(MCPKeychain.Set("made-up-token", server: "notes", field: "API_TOKEN"));

        Assert.Equal("eu", MCPKeychain.Value(server: "notes", field: "REGION"));
        Assert.Equal("other-made-up-token", MCPKeychain.Value(server: "other", field: "API_TOKEN"));
        Assert.Equal(3, memory.TargetNames.Count);
    }

    // ---- PR-MCP-099 and PR-MCP-228: deleteAll walks the ENTRY's fields; OD-0008 A12 checks each deletion ----

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_deletes_one_item_per_field_the_entry_declares_in_field_order()
    {
        // :64-66 for field in entry.fields { delete(server: entry.id, field: field.key) }: every field, secret or
        // text, in the entry's own order, under "MCP " + id + " " + key.
        using var scope = UseStore();
        Seed("MCP custom-my-notes API_TOKEN", "made-up-token");
        Seed("MCP custom-my-notes BASE_URL", "https://notes.example");
        Seed("MCP custom-my-notes REGION", "eu");

        Assert.True(MCPKeychain.DeleteAll(server: CustomServer()));

        Assert.Empty(memory.TargetNames);
        Assert.Equal(
            [Target("MCP custom-my-notes API_TOKEN"), Target("MCP custom-my-notes BASE_URL"), Target("MCP custom-my-notes REGION")],
            DeletesOf("MCP custom-my-notes API_TOKEN", "MCP custom-my-notes BASE_URL", "MCP custom-my-notes REGION"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_deletes_a_custom_servers_variables_although_no_catalog_entry_has_its_id()
    {
        // :61-62: the entry, not its id, "a custom server is not in the catalog, so looking it up there would
        // silently delete nothing".
        using var scope = UseStore();
        Seed("MCP custom-my-notes API_TOKEN", "made-up-token");
        Seed("MCP custom-my-notes REGION", "eu");

        Assert.True(MCPKeychain.DeleteAll(server: CustomServer()));

        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "API_TOKEN"));
        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "REGION"));
        Assert.Empty(memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_takes_the_fields_from_the_entry_passed_not_from_the_catalog_entry_with_that_id()
    {
        // :64 entry.fields. The catalog's GitHub entry declares "token" (MCPCatalog.swift:15-17); an entry with the
        // same id that declares only "other" deletes "other", and "token", which it no longer declares, stays behind
        // as an orphan (upstream_defects.md 111).
        using var scope = UseStore();
        Seed("MCP github token", "ghp-made-up-value");
        Seed("MCP github other", "made-up-other");

        Assert.True(MCPKeychain.DeleteAll(server: GitHub(new MCPField("other", "Other", ""))));

        Assert.Null(MCPKeychain.Value(server: "github", field: "other"));
        Assert.Equal("ghp-made-up-value", MCPKeychain.Value(server: "github", field: "token"));
        Assert.Equal([Target("MCP github token")], memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_leaves_the_two_OAuth_items_which_are_not_entry_fields()
    {
        // "oauth" and "oauth-client" are deleted separately by MCPOAuth.signOut (MCPOAuth.swift:189-191).
        using var scope = UseStore();
        Seed("MCP github token", "ghp-made-up-value");
        Seed("MCP github oauth", "{\"access_token\":\"made-up\"}");
        Seed("MCP github oauth-client", "{\"client_id\":\"made-up\"}");

        Assert.True(MCPKeychain.DeleteAll(server: GitHub(new MCPField("token", "Personal access token", "", MCPField.Kind.Secret))));

        Assert.Null(MCPKeychain.Value(server: "github", field: "token"));
        Assert.Equal([Target("MCP github oauth"), Target("MCP github oauth-client")],
            memory.TargetNames.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_of_an_entry_with_no_fields_deletes_nothing_and_is_not_an_error()
    {
        // :64 iterates an empty list.
        using var scope = UseStore();
        Seed("MCP github token", "ghp-made-up-value");

        Assert.True(MCPKeychain.DeleteAll(server: GitHub()));

        Assert.DoesNotContain(memory.Operations, operation => operation.StartsWith("delete ", StringComparison.Ordinal));
        Assert.Equal("ghp-made-up-value", MCPKeychain.Value(server: "github", field: "token"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_of_fields_that_hold_nothing_still_attempts_each_and_is_not_a_failure()
    {
        // :64-66 walks every declared field whether set or not; the deviation: an item that did not exist is not a
        // failure (upstream deletes items that were never written).
        using var scope = UseStore();

        Assert.True(MCPKeychain.DeleteAll(server: CustomServer()));

        Assert.Equal(
            [Target("MCP custom-my-notes API_TOKEN"), Target("MCP custom-my-notes BASE_URL"), Target("MCP custom-my-notes REGION")],
            DeletesOf("MCP custom-my-notes API_TOKEN", "MCP custom-my-notes BASE_URL", "MCP custom-my-notes REGION"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_reports_false_when_the_store_refuses_a_deletion_and_still_deletes_the_other_fields()
    {
        // DEVIATION OD-0008 A12: a refused deletion is reported instead of a silent success. The Swift loop
        // (:64-66) has no early exit, so the fields after the refused one are still deleted.
        using var scope = UseStore();
        Seed("MCP custom-my-notes API_TOKEN", "made-up-token");
        Seed("MCP custom-my-notes BASE_URL", "https://notes.example");
        Seed("MCP custom-my-notes REGION", "eu");
        backend.RefusedDeleteTargets.Add(Target("MCP custom-my-notes API_TOKEN"));

        Assert.False(MCPKeychain.DeleteAll(server: CustomServer()));

        Assert.Equal([Target("MCP custom-my-notes API_TOKEN")], backend.RefusedDeletes.Distinct(StringComparer.Ordinal).ToList());
        Assert.Equal("made-up-token", MCPKeychain.Value(server: "custom-my-notes", field: "API_TOKEN"));
        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "BASE_URL"));
        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "REGION"));
        Assert.Equal([Target("MCP custom-my-notes API_TOKEN")], memory.TargetNames);
    }

    [Fact]
    [Trait("Req", "PR-MCP-228")]
    public void DeleteAll_reports_false_when_the_refused_deletion_is_the_last_field()
    {
        // DEVIATION OD-0008 A12: one refusal anywhere in the walk makes the whole call a failure.
        using var scope = UseStore();
        Seed("MCP custom-my-notes API_TOKEN", "made-up-token");
        Seed("MCP custom-my-notes REGION", "eu");
        backend.RefusedDeleteTargets.Add(Target("MCP custom-my-notes REGION"));

        Assert.False(MCPKeychain.DeleteAll(server: CustomServer()));

        Assert.Null(MCPKeychain.Value(server: "custom-my-notes", field: "API_TOKEN"));
        Assert.Equal("eu", MCPKeychain.Value(server: "custom-my-notes", field: "REGION"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-099")]
    [Trait("Req", "PR-MCP-228")]
    public void Delete_reports_true_for_a_removed_item_true_for_a_missing_one_and_false_for_a_refusal()
    {
        // :52-59 deletes one account. DEVIATION OD-0008 A12: its status is checked, so the OAuth deletions that
        // signOut makes during disconnect (MCPOAuth.swift:190-191) can be reported too; not-found is not a failure.
        using var scope = UseStore();
        Seed("MCP github oauth", "{\"access_token\":\"made-up\"}");
        Seed("MCP github oauth-client", "{\"client_id\":\"made-up\"}");
        backend.RefusedDeleteTargets.Add(Target("MCP github oauth-client"));

        Assert.True(MCPKeychain.Delete(server: "github", field: "oauth"));
        Assert.True(MCPKeychain.Delete(server: "github", field: "oauth"));
        Assert.False(MCPKeychain.Delete(server: "github", field: "oauth-client"));

        Assert.Null(MCPKeychain.Value(server: "github", field: "oauth"));
        Assert.Equal("{\"client_id\":\"made-up\"}", MCPKeychain.Value(server: "github", field: "oauth-client"));
    }
}
