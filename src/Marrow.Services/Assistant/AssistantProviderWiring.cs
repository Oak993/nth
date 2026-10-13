// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.3 point 1, 15.5 "Policy check"; unit AS-4; ED-NEW-dextra-as4-7). No Swift twin.
// How each CLI and ACP provider lets the nth-dextra tools run. The server asks nth's policy engine before every tool and
// raises the owner's card itself, so a provider's own permission prompt for an nth-dextra call is answered at once, and
// only in a session that was given the server; every other prompt is untouched. The same shape as Tactus's
// PcProviderWiring (pc-control 3.4), so the two read alike and merge as one.
using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Services.Assistant;

public static partial class AssistantProviderWiring
{
    /// <summary>The server's name in every provider's config.</summary>
    public const string ServerName = AssistantNames.Server;

    /// <summary>Claude Code's (and the JS bridge's) prefix for the server's tools.</summary>
    public const string ClaudeToolPrefix = "mcp__" + ServerName + "__";

    /// <summary>Claude Code, Pi and Command Code: whether a tool name is one of the server's tools.</summary>
    public static bool IsClaudeTool(string? name) =>
        name is not null && name.StartsWith(ClaudeToolPrefix, StringComparison.Ordinal) && AssistantToolCatalog.Find(name[ClaudeToolPrefix.Length..]) is not null;

    /// <summary>
    /// ACP: the "allow once" option for a call of one of the server's tools, or null for anything else. Only a call of kind
    /// "other" (the kind agents give MCP tools; a shell command or an edit never passes, whatever its title) whose title
    /// names the server and a known tool in one of the forms agents print: "nth-dextra-recall: recall",
    /// "nth-dextra_recall", "mcp__nth-dextra__recall", "nth-dextra/recall", "nth-dextra: recall" or "nth-dextra.recall".
    /// A form not listed leaves the agent's own prompt in place, which is safe.
    /// + ED-NEW-secfinal-3 (security gate S7): the whole title is one of those forms, optionally followed by a parenthesised argument
    /// summary ("nth-dextra.recall (query)"); "nth-dextra-recall: delete_files" or "nth-dextra/recall: x" (a server whose name only
    /// begins like nth-dextra's) is not, and no connected server may carry such a name (MCPProviderConfig.IsReservedName).
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
        var match = AcpTitle().Match(title.Trim());
        return match.Success && AssistantToolCatalog.Find(match.Groups["tool"].Value) is not null ? match.Groups["tool"].Value : null;
    }

    /// <summary>Copilot: whether a permission request is a call of the server's tools (kind "mcp", serverName nth-dextra).</summary>
    public static bool IsCopilotRequest(JSONValue request) =>
        string.Equals(request["kind"]?.JSONValueString, "mcp", StringComparison.OrdinalIgnoreCase)
        && request["serverName"]?.JSONValueString == ServerName;

    /// <summary>Copilot's answer to such a request: approve this one call.</summary>
    public static JSONValue CopilotApproval() => JSONValue.FromDictionaryLiteral(("kind", "approve-once"));

    /// <summary>
    /// Codex: whether an mcpServer/elicitation/request comes from the server. nth-dextra never asks the client for input
    /// itself, so such a request is Codex's own approval of one call, which nth's policy check already governs.
    /// </summary>
    public static bool IsCodexElicitation(JSONValue parameters) => parameters["serverName"]?.JSONValueString == ServerName;

    /// <summary>Codex's answer to such a request: accept, with no content.</summary>
    public static JSONValue CodexElicitationAccept() => JSONValue.FromDictionaryLiteral(("action", "accept"), ("content", JSONValue.FromDictionaryLiteral()));

    [GeneratedRegex(@"\A(?:mcp__nth-dextra__(?<tool>[a-z_]+)|nth-dextra-(?<tool>[a-z_]+): \k<tool>|nth-dextra(?:__|[_./]|: )(?<tool>[a-z_]+))(?: \([^()\r\n]*\))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcpTitle();
}
