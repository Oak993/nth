using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Store;

// Source: DroppyCode/Services/Providers/CodexSession.swift (B3 52bc792), :1-1059.
//
// Unit Services.Providers.CodexSession part 1 of 4 (PR-PROVCLI-065 to 088) implements: the executable guard
// (:66-67), the connect handshake with its fixed "app-server" argv, bare-line framing with no jsonrpc member,
// the Marrow client identity and its 20 second watchdog (:505-531), start()'s resume/new-thread paths and its
// deferred close on a failed start (:66-108), requestThread's MCP-server retry (:110-127), send() (:129-162),
// interrupt() (:164-170), compact() (:172-175), rollback()'s paging/confirm path (:177-224, not the
// paginated-thread fork fallback), threadParameters and its Hydra, anti-slop and MCP-config assembly
// (:226-268) and policySettings' four-mode table (:1047-1058).
//
// forkHistory (:270-303, PR-PROVCLI-089), handleNotification (:535-662, PR-PROVCLI-107/108) and handleRequest
// (:916-995, PR-PROVCLI-124 to 129) belong to later parts of this unit and are left as stubs here so this part
// compiles; nothing this part's own connect() wiring can trigger reaches them (the stand-ins this part's tests
// drive only ever reply to requests, never send an unsolicited notification or server request). handleClose
// (:1039-1045, PR-PROVCLI-131) is ported here instead, in part 1, for the same reason ClaudeSession's DidClose
// was: Start()'s own connect() wires OnClose to it, and a connection this part opens can close on ANY of its
// own paths (a failed handshake, a failed thread/start or thread/resume, a watchdog timeout, or simply the
// stand-in exiting), so a stub here would throw out of this part's own tests, not just a later part's. It only
// needs the two fields its own body reads (pendingRequests, isStopping), both left empty/false by every member
// this part implements, which is exactly the Swift behaviour for a session that received no approval or
// question request and was never stopped.
namespace Marrow.Services.Providers;

// :11-1059. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class CodexSession : ProviderSession, IStandbyInjectionSession, Marrow.Services.Port.Skills.Providers.ISkillSessionLifetime
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

    // PORT-ONLY (SK-11b, WU-7): the folder Connect tells Codex about after initialize, and whether skills/list goes first (a session) or not (a ListCommands probe, which sends its own).
    private sealed record SkillRoot(string Folder, bool ListFirst);

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
    private readonly CodexSkillWiring? skills;            // PORT-ONLY (SK-11b, WU-7): null is the Mac's behaviour, byte for byte
    private JSONRPCConnection? skillsReader;
    public Task SkillReadersExited => skillsReader?.ChildExited ?? Task.CompletedTask;
    private JSONRPCConnection? connection;                // :36
    private MCPResolvedServer? pcServer; // PORT-ONLY (Tactus, ED-NEW-pcctl-41): the chat's nth-pc server while Tactus is on.
    private MCPResolvedServer? uiServer; // PORT-ONLY (ED-NEW-web-sec-rev-6): the chat's nth-ui server as the last launch gave it, so an item from a server of that name is nth's own only then.
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
    // DEVIATION (OD-0134 Ignis, ED-NEW-ignis-runtime): no Swift counterpart. Sub-threads Codex starts under one of this
    // lead's heads (or this lead) that the view does not take as heads (no agent nickname or role): Ignis still learns
    // their cache usage (and nothing else of them reaches the conversation), so helpers at every depth are counted.
    private readonly Dictionary<string, string?> ignisNestedThreads = new(StringComparer.Ordinal);
    // PORT-ONLY: OD-0116, OD-0122, ED-439. While the standby waits, no turn, item, hook or approval may appear on its thread.
    private bool standbyActive;
    private bool standbyDiscarding;
    private Action<ProviderEvent>? leadSinkField; // :53 backs the lazily created LeadSink below
    private Action<ProviderEvent> LeadSink => leadSinkField ??= e => OnEvent?.Invoke(e); // :53

    // :36-45. U+00B7 MIDDLE DOT, used to join a limit's title with its snapshot name (:423) and a head's
    // nickname with its role (:694, :703).
    private const string Dot = "\U000000B7"; // U+00B7 MIDDLE DOT

    // :55-57. PORT-ONLY (SK-11b, WU-7): the optional second parameter attaches this chat's skills generation; without it nothing differs.
    public CodexSession(SessionConfiguration configuration, CodexSkillWiring? skills = null)
    {
        this.configuration = configuration;
        this.skills = skills;
    }

    public Action<ProviderEvent>? OnEvent { get; set; } // :14

    // PORT-ONLY (SK-11b, DL-12): the normalized events of ONDEMAND-PLAN.md 5.2, for the missing-tool detector. Nothing else in the session depends on a listener.
    public Action<ToolEvent>? OnToolEvent { get; set; }

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
                skillsReader = c; // RR-15: retained through failed initialization and Stop
                c.OnNotification = HandleNotification; // :73
                c.OnRequest = HandleRequest;            // :74
                c.OnClose = HandleClose;                // :75
            },
            skills is null ? null : new SkillRoot(skills.ExtraRoot, ListFirst: true));
        connection = connected; // :77
        var started = false;    // :78
        try
        {
            var parameters = ThreadParameters; // :85
            if (configuration.ResumeID is { } resumeID) // :87
            {
                var resume = ResumeParameters(parameters, resumeID);
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

    // :85-90, moved out of Start unchanged; PORT-ONLY (SK-11b, WU-7): a chat whose generation moved (its old folder was still held) is told where its skills now are.
    private Dictionary<string, JSONValue> ResumeParameters(Dictionary<string, JSONValue> parameters, string resumeID)
    {
        var resume = new Dictionary<string, JSONValue>(parameters, StringComparer.Ordinal)
        {
            ["threadId"] = resumeID,       // :89
            ["excludeTurns"] = true,        // :90
        };
        if (configuration.ResumePath is { } path) resume["path"] = new JSONValue.String(path);
        if (skills?.MovedNote is { } moved)
        {
            resume["developerInstructions"] = (resume["developerInstructions"].JSONValueString ?? "") + "\n\n" + moved;
        }
        return resume;
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

    // :131. PORT-ONLY (SK-11b, WU-7): a skill path the listing probe reported is sent as the same skill under this chat's generation; without wiring the command is used as it is.
    private SlashCommand ResolveSkill(SlashCommand command) =>
        skills is not null && command.SkillPath is { } path ? command with { SkillPath = skills.ResolveSkillPath(path) } : command;

    // :129-162
    public async Task Send(TurnInput input)
    {
        if (connection is not { IsClosed: false } connected || threadID is not { } currentThreadID) // :130
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        // PORT-ONLY (ED-439): a waiting standby runs no model turn; the handover ends the standby first.
        if (standbyActive || standbyDiscarding)
        {
            throw new ProviderError(new ProviderError.Case.Failed("The standby is waiting for the switch and cannot run a turn."));
        }

        var content = new List<JSONValue>(
            input.Command is { } command
                ? ResolveSkill(command).CodexInput(input.Text) // :131
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
            // A turn can switch the thread's model; the spend it reports is this turn's model's. :148
            activeModel = model; // :149
            parameters["model"] = model; // :150
            parameters["collaborationMode"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["mode"] = input.InteractionMode == InteractionMode.Plan ? "plan" : "default",
                ["settings"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["model"] = model,
                    ["reasoning_effort"] = JSONValue.Optional(effort),
                    ["developer_instructions"] = new JSONValue.Null(),
                })),
            })); // :151-158
        }

        var result = await connected.Request("turn/start", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :160
        if (result["turn"]?["id"]?.JSONValueString is { } id)
        {
            turnID = id; // :161
        }
    }

    // PORT-ONLY: OD-0116, OD-0122, ED-439. Standby by history injection (`thread/inject_items`): no model turn, so nothing can act.
    public string? StandbyViolation { get; private set; }
    public Action<string>? OnStandbyViolation { get; set; }

    public async Task<bool> SupportsStandbyInjection(CancellationToken ct)
    {
        if (configuration.Executable is not { } executable) return false;
        if (CodexHistoryInjection.Known(executable) is { } known) return known;
        // An unknown CLI build: one short-lived app-server reads its version; the verdict is cached for this executable.
        JSONValue? initializeResult = null;
        JSONRPCConnection? probe = null;
        try
        {
            probe = await Connect(executable, configuration.WorkingDirectory, configuration.Environment, _ => { }, null, initialized: result => initializeResult = result);
            ct.ThrowIfCancellationRequested();
            return await CodexHistoryInjection.SupportedAsync(executable, configuration.Environment, initializeResult?["userAgent"]?.JSONValueString);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            probe?.Close();
        }
    }

    public async Task<string> StartInjectedStandby(CancellationToken ct)
    {
        if (configuration.Executable is not { } executable)
        {
            throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider));
        }

        JSONValue? initializeResult = null;
        standbyActive = true;
        var connected = await Connect(
            executable,
            configuration.WorkingDirectory,
            configuration.Environment,
            c =>
            {
                c.OnNotification = HandleNotification;
                c.OnRequest = HandleRequest;
                c.OnClose = HandleClose;
            },
            skills is null ? null : new SkillRoot(skills.ExtraRoot, ListFirst: true),
            initialized: result => initializeResult = result);
        connection = connected;
        var started = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var userAgent = initializeResult?["userAgent"]?.JSONValueString;
            if (!await CodexHistoryInjection.SupportedAsync(executable, configuration.Environment, userAgent))
            {
                throw new StandbyUnavailableException("This Codex version cannot load history without a model turn.");
            }

            ct.ThrowIfCancellationRequested();
            // Belt and braces only: the thread starts read-only and asking, and no turn is ever started on it before the handover.
            var parameters = ThreadParameters;
            parameters["approvalPolicy"] = "untrusted";
            parameters["sandbox"] = "read-only";
            parameters["approvalsReviewer"] = "user";
            var result = await RequestThread(connected, "thread/start", parameters);
            if (result["thread"]?["id"]?.JSONValueString is not { } id)
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not start a thread."));
            }

            threadID = id;
            activeModel = result["model"]?.JSONValueString;
            started = true;
            return id;
        }
        finally
        {
            if (!started)
            {
                standbyActive = false;
                isStopping = true;
                connected.Close();
                connection = null;
            }
        }
    }

    public async Task InjectStandbyHistory(string text, CancellationToken ct)
    {
        if (!standbyActive || StandbyViolation is not null || connection is not { IsClosed: false } connected || threadID is not { } id)
        {
            throw new StandbyUnavailableException("The standby is no longer waiting.");
        }

        ct.ThrowIfCancellationRequested();
        var item = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["type"] = "message",
            ["role"] = "developer",
            ["content"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["type"] = "input_text",
                ["text"] = text,
            }))])),
        }));
        try
        {
            _ = await connected.Request("thread/inject_items", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = id,
                ["items"] = new JSONValue.Array(new ValueList<JSONValue>([item])),
            })));
        }
        catch (RPCError error)
        {
            await DiscardStandby();
            throw new StandbyUnavailableException("Codex refused the injected history: " + error.Message);
        }
    }

    public async Task DiscardStandby()
    {
        standbyActive = false;
        if (standbyDiscarding) return;
        standbyDiscarding = true;
        isStopping = true;
        if (connection is { IsClosed: false } connected && threadID is { } id)
        {
            // thread/delete removes the persisted standby thread; a slow or silent CLI cannot keep the app waiting.
            var delete = SwiftTry.IgnoreAsync(async () =>
            {
                _ = await connected.Request("thread/delete", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["threadId"] = id })));
            });
            _ = await Task.WhenAny(delete, SwiftTask.TrySleep(TimeSpan.FromSeconds(3)));
        }

        connection?.Close();
    }

    public Task EndStandbyAsync(CancellationToken ct)
    {
        standbyActive = false;
        return Task.CompletedTask;
    }

    private void StandbyActivity(string method)
    {
        if (StandbyViolation is not null) return;
        StandbyViolation = method;
        OnStandbyViolation?.Invoke(method);
        _ = DiscardStandby();
    }

    // :164-170
    public async Task Interrupt()
    {
        if (connection is not { } connected || threadID is not { } currentThreadID || turnID is not { } currentTurnID) // :165
        {
            return;
        }

        await SwiftTry.IgnoreAsync(async () => // :166
        {
            _ = await connected.Request("turn/interrupt", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = currentThreadID, // :167
                ["turnId"] = currentTurnID,     // :168
            })));
        });
    }

    // :172-175
    public async Task Compact()
    {
        if (standbyActive || standbyDiscarding)
            throw new ProviderError(new ProviderError.Case.Failed("The standby is waiting for the switch and cannot run a turn."));
        if (connection is not { } connected || threadID is not { } currentThreadID) // :173
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        _ = await connected.Request("thread/compact/start", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["threadId"] = currentThreadID, // :174
        })));
    }

    // :177-224. Swift: rollback(from:). The paginated-thread fork fallback (:213-215, forkHistory :270-303) is
    // PR-PROVCLI-089, part 2 of this unit; ForkHistory below is a stub until then.
    public async Task<string?> Rollback(string targetID)
    {
        if (connection is not { } connected || threadID is not { } currentThreadID) // :178
        {
            throw new ProviderError(new ProviderError.Case.NotRunning());
        }

        var watchdog = MainActor.Spawn(async () => // :179-182
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(30));
            if (!SwiftTask.IsCancelled)
            {
                connected.Close();
            }
        });
        try
        {
            string? cursor = null; // :184
            var count = 0;         // :185
            var found = false;     // :186
            string? previousID = null; // :187

            do // :188
            {
                var page = await connected.Request("thread/turns/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = currentThreadID,       // :190
                    ["cursor"] = JSONValue.Optional(cursor),
                    ["limit"] = 100,                       // :191
                    ["sortDirection"] = "desc",
                    ["itemsView"] = "notLoaded",
                })));

                if (page["data"]?.JSONValueArray is not { } turns) // :193
                {
                    throw new ProviderError(new ProviderError.Case.Failed("Codex did not return its conversation history.")); // :194
                }

                foreach (var turn in turns) // :196
                {
                    if (turn["id"]?.JSONValueString is not { } id) // :197
                    {
                        throw new ProviderError(new ProviderError.Case.Failed("Codex returned a turn without an ID."));
                    }

                    if (found) // :198
                    {
                        previousID = id;
                        break;
                    }

                    count++; // :199
                    if (SwiftString.Equal(id, targetID)) // :200
                    {
                        found = true;
                    }
                }

                var next = page["nextCursor"]?.JSONValueString; // :202
                if (next is not null && cursor is not null && SwiftString.Equal(next, cursor)) // :203
                {
                    throw new ProviderError(new ProviderError.Case.Failed("Codex could not page its conversation history."));
                }

                cursor = next; // :204
            }
            while (cursor is not null && previousID is null); // :205

            if (!found) // :206
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex could not find the selected turn. The conversation was kept."));
            }

            JSONValue result; // :207
            try
            {
                result = await connected.Request("thread/rollback", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = currentThreadID, // :210
                    ["numTurns"] = (long)count,      // :211
                })));
            }
            catch (RPCError error) when (error.Message.Contains("paginated threads do not support thread/rollback", StringComparison.Ordinal)) // :213
            {
                return await ForkHistory(targetID, previousID, connected, currentThreadID); // :214
            }

            var resultThreadID = result["thread"]?["id"]?.JSONValueString;
            var remainingTurns = result["thread"]?["turns"]?.JSONValueArray;
            var confirmed =
                resultThreadID is not null && SwiftString.Equal(resultThreadID, currentThreadID) && // :216
                remainingTurns is not null && // :217
                !remainingTurns.Any(turn => turn["id"]?.JSONValueString is { } tid && SwiftString.Equal(tid, targetID)) && // :218
                LastIDEquals(remainingTurns, previousID); // :219
            if (!confirmed)
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not confirm the requested conversation rollback.")); // :220
            }

            turnID = null;           // :222
            return currentThreadID;  // :223
        }
        finally
        {
            watchdog.Cancel(); // :183
        }
    }

    // Swift optional equality of the last remaining turn's id against previousID (:218-219): both nil is a
    // match, one nil and the other not is never a match.
    private static bool LastIDEquals(IReadOnlyList<JSONValue> turns, string? previousID)
    {
        var lastID = turns.Count > 0 ? turns[^1]["id"]?.JSONValueString : null;
        return lastID is null ? previousID is null : previousID is not null && SwiftString.Equal(lastID, previousID);
    }

    // :270-303. PR-PROVCLI-089.
    private async Task<string?> ForkHistory(string targetID, string? previousID, JSONRPCConnection connection, string originalID)
    {
        if (previousID is not { } previous) // :271
        {
            _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = originalID, // :272
            })));
            threadID = null; // :273
            turnID = null;    // :274
            return null;      // :275
        }

        var parameters = ThreadParameters; // :277
        parameters["threadId"] = originalID; // :278
        parameters["lastTurnId"] = previous;  // :279
        parameters["excludeTurns"] = true;    // :280
        var result = await connection.Request("thread/fork", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :281
        if (result["thread"]?["id"]?.JSONValueString is not { } id || SwiftString.Equal(id, originalID)) // :282
        {
            throw new ProviderError(new ProviderError.Case.Failed("Codex did not create a conversation at the revert point.")); // :283
        }

        try // :285-298
        {
            var page = await connection.Request("thread/turns/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = id, // :287
                ["limit"] = 1,
                ["sortDirection"] = "desc",
                ["itemsView"] = "notLoaded",
            })));
            var turns = page["data"]?.JSONValueArray; // :289
            var firstID = turns is { Count: > 0 } ? turns[0]["id"]?.JSONValueString : null; // :290
            if (firstID is null || !SwiftString.Equal(firstID, previous) || SwiftString.Equal(firstID, targetID)) // :290-291
            {
                throw new ProviderError(new ProviderError.Case.Failed("Codex did not confirm the reverted conversation history.")); // :292
            }
            _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = originalID, // :294
            })));
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :295
        {
            await SwiftTry.IgnoreAsync(async () => // :296 try?
            {
                _ = await connection.Request("thread/archive", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["threadId"] = id,
                })));
            });
            throw; // :297
        }

        threadID = id;         // :299
        turnID = null;          // :300
        activeModel = result["model"]?.JSONValueString ?? activeModel; // :301
        return id;               // :302
    }

    // :226-268
    private Dictionary<string, JSONValue> ThreadParameters
    {
        get
        {
            var policy = PolicySettingsFor(configuration.RuntimeMode); // :227
            var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal)
            {
                ["cwd"] = WorkingDirectory,               // :229
                ["approvalPolicy"] = policy.ApprovalPolicy, // :230
                ["sandbox"] = policy.Sandbox,               // :231
                ["approvalsReviewer"] = policy.Reviewer,    // :232
            };
            if (configuration.Model is { } model) // :234
            {
                parameters["model"] = model;
            }

            var config = new Dictionary<string, JSONValue>(StringComparer.Ordinal); // :235
            if (configuration.Hydra is { } hydra) // :236
            {
                if (hydra.RunsNatively) // :237
                {
                    foreach (var pair in HydraPrompts.CodexConfig(hydra)) // :238, hydra wins every collision
                    {
                        config[pair.Key] = pair.Value;
                    }

                    parameters["developerInstructions"] = HydraPrompts.Policy(ProviderKind.Codex, hydra); // :239
                }
                else
                {
                    config["features"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["multi_agent"] = false, // :246
                    }));
                    parameters["developerInstructions"] = HydraPrompts.FallbackPolicy(hydra); // :247
                }
            }

            if (config.Count > 0) // :250
            {
                parameters["config"] = new JSONValue.Object(new ValueMap<string, JSONValue>(config));
            }

            var existingInstructions = parameters.TryGetValue("developerInstructions", out var instructions) // :254
                ? instructions.JSONValueString
                : null;
            var joined = new List<string>();
            if (existingInstructions is not null) joined.Add(existingInstructions);
            joined.Add(AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled));
            parameters["developerInstructions"] = string.Join("\n\n", joined); // :255-259

            // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): a Hydra head's ask_advisor server joins the connected ones.
            // + ED-NEW-web-w12-1: a chat's nth-ui server (Tabula's artifact tools) joins them too.
            // PORT-ONLY (Tactus, ED-NEW-pcctl-41): so does nth-pc while Tactus is on, with its tool timeout, and Codex's own
            // bundled computer-use plugins are turned off in the same overrides, so two controllers never fight in one chat.
            pcServer ??= MCPProviderConfig.PcServer(configuration);
            uiServer = MCPProviderConfig.UiServer(configuration);
            if (MCPProviderConfig.CodexBuiltIns(MCPProviderConfig.CodexOverrides(), MCPProviderConfig.BuiltInServers(MCPProviderConfig.AdvisorServer(configuration), pcServer, uiServer)) is { } servers) // :262
            {
                var mergedConfig = parameters.TryGetValue("config", out var currentConfig) && currentConfig.JSONValueObject is { } currentObject
                    ? new Dictionary<string, JSONValue>(currentObject, StringComparer.Ordinal)
                    : new Dictionary<string, JSONValue>(StringComparer.Ordinal); // :263
                mergedConfig["mcp_servers"] = servers; // :264
                if (pcServer is not null && Port.PcControl.PcControlBridge.TactusOn) Port.PcControl.PcProviderWiring.TurnOffCodexComputerUse(mergedConfig); // + ED-NEW-web-w11-1: only while Tactus itself is on
                parameters["config"] = new JSONValue.Object(new ValueMap<string, JSONValue>(mergedConfig)); // :265
            }

            return parameters; // :267
        }
    }

    // :305-320. PR-PROVCLI-090, 091, 092.
    public void ResolveApproval(string requestID, string optionID)
    {
        if (!pendingRequests.Remove(requestID, out var pending)) return; // :306
        switch (pending)
        {
            case PendingRequest.Decision(var id, var payloads):
                connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["decision"] = payloads.TryGetValue(optionID, out var payload) ? payload : "cancel", // :309
                })));
                break;
            case PendingRequest.Permissions(var id, var requested):
                if (optionID == "turn" || optionID == "session") // :311
                {
                    connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["permissions"] = requested, // :312
                        ["scope"] = optionID,
                    })));
                }
                else
                {
                    connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                    {
                        ["permissions"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty), // :314
                        ["scope"] = "turn",
                    })));
                }
                break;
            case PendingRequest.Question(var id):
                connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
                {
                    ["answers"] = new JSONValue.Object(ValueMap<string, JSONValue>.Empty), // :317
                })));
                break;
            default:
                throw new UnreachableException();
        }
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :319
    }

    // :322-330. PR-PROVCLI-093.
    public void AnswerQuestion(string requestID, ValueMap<string, ValueList<string>> answers)
    {
        if (!pendingRequests.Remove(requestID, out var pending) || pending is not PendingRequest.Question(var id)) return; // :323
        var payload = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        foreach (var (questionID, values) in answers) // :325
        {
            payload[questionID] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["answers"] = new JSONValue.Array(new ValueList<JSONValue>(values.Select(v => (JSONValue)new JSONValue.String(v)).ToList())), // :326
            }));
        }
        connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["answers"] = new JSONValue.Object(new ValueMap<string, JSONValue>(payload)), // :328
        })));
        OnEvent?.Invoke(new ProviderEvent.RequestResolved(requestID)); // :329
    }

    // :332-335. PR-PROVCLI-094.
    public void Stop()
    {
        isStopping = true;    // :333
        connection?.Close();   // :334
    }

    // :337-344. PR-PROVCLI-095.
    public async Task<bool> StopAgent(string id)
    {
        if (connection is not { } connected || threadID is not { } currentThreadID || SwiftString.Equal(id, currentThreadID) ||
            !headThreads.Contains(id) || !headTurns.TryGetValue(id, out var turn)) return false; // :338-339
        try // :340 (try? await connection.request(...)) != nil
        {
            _ = await connected.Request("turn/interrupt", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["threadId"] = id, // :341
                ["turnId"] = turn, // :342
            })));
            return true; // :343
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return false; // :343
        }
    }

    // :348-356. PR-PROVCLI-096.
    // PORT-ONLY (SK-11b, WU-7): an optional listing generation (SK-9) is registered as an extra root first, so the slash list also names Marrow's skills.
    public static async Task<ValueList<SlashCommand>> ListCommands(SwiftURL executable, SwiftURL directory, IReadOnlyDictionary<string, string> environment,
        Marrow.Services.Port.Skills.Generations.GenerationListing? listing = null)
    {
        var connection = await Connect(executable, directory, environment, _ => { }, listing is null ? null : new SkillRoot(listing.Folder, ListFirst: false)); // :349
        var watchdog = MainActor.Spawn(async () => // :350-352
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(20));
            if (!SwiftTask.IsCancelled) connection.Close();
        });
        try
        {
            var result = await connection.Request("skills/list", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["cwds"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String(directory.Path)])), // :355
                ["forceReload"] = true,
            })));
            return SlashCommand.CodexSkills(result); // :355
        }
        finally
        {
            watchdog.Cancel();  // :354 defer
            connection.Close(); // :354 defer
        }
    }

    // :358-388. PR-PROVCLI-097.
    public static async Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :359-364
        try
        {
            var models = new List<ModelOption>(); // :366
            JSONValue cursor = new JSONValue.Null(); // :367
            for (var i = 0; i < 10; i++) // :368
            {
                var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
                if (!cursor.IsNull) parameters["cursor"] = cursor; // :370
                var result = await connection.Request("model/list", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :371
                foreach (var model in result["data"]?.JSONValueArray ?? [])
                {
                    if (model["hidden"]?.JSONValueBool == true) continue; // :372
                    if (model["id"]?.JSONValueString is not { } id) continue; // :373
                    var efforts = (model["supportedReasoningEfforts"]?.JSONValueArray ?? [])
                        .Select(e => e["reasoningEffort"]?.JSONValueString)
                        .Where(e => e is not null).Select(e => e!).ToList(); // :378
                    models.Add(new ModelOption(
                        id,
                        model["displayName"]?.JSONValueString ?? id, // :376
                        detail: model["description"]?.JSONValueString, // :377
                        efforts: new ValueList<string>(efforts),
                        defaultEffort: model["defaultReasoningEffort"]?.JSONValueString, // :379
                        isDefault: model["isDefault"]?.JSONValueBool ?? false, // :380
                        fastTier: FastTier(model["serviceTiers"]?.JSONValueArray ?? []))); // :381
                }
                cursor = result["nextCursor"] ?? new JSONValue.Null(); // :384
                if (cursor.IsNull) break; // :385
            }
            return new ValueList<ModelOption>(models); // :387
        }
        finally
        {
            connection.Close(); // :365
        }
    }

    // :496-503. PR-PROVCLI-098.
    private static string? FastTier(IReadOnlyList<JSONValue> tiers)
    {
        foreach (var tier in tiers) // :498 lazy compactMap.first
        {
            if (tier["id"]?.JSONValueString is not { } id) continue; // :499
            var label = SwiftString.Lowercased($"{id} {tier["name"]?.JSONValueString ?? ""}"); // :500
            if (label.Contains("fast", StringComparison.Ordinal) || label.Contains("priority", StringComparison.Ordinal)) return id; // :501
        }
        return null; // :502
    }

    // :391-440. PR-PROVCLI-099, 101, 102, 103.
    public static async Task<PlanLimits?> ReadPlanLimits(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :392-397
        try
        {
            JSONValue result; // :399
            try
            {
                result = await connection.Request("account/rateLimits/read"); // :401
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) // :402
            {
                return new PlanLimits { PlanName = null, Windows = ValueList<PlanLimits.Window>.Empty, Problem = PlainProblem(error) }; // :403-407
            }

            var snapshots = new List<JSONValue>(); // :409
            if (result["rateLimitsByLimitId"]?.JSONValueObject is { Count: > 0 } byID) // :410
            {
                foreach (var key in byID.Keys.OrderBy(k => k, SwiftStringComparer.Instance)) // :411 (R-COLL-1)
                {
                    if (byID.TryGetValue(key, out var snapshot)) snapshots.Add(snapshot);
                }
            }
            else if (result["rateLimits"] is { IsNull: false } single) // :412-413
            {
                snapshots.Add(single);
            }

            string? plan = null; // :415
            var windows = new List<PlanLimits.Window>(); // :416
            foreach (var snapshot in snapshots) // :417
            {
                plan ??= snapshot["planType"]?.JSONValueString; // :418
                var name = snapshots.Count > 1 ? snapshot["limitName"]?.JSONValueString : null; // :419
                foreach (var key in new[] { "primary", "secondary" }) // :420
                {
                    if (snapshot[key] is not { } window || window["usedPercent"]?.JSONValueDouble is not { } percent) continue; // :421
                    var title = PlanLimitsReader.WindowTitle(window["windowDurationMins"]?.JSONValueInt); // :422
                    if (name is { } limitName) title += " " + Dot + " " + limitName; // :423
                    windows.Add(new PlanLimits.Window
                    {
                        Id = $"{snapshot["limitId"]?.JSONValueString ?? "codex"}-{key}", // :425
                        Title = title,                                                    // :426
                        DurationMinutes = window["windowDurationMins"]?.JSONValueInt, // OD-0114 / ED-406
                        Percent = percent,                                                // :427
                        ResetsAt = window["resetsAt"]?.JSONValueDouble is { } resets ? SwiftDate.From1970Seconds(resets) : null, // :428
                    });
                }
            }

            if (windows.Count == 0) // :432
            {
                return new PlanLimits
                {
                    PlanName = PlanLimitsReader.CodexPlanName(plan),                          // :434
                    Windows = ValueList<PlanLimits.Window>.Empty,                              // :435
                    Problem = "Codex reported no usage windows for this account.",             // :436
                };
            }
            return new PlanLimits
            {
                PlanName = PlanLimitsReader.CodexPlanName(plan),                                                          // :439
                Windows = new ValueList<PlanLimits.Window>(windows),
                PlanLimitsResetCredits = ParseResetCredits(result["rateLimitResetCredits"] ?? result["rate_limit_reset_credits"]), // :439
            };
        }
        finally
        {
            connection.Close(); // :398
        }
    }

    // :442-448. PR-PROVCLI-100.
    private static string PlainProblem(Exception error)
    {
        var text = TextCleanup.SingleLine(DescribeError(error), limit: 300); // :443
        if (text.Contains("token_expired", StringComparison.Ordinal) || text.Contains("401", StringComparison.Ordinal)) // :444
        {
            return "Codex's stored login has expired. Run `codex login` in a terminal, then check again."; // :445
        }
        return text.Length == 0 ? "Codex could not be asked for its usage." : text; // :446-447
    }

    // ENGINEERING DECISION: `String(describing: error)` for JSONRPC.swift's RPCError (a plain LocalizedError
    // struct with no CustomStringConvertible, JSONRPC.swift:31-36) is Swift's default struct-mirror dump. Every
    // throw reachable at the ReadPlanLimits call site (JSONRPC.swift:72, :131, :139) is an RPCError; a scripted
    // or real "error" reply never carries a "data" field, so data is always nil in practice.
    private static string DescribeError(Exception error) => error is RPCError rpc
        ? $"RPCError(code: {rpc.Code}, message: \"{rpc.Message}\", data: {(rpc.Data is null ? "nil" : rpc.Data.CompactString)})"
        : error.ToString();

    // :450-465. PR-PROVCLI-104.
    private static PlanLimits.ResetCredits? ParseResetCredits(JSONValue? raw)
    {
        if (raw is null) return null; // :452
        var count = raw["availableCount"]?.JSONValueInt ?? raw["available_count"]?.JSONValueInt; // :452
        if (count is not { } value) return null;
        var credits = new List<PlanLimits.ResetCredit>();
        foreach (var credit in raw["credits"]?.JSONValueArray ?? []) // :453
        {
            if (credit["id"]?.JSONValueString is not { Length: > 0 } id) continue; // :454
            var status = credit["status"]?.JSONValueString; // :455
            if (status is not null && status != "available" && status != "unknown") continue; // :456
            credits.Add(new PlanLimits.ResetCredit
            {
                Id = id,                                                                       // :458
                Title = credit["title"]?.JSONValueString,                                       // :459
                Detail = credit["description"]?.JSONValueString,                                // :460
                ExpiresAt = ResetDate(credit["expiresAt"] ?? credit["expires_at"]),              // :461
            });
        }
        return new PlanLimits.ResetCredits { AvailableCount = Math.Max(0L, value), Credits = new ValueList<PlanLimits.ResetCredit>(credits) }; // :464
    }

    // :467-476. PR-PROVCLI-105.
    private static DateTimeOffset? ResetDate(JSONValue? value)
    {
        if (value is null) return null; // :469
        var number = value.JSONValueDouble ?? (value.JSONValueString is { } text1 ? SwiftNumber.ParseDouble(text1) : null); // :470
        if (number is { } n)
        {
            if (!(n > 0)) return null; // :471
            return SwiftDate.From1970Seconds(n > 10_000_000_000 ? n / 1_000 : n); // :472
        }
        if (value.JSONValueString is not { } text) return null; // :474
        return ISO8601.Date(text); // :475
    }

    // :479-494. PR-PROVCLI-106.
    public static async Task<PlanLimits.ResetOutcome> ConsumeResetCredit(SwiftURL executable, IReadOnlyDictionary<string, string> environment, string? creditID)
    {
        var connection = await Connect(executable, DirectoryFileUrl(TemporaryDirectory), environment, _ => { }); // :480-485
        try
        {
            var parameters = new Dictionary<string, JSONValue>(StringComparer.Ordinal)
            {
                ["idempotencyKey"] = SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid())), // :487
            };
            if (creditID is { } id) parameters["creditId"] = id; // :488
            var result = await connection.Request("account/rateLimitResetCredit/consume", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters))); // :489
            if (result["outcome"]?.JSONValueString is not { } outcomeText || SwiftEnum<PlanLimits.ResetOutcome>.FromRawValue(outcomeText) is not { } outcome) // :490
            {
                throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :491
            }
            return outcome; // :493
        }
        finally
        {
            connection.Close(); // :486
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

    // FileManager.default.temporaryDirectory (:361, :394, :482): the Windows user temporary directory, the same
    // mapping PlanLimits.cs's TemporaryDirectory gives it (R-TEST-9: MARROW_PORT_DATA_DIR first).
    private static string TemporaryDirectory =>
        Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath();

    // :508, :511. The argument vector of every app-server launch.
    private static IReadOnlyList<string> AppServerArguments => ["app-server"];

    // :521-524, moved out of Connect unchanged.
    private static JSONValue InitializeParameters() => new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
    {
        ["clientInfo"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["name"] = "nth",   // DEVIATION OD-0008 B11: upstream "droppy-code"
            ["title"] = "nth",  // DEVIATION OD-0008 B11: upstream "Droppy Code"
            ["version"] = AppInfo.Current.Version,
        })),
        ["capabilities"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["experimentalApi"] = true,
        })),
    }));

    // :505-531
    private static async Task<JSONRPCConnection> Connect(
        SwiftURL executable,
        SwiftURL directory,
        IReadOnlyDictionary<string, string> environment,
        Action<JSONRPCConnection> configure,
        SkillRoot? skillRoot = null,
        Action<JSONValue>? initialized = null)
    {
        var process = new StdioProcess(executable.Path, AppServerArguments, directory.Path, CodexChildEnvironment.Apply(environment)); // :511. PORT-ONLY (SK-11b, DL-11): the PATH rule, nothing else.
        var connection = new JSONRPCConnection(process, sendsVersion: false); // :512
        configure(connection); // :513
        connection.Start(); // :514
        var watchdog = MainActor.Spawn(async () => // :515-519
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
                var initializeResult = await connection.Request("initialize", InitializeParameters()); // :521-524
                initialized?.Invoke(initializeResult);
            }
            catch
            {
                connection.Close(); // :526
                throw;              // :527
            }

            connection.Notify("initialized"); // :529
            if (skillRoot is not null)
            {
                // PORT-ONLY (SK-11b, WU-7): skills/list then skills/extraRoots/set on every connection; an error reply is tolerated.
                try { await CodexSkillRoots.Register(connection.Request, directory.Path, skillRoot.Folder, skillRoot.ListFirst); }
                catch
                {
                    connection.Close();
                    throw;
                }
            }
            return connection;                 // :530
        }
        finally
        {
            watchdog.Cancel(); // :519
        }
    }

    // :535-662. PR-PROVCLI-107, 108, 109, 110, 111, 112. itemStarted (:707-729) and itemCompleted (:731-753)
    // belong to a later part of this unit and stay stubbed below; nothing this part's own tests send an
    // "item/started" or "item/completed" notification, so the switch reaches those cases only in a later part.
    private void HandleNotification(string method, JSONValue parameters)
    {
        // PORT-ONLY (ED-439): fail closed. Activity on a waiting thread stops and discards it; nothing of it reaches the conversation.
        if (standbyActive || standbyDiscarding)
        {
            if (standbyActive && method is "turn/started" or "item/started" or "item/completed" or "hook/started") StandbyActivity(method);
            return;
        }

        if (method == "thread/started") // :536-539
        {
            HeadStarted(parameters["thread"] ?? new JSONValue.Null());
            return;
        }

        Target target = new Target.Lead(); // :541
        if (parameters["threadId"]?.JSONValueString is { } eventThread && threadID is { } currentThreadID && !SwiftString.Equal(eventThread, currentThreadID)) // :542
        {
            if (!headThreads.Contains(eventThread)) // :543
            {
                // Ignis only (ED-NEW-ignis-runtime): a nested helper's cache usage, as its own agent; nothing else.
                if (method == "thread/tokenUsage/updated" && ignisNestedThreads.TryGetValue(eventThread, out var nestedModel) && parameters["tokenUsage"] is { } nestedUsage && nestedUsage["last"] is not null)
                    OnEvent?.Invoke(new ProviderEvent.CacheUsage(CacheUsageOf(nestedUsage, nestedModel, eventThread)));
                return;
            }
            target = new Target.Head(eventThread); // :544
        }

        void Sink(ProviderEvent e) // :546-549
        {
            switch (target)
            {
                case Target.Lead: LeadSink(e); break;
                case Target.Head(var headID): OnEvent?.Invoke(new ProviderEvent.AgentEvent(headID, e)); break;
            }
        }

        switch (method)
        {
            case "turn/started": // :551-559
            {
                var id = parameters["turn"]?["id"]?.JSONValueString;
                switch (target)
                {
                    case Target.Lead:
                        turnID = id; // :555
                        break;
                    case Target.Head(var head):
                        if (id is null) headTurns.Remove(head); else headTurns[head] = id; // :557
                        break;
                }
                Sink(new ProviderEvent.TurnStarted(id)); // :559
                break;
            }
            case "item/started": // :560-561
                if (parameters["item"] is { } startedItem)
                {
                    Tap(CodexToolTaps.ItemStarted(startedItem, skills?.Chat.Skills));
                    ItemStarted(startedItem, target, Sink);
                }
                break;
            case "item/completed": // :562-563
                if (parameters["item"] is { } completedItem)
                {
                    Tap(CodexToolTaps.ItemCompleted(completedItem));
                    ItemCompleted(completedItem, target, Sink);
                }
                break;
            case "item/agentMessage/delta": // :564-567
                if (parameters["itemId"]?.JSONValueString is { } msgID && parameters["delta"]?.JSONValueString is { } msgDelta)
                {
                    Sink(new ProviderEvent.MessageDelta(msgID, msgDelta));
                }
                break;
            case "item/reasoning/summaryTextDelta":
            case "item/reasoning/textDelta": // :568-571
                if (parameters["itemId"]?.JSONValueString is { } reasoningID && parameters["delta"]?.JSONValueString is { } reasoningDelta)
                {
                    Sink(new ProviderEvent.ReasoningDelta(reasoningID, reasoningDelta));
                }
                break;
            case "item/reasoning/summaryPartAdded": // :572-575
                if (parameters["itemId"]?.JSONValueString is { } partID && parameters["summaryIndex"]?.JSONValueInt is { } index && index > 0)
                {
                    Sink(new ProviderEvent.ReasoningDelta(partID, "\n\n"));
                }
                break;
            case "item/commandExecution/outputDelta": // :576-579
                if (parameters["itemId"]?.JSONValueString is { } outID && parameters["delta"]?.JSONValueString is { } outDelta)
                {
                    Sink(new ProviderEvent.ToolOutput(outID, outDelta));
                }
                break;
            case "item/plan/delta": // :580-583
                if (parameters["itemId"]?.JSONValueString is { } planID && parameters["delta"]?.JSONValueString is { } planDelta)
                {
                    Sink(new ProviderEvent.PlanDelta(planID, planDelta));
                }
                break;
            case "turn/plan/updated": // :584-594. PR-PROVCLI-109.
            {
                var steps = new List<TodoStep>();
                foreach (var step in parameters["plan"]?.JSONValueArray ?? [])
                {
                    if (step["step"]?.JSONValueString is not { } text) continue; // :586
                    var status = step["status"]?.JSONValueString switch // :588-591
                    {
                        "completed" => TodoStep.Status.Done,
                        "inProgress" => TodoStep.Status.Active,
                        _ => TodoStep.Status.Pending,
                    };
                    steps.Add(new TodoStep { Text = text, TodoStepStatus = status }); // :592
                }
                Sink(new ProviderEvent.Todos(new ValueList<TodoStep>(steps))); // :594
                break;
            }
            case "turn/diff/updated": // :595-596
                if (parameters["diff"]?.JSONValueString is { } diff) Sink(new ProviderEvent.Diff(diff));
                break;
            case "thread/tokenUsage/updated": // :597-629. PR-PROVCLI-110, 111.
                HandleTokenUsageUpdated(parameters, target);
                break;
            case "error": // :630-634. PR-PROVCLI-112.
                if (target is Target.Lead && parameters["willRetry"]?.JSONValueBool == true)
                {
                    var message = ErrorMessage(parameters["error"]); // :632
                    OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
                    {
                        NoticeLevel = Notice.Level.Warning,
                        Message = message + " Retrying.", // :633
                    }));
                }
                break;
            case "turn/completed": // :635-653
                HandleTurnCompleted(parameters, target);
                break;
            case "serverRequest/resolved": // :654-657
                if (parameters["requestId"] is { } raw && RPCID.Create(raw) is { } resolvedID && pendingRequests.Remove(resolvedID.Key))
                {
                    OnEvent?.Invoke(new ProviderEvent.RequestResolved(resolvedID.Key));
                }
                break;
            case "thread/compacted": // :658-659
                Sink(new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }));
                break;
            default: // :660-661
                break;
        }
    }

    // :597-628. PR-PROVCLI-110, 111.
    private void HandleTokenUsageUpdated(JSONValue parameters, Target target)
    {
        if (parameters["tokenUsage"] is not { } usage || usage["last"] is not { } last) return; // :598
        var used = last["totalTokens"]?.JSONValueInt ?? 0; // :599
        switch (target)
        {
            case Target.Lead:
                OnEvent?.Invoke(new ProviderEvent.Usage(new ContextUsage { UsedTokens = used, WindowTokens = usage["modelContextWindow"]?.JSONValueInt })); // :602
                OnEvent?.Invoke(new ProviderEvent.CacheUsage(CacheUsageOf(usage, activeModel, null))); // DEVIATION (OD-0134 Ignis): no Swift counterpart.
                if (usage["total"]?["totalTokens"]?.JSONValueInt is { } total && total > 0) // :610
                {
                    var (next, spend) = spendTracker.Spend(total, Math.Max(0, total - used)); // :611
                    spendTracker = next;
                    RecordCodexSpend(spend, threadID, activeModel); // :612
                }
                else
                {
                    RecordCodexSpend(used, threadID, activeModel); // :614
                }
                break;
            case Target.Head(var head):
                var headTotal = usage["total"]?["totalTokens"]?.JSONValueInt ?? 0; // :618
                var headModel = headModels.GetValueOrDefault(head); // :619
                if (headTotal > 0)
                {
                    var tracker = headSpend.TryGetValue(head, out var existing) ? existing : new TokenSpendTracker(); // :621
                    var (next, spend) = tracker.Spend(headTotal, Math.Max(0, headTotal - used)); // :622
                    headSpend[head] = next; // :623
                    RecordCodexSpend(spend, head, headModel, head); // :624
                }
                else
                {
                    RecordCodexSpend(used, head, headModel, head); // :626
                }
                OnEvent?.Invoke(new ProviderEvent.AgentProgress(head, null, null, headTotal > 0 ? headTotal : used, null)); // :628
                OnEvent?.Invoke(new ProviderEvent.CacheUsage(CacheUsageOf(usage, headModel, head))); // DEVIATION (OD-0134 Ignis): no Swift counterpart.
                break;
        }
    }

    // DEVIATION (OD-0134 Ignis): no Swift counterpart. One request's cache usage from tokenUsage.last: inputTokens counts
    // the cached part too (OpenAI's convention), so the uncached bucket is inputTokens - cachedInputTokens - the written
    // part. ED-NEW-ignis-runtime: newer Codex CLIs also report the tokens written to the prompt cache
    // (cache_write_input_tokens in `codex exec --json`, codex-rs exec_events.rs Usage; the app-server's camelCase
    // cacheWriteInputTokens is read too, its exact name UNRESOLVED); the stream gives no lifetime for them, so they
    // land in CacheWriteUnknownTtl. Older CLIs omit the field: the writes stay 0. Without cachedInputTokens the usage
    // is "not reported".
    internal static global::Marrow.Core.Port.Ignis.IgnisTurnUsage CacheUsageOf(JSONValue tokenUsage, string? model, string? agentID)
    {
        var last = tokenUsage["last"];
        long? window = tokenUsage["modelContextWindow"]?.JSONValueInt;
        long? used = last?["totalTokens"]?.JSONValueInt;
        if (last?["cachedInputTokens"]?.JSONValueInt is not { } cached)
            return global::Marrow.Core.Port.Ignis.IgnisTurnUsage.NotReported(ProviderKind.Codex, model, agentID, used, window);
        var input = last["inputTokens"]?.JSONValueInt ?? 0;
        var output = last["outputTokens"]?.JSONValueInt ?? 0;
        var written = Math.Max(0, last["cacheWriteInputTokens"]?.JSONValueInt ?? last["cache_write_input_tokens"]?.JSONValueInt ?? 0);
        var read = Math.Max(0, cached);
        return new global::Marrow.Core.Port.Ignis.IgnisTurnUsage(ProviderKind.Codex, model, agentID,
            Math.Max(0, input - read - written), read, 0, 0, written, output, used, window, true);
    }

    // :671-677
    private void RecordCodexSpend(long spend, string? rolloutID, string? model, string? agentID = null)
    {
        if (spend <= 0) return; // :672
        if (rolloutID is { Length: > 0 }) // :673
        {
            TokenLedger.Shared.Current.RecordCodexSpend(spend, rolloutID); // :674
        }
        OnEvent?.Invoke(new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Codex, spend, model: model, agentID: agentID))); // :676
    }

    // :635-653
    private void HandleTurnCompleted(JSONValue parameters, Target target)
    {
        var turn = parameters["turn"]; // :636
        var status = turn?["status"]?.JSONValueString switch // :637-641
        {
            "interrupted" => TurnStatus.Interrupted,
            "failed" => TurnStatus.Failed,
            _ => TurnStatus.Completed,
        };
        var error = turn?["error"] is { IsNull: false } errorValue ? ErrorMessage(errorValue) : null; // :642
        switch (target)
        {
            case Target.Lead:
                turnID = null; // :645
                if (status == TurnStatus.Failed && error is { } errorText && UsageLimitSignal.Matches(errorText)) // :646
                {
                    OnEvent?.Invoke(new ProviderEvent.UsageLimit(UsageLimitSignal.ResetTime(errorText))); // :647
                }
                OnEvent?.Invoke(new ProviderEvent.TurnCompleted(status, error)); // :649
                break;
            case Target.Head(var head):
                headTurns.Remove(head); // :651
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(head, status, error)); // :652
                break;
        }
    }

    // :679-705. Needed by handleNotification's thread/started routing (PR-PROVCLI-107).
    private void HeadStarted(JSONValue thread)
    {
        if (thread["id"]?.JSONValueString is not { } id || threadID is not { } currentThreadID ||
            SwiftString.Equal(id, currentThreadID) || headThreads.Contains(id)) return; // :681-682
        var parent = thread["parentThreadId"]?.JSONValueString;
        if (parent is null || !SwiftString.Equal(parent, currentThreadID)) // :683
        {
            var nickname = thread["agentNickname"]?.JSONValueString; // :688
            var role = thread["agentRole"]?.JSONValueString;
            if (nickname is null && role is null) // :689
            {
                // Ignis only (ED-NEW-ignis-runtime): a sub-thread of one of this lead's heads keeps its cache usage.
                if (parent is not null && (headThreads.Contains(parent) || ignisNestedThreads.ContainsKey(parent))) ignisNestedThreads[id] = thread["model"]?.JSONValueString;
                return;
            }
            Log.Error($"thread/started {id} names parent {parent ?? "none"}, not this lead {currentThreadID}; taken as a head by its agent nickname"); // :690
            headThreads.Add(id); // :691
            if (thread["model"]?.JSONValueString is { } headModel1) headModels[id] = headModel1; // :692
            var preview1 = thread["preview"]?.JSONValueString?.Trimmed() ?? ""; // :693
            var description1 = DescribeHead(preview1, nickname, role); // :694
            OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
            {
                Id = id,
                Description = description1,
                Prompt = preview1.Length > 0 ? preview1 : null,
                Model = thread["model"]?.JSONValueString,
            })); // :695
            return;
        }

        headThreads.Add(id); // :698
        if (thread["model"]?.JSONValueString is { } headModel2) headModels[id] = headModel2; // :699
        var nickname2 = thread["agentNickname"]?.JSONValueString; // :700
        var role2 = thread["agentRole"]?.JSONValueString; // :701
        var preview2 = thread["preview"]?.JSONValueString?.Trimmed() ?? ""; // :702
        var description2 = DescribeHead(preview2, nickname2, role2); // :703
        OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
        {
            Id = id,
            Description = description2,
            Prompt = preview2.Length > 0 ? preview2 : null,
            Model = thread["model"]?.JSONValueString,
        })); // :704
    }

    // Shared by both HeadStarted branches (:694, :703): `!preview.isEmpty ? TextCleanup.singleLine(preview, limit:
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

    // :707-729. PR-PROVCLI-117.
    private void ItemStarted(JSONValue item, Target target, Action<ProviderEvent> sink)
    {
        if (item["id"]?.JSONValueString is not { } id) return; // :708
        var type = item["type"]?.JSONValueString;
        if (type is not null && SwiftString.Equal(type, "agentMessage")) // :710-711
        {
            sink(new ProviderEvent.MessageDelta(id, item["text"]?.JSONValueString ?? ""));
            return;
        }
        if (type is not null && SwiftString.Equal(type, "reasoning")) // :712-713
        {
            sink(new ProviderEvent.ReasoningDelta(id, ""));
            return;
        }
        if (type is not null && SwiftString.Equal(type, "plan")) // :714-715
        {
            sink(new ProviderEvent.PlanDelta(id, item["text"]?.JSONValueString ?? ""));
            return;
        }
        if (type is not null && SwiftString.Equal(type, "userMessage")) // :716-721
        {
            if (target is Target.Head(var head) && UserText(item) is { } text)
            {
                OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn
                {
                    Id = head,
                    Description = TextCleanup.SingleLine(text, 80),
                    Prompt = text,
                }));
            }
            return;
        }
        if (type is not null && (SwiftString.Equal(type, "hookPrompt") || SwiftString.Equal(type, "functionCallOutput") || SwiftString.Equal(type, "contextCompaction"))) // :722-723
        {
            return;
        }
        if (ToolCallFrom(item) is { } call) sink(new ProviderEvent.ToolStarted(id, call) { McpTool = McpToolOf(item) }); // :725-726 + ED-NEW-web-sec-rev-6
        if (type is not null && SwiftString.Equal(type, "collabAgentToolCall")) DescribeHeads(item); // :727-728
    }

    // PORT-ONLY (ED-NEW-web-sec-rev-6): the MCP tool an mcpToolCall item is for, by the item's own server and tool fields (never its
    // arguments or any title); a server named nth-ui is nth's own only when this session's launch gave nth-ui.
    private McpToolIdentity? McpToolOf(JSONValue item) =>
        item["type"]?.JSONValueString is { } type && SwiftString.Equal(type, "mcpToolCall")
            ? McpToolIdentity.FromFields(item["server"]?.JSONValueString, item["tool"]?.JSONValueString, uiServer is not null)
            : null;

    // :731-753. PR-PROVCLI-118.
    private void ItemCompleted(JSONValue item, Target target, Action<ProviderEvent> sink)
    {
        if (item["id"]?.JSONValueString is not { } id) return; // :732
        var type = item["type"]?.JSONValueString;
        if (type is not null && SwiftString.Equal(type, "agentMessage")) // :734-735
        {
            sink(new ProviderEvent.MessageCompleted(id, item["text"]?.JSONValueString ?? ""));
            return;
        }
        if (type is not null && SwiftString.Equal(type, "reasoning")) // :736-739
        {
            var summary = string.Join("\n\n", (item["summary"]?.JSONValueArray ?? []).Select(v => v.JSONValueString).Where(v => v is not null));
            var content = string.Join("\n\n", (item["content"]?.JSONValueArray ?? []).Select(v => v.JSONValueString).Where(v => v is not null));
            sink(new ProviderEvent.ReasoningCompleted(id, summary.Length > 0 ? summary : content));
            return;
        }
        if (type is not null && SwiftString.Equal(type, "plan")) // :740-741
        {
            sink(new ProviderEvent.PlanCompleted(id, item["text"]?.JSONValueString ?? ""));
            return;
        }
        if (type is not null && (SwiftString.Equal(type, "userMessage") || SwiftString.Equal(type, "hookPrompt") ||
                                  SwiftString.Equal(type, "functionCallOutput") || SwiftString.Equal(type, "contextCompaction"))) // :742-743
        {
            return;
        }
        if (ToolCallFrom(item) is { } call) // :745-748
        {
            sink(new ProviderEvent.ToolStarted(id, call) { McpTool = McpToolOf(item) }); // + ED-NEW-web-sec-rev-6
            sink(new ProviderEvent.ToolUpdated(id, ToolUpdateFrom(item)));
        }
        DescribeHeads(item); // :750
    }

    // :755-780. PR-PROVCLI-119. :781-804. PR-PROVCLI-120.
    private void DescribeHeads(JSONValue item)
    {
        if (threadID is not { } currentThreadID) return; // :760
        var type = item["type"]?.JSONValueString;
        if (type is not null && SwiftString.Equal(type, "collabAgentToolCall")) // :761-762
        {
            if (item["tool"]?.JSONValueString is not { } tool || !SwiftString.Equal(tool, "spawnAgent")) return; // :765
            var itemModel = item["model"]?.JSONValueString; // :769
            var itemEffort = item["reasoningEffort"]?.JSONValueString; // :770
            string? routed = null; // :771-774
            if (configuration.Hydra is { } hydra)
            {
                var matches = HydraPrompts.NativeRoutableProfiles(hydra)
                    .Where(profile =>
                        (profile.Model is null && itemModel is null || profile.Model is { } pm && itemModel is { } im && SwiftString.Equal(pm, im)) &&
                        (profile.Effort is null && itemEffort is null || profile.Effort is { } pe && itemEffort is { } ie && SwiftString.Equal(pe, ie)))
                    .ToList();
                routed = matches.Count == 1 ? matches[0].Name : null;
            }
            var prompt = item["prompt"]?.JSONValueString?.Trimmed() ?? ""; // :775
            foreach (var receiverValue in item["receiverThreadIds"]?.JSONValueArray ?? []) // :776
            {
                if (receiverValue.JSONValueString is not { } receiver) continue;
                if (SwiftString.Equal(receiver, currentThreadID)) continue; // :777
                if (!headThreads.Contains(receiver)) headThreads.Add(receiver); // :778
                OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn // :779
                {
                    Id = receiver,
                    ToolUseID = item["id"]?.JSONValueString,
                    Description = prompt.Length == 0 ? "Head" : TextCleanup.SingleLine(prompt, 80),
                    Prompt = prompt.Length == 0 ? null : prompt,
                    Model = itemModel,
                    Profile = routed,
                }));
            }
            return;
        }
        if (type is not null && SwiftString.Equal(type, "subAgentActivity")) // :781
        {
            if (item["agentThreadId"]?.JSONValueString is not { } agentThread ||
                SwiftString.Equal(agentThread, currentThreadID)) return; // :782-783
            if (!headThreads.Contains(agentThread)) // :784
            {
                headThreads.Add(agentThread); // :790
                var lastComponent = item["agentPath"]?.JSONValueString is { } path
                    ? path.Split('/').LastOrDefault()
                    : null;
                var nickname = lastComponent is { Length: > 0 } ? lastComponent : "Head"; // :791
                Log.Error($"head {agentThread} registered from a subAgentActivity note ({nickname}); no thread/started or spawn receiver id preceded it"); // :792
                OnEvent?.Invoke(new ProviderEvent.AgentStarted(new AgentSpawn // :793
                {
                    Id = agentThread,
                    Description = nickname.Replace('_', ' '),
                }));
            }
            var kind = item["kind"]?.JSONValueString; // :795
            if (kind is not null && SwiftString.Equal(kind, "completed")) // :796
            {
                headTurns.Remove(agentThread); // :797
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(agentThread, TurnStatus.Completed, null)); // :798
            }
            else if (kind is not null && SwiftString.Equal(kind, "interrupted")) // :799
            {
                headTurns.Remove(agentThread); // :800
                OnEvent?.Invoke(new ProviderEvent.AgentFinished(agentThread, TurnStatus.Interrupted, null)); // :801
            }
        }
    }

    // :810-817. PR-PROVCLI-133.
    private static string? UserText(JSONValue item)
    {
        var parts = (item["content"]?.JSONValueArray ?? []).Select(part =>
        {
            if (part["text"]?.JSONValueString is { } text) return text;
            return part["type"]?.JSONValueString is { } t && SwiftString.Equal(t, "text") ? part["value"]?.JSONValueString : null;
        }).Where(text => text is not null);
        var joined = string.Join("\n", parts).Trimmed(); // :815
        return joined.Length == 0 ? null : joined; // :816
    }

    // :819-871. PR-PROVCLI-121.
    private ToolCall? ToolCallFrom(JSONValue item)
    {
        var type = item["type"]?.JSONValueString;
        if (type is null) return null;
        if (SwiftString.Equal(type, "commandExecution")) // :820-829
        {
            var command = ToolTitles.UnwrapShell(item["command"]?.JSONValueString ?? "");
            var actions = item["commandActions"]?.JSONValueArray ?? [];
            var kinds = new HashSet<string>(actions.Select(a => a["type"]?.JSONValueString).Where(v => v is not null)!, SwiftStringComparer.Instance);
            if (kinds.SetEquals(new HashSet<string>(["read"], SwiftStringComparer.Instance)) &&
                actions.Count > 0 && actions[0]["path"]?.JSONValueString is { } path)
            {
                return new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = ToolTitles.RelativePath(path, WorkingDirectory), Detail = command };
            }
            if (kinds.Count > 0 && kinds.IsSubsetOf(new HashSet<string>(["search", "listFiles"], SwiftStringComparer.Instance)))
            {
                return new ToolCall { ToolCallKind = ToolCall.Kind.Search, Title = command };
            }
            return new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = command };
        }
        if (SwiftString.Equal(type, "fileChange")) // :830-833
        {
            var paths = (item["changes"]?.JSONValueArray ?? []).Select(c => c["path"]?.JSONValueString).Where(p => p is not null).ToList();
            var title = paths.Count == 1 ? ToolTitles.RelativePath(paths[0]!, WorkingDirectory) : $"Edit {paths.Count} files";
            return new ToolCall { ToolCallKind = ToolCall.Kind.Edit, Title = title };
        }
        if (SwiftString.Equal(type, "mcpToolCall")) // :834-840
        {
            var server = item["server"]?.JSONValueString ?? "MCP";
            var tool = item["tool"]?.JSONValueString ?? "tool";
            var arguments = item["arguments"]?.CompactString;
            return new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = server + " " + Dot + " " + tool, Detail = arguments == "null" ? null : arguments };
        }
        if (SwiftString.Equal(type, "dynamicToolCall")) return new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = item["tool"]?.JSONValueString ?? "Tool" }; // :841
        if (SwiftString.Equal(type, "webSearch")) return new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = item["query"]?.JSONValueString ?? "Web search" }; // :842
        if (SwiftString.Equal(type, "collabAgentToolCall")) // :843-858
        {
            var tool = item["tool"]?.JSONValueString;
            var verb = tool switch
            {
                { } t when SwiftString.Equal(t, "spawnAgent") => "Send out a head",
                { } t when SwiftString.Equal(t, "sendInput") || SwiftString.Equal(t, "sendMessage") || SwiftString.Equal(t, "followupTask") => "Brief a head",
                { } t when SwiftString.Equal(t, "wait") => "Wait for heads",
                { } t when SwiftString.Equal(t, "closeAgent") || SwiftString.Equal(t, "interruptAgent") => "Recall a head",
                { } t when SwiftString.Equal(t, "resumeAgent") => "Resume a head",
                { } t when SwiftString.Equal(t, "listAgents") => "List heads",
                _ => "Agent",
            };
            var brief = item["prompt"]?.JSONValueString is { } prompt ? TextCleanup.SingleLine(prompt, 90) : null;
            return brief is null
                ? new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = verb }
                : new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = brief, Detail = verb };
        }
        if (SwiftString.Equal(type, "subAgentActivity")) return null; // :859-861
        if (SwiftString.Equal(type, "imageView")) return new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = ToolTitles.RelativePath(item["path"]?.JSONValueString ?? "Image", WorkingDirectory) }; // :862-864
        if (SwiftString.Equal(type, "imageGeneration")) return new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = item["revisedPrompt"]?.JSONValueString ?? "Generate image" }; // :865-866
        if (SwiftString.Equal(type, "enteredReviewMode") || SwiftString.Equal(type, "exitedReviewMode")) // :867-869
        {
            return new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = "Review", Detail = item["review"]?.JSONValueString };
        }
        return null; // :870
    }

    // :873-904. PR-PROVCLI-122.
    private ToolUpdate ToolUpdateFrom(JSONValue item)
    {
        var type = item["type"]?.JSONValueString;
        var rawStatus = item["status"]?.JSONValueString;
        if (type is not null && SwiftString.Equal(type, "commandExecution")) // :874-878
        {
            var exitCode = item["exitCode"]?.JSONValueInt;
            return new ToolUpdate
            {
                Output = item["aggregatedOutput"]?.JSONValueString,
                ExitCode = exitCode,
                Status = ToolStatus(rawStatus, exitCode),
            };
        }
        if (type is not null && SwiftString.Equal(type, "fileChange")) // :879-887
        {
            var edits = new List<FileEdit>();
            foreach (var change in item["changes"]?.JSONValueArray ?? [])
            {
                var diff = change["diff"]?.JSONValueString;
                var stats = ToolTitles.DiffStats(diff ?? "");
                edits.Add(new FileEdit(ToolTitles.RelativePath(change["path"]?.JSONValueString ?? "", WorkingDirectory), diff, stats.Additions, stats.Deletions));
            }
            return new ToolUpdate { Edits = new ValueList<FileEdit>(edits), Status = ToolStatus(rawStatus, null) };
        }
        if (type is not null && SwiftString.Equal(type, "mcpToolCall")) // :888-895
        {
            string? output;
            if (item["error"]?["message"]?.JSONValueString is { } message) output = message;
            else output = string.Join("\n", (item["result"]?["content"]?.JSONValueArray ?? []).Select(v => v["text"]?.JSONValueString).Where(t => t is not null));
            return new ToolUpdate { Output = output, Status = ToolStatus(rawStatus, null) };
        }
        if (type is not null && (SwiftString.Equal(type, "webSearch") || SwiftString.Equal(type, "imageView") ||
                                  SwiftString.Equal(type, "enteredReviewMode") || SwiftString.Equal(type, "exitedReviewMode"))) // :896-899
        {
            return new ToolUpdate { Status = ToolCall.Status.Completed };
        }
        return new ToolUpdate { Status = ToolStatus(rawStatus, null) }; // :900-903
    }

    // :906-915. PR-PROVCLI-123.
    private static ToolCall.Status ToolStatus(string? raw, long? exitCode)
    {
        if (raw is not null && SwiftString.Equal(raw, "inProgress")) return ToolCall.Status.Running; // :908
        if (raw is not null && (SwiftString.Equal(raw, "failed") || SwiftString.Equal(raw, "interrupted"))) return ToolCall.Status.Failed; // :909-910
        if (raw is not null && SwiftString.Equal(raw, "declined")) return ToolCall.Status.Declined; // :911
        return exitCode is { } code && code != 0 ? ToolCall.Status.Failed : ToolCall.Status.Completed; // :913-914
    }

    // PORT-ONLY (SK-11b, DL-12): a listener that throws must never break the session.
    private void Tap(IReadOnlyList<ToolEvent> events)
    {
        if (OnToolEvent is not { } listener) return;
        foreach (var toolEvent in events)
        {
            try { listener(toolEvent); }
            catch (Exception error) { Log.Error($"a tool event listener threw: {error.GetType().Name}"); }
        }
    }

    // :929-996. PR-PROVCLI-124, 125, 126, 127, 128, 129.
    private void HandleRequest(RPCID id, string method, JSONValue parameters)
    {
        // PORT-ONLY (ED-439): a server request (approval, question, tool) on a waiting thread means something ran. It is never answered.
        if (standbyActive || standbyDiscarding)
        {
            if (standbyActive) StandbyActivity(method);
            return;
        }

        if (SwiftString.Equal(method, "item/commandExecution/requestApproval")) // :931
        {
            var (options, payloads) = DecisionOptions(parameters["availableDecisions"]?.JSONValueArray, command: true); // :932
            if (skills is not null && CodexAlwaysAllow.Withholds(parameters["availableDecisions"]?.JSONValueArray, skills.ViewsRoot))
            {
                // PORT-ONLY (SK-11b, WU-7): no "Always allow" for a skill script; Approve and Approve for session remain.
                options = new ValueList<ApprovalRequest.Option>(options.Where(o => o.Id != "amend"));
                payloads = new ValueMap<string, JSONValue>(payloads.Where(p => p.Key != "amend").ToDictionary(p => p.Key, p => p.Value));
            }
            if (CodexToolTaps.Request(method, parameters) is { } held) Tap([held]);
            pendingRequests[id.Key] = new PendingRequest.Decision(id, payloads); // :933
            var command = parameters["command"]?.JSONValueString is { } rawCommand ? ToolTitles.UnwrapShell(rawCommand) : "Command"; // :934
            var cwd = parameters["cwd"]?.JSONValueString is { } cwdRaw ? ToolTitles.RelativePath(cwdRaw, WorkingDirectory) : null; // :937
            OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                Id: id.Key,
                RequestKind: ApprovalRequest.Kind.Command,
                Title: command,
                Options: options,
                Detail: cwd,
                Reason: parameters["reason"]?.JSONValueString,
                ToolItemID: parameters["itemId"]?.JSONValueString))); // :935-943
            return;
        }
        if (SwiftString.Equal(method, "item/fileChange/requestApproval")) // :944
        {
            var (options, payloads) = DecisionOptions(null, command: false); // :945
            pendingRequests[id.Key] = new PendingRequest.Decision(id, payloads); // :946
            OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                Id: id.Key,
                RequestKind: ApprovalRequest.Kind.FileChange,
                Title: "Apply file changes",
                Options: options,
                Detail: parameters["grantRoot"]?.JSONValueString,
                Reason: parameters["reason"]?.JSONValueString,
                ToolItemID: parameters["itemId"]?.JSONValueString))); // :947-955
            return;
        }
        if (SwiftString.Equal(method, "item/permissions/requestApproval")) // :956
        {
            var requested = parameters["permissions"] ?? new JSONValue.Object(ValueMap<string, JSONValue>.Empty); // :957
            pendingRequests[id.Key] = new PendingRequest.Permissions(id, requested); // :958
            OnEvent?.Invoke(new ProviderEvent.Approval(new ApprovalRequest(
                Id: id.Key,
                RequestKind: ApprovalRequest.Kind.Permissions,
                Title: DescribePermissions(requested),
                Options: new ValueList<ApprovalRequest.Option>(
                [
                    new ApprovalRequest.Option("turn", "Allow once", ApprovalRequest.Option.Role.Approve),
                    new ApprovalRequest.Option("session", "Allow for session", ApprovalRequest.Option.Role.ApproveAlways),
                    new ApprovalRequest.Option("decline", "Decline", ApprovalRequest.Option.Role.Decline),
                ]),
                Detail: null,
                Reason: parameters["reason"]?.JSONValueString,
                ToolItemID: parameters["itemId"]?.JSONValueString))); // :959-971
            return;
        }
        if (SwiftString.Equal(method, "item/tool/requestUserInput")) // :972
        {
            var questions = new List<QuestionRequest.Question>();
            foreach (var question in parameters["questions"]?.JSONValueArray ?? []) // :974
            {
                if (question["id"]?.JSONValueString is not { } questionID) continue; // :975
                var choices = new List<QuestionRequest.Choice>();
                foreach (var option in question["options"]?.JSONValueArray ?? [])
                {
                    if (option["label"]?.JSONValueString is { } label) choices.Add(new QuestionRequest.Choice(label, option["description"]?.JSONValueString));
                }
                questions.Add(new QuestionRequest.Question(
                    questionID,
                    question["header"]?.JSONValueString ?? "",
                    question["question"]?.JSONValueString ?? "",
                    new ValueList<QuestionRequest.Choice>(choices),
                    false,
                    question["isOther"]?.JSONValueBool ?? false,
                    question["isSecret"]?.JSONValueBool ?? false)); // :976-985
            }
            pendingRequests[id.Key] = new PendingRequest.Question(id); // :986
            OnEvent?.Invoke(new ProviderEvent.Question(new QuestionRequest(id.Key, new ValueList<QuestionRequest.Question>(questions)))); // :987
            return;
        }
        if (SwiftString.Equal(method, "mcpServer/elicitation/request")) // :989
        {
            // PORT-ONLY (Tactus, ED-NEW-pcctl-41): nth-pc never asks for input itself, so its elicitation is Codex's own
            // approval of one nth-pc call, which nth's per-app approval already governs (DESIGN.md 3.4, 4.1).
            if (pcServer is not null && Port.PcControl.PcProviderWiring.IsCodexElicitation(parameters))
            {
                connection?.Respond(id, Port.PcControl.PcProviderWiring.CodexElicitationAccept());
                return;
            }
            connection?.Respond(id, new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
            {
                ["action"] = "decline", // :990
            })));
            var server = parameters["serverName"]?.JSONValueString ?? "An MCP server"; // :991
            OnEvent?.Invoke(new ProviderEvent.Notice(new Notice
            {
                NoticeLevel = Notice.Level.Warning,
                Message = server + " asked for input, which nth cannot show yet.", // :992. DEVIATION OD-0012 F1: upstream "Droppy Code".
            }));
            return;
        }
        connection?.Respond(id, -32601, $"nth does not support {method}."); // :993-995. DEVIATION OD-0012 F1: upstream "Droppy Code".
    }

    // :998-1023. PR-PROVCLI-130.
    private static (ValueList<ApprovalRequest.Option> Options, ValueMap<string, JSONValue> Payloads) DecisionOptions(
        IReadOnlyList<JSONValue>? available, bool command)
    {
        var decisions = available ?? new JSONValue[]
        {
            new JSONValue.String("accept"), new JSONValue.String("acceptForSession"),
            new JSONValue.String("decline"), new JSONValue.String("cancel"),
        }; // :999
        var options = new List<ApprovalRequest.Option>();
        var payloads = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (decision.JSONValueString is { } name) // :1001
            {
                ApprovalRequest.Option? option = null;
                if (SwiftString.Equal(name, "accept")) option = new ApprovalRequest.Option(name, "Approve", ApprovalRequest.Option.Role.Approve); // :1008
                else if (SwiftString.Equal(name, "acceptForSession")) option = new ApprovalRequest.Option(name, "Approve for session", ApprovalRequest.Option.Role.ApproveAlways); // :1009
                else if (SwiftString.Equal(name, "decline")) option = new ApprovalRequest.Option(name, "Decline", ApprovalRequest.Option.Role.Decline); // :1010
                else if (SwiftString.Equal(name, "cancel")) option = new ApprovalRequest.Option(name, "Stop turn", ApprovalRequest.Option.Role.Cancel); // :1011
                if (option is not null) // :1012-1015
                {
                    options.Add(option);
                    payloads[name] = decision;
                }
            }
            else if (command && decision["acceptWithExecpolicyAmendment"] is not null) // :1016-1019
            {
                options.Add(new ApprovalRequest.Option("amend", "Always allow", ApprovalRequest.Option.Role.ApproveAlways));
                payloads["amend"] = decision;
            }
        }

        static int Order(ApprovalRequest.Option.Role role) => role switch // :1022
        {
            ApprovalRequest.Option.Role.Approve => 0,
            ApprovalRequest.Option.Role.ApproveAlways => 1,
            ApprovalRequest.Option.Role.Decline => 2,
            ApprovalRequest.Option.Role.Cancel => 3,
            _ => 0,
        };
        var sorted = options.OrderBy(o => Order(o.OptionRole)).ToList(); // :1022 stable sort
        return (new ValueList<ApprovalRequest.Option>(sorted), new ValueMap<string, JSONValue>(payloads));
    }

    // :1025-1037. Used by PR-PROVCLI-126.
    private static string DescribePermissions(JSONValue permissions)
    {
        var parts = new List<string>();
        if (permissions["fileSystem"]?["write"]?.JSONValueArray is { Count: > 0 } write) // :1027-1029
        {
            parts.Add("Write to " + string.Join(", ", write.Select(v => v.JSONValueString).Where(v => v is not null)));
        }
        if (permissions["fileSystem"]?["read"]?.JSONValueArray is { Count: > 0 } read) // :1030-1032
        {
            parts.Add("Read " + string.Join(", ", read.Select(v => v.JSONValueString).Where(v => v is not null)));
        }
        if (permissions["network"]?["enabled"]?.JSONValueBool == true) parts.Add("Network access"); // :1033
        return parts.Count == 0 ? "Additional permissions" : string.Join(" " + Dot + " ", parts); // :1034-1036
    }

    // :1039-1045. Ported in part 1: see the file header note (the connection Start() wires this to can close
    // on any of part 1's own paths).
    private void HandleClose(string tail)
    {
        var keys = pendingRequests.Keys.ToArray(); // :1040
        pendingRequests.Clear();                    // :1041
        foreach (var key in keys)
        {
            OnEvent?.Invoke(new ProviderEvent.RequestResolved(key)); // :1042
        }

        if (!isStopping) // :1044
        {
            OnEvent?.Invoke(new ProviderEvent.Exited(TextCleanup.LastLines(tail))); // :1045
        }
    }

    // :917-925
    public static string ErrorMessage(JSONValue? error)
    {
        if (error is null) return "Codex reported an error."; // :918
        var raw = error["message"]?.JSONValueString ?? error.DisplayText; // :919
        if (JSONValue.Parse(raw) is { } nested &&
            (nested["error"]?["message"]?.JSONValueString ?? nested["message"]?.JSONValueString) is { } message) // :920-921
        {
            return message; // :922
        }
        return raw; // :924
    }

    // :1047-1058
    private static PolicySettings PolicySettingsFor(RuntimeMode mode) => mode switch
    {
        RuntimeMode.Supervised => new PolicySettings( // :1048-1050
            new JSONValue.String("untrusted"),
            new JSONValue.String("read-only"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "readOnly" })),
            new JSONValue.String("user")),
        RuntimeMode.AutoAcceptEdits => new PolicySettings( // :1051-1053
            new JSONValue.String("on-request"),
            new JSONValue.String("workspace-write"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "workspaceWrite" })),
            new JSONValue.String("user")),
        RuntimeMode.Auto => new PolicySettings( // :1054-1056
            new JSONValue.String("on-request"),
            new JSONValue.String("workspace-write"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "workspaceWrite" })),
            new JSONValue.String("auto_review")),
        RuntimeMode.FullAccess => new PolicySettings( // :1057-1058 approximately (table's fourth row)
            new JSONValue.String("never"),
            new JSONValue.String("danger-full-access"),
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["type"] = "dangerFullAccess" })),
            new JSONValue.String("user")),
        _ => throw new UnreachableException(),
    };
}
