// PORT-ONLY: Dextra (DESIGN.md rev 5, 16 "Name lock", 18; unit AS-4; ED-NEW-dextra-as4-1). No Swift twin.
// The owner-locked names (2026-10-11), each in one place: the MCP server's name persists in every provider's config, and
// the Astur word persists in tool text now and in toast strings and the sidebar row later (AS-8).
namespace Marrow.Core.Port.Assistant;

public static class AssistantNames
{
    /// <summary>The assistant.</summary>
    public const string Assistant = "Dextra";

    /// <summary>An area assistant under Dextra ("Add an Astur").</summary>
    public const string Astur = "Astur";

    /// <summary>More than one ("3 Asturs at work").</summary>
    public const string Asturs = "Asturs";

    /// <summary>The usage reserve keeps the plain word.</summary>
    public const string Reserve = "Reserve";

    /// <summary>
    /// The built-in MCP server's name in every provider's config, the prefix of its tools ("mcp__nth-dextra__recall"),
    /// and the info word of the fenced-text fallback. It replaces the placeholder "nth-assistant" now the name is locked.
    /// </summary>
    public const string Server = "nth-dextra";
}
