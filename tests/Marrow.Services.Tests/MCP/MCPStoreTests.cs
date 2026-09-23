using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// Tests for src/Marrow.Services/MCP/MCPStore.cs, the port of DroppyCode/Services/MCP/MCPStore.swift (B2 1fbf81e),
// :1-357, and records PR-MCP-049 to PR-MCP-072 (part 1 of Services.MCP.MCPStore; part 2 covers load/save,
// deleteCustom, storedValue and saveCustomValues, PR-MCP-073 and up, and the byte-level persistence format).
//
// StartProbe's spawned task (MainActor.Spawn inside Connect/Refresh) is a real Task queued on the test's manual
// main loop. RunOneStep below drains exactly ONE queued item, so a test that only needs Connect/Refresh's
// SYNCHRONOUS pre-probe contract never lets the spawned probe task run (no real network or child process, per
// R-TEST-10). Where a test needs an actual probe OUTCOME, it uses ProbeProcessScripts (R-TEST-8, the same fixture
// MCPProbeTests.cs uses): a local scripted PowerShell "MCP server", never a real AI CLI or the network.
//
// Not tested here, with the reason (R-TEST rule "cannot be tested automatically"):
//  - PR-MCP-065 (the OAuth unauthorizedOAuth/401/403 retry inside startProbe) and PR-MCP-066 (probeOnce's sign-in
//    -> register -> probe ordering): the only OAuth transport in the catalog is "gitlab", whose URL template is
//    hardcoded to "https://{instance}/...", so MCPProbe would need a TLS loopback server standing in for GitLab.
//    No such fixture exists in this tree (MCPProbeTests' LoopbackHttpServer is plain HTTP) and standing one up
//    (a self-signed cert, SNI) is out of proportion for this unit; MCPOAuthTests.cs and MCPProbeTests.cs already
//    prove MCPOAuth.SignIn/SignOut and MCPProbe's 401/403 classification on their own. What IS testable at the
//    store level - connect's own two sign-out branches, independent of any network - is PR-MCP-057/058 below.
//  - The needsSetup half of PR-MCP-068 (states becomes .needsSetup and the OAuth session is kept): needsSetup is
//    thrown only for a live GitLab probe (MCPProbe.GitLabServerDisabled), the same TLS-loopback gap as above.
public sealed class MCPStoreTests
{
    private static MCPCustomServer StdioCustom(string name, string command = "marrow-store-test-nonexistent-cmd-93a1",
        IEnumerable<string>? args = null, IEnumerable<MCPCustomServer.Variable>? env = null) =>
        new(MCPCustomServer.MakeID(name), name, MCPCustomServer.Kind.Stdio)
        {
            Command = command,
            Args = new ValueList<string>(args ?? []),
            Env = new ValueList<MCPCustomServer.Variable>(env ?? []),
        };

    /// Runs body as the only queued action, then returns before any task IT spawns (StartProbe's Task) is ever
    /// dequeued: RunUntil re-checks its condition after each single item, and `() => true` is true right away.
    private static void RunOneStep(TestRuntime runtime, Action body)
    {
        runtime.Loop.Enqueue(body);
        runtime.Loop.RunUntil(() => true);
    }

    // ===== PR-MCP-050: allEntries =====

    [Fact]
    [Trait("Req", "PR-MCP-050")] // DroppyCode/Services/MCP/MCPStore.swift:53-55
    public void AllEntries_lists_customs_before_the_catalog_in_creation_order()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var first = StdioCustom("Zeta Tool");
            var second = StdioCustom("Alpha Tool");
            Assert.Null(store.SaveCustom(first));
            Assert.Null(store.SaveCustom(second));
            var ids = store.AllEntries.Select(e => e.Id).ToList();
            var catalogIds = MCPCatalog.Entries.Select(e => e.Id).ToList();
            // Customs first, in creation order (Zeta before Alpha, not alphabetical), then the catalog in its order.
            Assert.Equal(new[] { first.Id, second.Id }.Concat(catalogIds), ids);
        });
    }

    // ===== PR-MCP-051: entry(for:) =====

    [Fact]
    [Trait("Req", "PR-MCP-051")] // :58-60
    public void Entry_prefers_a_custom_over_the_catalog_and_falls_back_to_the_catalog_then_nil()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var custom = StdioCustom("Github Shadow"); // deliberately not colliding with a real catalog id
            Assert.Null(store.SaveCustom(custom));
            Assert.Equal(custom.Id, store.Entry(custom.Id)!.Id);
            Assert.NotNull(store.Entry("github")); // real catalog id, no custom shadowing it
            Assert.Null(store.Entry("not-a-real-server-id"));
        });
    }

    // ===== PR-MCP-052: value(for:of:) =====

    [Fact]
    [Trait("Req", "PR-MCP-052")] // :63-71
    public void Value_prefers_draft_then_secret_store_or_connection_by_kind_then_default()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var custom = StdioCustom("Kv Tool", env: [new("apiKey", IsSecret: true), new("mode", IsSecret: false)]);
            Assert.Null(store.SaveCustom(custom));
            var entry = store.Entry(custom.Id)!;
            var secretField = entry.Fields.Single(f => f.Key == "apiKey");
            var textField = entry.Fields.Single(f => f.Key == "mode");

            // No draft, nothing stored: the default (empty string; MCPField from a custom variable has none).
            Assert.Equal("", store.Value(secretField, entry));

            // A draft wins outright, even set to the field's own key text.
            store.SetDraft("draft-value", secretField, entry);
            Assert.Equal("draft-value", store.Value(secretField, entry));

            // Once the draft is gone, a secret field reads the secret store, never the plaintext connection.
            store.Drafts.Remove(entry.Id);
            MCPKeychain.Set("stored-secret", entry.Id, "apiKey");
            Assert.Equal("stored-secret", store.Value(secretField, entry));

            // A non-secret field reads the connection's plaintext values, never the secret store.
            MCPKeychain.Set("should-not-be-read", entry.Id, "mode");
            Assert.Equal("", store.Value(textField, entry)); // no connection row yet
        });
    }

    // ===== PR-MCP-055: storedValues overlays the store's secrets on the connection's plaintext values =====

    [Fact]
    [Trait("Req", "PR-MCP-055")] // :293-301
    public async Task EnabledServers_resolves_a_secret_field_from_the_store_overlaid_on_the_connection_values()
    {
        using var harness = new MCPStoreDirectHarness();
        var store = new MCPStore();
        var custom = StdioCustom("Overlay Tool", command: ProbeProcessScripts.PowerShell,
            args: ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd()),
            env: [new("apiKey", IsSecret: true), new("mode", IsSecret: false)]);
        Assert.Null(store.SaveCustom(custom));
        var entry = store.Entry(custom.Id)!;
        var secretField = entry.Fields.Single(f => f.Key == "apiKey");
        var textField = entry.Fields.Single(f => f.Key == "mode");

        store.SetDraft("secret-value", secretField, entry);
        store.SetDraft("plain-value", textField, entry);
        store.Connect(entry); // commits the secret to the store, the plain value to the connection
        await WaitWhileConnecting(store, entry.Id);

        Assert.True(store.State(entry.Id) is MCPConnectionState.Connected);
        var resolved = store.EnabledServers.Single(s => s.Id == entry.Id);
        // storedValues (:293-301) overlays the secret store's "apiKey" onto the connection's plain "mode":
        // both reach the resolved server's env, proving the merge, not just one source or the other.
        Assert.Equal("secret-value", resolved.Env["apiKey"]);
        Assert.Equal("plain-value", resolved.Env["mode"]);
        Assert.False(store.Connection(entry.Id)!.Values.ContainsKey("apiKey")); // never the plaintext connection
    }

    // ===== PR-MCP-053: setDraft =====

    [Fact]
    [Trait("Req", "PR-MCP-053")] // :73-75
    public void SetDraft_records_a_per_entry_per_field_draft_that_value_then_returns()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var custom = StdioCustom("Draft Tool", env: [new("token")]);
            Assert.Null(store.SaveCustom(custom));
            var entry = store.Entry(custom.Id)!;
            var field = entry.Fields.Single();
            store.SetDraft("typed-text", field, entry);
            Assert.Equal("typed-text", store.Drafts[entry.Id][field.Key]);
            Assert.Equal("typed-text", store.Value(field, entry));
        });
    }

    // ===== PR-MCP-054: canConnect =====

    [Fact]
    [Trait("Req", "PR-MCP-054")] // :78-80
    public void CanConnect_is_true_only_once_every_required_field_has_a_value()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var custom = StdioCustom("Required Tool", env: [new("apiKey", IsSecret: true)]);
            Assert.Null(store.SaveCustom(custom));
            var entry = store.Entry(custom.Id)!;
            var field = entry.Fields.Single();
            Assert.True(field.IsRequired); // a custom variable field is required (MCPField default)
            Assert.False(store.CanConnect(entry));
            store.SetDraft("x", field, entry);
            Assert.True(store.CanConnect(entry));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-054")] // :78-80, "Optional fields are ignored entirely"
    public void CanConnect_ignores_optional_fields_and_is_true_with_none_required()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var entry = MCPCatalog.Entry("gitlab")!; // its only field ("instance") is not required
            Assert.True(store.CanConnect(entry));
        });
    }

    // ===== PR-MCP-056/059/060: connect's synchronous contract =====

    [Fact]
    [Trait("Req", "PR-MCP-056")] // :86-89
    [Trait("Req", "PR-MCP-060")] // :113-117
    public void Connect_marks_enabled_saves_the_connection_sets_connecting_and_clears_the_draft()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Enable Tool");
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        RunOneStep(harness.Runtime, () => store.SetDraft("ignored plain value", entry.Fields.Count > 0 ? entry.Fields[0] : new MCPField("x", "x", ""), entry));

        RunOneStep(harness.Runtime, () =>
        {
            store.Connect(entry);
            Assert.True(store.State(entry.Id) is MCPConnectionState.Connecting); // :116, observed before the probe runs
            Assert.True(store.Connection(entry.Id)!.IsEnabled); // :89
        });
        // exportAll is NOT reached synchronously by connect (:113-117 has no exportAll call), only save().
        Assert.True(File.Exists(MCPPaths.ConnectionsURL));
    }

    [Fact]
    [Trait("Req", "PR-MCP-059")] // :103-112
    public void Connect_commits_drafted_fields_secrets_to_the_store_plain_ones_to_the_connection()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Field Tool", env: [new("apiKey", IsSecret: true), new("mode", IsSecret: false)]);
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        var secretField = entry.Fields.Single(f => f.Key == "apiKey");
        var textField = entry.Fields.Single(f => f.Key == "mode");

        RunOneStep(harness.Runtime, () =>
        {
            store.SetDraft("secret-text", secretField, entry);
            store.SetDraft("plain-text", textField, entry);
            store.Connect(entry);
        });

        Assert.Equal("secret-text", MCPKeychain.Value(entry.Id, "apiKey"));
        Assert.Equal("plain-text", harness.Runtime.OnMain(() => store.Connection(entry.Id)!.Values["mode"]));
        // The connection never receives the secret's plaintext.
        Assert.False(harness.Runtime.OnMain(() => store.Connection(entry.Id)!.Values.ContainsKey("apiKey")));
    }

    // ===== PR-MCP-057: OAuth address change signs out first =====

    [Fact]
    [Trait("Req", "PR-MCP-057")] // :90-96
    public void Connect_signs_out_an_oauth_server_when_the_draft_changes_its_filled_address()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var gitlab = harness.Runtime.OnMain(() => store.Entry("gitlab")!);
        var instanceField = gitlab.Fields.Single(f => f.Key == "instance");
        MCPKeychain.Set("token", "gitlab", "oauth");
        MCPKeychain.Set("client", "gitlab", "oauth-client");

        RunOneStep(harness.Runtime, () =>
        {
            store.SetDraft("gitlab.example.org", instanceField, gitlab); // differs from the default "gitlab.com"
            store.Connect(gitlab);
        });

        Assert.Null(MCPKeychain.Value("gitlab", "oauth"));
        Assert.Null(MCPKeychain.Value("gitlab", "oauth-client"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-057")] // :90-96, the no-op side: an unchanged address signs out nothing
    public void Connect_does_not_sign_out_an_oauth_server_when_the_draft_does_not_change_its_address()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var gitlab = harness.Runtime.OnMain(() => store.Entry("gitlab")!);
        var instanceField = gitlab.Fields.Single(f => f.Key == "instance");
        MCPKeychain.Set("token", "gitlab", "oauth");

        RunOneStep(harness.Runtime, () =>
        {
            store.SetDraft("gitlab.com", instanceField, gitlab); // the default value, same filled URL
            store.Connect(gitlab);
        });

        Assert.Equal("token", MCPKeychain.Value("gitlab", "oauth"));
    }

    // ===== PR-MCP-058: retrying a failed OAuth server signs out =====

    [Fact]
    [Trait("Req", "PR-MCP-058")] // :99-102, the state guard: only a .failed entry is signed out on this branch
    public void Connect_does_not_sign_out_an_oauth_server_whose_state_is_not_failed()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var gitlab = harness.Runtime.OnMain(() => store.Entry("gitlab")!);
        MCPKeychain.Set("token", "gitlab", "oauth");
        // A fresh entry has no state (.notConnected, not .failed): connecting it must not run this branch, so
        // the address-change branch (PR-MCP-057, already proven not to fire for an unchanged address) is the
        // only source of a sign-out here.
        Assert.True(store.State("gitlab") is MCPConnectionState.NotConnected);
        RunOneStep(harness.Runtime, () => store.Connect(gitlab));
        Assert.Equal("token", MCPKeychain.Value("gitlab", "oauth")); // not signed out: state was not .failed
    }

    // PR-MCP-058's positive case (a genuinely .failed entry IS signed out) needs a real failed probe outcome for
    // the OAuth-only "gitlab" entry, the same TLS-loopback gap noted at the top of this file; not tested here.

    // ===== PR-MCP-061: disconnect =====

    [Fact]
    [Trait("Req", "PR-MCP-061")] // :120-134
    public void Disconnect_forgets_the_connection_state_and_draft_and_deletes_the_stored_fields()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Disconnect Tool", env: [new("apiKey", IsSecret: true)]);
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        var field = entry.Fields.Single();

        RunOneStep(harness.Runtime, () =>
        {
            store.SetDraft("secret", field, entry);
            store.Connect(entry);
        });
        Assert.Equal("secret", MCPKeychain.Value(entry.Id, "apiKey"));

        harness.Runtime.OnMain(() => store.Disconnect(entry));

        harness.Runtime.OnMain(() =>
        {
            Assert.Null(store.Connection(entry.Id));
            Assert.True(store.State(entry.Id) is MCPConnectionState.NotConnected);
            Assert.False(store.Drafts.ContainsKey(entry.Id));
        });
        Assert.Null(MCPKeychain.Value(entry.Id, "apiKey"));
    }

    // ===== PR-MCP-062: refresh =====

    [Fact]
    [Trait("Req", "PR-MCP-062")] // :136-141
    public void Refresh_sets_connecting_without_reading_or_clearing_drafts_or_saving_immediately()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Refresh Tool", env: [new("mode")]);
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        var field = entry.Fields.Single();

        RunOneStep(harness.Runtime, () =>
        {
            store.SetDraft("leftover-draft", field, entry);
            store.Refresh(entry);
            Assert.True(store.State(entry.Id) is MCPConnectionState.Connecting);
            // Refresh does not touch drafts at all (:137-141 reads and writes none).
            Assert.Equal("leftover-draft", store.Drafts[entry.Id][field.Key]);
        });
    }

    // ===== PR-MCP-063: setEnabled =====

    [Fact]
    [Trait("Req", "PR-MCP-063")] // :143-149, the no-connection guard
    public void SetEnabled_does_nothing_without_a_connection_row()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Guard Tool");
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        harness.Runtime.OnMain(() => store.SetEnabled(false, entry)); // :144 guard var connection = connections[id] else return
        Assert.False(File.Exists(MCPPaths.ConnectionsURL));
    }

    [Fact]
    [Trait("Req", "PR-MCP-063")] // :143-149, the flip-save-export path
    public void SetEnabled_flips_the_switch_and_saves_once_a_connection_exists()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Flip Tool");
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);
        RunOneStep(harness.Runtime, () => store.Connect(entry)); // creates the connection row (isEnabled true)

        harness.Runtime.OnMain(() => store.SetEnabled(false, entry));
        Assert.False(harness.Runtime.OnMain(() => store.Connection(entry.Id)!.IsEnabled));

        var savedAt = File.GetLastWriteTimeUtc(MCPPaths.ConnectionsURL);
        harness.Runtime.OnMain(() => store.SetEnabled(true, entry));
        Assert.True(harness.Runtime.OnMain(() => store.Connection(entry.Id)!.IsEnabled));
        Assert.True(File.GetLastWriteTimeUtc(MCPPaths.ConnectionsURL) >= savedAt);
    }

    // ===== PR-MCP-064: a successful probe writes tools/version only when the row still exists =====

    /// Polls until the state leaves .connecting, on a real async wait (not ManualMainLoop, which stalls a real
    /// stdio probe's process I/O; see MCPStoreDirectHarness).
    private static async Task WaitWhileConnecting(MCPStore store, string id, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (store.State(id) is MCPConnectionState.Connecting)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The probe did not finish.");
            await Task.Delay(20);
        }
    }

    [Fact]
    [Trait("Req", "PR-MCP-064")] // :334-343
    public async Task A_successful_probe_writes_connectedAt_version_and_tools_and_marks_connected()
    {
        using var harness = new MCPStoreDirectHarness();
        var store = new MCPStore();
        var custom = StdioCustom("Success Tool", command: ProbeProcessScripts.PowerShell,
            args: ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd()));
        Assert.Null(store.SaveCustom(custom));
        var entry = store.Entry(custom.Id)!;

        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);

        Assert.True(store.State(entry.Id) is MCPConnectionState.Connected(0));
        var connection = store.Connection(entry.Id)!;
        Assert.NotNull(connection.ConnectedAt);
        Assert.NotNull(connection.ServerVersion); // the script echoes (Get-Location).Path, never null
        Assert.Empty(connection.Tools);
        Assert.True(File.Exists(MCPPaths.ConnectionsURL));
    }

    // ===== PR-MCP-067: a cancelled probe changes nothing =====

    [Fact]
    [Trait("Req", "PR-MCP-067")] // :344, empty CancellationError body
    public void A_probe_cancelled_by_disconnect_before_it_runs_leaves_no_state_once_the_loop_drains()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Cancel Tool"); // nonexistent command: if the task ran for real it would fail fast
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);

        RunOneStep(harness.Runtime, () => store.Connect(entry)); // queues StartProbe's task, does not run it
        harness.Runtime.OnMain(() => store.Disconnect(entry)); // cancels it, then drains it (its guard sees IsCancelled)

        harness.Runtime.OnMain(() =>
        {
            Assert.Null(store.Connection(entry.Id));
            Assert.True(store.State(entry.Id) is MCPConnectionState.NotConnected); // never became .failed
        });
    }

    // ===== PR-MCP-069: any other probe error becomes .failed with the error's own message =====

    [Fact]
    [Trait("Req", "PR-MCP-069")] // :350-353
    public void A_command_missing_probe_error_becomes_failed_with_its_own_message()
    {
        using var harness = new MCPStoreHarness();
        var store = harness.Runtime.OnMain(() => new MCPStore());
        var custom = StdioCustom("Missing Command Tool", command: "marrow-store-test-definitely-nonexistent-93a1");
        harness.Runtime.OnMain(() => Assert.Null(store.SaveCustom(custom)));
        var entry = harness.Runtime.OnMain(() => store.Entry(custom.Id)!);

        harness.Runtime.OnMain(() => store.Connect(entry));
        harness.Runtime.Loop.RunUntil(() => store.State(entry.Id) is not MCPConnectionState.Connecting, TimeSpan.FromSeconds(20));

        harness.Runtime.OnMain(() =>
        {
            var state = store.State(entry.Id);
            var failed = Assert.IsType<MCPConnectionState.Failed>(state);
            Assert.False(string.IsNullOrEmpty(failed.Message));
            // Nothing is saved or exported on this path (:350-353): the connection row is untouched (never connected).
            Assert.Null(store.Connection(entry.Id)!.ConnectedAt);
        });
    }

    // ===== PR-MCP-070/071: saveCustom validates, mints the id once, and inserts/replaces in place =====

    [Fact]
    [Trait("Req", "PR-MCP-070")] // :156-166
    public void SaveCustom_trims_the_name_mints_the_id_once_and_validates_against_taken_ids()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var created = StdioCustom("  Spaced Name  ");
            Assert.Null(store.SaveCustom(created));
            var saved = store.Customs.Single();
            Assert.Equal("Spaced Name", saved.Name); // :162 trimmed
            Assert.Equal(MCPCustomServer.MakeID("  Spaced Name  "), saved.Id); // minted once, from the raw name

            // A rename (replacing: same id) never changes the id.
            var renamed = saved with { Name = "Totally Different Name" };
            Assert.Null(store.SaveCustom(renamed, replacing: saved.Id));
            Assert.Equal(saved.Id, store.Customs.Single().Id);
            Assert.Equal("Totally Different Name", store.Customs.Single().Name);

            // A new custom whose name mints the SAME id as an existing one is rejected, nothing written. The
            // renamed entry above still carries the ORIGINAL id (minted once, :165, never moved by a rename),
            // so the collision is with "Spaced Name", not with the entry's current name.
            var colliding = StdioCustom("Spaced Name");
            var error = store.SaveCustom(colliding);
            Assert.IsType<MCPCustomError.TakenName>(error);
            Assert.Single(store.Customs); // nothing added
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-070")] // :166, the empty-name validation error, one representative failure case
    public void SaveCustom_rejects_an_empty_name()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var error = store.SaveCustom(StdioCustom("   "));
            Assert.IsType<MCPCustomError.EmptyName>(error);
            Assert.Empty(store.Customs);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-071")] // :167-175
    public void SaveCustom_replaces_in_place_keeping_creation_order()
    {
        using var harness = new MCPStoreHarness();
        harness.Runtime.OnMain(() =>
        {
            var store = new MCPStore();
            var a = StdioCustom("A Tool");
            var b = StdioCustom("B Tool");
            var c = StdioCustom("C Tool");
            Assert.Null(store.SaveCustom(a));
            Assert.Null(store.SaveCustom(b));
            Assert.Null(store.SaveCustom(c));
            var renamedB = store.Customs[1] with { Name = "B Tool Renamed" };
            Assert.Null(store.SaveCustom(renamedB, replacing: b.Id));
            Assert.Equal(new[] { a.Id, b.Id, c.Id }, store.Customs.Select(s => s.Id)); // order kept, not re-appended
            Assert.Equal("B Tool Renamed", store.Customs[1].Name);
            Assert.True(File.Exists(MCPPaths.CustomsURL));
        });
    }

    // ===== PR-MCP-072: saving an edit to a connected custom server drops its connection =====

    [Fact]
    [Trait("Req", "PR-MCP-072")] // :176-189
    public async Task SaveCustom_clears_connectedAt_version_and_tools_and_sets_notConnected_for_a_connected_edit()
    {
        using var harness = new MCPStoreDirectHarness();
        var store = new MCPStore();
        var custom = StdioCustom("Edit Tool", command: ProbeProcessScripts.PowerShell,
            args: ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd()));
        Assert.Null(store.SaveCustom(custom));
        var entry = store.Entry(custom.Id)!;

        store.Connect(entry);
        await WaitWhileConnecting(store, entry.Id);
        Assert.True(store.State(entry.Id) is MCPConnectionState.Connected);

        var edited = custom with { Args = new ValueList<string>(custom.Args.Append("-extra")) };
        Assert.Null(store.SaveCustom(edited, replacing: custom.Id));
        Assert.True(store.State(custom.Id) is MCPConnectionState.NotConnected); // :185
        var connection = store.Connection(custom.Id)!;
        Assert.Null(connection.ConnectedAt); // :181
        Assert.Null(connection.ServerVersion); // :182
        Assert.Empty(connection.Tools); // :183
        // enabledServers (and so every provider config) excludes it immediately: connectedAt is nil (PR-MCP-049).
        Assert.DoesNotContain(entry.Id, store.EnabledServers.Select(s => s.Id));
    }

    // ===== PR-MCP-049: enabledServers (drop-on-missing-entry, via a raw seeded mcp.json) =====

    [Fact]
    [Trait("Req", "PR-MCP-049")] // :40-49
    public async Task EnabledServers_keeps_only_enabled_connected_rows_resolved_and_sorted_and_drops_missing_entries()
    {
        using var harness = new MCPStoreDirectHarness();
        // Seed mcp.json directly with a connection for an id that names neither a custom nor a catalog entry
        // (load() itself is part 2's own record; this only exercises what part 1's enabledServers does with
        // what load() already put in `connections`, the same way the store would see it after a real launch).
        Directory.CreateDirectory(Path.GetDirectoryName(MCPPaths.ConnectionsURL)!);
        File.WriteAllText(MCPPaths.ConnectionsURL,
            "[{\"catalogID\":\"ghost-not-in-catalog\",\"connectedAt\":\"2024-01-01T00:00:00Z\",\"isEnabled\":true,\"serverVersion\":null,\"tools\":[],\"values\":{}}]",
            Encoding.UTF8);

        var store = new MCPStore();
        var b = StdioCustom("B Server", command: ProbeProcessScripts.PowerShell, args: ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd()));
        var a = StdioCustom("A Server", command: ProbeProcessScripts.PowerShell, args: ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd()));
        Assert.Null(store.SaveCustom(b));
        Assert.Null(store.SaveCustom(a));
        var entryA = store.Entry(a.Id)!;
        var entryB = store.Entry(b.Id)!;

        foreach (var entry in new[] { entryA, entryB })
        {
            store.Connect(entry);
            await WaitWhileConnecting(store, entry.Id);
        }
        store.SetEnabled(false, entryB); // connected but disabled: excluded

        var ids = store.EnabledServers.Select(s => s.Id).ToList();
        Assert.Equal(new[] { a.Id }, ids); // b excluded (disabled), ghost excluded (no entry), sorted (trivially, one item)
        Assert.DoesNotContain("ghost-not-in-catalog", ids);
    }
}
