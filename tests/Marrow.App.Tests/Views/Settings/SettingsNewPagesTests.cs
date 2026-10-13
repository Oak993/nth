using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// SA-1 (customization-SPEC CZ-2.1, CZ-2.2, OD-0099 Sections C and Q-CZ-1 A, OD-0100): the Settings pages the first release adds,
// in sidebar order, hidden behind their shell's IsReady const until the unit that builds the page sets it.
[Collection("Headless")]
public sealed class SettingsNewPagesTests(HeadlessSession headless)
{
    private static readonly IReadOnlyList<SettingsPage> NewPages = UpstreamSettingsPages.PortAdded;

    private static readonly string[] UpstreamRaw = ["general", "hydra", "appearance", "providers", "mcp", "models", "sourceControl", "shortcuts", "archive", "about"];

    private static readonly string[] UpstreamTitles = ["General", "Hydra", "Appearance", "Providers", "MCP", "Models", "Source control", "Shortcuts", "Archive", "About"];

    // CZ-4 sets Customization ready: it is listed right after Appearance (CZ-2.1); the other new pages are still hidden

    private static string[] ReadyTitles => SettingsSidebar.Pages("").Select(page => page.Title).ToArray();

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T1 / CZ-2.1: raw values, and AllCases puts Customization and Voice typing after Appearance; Updates takes the Tools place; Plugins sits directly above Skills
    [Trait("Spec", "CZ-T1")]
    public void The_new_pages_sit_in_the_sidebar_order_with_their_raw_values()
    {
        var pages = SwiftEnum<SettingsPage>.AllCases;
        Assert.Equal(
            ["general", "hydra", "ignis", "assistant", "appearance", "customization", "voiceTyping", "providers", "updates", "mcp", "plugins", "skills", "models", "pills", "sourceControl", "shortcuts", "archive", "about"],
            pages.Select(page => page.Id));
        Assert.Equal(["customization", "voiceTyping", "updates", "plugins", "skills", "pills"], NewPages.Select(page => page.Id));
        Assert.Equal(pages.ToList().IndexOf(SettingsPage.Plugins) + 1, pages.ToList().IndexOf(SettingsPage.Skills));
        Assert.Equal(SettingsPage.Ignis, pages[2]); // OD-0134 (ED-NEW-ignis-settings): Ignis directly below Hydra
        Assert.Equal(SettingsPage.Assistant, pages[3]); // Dextra AS-8 (ED-NEW-dextra-as8-8): Settings › Assistant directly after Ignis
        Assert.Equal(SettingsPage.Appearance, pages[4]);
        Assert.Equal(SettingsPage.Customization, pages[5]);
        Assert.Equal(SettingsPage.VoiceTyping, pages[6]);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the pinned upstream pages keep their order, titles and raw values with the new ones taken out (PR-SETTINGS-005)
    public void The_upstream_pages_keep_their_relative_order_and_texts()
    {
        var upstream = UpstreamSettingsPages.All;
        Assert.Equal(UpstreamRaw, upstream.Select(page => page.Id));
        Assert.Equal(UpstreamTitles, upstream.Select(page => page.Title));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T2 / CZ-2.1: titles, symbols and the Q-CZ-4 search words, exactly
    [Trait("Spec", "CZ-T2")]
    public void Titles_symbols_and_keywords_are_the_specs()
    {
        Assert.Equal(["Customization", "Voice typing", "Updates", "Plugins", "Skills", "Pills"], NewPages.Select(page => page.Title));
        Assert.Equal(["sparkles", "waveform", "arrow.triangle.2.circlepath", "powerplug", "wand.and.sparkles", "pill.fill"], NewPages.Select(page => page.Symbol));
        Assert.Equal(["light", "glow", "beam", "composer", "effects", "customization", "orb", "voice", "dots", "uiverse", "aura"], SettingsPage.Customization.Keywords);
        Assert.Equal(["voice", "dictation", "microphone", "mic", "whisper", "speech", "transcribe", "language", "model", "hardware"], SettingsPage.VoiceTyping.Keywords);
        Assert.Equal(["updates", "update", "version", "latest", "upgrade", "check now", "restart"], SettingsPage.Updates.Keywords);
        Assert.Equal(["plugins", "plugin", "download", "install", "add"], SettingsPage.Plugins.Keywords);
        Assert.Equal(["skills", "skill", "download", "install", "add", "slash"], SettingsPage.Skills.Keywords);
        Assert.Equal(["pills", "pill", "accounts", "chain", "sequence", "handshake", "fallback", "switch"], SettingsPage.Pills.Keywords); // MA-8, T11 step 2
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T3 / CZ-2.1.1: every new page symbol resolves, and the sidebar row draws a non-empty icon
    [Trait("Spec", "CZ-T3")]
    public void Every_new_pages_symbol_draws_a_non_empty_icon() => headless.OnUiThread(() =>
    {
        foreach (var page in NewPages)
        {
            Assert.NotNull(SymbolCatalogue.Mapping(page.Symbol));
            Assert.False(SwiftImage.SystemName(page.Symbol).IsEmpty, page.Title);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-2.1.2: the Tint arms are Chrome.Gray, and no code path of the app reads a SettingsPage's Tint
    [Trait("Spec", "CZ-T4")]
    public void The_new_tints_are_gray_and_no_sidebar_code_reads_a_tint()
    {
        var theme = Avalonia.Styling.ThemeVariant.Light;
        foreach (var page in NewPages)
            Assert.Equal(Chrome.Gray.Resolve(theme), page.Tint.Resolve(theme));
        var read = new Regex(@"(\bpage|\bitem|\bSettingsPage\.\w+)\.Tint\b", RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Marrow.App"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            Assert.False(read.IsMatch(File.ReadAllText(file)), file + " reads a SettingsPage's Tint");
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T2 / CZ-2.1.3: search matches titles and keywords for a ready page ("glow" finds Customization); a hidden page is never found
    [Trait("Spec", "CZ-T2")]
    public void Search_finds_a_ready_page_by_title_or_keyword_and_never_a_hidden_one()
    {
        Func<SettingsPage, bool> allReady = _ => true;
        Assert.Equal([SettingsPage.Customization], SettingsSidebar.Pages("glow", allReady));
        Assert.Equal([SettingsPage.Customization], SettingsSidebar.Pages("  GLOW ", allReady));
        Assert.Equal([SettingsPage.Customization, SettingsPage.VoiceTyping], SettingsSidebar.Pages("voice", allReady));
        Assert.Equal([SettingsPage.VoiceTyping], SettingsSidebar.Pages("dictation", allReady));
        Assert.Equal([SettingsPage.Updates], SettingsSidebar.Pages("latest", allReady));
        Assert.Equal([SettingsPage.Plugins, SettingsPage.Skills], SettingsSidebar.Pages("install", allReady));
        Assert.Equal(SwiftEnum<SettingsPage>.AllCases, SettingsSidebar.Pages("", allReady));
        Assert.Equal([SettingsPage.Customization], SettingsSidebar.Pages("glow")); // CZ-4: Customization is ready
        Assert.Equal([SettingsPage.VoiceTyping], SettingsSidebar.Pages("dictation")); // VT-13: Voice typing is ready, so found
        Assert.Equal(["General", "Providers"], SettingsSidebar.Pages("credits").Select(page => page.Title));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0103 / V-2.6 pattern: each page has an IsReady const that is false until its unit sets it (Customization: CZ-4 has; Voice typing: VT-13 has)
    public void Each_new_page_has_an_IsReady_const_that_its_unit_sets()
    {
        (SettingsPage Page, string Type, bool Ready)[] shells =
        [
            (SettingsPage.Customization, "CustomizationSettingsPage", true), (SettingsPage.VoiceTyping, "VoiceTypingSettingsPage", true),
            (SettingsPage.Updates, "UpdatesSettingsPage", true), (SettingsPage.Plugins, "PluginsSettingsPage", true), (SettingsPage.Skills, "SkillsSettingsPage", true), (SettingsPage.Pills, "PillsSettingsPage", true), // UE-4, SK-15 and MA-8 supply the ready page bodies.
        ];
        foreach (var (page, name, ready) in shells)
        {
            var type = typeof(SettingsSidebar).Assembly.GetType("Marrow.App.Port.Settings." + name);
            Assert.NotNull(type);
            var field = type.GetField("IsReady", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.True(field.IsLiteral, name + ".IsReady is a const");
            Assert.Equal(typeof(bool), field.FieldType);
            Assert.Equal(ready, field.GetRawConstantValue());
            Assert.Equal((bool)field.GetRawConstantValue()!, page.IsReady);
            Assert.True(typeof(Control).IsAssignableFrom(type));
        }
        Assert.All(SwiftEnum<SettingsPage>.AllCases.Where(page => !NewPages.Contains(page)), page => Assert.True(page.IsReady, page.Title));
        Assert.Equal(SwiftEnum<SettingsPage>.AllCases.Where(page => page.IsReady).Select(page => page.Id), SettingsSidebar.Pages("").Select(page => page.Id));
        Assert.Equal(SettingsPage.Assistant, SettingsSidebar.Pages("")[3]); // [2] is Ignis (OD-0134), [3] the Assistant (Dextra AS-8)
        Assert.Equal(SettingsPage.Appearance, SettingsSidebar.Pages("")[4]);
        Assert.Equal(SettingsPage.Customization, SettingsSidebar.Pages("")[5]);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-2.2: the factory line per page, and the chrome row (hasControls) per page
    public void The_factory_builds_each_new_pages_shell_and_the_chrome_row_follows_the_spec()
    {
        headless.OnUiThread(() =>
        {
            Assert.IsType<CustomizationSettingsPage>(SettingsView.PageFor(SettingsPage.Customization, "", ""));
            Assert.IsType<VoiceTypingSettingsPage>(SettingsView.PageFor(SettingsPage.VoiceTyping, "", ""));
            Assert.IsType<UpdatesSettingsPage>(SettingsView.PageFor(SettingsPage.Updates, "", ""));
            Assert.IsType<PluginsSettingsPage>(SettingsView.PageFor(SettingsPage.Plugins, "", ""));
            Assert.IsType<SkillsSettingsPage>(SettingsView.PageFor(SettingsPage.Skills, "", ""));
            Assert.IsType<PillsSettingsPage>(SettingsView.PageFor(SettingsPage.Pills, "", "")); // MA-8
            Assert.True(SettingsView.HasChromeControls(SettingsPage.Customization, new CustomizationSettingsPage()));   // its chrome row carries the Preview control (CZ-3.2)
            Assert.False(SettingsView.HasChromeControls(SettingsPage.VoiceTyping, new VoiceTypingSettingsPage()));      // no chrome accessory (V-16.3)
            Assert.False(SettingsView.HasChromeControls(SettingsPage.Updates, new UpdatesSettingsPage()));
            Assert.False(SettingsView.HasChromeControls(SettingsPage.Plugins, new PluginsSettingsPage()));
            Assert.False(SettingsView.HasChromeControls(SettingsPage.Skills, new SkillsSettingsPage()));
            // the pages upstream names keep the source's list (:270-272)
            Assert.True(SettingsView.HasChromeControls(SettingsPage.Providers, new Border()));
            Assert.False(SettingsView.HasChromeControls(SettingsPage.General, new Border()));
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // a page unit supplies its own chrome accessories through ISettingsPageChrome, so SettingsView.cs is never edited again
    public void A_page_that_implements_the_chrome_interface_asks_for_the_row_and_supplies_its_controls()
    {
        headless.OnUiThread(() =>
        {
            var page = new ChromePageSample();
            Assert.True(SettingsView.HasChromeControls(SettingsPage.Skills, page));
            Assert.Single(SettingsView.ChromeAccessoriesOf(page));
            Assert.Empty(SettingsView.ChromeAccessoriesOf(new Border()));
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // SK-15: both ready pages appear in the real sidebar and its search.
    public void Ready_pages_are_present_in_the_sidebar_and_search() => SettingsViewRig.Run(headless, (view, model, sidebar) =>
    {
        Assert.Equal(ReadyTitles, SettingsViewRig.RowTitles(sidebar));
        foreach (var (query, title) in new[] { ("plugins", "Plugins"), ("skills", "Skills") }) // SK-15 made the two ready: each is found by its title
        {
            SettingsViewRig.Search(sidebar, query);
            var titles = SettingsViewRig.RowTitles(sidebar);
            Assert.Equal(query == "install" ? ["Plugins", "Skills"] : query == "plugins" ? ["Plugins"] : new[] { "Skills" }, titles);
        }
        foreach (var query in new[] { "voice typing", "dictation", "microphone" }) // VT-13: the ready page is found by its title and its keywords
        {
            SettingsViewRig.Search(sidebar, query);
            Assert.Equal(["Voice typing"], SettingsViewRig.RowTitles(sidebar));
        }
        SettingsViewRig.Search(sidebar, "glow");
        Assert.Equal(["Customization"], SettingsViewRig.RowTitles(sidebar)); // CZ-4: the ready page is found by its keyword
        SettingsViewRig.Search(sidebar, "");
        Assert.Equal(ReadyTitles, SettingsViewRig.RowTitles(sidebar));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // a request for a hidden page (from an app command or a link) is consumed and the window stays where it was
    public void A_request_for_a_hidden_page_is_consumed_and_changes_nothing() => SettingsViewRig.Run(headless, (view, model, sidebar) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Hydra;
        SettingsViewRig.Sync();
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Equal(SettingsPage.Hydra, SettingsViewRig.CurrentPage(view));
        foreach (var hidden in NewPages.Where(page => !page.IsReady)) // CZ-4 made Customization ready: it opens when asked
        {
            SettingsNavigation.Shared.RequestedPage = hidden;
            SettingsViewRig.Sync();
            Assert.Null(SettingsNavigation.Shared.RequestedPage);
            Assert.Equal(SettingsPage.Hydra, SettingsViewRig.CurrentPage(view));
        }
    });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Marrow.sln not found above the test output");
    }
}
