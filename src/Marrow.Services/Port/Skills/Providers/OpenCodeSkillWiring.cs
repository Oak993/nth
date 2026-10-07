// PORT-ONLY: SK-11d / WU-9. Applies the generation to an OpenCode child environment (skills-PLAN.md 2.2) and the DL-6 PATH rule.
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Port.Skills.Providers;

public static class OpenCodeSkillWiring
{
    private const string Variable = "OPENCODE_CONFIG_CONTENT";

    /// <summary>The OpenCode child environment for a chat with a generation. <paramref name="environment"/> is today's
    /// ProviderRegistry.Environment result for OpenCode (MCP content already merged). Returns a new dictionary; the input is untouched.</summary>
    public static Dictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment, OpenCodeSkillLaunch launch, IReadOnlyDictionary<string, string> mcpEnvironment)
    {
        var child = ProviderCustomCommands.ForChild(environment, launch.DataRoot); // DL-6: addons\bin on PATH, nothing else changes
        var mcp = mcpEnvironment.TryGetValue(Variable, out var content) ? content : null;
        // The MCP JSON replaces an inherited variable (Mac behaviour); otherwise the inherited one is source 6 and the base.
        var inherited = mcp is null && environment.TryGetValue(Variable, out var own) && own.Length != 0 ? own : null;
        var paths = OpenCodeSkillPaths.Resolve(environment, launch, inherited);
        if (paths.LeftOut) return child; // bundle off for this session: the variable stays exactly as today
        try { child[Variable] = OpenCodeConfigContent.WithSkillPaths(mcp ?? inherited ?? "{}", paths.Paths!, launch.GenerationFolder); }
        catch (Exception e) when (e is OpenCodeConfigUnreadableException or System.Text.Json.JsonException) { }
        return child;
    }

    public static Dictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment, OpenCodeSkillLaunch launch) =>
        Apply(environment, launch, MCPSessionOverrides.OpencodeEnvironment());
}
