using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;

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

    private readonly SessionConfiguration configuration; // :35
    private JSONRPCConnection? connection;                // :36
    private string? threadID;                             // :37
    private string? turnID;                               // :38
    private string? activeModel;                           // :39
    private readonly Dictionary<string, PendingRequest> pendingRequests = new(StringComparer.Ordinal); // :40
#pragma warning disable CS0649 // written by Stop() (a later part); read here by HandleClose (:1037)
    private bool isStopping; // :41
#pragma warning restore CS0649

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

    // :268-301. PR-PROVCLI-089, part 2 of this unit.
    private Task<string?> ForkHistory(string targetID, string? previousID, JSONRPCConnection connection, string originalID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:268");

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

    // :303-318
    public void ResolveApproval(string requestID, string optionID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:303");

    // :320-328
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:320");

    // :330-333
    public void Stop() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:330");

    // :335-341
    public Task<bool> StopAgent(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:335");

    // :345-353
    public static Task<ValueList<SlashCommand>> ListCommands(SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:345");

    // :355-385
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:355");

    // :388-437
    public static Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:388");

    // :476-491
    public static Task<PlanLimits.ResetOutcome> ConsumeResetCredit(SwiftURL executable, IReadOnlyDictionary<string, string> environment, string? creditID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:476");

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

    // :532-546. Later part.
    private void HandleNotification(string method, JSONValue parameters) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:532");

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
    public static string ErrorMessage(JSONValue? error) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CodexSession.swift:910");

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
