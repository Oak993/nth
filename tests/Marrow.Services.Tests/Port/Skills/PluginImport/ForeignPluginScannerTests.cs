using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.PluginImport;
using Marrow.Services.Tests.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.PluginImport;

// ED-NEW-mcpimport-2: the plugins other apps installed, read-only, under a temp home shaped like theirs (ForeignHome): never the real profile.
public sealed class ForeignPluginScannerTests
{
    private static string Plugin(ForeignHome home, string relative, string name, string description = "A fixture plugin.", string marker = ".claude-plugin") =>
        Path.GetDirectoryName(Path.GetDirectoryName(home.Raw(relative + "/" + marker + "/plugin.json", "{ \"name\": \"" + name + "\", \"description\": \"" + description + "\" }"))!)!;

    private static string Json(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void Claude_code_lists_what_installed_plugins_json_names_in_both_of_its_shapes()
    {
        using var home = new ForeignHome();
        var alpha = Plugin(home, ".claude/plugins/cache/market/alpha/1.0.0", "alpha", "Alpha plugin.");
        var beta = Plugin(home, ".claude/plugins/cache/market/beta/2.0.0", "beta");
        Plugin(home, ".claude/plugins/cache/market/stale/0.1.0", "stale"); // in the cache, not installed
        home.Raw(".claude/plugins/installed_plugins.json", "{ \"version\": 2, \"plugins\": { \"alpha@market\": [ { \"scope\": \"user\", \"installPath\": \"" + Json(alpha) + "\" } ], \"beta@market\": { \"installPath\": \"" + Json(beta) + "\" } } }");
        var before = ForeignHome.Fingerprint(home.Home);

        var scan = new ForeignPluginScanner(home.Home).Scan();

        Assert.Empty(scan.Problems);
        Assert.Equal(["alpha", "beta"], scan.Plugins.Select(p => p.Name));
        var first = scan.Plugins[0];
        Assert.Equal((ForeignPluginApp.ClaudeCode, "market", "Alpha plugin.", alpha), (first.App, first.Source, first.Description, first.Path));
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home)); // nothing was written or touched
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void A_plugin_with_no_install_path_is_found_through_its_marketplace()
    {
        using var home = new ForeignHome();
        var market = Path.Combine(home.Home, ".claude", "plugins", "marketplaces", "market");
        Plugin(home, ".claude/plugins/marketplaces/market/plugins/gamma", "gamma");
        home.Raw(".claude/plugins/known_marketplaces.json", "{ \"market\": { \"installLocation\": \"" + Json(market) + "\" } }");
        home.Raw(".claude/plugins/installed_plugins.json", "{ \"plugins\": { \"gamma@market\": [ { \"scope\": \"user\" } ] } }");
        Assert.Equal("gamma", Assert.Single(new ForeignPluginScanner(home.Home).Scan().Plugins).Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void A_malformed_list_or_manifest_is_reported_not_thrown()
    {
        using var home = new ForeignHome();
        home.Raw(".claude/plugins/installed_plugins.json", "{ \"plugins\": ");
        home.Raw(".codex/plugins/cache/x/broken/.codex-plugin/plugin.json", "{ name: ");
        Plugin(home, ".codex/plugins/cache/x/fine", "fine", marker: ".codex-plugin");
        var scan = new ForeignPluginScanner(home.Home).Scan();
        Assert.Equal(2, scan.Problems.Count);
        var fine = Assert.Single(scan.Plugins);
        Assert.Equal((ForeignPluginApp.Codex, "fine", "x"), (fine.App, fine.Name, fine.Source));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void A_chosen_folder_is_a_plugin_or_holds_plugins()
    {
        using var home = new ForeignHome();
        var single = Plugin(home, "picked/one", "one");
        Plugin(home, "picked/many/a", "a");
        Plugin(home, "picked/many/nested/b", "b");
        var scan = new ForeignPluginScanner(home.Home, [single, Path.Combine(home.Home, "picked", "many"), Path.Combine(home.Home, "missing")]).Scan();
        Assert.Equal(["a", "b", "one"], scan.Plugins.Select(p => p.Name).Order());
        Assert.All(scan.Plugins, p => Assert.Equal(ForeignPluginApp.Folder, p.App));
        Assert.Single(scan.Problems); // the missing folder
    }

    private sealed class FakeTarget : IPluginImportTarget
    {
        public List<(string Id, string Name)> Own { get; } = [];
        public List<(string Folder, string Name, string? Replacing)> Calls { get; } = [];

        public Task<IReadOnlyList<(string Id, string Name)>> ExistingAsync() => Task.FromResult<IReadOnlyList<(string Id, string Name)>>([.. Own]);

        public Task<string?> AddAsync(string folder, string name, string? replacingId, CancellationToken cancel)
        {
            Calls.Add((folder, name, replacingId));
            if (replacingId is not null) Own.RemoveAll(o => o.Id == replacingId);
            Own.Add(("id-" + name + Calls.Count, name));
            return Task.FromResult<string?>(null);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public async Task The_controller_skips_a_plugin_nth_has_until_replace_is_chosen()
    {
        var target = new FakeTarget();
        target.Own.Add(("own-1", "Alpha"));
        var scan = new ForeignPluginScan([new(ForeignPluginApp.ClaudeCode, "alpha", "", "market", @"C:\fixture\alpha"), new(ForeignPluginApp.Codex, "beta", "", "x", @"C:\fixture\beta")], []);
        var controller = new PluginImportController(_ => scan, target);
        await controller.OpenAsync();
        Assert.True(controller.Groups.Single(g => g.App == ForeignPluginApp.ClaudeCode).Rows.Single().Duplicate);
        Assert.Equal(DuplicateChoice.Skip, controller.Choice);
        controller.SetChoice(DuplicateChoice.KeepBoth); // not offered for plugins
        Assert.Equal(DuplicateChoice.Skip, controller.Choice);

        var first = await controller.ImportAllAsync();
        Assert.Equal((1, 0, 1), (first!.Imported, first.Replaced, first.Skipped));
        Assert.Equal([(@"C:\fixture\beta", "beta", (string?)null)], target.Calls);

        controller.Close();
        await controller.OpenAsync();
        controller.SetChoice(DuplicateChoice.Replace);
        controller.Toggle(controller.Groups.Single(g => g.App == ForeignPluginApp.ClaudeCode).Rows.Single().Key);
        var second = await controller.ImportSelectedAsync();
        Assert.Equal(1, second!.Replaced);
        Assert.Equal((@"C:\fixture\alpha", "alpha", (string?)"own-1"), target.Calls[^1]);
    }
}
