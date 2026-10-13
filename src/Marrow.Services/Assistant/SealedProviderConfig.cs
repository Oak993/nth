// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a "Mission chats are sealed", 12.3 seams 2 and 3; unit AS-5;
// ED-NEW-dextra-as5-3). No Swift twin.
// Every chat of a mission (lead, head, helper, reviewer) launches sealed: in the ask mode (never Full access, Auto or
// Auto-accept edits, so every risky step surfaces as an approval nth's policy engine sees), with none of the owner's
// user-level or project-level allow rules, auto-approve lists, hooks or permission bypasses, and with only the MCP
// servers the owning assistant's profile lists (plus nth-dextra). Providers whose own allow rules nth cannot seal yet
// refuse mission work (fail closed). ThreadRuntime applies Seal at every launch and every turn of a chat that carries a
// MissionID; the Claude and Codex sessions honour SessionConfiguration.Sealed themselves too.
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Providers;

namespace Marrow.Services.Assistant;

/// <summary>What a sealed launch carries: its mission and the owner's MCP servers the mission may use.</summary>
public sealed record SealedLaunch(Guid MissionId)
{
    /// <summary>The names of the owner's connected MCP servers the owning assistant's profile lists (12.1a).</summary>
    public ValueList<string> Connections { get; init; } = ValueList<string>.Empty;
}

/// <summary>The sealed provider configuration of 12.1a.</summary>
public static partial class SealedProviderConfig
{
    /// <summary>The capped mode of every mission chat: the provider's ask mode.</summary>
    public const RuntimeMode CappedMode = RuntimeMode.Supervised;

    /// <summary>The Claude Code settings sources a sealed chat loads: none (no user, project or local settings file).</summary>
    public const string ClaudeSettingSources = "";

    /// <summary>
    /// Whether nth can seal this provider: Claude Code (setting sources, flag settings, strict MCP config), Codex (its ask
    /// policy, its servers, and a refusal while its own "Always allow" rules exist), and the API and local providers, whose
    /// tools and approvals nth runs itself. The other CLIs keep allow lists of their own that nth cannot switch off yet.
    /// </summary>
    public static bool CanSeal(ProviderKind provider) =>
        provider is ProviderKind.Claude or ProviderKind.Codex or ProviderKind.Local || provider.IsAPIKeyBased;

    /// <summary>Null when the provider can run mission work; else why not, for the board's Blocked line.</summary>
    public static string? Refusal(ProviderKind provider) => CanSeal(provider)
        ? null
        : $"{provider.DisplayName} cannot run mission work yet: nth cannot switch off its own allow rules. Use Claude, Codex or an API provider.";

    /// <summary>The mode a mission chat runs in, whatever it asked for.</summary>
    public static RuntimeMode Cap(RuntimeMode requested) => CappedMode;

    /// <summary>Whether a mode lets anything past nth's policy engine (Full access, the provider's own reviewer, edits alone).</summary>
    public static bool Bypasses(RuntimeMode mode) => mode != CappedMode;

    /// <summary>A launch, sealed: the capped mode and the seal. Throws for a provider nth cannot seal.</summary>
    public static SessionConfiguration Seal(SessionConfiguration configuration, SealedLaunch seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        if (Refusal(configuration.Provider) is { } refused) throw new ProviderError(new ProviderError.Case.Failed(refused));
        return configuration with { RuntimeMode = CappedMode, Sealed = seal };
    }

    /// <summary>A turn of a sealed chat: always in the capped mode.</summary>
    public static TurnInput Seal(TurnInput input) => input with { RuntimeMode = CappedMode };

    /// <summary>
    /// The option a sealed chat answers with when the owner allows a step: the provider's "once" option, never one that
    /// keeps a rule for the session or for good (Approve for session, Always allow). Null when the request offers none.
    /// </summary>
    public static ApprovalRequest.Option? OnceOption(ApprovalRequest request) =>
        request.Options.FirstOrDefault(o => o.OptionRole == ApprovalRequest.Option.Role.Approve);

    /// <summary>The option a sealed chat answers with when a step is refused: Decline, or Cancel when there is no Decline.</summary>
    public static ApprovalRequest.Option? DeclineOption(ApprovalRequest request) =>
        request.Options.FirstOrDefault(o => o.OptionRole == ApprovalRequest.Option.Role.Decline)
        ?? request.Options.FirstOrDefault(o => o.OptionRole == ApprovalRequest.Option.Role.Cancel);

    // ---- Claude Code ----

    /// <summary>
    /// The flag settings of a sealed Claude chat (--settings, the highest file-free layer): no allow, deny or ask rules,
    /// the ask mode, bypass mode switched off, no hooks, no project MCP servers. <paramref name="others"/> are the launch's
    /// other flag settings (fast mode, the handshake agent); the seal's keys win any clash.
    /// </summary>
    public static string ClaudeFlagSettings(IReadOnlyDictionary<string, JSONValue>? others = null)
    {
        var flags = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        if (others is not null) foreach (var (key, value) in others) flags[key] = value;
        flags["permissions"] = JSONValue.FromDictionaryLiteral(
            ("allow", JSONValue.FromArrayLiteral()),
            ("deny", JSONValue.FromArrayLiteral()),
            ("ask", JSONValue.FromArrayLiteral()),
            ("additionalDirectories", JSONValue.FromArrayLiteral()),
            ("defaultMode", "default"),
            ("disableBypassPermissionsMode", "disable"));
        flags["disableAllHooks"] = true;
        flags["enableAllProjectMcpServers"] = false;
        flags["enabledMcpjsonServers"] = JSONValue.FromArrayLiteral();
        return new JSONValue.Object(new ValueMap<string, JSONValue>(flags)).CompactString;
    }

    /// <summary>
    /// The mcpServers object of a sealed Claude chat: the owner's connected servers the seal lists, then the built-ins
    /// (nth-dextra, the advisor), which replace a connected server of the same name.
    /// </summary>
    public static JSONValue ClaudeServers(JSONValue? connected, SealedLaunch seal, IReadOnlyList<MCPResolvedServer> builtIns)
    {
        var servers = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        foreach (var (name, entry) in connected?.JSONValueObject ?? new Dictionary<string, JSONValue>())
            if (Listed(seal, name)) servers[name] = entry;
        foreach (var (name, entry) in MCPProviderConfig.Claude(builtIns)["mcpServers"]!.JSONValueObject!) servers[name] = entry;
        return new JSONValue.Object(new ValueMap<string, JSONValue>(servers));
    }

    /// <summary>
    /// Writes a sealed Claude chat's own --mcp-config file (always followed by --strict-mcp-config, so the owner's own
    /// servers never load beside it) and returns its path. Throws when it cannot be written: the launch fails closed.
    /// </summary>
    public static string WriteClaudeMcpConfig(SealedLaunch seal, IReadOnlyList<MCPResolvedServer> builtIns, Guid? thread)
    {
        var root = JSONValue.FromDictionaryLiteral(("mcpServers", ClaudeServers(MCPProviderConfig.ClaudeConnectedServers(), seal, builtIns)));
        var path = SwiftPath.AppendingPathComponent(MCPPaths.Directory, "sealed-" + (thread ?? seal.MissionId).ToString("N") + ".json");
        FileOps.WriteAtomic(path, root.Data(pretty: true));
        return path;
    }

    /// <summary>
    /// The launch arguments of a sealed Claude chat, from the ones an ordinary chat would use: the settings sources
    /// emptied, --allow-dangerously-skip-permissions removed, and the permission mode never bypassPermissions.
    /// </summary>
    public static List<string> ClaudeArguments(IReadOnlyList<string> ordinary)
    {
        var sealedArguments = new List<string>(ordinary.Count);
        for (var i = 0; i < ordinary.Count; i++)
        {
            var argument = ordinary[i];
            if (argument == "--allow-dangerously-skip-permissions" || argument == "--dangerously-skip-permissions") continue;
            sealedArguments.Add(argument);
            if (i + 1 >= ordinary.Count) continue;
            if (argument == "--setting-sources") { sealedArguments.Add(ClaudeSettingSources); i++; }
            else if (argument == "--permission-mode")
            {
                var mode = ordinary[i + 1];
                sealedArguments.Add(mode == "plan" ? "plan" : "default");
                i++;
            }
        }
        return sealedArguments;
    }

    /// <summary>A permission mode name a sealed Claude chat may switch to: plan, or the ask mode.</summary>
    public static string ClaudeMode(string requested) => requested == "plan" ? "plan" : "default";

    /// <summary>
    /// Whether the settings a sealed Claude chat reports (the get_settings control reply) keep the seal: the effective
    /// permissions hold no allow rule and the mode is not bypassPermissions. Null reply: the CLI cannot say (older
    /// versions), so the launch flags stand alone.
    /// </summary>
    public static string? ClaudeSealBroken(JSONValue? settings)
    {
        if (settings?["effective"] is not { } effective) return null;
        if (effective["permissions"]?["allow"]?.JSONValueArray is { Count: > 0 } allow)
            return $"Claude Code still applies {allow.Count} allow rule(s) to this chat.";
        if (effective["permissions"]?["defaultMode"]?.JSONValueString is "bypassPermissions")
            return "Claude Code would run this chat with permissions bypassed.";
        return null;
    }

    // ---- Codex ----

    /// <summary>
    /// The mcp_servers override of a sealed Codex chat: the owner's connected servers the seal lists; every other
    /// connected server and every server of the account's own config.toml switched off; then the built-ins.
    /// </summary>
    public static JSONValue CodexServers(JSONValue? connected, IEnumerable<string> userServerNames, SealedLaunch seal, IReadOnlyList<MCPResolvedServer> builtIns)
    {
        var off = JSONValue.FromDictionaryLiteral(("enabled", false));
        var servers = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        foreach (var (name, entry) in connected?.JSONValueObject ?? new Dictionary<string, JSONValue>())
            servers[name] = Listed(seal, name) ? entry : off;
        foreach (var name in userServerNames)
            if (!servers.ContainsKey(name)) servers[name] = off;
        return MCPProviderConfig.CodexBuiltIns(new JSONValue.Object(new ValueMap<string, JSONValue>(servers)), builtIns)!;
    }

    /// <summary>The Codex home a session reads: CODEX_HOME from its environment, else ~/.codex.</summary>
    public static string CodexHome(IReadOnlyDictionary<string, string> environment, string homeDirectory) =>
        environment.TryGetValue("CODEX_HOME", out var home) && !string.IsNullOrWhiteSpace(home)
            ? home
            : Path.Combine(homeDirectory, ".codex");

    /// <summary>
    /// The rules files of a Codex home that hold an "allow" decision (Codex's "Always allow", kept as prefix rules that
    /// run a command without asking whatever the thread's approval policy). A sealed chat refuses to launch while any
    /// exists, because nth cannot switch them off for one thread. Unreadable files count as holding one (fail closed).
    /// </summary>
    public static IReadOnlyList<string> CodexAllowRules(string codexHome)
    {
        var folder = Path.Combine(codexHome, "rules");
        if (!Directory.Exists(folder)) return [];
        var found = new List<string>();
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*.rules", SearchOption.AllDirectories).ToList(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return [folder]; }
        foreach (var file in files)
        {
            try
            {
                if (AllowDecision().IsMatch(File.ReadAllText(file))) found.Add(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { found.Add(file); }
        }
        return found;
    }

    /// <summary>The refusal of a sealed Codex launch while allow rules exist; null when there are none.</summary>
    public static string? CodexRefusal(IReadOnlyDictionary<string, string> environment, string homeDirectory) =>
        CodexAllowRules(CodexHome(environment, homeDirectory)) is { Count: > 0 } rules
            ? $"Codex keeps \"Always allow\" rules ({Path.GetFileName(rules[0])}), which would run commands in a mission without asking. "
              + "Remove them, or run the mission on another account or provider."
            : null;

    /// <summary>The Codex option a sealed chat answers with: the once option for any that would keep a rule.</summary>
    public static string CodexOnce(string optionId) => optionId switch
    {
        "acceptForSession" or "amend" => "accept",
        "session" => "turn",
        _ => optionId,
    };

    [GeneratedRegex(@"decision\s*=\s*[""']allow[""']", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AllowDecision();

    private static bool Listed(SealedLaunch seal, string name) =>
        seal.Connections.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
}
