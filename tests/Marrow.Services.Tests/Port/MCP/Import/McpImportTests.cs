using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-1: what an imported server becomes and where its values go. The target stands in for MCPStore with the same id rule and the same validation (MCPCustomError.Validate) SaveCustom applies,
// and records what reaches SaveCustomValues: a locked value goes where Add server keeps locked values (the Credential Manager), a plain one in mcp.json.
public sealed class McpImportTests
{
    internal sealed class FakeTarget : IMcpImportTarget
    {
        public List<MCPCustomServer> Saved { get; } = [];
        public Dictionary<string, IReadOnlyList<(string Key, string Value, bool IsSecret)>> Values { get; } = [];
        public List<(string Id, string? Replacing)> Calls { get; } = [];
        public HashSet<string> Connected { get; } = [];
        public List<string> Refreshed { get; } = [];

        public IReadOnlyList<MCPCustomServer> Customs => [.. Saved];

        public IReadOnlySet<string> TakenIds => Saved.Select(s => s.Id).ToHashSet(SwiftStringComparer.Instance);

        public bool IsConnected(string id) => Connected.Contains(id);

        public MCPCustomError? SaveCustom(MCPCustomServer server, string? replacing)
        {
            var old = replacing is null ? null : Saved.FirstOrDefault(s => s.Id == replacing);
            var taken = Saved.Select(s => s.Id).ToHashSet(SwiftStringComparer.Instance);
            if (old is not null) taken.Remove(old.Id);
            server = server with { Name = server.Name.Trimmed() };
            server = server with { Id = old?.Id ?? MCPCustomServer.MakeID(server.Name) };
            if (MCPCustomError.Validate(server, taken) is { } error) return error;
            var index = Saved.FindIndex(s => s.Id == server.Id);
            if (index >= 0) Saved[index] = server; else Saved.Add(server);
            Calls.Add((server.Id, replacing));
            return null;
        }

        public bool SaveCustomValues(IReadOnlyList<(string Key, string Value, bool IsSecret)> values, string id, MCPCatalogEntry? replacingFieldsOf)
        {
            Values[id] = values;
            return true;
        }

        public void Refresh(string id) => Refreshed.Add(id);
    }

    private static ForeignMcpServer Stdio(string name, string command = "npx", string[]? args = null, params (string, string)[] env) =>
        new(ForeignMcpApp.Cursor, "", name, MCPCustomServer.Kind.Stdio, command, args ?? ["-y", "@fixture/" + name], "", [.. env.Select(e => new ForeignMcpVariable(e.Item1, e.Item2))], @"C:\fixture\.cursor\mcp.json");

    private static ForeignMcpServer Remote(string name, string url, params (string, string)[] headers) =>
        new(ForeignMcpApp.ClaudeCode, "User", name, MCPCustomServer.Kind.Http, "", [], url, [.. headers.Select(h => new ForeignMcpVariable(h.Item1, h.Item2))], @"C:\fixture\.claude.json");

    // fixture values shaped like keys, built at run time so no key-like literal sits in the source
    private static string Fake(string prefix) => prefix + new string('0', 12) + "fixture" + new string('9', 12);

    [Theory]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    [InlineData("API_KEY", "anything", true)]
    [InlineData("GITHUB_TOKEN", "x", true)]
    [InlineData("X-Api-Key", "x", true)]
    [InlineData("Authorization", "x", true)]
    [InlineData("CLIENT_SECRET", "x", true)]
    [InlineData("DB_PASSWORD", "x", true)]
    [InlineData("NODE_ENV", "production", false)]
    [InlineData("LOG_LEVEL", "debug", false)]
    [InlineData("KEYBOARD_LAYOUT", "us", false)]
    [InlineData("TOKENIZERS_PARALLELISM", "false", false)]
    [InlineData("ROOT", @"C:\Users\fixture\projects\some-long-folder-name-2026", false)]
    [InlineData("BASE_URL", "https://example.test/abcdefghijklmnop1234567890", false)]
    public void Secret_routing_by_name(string key, string value, bool secret) => Assert.Equal(secret, McpImport.IsSecret(key, value));

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Secret_routing_by_the_shape_of_the_value()
    {
        Assert.True(McpImport.IsSecret("UPSTREAM", Fake("sk-")));
        Assert.True(McpImport.IsSecret("GH", Fake("ghp_")));
        Assert.True(McpImport.IsSecret("X-Upstream", "Bearer " + Fake("")));
        Assert.True(McpImport.IsSecret("OPAQUE", "a1B2c3D4e5F6g7H8i9J0k1L2m3N4"));
        Assert.False(McpImport.IsSecret("NAME", "short"));
        Assert.False(McpImport.IsSecret("LONG_WORDS", "only-letters-and-dashes-here-ok"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Import_saves_through_the_add_server_calls_with_locked_values_split_out()
    {
        var target = new FakeTarget();
        var outcome = McpImport.Import([Stdio("files", env: [("LOG_LEVEL", "info"), ("API_KEY", Fake("sk-"))]), Remote("docs", "https://docs.example.test/mcp", ("Authorization", "Bearer " + Fake("")), ("X-Team", "one"))], DuplicateChoice.Skip, target);
        Assert.Equal((2, 0, 0, 0), (outcome.Imported, outcome.Replaced, outcome.Skipped, outcome.Failed.Count));
        var files = target.Saved.Single(s => s.Id == "custom-files");
        Assert.Equal(("npx", MCPCustomServer.Kind.Stdio), (files.Command, files.ServerKind));
        Assert.Equal(["-y", "@fixture/files"], files.Args);
        Assert.Equal([new MCPCustomServer.Variable("LOG_LEVEL", false), new MCPCustomServer.Variable("API_KEY", true)], files.Env);
        Assert.Equal([("LOG_LEVEL", "info", false), ("API_KEY", Fake("sk-"), true)], target.Values["custom-files"]);
        var docs = target.Saved.Single(s => s.Id == "custom-docs");
        Assert.Equal("https://docs.example.test/mcp", docs.Url);
        Assert.Equal([true, false], docs.Headers.Select(h => h.IsSecret));
        Assert.Equal([true, false], target.Values["custom-docs"].Select(v => v.IsSecret));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void Duplicates_are_found_by_name_by_command_and_arguments_and_by_address()
    {
        var target = new FakeTarget();
        McpImport.Import([Stdio("files"), Remote("docs", "https://docs.example.test/mcp")], DuplicateChoice.Skip, target);
        Assert.NotNull(McpImport.DuplicateOf(Stdio("Files", command: "other"), target.Customs)); // the same name makes the same id
        Assert.NotNull(McpImport.DuplicateOf(Stdio("renamed", args: ["-y", "@fixture/files"]), target.Customs));
        Assert.NotNull(McpImport.DuplicateOf(Remote("elsewhere", "https://docs.example.test/mcp/"), target.Customs));
        Assert.Null(McpImport.DuplicateOf(Stdio("renamed", args: ["-y", "@fixture/other"]), target.Customs));
        Assert.Null(McpImport.DuplicateOf(Remote("elsewhere", "https://other.example.test/mcp"), target.Customs));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void A_duplicate_is_skipped_replaced_in_place_or_kept_both_under_a_new_name()
    {
        var target = new FakeTarget();
        McpImport.Import([Stdio("files")], DuplicateChoice.Skip, target);
        target.Connected.Add("custom-files");

        var skipped = McpImport.Import([Stdio("files", command: "uvx")], DuplicateChoice.Skip, target);
        Assert.Equal(1, skipped.Skipped);
        Assert.Equal("npx", target.Saved.Single().Command);

        var replaced = McpImport.Import([Stdio("files", command: "uvx")], DuplicateChoice.Replace, target);
        Assert.Equal(1, replaced.Replaced);
        Assert.Equal(("custom-files", "uvx"), (target.Saved.Single().Id, target.Saved.Single().Command));
        Assert.Equal(("custom-files", (string?)"custom-files"), target.Calls[^1]);
        Assert.Equal(["custom-files"], target.Refreshed); // it was connected: probed again, as the editor does

        var both = McpImport.Import([Stdio("files", command: "deno")], DuplicateChoice.KeepBoth, target);
        Assert.Equal(1, both.Imported);
        Assert.Equal(["custom-files", "custom-files-2"], target.Saved.Select(s => s.Id));
        Assert.Equal("files 2", target.Saved[1].Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public void What_add_server_would_refuse_is_reported_as_a_failure()
    {
        var target = new FakeTarget();
        var outcome = McpImport.Import([Remote("plain", "http://not-local.example.test/mcp"), Stdio("bad-env", env: [("1BAD", "x")])], DuplicateChoice.Skip, target);
        Assert.Equal(2, outcome.Failed.Count);
        Assert.Equal(new MCPCustomError.BadURL().Message, outcome.Failed[0].Reason);
        Assert.Empty(target.Saved);
        Assert.Equal("2 couldn\u2019t be imported.", outcome.Summary);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    public async Task The_controller_scans_lists_by_app_with_unreadable_files_and_imports_only_the_ticked()
    {
        var target = new FakeTarget();
        McpImport.Import([Stdio("files")], DuplicateChoice.Skip, target);
        var scan = new ForeignMcpScan([Stdio("files"), Stdio("memory"), Remote("docs", "https://docs.example.test/mcp")], [new ForeignMcpProblem(ForeignMcpApp.Codex, @"C:\fixture\.codex\config.toml", "bad")]);
        var controller = new McpImportController(() => scan, target);
        await controller.OpenAsync();
        Assert.Equal(McpImportPhase.Choosing, controller.Phase);
        Assert.Equal(DuplicateChoice.Skip, controller.Choice);
        Assert.Equal([ForeignMcpApp.ClaudeCode, ForeignMcpApp.Codex, ForeignMcpApp.Cursor], controller.Groups.Select(g => g.App));
        var unreadable = controller.Groups.Single(g => g.App == ForeignMcpApp.Codex).Rows.Single();
        Assert.True(unreadable.Unreadable);
        Assert.Equal("config.toml", unreadable.Name);
        Assert.True(controller.Groups.Single(g => g.App == ForeignMcpApp.Cursor).Rows.Single(r => r.Name == "files").Duplicate);

        controller.Toggle(unreadable.Key); // not a server: nothing to tick
        Assert.Equal(0, controller.TickedCount);
        controller.Toggle(controller.Groups.Single(g => g.App == ForeignMcpApp.Cursor).Rows.Single(r => r.Name == "memory").Key);
        var outcome = await controller.ImportSelectedAsync(); // ED-NEW-mcpimport-7: the import is async (it hands the thread back between servers); the assertions below are unchanged
        Assert.Equal(1, outcome!.Imported);
        Assert.Equal(McpImportPhase.Done, controller.Phase);
        Assert.Equal(["custom-files", "custom-memory"], target.Saved.Select(s => s.Id));
    }
}
