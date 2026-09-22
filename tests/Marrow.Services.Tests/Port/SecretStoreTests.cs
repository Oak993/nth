using System.Text;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port;

// PORT-ONLY: tests for the secret store, src/Marrow.Services/Port/SecretStore.cs (IMPLEMENTING.md 4.3, R-SEC-1;
// ADR-0002 deviation 8; OD-0008 B3 "Credential Manager" and A6 "fail closed, store nothing"; ED-093).
//
// The store stands in for the eight SecItem calls of the two Keychain helpers (B2 1fbf81e):
//   DroppyCode/Services/Providers/APIKeychain.swift:13-65: setKey trims, deletes for an empty value and returns
//     true, otherwise deletes then adds and returns SecItemAdd == errSecSuccess; read trims and treats a blank or
//     non-UTF-8 value as absent.
//   DroppyCode/Services/MCP/MCPKeychain.swift:11-59: the same read; set trims, deletes for an empty value and
//     returns true, otherwise updates, returns true on success, false on any status but errSecItemNotFound,
//     and adds on errSecItemNotFound.
//   DroppyCode/Core/Support/Text.swift:132: trimmed is trimmingCharacters(in: .whitespacesAndNewlines).
// Both helpers query kSecClassGenericPassword with kSecAttrService AppInfo.name and one kSecAttrAccount per item
// (APIKeychain.swift:29-33, MCPKeychain.swift:13-16); the service is passed in here, so this unit needs no AppInfo.
// The capture-run guards (APIKeychain.swift:14, MCPKeychain.swift:12) belong to the callers and are always false
// on Windows (dead_code.md entry 44, ED-001); they are not part of the store.
//
// ED-093 fixes the Credential Manager encoding these tests check through the in-memory backend
// (Fixtures/InMemoryCredentialBackend.cs), which compares targets without case and refuses a blob over 2560 bytes
// as Credential Manager does. Win32 numbers from wincred.h: CRED_TYPE_GENERIC 1, CRED_PERSIST_LOCAL_MACHINE 2.
// No real secret appears here; every value is made up. Only the last test touches the real Credential Manager,
// and only when MARROW_PORT_CREDMAN_LIVE=1 (ED-093, R-TEST-9).
public sealed class SecretStoreTests
{
    private const string Service = "Marrow";
    private const int Chunk = 2560;

    private readonly InMemoryCredentialBackend backend = new();

    private SecretStore NewStore() => new(backend);

    private static byte[] Bytes(int count, byte seed = 0) =>
        Enumerable.Range(0, count).Select(i => (byte)(0x41 + ((i + seed) % 26))).ToArray();

    private static byte[]? Stored(SecretStore store, string service, string account) =>
        store.CopyMatching(service, account, out var data) == SecretStoreStatus.Success ? data : null;

    // APIKeychain.setKey (delete, then add) when deleteFirst; MCPKeychain.set (update, then add) otherwise.
    private static bool Set(SecretStore store, bool deleteFirst, string account, string value) =>
        deleteFirst ? store.SetReplacing(Service, account, value) : store.SetUpdating(Service, account, value);

    private static void AssertRefusedStatus(SecretStoreStatus status)
    {
        // A refusal is any status other than these three; MCPKeychain.swift:44 treats every such status alike.
        Assert.NotEqual(SecretStoreStatus.Success, status);
        Assert.NotEqual(SecretStoreStatus.ItemNotFound, status);
        Assert.NotEqual(SecretStoreStatus.DuplicateItem, status);
    }

    // ---- ED-093: the credential mapping ----

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_target_name_is_service_colon_account_colon_16_lowercase_hex_digits_of_SHA256_of_the_account()
    {
        // SHA-256 of the UTF-8 bytes of "DeepSeek API Key" (DeepSeekKeychain.swift:6), computed outside .NET with
        // sha256sum: e4d41a2f217aad94642496c93fcc5573b1c9243964872818d802b543ea226a1b.
        Assert.Equal("Marrow:DeepSeek API Key:e4d41a2f217aad94", SecretStore.TargetName("Marrow", "DeepSeek API Key"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_account_is_hashed_as_UTF8_bytes()
    {
        // "MCP caf" U+00E9 " key": sha256sum of the UTF-8 bytes 4D 43 50 20 63 61 66 C3 A9 20 6B 65 79 is
        // 8f0b22968bbe0e599fd35e03804a260a660b618fbd19615c52737176114cd121. UTF-16 bytes would give another digest.
        var account = "MCP caf\U000000E9 key"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        Assert.Equal("Marrow:" + account + ":8f0b22968bbe0e59", SecretStore.TargetName("Marrow", account));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    public void An_item_is_one_generic_local_machine_credential_whose_user_name_is_the_account_verbatim()
    {
        // APIKeychain.swift:28-39: the item holds Data(trimmed.utf8). ED-093: CRED_TYPE_GENERIC,
        // CRED_PERSIST_LOCAL_MACHINE, UserName the account verbatim, TargetName as above.
        var store = NewStore();

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "DeepSeek API Key", "sk-test-1"u8.ToArray()));

        Assert.Equal(["Marrow:DeepSeek API Key:e4d41a2f217aad94"], backend.TargetNames);
        var credential = backend.Peek("Marrow:DeepSeek API Key:e4d41a2f217aad94");
        Assert.NotNull(credential);
        Assert.Equal("Marrow:DeepSeek API Key:e4d41a2f217aad94", credential.TargetName);
        Assert.Equal("DeepSeek API Key", credential.UserName);
        Assert.Equal("sk-test-1"u8.ToArray(), credential.Blob);
        Assert.Equal(1, credential.Type);
        Assert.Equal(2, credential.Persist);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_backend_double_compares_targets_without_case_as_Credential_Manager_does()
    {
        // Negative control for the case tests below: without the hash, two accounts that differ only in case would
        // land on one credential, the second write replacing the first.
        Assert.Equal(0, backend.Write(new GenericCredential("Marrow:MCP notes token", "MCP notes token", [1], 1, 2, [])));
        Assert.Equal(0, backend.Write(new GenericCredential("Marrow:MCP notes Token", "MCP notes Token", [2], 1, 2, [])));

        Assert.Equal(["Marrow:MCP notes Token"], backend.TargetNames);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    public void Accounts_that_differ_only_in_case_are_separate_items()
    {
        // Keychain account matching is exact (ED-093); MCP accounts carry user-written field keys
        // (MCPKeychain.swift:8, MCPStore.swift:107, :217). Digests by sha256sum: "MCP notes token" abc258d4d02c2ef6...,
        // "MCP notes Token" a762c84d50f4462f...
        var store = NewStore();

        Assert.True(store.SetUpdating(Service, "MCP notes token", "lower"));
        Assert.True(store.SetUpdating(Service, "MCP notes Token", "upper"));

        Assert.Equal(["Marrow:MCP notes Token:a762c84d50f4462f", "Marrow:MCP notes token:abc258d4d02c2ef6"], backend.TargetNames);
        Assert.Equal("lower", store.Read(Service, "MCP notes token"));
        Assert.Equal("upper", store.Read(Service, "MCP notes Token"));

        Assert.Equal(SecretStoreStatus.Success, store.Delete(Service, "MCP notes Token"));

        Assert.Equal("lower", store.Read(Service, "MCP notes token"));
        Assert.Null(store.Read(Service, "MCP notes Token"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    public void Service_and_account_stay_two_values_even_when_the_colon_joined_text_is_the_same()
    {
        // kSecAttrService and kSecAttrAccount are separate attributes (APIKeychain.swift:31-32): ("S", "a:b") and
        // ("S:a", "b") are two items although both join to "S:a:b". The account digest tells them apart.
        var store = NewStore();

        Assert.True(store.SetReplacing("S", "a:b", "first"));
        Assert.Null(store.Read("S:a", "b"));
        Assert.True(store.SetReplacing("S:a", "b", "second"));

        Assert.Equal("first", store.Read("S", "a:b"));
        Assert.Equal("second", store.Read("S:a", "b"));
        Assert.Equal(2, backend.TargetNames.Count);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    public void The_four_provider_accounts_and_an_MCP_account_are_kept_apart_under_one_service()
    {
        // DeepSeekKeychain.swift:6, MetaKeychain.swift:6, ZaiKeychain.swift:6, CommandCodeKeychain.swift:8, and
        // MCPKeychain.swift:8 ("MCP " + server + " " + field): one service, items told apart by account.
        var store = NewStore();
        string[] accounts = ["DeepSeek API Key", "Meta API Key", "Z.ai API Key", "Command Code API Key", "MCP notes API_TOKEN"];
        for (var index = 0; index < accounts.Length; index++)
            Assert.True(store.SetReplacing(Service, accounts[index], "value " + index));

        for (var index = 0; index < accounts.Length; index++)
            Assert.Equal("value " + index, store.Read(Service, accounts[index]));
        Assert.Equal(5, backend.TargetNames.Count);
    }

    // ---- ED-093: chunking at 2560 bytes ----

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_backend_double_refuses_a_2561_byte_blob_with_1783_and_takes_2560()
    {
        // Negative control for the chunk tests: ED-093's probe of CredWriteW, 2560 accepted, 2561 refused with 1783.
        Assert.Equal(0, backend.Write(new GenericCredential("t2560", "u", Bytes(2560), 1, 2, [])));
        Assert.Equal(1783, backend.Write(new GenericCredential("t2561", "u", Bytes(2561), 1, 2, [])));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_2560_byte_value_is_one_credential()
    {
        var store = NewStore();
        var value = Bytes(Chunk);

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", value));

        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.Equal([target], backend.TargetNames);
        Assert.Equal(value, backend.Peek(target)!.Blob);
        Assert.Equal(value, Stored(store, Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    public void A_2561_byte_value_round_trips_in_two_chunks_written_continuation_first()
    {
        // The Keychain has no small size limit and field "oauth" holds whole OAuth token JSON (MCPOAuth.swift:712).
        // ED-093: 2560-byte chunks; the continuation <target>#2 is written first and the primary last, the primary
        // holding the first chunk and the chunk count in a credential attribute.
        var store = NewStore();
        var value = Bytes(Chunk + 1);
        var target = SecretStore.TargetName(Service, "MCP notes oauth");

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", value));

        Assert.Equal([target, target + "#2"], backend.TargetNames);
        var primary = backend.Peek(target)!;
        Assert.Equal(value[..Chunk], primary.Blob);
        Assert.Equal("MCP notes oauth", primary.UserName);
        Assert.NotEmpty(primary.Attributes);
        Assert.Equal(value[Chunk..], backend.Peek(target + "#2")!.Blob);
        var writes = backend.Operations.Where(o => o.StartsWith("write ", StringComparison.Ordinal)).ToList();
        Assert.Equal(["write " + target + "#2", "write " + target], writes);

        Assert.Equal(value, Stored(store, Service, "MCP notes oauth"));
        Assert.Equal(Encoding.ASCII.GetString(value), store.Read(Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_four_chunk_value_writes_every_continuation_before_the_primary_and_each_is_a_generic_local_machine_credential()
    {
        var store = NewStore();
        var value = Bytes(3 * Chunk + 1, seed: 7);
        var target = SecretStore.TargetName(Service, "MCP notes oauth");

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", value));

        Assert.Equal([target, target + "#2", target + "#3", target + "#4"], backend.TargetNames);
        var writes = backend.Operations.Where(o => o.StartsWith("write ", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, writes.Count);
        Assert.Equal("write " + target, writes[^1]);
        Assert.Equal(
            ["write " + target + "#2", "write " + target + "#3", "write " + target + "#4"],
            writes.Take(3).Order(StringComparer.Ordinal));
        Assert.Equal(value[..Chunk], backend.Peek(target)!.Blob);
        Assert.Equal(value[Chunk..(2 * Chunk)], backend.Peek(target + "#2")!.Blob);
        Assert.Equal(value[(2 * Chunk)..(3 * Chunk)], backend.Peek(target + "#3")!.Blob);
        Assert.Equal(value[(3 * Chunk)..], backend.Peek(target + "#4")!.Blob);
        foreach (var name in backend.TargetNames)
        {
            Assert.Equal(1, backend.Peek(name)!.Type);
            Assert.Equal(2, backend.Peek(name)!.Persist);
        }

        Assert.Equal(value, Stored(store, Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-STR-8")]
    public void A_character_split_across_the_chunk_boundary_reads_back_whole()
    {
        // 2559 ASCII bytes, then U+00E9 (C3 A9) across bytes 2559 and 2560: chunks are cut on bytes and joined
        // before the UTF-8 decode of APIKeychain.swift:53 / MCPKeychain.swift:23.
        var store = NewStore();
        var value = new string('a', Chunk - 1) + "\U000000E9"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE

        Assert.True(store.SetUpdating(Service, "MCP notes oauth", value));

        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.Equal(0xC3, backend.Peek(target)!.Blob[^1]);
        Assert.Equal(new byte[] { 0xA9 }, backend.Peek(target + "#2")!.Blob);
        Assert.Equal(value, store.Read(Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_reader_during_a_chunked_add_sees_no_item_until_the_primary_is_written()
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        var seen = new List<SecretStoreStatus>();
        backend.BeforeWrite = credential =>
        {
            seen.Add(store.CopyMatching(Service, "MCP notes oauth", out _));
            return 0;
        };

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", Bytes(2 * Chunk + 5)));

        Assert.Equal([SecretStoreStatus.ItemNotFound, SecretStoreStatus.ItemNotFound, SecretStoreStatus.ItemNotFound], seen);
        Assert.Equal(target, backend.Operations.Last(o => o.StartsWith("write ", StringComparison.Ordinal))["write ".Length..]);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_reader_while_a_short_value_grows_into_chunks_sees_the_old_value_until_the_primary_is_written()
    {
        // MCPKeychain.swift:42 updates in place. ED-093: continuations first, primary last, so until the primary
        // (with its chunk count) is replaced, the old one-chunk value is what a reader gets.
        var store = NewStore();
        Assert.True(store.SetUpdating(Service, "MCP notes oauth", "old"));
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        var seen = new List<string?>();
        backend.BeforeWrite = credential =>
        {
            seen.Add(store.Read(Service, "MCP notes oauth"));
            return 0;
        };
        var grown = new string('n', Chunk + 10);

        Assert.True(store.SetUpdating(Service, "MCP notes oauth", grown));

        Assert.Equal(["old", "old"], seen);
        Assert.Equal(grown, store.Read(Service, "MCP notes oauth"));
        Assert.Equal([target, target + "#2"], backend.TargetNames);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_missing_chunk_reads_as_item_not_found()
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", Bytes(2 * Chunk + 1)));

        Assert.True(backend.Remove(target + "#2"));

        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "MCP notes oauth", out var data));
        Assert.Null(data);
        Assert.Null(store.Read(Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_shorter_update_removes_the_chunks_it_no_longer_needs()
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", Bytes(3 * Chunk + 1)));

        var twoChunks = Bytes(Chunk + 3, seed: 3);
        Assert.Equal(SecretStoreStatus.Success, store.Update(Service, "MCP notes oauth", twoChunks));
        Assert.Equal([target, target + "#2"], backend.TargetNames);
        Assert.Equal(twoChunks, Stored(store, Service, "MCP notes oauth"));

        var oneChunk = "short"u8.ToArray();
        Assert.Equal(SecretStoreStatus.Success, store.Update(Service, "MCP notes oauth", oneChunk));
        Assert.Equal([target], backend.TargetNames);
        Assert.Equal(oneChunk, Stored(store, Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Delete_removes_the_primary_first_then_every_chunk()
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes oauth", Bytes(2 * Chunk + 1)));
        backend.Operations.Clear();

        Assert.Equal(SecretStoreStatus.Success, store.Delete(Service, "MCP notes oauth"));

        Assert.Empty(backend.TargetNames);
        var deletes = backend.Operations.Where(o => o.StartsWith("delete ", StringComparison.Ordinal)).ToList();
        Assert.Equal("delete " + target, deletes[0]);
        Assert.Contains("delete " + target + "#2", deletes);
        Assert.Contains("delete " + target + "#3", deletes);
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "MCP notes oauth", out _));
    }

    [Theory]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-SEC-1")]
    [InlineData(true)]
    [InlineData(false)]
    public void Replacing_a_chunked_value_through_either_helper_leaves_only_the_new_value(bool deleteFirst)
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        Assert.True(Set(store, deleteFirst, "MCP notes oauth", new string('x', 2 * Chunk + 9)));

        Assert.True(Set(store, deleteFirst, "MCP notes oauth", "  small  "));

        Assert.Equal([target], backend.TargetNames);
        Assert.Equal("small", store.Read(Service, "MCP notes oauth"));
    }

    // ---- The SecItem status contract (ED-093) ----

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Add_of_an_existing_item_reports_DuplicateItem_and_keeps_the_stored_bytes()
    {
        // SecItemAdd returns errSecDuplicateItem for an existing item; that is why APIKeychain.swift:34 deletes first.
        // CredWriteW would overwrite, so the store has to check.
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "DeepSeek API Key", "one"u8.ToArray()));

        Assert.Equal(SecretStoreStatus.DuplicateItem, store.Add(Service, "DeepSeek API Key", "two"u8.ToArray()));

        Assert.Equal("one"u8.ToArray(), Stored(store, Service, "DeepSeek API Key"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Add_of_an_account_that_differs_only_in_case_from_an_existing_one_succeeds()
    {
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes key", "one"u8.ToArray()));

        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes KEY", "two"u8.ToArray()));

        Assert.Equal("one"u8.ToArray(), Stored(store, Service, "MCP notes key"));
        Assert.Equal("two"u8.ToArray(), Stored(store, Service, "MCP notes KEY"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Update_of_a_missing_item_reports_ItemNotFound_and_creates_nothing()
    {
        // SecItemUpdate never creates an item; MCPKeychain.swift:44 relies on errSecItemNotFound to go on to add.
        var store = NewStore();

        Assert.Equal(SecretStoreStatus.ItemNotFound, store.Update(Service, "MCP notes key", "value"u8.ToArray()));

        Assert.Empty(backend.TargetNames);
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "MCP notes key", out _));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Update_of_an_existing_item_replaces_its_bytes()
    {
        // MCPKeychain.swift:42-43.
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes key", "one"u8.ToArray()));

        Assert.Equal(SecretStoreStatus.Success, store.Update(Service, "MCP notes key", "two"u8.ToArray()));

        Assert.Equal("two"u8.ToArray(), Stored(store, Service, "MCP notes key"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void Delete_reports_Success_for_an_item_and_ItemNotFound_when_there_is_none()
    {
        // SecItemDelete (APIKeychain.swift:58-65, MCPKeychain.swift:52-59); the helpers ignore the status.
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "Z.ai API Key", "value"u8.ToArray()));

        Assert.Equal(SecretStoreStatus.Success, store.Delete(Service, "Z.ai API Key"));
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.Delete(Service, "Z.ai API Key"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void CopyMatching_of_a_missing_item_reports_ItemNotFound_with_no_data()
    {
        // APIKeychain.swift:51; MCPKeychain.swift:21.
        var store = NewStore();

        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "Command Code API Key", out var data));
        Assert.Null(data);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_refused_add_reports_a_failure_status_and_leaves_no_item()
    {
        var store = NewStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        AssertRefusedStatus(store.Add(Service, "Meta API Key", "value"u8.ToArray()));

        Assert.Empty(backend.TargetNames);
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "Meta API Key", out _));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_chunked_add_whose_primary_write_is_refused_reports_a_failure_and_reads_as_item_not_found()
    {
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes oauth");
        backend.BeforeWrite = credential =>
            credential.TargetName == target ? InMemoryCredentialBackend.ErrorAccessDenied : 0;

        AssertRefusedStatus(store.Add(Service, "MCP notes oauth", Bytes(Chunk + 1)));

        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "MCP notes oauth", out _));
        Assert.Null(store.Read(Service, "MCP notes oauth"));
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void A_refused_update_reports_a_failure_status_and_keeps_the_old_bytes()
    {
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "MCP notes key", "old"u8.ToArray()));
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        AssertRefusedStatus(store.Update(Service, "MCP notes key", "new"u8.ToArray()));

        Assert.Equal("old"u8.ToArray(), Stored(store, Service, "MCP notes key"));
    }

    // ---- The helper forms: trimming, empty means delete, the Boolean result (R-SEC-1) ----

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [Trait("Rule", "R-STR-8")]
    [InlineData(true)]
    [InlineData(false)]
    public void Setting_a_value_stores_the_UTF8_bytes_of_the_trimmed_value_and_reports_true(bool deleteFirst)
    {
        // APIKeychain.swift:23, :28, :39; MCPKeychain.swift:31, :36, :43/:49. Leading TAB, SPACE, U+00A0; trailing
        // CR, LF, U+0085, U+3000; the inner space and U+00E9 are kept.
        var store = NewStore();
        var value = "\t \U000000A0sk-cl\U000000E9 42\r\n\U00000085\U00003000"; // U+00A0 NO-BREAK SPACE, U+00E9 LATIN SMALL LETTER E WITH ACUTE, U+0085 NEXT LINE, U+3000 IDEOGRAPHIC SPACE

        Assert.True(Set(store, deleteFirst, "DeepSeek API Key", value));

        var expected = new byte[] { 0x73, 0x6B, 0x2D, 0x63, 0x6C, 0xC3, 0xA9, 0x20, 0x34, 0x32 }; // "sk-cl" U+00E9 " 42" in UTF-8, no BOM
        Assert.Equal(expected, Stored(store, Service, "DeepSeek API Key"));
        Assert.Equal("sk-cl\U000000E9 42", store.Read(Service, "DeepSeek API Key")); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    }

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [Trait("Rule", "R-STR-7")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(true)]
    [InlineData(false)]
    public void Trimming_uses_Foundation_whitespace_and_newlines_not_String_Trim(bool deleteFirst)
    {
        // Text.swift:132 .whitespacesAndNewlines trims U+200B and U+2028 (as FoundationText does, SwiftStringTests);
        // String.Trim() leaves U+200B (the negative control).
        var store = NewStore();
        var value = "\U0000200B\U00002028key\U00002029\U0000200B"; // U+200B ZERO WIDTH SPACE, U+2028 LINE SEPARATOR, U+2029 PARAGRAPH SEPARATOR

        Assert.Equal("\U0000200Bkey\U0000200B", "\U0000200Bkey\U0000200B".Trim()); // U+200B ZERO WIDTH SPACE

        Assert.True(Set(store, deleteFirst, "Meta API Key", value));
        Assert.Equal("key"u8.ToArray(), Stored(store, Service, "Meta API Key"));
        Assert.Equal("key", store.Read(Service, "Meta API Key"));
    }

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    [InlineData(true, "\r\n\t")]
    [InlineData(true, "\U00003000\U00000085")] // U+3000 IDEOGRAPHIC SPACE, U+0085 NEXT LINE
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(false, "\r\n\t")]
    [InlineData(false, "\U00003000\U00000085")] // U+3000 IDEOGRAPHIC SPACE, U+0085 NEXT LINE
    public void An_empty_or_whitespace_only_value_deletes_the_item_and_reports_true(bool deleteFirst, string value)
    {
        // APIKeychain.swift:24-27; MCPKeychain.swift:32-35: delete, then return true.
        var store = NewStore();
        Assert.True(Set(store, deleteFirst, "Z.ai API Key", "stored before"));
        Assert.Equal("stored before", store.Read(Service, "Z.ai API Key"));

        Assert.True(Set(store, deleteFirst, "Z.ai API Key", value));

        Assert.Empty(backend.TargetNames);
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(Service, "Z.ai API Key", out _));
        Assert.Null(store.Read(Service, "Z.ai API Key"));
    }

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [InlineData(true)]
    [InlineData(false)]
    public void Clearing_an_account_that_holds_nothing_still_reports_true(bool deleteFirst)
    {
        // APIKeychain.swift:25-26 and MCPKeychain.swift:33-34 ignore the delete's status.
        var store = NewStore();

        Assert.True(Set(store, deleteFirst, "Command Code API Key", " \n"));

        Assert.Empty(backend.TargetNames);
    }

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_second_value_replaces_the_first(bool deleteFirst)
    {
        // APIKeychain.swift:34-39 (delete, then add); MCPKeychain.swift:42-43 (the update succeeds: true).
        var store = NewStore();

        Assert.True(Set(store, deleteFirst, "MCP notes API_TOKEN", "first"));
        Assert.True(Set(store, deleteFirst, "MCP notes API_TOKEN", "  second  "));

        Assert.Equal("second"u8.ToArray(), Stored(store, Service, "MCP notes API_TOKEN"));
        Assert.Single(backend.TargetNames);
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void SetReplacing_deletes_the_item_before_it_writes_the_new_one()
    {
        // APIKeychain.swift:34 SecItemDelete, then :39 SecItemAdd.
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "DeepSeek API Key");
        Assert.True(store.SetReplacing(Service, "DeepSeek API Key", "first"));
        backend.Operations.Clear();

        Assert.True(store.SetReplacing(Service, "DeepSeek API Key", "second"));

        var firstDelete = backend.Operations.IndexOf("delete " + target);
        var lastWrite = backend.Operations.LastIndexOf("write " + target);
        Assert.True(firstDelete >= 0, "the old item is deleted");
        Assert.True(firstDelete < lastWrite, "the delete comes before the write");
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void SetUpdating_updates_an_existing_item_without_deleting_it()
    {
        // MCPKeychain.swift:42-43: SecItemUpdate succeeds, so no delete and no add.
        var store = NewStore();
        var target = SecretStore.TargetName(Service, "MCP notes API_TOKEN");
        Assert.True(store.SetUpdating(Service, "MCP notes API_TOKEN", "first"));
        backend.Operations.Clear();

        Assert.True(store.SetUpdating(Service, "MCP notes API_TOKEN", "second"));

        Assert.DoesNotContain("delete " + target, backend.Operations);
        Assert.Contains("write " + target, backend.Operations);
        Assert.Equal("second", store.Read(Service, "MCP notes API_TOKEN"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void Read_returns_the_trimmed_value()
    {
        // APIKeychain.swift:54-55; MCPKeychain.swift:24-25: the stored bytes are trimmed on the way out too.
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "DeepSeek API Key", "\t abc \r\n"u8.ToArray()));

        Assert.Equal("abc", store.Read(Service, "DeepSeek API Key"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void Read_treats_a_stored_blank_value_as_absent()
    {
        // APIKeychain.swift:54 and MCPKeychain.swift:24: !value.trimmed.isEmpty else { return nil }.
        var store = NewStore();
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "Meta API Key", " \n\t"u8.ToArray()));

        Assert.Null(store.Read(Service, "Meta API Key"));
        Assert.Equal(" \n\t"u8.ToArray(), Stored(store, Service, "Meta API Key")); // the item itself is still there
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    [Trait("Rule", "R-STR-8")]
    public void Read_returns_null_for_bytes_that_are_not_valid_UTF8()
    {
        // APIKeychain.swift:53 and MCPKeychain.swift:23: String(data: data, encoding: .utf8) fails, so nil.
        var store = NewStore();
        var invalid = new byte[] { 0x61, 0xFF, 0xFE, 0x62 };
        Assert.Equal(SecretStoreStatus.Success, store.Add(Service, "Z.ai API Key", invalid));

        Assert.Null(store.Read(Service, "Z.ai API Key"));
        Assert.Equal(invalid, Stored(store, Service, "Z.ai API Key"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void Read_of_an_account_that_holds_nothing_returns_null()
    {
        // APIKeychain.swift:51; MCPKeychain.swift:21: SecItemCopyMatching fails, so nil.
        var store = NewStore();

        Assert.Null(store.Read(Service, "Command Code API Key"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void A_refused_write_through_SetReplacing_reports_false_and_leaves_no_item()
    {
        // APIKeychain.swift:34 deletes the old item first, then :39 returns false when SecItemAdd fails: after a
        // refused write the account holds nothing, the old value included.
        var store = NewStore();
        Assert.True(store.SetReplacing(Service, "DeepSeek API Key", "old"));
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(store.SetReplacing(Service, "DeepSeek API Key", "new"));

        Assert.Empty(backend.TargetNames);
        Assert.Null(store.Read(Service, "DeepSeek API Key"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void A_refused_update_through_SetUpdating_reports_false_and_keeps_the_old_value()
    {
        // MCPKeychain.swift:42-44: an update status other than success and errSecItemNotFound returns false
        // without trying the add, so the item keeps its old bytes.
        var store = NewStore();
        Assert.True(store.SetUpdating(Service, "MCP notes API_TOKEN", "old"));
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(store.SetUpdating(Service, "MCP notes API_TOKEN", "new"));

        backend.BeforeWrite = null;
        Assert.Equal("old", store.Read(Service, "MCP notes API_TOKEN"));
    }

    [Fact]
    [Trait("Rule", "R-SEC-1")]
    public void A_refused_add_after_the_update_found_nothing_reports_false_and_leaves_no_item()
    {
        // MCPKeychain.swift:44-49: errSecItemNotFound falls through to SecItemAdd, whose failure is false.
        var store = NewStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(store.SetUpdating(Service, "MCP notes API_TOKEN", "new"));

        Assert.Empty(backend.TargetNames);
    }

    [Theory]
    [Trait("Rule", "R-SEC-1")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_refused_write_stores_the_value_nowhere_else(bool deleteFirst)
    {
        // OD-0008 A6 (BLK-0006): fail closed, store nothing; the key is never written in plain text. Upstream's
        // fallback was the caller's defaults write (AppSettings.swift:479-486); the store adds none of its own:
        // no settings write and no file under the data folder.
        using var runtime = new TestRuntime();
        var dataDirectory = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(dataDirectory), "MARROW_PORT_DATA_DIR must name a throwaway folder (R-TEST-9)");
        Directory.CreateDirectory(dataDirectory!);
        var before = Directory.GetFileSystemEntries(dataDirectory!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var store = NewStore();
        backend.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;

        Assert.False(Set(store, deleteFirst, "Meta API Key", "sk-not-a-real-key"));

        Assert.Empty(runtime.Defaults.Writes);
        var after = Directory.GetFileSystemEntries(dataDirectory!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(before, after);
        Assert.Empty(backend.TargetNames);
    }

    // ---- The shared store and the live check ----

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_shared_store_defaults_to_the_Credential_Manager_backend()
    {
        // Reading the property writes and reads no credential.
        Assert.IsType<CredentialManagerBackend>(SecretStore.Shared.Current.Backend);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    public void The_shared_store_can_be_replaced_for_one_async_flow()
    {
        // Ambient<T>: the Keychain helpers reach the store as a static; tests replace it without touching the real one.
        var store = NewStore();
        using (SecretStore.Shared.Override(store))
            Assert.Same(store, SecretStore.Shared.Current);
        Assert.NotSame(store, SecretStore.Shared.Current);
    }

    [Fact]
    [Trait("Decision", "ED-093")]
    [Trait("Rule", "R-TEST-9")]
    public void The_real_Credential_Manager_holds_a_chunked_item_under_a_throwaway_service_when_MARROW_PORT_CREDMAN_LIVE_is_1()
    {
        // ED-093: the one live test, run only when MARROW_PORT_CREDMAN_LIVE=1, so nothing touches the user's
        // profile by default (R-TEST-9). It works under "Marrow port tests <GUID>" and deletes everything in finally.
        if (Environment.GetEnvironmentVariable("MARROW_PORT_CREDMAN_LIVE") != "1") return;

        var live = new CredentialManagerBackend();
        var store = new SecretStore(live);
        var service = "Marrow port tests " + Guid.NewGuid().ToString("D");
        var probe = service + ":probe";
        string[] accounts = ["MCP live oauth", "MCP live OAUTH"];
        try
        {
            // The limit ED-093 probed: 2560 accepted, 2561 refused with 1783.
            Assert.Equal(0, live.Write(new GenericCredential(probe, "probe", Bytes(Chunk), 1, 2, [])));
            Assert.Equal(1783, live.Write(new GenericCredential(probe, "probe", Bytes(Chunk + 1), 1, 2, [])));

            var big = new string('b', 2 * Chunk + 1);
            Assert.True(store.SetUpdating(service, accounts[0], big));
            Assert.True(store.SetUpdating(service, accounts[1], "upper"));

            Assert.Equal(big, store.Read(service, accounts[0]));
            Assert.Equal("upper", store.Read(service, accounts[1]));
            Assert.Equal(0, live.Read(SecretStore.TargetName(service, accounts[0]), out var primary));
            Assert.Equal(accounts[0], primary!.UserName);
            Assert.Equal(1, primary.Type);
            Assert.Equal(2, primary.Persist);

            Assert.True(store.SetUpdating(service, accounts[0], "   "));
            Assert.Null(store.Read(service, accounts[0]));
            Assert.Equal("upper", store.Read(service, accounts[1]));
        }
        finally
        {
            foreach (var account in accounts) store.Delete(service, account);
            live.Delete(probe);
        }
        Assert.Equal(SecretStoreStatus.ItemNotFound, store.CopyMatching(service, accounts[1], out _));
    }
}
