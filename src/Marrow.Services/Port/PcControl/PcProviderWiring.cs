// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-41; DESIGN.md 3.4, 4.1). How each CLI and ACP provider lets the nth-pc
// tools run. nth's own approval governs them (the engine's per-app ask, the guarded-action confirm, the pill), so a
// provider's own permission prompt for an nth-pc call is answered at once, and only in a session that was given the
// nth-pc server; every other prompt is untouched. Also the Codex overrides that keep a second computer-use controller
// out of an nth chat, and the folder where the screenshots go for a client whose bridge cannot carry images.
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;

namespace Marrow.Services.Port.PcControl;

public static partial class PcProviderWiring
{
    /// <summary>The server's name in every provider's config.</summary>
    public const string ServerName = PcToolSchemas.ServerName;

    /// <summary>Codex's per-server tool timeout for nth-pc (DESIGN.md 3.4, critique C10): above the 25 s cap plus a slow step.</summary>
    public const int CodexToolTimeoutSec = 120;

    /// <summary>Codex's own bundled computer-use plugins, turned off inside a chat that has nth-pc (DESIGN.md 3.4).</summary>
    public static IReadOnlyList<string> CodexComputerUsePlugins { get; } = ["computer-use@openai-bundled", "unified-computer-use@openai-bundled"];

    /// <summary>Claude Code's prefix for the server's tools.</summary>
    public const string ClaudeToolPrefix = "mcp__" + ServerName + "__";

    /// <summary>Where screenshots are saved for clients whose bridge drops image parts (Pi, Command Code); per chat below it.</summary>
    public static Ambient<Func<string>> ImageRoot { get; } = new(new Func<string>(() => Path.Combine(Path.GetTempPath(), "nth-pc")));

    /// <summary>The newest screenshot files a chat's image folder keeps (older ones are deleted when a new one is saved).</summary>
    public const int KeptImageFiles = 12;

    /// <summary>
    /// Every tool name the server can list (the fast helper and pc_ask included, and, PORT-ONLY web surfaces W11,
    /// ED-NEW-web-w11-1, the web_* tools, which nth's own Visus approval governs the same way).
    /// </summary>
    public static IReadOnlySet<string> ToolNames { get; } = new HashSet<string>(
        PcToolSchemas.All.Select(t => t.Name).Append(PcToolSchemas.Delegate).Append(PcToolSchemas.Ask)
            .Concat(Marrow.Core.Web.WebToolSchemas.All.Select(t => t.Name)), StringComparer.Ordinal);

    /// <summary>Claude Code: whether a can_use_tool request names one of the server's tools.</summary>
    public static bool IsClaudeTool(string? name) =>
        name is not null && name.StartsWith(ClaudeToolPrefix, StringComparison.Ordinal) && ToolNames.Contains(name[ClaudeToolPrefix.Length..]);

    /// <summary>
    /// ACP: the "allow once" option for a call of one of the server's tools, or null for anything else. Only a call of kind
    /// "other" (the kind agents give MCP tools; a shell command or an edit never passes, whatever its title) whose title
    /// names the server and a known tool in one of the forms agents print: "nth-pc-pc_observe: pc_observe" (the form the
    /// skills relay proved live, SK-17), "nth-pc_pc_observe", "mcp__nth-pc__pc_observe", "nth-pc/pc_observe",
    /// "nth-pc: pc_observe" or "nth-pc.pc_observe". A form not listed leaves the agent's own prompt in place, which is safe.
    /// </summary>
    public static string? AcpAutomaticOption(JSONValue toolCall, IReadOnlyList<JSONValue> options)
    {
        if (toolCall["kind"]?.JSONValueString != "other") return null;
        if (AcpTool(toolCall["title"]?.JSONValueString) is null) return null;
        return options.FirstOrDefault(o => o["kind"]?.JSONValueString == "allow_once")?["optionId"]?.JSONValueString;
    }

    /// <summary>The server's tool an ACP title names, or null.</summary>
    public static string? AcpTool(string? title)
    {
        if (title is null) return null;
        var match = AcpTitle().Match(title);
        return match.Success && ToolNames.Contains(match.Groups["tool"].Value) ? match.Groups["tool"].Value : null;
    }

    /// <summary>Copilot: whether a permission request is a call of the server's tools (kind "mcp", serverName nth-pc).</summary>
    public static bool IsCopilotRequest(JSONValue request) =>
        string.Equals(request["kind"]?.JSONValueString, "mcp", StringComparison.OrdinalIgnoreCase)
        && request["serverName"]?.JSONValueString == ServerName;

    /// <summary>Copilot's answer to such a request: approve this one call.</summary>
    public static JSONValue CopilotApproval() => JSONValue.FromDictionaryLiteral(("kind", "approve-once"));

    /// <summary>
    /// Codex: whether an mcpServer/elicitation/request comes from the server. nth-pc never asks the client for input
    /// itself, so such a request is Codex's own approval of one nth-pc call, which nth's approval already governs.
    /// </summary>
    public static bool IsCodexElicitation(JSONValue parameters) => parameters["serverName"]?.JSONValueString == ServerName;

    /// <summary>Codex's answer to such a request: accept, with no content.</summary>
    public static JSONValue CodexElicitationAccept() => JSONValue.FromDictionaryLiteral(("action", "accept"), ("content", JSONValue.FromDictionaryLiteral()));

    /// <summary>
    /// Codex: the thread's config overrides with the bundled computer-use plugins turned off, as dotted keys so the user's
    /// other plugin settings stay as they are.
    /// </summary>
    public static void TurnOffCodexComputerUse(IDictionary<string, JSONValue> config)
    {
        foreach (var plugin in CodexComputerUsePlugins) config["plugins." + plugin + ".enabled"] = false;
    }

    /// <summary>The image folder of a chat whose client cannot carry image parts (Pi, Command Code); null for every other.</summary>
    public static string? ImageFolder(Marrow.Core.Models.ProviderKind provider, string thread) =>
        provider == Marrow.Core.Models.ProviderKind.Pi || provider == Marrow.Core.Models.ProviderKind.Commandcode
            ? Path.Combine(ImageRoot.Current(), SafeName(thread))
            : null;

    private static string SafeName(string thread) => string.Concat(thread.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_'));

    [GeneratedRegex(@"^(?:mcp__)?nth-pc(?:__|[-_./]|: )(?<tool>(?:pc|web)_[a-z_]+)(?:$|[:\s(])", RegexOptions.CultureInvariant)]
    private static partial Regex AcpTitle();
}
