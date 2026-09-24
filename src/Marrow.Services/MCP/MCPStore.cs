using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;
using Marrow.Services.Port;

// Source: DroppyCode/Services/MCP/MCPStore.swift (B2 1fbf81e), :1-357. Records PR-MCP-049 to PR-MCP-072 (this
// part) and PR-MCP-073 and up (load/save/deleteCustom/storedValue/saveCustomValues, part 2 of this unit); this
// file implements the whole class so it compiles and the part-1 records behave correctly end to end.
namespace Marrow.Services.MCP;

// :8-10 @MainActor @Observable final class MCPStore (R-OBS-6)
public sealed class MCPStore : MainActorModel
{
    // :12 private(set) var connections: [String: MCPConnection] = [:]
    private Dictionary<string, MCPConnection> connectionsField = new(StringComparer.Ordinal);

    // :14 private(set) var states: [String: MCPConnectionState] = [:]
    private Dictionary<string, MCPConnectionState> statesField = new(StringComparer.Ordinal);

    // :16 var drafts: [String: [String: String]] = [:]. Public read-write in Swift; exposed directly so
    // setDraft/value(for:of:) (below) and callers outside this file read and write the same storage.
    public Dictionary<string, Dictionary<string, string>> Drafts { get; } = new(StringComparer.Ordinal);

    // :18 private(set) var customs: [MCPCustomServer] = []
    private List<MCPCustomServer> customsField = [];

    // :20 @ObservationIgnored private var probes: [String: Task<Void, Never>] = [:] (R-TASK-1)
    private readonly Dictionary<string, TaskHandle> probes = new(StringComparer.Ordinal);

    // :22-28
    public MCPStore()
    {
        Load(); // :23
        // :24-25 Up before anything resolves a route to it: the probe, the hub and every launch reach
        // signed-in servers through the proxy.
        MCPProxy.Shared.Current.Start(); // :26
        ExportAll(); // :27
    }

    // MARK: Reading (:30)

    public IReadOnlyDictionary<string, MCPConnection> Connections => Read(connectionsField); // :11-12
    public IReadOnlyDictionary<string, MCPConnectionState> States => Read(statesField); // :13-14
    public IReadOnlyList<MCPCustomServer> Customs => Read(customsField); // :17-18

    // :32-34
    public MCPConnectionState State(string id) =>
        statesField.TryGetValue(id, out var state) ? state : new MCPConnectionState.NotConnected();

    // :36-38
    public MCPConnection? Connection(string id) => connectionsField.TryGetValue(id, out var connection) ? connection : null;

    /// :40-49 Connected and enabled servers, resolved with their field values: what providers get.
    public IReadOnlyList<MCPResolvedServer> EnabledServers => Computed(() => Connections.Values
        .Where(connection => connection.IsEnabled && connection.ConnectedAt is not null) // :43
        .Select(connection => // :44-47
        {
            var entry = Entry(connection.CatalogID);
            return entry is null ? null : MCPResolver.Resolve(entry, StoredValues(entry), MCPProxy.Url);
        })
        .Where(resolved => resolved is not null)
        .Select(resolved => resolved!)
        .OrderBy(resolved => resolved.Id, SwiftStringComparer.Instance) // :48, R-COLL-1
        .ToList());

    /// :51-55 Every server the page can show: the catalog's and the user's own, customs first on a name
    /// collision (theirs are the ones to edit).
    public IReadOnlyList<MCPCatalogEntry> AllEntries => Computed(() =>
        Customs.Select(custom => custom.CatalogEntry()).Concat(MCPCatalog.Entries).ToList()); // :54

    /// :57-60 The entry an id names: a custom first, then the catalog.
    public MCPCatalogEntry? Entry(string id) => // :58-60
        customsField.FirstOrDefault(custom => SwiftString.Equal(custom.Id, id))?.CatalogEntry() ?? MCPCatalog.Entry(id);

    /// :62-63 The value the card shows for a field: the draft, else the stored value (secrets from the store).
    public string Value(MCPField field, MCPCatalogEntry entry) // :63-71
    {
        if (Drafts.TryGetValue(entry.Id, out var entryDrafts) && entryDrafts.TryGetValue(field.Key, out var draft))
            return draft; // :64
        if (field.FieldKind == MCPField.Kind.Secret) // :65
        {
            if (MCPKeychain.Value(entry.Id, field.Key) is { } secret) return secret; // :66
        }
        else if (Connection(entry.Id)?.Values is { } values && values.TryGetValue(field.Key, out var stored)) // :67
        {
            return stored; // :68
        }
        return field.DefaultValue ?? ""; // :70
    }

    // :73-75
    public void SetDraft(string value, MCPField field, MCPCatalogEntry entry)
    {
        if (!Drafts.TryGetValue(entry.Id, out var entryDrafts))
            Drafts[entry.Id] = entryDrafts = new Dictionary<string, string>(StringComparer.Ordinal); // :74, default: [:]
        entryDrafts[field.Key] = value;
    }

    /// :77-80 Whether every required field of the entry has a value (draft or stored).
    public bool CanConnect(MCPCatalogEntry entry) => // :78-80
        entry.Fields.Where(field => field.IsRequired).All(field => Value(field, entry).Length > 0);

    // MARK: Connecting (:82)

    /// :84-118 Stores the drafts (secrets to protected storage), probes the server, and on success marks it
    /// connected with its tools; on failure keeps the values and records the message.
    public void Connect(MCPCatalogEntry entry) // :86
    {
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :87

        var connection = Connection(entry.Id) ?? new MCPConnection(entry.Id); // :88
        connection = connection with { IsEnabled = true }; // :89

        // :90-96 A sign-in belongs to one address: pointing the server elsewhere (another GitLab instance)
        // drops the old token so the browser flow runs against the new one.
        if (entry.IsOAuth && Drafts.TryGetValue(entry.Id, out var addressDraft)) // :92
        {
            var before = entry.OauthURL(connection.Values)!; // :93
            var merged = connection.Values.Mutating(map => { foreach (var pair in addressDraft) map[pair.Key] = pair.Value; }); // :93 merging(draft) { $1 }
            var after = entry.OauthURL(merged)!; // :93
            if (!SwiftString.Equal(before, after)) // :93
            {
                MCPOAuth.SignOut(entry.Id); // :94
                MCPProxy.Shared.Current.Unregister(entry.Id); // :95
            }
        }

        // :97-102 A Try again after a rejection must not reuse the rejected token: a failed OAuth entry
        // starts signed out so the browser flow runs afresh.
        if (entry.IsOAuth && State(entry.Id) is MCPConnectionState.Failed) // :99
        {
            MCPOAuth.SignOut(entry.Id); // :100
            MCPProxy.Shared.Current.Unregister(entry.Id); // :101
        }

        if (Drafts.TryGetValue(entry.Id, out var draft)) // :103
        {
            foreach (var field in entry.Fields) // :104
            {
                if (!draft.TryGetValue(field.Key, out var text)) continue; // :105
                if (field.FieldKind == MCPField.Kind.Secret) // :106
                    MCPKeychain.Set(text, entry.Id, field.Key); // :107
                else
                    connection = connection with { Values = connection.Values.Mutating(map => map[field.Key] = text) }; // :109
            }
        }

        connectionsField[entry.Id] = connection; // :113
        Drafts.Remove(entry.Id); // :114
        Save(); // :115
        statesField[entry.Id] = new MCPConnectionState.Connecting(); // :116
        StartProbe(entry); // :117
    }

    /// :120-134 Forgets the connection, its values and its secrets, and removes it from every provider config.
    public void Disconnect(MCPCatalogEntry entry) // :121
    {
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :122-123
        MCPKeychain.DeleteAll(entry); // :124
        if (entry.IsOAuth) // :125
        {
            MCPOAuth.SignOut(entry.Id); // :126
            MCPProxy.Shared.Current.Unregister(entry.Id); // :127
        }
        connectionsField.Remove(entry.Id); // :129
        statesField.Remove(entry.Id); // :130
        Drafts.Remove(entry.Id); // :131
        Save(); // :132
        ExportAll(); // :133
    }

    /// :136-141 Re-probes a connected server without changing its values.
    public void Refresh(MCPCatalogEntry entry) // :137
    {
        if (probes.Remove(entry.Id, out var existing)) existing.Cancel(); // :138
        statesField[entry.Id] = new MCPConnectionState.Connecting(); // :139
        StartProbe(entry); // :140
    }

    // :143-149
    public void SetEnabled(bool enabled, MCPCatalogEntry entry)
    {
        if (Connection(entry.Id) is not { } connection) return; // :144
        connection = connection with { IsEnabled = enabled }; // :145
        connectionsField[entry.Id] = connection; // :146
        Save(); // :147
        ExportAll(); // :148
    }

    // MARK: Custom servers (:151)

    /// :153-190 Adds or replaces a custom server. Validates first; the error, when there is one, is for the
    /// editor to show and nothing is written.
    public MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing = null) // :156
    {
        var old = replacing is { } oldID ? customsField.FirstOrDefault(custom => SwiftString.Equal(custom.Id, oldID)) : null; // :157
        var taken = new HashSet<string>(MCPCatalog.Entries.Select(entry => entry.Id), SwiftStringComparer.Instance); // :158
        foreach (var custom in customsField) taken.Add(custom.Id); // :159
        if (old is not null) taken.Remove(old.Id); // :160
        server = server with { Name = server.Name.Trimmed() }; // :161-162
        // :163-165 The id is made once, at creation, from the first name; a rename never moves it, so the
        // connection and the store's secrets stay where they are.
        server = server with { Id = old?.Id ?? MCPCustomServer.MakeID(server.Name) }; // :165
        if (MCPCustomError.Validate(server, taken) is { } error) return error; // :166

        if (probes.Remove(server.Id, out var existing)) existing.Cancel(); // :167-168
        Drafts.Remove(server.Id); // :169
        var index = customsField.FindIndex(custom => SwiftString.Equal(custom.Id, server.Id)); // :170
        if (index >= 0) customsField[index] = server; // :170-171
        else customsField.Add(server); // :173
        SaveCustoms(); // :175

        // :176-179 The definition changed under any connection it had: the tools and the provider configs
        // still describe the old command, address or variables, so both go now. The server leaves the
        // providers' configs (it is no longer connected) until the editor's re-probe succeeds, and the probe
        // runs after the values are saved.
        if (Connection(server.Id) is { } connection) // :180
        {
            connection = connection with { ConnectedAt = null, ServerVersion = null, Tools = ValueList<MCPToolSummary>.Empty }; // :181-183
            connectionsField[server.Id] = connection; // :184
            statesField[server.Id] = new MCPConnectionState.NotConnected(); // :185
            Save(); // :186
            ExportAll(); // :187
        }
        return null; // :189
    }

    /// :192-199 Removes a custom server: its connection and secrets go with it.
    public void DeleteCustom(MCPCustomServer server)
    {
        if (customsField.FirstOrDefault(custom => SwiftString.Equal(custom.Id, server.Id)) is { } entry) // :194
            Disconnect(entry.CatalogEntry()); // :195
        customsField.RemoveAll(custom => SwiftString.Equal(custom.Id, server.Id)); // :197
        SaveCustoms(); // :198
    }

    /// :201-206 A variable's stored value, for the editor to prefill: the store's when the variable is
    /// secret, the connection's otherwise.
    public string? StoredValue(string id, string field, bool secret) =>
        secret ? MCPKeychain.Value(id, field) : Connection(id)?.Values.GetValueOrDefault(field); // :204-205

    /// :208-241 Saves a custom's variable values where connect would put them: plain ones in the connection
    /// (mcp.json), secret ones in protected storage. A variable that moved between the two keeps its saved
    /// value and the stale copy in the other store is removed. Empty values clear the field; the editor
    /// supplies the stored value for fields the user has not changed.
    public bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf) // :215
    {
        foreach (var item in values.Where(item => item.IsSecret)) // :216
            if (!MCPKeychain.Set(item.Value, id, item.Key)) return false; // :217

        var kept = new HashSet<string>(values.Select(item => item.Key), SwiftStringComparer.Instance); // :219
        var connection = Connection(id) ?? new MCPConnection(id); // :220

        // :222-224 Fields the edit no longer declares: their plain values go, their secrets too.
        foreach (var field in (replacingFieldsOf?.Fields ?? ValueList<MCPField>.Empty).Where(field => !kept.Contains(field.Key)))
            MCPKeychain.Delete(id, field.Key); // :223

        connection = connection with { Values = new ValueMap<string, string>(connection.Values.Where(pair => kept.Contains(pair.Key)), SwiftStringComparer.Instance) }; // :225

        foreach (var item in values) // :226
        {
            var resolved = item.Value; // :227
            if (item.IsSecret) // :228
            {
                connection = connection with { Values = connection.Values.Mutating(map => map.Remove(item.Key)) }; // :229
            }
            else
            {
                connection = connection with
                {
                    Values = resolved.Length == 0
                        ? connection.Values.Mutating(map => map.Remove(item.Key)) // :231 nil clears it
                        : connection.Values.Mutating(map => map[item.Key] = resolved),
                };
                MCPKeychain.Delete(id, item.Key); // :232
            }
        }

        // :235-236 A server whose values are all empty needs no connection yet: it gets one on connect.
        if (Connection(id) is not null || connection.Values.Count > 0) // :236
        {
            connectionsField[id] = connection; // :237
            Save(); // :238
        }
        return true; // :240
    }

    // MARK: Persistence (:243)

    private void Load() // :245-260
    {
        if (SwiftTry.Optional(() => File.ReadAllBytes(MCPPaths.CustomsURL)) is { } customData && // :246
            SwiftTry.Optional(() => JsonSerializer.Deserialize<List<MCPCustomServer>>(customData, CustomOptions)) is { } list) // :247
        {
            customsField = list; // :248
        }
        var data = SwiftTry.Optional(() => File.ReadAllBytes(MCPPaths.ConnectionsURL)); // :250
        if (data is null) return;
        var connections = SwiftTry.Optional(() => JsonSerializer.Deserialize<List<MCPConnection>>(data, ConnectionOptions)); // :253
        if (connections is null) return;
        foreach (var connection in connections) // :254
        {
            connectionsField[connection.CatalogID] = connection; // :255
            if (connection.ConnectedAt is not null) // :256
                statesField[connection.CatalogID] = new MCPConnectionState.Connected(connection.Tools.Count); // :257
        }
    }

    private void Save() // :262-269
    {
        var list = connectionsField.Values.OrderBy(connection => connection.CatalogID, SwiftStringComparer.Instance).ToList(); // :266
        var data = SwiftTry.Optional(() => SwiftJson.Encode(list, ConnectionOptions, SwiftJsonStyle.PrettySorted)); // :263-267
        if (data is null) return;
        SwiftTry.Ignore(() => FileOps.WriteAtomic(MCPPaths.ConnectionsURL, data)); // :268
    }

    private void SaveCustoms() // :271-276
    {
        var data = SwiftTry.Optional(() => SwiftJson.Encode(customsField, CustomOptions, SwiftJsonStyle.PrettySorted)); // :273-274
        if (data is null) return;
        SwiftTry.Ignore(() => FileOps.WriteAtomic(MCPPaths.CustomsURL, data)); // :275
    }

    private static readonly JsonSerializerOptions ConnectionOptions = new() { Converters = { new MCPConnectionConverter() } };
    private static readonly JsonSerializerOptions CustomOptions = new() { Converters = { new MCPCustomServerConverter() } };

    /// :278-290 Rewrites every provider's config from enabledServers and runs the one-time external-CLI cleanup.
    private void ExportAll() // :279
    {
        var servers = EnabledServers; // :280
        // :281-282 The proxy's routes follow the enabled list: one per signed-in server.
        MCPProxy.Shared.Current.ReplaceAll(servers // :282-285
            .Where(server => server.OauthUpstream is not null)
            .Select(server => (ServerID: server.Id, Upstream: server.OauthUpstream!, Headers: (IReadOnlyDictionary<string, string>)ValueMap<string, string>.Empty, UsesOAuth: true))
            .ToList());
        MCPProviderConfig.WriteAll(servers); // :286
        MCPExternalSync.Sync(servers); // :287
        // :288-289 The in-app clients (API providers) pick the new list up on their next use.
        MainActor.Spawn(async () => await MCPHub.Shared.Reload());
    }

    /// :292-301 Non-secret values from the connection, overlaid with the store's secrets.
    private ValueMap<string, string> StoredValues(MCPCatalogEntry entry) // :293
    {
        var values = Connection(entry.Id)?.Values ?? ValueMap<string, string>.Empty; // :294
        foreach (var field in entry.Fields.Where(field => field.FieldKind == MCPField.Kind.Secret)) // :295
            if (MCPKeychain.Value(entry.Id, field.Key) is { } secret) // :296
                values = values.Mutating(map => map[field.Key] = secret); // :297
        return values; // :300
    }

    /// :303-314 A server that signs in: the browser flow first (Marrow's own, with its branded page), then
    /// its route on the proxy, and only then the probe, which goes through that route like every provider will.
    private static async Task<MCPProbeResult> ProbeOnce(MCPCatalogEntry entry, MCPResolvedServer resolved) // :303
    {
        if (resolved.OauthUpstream is { } remote) // :307
        {
            if (!MCPOAuth.IsSignedIn(entry.Id)) // :308
                await MCPOAuth.SignIn(entry.Id, entry.Name, remote); // :309
            MCPProxy.Shared.Current.Register(entry.Id, remote, ValueMap<string, string>.Empty, usesOAuth: true); // :311
        }
        return await MCPProbe.Probe(resolved); // :313
    }

    private void StartProbe(MCPCatalogEntry entry) // :316-355
    {
        probes[entry.Id] = MainActor.Spawn(async () =>
        {
            try
            {
                var resolved = MCPResolver.Resolve(entry, StoredValues(entry), MCPProxy.Url); // :319
                MCPProbeResult result; // :320
                try
                {
                    result = await ProbeOnce(entry, resolved); // :322
                }
                catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.UnauthorizedOAuth && resolved.OauthUpstream is not null) // :323
                {
                    SwiftTask.CheckCancellation(); // :324
                    MCPOAuth.SignOut(entry.Id); // :325
                    MCPProxy.Shared.Current.Unregister(entry.Id); // :326
                    result = await ProbeOnce(entry, resolved); // :327
                }
                catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.Http(401 or 403, _) && resolved.OauthUpstream is not null) // :328
                {
                    SwiftTask.CheckCancellation(); // :329
                    MCPOAuth.SignOut(entry.Id); // :330
                    MCPProxy.Shared.Current.Unregister(entry.Id); // :331
                    result = await ProbeOnce(entry, resolved); // :332
                }
                SwiftTask.CheckCancellation(); // :334
                if (Connection(entry.Id) is { } connection) // :335
                {
                    connection = connection with
                    {
                        ConnectedAt = SwiftDate.Now, // :336
                        ServerVersion = result.ServerVersion, // :337
                        Tools = result.Tools, // :338
                    };
                    connectionsField[entry.Id] = connection; // :339
                }
                statesField[entry.Id] = new MCPConnectionState.Connected(result.Tools.Count); // :341
                Save(); // :342
                ExportAll(); // :343
            }
            catch (CancellationError) // :344
            {
            }
            catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.NeedsSetup(var guide)) // :345
            {
                if (SwiftTask.IsCancelled) return; // :346
                // :347-348 The token is good; the server needs a step on its side. Keep the sign-in so Check
                // again re-probes without another browser round trip.
                statesField[entry.Id] = new MCPConnectionState.NeedsSetup(guide); // :349
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :350
            {
                if (SwiftTask.IsCancelled) return; // :351
                statesField[entry.Id] = new MCPConnectionState.Failed(SwiftErrors.LocalizedDescription(error)); // :352
            }
        });
    }
}
