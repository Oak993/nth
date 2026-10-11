using Marrow.Services.Port.Skills.PluginImport;
using Marrow.Services.Tests.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.PluginImport;

// ED-NEW-mcpimport-3 (review of claude/mcp-import): the plugin scan honours CLAUDE_CONFIG_DIR and CODEX_HOME like the server scan does. Folders are temp folders (ForeignHome): never the real profile.
public sealed class ForeignPluginScannerHomesTests
{
    private static string Plugin(ForeignHome home, string relative, string name, string marker = ".claude-plugin") =>
        Path.GetDirectoryName(Path.GetDirectoryName(home.Raw(relative + "/" + marker + "/plugin.json", "{ \"name\": \"" + name + "\", \"description\": \"A fixture plugin.\" }"))!)!;

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void Plugins_in_claude_config_dir_and_codex_home_are_found_besides_the_usual_places()
    {
        using var home = new ForeignHome();
        Plugin(home, ".claude/plugins/cache/market/usual/1.0.0", "usual");
        Plugin(home, ".codex/plugins/cache/x/usual-codex", "usual-codex", ".codex-plugin");
        Plugin(home, "elsewhere/claude-config/plugins/cache/market/moved/1.0.0", "moved");
        Plugin(home, "elsewhere/codex-home/plugins/cache/x/moved-codex", "moved-codex", ".codex-plugin");
        var scan = new ForeignPluginScanner(home.Home, null, Path.Combine(home.Home, "elsewhere", "claude-config"), Path.Combine(home.Home, "elsewhere", "codex-home")).Scan();
        Assert.Empty(scan.Problems);
        Assert.Equal(["moved", "usual"], scan.Plugins.Where(p => p.App == ForeignPluginApp.ClaudeCode).Select(p => p.Name).Order());
        Assert.Equal(["moved-codex", "usual-codex"], scan.Plugins.Where(p => p.App == ForeignPluginApp.Codex).Select(p => p.Name).Order());
    }

    [Theory]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not\\a\\full\\path")]
    public void A_variable_that_is_empty_or_not_a_full_path_changes_nothing(string? value)
    {
        using var home = new ForeignHome();
        Plugin(home, ".claude/plugins/cache/market/usual/1.0.0", "usual");
        var scan = new ForeignPluginScanner(home.Home, null, value, value).Scan();
        Assert.Equal("usual", Assert.Single(scan.Plugins).Name);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-3")]
    public void The_same_folder_named_by_the_variable_and_the_usual_place_lists_each_plugin_once()
    {
        using var home = new ForeignHome();
        Plugin(home, ".claude/plugins/cache/market/usual/1.0.0", "usual");
        var scan = new ForeignPluginScanner(home.Home, null, Path.Combine(home.Home, ".claude"), Path.Combine(home.Home, ".codex")).Scan();
        Assert.Equal("usual", Assert.Single(scan.Plugins).Name);
    }
}
