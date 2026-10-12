using Marrow.Core.Models;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-5 (review of claude/mcp-import): a value that is a placeholder of the other app (${input:x}, ${env:X}, ${X}) is filled in from this PC where it can be and is never saved as text. The environment
// is a function the test hands over: no test reads the machine's own variables, and every key-like value is built at run time.
public sealed class McpPlaceholderTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    private static string Fake(string prefix) => prefix + new string('0', 12) + "fixture" + new string('9', 12);

    private static ForeignMcpServer Stdio(string name, string[]? args = null, string command = "npx", string scope = "", params (string, string)[] env) =>
        new(ForeignMcpApp.VSCode, "User", name, MCPCustomServer.Kind.Stdio, command, args ?? ["-y", "@fixture/" + name], "", [.. env.Select(e => new ForeignMcpVariable(e.Item1, e.Item2))], @"C:\fixture\mcp.json", scope);

    private static ForeignMcpServer Remote(string name, string url, bool sse = false, params (string, string)[] headers) =>
        new(ForeignMcpApp.Cursor, "", name, MCPCustomServer.Kind.Http, "", [], url, [.. headers.Select(h => new ForeignMcpVariable(h.Item1, h.Item2))], @"C:\fixture\.cursor\mcp.json", LegacySse: sse);

    [Theory]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    [InlineData("${env:TOK}", "abc", "")]
    [InlineData("${TOK}", "abc", "")]
    [InlineData("Bearer ${env:TOK}", "Bearer abc", "")]
    [InlineData("a ${TOK} b ${env:TOK}", "a abc b abc", "")]
    [InlineData("${UNSET}", "${UNSET}", "${UNSET}")]
    [InlineData("${env:UNSET}", "${env:UNSET}", "${env:UNSET}")]
    [InlineData("${UNSET:-fallback}", "fallback", "")]
    [InlineData("${EMPTY:-fallback}", "fallback", "")]
    [InlineData("${EMPTY}", "", "")]
    [InlineData("${input:github_token}", "${input:github_token}", "${input:github_token}")]
    [InlineData("Bearer ${input:t}", "Bearer ${input:t}", "${input:t}")]
    [InlineData("${config:some.setting}", "${config:some.setting}", "${config:some.setting}")]
    [InlineData("${command:pick}", "${command:pick}", "${command:pick}")]
    [InlineData("${TOK}${UNSET}${input:x}", "abc${UNSET}${input:x}", "${UNSET},${input:x}")]
    [InlineData("${pathSeparator}x${/}y", @"\x\y", "")]
    [InlineData("$TOK and %TOK% and plain", "$TOK and %TOK% and plain", "")]
    public void Placeholders_are_filled_in_from_the_environment_or_named_when_they_cannot_be(string text, string expected, string unresolved)
    {
        var result = McpPlaceholders.Expand(text, name => name switch { "pathSeparator" or "/" => @"\", _ => Env(("TOK", "abc"), ("EMPTY", ""))(name) });
        Assert.Equal(expected, result.Text);
        Assert.Equal(unresolved.Length == 0 ? [] : unresolved.Split(','), result.Unresolved);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void Text_without_braces_is_returned_as_it_was()
    {
        Assert.False(McpPlaceholders.Has("npx -y @fixture/x"));
        Assert.True(McpPlaceholders.Has("a ${b} c"));
        Assert.Same("plain", McpPlaceholders.Expand("plain", _ => throw new InvalidOperationException("no lookup needed")).Text);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_variable_that_names_a_prompt_comes_in_empty_and_is_named_not_saved_as_text()
    {
        var prepared = McpImport.Prepare(Stdio("gh", env: [("GITHUB_PERSONAL_ACCESS_TOKEN", "${input:github_token}"), ("LOG_LEVEL", "info")]), Env());
        Assert.Null(prepared.Blocked);
        Assert.Equal(["GITHUB_PERSONAL_ACCESS_TOKEN"], prepared.NeedsValue);
        Assert.Equal([new ForeignMcpVariable("GITHUB_PERSONAL_ACCESS_TOKEN", ""), new ForeignMcpVariable("LOG_LEVEL", "info")], prepared.Server.Variables);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_variable_from_the_environment_is_filled_in_and_then_judged_by_its_filled_in_value()
    {
        var token = Fake("ghp_");
        var prepared = McpImport.Prepare(Remote("docs", "https://docs.example.test/mcp", headers: [("X-Upstream", "${env:UP}"), ("X-Mode", "${MODE:-fast}")]), Env(("UP", token)));
        Assert.Empty(prepared.NeedsValue);
        var values = McpImport.Values(prepared.Server);
        Assert.Equal([("X-Upstream", token, true), ("X-Mode", "fast", false)], values); // the key-like value is locked because of what it is
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_header_with_an_unset_placeholder_inside_a_longer_value_comes_in_empty_and_locked_by_its_name()
    {
        var prepared = McpImport.Prepare(Remote("docs", "https://docs.example.test/mcp", headers: [("Authorization", "Bearer ${UNSET_TOKEN}")]), Env());
        Assert.Equal(["Authorization"], prepared.NeedsValue);
        Assert.Equal([("Authorization", "", true)], McpImport.Values(prepared.Server));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void An_argument_or_an_address_that_cannot_be_filled_in_blocks_the_server()
    {
        var args = McpImport.Prepare(Stdio("a", args: ["--token", "${input:t}", "--dir", "${env:DIR}"]), Env(("DIR", @"C:\data")));
        Assert.NotNull(args.Blocked);
        Assert.Contains("${input:t}", args.Blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("${env:DIR}", args.Blocked, StringComparison.Ordinal);
        Assert.Equal(["--token", "${input:t}", "--dir", @"C:\data"], args.Server.Args);

        var url = McpImport.Prepare(Remote("u", "https://${HOST}/mcp"), Env());
        Assert.Contains("${HOST}", url.Blocked, StringComparison.Ordinal);
        Assert.Equal("https://api.example.test/mcp", McpImport.Prepare(Remote("u", "https://${HOST}/mcp"), Env(("HOST", "api.example.test"))).Server.Url);

        Assert.Contains("${TOOL}", McpImport.Prepare(Stdio("c", command: "${TOOL}"), Env()).Blocked, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void Workspace_folder_is_the_project_the_config_belongs_to_and_user_home_is_the_profile()
    {
        var inProject = McpImport.Prepare(Stdio("p", args: ["${workspaceFolder}\\data", "${workspaceFolderBasename}", "${userHome}"], scope: "project:" + @"C:\work\alpha"), Env(("USERPROFILE", @"C:\Users\fixture")));
        Assert.Null(inProject.Blocked);
        Assert.Equal([@"C:\work\alpha\data", "alpha", @"C:\Users\fixture"], inProject.Server.Args);
        Assert.NotNull(McpImport.Prepare(Stdio("p", args: ["${workspaceFolder}"]), Env()).Blocked); // a user-level config has no workspace
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_server_on_the_older_sse_connection_is_blocked_with_the_reason()
    {
        var prepared = McpImport.Prepare(Remote("old", "https://x.example.test/sse", sse: true), Env());
        Assert.Contains("SSE", prepared.Blocked, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void Claude_desktop_and_codex_configs_are_taken_as_written_because_those_apps_fill_nothing_in()
    {
        // read from the real Claude Desktop config on this PC (shape only): mcp-remote expands ${STITCH_API_KEY} itself from the environment the server is given
        var desktop = new ForeignMcpServer(ForeignMcpApp.ClaudeDesktop, "", "stitch", MCPCustomServer.Kind.Stdio, "npx", ["-y", "mcp-remote", "https://stitch.example.test/mcp", "--header", "X-Goog-Api-Key:${STITCH_API_KEY}"], "",
            [new("STITCH_API_KEY", Fake("AIza"))], @"C:\fixture\claude_desktop_config.json");
        var prepared = McpImport.Prepare(desktop, Env());
        Assert.Null(prepared.Blocked);
        Assert.Empty(prepared.NeedsValue);
        Assert.Equal(desktop.Args, prepared.Server.Args); // as written
        Assert.Equal(desktop.Variables, prepared.Server.Variables);

        var codex = new ForeignMcpServer(ForeignMcpApp.Codex, "", "docs", MCPCustomServer.Kind.Http, "", [], "https://docs.example.test/mcp", [new("X-Team", "${TEAM}")], @"C:\fixture\config.toml");
        Assert.Equal("${TEAM}", McpImport.Prepare(codex, Env(("TEAM", "filled"))).Server.Variables.Single().Value); // Codex would not fill it in either
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_claude_desktop_extension_manifest_does_have_placeholders_and_a_required_one_nobody_set_is_a_value_it_needs()
    {
        var extension = new ForeignMcpServer(ForeignMcpApp.ClaudeDesktop, "Windows-MCP", "windows-mcp", MCPCustomServer.Kind.Stdio, "uv", ["run"], "", [new("TOKEN", "${user_config.token}"), new("MODE", "local")], @"C:\fixture\manifest.json",
            ForeignMcpServer.ExtensionScope + "ant.dir.fixture");
        var prepared = McpImport.Prepare(extension, Env());
        Assert.Equal(["TOKEN"], prepared.NeedsValue);
        Assert.Equal([new ForeignMcpVariable("TOKEN", ""), new ForeignMcpVariable("MODE", "local")], prepared.Server.Variables);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-4")]
    public void A_legacy_sse_server_of_a_desktop_or_codex_config_is_still_blocked()
    {
        var old = new ForeignMcpServer(ForeignMcpApp.ClaudeDesktop, "", "old", MCPCustomServer.Kind.Http, "", [], "https://x.example.test/sse", [], @"C:\fixture\c.json", LegacySse: true);
        Assert.Contains("SSE", McpImport.Prepare(old, Env()).Blocked, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void Import_never_saves_placeholder_text_and_reports_what_needs_a_value()
    {
        var target = new McpImportTests.FakeTarget();
        var outcome = McpImport.Import(
            [Stdio("github", env: [("GITHUB_PERSONAL_ACCESS_TOKEN", "${input:github_token}"), ("LOG", "${env:LOGLEVEL}"), ("REGION", "${AWS_REGION:-eu}")])],
            DuplicateChoice.Skip, target, Env(("LOGLEVEL", "debug")));
        Assert.Equal((1, 0, 0, 0), (outcome.Imported, outcome.Replaced, outcome.Skipped, outcome.Failed.Count));
        Assert.Equal("github", Assert.Single(outcome.NeedsValue).Name);
        Assert.Equal(["GITHUB_PERSONAL_ACCESS_TOKEN"], outcome.NeedsValue[0].Keys);
        Assert.Equal("1 imported, 1 needs a value.", outcome.Summary);

        var saved = target.Saved.Single();
        Assert.Equal([new MCPCustomServer.Variable("GITHUB_PERSONAL_ACCESS_TOKEN", true), new MCPCustomServer.Variable("LOG", false), new MCPCustomServer.Variable("REGION", false)], saved.Env);
        Assert.Equal([("GITHUB_PERSONAL_ACCESS_TOKEN", "", true), ("LOG", "debug", false), ("REGION", "eu", false)], target.Values["custom-github"]);
        Assert.DoesNotContain(target.Values["custom-github"], v => v.Value.Contains("${", StringComparison.Ordinal));
        Assert.DoesNotContain("${", string.Join(" ", saved.Args), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void Replace_reports_the_values_it_needs_under_the_name_already_in_nth()
    {
        var target = new McpImportTests.FakeTarget();
        McpImport.Import([Stdio("github")], DuplicateChoice.Skip, target, Env());
        var outcome = McpImport.Import([Stdio("github", env: [("TOKEN", "${input:t}")])], DuplicateChoice.Replace, target, Env());
        Assert.Equal(1, outcome.Replaced);
        Assert.Equal(("github", "TOKEN"), (outcome.NeedsValue.Single().Name, outcome.NeedsValue.Single().Keys.Single()));
        Assert.Equal("1 replaced, 1 needs a value.", outcome.Summary);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void A_blocked_server_is_a_failure_with_its_reason_and_nothing_of_it_is_saved()
    {
        var target = new McpImportTests.FakeTarget();
        var outcome = McpImport.Import([Stdio("a", args: ["${input:x}"]), Remote("old", "https://x.example.test/sse", sse: true), Stdio("fine")], DuplicateChoice.Skip, target, Env());
        Assert.Equal(1, outcome.Imported);
        Assert.Equal(["a", "old"], outcome.Failed.Select(f => f.Name));
        Assert.Contains("${input:x}", outcome.Failed[0].Reason, StringComparison.Ordinal);
        Assert.Contains("SSE", outcome.Failed[1].Reason, StringComparison.Ordinal);
        Assert.Equal(["custom-fine"], target.Saved.Select(s => s.Id));
        Assert.Equal("1 imported, 2 couldn\u2019t be imported.", outcome.Summary);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public void The_default_lookup_reads_the_environment_the_process_has()
    {
        var name = "NTH_FIXTURE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "set-by-test");
        try
        {
            Assert.Equal("set-by-test", McpPlaceholders.EnvironmentValue(name));
            Assert.Null(McpPlaceholders.EnvironmentValue("NTH_FIXTURE_NOT_SET_" + Guid.NewGuid().ToString("N")));
            Assert.Null(McpPlaceholders.EnvironmentValue("bad name with spaces"));
            Assert.Null(McpPlaceholders.EnvironmentValue("a=b"));
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }
}
