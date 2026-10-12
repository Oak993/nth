using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.MCP.Import;

// ED-NEW-mcpimport-4, -5, -7, -10 (review of claude/mcp-import): the sheet's controller keeps the servers it cannot take out of reach, gives each found server its own tick, and imports without holding the caller's thread.
public sealed class McpImportControllerReviewTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    private static ForeignMcpServer Stdio(string name, string scope = "", string source = "", string path = @"C:\fixture\.cursor\mcp.json", params (string, string)[] env) =>
        new(ForeignMcpApp.Cursor, source, name, MCPCustomServer.Kind.Stdio, "npx", ["-y", "@fixture/" + name], "", [.. env.Select(e => new ForeignMcpVariable(e.Item1, e.Item2))], path, scope);

    private static ForeignMcpServer Remote(string name, string url, bool sse = false) =>
        new(ForeignMcpApp.Cursor, "", name, MCPCustomServer.Kind.Http, "", [], url, [], @"C:\fixture\.cursor\mcp.json", LegacySse: sse);

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public async Task The_import_gives_the_thread_back_between_servers_and_says_how_far_it_is()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScan([Stdio("one"), Stdio("two"), Stdio("three")], []);
        var phases = new List<(McpImportPhase Phase, int Done, int Total, int Saved)>();
        McpImportController? controller = null;
        controller = new McpImportController(() => scan, target, Env(), () =>
        {
            var (done, total) = controller!.Progress;
            phases.Add((controller.Phase, done, total, target.Saved.Count));
            return Task.CompletedTask;
        });
        await controller.OpenAsync();
        var changes = new List<McpImportPhase>();
        controller.Changed += () => changes.Add(controller.Phase);

        var outcome = await controller.ImportAllAsync();

        Assert.Equal(3, outcome!.Imported);
        Assert.Equal(McpImportPhase.Done, controller.Phase);
        // before each server the thread was given back, with the sheet already showing the import under way, one server further each time
        Assert.Equal([(McpImportPhase.Importing, 0, 3, 0), (McpImportPhase.Importing, 1, 3, 1), (McpImportPhase.Importing, 2, 3, 2)], phases);
        Assert.Contains(McpImportPhase.Importing, changes);
        Assert.Equal(McpImportPhase.Done, changes[^1]);
        Assert.Equal("3 imported.", controller.Summary);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public async Task While_importing_nothing_else_can_start_or_change()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScan([Stdio("one"), Stdio("two")], []);
        Task<McpImportOutcome?>? second = null;
        McpImportController? controller = null;
        controller = new McpImportController(() => scan, target, Env(), () =>
        {
            second ??= controller!.ImportAllAsync(); // pressed again mid-way
            controller!.Toggle(controller.Groups[0].Rows[0].Key);
            controller.SetChoice(DuplicateChoice.Replace);
            return Task.CompletedTask;
        });
        await controller.OpenAsync();
        var outcome = await controller.ImportAllAsync();
        Assert.Null(await second!);
        Assert.Equal(DuplicateChoice.Skip, controller.Choice);
        Assert.Equal(2, outcome!.Imported);
        Assert.Equal(["custom-one", "custom-two"], target.Saved.Select(s => s.Id));
        Assert.Equal(0, controller.TickedCount);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public async Task Closing_the_sheet_mid_import_stops_after_the_server_it_is_on_and_keeps_what_was_done()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScan([Stdio("one"), Stdio("two"), Stdio("three")], []);
        var yields = 0;
        McpImportController? controller = null;
        controller = new McpImportController(() => scan, target, Env(), () =>
        {
            if (++yields == 2) controller!.Close(); // Escape while the second server is next
            return Task.CompletedTask;
        });
        await controller.OpenAsync();
        var outcome = await controller.ImportAllAsync();
        Assert.Equal(1, outcome!.Imported);
        Assert.Equal(["custom-one"], target.Saved.Select(s => s.Id));
        Assert.Equal(McpImportPhase.Closed, controller.Phase);
        await controller.OpenAsync(); // and it can be opened again
        Assert.Equal(McpImportPhase.Choosing, controller.Phase);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public async Task Opening_the_sheet_again_while_it_is_stopping_brings_the_import_back_and_it_finishes()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScan([Stdio("one"), Stdio("two")], []);
        var yields = 0;
        McpImportController? controller = null;
        controller = new McpImportController(() => scan, target, Env(), () =>
        {
            if (++yields == 1) { controller!.Close(); _ = controller.OpenAsync(); }
            return Task.CompletedTask;
        });
        await controller.OpenAsync();
        var outcome = await controller.ImportAllAsync();
        Assert.Equal(2, outcome!.Imported);
        Assert.Equal(McpImportPhase.Done, controller.Phase);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-4")]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public async Task A_blocked_server_is_listed_with_its_reason_cannot_be_ticked_and_is_left_out_of_import_all()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScan([Stdio("fine"), Remote("old", "https://x.example.test/sse", sse: true), Stdio("prompted", env: [("TOKEN", "${input:t}")])], []);
        var controller = new McpImportController(() => scan, target, Env());
        await controller.OpenAsync();
        var group = Assert.Single(controller.Groups);
        var old = group.Rows.Single(r => r.Name == "old");
        Assert.False(old.CanTick);
        Assert.Contains("SSE", old.Blocked, StringComparison.Ordinal);
        Assert.Null(group.Rows.Single(r => r.Name == "fine").Blocked);
        var prompted = group.Rows.Single(r => r.Name == "prompted");
        Assert.True(prompted.CanTick);
        Assert.Contains("TOKEN", prompted.NeedsValue, StringComparison.Ordinal);
        Assert.Equal(1, controller.BlockedCount);

        controller.Toggle(old.Key);
        Assert.Equal(0, controller.TickedCount);
        controller.ToggleGroup(ForeignMcpApp.Cursor); // the group tick takes the ones that can be taken
        Assert.Equal(2, controller.TickedCount);
        Assert.True(controller.Groups[0].AllTicked);

        var outcome = await controller.ImportAllAsync();
        Assert.Equal((2, 0), (outcome!.Imported, outcome.Failed.Count));
        Assert.Equal(["custom-fine", "custom-prompted"], target.Saved.Select(s => s.Id));
        Assert.Equal("2 imported, 1 needs a value.", controller.Summary);
        Assert.Equal(("prompted", "TOKEN"), (outcome.NeedsValue.Single().Name, outcome.NeedsValue.Single().Keys.Single()));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    public async Task A_found_server_is_a_duplicate_by_what_it_becomes_once_its_placeholders_are_filled_in()
    {
        var target = new FakeMcpImportTarget();
        McpImport.Import([new ForeignMcpServer(ForeignMcpApp.Cursor, "", "base", MCPCustomServer.Kind.Stdio, "npx", ["-y", @"C:\data"], "", [], @"C:\x")], DuplicateChoice.Skip, target);
        var scan = new ForeignMcpScan([new ForeignMcpServer(ForeignMcpApp.VSCode, "", "renamed", MCPCustomServer.Kind.Stdio, "npx", ["-y", "${env:DATA}"], "", [], @"C:\y")], []);
        var controller = new McpImportController(() => scan, target, Env(("DATA", @"C:\data")));
        await controller.OpenAsync();
        Assert.True(controller.Groups.Single().Rows.Single().Duplicate);
        controller.ToggleGroup(ForeignMcpApp.VSCode);
        Assert.True(controller.TickedDuplicates);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-10")]
    public async Task Two_rows_that_look_alike_are_ticked_and_counted_one_by_one()
    {
        var target = new FakeMcpImportTarget();
        var scan = new ForeignMcpScanner([new(ForeignMcpApp.ClaudeCode, @"C:\fixture\.claude.json", ForeignMcpFormat.ClaudeJson)], path => path == @"C:\fixture\.claude.json" ? """
            { "projects": {
                "C:/work/app": { "mcpServers": { "db": { "command": "npx", "args": ["-y", "@fixture/one"] } } },
                "D:/old/app":  { "mcpServers": { "db": { "command": "npx", "args": ["-y", "@fixture/two"] } } } } }
            """ : null, home: @"C:\fixture\home").Scan();
        var controller = new McpImportController(() => scan, target, Env());
        await controller.OpenAsync();
        var rows = controller.Groups.Single().Rows;
        Assert.Equal(2, rows.Select(r => r.Key).Distinct().Count());
        Assert.All(rows, r => Assert.Equal("db", r.Name));
        Assert.Equal([@"C:\fixture\.claude.json (C:\work\app)", @"C:\fixture\.claude.json (D:\old\app)"], rows.Select(r => r.Origin).Order());

        controller.Toggle(rows[0].Key);
        Assert.Equal(1, controller.TickedCount);
        Assert.Equal([true, false], controller.Groups.Single().Rows.Select(r => r.Ticked));
        var outcome = await controller.ImportSelectedAsync();
        Assert.Equal(1, outcome!.Imported); // exactly what was ticked
        Assert.Equal(["@fixture/one"], target.Saved.Single().Args.Skip(1));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public async Task Nothing_to_import_returns_null_and_stays_choosing()
    {
        var controller = new McpImportController(() => new ForeignMcpScan([Remote("old", "https://x.example.test/sse", sse: true)], []), new FakeMcpImportTarget(), Env());
        await controller.OpenAsync();
        Assert.Null(await controller.ImportAllAsync());
        Assert.Null(await controller.ImportSelectedAsync());
        Assert.Equal(McpImportPhase.Choosing, controller.Phase);
    }
}
