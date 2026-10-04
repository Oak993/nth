using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d / WU-9 and DL-11: the wired environment, and byte-identity with today's when nothing is installed.
public sealed class OpenCodeWiringTests
{
    private const string McpClaudeJson = "{\"mcpServers\":{\"local\":{\"command\":\"exe\",\"args\":[\"a\"]}}}";

    private static Dictionary<string, string> Mcp(OpenCodeFixture f)
    {
        var claude = f.Write(Path.Combine(f.Root, "claude.json"), McpClaudeJson);
        using var _ = new ClaudeConfigOverride(claude);
        return new Dictionary<string, string>(MCPSessionOverrides.OpencodeEnvironment());
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void With_no_mcp_and_no_inherited_variable_the_variable_is_new_and_holds_only_skills()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("agent-skills", "~/team-skills", "~\\x"));
        var child = OpenCodeSkillWiring.Apply(f.Env, f.Launch, new Dictionary<string, string>());
        Assert.Equal("{\"skills\":{\"paths\":[\"agent-skills\",\"~/team-skills\",\"~\\\\x\"," + System.Text.Json.JsonSerializer.Serialize(f.Generation) + "]}}", child["OPENCODE_CONFIG_CONTENT"]);
        Assert.Equal(["agent-skills", "~/team-skills", "~\\x", f.Generation], OpenCodeFixture.ReadPaths(child["OPENCODE_CONFIG_CONTENT"]));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void An_inherited_variable_is_kept_key_for_key_with_only_skills_added()
    {
        using var f = new OpenCodeFixture();
        f.Env["OPENCODE_CONFIG_CONTENT"] = "{\"model\":\"m\",\"theme\":\"t\"}";
        var child = OpenCodeSkillWiring.Apply(f.Env, f.Launch, new Dictionary<string, string>());
        Assert.StartsWith("{\"model\":\"m\",\"theme\":\"t\",\"skills\":", child["OPENCODE_CONFIG_CONTENT"], StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void With_mcp_servers_the_mcp_json_is_byte_identical_and_replaces_an_inherited_variable()
    {
        using var f = new OpenCodeFixture();
        var mcp = Mcp(f);
        var today = mcp["OPENCODE_CONFIG_CONTENT"];
        f.Env["OPENCODE_CONFIG_CONTENT"] = f.Paths("ignored-because-replaced");
        f.Env["OPENCODE_CONFIG_CONTENT"] = today; // what ProviderRegistry.Environment leaves in the child
        var child = OpenCodeSkillWiring.Apply(f.Env, f.Launch, mcp);
        var wired = child["OPENCODE_CONFIG_CONTENT"];
        Assert.StartsWith(today[..^1] + ",\"skills\":{\"paths\":[", wired, StringComparison.Ordinal);
        Assert.Equal([f.Generation], OpenCodeFixture.ReadPaths(wired));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void With_mcp_the_users_inherited_skills_paths_are_not_a_source_because_the_mcp_json_replaced_them()
    {
        using var f = new OpenCodeFixture();
        var mcp = Mcp(f);
        f.Env["OPENCODE_CONFIG_CONTENT"] = mcp["OPENCODE_CONFIG_CONTENT"];
        f.Write(Path.Combine(f.Config, "opencode.json"), f.Paths("global"));
        Assert.Equal(["global", f.Generation], OpenCodeFixture.ReadPaths(OpenCodeSkillWiring.Apply(f.Env, f.Launch, mcp)["OPENCODE_CONFIG_CONTENT"]));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_left_out_session_keeps_every_variable_exactly_as_today_except_PATH()
    {
        using var f = new OpenCodeFixture();
        f.Write(Path.Combine(f.Config, "config"), "model = \"x\""); // legacy TOML: left out
        f.Env["OPENCODE_CONFIG_CONTENT"] = "{\"model\":\"m\"}";
        f.Env["OTHER"] = "keep";
        var child = OpenCodeSkillWiring.Apply(f.Env, f.Launch, new Dictionary<string, string>());
        Assert.Equal(f.Env.Where(p => p.Key != "PATH").OrderBy(p => p.Key).ToArray(), child.Where(p => p.Key != "PATH").OrderBy(p => p.Key).ToArray());
        Assert.Equal("{\"model\":\"m\"}", child["OPENCODE_CONFIG_CONTENT"]);
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_left_out_session_with_no_variable_today_sets_none()
    {
        using var f = new OpenCodeFixture();
        f.Write(f.AuthFile, "{\"c\":{\"type\":\"wellknown\"}}");
        Assert.False(OpenCodeSkillWiring.Apply(f.Env, f.Launch, new Dictionary<string, string>()).ContainsKey("OPENCODE_CONFIG_CONTENT"));
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void PATH_gets_addons_bin_before_the_first_WindowsApps_entry_and_the_input_is_not_changed()
    {
        using var f = new OpenCodeFixture();
        var sep = Path.PathSeparator;
        f.Env["PATH"] = "one" + sep + "x/Microsoft/WindowsApps" + sep + "two";
        var child = OpenCodeSkillWiring.Apply(f.Env, f.Launch, new Dictionary<string, string>());
        Assert.Equal("one" + sep + Path.Combine(f.Data, "addons", "bin") + sep + "x/Microsoft/WindowsApps" + sep + "two", child["PATH"]);
        Assert.Equal("one" + sep + "x/Microsoft/WindowsApps" + sep + "two", f.Env["PATH"]);
    }
}
