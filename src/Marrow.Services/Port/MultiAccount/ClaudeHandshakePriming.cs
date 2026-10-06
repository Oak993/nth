// PORT-ONLY: OD-0114. Published CLI agent restriction and same-process flag restoration.
// Sources: code.claude.com/docs/en/sub-agents; CLI reference; Agent SDK CHANGELOG 0.3.161.
// Unknown versions, settings shapes, managed actions or missing native init proof fail closed.
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
namespace Marrow.Services.Providers;
public sealed partial class ClaudeSession
{
    private string? handshakeAgent;
    private string? handshakeVersion;
    private string? handshakeIdentity;
    private JSONValue? handshakeSources;
    private JSONValue? handshakeNormalFlags;
    private bool handshakeFailed;
    internal Func<bool>? CanTransferContext { get; set; }
    internal bool HandshakeToolsRestored { get; private set; }
    internal string VerifiedHandshakeIdentity => handshakeIdentity ?? throw new InvalidOperationException("The CLI did not acknowledge its text-only session.");

    internal async Task<string> StartHandshakeAsync(CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20), SwiftClock.Provider.Current);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        ct = linked.Token;
        if (process is not null || configuration.ResumeID is not null)
            throw new InvalidOperationException("Context transfer needs a fresh private session.");
        var executable = configuration.Executable ?? throw new InvalidOperationException("The CLI is unavailable.");
        var probe = await ClaudeProviderRuntime.Version.Current(executable, ProviderChildEnvironment.Apply(configuration.Environment)).WaitAsync(ct);
        var match = Regex.Match(Encoding.UTF8.GetString(probe.Stdout), @"^2\.1\.(\d+) \(Claude Code\)\s*$", RegexOptions.CultureInvariant);
        if (!probe.Succeeded || !match.Success || !int.TryParse(match.Groups[1].Value, out var patch) || patch < 281)
            throw new NotSupportedException("This CLI has no verified text-only context-transfer capability.");
        handshakeVersion = "2.1." + patch;
        handshakeAgent = "marrow-handshake-" + Guid.NewGuid().ToString("N");
        var id = await Start().WaitAsync(ct);
        await CheckHandshakePolicyAsync().WaitAsync(ct);
        return id;
    }

    private string HandshakeLaunchSettings()
    {
        var flags = new Dictionary<string, JSONValue>();
        if (configuration.FastMode || handshakeAgent is not null) flags["fastMode"] = configuration.FastMode;
        if (handshakeAgent is not null) { flags["agent"] = handshakeAgent; flags["disableAllHooks"] = true; }
        return new JSONValue.Object(new ValueMap<string, JSONValue>(flags)).CompactString;
    }

    private JSONValue HandshakeAgents(JSONValue existing)
    {
        if (handshakeAgent is null) return existing;
        var agents = new Dictionary<string, JSONValue>(existing.JSONValueObject ?? throw new InvalidOperationException("Invalid native agents."));
        agents.Add(handshakeAgent, JSONValue.FromDictionaryLiteral(("description", "Private text-only context transfer"),
            ("prompt", ""), ("tools", JSONValue.FromArrayLiteral()), ("disallowedTools", JSONValue.FromArrayLiteral("*", "mcp__*"))));
        return new JSONValue.Object(new ValueMap<string, JSONValue>(agents));
    }

    private async Task CheckHandshakePolicyAsync()
    {
        if (handshakeFailed || handshakeAgent is null) throw new InvalidOperationException("Text-only context transfer is unavailable.");
        var settings = await Control(JSONValue.FromDictionaryLiteral(("subtype", "get_settings")));
        var (sources, flags) = HandshakeSettings(settings);
        if (settings["effective"]?["agent"]?.JSONValueString != handshakeAgent
            || settings["effective"]?["disableAllHooks"]?.JSONValueBool != true
            || flags["agent"]?.JSONValueString != handshakeAgent || flags["disableAllHooks"]?.JSONValueBool != true)
            throw new InvalidOperationException("The CLI did not enforce its text-only policy.");
        var policy = sources["policySettings"];
        if (policy?["disableAllHooks"]?.JSONValueBool != true
            && (HasSetting(policy?["hooks"]) || HasSetting(policy?["statusLine"]) || HasSetting(policy?["fileSuggestion"])))
            throw new NotSupportedException("Managed actions prevent verified text-only context transfer.");
        var normal = new JSONValue.Object(new ValueMap<string, JSONValue>(flags.JSONValueObject!.Where(pair => pair.Key is not ("agent" or "disableAllHooks"))));
        if (handshakeSources is not null && (!handshakeSources.Equals(sources) || !handshakeNormalFlags!.Equals(normal)))
            throw new InvalidOperationException("The CLI settings changed while context was being transferred.");
        handshakeSources = sources;
        handshakeNormalFlags = normal;
    }

    private static (JSONValue Sources, JSONValue Flags) HandshakeSettings(JSONValue response)
    {
        if (response["effective"] is not JSONValue.Object || response["sources"] is not JSONValue.Object raw
            || raw["flagSettings"] is not JSONValue.Object flags)
            throw new NotSupportedException("The CLI cannot report its effective context-transfer policy.");
        // get_settings is an untyped SDK response. Accept this known shape only, including policySettings.
        if (raw.Value.Any(pair => pair.Key is not ("userSettings" or "projectSettings" or "localSettings" or "flagSettings" or "policySettings")
            || pair.Value is not JSONValue.Object))
            throw new NotSupportedException("The CLI reported an unknown settings source.");
        return (new JSONValue.Object(new ValueMap<string, JSONValue>(raw.Value.Where(pair => pair.Key != "flagSettings"))), flags);
    }

    private static bool HasSetting(JSONValue? value) => value is not null and not JSONValue.Null
        && (value is not JSONValue.Object obj || obj.Value.Count > 0);

    private bool VerifyHandshakeIdentity(JSONValue message)
    {
        if (message["session_id"]?.JSONValueString is not { Length: > 0 } id
            || message["claude_code_version"]?.JSONValueString != handshakeVersion
            || message["tools"]?.JSONValueArray is not { Count: 0 }
            || handshakeIdentity is not null && handshakeIdentity != id)
        {
            FailHandshake("The CLI did not acknowledge a stable text-only session.");
            return false;
        }
        handshakeIdentity = id;
        return true;
    }

    private bool RejectHandshakeAction(JSONValue message)
    {
        if (handshakeAgent is null) return false;
        var type = message["type"]?.JSONValueString;
        var toolRequest = type == "control_request" && message["request"]?["subtype"]?.JSONValueString == "can_use_tool";
        var action = toolRequest || type == "stream_event" && message["event"]?["content_block"]?["type"]?.JSONValueString == "tool_use"
            || type is "assistant" or "user" && (message["message"]?["content"]?.JSONValueArray ?? []).Any(block => block["type"]?.JSONValueString is "tool_use" or "tool_result")
            || type == "system" && message["subtype"]?.JSONValueString is "task_started" or "task_progress" or "task_notification";
        if (!action) return handshakeFailed;
        if (toolRequest && message["request_id"]?.JSONValueString is { } request)
            SendToolResponse(request, JSONValue.FromDictionaryLiteral(("behavior", "deny"), ("message", "Context transfer permits no actions."), ("interrupt", true)));
        FailHandshake("The CLI attempted an action during text-only context transfer.");
        return true;
    }

    private void FailHandshake(string reason)
    {
        handshakeFailed = true;
        OnEvent?.Invoke(new ProviderEvent.Exited(reason));
        Stop();
    }

    private static JSONValue? RestoredSetting(JSONValue sources, JSONValue flags, string key) =>
        sources["policySettings"]?[key] ?? flags[key] ?? sources["localSettings"]?[key]
            ?? sources["projectSettings"]?[key] ?? sources["userSettings"]?[key];

    private static bool RestoredSettingMatches(JSONValue? actual, JSONValue? expected, bool hooks)
    {
        if (expected is not null and not JSONValue.Null) return expected.Equals(actual);
        // With no configured value, the CLI's normal defaults are no custom agent and hooks enabled.
        return actual is null or JSONValue.Null || hooks && actual.JSONValueBool == false;
    }

    internal async Task RestoreHandshakeToolsAsync(CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20), SwiftClock.Provider.Current);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        ct = linked.Token;
        if (handshakeAgent is null || handshakeFailed || turnActive || handshakeSources is null || !IsRunning)
            throw new InvalidOperationException("The private CLI session is not ready for adoption.");
        _ = VerifiedHandshakeIdentity;
        await CheckHandshakePolicyAsync().WaitAsync(ct);
        await Control(JSONValue.FromDictionaryLiteral(("subtype", "apply_flag_settings"),
            ("settings", JSONValue.FromDictionaryLiteral(("agent", new JSONValue.Null()), ("disableAllHooks", new JSONValue.Null()))))).WaitAsync(ct);
        var settings = await Control(JSONValue.FromDictionaryLiteral(("subtype", "get_settings"))).WaitAsync(ct);
        var (sources, flags) = HandshakeSettings(settings);
        if (!handshakeSources.Equals(sources) || !handshakeNormalFlags!.Equals(flags)
            || !RestoredSettingMatches(settings["effective"]?["agent"], RestoredSetting(sources, flags, "agent"), hooks: false)
            || !RestoredSettingMatches(settings["effective"]?["disableAllHooks"], RestoredSetting(sources, flags, "disableAllHooks"), hooks: true))
            throw new InvalidOperationException("The CLI did not restore its normal settings.");
        var mode = ModeName(configuration.RuntimeMode, configuration.InteractionMode);
        await Control(JSONValue.FromDictionaryLiteral(("subtype", "set_permission_mode"), ("mode", mode))).WaitAsync(ct);
        permissionMode = mode;
        runtimeMode = configuration.RuntimeMode;
        if (!IsRunning || handshakeFailed) throw new InvalidOperationException("The CLI exited before adoption.");
        handshakeAgent = null;
        HandshakeToolsRestored = true;
    }
}
