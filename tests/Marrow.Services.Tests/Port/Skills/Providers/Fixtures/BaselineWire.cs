// PORT-ONLY: frozen wire builders from 7993466e (before SK-11c), CopilotSession and ACPSession.
// Copied verbatim except member visibility/name, so DL-11 compares with existing provider code, not a second implementation of SK-11c.
using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
namespace Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
public sealed class BaselineWire(SessionConfiguration configuration)
{
    private string? model = configuration.Model;
    private string? effort = configuration.Effort;
    private string WorkingDirectory => configuration.WorkingDirectory.Path;
    private bool UsesParameterizedModels => configuration.Provider == ProviderKind.Cursor;
    public Dictionary<string, JSONValue> CopilotParameters() // :117-158
    {
        var parameters = new Dictionary<string, JSONValue>(SwiftStringComparer.Instance) // :122-131
        {
            ["clientName"] = "nth", // DEVIATION OD-0012 F1: upstream sends "droppy-code" (:123).
            ["workingDirectory"] = WorkingDirectory,
            ["streaming"] = true,
            ["requestPermission"] = true,
            ["requestUserInput"] = true,
            ["requestExitPlanMode"] = true,
            ["includeSubAgentStreamingEvents"] = configuration.Hydra?.RunsNatively == true,
            ["isExperimentalMode"] = configuration.RuntimeMode == RuntimeMode.Auto,
        };
        if (model is { } m && m.Length != 0) parameters["model"] = m; // :132
        if (effort is { } e && e.Length != 0) parameters["reasoningEffort"] = e; // :133
        // The native or fallback Hydra policy, when Hydra is on: the system message below is assembled fresh, once,
        // with the anti-slop guidance alongside it. :134-136
        string? hydraPolicy = null;
        if (configuration.Hydra is { } hydra) // :137
        {
            if (hydra.RunsNatively) // :138-140
            {
                parameters["customAgents"] = new JSONValue.Array(HydraPrompts.CopilotAgents(hydra));
                hydraPolicy = HydraPrompts.Policy(ProviderKind.Copilot, hydra);
            }
            else
            {
                // Heads on another provider are app-run: the lead asks for them with the delegation block, and no
                // agents of the CLI's own are defined. :141-145
                hydraPolicy = HydraPrompts.FallbackPolicy(hydra);
            }
        }
        // Exactly one appended system message for every session, Hydra or not. The off notice goes out too, so a
        // resumed thread cannot keep earlier guidance alive. :147-156
        parameters["systemMessage"] = JSONValue.FromDictionaryLiteral(
            ("mode", "append"),
            ("content", string.Join("\n\n", new[] { hydraPolicy, AntiSlopPolicy.Instructions(configuration.AntiSlopEnabled) }.OfType<string>())));
        return parameters; // :157
    }

    public ValueList<string> LaunchArguments => configuration.Provider switch
    {
        ProviderKind.Cursor => configuration.RuntimeMode switch
        {
            RuntimeMode.Auto => new ValueList<string>(["--auto-review", "acp"]), // :82
            RuntimeMode.FullAccess => new ValueList<string>(["--force", "acp"]), // :83
            _ => new ValueList<string>(["acp"]), // :84 .supervised, .autoAcceptEdits
        },
        ProviderKind.Grok => configuration.RuntimeMode switch
        {
            RuntimeMode.Supervised => new ValueList<string>(["--permission-mode", "default", "agent", "stdio"]), // :88
            RuntimeMode.AutoAcceptEdits => new ValueList<string>(["--permission-mode", "acceptEdits", "agent", "stdio"]), // :89
            RuntimeMode.Auto => new ValueList<string>(["--permission-mode", "auto", "agent", "stdio"]), // :90
            RuntimeMode.FullAccess => new ValueList<string>(["agent", "--always-approve", "stdio"]), // :91
            _ => throw new UnreachableException(),
        },
        ProviderKind.Devin => configuration.RuntimeMode switch
        {
            // Devin's modes at a glance: auto approves read-only tools, accept-edits adds workspace edits,
            // smart adds model-judged-safe actions, dangerous approves everything. Passed before the
            // subcommand, like its CLI expects. :94-96
            RuntimeMode.Supervised => new ValueList<string>(["--permission-mode", "auto", "acp"]), // :98
            RuntimeMode.AutoAcceptEdits => new ValueList<string>(["--permission-mode", "accept-edits", "acp"]), // :99
            RuntimeMode.Auto => new ValueList<string>(["--permission-mode", "smart", "acp"]), // :100
            RuntimeMode.FullAccess => new ValueList<string>(["--permission-mode", "dangerous", "acp"]), // :101
            _ => throw new UnreachableException(),
        },
        _ => new ValueList<string>(["acp"]), // :103-104 .opencode, .opencodeGo (and any other ACP kind)
    };

    public JSONValue InitializeParams => JSONValue.FromDictionaryLiteral(
        ("protocolVersion", 1L),
        ("clientCapabilities", ClientCapabilities),
        ("clientInfo", JSONValue.FromDictionaryLiteral(
            ("name", "nth"),
            ("title", "Nth"),
            ("version", AppInfo.Current.Version))));

    public JSONValue ClientCapabilities // :156-165
    {
        get
        {
            var capabilities = new Dictionary<string, JSONValue> // :157-160
            {
                ["fs"] = JSONValue.FromDictionaryLiteral(("readTextFile", false), ("writeTextFile", false)),
                ["terminal"] = false,
            };
            if (UsesParameterizedModels) capabilities["_meta"] = JSONValue.FromDictionaryLiteral(("parameterizedModelPicker", true)); // :161-163
            return new JSONValue.Object(new ValueMap<string, JSONValue>(capabilities)); // :164
        }
    }

    public static IReadOnlyList<string> CopilotArguments()
    {
        var arguments = new List<string> { "--headless", "--stdio", "--no-auto-update", "--log-level", "error" };
        arguments.AddRange(MCPSessionOverrides.CopilotArguments());
        return arguments;
    }
    public bool canLoadSessions;
    public bool supportsHTTPMCP;
    private bool isReplaying;
    private string? sessionID;
    public (bool Replaying, string? Id) State => (isReplaying, sessionID);
    public async Task<JSONValue> OpenACP(Func<string, JSONValue, Task<JSONValue>> request)
    {
        // :252-253. The connected servers ride into every new session through ACP's own field, HTTP ones
        // directly where the agent says it speaks HTTP MCP, else through mcp-remote.
        var baseParams = new Dictionary<string, JSONValue>
        {
            ["cwd"] = WorkingDirectory, // :254
            ["mcpServers"] = MCPProviderConfig.AcpServers(supportsHTTPMCP), // :254
        };
        if (configuration.ResumeID is { } resumeID && canLoadSessions) // :255
        {
            var parameters = new Dictionary<string, JSONValue>(baseParams) { ["sessionId"] = resumeID }; // :256-257
            isReplaying = true; // :258
            var result = await SwiftTry.OptionalAsync<JSONValue>(async () =>
                await request("session/load", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters)))); // :259
            isReplaying = false; // :260
            if (result is { } r) // :261
            {
                sessionID = resumeID; // :262
                return r; // :263
            }
        }
        var newResult = await request("session/new", new JSONValue.Object(new ValueMap<string, JSONValue>(baseParams))); // :266
        sessionID = newResult["sessionId"]?.JSONValueString; // :267
        return newResult; // :268
    }

    public Task<JSONValue> OpenCopilot(string id, Func<string, JSONValue, Task<JSONValue>> request)
    {
        var parameters = CopilotParameters();
        parameters["sessionId"] = id;
        return request(configuration.ResumeID is null ? "session.create" : "session.resume",
            new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance)));
    }
}
