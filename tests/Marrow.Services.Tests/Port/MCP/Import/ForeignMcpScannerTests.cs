using Marrow.Core.Models;
using Marrow.Services.Port.MCP.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-1: the read-only scan of other AI apps' MCP configs. Contents come through the read seam: no test reads the real profile. Values here are fixture text, never real keys.
public sealed class ForeignMcpScannerTests
{
    private const string Home = @"C:\fixture\home";
    private const string AppData = @"C:\fixture\home\AppData\Roaming";

    private static ForeignMcpScan Scan(params (string Path, string Text)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Text, StringComparer.OrdinalIgnoreCase);
        return new ForeignMcpScanner(ForeignMcpScanner.Places(Home, AppData), path => map.GetValueOrDefault(path)).Scan();
    }

    private static string At(params string[] parts) => Path.Combine([Home, .. parts]);

    private static string Roaming(params string[] parts) => Path.Combine([AppData, .. parts]);

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Nothing_present_finds_nothing_and_reports_nothing()
    {
        var scan = Scan();
        Assert.Empty(scan.Servers);
        Assert.Empty(scan.Problems);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Claude_code_reads_the_user_servers_and_each_project_s_servers()
    {
        var scan = Scan((At(".claude.json"), """
            { "numStartups": 3,
              "mcpServers": { "files": { "type": "stdio", "command": "npx", "args": ["-y", "@fixture/files", "C:\\work"], "env": { "LOG_LEVEL": "debug" } } },
              "projects": { "C:/work/alpha": { "mcpServers": { "docs": { "type": "http", "url": "https://docs.example.test/mcp", "headers": { "X-Team": "one" } } } },
                            "C:/work/beta": { "allowedTools": [] } } }
            """));
        Assert.Empty(scan.Problems);
        Assert.Equal(2, scan.Servers.Count);
        var files = scan.Servers.Single(s => s.Name == "files");
        Assert.Equal((ForeignMcpApp.ClaudeCode, MCPCustomServer.Kind.Stdio, "npx", "User"), (files.App, files.Kind, files.Command, files.Source));
        Assert.Equal(["-y", "@fixture/files", @"C:\work"], files.Args);
        Assert.Equal([new ForeignMcpVariable("LOG_LEVEL", "debug")], files.Variables);
        var docs = scan.Servers.Single(s => s.Name == "docs");
        Assert.Equal((MCPCustomServer.Kind.Http, "https://docs.example.test/mcp", "alpha"), (docs.Kind, docs.Url, docs.Source));
        Assert.Equal([new ForeignMcpVariable("X-Team", "one")], docs.Variables);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Claude_desktop_cursor_windsurf_and_gemini_read_mcp_servers()
    {
        var scan = Scan(
            (Roaming("Claude", "claude_desktop_config.json"), """{ "mcpServers": { "memory": { "command": "uvx", "args": ["fixture-memory"] } } }"""),
            (At(".cursor", "mcp.json"), """{ "mcpServers": { "remote": { "url": "https://cursor.example.test/sse" } } }"""),
            (At(".codeium", "windsurf", "mcp_config.json"), """{ "mcpServers": { "wind": { "serverUrl": "https://wind.example.test/mcp", "headers": { "X-Org": "a" } } } }"""),
            (At(".gemini", "settings.json"), """{ "theme": "dark", "mcpServers": { "gem": { "httpUrl": "https://gem.example.test/mcp" }, "local": { "command": "node", "args": ["server.js"], "cwd": "C:/x" } } }"""));
        Assert.Empty(scan.Problems);
        Assert.Equal(MCPCustomServer.Kind.Stdio, scan.Servers.Single(s => s.App == ForeignMcpApp.ClaudeDesktop).Kind);
        Assert.Equal("https://cursor.example.test/sse", scan.Servers.Single(s => s.App == ForeignMcpApp.Cursor).Url);
        var wind = scan.Servers.Single(s => s.App == ForeignMcpApp.Windsurf);
        Assert.Equal(("https://wind.example.test/mcp", "X-Org"), (wind.Url, wind.Variables.Single().Key));
        Assert.Equal(["gem", "local"], scan.Servers.Where(s => s.App == ForeignMcpApp.Gemini).Select(s => s.Name).Order());
        Assert.Equal("https://gem.example.test/mcp", scan.Servers.Single(s => s.Name == "gem").Url);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Vs_code_reads_mcp_json_with_comments_and_settings_mcp_servers()
    {
        var scan = Scan(
            (Roaming("Code", "User", "mcp.json"), """
                // VS Code keeps comments in this file
                { "servers": { "gh": { "type": "http", "url": "https://gh.example.test/mcp", }, "py": { "type": "stdio", "command": "python", "args": ["-m", "fixture"], "env": { "MODE": "1" } } },
                  "inputs": [] }
                """),
            (Roaming("Code", "User", "settings.json"), """{ "editor.fontSize": 13, "mcp": { "servers": { "nested": { "command": "deno", "args": ["run", "x.ts"] } } } }"""));
        Assert.Empty(scan.Problems);
        Assert.Equal(["gh", "nested", "py"], scan.Servers.Where(s => s.App == ForeignMcpApp.VSCode).Select(s => s.Name).Order());
        Assert.Equal(MCPCustomServer.Kind.Http, scan.Servers.Single(s => s.Name == "gh").Kind);
        Assert.Equal("Settings", scan.Servers.Single(s => s.Name == "nested").Source);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Codex_reads_mcp_servers_tables_inline_and_sub_table_env_and_http_headers()
    {
        var scan = Scan((At(".codex", "config.toml"), """
            model = "fixture-model"
            # a comment
            [mcp_servers.files]
            command = "npx"
            args = [
              "-y", # first
              '@fixture/files',
            ]
            env = { LOG_LEVEL = "info", "QUOTED.KEY" = 'x' }

            [mcp_servers."docs site"]
            url = "https://docs.example.test/mcp"
            http_headers = { "X-Team" = "two" }

            [mcp_servers.tools]
            command = "uvx"
            args = ["fixture-tools"]

            [mcp_servers.tools.env]
            MODE = "fast"
            startup_timeout_sec = 20

            [[profiles]]
            name = "x"
            """));
        Assert.Empty(scan.Problems);
        var files = scan.Servers.Single(s => s.Name == "files");
        Assert.Equal(["-y", "@fixture/files"], files.Args);
        Assert.Equal(["LOG_LEVEL", "QUOTED.KEY"], files.Variables.Select(v => v.Key));
        var docs = scan.Servers.Single(s => s.Name == "docs site");
        Assert.Equal((MCPCustomServer.Kind.Http, "https://docs.example.test/mcp"), (docs.Kind, docs.Url));
        Assert.Equal(new ForeignMcpVariable("X-Team", "two"), docs.Variables.Single());
        Assert.Equal([new ForeignMcpVariable("MODE", "fast")], scan.Servers.Single(s => s.Name == "tools").Variables); // the number is not a string value
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void A_malformed_file_is_reported_not_thrown_and_the_others_still_scan()
    {
        var scan = Scan(
            (At(".cursor", "mcp.json"), "{ \"mcpServers\": { \"broken\": "),
            (At(".codex", "config.toml"), "[mcp_servers.x\ncommand = \"npx\""),
            (Roaming("Claude", "claude_desktop_config.json"), "[1, 2]"),
            (At(".gemini", "settings.json"), """{ "mcpServers": { "ok": { "command": "node" } } }"""));
        Assert.Equal([ForeignMcpApp.ClaudeDesktop, ForeignMcpApp.Codex, ForeignMcpApp.Cursor], scan.Problems.Select(p => p.App).Order());
        Assert.Equal("ok", Assert.Single(scan.Servers).Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void A_file_that_cannot_be_opened_is_reported()
    {
        var scanner = new ForeignMcpScanner(ForeignMcpScanner.Places(Home, AppData), path => path.EndsWith("mcp.json", StringComparison.Ordinal) && path.Contains(".cursor", StringComparison.Ordinal) ? throw new UnauthorizedAccessException("denied") : null);
        var problem = Assert.Single(scanner.Scan().Problems);
        Assert.Equal(ForeignMcpApp.Cursor, problem.App);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void An_entry_with_neither_command_nor_address_is_left_out()
    {
        var scan = Scan((At(".cursor", "mcp.json"), """{ "mcpServers": { "empty": { "env": { "A": "b" } }, "not-an-object": 3 } }"""));
        Assert.Empty(scan.Servers);
        Assert.Empty(scan.Problems);
    }
}
