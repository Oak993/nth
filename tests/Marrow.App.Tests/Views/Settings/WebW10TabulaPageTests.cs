using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using static Marrow.App.Tests.Fixtures.WebSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY: web surfaces W10, Settings > Tabula (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; 6.5, 7.2, 7.7, 10; ED-NEW-web-w10-4, -5). Live answers in chat, Visual answers (Fewer, Balanced, More), Inline HTML widgets, MCP Apps, the street tile source
// (an https address with {z} {x} {y}, checked, removable), whether artifacts may use the reader's model, and the storage artifacts use per thread with a confirmed Clear.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-4")]
public sealed class WebW10TabulaPageTests(HeadlessSession headless)
{
    private static (WebSettings Web, TabulaSettingsParts Parts) Make(Action<WebSettings>? setup = null, Func<CancellationToken, Task<IReadOnlyList<ArtifactStorageRow>>>? storage = null,
        Func<Guid, Task>? clear = null, Func<string, string, string, Task<bool>>? confirm = null)
    {
        var web = new WebSettings(new InMemorySettingsStore());
        setup?.Invoke(web);
        var parts = new TabulaSettingsParts
        {
            Settings = () => web,
            Storage = storage,
            ClearArtifacts = clear,
            Confirm = confirm ?? ((_, _, _) => Task.FromResult(true)),
        };
        return (web, parts);
    }

    [Fact]
    public void Every_row_of_the_approved_list_is_on_the_page_in_order() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            var texts = Texts(page).ToList();
            string[] rows = ["Live answers in chat", "Visual answers", "Inline HTML widgets", "MCP Apps", "Map street tiles source", "Artifacts may use your model", "Artifact storage used"];
            var at = -1;
            foreach (var row in rows)
            {
                var found = texts.IndexOf(row);
                Assert.True(found > at, $"\"{row}\" is missing or out of order");
                at = found;
            }
            Assert.Equal("Tabula", TabulaSettingsPage.Title);
            Assert.True(TabulaSettingsPage.IsReady);
        });
    });

    [Fact]
    public void The_switches_start_on_and_each_edits_only_its_own_setting() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            foreach (var name in new[] { "Live answers in chat", "Inline HTML widgets", "MCP Apps" }) Assert.True(IsOn(page, name), name);
            Toggle(page, "Live answers in chat");
            Assert.False(web.LiveAnswers);
            Assert.True(web.InlineHtmlWidgets && web.McpApps);
            Toggle(page, "Inline HTML widgets");
            Assert.False(web.InlineHtmlWidgets);
            Toggle(page, "MCP Apps");
            Assert.False(web.McpApps);
            Assert.Equal(VisualAnswerLevel.Balanced, web.VisualAnswers);
            Assert.Equal(ArtifactModelChoice.AskEachArtifact, web.ArtifactModel);
        });
    });

    [Fact]
    public void Visual_answers_is_a_strip_of_Fewer_Balanced_More_that_starts_on_Balanced() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            var picker = Named<Control>(page, "Visual answers");
            Assert.Equal(["Fewer", "Balanced", "More"], SegmentTitles(picker));
            Segment(picker, 0);
            Assert.Equal(VisualAnswerLevel.Fewer, web.VisualAnswers);
            Segment(picker, 2);
            Assert.Equal(VisualAnswerLevel.More, web.VisualAnswers);
            Segment(picker, 1);
            Assert.Equal(VisualAnswerLevel.Balanced, web.VisualAnswers);
        });
    });

    [Fact]
    public void Artifacts_may_use_your_model_is_Ask_each_artifact_or_Never() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            var picker = Named<Control>(page, "Artifacts may use your model");
            Assert.Equal(["Ask each artifact", "Never"], SegmentTitles(picker));
            Segment(picker, 1);
            Assert.Equal(ArtifactModelChoice.Never, web.ArtifactModel);
            Segment(picker, 0);
            Assert.Equal(ArtifactModelChoice.AskEachArtifact, web.ArtifactModel);
        });
    });

    [Fact]
    public void A_setting_changed_from_elsewhere_moves_the_switch_and_the_page_still_stands() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            web.LiveAnswers = false;
            web.McpApps = false;
            Settle(window);
            Assert.False(IsOn(page, "Live answers in chat"));
            Assert.False(IsOn(page, "MCP Apps"));
            Assert.True(IsOn(page, "Inline HTML widgets"));
        });
    });

    [Fact]
    public void The_tile_source_starts_as_None_and_Set_opens_an_editor_that_checks_the_address() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Equal("None", page.TilesText);
            Assert.False(page.IsEditingTiles);
            Assert.False(Named<CzCapsuleButton>(page, "Remove map street tiles source").IsVisible);
            Press(Named<CzCapsuleButton>(page, "Set map street tiles source"));
            Settle(window);
            Assert.True(page.IsEditingTiles);

            page.TilesField.Text = "http://tiles.example.org/{z}/{x}/{y}.png";
            page.UseTiles();
            Assert.Equal(TabulaSettingsPage.TilesInvalid, page.TilesProblem);
            Assert.Equal("", web.MapTileSource);
            Assert.True(page.IsEditingTiles);

            page.TilesField.Text = "https://localhost/{z}/{x}/{y}.png";
            page.UseTiles();
            Assert.Equal("", web.MapTileSource);

            page.TilesField.Text = "https://tiles.example.org/{z}/{x}/{y}.png";
            page.UseTiles();
            Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", web.MapTileSource);
            Assert.False(page.IsEditingTiles);
            Assert.Equal("", page.TilesProblem);
            Settle(window);
            Assert.Equal("tiles.example.org", page.TilesText);
            Assert.True(Named<CzCapsuleButton>(page, "Remove map street tiles source").IsVisible);
        });
    });

    [Fact]
    public void Remove_takes_the_tile_source_away_and_Escape_closes_the_editor_without_keeping_the_text() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make(setup: settings => settings.MapTileSource = "https://tiles.example.org/{z}/{x}/{y}.png");
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Equal("tiles.example.org", page.TilesText);
            Press(Named<CzCapsuleButton>(page, "Set map street tiles source"));
            Settle(window);
            Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", page.TilesField.Text);
            page.TilesField.Text = "https://other.example.org/{z}/{x}/{y}.png";
            page.TilesField.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Settle(window);
            Assert.False(page.IsEditingTiles);
            Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", web.MapTileSource);

            Press(Named<CzCapsuleButton>(page, "Remove map street tiles source"));
            Settle(window);
            Assert.Equal("", web.MapTileSource);
            Assert.Equal("None", page.TilesText);
        });
    });

    [Fact]
    public void Enter_in_the_editor_keeps_the_address() => headless.OnUiThread(() =>
    {
        var (web, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            page.ToggleTilesEditor();
            Settle(window);
            page.TilesField.Text = "https://tiles.example.org/{z}/{x}/{y}.png";
            page.TilesField.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Settle(window);
            Assert.Equal("https://tiles.example.org/{z}/{x}/{y}.png", web.MapTileSource);
        });
    });

    [Fact]
    public void The_tile_row_tells_the_reader_who_learns_what_before_a_source_is_added() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make();
        var page = new TabulaSettingsPage(parts);
        Show(page, window => Assert.Contains(TabulaSettingsPage.TilesDetail, Texts(page)));
        Assert.Contains("learns which area you look at", TabulaSettingsPage.TilesDetail, StringComparison.Ordinal);
    });

    [Fact]
    public void Artifact_storage_is_counted_off_the_UI_thread_listed_per_thread_largest_first_and_Clear_asks_first() => headless.OnUiThread(() =>
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var cleared = new List<Guid>();
        var counts = 0;
        var answer = false;
        var questions = new List<string>();
        var (_, parts) = Make(
            storage: _ => { counts++; return Task.FromResult<IReadOnlyList<ArtifactStorageRow>>([new ArtifactStorageRow(two, "Big thread", 3_500_000), new ArtifactStorageRow(one, "Small thread", 12_000)]); },
            clear: thread => { cleared.Add(thread); return Task.CompletedTask; },
            confirm: (question, _, _) => { questions.Add(question); return Task.FromResult(answer); });
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            Settle(window);
            Assert.True(counts >= 1);
            var texts = Texts(page);
            Assert.Contains("Big thread", texts);
            Assert.Contains("3.3 MB", texts);
            Assert.Contains("Small thread", texts);
            Assert.Contains("11 KB", texts);
            Assert.Equal(["Big thread", "Small thread"], page.ListedStorage.Select(row => row.Title));

            Press(Named<CzCapsuleButton>(page, "Clear artifacts of Small thread"));
            Settle(window);
            Assert.Equal(["Clear the artifacts of \"Small thread\"?"], questions);
            Assert.Empty(cleared);

            answer = true;
            Press(Named<CzCapsuleButton>(page, "Clear artifacts of Small thread"));
            Settle(window);
            Assert.Equal([one], cleared);
            Assert.True(counts >= 2); // counted again after the clear
        });
    });

    [Fact]
    public void With_no_thread_holding_artifacts_the_page_says_so_and_a_failed_count_does_not_break_it() => headless.OnUiThread(() =>
    {
        var (_, parts) = Make(storage: _ => Task.FromResult<IReadOnlyList<ArtifactStorageRow>>([]));
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            Settle(window);
            Assert.Contains(TabulaSettingsPage.StorageEmpty, Texts(page));
        });
        var (_, none) = Make();
        var bare = new TabulaSettingsPage(none);
        Show(bare, window => Assert.Contains(TabulaSettingsPage.StorageEmpty, Texts(bare)));
    });

    [Fact]
    public void A_page_that_leaves_cancels_a_count_still_running() => headless.OnUiThread(() =>
    {
        CancellationToken seen = default;
        var release = new TaskCompletionSource<IReadOnlyList<ArtifactStorageRow>>();
        var (_, parts) = Make(storage: token => { seen = token; return release.Task; });
        var page = new TabulaSettingsPage(parts);
        Show(page, window =>
        {
            Settle(window);
            Assert.Contains(TabulaSettingsPage.StorageLoading, Texts(page));
            Assert.False(seen.IsCancellationRequested);
        });
        Assert.True(seen.IsCancellationRequested);
        release.SetResult([]);
    });

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(1023L, "0 KB")]
    [InlineData(2048L, "2 KB")]
    [InlineData(1_048_575L, "1023 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(5_452_595L, "5.2 MB")]
    [InlineData(1_610_612_736L, "1.5 GB")]
    public void Sizes_read_in_KB_MB_and_GB(long bytes, string expected) => Assert.Equal(expected, TabulaSettingsPage.SizeText(bytes));

    [Fact]
    public void Every_control_has_a_name_and_the_page_is_made_from_parts_alone() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var page = new TabulaSettingsPage();
        Show(page, window =>
        {
            Assert.Contains("Live answers in chat", Texts(page));
            foreach (var toggle in page.GetVisualDescendants().OfType<SmallSwitch>().Select(item => item.FindAncestorOfType<SettingsSwitch>()!))
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)));
            foreach (var button in page.GetVisualDescendants().OfType<CzCapsuleButton>())
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(page.TilesField)));
        });
        Assert.Null(TabulaSettingsPage.Bound());
    });
}
