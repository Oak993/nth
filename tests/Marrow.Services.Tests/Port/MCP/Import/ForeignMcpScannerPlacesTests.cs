using Marrow.Core.Models;
using Marrow.Services.Port.MCP.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-3, -4, -9, -10 (review of claude/mcp-import): where the scan looks (the packaged Claude Desktop, CODEX_HOME, CLAUDE_CONFIG_DIR, projects, extensions), what it keeps apart (row keys) and what it flags
// (the older SSE connection). Contents come through the read and list seams: no test reads the real profile.
public sealed class ForeignMcpScannerPlacesTests
{
    private const string Home = @"C:\fixture\home";
    private const string AppData = @"C:\fixture\home\AppData\Roaming";
    private const string LocalAppData = @"C:\fixture\home\AppData\Local";
    private static readonly string Packaged = Path.Combine(LocalAppData, "Packages", ForeignMcpScanner.ClaudeDesktopPackage, "LocalCache", "Roaming", "Claude");
    private static readonly string Classic = Path.Combine(AppData, "Claude");

    private static IReadOnlyList<ForeignMcpPlace> AllPlaces(string? codexHome = null, string? claudeConfigDir = null, Func<string, IReadOnlyList<string>>? packages = null) =>
        ForeignMcpScanner.Places(Home, AppData, LocalAppData, codexHome, claudeConfigDir, packages ?? (_ => []));

    private static ForeignMcpScan Scan(IReadOnlyList<ForeignMcpPlace> places, IEnumerable<(string Path, string Text)> files, params (string Folder, string[] Children)[] folders)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Text, StringComparer.OrdinalIgnoreCase);
        var lists = folders.ToDictionary(f => f.Folder, f => (IReadOnlyList<string>)f.Children, StringComparer.OrdinalIgnoreCase);
        return new ForeignMcpScanner(places, path => map.GetValueOrDefault(path), path => lists.GetValueOrDefault(path) ?? [], Home).Scan();
    }

    private static ForeignMcpScan Scan(params (string Path, string Text)[] files) => Scan(AllPlaces(), files);

    private const string Memory = """{ "mcpServers": { "memory": { "command": "uvx", "args": ["fixture-memory"] } } }""";

    // ---- the packaged Claude Desktop (MSIX)

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_packaged_claude_desktop_config_is_found_when_it_is_the_only_one()
    {
        var packaged = Path.Combine(Packaged, "claude_desktop_config.json");
        var scan = Scan((packaged, Memory));
        Assert.Empty(scan.Problems);
        var found = Assert.Single(scan.Servers);
        Assert.Equal((ForeignMcpApp.ClaudeDesktop, "memory", packaged), (found.App, found.Name, found.Path));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void Another_claude_package_folder_is_found_too_and_a_package_of_another_name_is_not_looked_in()
    {
        var other = Path.Combine(LocalAppData, "Packages", "Claude_changedpublisher");
        var places = AllPlaces(packages: _ => [other, Path.Combine(LocalAppData, "Packages", "Other_thing")]);
        Assert.Contains(places, p => p.Path.StartsWith(other, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(places, p => p.Path.Contains("Other_thing", StringComparison.OrdinalIgnoreCase));
        var scan = Scan(places, [(Path.Combine(other, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json"), Memory)]);
        Assert.Equal("memory", Assert.Single(scan.Servers).Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_packaged_copy_comes_first_and_the_same_server_in_both_copies_shows_once()
    {
        var packaged = Path.Combine(Packaged, "claude_desktop_config.json");
        var scan = Scan((Path.Combine(Classic, "claude_desktop_config.json"), Memory), (packaged, Memory));
        var found = Assert.Single(scan.Servers);
        Assert.Equal(packaged, found.Path); // the one the packaged app reads
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void Two_copies_that_differ_both_show()
    {
        var scan = Scan(
            (Path.Combine(Packaged, "claude_desktop_config.json"), Memory),
            (Path.Combine(Classic, "claude_desktop_config.json"), """{ "mcpServers": { "memory": { "command": "uvx", "args": ["fixture-memory", "--older"] } } }"""));
        Assert.Equal(2, scan.Servers.Count);
        Assert.Equal(2, scan.Servers.Select(s => s.Key).Distinct().Count());
    }

    // ---- CODEX_HOME and CLAUDE_CONFIG_DIR

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void Codex_home_and_claude_config_dir_are_looked_in_before_the_usual_places()
    {
        const string codexHome = @"D:\fixture\codex-home";
        const string claudeDir = @"D:\fixture\claude-config";
        var places = AllPlaces(codexHome, claudeDir);
        var scan = Scan(places,
        [
            (Path.Combine(claudeDir, ".claude.json"), """{ "mcpServers": { "cfg-user": { "command": "node", "args": ["a.js"] } } }"""),
            (Path.Combine(claudeDir, "settings.json"), """{ "mcpServers": { "cfg-settings": { "command": "node", "args": ["b.js"] } } }"""),
            (Path.Combine(Home, ".claude.json"), """{ "mcpServers": { "home-user": { "command": "node", "args": ["c.js"] } } }"""),
            (Path.Combine(codexHome, "config.toml"), "[mcp_servers.cx-home]\ncommand = \"npx\"\n"),
            (Path.Combine(Home, ".codex", "config.toml"), "[mcp_servers.cx-usual]\ncommand = \"npx\"\n"),
        ]);
        Assert.Empty(scan.Problems);
        Assert.Equal(["cfg-user", "cfg-settings", "home-user", "cx-home", "cx-usual"], scan.Servers.Select(s => s.Name));
        Assert.Equal(places.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(), places.Count); // no place twice
        Assert.True(places.ToList().FindIndex(p => p.Path == Path.Combine(claudeDir, ".claude.json")) < places.ToList().FindIndex(p => p.Path == Path.Combine(Home, ".claude.json")));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative\\folder")]
    public void A_variable_that_is_empty_or_not_a_full_path_is_ignored(string? value)
    {
        var plain = ForeignMcpScanner.Places(Home, AppData, LocalAppData, null, null, _ => []);
        Assert.Equal(plain.Select(p => p.Path), AllPlaces(value, value).Select(p => p.Path));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_same_folder_named_by_the_variable_and_by_the_usual_place_is_one_place()
    {
        var places = AllPlaces(Path.Combine(Home, ".codex"), Path.Combine(Home, ".claude"));
        Assert.Equal(1, places.Count(p => p.Path == Path.Combine(Home, ".codex", "config.toml")));
    }

    // ---- project-scoped files

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_project_files_of_the_projects_claude_code_and_codex_know_are_read()
    {
        var alpha = @"C:\work\alpha";
        var beta = @"C:\work\beta";
        var scan = Scan(
            (Path.Combine(Home, ".claude.json"), """{ "projects": { "C:/work/alpha": { "allowedTools": [] }, "C:/work/gone": { } } }"""),
            (Path.Combine(Home, ".codex", "config.toml"), "[projects.'" + beta + "']\ntrust_level = \"trusted\"\n"),
            (Path.Combine(alpha, ".mcp.json"), """{ "mcpServers": { "team-db": { "command": "npx", "args": ["-y", "@fixture/db"] } } }"""),
            (Path.Combine(alpha, ".cursor", "mcp.json"), """{ "mcpServers": { "cur": { "url": "https://cur.example.test/mcp" } } }"""),
            (Path.Combine(alpha, ".vscode", "mcp.json"), """
                // comments are fine here
                { "servers": { "vs": { "command": "python", "args": ["-m", "fixture"] } } }
                """),
            (Path.Combine(alpha, ".gemini", "settings.json"), """{ "mcpServers": { "gem": { "httpUrl": "https://gem.example.test/mcp" } } }"""),
            (Path.Combine(beta, ".codex", "config.toml"), "[mcp_servers.cx]\ncommand = \"uvx\"\n"),
            (Path.Combine(beta, ".mcp.json"), """{ "mcpServers": { "team-db": { "command": "npx", "args": ["-y", "@fixture/db"] } } }"""));
        Assert.Empty(scan.Problems);
        Assert.Equal(["cur", "cx", "gem", "team-db", "team-db", "vs"], scan.Servers.Select(s => s.Name).Order(StringComparer.Ordinal));
        var db = scan.Servers.Where(s => s.Name == "team-db").ToArray();
        Assert.Equal([("alpha", alpha), ("beta", beta)], db.Select(s => (s.Source, s.ProjectPath)).Order());
        Assert.All(db, s => Assert.Equal(ForeignMcpApp.ClaudeCode, s.App));
        Assert.Equal(ForeignMcpApp.Cursor, scan.Servers.Single(s => s.Name == "cur").App);
        Assert.Equal(ForeignMcpApp.VSCode, scan.Servers.Single(s => s.Name == "vs").App);
        Assert.Equal(ForeignMcpApp.Gemini, scan.Servers.Single(s => s.Name == "gem").App);
        Assert.Equal((ForeignMcpApp.Codex, beta), (scan.Servers.Single(s => s.Name == "cx").App, scan.Servers.Single(s => s.Name == "cx").ProjectPath));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_home_folder_and_network_folders_are_not_treated_as_projects()
    {
        var scan = Scan(
            (Path.Combine(Home, ".claude.json"), """{ "projects": { "C:/fixture/home": { }, "//server/share/app": { } } }"""),
            (Path.Combine(Home, ".mcp.json"), Memory),
            (@"\\server\share\app\.mcp.json", Memory));
        Assert.Empty(scan.Servers);
    }

    // ---- row keys

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-10")]
    public void Two_projects_that_share_a_folder_name_and_a_server_name_get_two_keys()
    {
        var scan = Scan((Path.Combine(Home, ".claude.json"), """
            { "projects": {
                "C:/work/app": { "mcpServers": { "db": { "command": "npx", "args": ["-y", "@fixture/one"] } } },
                "D:/old/app":  { "mcpServers": { "db": { "command": "npx", "args": ["-y", "@fixture/two"] } } } } }
            """));
        Assert.Equal(2, scan.Servers.Count);
        Assert.Equal(2, scan.Servers.Select(s => s.Key).Distinct().Count());
        Assert.All(scan.Servers, s => Assert.Equal("app", s.Source));
        Assert.Equal([@"C:\work\app", @"D:\old\app"], scan.Servers.Select(s => s.ProjectPath).Order());
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-10")]
    public void The_same_name_twice_in_one_object_still_gets_two_keys()
    {
        var scan = Scan((Path.Combine(Home, ".cursor", "mcp.json"), """{ "mcpServers": { "dup": { "command": "a" }, "dup": { "command": "b" } } }"""));
        Assert.Equal(["a", "b"], scan.Servers.Select(s => s.Command).Order());
        Assert.Equal(2, scan.Servers.Select(s => s.Key).Distinct().Count());
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-10")]
    public void Vs_code_settings_naming_a_server_in_both_forms_give_one_server_the_nested_one()
    {
        var scan = Scan((Path.Combine(AppData, "Code", "User", "settings.json"), """
            { "mcp": { "servers": { "same": { "command": "nested" } } },
              "mcp.servers": { "same": { "command": "flat" }, "only-flat": { "command": "flat2" } } }
            """));
        Assert.Equal([("nested", "same"), ("flat2", "only-flat")], scan.Servers.Select(s => (s.Command, s.Name)));
        Assert.Equal(2, scan.Servers.Select(s => s.Key).Distinct().Count());
    }

    // ---- Claude Desktop extensions

    private const string Manifest = """
        { "name": "windows-mcp", "display_name": "Windows-MCP",
          "server": { "type": "python", "entry_point": "src/main.py",
            "mcp_config": { "command": "uv", "args": ["--directory", "${__dirname}", "run", "windows-mcp"],
              "env": { "MODE": "${user_config.mode}", "API_KEY": "${user_config.api_key}", "TOKEN": "${user_config.token}", "DEBUG": "${user_config.debug}", "DOCS": "${DOCUMENTS}/notes", "PLAIN": "x" },
              "platform_overrides": { "win32": { "command": "uv.exe" } } } },
          "user_config": { "mode": { "type": "string", "default": "local" }, "api_key": { "type": "string", "sensitive": true, "required": false },
                           "token": { "type": "string", "required": true }, "debug": { "type": "boolean" } } }
        """;

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void A_claude_desktop_extension_that_defines_a_server_is_found_with_its_own_placeholders_filled_in()
    {
        var extensions = Path.Combine(Packaged, "Claude Extensions");
        var folder = Path.Combine(extensions, "ant.dir.fixture.windows-mcp");
        var scan = Scan(AllPlaces(),
        [
            (Path.Combine(folder, "manifest.json"), Manifest),
            (Path.Combine(Packaged, "Claude Extensions Settings", "ant.dir.fixture.windows-mcp.json"), """{ "isEnabled": true, "userConfig": { "mode": "remote", "debug": true } }"""),
        ], (extensions, [folder]));
        Assert.Empty(scan.Problems);
        var server = Assert.Single(scan.Servers);
        Assert.Equal((ForeignMcpApp.ClaudeDesktop, MCPCustomServer.Kind.Stdio, "windows-mcp", "Windows-MCP"), (server.App, server.Kind, server.Name, server.Source));
        Assert.Equal("uv.exe", server.Command); // the win32 override
        Assert.Equal(["--directory", folder, "run", "windows-mcp"], server.Args);
        var env = server.Variables.ToDictionary(v => v.Key, v => v.Value);
        Assert.Equal("remote", env["MODE"]);
        Assert.Equal("true", env["DEBUG"]);
        Assert.Equal(Path.Combine(Home, "Documents") + "/notes", env["DOCS"]);
        Assert.Equal("x", env["PLAIN"]);
        Assert.Equal("${user_config.token}", env["TOKEN"]); // required and not set: stays as written, the import reports it as a value it needs
        Assert.DoesNotContain("API_KEY", env.Keys); // optional and not set: left out
        Assert.Equal(Path.Combine(folder, "manifest.json"), server.Path);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void An_extension_with_no_settings_file_takes_the_manifest_defaults_and_a_disabled_one_has_no_server()
    {
        var extensions = Path.Combine(Classic, "Claude Extensions");
        var enabled = Path.Combine(extensions, "ant.dir.fixture.windows-mcp");
        var disabled = Path.Combine(extensions, "ant.dir.fixture.other");
        var scan = Scan(AllPlaces(),
        [
            (Path.Combine(enabled, "manifest.json"), Manifest),
            (Path.Combine(disabled, "manifest.json"), Manifest.Replace("windows-mcp\", \"display_name", "other\", \"display_name", StringComparison.Ordinal)),
            (Path.Combine(Classic, "Claude Extensions Settings", "ant.dir.fixture.other.json"), """{ "isEnabled": false }"""),
        ], (extensions, [enabled, disabled]));
        var server = Assert.Single(scan.Servers);
        Assert.Equal("local", server.Variables.Single(v => v.Key == "MODE").Value);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_same_extension_in_the_packaged_and_the_classic_folder_is_one_server_and_a_broken_manifest_is_reported()
    {
        var packaged = Path.Combine(Packaged, "Claude Extensions");
        var classic = Path.Combine(Classic, "Claude Extensions");
        var id = "ant.dir.fixture.windows-mcp";
        var broken = Path.Combine(classic, "ant.dir.fixture.broken");
        var scan = Scan(AllPlaces(),
        [
            (Path.Combine(packaged, id, "manifest.json"), Manifest),
            (Path.Combine(classic, id, "manifest.json"), Manifest),
            (Path.Combine(broken, "manifest.json"), "{ \"server\": "),
        ], (packaged, [Path.Combine(packaged, id)]), (classic, [Path.Combine(classic, id), broken]));
        var server = Assert.Single(scan.Servers);
        Assert.StartsWith(Packaged, server.Path, StringComparison.OrdinalIgnoreCase);
        var problem = Assert.Single(scan.Problems);
        Assert.Equal(Path.Combine(broken, "manifest.json"), problem.Path);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void An_extension_list_setting_that_is_a_whole_argument_becomes_several_arguments()
    {
        var folder = @"C:\fixture\ext";
        var place = new ForeignMcpPlace(ForeignMcpApp.ClaudeDesktop, @"C:\fixture\Claude Extensions", ForeignMcpFormat.ClaudeExtensions);
        var manifest = """
            { "name": "files", "server": { "mcp_config": { "command": "node", "args": ["${__dirname}/server.js", "${user_config.dirs}", "--note", "${user_config.missing}"] } },
              "user_config": { "dirs": { "type": "directory", "multiple": true }, "missing": { "type": "string" } } }
            """;
        var server = Assert.Single(ForeignMcpScanner.Extension(place, folder, manifest, """{ "userConfig": { "dirs": ["C:\\a", "C:\\b"] } }""", Home));
        Assert.Equal([folder + "/server.js", @"C:\a", @"C:\b", "--note"], server.Args); // the unset optional argument is left out
    }

    // ---- the older SSE connection

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-4")]
    public void Sse_servers_are_flagged_and_streamable_http_ones_are_not()
    {
        var scan = Scan(
            (Path.Combine(Home, ".cursor", "mcp.json"), """{ "mcpServers": { "old": { "type": "sse", "url": "https://x.example.test/sse" }, "new": { "type": "http", "url": "https://x.example.test/mcp" }, "bare": { "url": "https://x.example.test/mcp" } } }"""),
            (Path.Combine(Home, ".claude.json"), """{ "mcpServers": { "legacy": { "type": "sse", "url": "https://y.example.test/sse", "headers": { "X": "1" } } } }"""),
            (Path.Combine(Home, ".gemini", "settings.json"), """{ "mcpServers": { "gem-sse": { "url": "https://z.example.test/sse" }, "gem-http": { "httpUrl": "https://z.example.test/mcp" }, "gem-both": { "url": "https://z.example.test/sse", "httpUrl": "https://z.example.test/mcp" } } }"""));
        Assert.Equal(["gem-sse", "legacy", "old"], scan.Servers.Where(s => s.LegacySse).Select(s => s.Name).Order());
        Assert.Equal(["bare", "gem-both", "gem-http", "new"], scan.Servers.Where(s => !s.LegacySse).Select(s => s.Name).Order());
        Assert.Equal("https://z.example.test/mcp", scan.Servers.Single(s => s.Name == "gem-both").Url); // the streamable address wins
        Assert.All(scan.Servers.Where(s => s.LegacySse), s => Assert.Equal(MCPCustomServer.Kind.Http, s.Kind)); // found as remote; the import is what refuses it
    }

    // ---- TomlLite depth

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-9")]
    public void A_deeply_nested_codex_file_is_reported_unreadable_and_nothing_throws()
    {
        var path = Path.Combine(Home, ".codex", "config.toml");
        var scan = Scan((path, "a = " + new string('[', 100_000) + "\n"));
        var problem = Assert.Single(scan.Problems);
        Assert.Equal((ForeignMcpApp.Codex, path), (problem.App, problem.Path));
        Assert.Contains("nested too deeply", problem.Reason, StringComparison.Ordinal);
        Assert.Empty(scan.Servers);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-mcpimport-9")]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void The_nesting_limit_is_sixty_four_for_arrays_and_inline_tables(int depth, bool allowed)
    {
        var path = Path.Combine(Home, ".codex", "config.toml");
        var arrays = "a = " + new string('[', depth) + new string(']', depth) + "\n";
        var tables = "a = " + string.Concat(Enumerable.Repeat("{ x = ", depth)) + "1" + string.Concat(Enumerable.Repeat(" }", depth)) + "\n";
        foreach (var text in new[] { arrays, tables })
        {
            var scan = Scan((path, text + "[mcp_servers.ok]\ncommand = \"npx\"\n"));
            Assert.Equal(allowed, scan.Problems.Count == 0);
            Assert.Equal(allowed ? 1 : 0, scan.Servers.Count);
        }
    }
}
