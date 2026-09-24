using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Store;

// Source: DroppyCode/Services/Providers/CodexSession.swift (B2 1fbf81e), :1-1052.
//
// Unit Services.Providers.CodexSession part 1 of 4 (PR-PROVCLI-065 to 088) implements: the executable guard
// (:66-67), the connect handshake with its fixed "app-server" argv, bare-line framing with no jsonrpc member,
// the Marrow client identity and its 20 second watchdog (:502-528), start()'s resume/new-thread paths and its
// deferred close on a failed start (:66-108), requestThread's MCP-server retry (:110-127), send() (:129-160),
// interrupt() (:162-168), compact() (:170-173), rollback()'s paging/confirm path (:175-222, not the
// paginated-thread fork fallback), threadParameters and its Hydra, anti-slop and MCP-config assembly
// (:224-266) and policySettings' four-mode table (:1040-1051).
//
// forkHistory (:268-301, PR-PROVCLI-089), handleNotification (:532-659, PR-PROVCLI-107/108) and handleRequest
// (:909-988, PR-PROVCLI-124 to 129) belong to later parts of this unit and are left as stubs here so this part
// compiles; nothing this part's own connect() wiring can trigger reaches them (the stand-ins this part's tests
// drive only ever reply to requests, never send an unsolicited notification or server request). handleClose
// (:1032-1038, PR-PROVCLI-131) is ported here instead, in part 1, for the same reason ClaudeSession's DidClose
// was: Start()'s own connect() wires OnClose to it, and a connection this part opens can close on ANY of its
// own paths (a failed handshake, a failed thread/start or thread/resume, a watchdog timeout, or simply the
// stand-in exiting), so a stub here would throw out of this part's own tests, not just a later part's. It only
// needs the two fields its own body reads (pendingRequests, isStopping), both left empty/false by every member
// this part implements, which is exactly the Swift behaviour for a session that received no approval or
// question request and was never stopped.
namespace Marrow.Services.Providers;

// :11-1052. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class CodexSession : ProviderSession
{
    // :22-26. R-ENUM-2: associated-value enum -> abstract record with nested sealed records. Only the type is
    // needed by this part (HandleClose reads pendingRequests' keys only); the cases are populated by
    // handleRequest and consumed by ResolveApproval/AnswerQuestion, both later parts.
    private abstract record PendingRequest
    {
        private PendingRequest() { }
        public sealed record Decision(RPCID Id, ValueMap<string, JSONValue> Payloads) : PendingRequest; // :23
        public sealed record Permissions(RPCID Id, JSONValue Requested) : PendingRequest;                // :24
        public sealed record Question(RPCID Id) : PendingRequest;                                        // :25
    }

    // :28-33
    private sealed record PolicySettings(JSONValue ApprovalPolicy, JSONValue Sandbox, JSONValue SandboxPolicy, JSONValue Reviewer);

    // :17-20. R-ENUM-2: associated-value enum -> abstract record with nested sealed records.
    private abstract record Target
    {
        private Target() { }
        public sealed record Lead : Target;             // :18
        public sealed record Head(string Id) : Target;  // :19
    }

    private static readonly Logger Log = new(Logger.Subsystem, "codex"); // :13

    private readonly SessionConfiguration configuration; // :35
    private JSONRPCConnection? connection;                // :36
    private string? threadID;                             // :37
    private string? turnID;                               // :38
    private string? activeModel;                           // :39
    private readonly Dictionary<string, PendingRequest> pendingRequests = new(StringComparer.Ordinal); // :40
    private bool isStopping; // :41
    private TokenSpendTracker spendTracker = new();                          // :43
    private readonly HashSet<string> headThreads = new(StringComparer.Ordinal);       // :46
    private readonly Dictionary<string, string> headTurns = new(StringComparer.Ordinal); // :47
    private readonly Dictionary<string, TokenSpendTracker> headSpend = new(StringComparer.Ordinal); // :48
    private readonly Dictionary<string, string> headModels = new(StringComparer.Ordinal); // :51
    private Action<ProviderEvent>? leadSinkField; // :53 backs the lazily created LeadSink below
    private Action<ProviderEvent> LeadSink => leadSinkField ??= e => OnEvent?.Invoke(e); // :53

    // :36-45. U+00B7 MIDDLE DOT, used to join a limit's title with its snapshot name (:420) and a head's
    // nickname with its role (:690, :699).
    private const string Dot = "\U000000B7"; // U+00B7 MIDDLE DOT

    // :55-57
    public CodexSession(SessionConfiguration configuration) => this.configuration = configuration;

    public Action<ProviderEvent>? OnEvent { get; set; } // :14

    // :59-62
    public bool IsRunning => connection is { } c && !c.IsClosed;

    // :64
    private string WorkingDirectory => configuration.WorkingDirectory.Path;

    // :66-108
    public async Task<string> Start()
    {
        if (configuration.Executable is not { } executable)
        {
            throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider)); // :67
        }

        var connected = await Connect(
            executable,
            configuration.WorkingDirectory,
            configuration.Environment,
            c =>
            {
                c.OnNotification = HandleNotification; // :73
                c.OnRequest = HandleRequest;            // :74
                c.OnClose = HandleClose;                // :75
            });
        connection = connected; // :77
        var started = false;    // :78
        try
        {
            var parameters = ThreadParameters; // :85

            if (configuration.ResumeID is { } resumeID) // :87
            {
                var resume = new Dictionary<string, JSONValue>(parameters, StringComparer.Ordinal)
                {
                    ["threadId"] = resumeID,       // :89
                    ["excludeTurns"] = true,        // :90
                };
                var result = await RequestThread(connected, "thread/resume", resume); // :91
                if (result["thread"]?["id"]?.JSONValueString is not { } id || !SwiftString.Equal(id, resumeID)) // :92
                {
                    throw new ProviderError(new ProviderError.Case.Failed("Codex could not resume the original conversation.")); // :93
                }

                threadID = id;                                       // :95
                activeModel = result["model"]?.JSONValueString;      // :96
                started = true;                                      // :97
                return id;                                           // :98
            }

            var newResult = await RequestThread(connected, "thread/start", parameters); // :100
            if (newResult["thread"]?["id"]?.JSONValueString is not { } newID) // :101
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not start a thread.")); // :102
            }

            threadID = newID;                                        // :104
            activeModel = newResult["model"]?.JSONValueString;       // :105
            started = true;                                          // :106
            return newID;                                            // :107
        }
        finally
        {
            if (!started) // :80
            {
                connected.Close(); // :81
                connection = null; // :82
            }
        }
    }

    // :110-127
    private async Task<JSONValue> RequestThread(JSONRPCConnection connection, string method, Dictionary<string, JSONValue> parameters)
    {
        try
        {
            return await connection.Request(method, new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :112
        }
        catch (RPCError error) when (
            (error.Message.Contains("failed to load configuration", StringComparison.Ordinal) ||
             error.Message.Contains("invalid transport", StringComparison.Ordinal)) &&
            parameters.TryGetValue("config", out var configValue) && configValue["mcp_servers"] is not null) // :113
        {
            var retried = new Dictionary<string, JSONValue>(parameters, StringComparer.Ordinal); // :114
            if (retried.TryGetValue("config", out var configObject) && configObject.JSONValueObject is { } configDictionary) // :115
            {
                var config = new Dictionary<string, JSONValue>(configDictionary, StringComparer.Ordinal);
                config.Remove("mcp_servers"); // :116
                if (config.Count == 0)
                {
                    retried.Remove("config"); // :117-118
                }
                else
                {
                    retried["config"] = new JSONValue.Object(new ValueMap<string, JSONValue>(config)); // :119-120
                }
            }

            var result = await connection.Request(method, new JSONValue.Object(new ValueMap<string, JSONValue>(retried))); // :123
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
            {
                NoticeLevel = Notice.Level.Warning,
                Message = "Codex refused the MCP server configuration, so this session runs without MCP servers. Check MCP in Settings.", // :124
            }));
            return result; // :125
        }
    }

    // :129-160
    public async Task Send(TurnInput input)
    {
        if (connection is not { IsClosed: false } connected || threadID is not { } currentThreadID) // :130
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        var content = new List<JSONValue>(
            input.Command is { } command
                ? command.CodexInput(input.Text) // :131
                : [new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                  {
                      ["type"] = "text",
                      ["text"] = input.Text,
                  }))]);
        foreach (var image in input.Images) // :132
        {
            if (image.IsImage)
            {
                content.Add(new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["type"] = "localImage",
                    ["path"] = image.Path,
                }))); // :133
            }
        }

        var policy = PolicySettingsFor(input.RuntimeMode); // :135
        var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal)
        {
            ["threadId"] = currentThreadID,                                      // :137
            ["input"] = new JSONValue.Array(new ValueList<JSONValue>(content)),   // :138
            ["approvalPolicy"] = policy.ApprovalPolicy,                          // :139
            ["approvalsReviewer"] = policy.Reviewer,                             // :140
            ["sandboxPolicy"] = policy.SandboxPolicy,                            // :141
            ["summary"] = "auto",                                                // :142
        };

        var effort = input.Effort is { Length: > 0 } inputEffort ? inputEffort : null; // :144
        if (effort is not null)
        {
            parameters["effort"] = effort; // :145
        }

        if (input.ServiceTier is { } tier) // :146
        {
            parameters["serviceTierForTurn"] = tier;
        }

        if ((input.Model ?? activeModel) is { } model) // :147
        {
            parameters["model"] = model;
            parameters["collaborationMode"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["mode"] = input.InteractionMode == InteractionMode.Plan ? "plan" : "default",
                ["settings"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["model"] = model,
                    ["reasoning_effort"] = JSONValue.Optional(effort),
                    ["developer_instructions"] = new JSONValue.Null(),
                })),
            })); // :149-156
        }

        var result = await connected.Request("turn/start", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :158
        if (result["turn"]?["id"]?.JSONValueString is { } id)
        {
            turnID = id; // :159
        }
    }

    // :162-168
    public async Task Interrupt()
    {
        if (connection is not { } connected || threadID is not { } currentThreadID || turnID is not { } currentTurnID) // :163
        {
            return;
        }

        await SwiftTry.IgnoreAsync(async () => // :164
        {
            _ = await connected.Request("turn/interrupt", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = currentThreadID, // :165
                ["turnId"] = currentTurnID,     // :166
            })));
        });
    }

    // :170-173
    public async Task Compact()
    {
        if (connection is not { } connected || threadID is not { } currentThreadID) // :171
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        _ = await connected.Request("thread/compact/start", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["threadId"] = currentThreadID, // :172
        })));
    }

    // :175-222. Swift: rollback(from:). The paginated-thread fork fallback (:211-213, forkHistory :268-301) is
    // PR-PROVCLI-089, part 2 of this unit; ForkHistory below is a stub until then.
    public async Task<string?> Rollback(string targetID)
    {
        if (connection is not { } connected || threadID is not { } currentThreadID) // :176
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        var watchdog = MainActor.Spawn(async () => // :177-180
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(30));
            if (!SwiftTask.IsCancelled)
            {
                connected.Close();
            }
        });
        try
        {
            string? cursor = null; // :182
            var count = 0;         // :183
            var found = false;     // :184
            string? previousID = null; // :185

            do // :186
            {
                var page = await connected.Request("thread/turns/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = currentThreadID,       // :188
                    ["cursor"] = JSONValue.Optional(cursor),
                    ["limit"] = 100,                       // :189
                    ["sortDirection"] = "desc",
                    ["itemsView"] = "notLoaded",
                })));

                if (page["data"]?.JSONValueArray is not { } turns) // :191
                {
                    throw new ProviderError(new ProviderError.Case.Failed("Codex did not return its conversation history.")); // :192
                }

                foreach (var turn in turns) // :194
                {
                    if (turn["id"]?.JSONValueString is not { } id) // :195
                    {
                        throw new ProviderError(new ProviderError.Case.Failed("Codex returned a turn without an ID."));
                    }

                    if (found) // :196
                    {
                        previousID = id;
                        break;
                    }

                    count++; // :197
                    if (SwiftString.Equal(id, targetID)) // :198
                    {
                        found = true;
                    }
                }

                var next = page["nextCursor"]?.JSONValueString; // :200
                if (next is not null && cursor is not null && SwiftString.Equal(next, cursor)) // :201
                {
                    throw new ProviderError(new ProviderError.Case.Failed("Codex could not page its conversation history."));
                }

                cursor = next; // :202
            }
            while (cursor is not null && previousID is null); // :203

            if (!found) // :204
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex could not find the selected turn. The conversation was kept."));
            }

            JSONValue result; // :205
            try
            {
                result = await connected.Request("thread/rollback", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = currentThreadID, // :208
                    ["numTurns"] = (long)count,      // :209
                })));
            }
            catch (RPCError error) when (error.Message.Contains("paginated threads do not support thread/rollback", StringComparison.Ordinal)) // :211
            {
                return await ForkHistory(targetID, previousID, connected, currentThreadID); // :212
            }

            var resultThreadID = result["thread"]?["id"]?.JSONValueString;
            var remainingTurns = result["thread"]?["turns"]?.JSONValueArray;
            var confirmed =
                resultThreadID is not null && SwiftString.Equal(resultThreadID, currentThreadID) && // :214
                remainingTurns is not null && // :215
                !remainingTurns.Any(turn => turn["id"]?.JSONValueString is { } tid && SwiftString.Equal(tid, targetID)) && // :216
                LastIDEquals(remainingTurns, previousID); // :217
            if (!confirmed)
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not confirm the requested conversation rollback.")); // :218
            }

            turnID = null;           // :220
            return currentThreadID;  // :221
        }
        finally
        {
            watchdog.Cancel(); // :181
        }
    }

    // Swift optional equality of the last remaining turn's id against previousID (:216-217): both nil is a
    // match, one nil and the other not is never a match.
    private static bool LastIDEquals(IReadOnlyList<JSONValue> turns, string? previousID)
    {
        var lastID = turns.Count > 0 ? turns[^1]["id"]?.JSONValueString : null;
        return lastID is null ? previousID is null : previousID is not null && SwiftString.Equal(lastID, previousID);
    }

    // :268-301. PR-PROVCLI-089.
    private async Task<string?> ForkHistory(string targetID, string? previousID, JSONRPCConnection connection, string originalID)
    {
        if (previousID is not { } previous) // :269
        {
            _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = originalID, // :270
            })));
            threadID = null; // :271
            turnID = null;    // :272
            return null;      // :273
        }

        var parameters = ThreadParameters; // :275
        parameters["threadId"] = originalID; // :276
        parameters["lastTurnId"] = previous;  // :277
        parameters["excludeTurns"] = true;    // :278
        var result = await connection.Request("thread/fork", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :279
        if (result["thread"]?["id"]?.JSONValueString is not { } id || SwiftString.Equal(id, originalID)) // :280
        {
            throw new ProviderError(new ProviderError.Case.Failed("Codex did not create a conversation at the revert point.")); // :281
        }

        try // :283-296
        {
            var page = await connection.Request("thread/turns/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = id, // :285
                ["limit"] = 1,
                ["sortDirection"] = "desc",
                ["itemsView"] = "notLoaded",
            })));
            var turns = page["data"]?.JSONValueArray; // :287
            var firstID = turns is { Count: > 0 } ? turns[0]["id"]?.JSONValueString : null; // :288
            if (firstID is null || !SwiftString.Equal(firstID, previous) || SwiftString.Equal(firstID, targetID)) // :288-289
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not confirm the reverted conversation history.")); // :290
            }
            _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = originalID, // :292
            })));
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :293
        {
            await SwiftTry.IgnoreAsync(async () => // :294 try?
            {
                _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = id,
                })));
            });
            throw; // :295
        }

        threadID = id;         // :297
        turnID = null;          // :298
        activeModel = result["model"]?.JSONValueString ?? activeModel; // :299
        return id;               // :300
    }

    // :224-266
    private Dictionary<string, JSONValue> ThreadParameters
    {
        get
        {
            var policy = PolicySettingsFor(configuration.RuntimeMode); // :225
            var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal)
            {
                ["cwd"] = WorkingDirectory,               // :227
                ["approvalPolicy"] = policy.ApprovalPolicy, // :228
                ["sandbox"] = policy.Sandbox,               // :229
                ["approvalsReviewer"] = policy.Reviewer,    // :230
            };
            if (configuration.Model is { } model) // :232
            {
                parameters["model"] = model;
            }

            var config = new Dictionary<string, JSONValue>(StringComparer.Ordinal); // :233
            if (configuration.Hydra is { } hydra) // :234
            {
                if (hydra.RunsNatively) // :235
                {
                    foreach (var pair in HydraPrompts.CodexConfig(hydra)) // :236, hydra wins every collision
                    {
                        config[pair.Key] = pair.Value;
                    }

                    parameters["developerInstructions"] = HydraPrompts.Policy(ProviderKind.Codex, hydra); // :237
                }
                else
                {
                    config["features"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["multi_agent"] = false, // :244
                    }));
                    parameters["developerInstructions"] = HydraPrompts.FallbackPolicy(hydra); // :245
                }
            }

            if (config.Count > 0) // :248
            {
                parameters["config"] = new JSONValue.Object(new ValueMap<string, JSONValue>(config));
            }

            var existingInstructions = parameters.TryGetValue("developerInstructions", out var instructions) // :252
                ? instructions.JSONValueString
                : null;
            var joined = new List<string>();
            if (existingInstructions is not null) joined.Add(existingInstructions);
            joined.Add(AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled));
            parameters["developerInstructions"] = string.Join("\n\n", joined); // :253-257

            if (MCPProviderConfig.CodexOverrides() is { } servers) // :260
            {
                var mergedConfig = parameters.TryGetValue("config", out var currentConfig) && currentConfig.JSONValueObject is { } currentObject
                    ? new Dictionary<string, JSONValue>(currentObject, StringComparer.Ordinal)
                    : new Dictionary<string, JSONValue>(StringComparer.Ordinal); // :261
                mergedConfig["mcp_servers"] = servers; // :262
                parameters["config"] = new JSONValue.Object(new ValueMap<string, JSONValue>(mergedConfig)); // :263
            }

            return parameters; // :265
        }
    }

    // :303-318. PR-PROVCLI-090, 091, 092.
    public void ResolveApproval(string requestID, string optionID)
    {
        if (!pendingRequests.Remove(requestID, out var pending)) return; // :304
        switch (pending)
        {
            case PendingRequest.Decision(var id, var payloads):
                connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["decision"] = payloads.TryGetValue(optionID, out var payload) ? payload : "cancel", // :307
                })));
                break;
            case PendingRequest.Permissions(var id, var requested):
                if (optionID == "turn" || optionID == "session") // :309
                {
                    connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["permissions"] = requested, // :310
                        ["scope"] = optionID,
                    })));
                }
                else
                {
                    connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["permissions"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty), // :312
                        ["scope"] = "turn",
                    })));
                }
                break;
            case PendingRequest.Question(var id):
                connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["answers"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty), // :315
                })));
                break;
            default:
                throw new UnreachableException();
        }
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :317
    }

    // :320-328. PR-PROVCLI-093.
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers)
    {
        if (!pendingRequests.Remove(requestID, out var pending) || pending is not PendingRequest.Question(var id)) return; // :321
        var payload = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        foreach (var (questionID, values) in answers) // :323
        {
            payload[questionID] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["answers"] = new JSONValue.Array(new ValueList<JSONValue>(values.Select(v => (JSONValue)new JSONValue.String(v)).ToList())), // :324
            }));
        }
        connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["answers"] = new JSONValue.Object(new ValueMap<string, JSONValue>(payload)), // :326
        })));
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :327
    }

    // :330-333. PR-PROVCLI-094.
    public void Stop()
    {
        isStopping = true;    // :331
        connection?.Close();   // :332
    }

    // :335-341. PR-PROVCLI-095.
    public async Task<bool> StopAgent(string id)
    {
        if (connection is not { } connected || !headThreads.Contains(id) || !headTurns.TryGetValue(id, out var turn)) return false; // :336
        try // :337 (try? await connection.request(...)) != nil
        {
            _ = await connected.Request("turn/interrupt", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = id, // :338
                ["turnId"] = turn, // :339
            })));
            return true; // :340
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return false; // :340
        }
    }

    // :345-353. PR-PROVCLI-096.
    public static async Task<ValueList<SlashCommand>> ListCommands(SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, directory, environment, _ => { }); // :346
        var watchdog = MainActor.Spawn(async () => // :347-349
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(20));
            if (!SwiftTask.IsCancelled) connection.Close();
        });
        try
        {
            var result = await connection.Request("skills/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["cwds"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String(directory.Path)])), // :352
                ["forceReload"] = true,
            })));
            return SlashCommand.CodexSkills(result); // :352
        }
        finally
        {
            watchdog.Cancel();  // :351 defer
            connection.Close(); // :351 defer
        }
    }

    // :355-385. PR-PROVCLI-097.
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :356-361
        try
        {
            var models = new List<ModelOption>(); // :363
            JSONValue cursor = new JSONValue.Null(); // :364
            for (var i = 0; i < 10; i++) // :365
            {
                var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
                if (!cursor.IsNull) parameters["cursor"] = cursor; // :367
                var result = await connection.Request("model/list", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :368
                foreach (var model in result["data"]?.JSONValueArray ?? [])
                {
                    if (model["hidden"]?.JSONValueBool == true) continue; // :369
                    if (model["id"]?.JSONValueString is not { } id) continue; // :370
                    var efforts = (model["supportedReasoningEfforts"]?.JSONValueArray ?? [])
                        .Select(e => e["reasoningEffort"]?.JSONValueString)
                        .Where(e => e is not null).Select(e => e!).ToList(); // :375
                    models.Add(new ModelOption(
                        id,
                        model["displayName"]?.JSONValueString ?? id, // :373
                        detail: model["description"]?.JSONValueString, // :374
                        efforts: new ValueList<string>(efforts),
                        defaultEffort: model["defaultReasoningEffort"]?.JSONValueString, // :376
                        isDefault: model["isDefault"]?.JSONValueBool ?? false, // :377
                        fastTier: FastTier(model["serviceTiers"]?.JSONValueArray ?? []))); // :378
                }
                cursor = result["nextCursor"] ?? new JSONValue.Null(); // :381
                if (cursor.IsNull) break; // :382
            }
            return new ValueList<ModelOption>(models); // :384
        }
        finally
        {
            connection.Close(); // :362
        }
    }

    // :493-500. PR-PROVCLI-098.
    private static string? FastTier(IReadOnlyList<JSONValue> tiers)
    {
        foreach (var tier in tiers) // :495 lazy compactMap.first
        {
            if (tier["id"]?.JSONValueString is not { } id) continue; // :496
            var label = SwiftString.Lowercased($"{id} {tier["name"]?.JSONValueString ?? ""}"); // :497
            if (label.Contains("fast", StringComparison.Ordinal) || label.Contains("priority", StringComparison.Ordinal)) return id; // :498
        }
        return null; // :499
    }

    // :388-437. PR-PROVCLI-099, 101, 102, 103.
    public static async Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :389-394
        try
        {
            JSONValue result; // :396
            try
            {
                result = await connection.Request("account/rateLimits/read"); // :398
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :399
            {
                return new PlanLimits { PlanName = null, Windows = ValueList<PlanLimits.Window>.Empty, Problem = PlainProblem(error) }; // :400-404
            }

            var snapshots = new List<JSONValue>(); // :406
            if (result["rateLimitsByLimitId"]?.JSONValueObject is { Count: > 0 } byID) // :407
            {
                foreach (var key in byID.Keys.OrderBy(k => k, SwiftStringComparer.Instance)) // :408 (R-COLL-1)
                {
                    if (byID.TryGetValue(key, out var snapshot)) snapshots.Add(snapshot);
                }
            }
            else if (result["rateLimits"] is { IsNull: false } single) // :409-410
            {
                snapshots.Add(single);
            }

            string? plan = null; // :412
            var windows = new List<PlanLimits.Window>(); // :413
            foreach (var snapshot in snapshots) // :414
            {
                plan ??= snapshot["planType"]?.JSONValueString; // :415
                var name = snapshots.Count > 1 ? snapshot["limitName"]?.JSONValueString : null; // :416
                foreach (var key in new[] { "primary", "secondary" }) // :417
                {
                    if (snapshot[key] is not { } window || window["usedPercent"]?.JSONValueDouble is not { } percent) continue; // :418
                    var title = PlanLimitsReader.WindowTitle(window["windowDurationMins"]?.JSONValueInt); // :419
                    if (name is { } limitName) title += " " + Dot + " " + limitName; // :420
                    windows.Add(new PlanLimits.Window
                    {
                        Id = $"{snapshot["limitId"]?.JSONValueString ?? "codex"}-{key}", // :422
                        Title = title,                                                    // :423
                        Percent = percent,                                                // :424
                        ResetsAt = window["resetsAt"]?.JSONValueDouble is { } resets ? SwiftDate.From1970Seconds(resets) : null, // :425
                    });
                }
            }

            if (windows.Count == 0) // :429
            {
                return new PlanLimits
                {
                    PlanName = PlanLimitsReader.CodexPlanName(plan),                          // :431
                    Windows = ValueList<PlanLimits.Window>.Empty,                              // :432
                    Problem = "Codex reported no usage windows for this account.",             // :433
                };
            }
            return new PlanLimits
            {
                PlanName = PlanLimitsReader.CodexPlanName(plan),                                                          // :436
                Windows = new ValueList<PlanLimits.Window>(windows),
                PlanLimitsResetCredits = ParseResetCredits(result["rateLimitResetCredits"] ?? result["rate_limit_reset_credits"]), // :436
            };
        }
        finally
        {
            connection.Close(); // :395
        }
    }

    // :439-445. PR-PROVCLI-100.
    private static string PlainProblem(Exception error)
    {
        var text = TextCleanup.SingleLine(DescribeError(error), limit: 300); // :440
        if (text.Contains("token_expired", StringComparison.Ordinal) || text.Contains("401", StringComparison.Ordinal)) // :441
        {
            return "Codex's stored login has expired. Run `codex login` in a terminal, then check again."; // :442
        }
        return text.Length == 0 ? "Codex could not be asked for its usage." : text; // :443-444
    }

    // ENGINEERING DECISION: `String(describing: error)` for JSONRPC.swift's RPCError (a plain LocalizedError
    // struct with no CustomStringConvertible, JSONRPC.swift:31-36) is Swift's default struct-mirror dump. Every
    // throw reachable at the ReadPlanLimits call site (JSONRPC.swift:72, :131, :139) is an RPCError; a scripted
    // or real "error" reply never carries a "data" field, so data is always nil in practice.
    private static string DescribeError(Exception error) => error is RPCError rpc
        ? $"RPCError(code: {rpc.Code}, message: \"{rpc.Message}\", data: {(rpc.Data is null ? "nil" : rpc.Data.CompactString)})"
        : error.ToString();

    // :447-462. PR-PROVCLI-104.
    private static PlanLimits.ResetCredits? ParseResetCredits(JSONValue? raw)
    {
        if (raw is null) return null; // :449
        var count = raw["availableCount"]?.JSONValueInt ?? raw["available_count"]?.JSONValueInt; // :449
        if (count is not { } value) return null;
        var credits = new List<PlanLimits.ResetCredit>();
        foreach (var credit in raw["credits"]?.JSONValueArray ?? []) // :450
        {
            if (credit["id"]?.JSONValueString is not { Length: > 0 } id) continue; // :451
            var status = credit["status"]?.JSONValueString; // :452
            if (status is not null && status != "available" && status != "unknown") continue; // :453
            credits.Add(new PlanLimits.ResetCredit
            {
                Id = id,                                                                       // :455
                Title = credit["title"]?.JSONValueString,                                       // :456
                Detail = credit["description"]?.JSONValueString,                                // :457
                ExpiresAt = ResetDate(credit["expiresAt"] ?? credit["expires_at"]),              // :458
            });
        }
        return new PlanLimits.ResetCredits { AvailableCount = Math.Max(0L, value), Credits = new ValueList<PlanLimits.ResetCredit>(credits) }; // :461
    }

    // :464-473. PR-PROVCLI-105.
    private static DateTimeOffset? ResetDate(JSONValue? value)
    {
        if (value is null) return null; // :466
        var number = value.JSONValueDouble ?? (value.JSONValueString is { } text1 ? SwiftNumber.ParseDouble(text1) : null); // :467
        if (number is { } n)
        {
            if (!(n > 0)) return null; // :468
            return SwiftDate.From1970Seconds(n > 10_000_000_000 ? n / 1_000 : n); // :469
        }
        if (value.JSONValueString is not { } text) return null; // :471
        return ISO8601.Date(text); // :472
    }

    // :476-491. PR-PROVCLI-106.
    public static async Task<PlanLimits.ResetOutcome> ConsumeResetCredit(SwiftURL executable, IReadOnlyDictionary<string, string> environment, string? creditID)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :477-482
        try
        {
            var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal)
            {
                ["idempotencyKey"] = SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())), // :484
            };
            if (creditID is { } id) parameters["creditId"] = id; // :485
            var result = await connection.Request("account/rateLimitResetCredit/consume", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :486
            if (result["outcome"]?.JSONValueString is not { } outcomeText || SwiftEnum<PlanLimits.ResetOutcome>.FromRawValue(outcomeText) is not { } outcome) // :487
            {
                throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :488
            }
            return outcome; // :490
        }
        finally
        {
            connection.Close(); // :483
        }
    }

    // ENGINEERING DECISION: SwiftURL.Path's file:// branch (FileSystemPath, SwiftURL.cs:154-155) reproduces
    // macOS's POSIX url.path unconditionally, which corrupts a Windows drive-letter path (observed: "C:\Users\x"
    // -> "/C:/Users/x", which CreateProcessW's lpCurrentDirectory then rejects). The same shared, unfixed gap
    // sits behind ACPSession.cs's DirectoryFileUrl and Library.cs's Url, both of which build a "file://" URL the
    // same way; Marrow.Core/Port/SwiftURL.cs is not this unit's file to change. A non-"file" scheme sidesteps
    // the corrupting branch (SwiftURL.Path's non-file branch, SwiftURL.cs:126-128, just percent-decodes the
    // path component with no POSIX-root handling) - the same technique this file's own tests already use via
    // CodexSessionTests.FileUrl for every fixture path in this unit. Connect() below calls directory.Path to
    // build StdioProcess's working directory, so this needs a real, round-tripping Windows path.
    private static SwiftURL DirectoryFileUrl(string path)
    {
        var normalized = path.Replace('\\', '/');
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return SwiftURL.Create("marrow-directory-path:" + builder) ?? throw SwiftTraps.Fatal("Invalid temporary directory path.");
    }

    // FileManager.default.temporaryDirectory (:358, :391, :479): the Windows user temporary directory, the same
    // mapping PlanLimits.cs's TemporaryDirectory gives it (R-TEST-9: MARROW_PORT_DATA_DIR first).
    private static string TemporaryDirectory =>
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath();

    // :502-528
    private static async Task<JSONRPCConnection> Connect(
        SwiftURL executable,
        SwiftURL directory,
        IReadOnlyDictionary<string, string> environment,
        Action<JSONRPCConnection> configure)
    {
        var process = new StdioProcess(executable.Path, ["app-server"], directory.Path, environment); // :508
        var connection = new JSONRPCConnection(process, sendsVersion: false); // :509
        configure(connection); // :510
        connection.Start(); // :511
        var watchdog = MainActor.Spawn(async () => // :512-516
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(20));
            if (!SwiftTask.IsCancelled)
            {
                connection.Close();
            }
        });
        try
        {
            try
            {
                _ = await connection.Request("initialize", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> // :518-521
                {
                    ["clientInfo"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["name"] = "marrow",   // DEVIATION OD-0008 B11: upstream "droppy-code"
                        ["title"] = "Marrow",  // DEVIATION OD-0008 B11: upstream "Droppy Code"
                        ["version"] = AppInfo.Current.Version,
                    })),
                    ["capabilities"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["experimentalApi"] = true,
                    })),
                })));
            }
            catch
            {
                connection.Close(); // :523
                throw;              // :524
            }

            connection.Notify("initialized"); // :526
            return connection;                 // :527
        }
        finally
        {
            watchdog.Cancel(); // :516
        }
    }

    // :532-659. PR-PROVCLI-107, 108, 109, 110, 111, 112. itemStarted (:703-725) and itemCompleted (:727-749)
    // belong to a later part of this unit and stay stubbed below; nothing this part's own tests send an
    // "item/started" or "item/completed" notification, so the switch reaches those cases only in a later part.
    private void HandleNotification(string method, JSONValue parameters)
    {
        if (method == "thread/started") // :533-536
        {
            HeadStarted(parameters["thread"] ?? new JSONValue.Null());
            return;
        }

        Target target = new Target.Lead(); // :538
        if (parameters["threadId"]?.JSONValueString is { } eventThread && threadID is { } currentThreadID && !SwiftString.Equal(eventThread, currentThreadID)) // :539
        {
            if (!headThreads.Contains(eventThread)) return; // :540
            target = new Target.Head(eventThread); // :541
        }

        void Sink(ProviderEvent e) // :543-546
        {
            switch (target)
            {
                case Target.Lead: LeadSink(e); break;
                case Target.Head(var headID): OnEvent?.Invoke(new ProviderEvent.AgentEvent(headID, e)); break;
            }
        }

        switch (method)
        {
            case "turn/started": // :548-556
            {
                var id = parameters["turn"]?["id"]?.JSONValueString;
                switch (target)
                {
                    case Target.Lead:
                        turnID = id; // :552
                        break;
                    case Target.Head(var head):
                        if (id is null) headTurns.Remove(head); else headTurns[head] = id; // :554
                        break;
                }
                Sink(new ProviderEvent.TurnStarted(id)); // :556
                break;
            }
            case "item/started": // :557-558
                if (parameters["item"] is { } startedItem) ItemStarted(startedItem, target, Sink);
                break;
            case "item/completed": // :559-560
                if (parameters["item"] is { } completedItem) ItemCompleted(completedItem, target, Sink);
                break;
            case "item/agentMessage/delta": // :561-564
                if (parameters["itemId"]?.JSONValueString is { } msgID && parameters["delta"]?.JSONValueString is { } msgDelta)
                {
                    Sink(new ProviderEvent.MessageDelta(msgID, msgDelta));
                }
                break;
            case "item/reasoning/summaryTextDelta":
            case "item/reasoning/textDelta": // :565-568
                if (parameters["itemId"]?.JSONValueString is { } reasoningID && parameters["delta"]?.JSONValueString is { } reasoningDelta)
                {
                    Sink(new ProviderEvent.ReasoningDelta(reasoningID, reasoningDelta));
                }
                break;
            case "item/reasoning/summaryPartAdded": // :569-572
                if (parameters["itemId"]?.JSONValueString is { } partID && parameters["summaryIndex"]?.JSONValueInt is { } index && index > 0)
                {
                    Sink(new ProviderEvent.ReasoningDelta(partID, "\n\n"));
                }
                break;
            case "item/commandExecution/outputDelta": // :573-576
                if (parameters["itemId"]?.JSONValueString is { } outID && parameters["delta"]?.JSONValueString is { } outDelta)
                {
                    Sink(new ProviderEvent.ToolOutput(outID, outDelta));
                }
                break;
            case "item/plan/delta": // :577-580
                if (parameters["itemId"]?.JSONValueString is { } planID && parameters["delta"]?.JSONValueString is { } planDelta)
                {
                    Sink(new ProviderEvent.PlanDelta(planID, planDelta));
                }
                break;
            case "turn/plan/updated": // :581-591. PR-PROVCLI-109.
            {
                var steps = new List<TodoStep>();
                foreach (var step in parameters["plan"]?.JSONValueArray ?? [])
                {
                    if (step["step"]?.JSONValueString is not { } text) continue; // :583
                    var status = step["status"]?.JSONValueString switch // :585-588
                    {
                        "completed" => TodoStep.Status.Done,
                        "inProgress" => TodoStep.Status.Active,
                        _ => TodoStep.Status.Pending,
                    };
                    steps.Add(new TodoStep { Text = text, TodoStepStatus = status }); // :589
                }
                Sink(new ProviderEvent.Todos(new ValueList<TodoStep>(steps))); // :591
                break;
            }
            case "turn/diff/updated": // :592-593
                if (parameters["diff"]?.JSONValueString is { } diff) Sink(new ProviderEvent.Diff(diff));
                break;
            case "thread/tokenUsage/updated": // :594-626. PR-PROVCLI-110, 111.
                HandleTokenUsageUpdated(parameters, target);
                break;
            case "error": // :627-631. PR-PROVCLI-112.
                if (target is Target.Lead && parameters["willRetry"]?.JSONValueBool == true)
                {
                    var message = ErrorMessage(parameters["error"]); // :629
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
                    {
                        NoticeLevel = Notice.Level.Warning,
                        Message = message + " Retrying.", // :630
                    }));
                }
                break;
            case "turn/completed": // :632-650
                HandleTurnCompleted(parameters, target);
                break;
            case "serverRequest/resolved": // :651-654
                if (parameters["requestId"] is { } raw && RPCID.Create(raw) is { } resolvedID && pendingRequests.Remove(resolvedID.Key))
                {
                    OnEvent?.Invoke(new ProviderEvent.RequestResolved(resolvedID.Key));
                }
                break;
            case "thread/compacted": // :655-656
                Sink(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
                break;
            default: // :657-658
                break;
        }
    }

    // :594-625. PR-PROVCLI-110, 111.
    private void HandleTokenUsageUpdated(JSONValue parameters, Target target)
    {
        if (parameters["tokenUsage"] is not { } usage || usage["last"] is not { } last) return; // :595
        var used = last["totalTokens"]?.JSONValueInt ?? 0; // :596
        switch (target)
        {
            case Target.Lead:
                OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = usage["modelContextWindow"]?.JSONValueInt })); // :599
                if (usage["total"]?["totalTokens"]?.JSONValueInt is { } total && total > 0) // :607
                {
                    var (next, spend) = spendTracker.Spend(total, Math.Max(0, total - used)); // :608
                    spendTracker = next;
                    RecordCodexSpend(spend, threadID, activeModel); // :609
                }
                else
                {
                    RecordCodexSpend(used, threadID, activeModel); // :611
                }
                break;
            case Target.Head(var head):
                var headTotal = usage["total"]?["totalTokens"]?.JSONValueInt ?? 0; // :615
                var headModel = headModels.GetValueOrDefault(head); // :616
                if (headTotal > 0)
                {
                    var tracker = headSpend.TryGetValue(head, out var existing) ? existing : new TokenSpendTracker(); // :618
                    var (next, spend) = tracker.Spend(headTotal, Math.Max(0, headTotal - used)); // :619
                    headSpend[head] = next; // :620
                    RecordCodexSpend(spend, head, headModel, head); // :621
                }
                else
                {
                    RecordCodexSpend(used, head, headModel, head); // :623
                }
                OnEvent?.Invoke(new ProviderEvent.AgentProgress(head, null, null, headTotal > 0 ? headTotal : used, null)); // :625
                break;
        }
    }

    // :668-674
    private void RecordCodexSpend(long spend, string? rolloutID, string? model, string? agentID = null)
    {
        if (spend <= 0) return; // :669
        if (rolloutID is { Length: > 0 }) // :670
        {
            TokenLedger.Shared.Current.RecordCodexSpend(spend, rolloutID); // :671
        }
        OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Codex, spend, model: model, agentID: agentID))); // :673
    }

    // :632-650
    private void HandleTurnCompleted(JSONValue parameters, Target target)
    {
        var turn = parameters["turn"]; // :633
        var status = turn?["status"]?.JSONValueString switch // :634-638
        {
            "interrupted" => TurnStatus.Interrupted,
            "failed" => TurnStatus.Failed,
            _ => TurnStatus.Completed,
        };
        var error = turn?["error"] is { IsNull: false } errorValue ? ErrorMessage(errorValue) : null; // :639
        switch (target)
        {
            case Target.Lead:
                turnID = null; // :642
                if (status == TurnStatus.Failed && error is { } errorText && UsageLimitSignal.Matches(errorText)) // :643
                {
                    OnEvent?.Invoke(new ProviderEvent.UsageLimit(UsageLimitSignal.ResetTime(errorText))); // :644
                }
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(status, error)); // :646
                break;
            case Target.Head(var head):
                headTurns.Remove(head); // :648
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(head, status, error)); // :649
                break;
        }
    }

    // :676-701. Needed by handleNotification's thread/started routing (PR-PROVCLI-107).
    private void HeadStarted(JSONValue thread)
    {
        if (thread["id"]?.JSONValueString is not { } id || threadID is not { } currentThreadID || headThreads.Contains(id)) return; // :678
        var parent = thread["parentThreadId"]?.JSONValueString;
        if (parent is null || !SwiftString.Equal(parent, currentThreadID)) // :679
        {
            var nickname = thread["agentNickname"]?.JSONValueString; // :684
            var role = thread["agentRole"]?.JSONValueString;
            if (nickname is null && role is null) return; // :685
            Log.Error($"thread/started {id} names parent {parent ?? "none"}, not this lead {currentThreadID}; taken as a head by its agent nickname"); // :686
            headThreads.Add(id); // :687
            if (thread["model"]?.JSONValueString is { } headModel1) headModels[id] = headModel1; // :688
            var preview1 = thread["preview"]?.JSONValueString?.Trimmed() ?? ""; // :689
            var description1 = DescribeHead(preview1, nickname, role); // :690
            OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
            {
                Id = id,
                Description = description1,
                Prompt = preview1.Length > 0 ? preview1 : null,
                Model = thread["model"]?.JSONValueString,
            })); // :691
            return;
        }

        headThreads.Add(id); // :694
        if (thread["model"]?.JSONValueString is { } headModel2) headModels[id] = headModel2; // :695
        var nickname2 = thread["agentNickname"]?.JSONValueString; // :696
        var role2 = thread["agentRole"]?.JSONValueString; // :697
        var preview2 = thread["preview"]?.JSONValueString?.Trimmed() ?? ""; // :698
        var description2 = DescribeHead(preview2, nickname2, role2); // :699
        OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
        {
            Id = id,
            Description = description2,
            Prompt = preview2.Length > 0 ? preview2 : null,
            Model = thread["model"]?.JSONValueString,
        })); // :700
    }

    // Shared by both HeadStarted branches (:690, :699): `!preview.isEmpty ? TextCleanup.singleLine(preview, limit:
    // 80) : ([nickname, role].compactMap { $0 }.joined(separator: " · ").nilIfEmpty ?? "Subagent")`.
    private static string DescribeHead(string preview, string? nickname, string? role)
    {
        if (preview.Length > 0) return TextCleanup.SingleLine(preview, 80);
        var parts = new List<string>();
        if (nickname is not null) parts.Add(nickname);
        if (role is not null) parts.Add(role);
        var joined = string.Join(" " + Dot + " ", parts);
        return joined.Length > 0 ? joined : "Subagent";
    }

    // :703-725. Later part of this unit; not reached by any notification this part's own tests send.
    private void ItemStarted(JSONValue item, Target target, Action<ProviderEvent> sink) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:703");

    // :727-749. Later part of this unit; not reached by any notification this part's own tests send.
    private void ItemCompleted(JSONValue item, Target target, Action<ProviderEvent> sink) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:727");

    // :909-988. Later part.
    private void HandleRequest(RPCID id, string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:909");

    // :1032-1038. Ported in part 1: see the file header note (the connection Start() wires this to can close
    // on any of part 1's own paths).
    private void HandleClose(string tail)
    {
        var keys = pendingRequests.Keys.ToArray(); // :1033
        pendingRequests.Clear();                    // :1034
        foreach (var key in keys)
        {
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :1035
        }

        if (!isStopping) // :1037
        {
            OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(tail))); // :1038
        }
    }

    // :910-918
    public static string ErrorMessage(JSONValue? error)
    {
        if (error is null) return "Codex reported an error."; // :911
        var raw = error["message"]?.JSONValueString ?? error.DisplayText; // :912
        if (JSONValue.Parse(raw) is { } nested &&
            (nested["error"]?["message"]?.JSONValueString ?? nested["message"]?.JSONValueString) is { } message) // :913-914
        {
            return message; // :915
        }
        return raw; // :917
    }

    // :1040-1051
    private static PolicySettings PolicySettingsFor(RuntimeMode mode) => mode switch
    {
        RuntimeMode.Supervised => new PolicySettings( // :1041-1043
            new JSONValue.String("untrusted"),
            new JSONValue.String("read-only"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "readOnly" })),
            new JSONValue.String("user")),
        RuntimeMode.AutoAcceptEdits => new PolicySettings( // :1044-1046
            new JSONValue.String("on-request"),
            new JSONValue.String("workspace-write"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "workspaceWrite" })),
            new JSONValue.String("user")),
        RuntimeMode.Auto => new PolicySettings( // :1047-1049
            new JSONValue.String("on-request"),
            new JSONValue.String("workspace-write"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "workspaceWrite" })),
            new JSONValue.String("auto_review")),
        RuntimeMode.FullAccess => new PolicySettings( // :1050-1051 approximately (table's fourth row)
            new JSONValue.String("never"),
            new JSONValue.String("danger-full-access"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "dangerFullAccess" })),
            new JSONValue.String("user")),
        _ => throw new UnreachableException(),
    };
}
