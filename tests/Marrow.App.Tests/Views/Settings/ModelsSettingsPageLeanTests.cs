using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.ModelsSettingsSectionRig;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-perf-ui-thread-1: Settings > Models with a provider whose catalogue holds hundreds of models (OpenRouter). The rows are made in turns (the first screen at once), a search
// keystroke touches only the rows whose state changed, and the ids that match a query are worked out once for a catalogue. The look, the order and the states are those of the page
// that made every row in one turn: the existing ModelsSettingsPage* classes hold those, and the checks here hold the same of the rows made in turns.
[Collection("Headless")]
public sealed class ModelsSettingsPageLeanTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Codex;

    private static ValueList<ModelOption> Catalogue(int count) =>
        new(Enumerable.Range(0, count).Select(i => new ModelOption("m" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Model " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    private static Action<AppModel> Seed(int count, params int[] chosen) => model =>
    {
        model.Providers.UpdateCatalog(Catalogue(count), Provider);
        foreach (var index in chosen) model.Settings.AddToModelList(new ModelPin(Provider, "m" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    };

    private static List<string> Titles(Control control) => Of<ChromeRow>(control).Select(row => Of<TextBlock>(row).First().Text ?? "").ToList();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: the first screen of rows at once, the rest in turns of a few
    public void A_catalogue_of_four_hundred_models_makes_its_first_screen_at_once_and_the_rest_a_few_rows_at_a_time() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Seed(400)(model);
        var section = new ProviderModelsSection(model, Provider, new Binding<bool>(() => true, _ => { }));

        // The constructor's turn: the first screen. Nothing else is made until the loop gives the next turn.
        Assert.Equal([ProviderModelsSection.FirstRows], section.BuildTurns);
        Assert.Equal(400 - ProviderModelsSection.FirstRows, section.PendingRows);
        Assert.Equal(ProviderModelsSection.FirstRows, Of<ChromeRow>(section).Count());

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, section.PendingRows);
        Assert.Equal(400, Of<ChromeRow>(section).Count());
        Assert.Equal(400, section.BuildTurns.Sum());
        Assert.Equal(ProviderModelsSection.FirstRows, section.BuildTurns[0]);
        Assert.All(section.BuildTurns.Skip(1), made => Assert.InRange(made, 1, ProviderModelsSection.RowsPerTurn));
        Assert.Equal(1 + (400 - ProviderModelsSection.FirstRows + ProviderModelsSection.RowsPerTurn - 1) / ProviderModelsSection.RowsPerTurn, section.BuildTurns.Count);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: a catalogue that fits the first screen is made in the one turn, as it always was
    public void A_catalogue_of_a_screen_or_less_is_made_whole_in_the_one_turn() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Seed(ProviderModelsSection.FirstRows)(model);
        var section = new ProviderModelsSection(model, Provider, new Binding<bool>(() => true, _ => { }));

        Assert.Equal([ProviderModelsSection.FirstRows], section.BuildTurns);
        Assert.Equal(0, section.PendingRows);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([ProviderModelsSection.FirstRows], section.BuildTurns);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: rows made in turns are where the catalogue's order puts them, whatever was made first
    public void With_the_section_closed_the_chosen_models_are_made_first_and_every_row_ends_in_the_catalogue_order_in_its_state() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Seed(90, 70, 41)(model);
        var section = new ProviderModelsSection(model, Provider, new Binding<bool>(() => false, _ => { }));

        // Closed, only the chosen rows show: they are in the first turn though they are far down the catalogue.
        var first = Titles(section);
        Assert.Contains("Model 41", first);
        Assert.Contains("Model 70", first);
        Assert.Equal(["Model 41", "Model 70"], first.Where(title => title is "Model 41" or "Model 70").ToList()); // and in the catalogue's order among themselves
        Dispatcher.UIThread.RunJobs();

        var all = Titles(section);
        Assert.Equal(Enumerable.Range(0, 90).Select(i => "Model " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(), all);
        foreach (var row in Of<ChromeRow>(section))
        {
            var shown = Of<TextBlock>(row).First().Text is "Model 41" or "Model 70";
            Assert.Equal(shown, row.IsEnabled);
            Assert.Equal(shown, row.IsHitTestVisible);
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: a search typed while the rows are still being made shows the rows that match, in the catalogue's order
    public void A_search_typed_before_the_rest_is_made_still_ends_with_the_matching_rows_in_order() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Seed(120)(model);
        var section = new ProviderModelsSection(model, Provider, new Binding<bool>(() => true, _ => { }));
        Assert.True(section.PendingRows > 0);

        section.SetQuery("Model 11"); // matches Model 11 and Model 110 to 119
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, section.PendingRows);
        var shown = Of<ChromeRow>(section).Where(row => row.IsEnabled).Select(row => Of<TextBlock>(row).First().Text!).ToList();
        Assert.Equal(["Model 11", .. Enumerable.Range(110, 10).Select(i => "Model " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))], shown);
        Assert.Equal(120, Titles(section).Count);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: a search keystroke keeps the glyph of every row whose state did not change (each keystroke made a glyph control for every row)
    public void A_search_keystroke_keeps_the_glyph_of_a_row_that_is_still_as_it_was() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Seed(60, 5));
        Dictionary<string, SwiftImage> Glyphs() => Of<ChromeRow>(rig.SectionOf(Provider)).ToDictionary(row => Of<TextBlock>(row).First().Text!, row => Of<SwiftImage>(Of<Button>(row).Single()).Single());
        var before = Glyphs();
        Assert.Equal(60, before.Count);
        Assert.Equal("checkmark.circle.fill", before["Model 5"].SymbolName);

        rig.Page.SetQuery("Model 1");
        rig.Sync();
        rig.Page.SetQuery("");
        rig.Sync();

        var after = Glyphs();
        Assert.All(before, pair => Assert.Same(pair.Value, after[pair.Key]));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: the toggle still swaps its glyph when the model is chosen or dropped
    public void Choosing_a_model_still_replaces_the_glyph_of_its_row_and_only_that_of_its_row() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Seed(40));
        ChromeRow Row(string title) => Of<ChromeRow>(rig.SectionOf(Provider)).Single(row => Of<TextBlock>(row).First().Text == title);
        string Symbol(string title) => Of<SwiftImage>(Of<Button>(Row(title)).Single()).Last().SymbolName ?? "";
        var other = Of<SwiftImage>(Of<Button>(Row("Model 7")).Single()).Single();

        Assert.Equal("plus.circle", Symbol("Model 3"));
        rig.Click(Of<Button>(Row("Model 3")).Single());
        rig.Advance(1);

        Assert.Equal("checkmark.circle.fill", Symbol("Model 3"));
        Assert.Same(other, Of<SwiftImage>(Of<Button>(Row("Model 7")).Single()).Single());
        rig.Click(Of<Button>(Row("Model 3")).Single());
        rig.Advance(1);
        Assert.Equal("plus.circle", Symbol("Model 3"));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-1: the ids that match a query are worked out once for a catalogue and kept for its last four queries
    public void The_ids_that_match_a_query_are_worked_out_once_per_catalogue_and_query() => headless.OnUiThread(() =>
    {
        var catalogue = Catalogue(50);
        var first = ModelsSettingsPage.MatchingIDs(catalogue, Provider, "Model 4");
        Assert.Same(first, ModelsSettingsPage.MatchingIDs(catalogue, Provider, "Model 4"));
        Assert.Equal(catalogue.Where(option => ModelsSettingsPage.Matches(option, option.Id, Provider, "Model 4")).Select(option => option.Id).ToHashSet(), first);
        Assert.Equal(11, first.Count); // Model 4 and Model 40 to 49

        // Another catalogue (a new list) is another entry, whatever its content.
        var equal = Catalogue(50);
        Assert.NotSame(first, ModelsSettingsPage.MatchingIDs(equal, Provider, "Model 4"));
        Assert.Equal(first, ModelsSettingsPage.MatchingIDs(equal, Provider, "Model 4"));
        // The last four queries are kept; a fifth drops the oldest.
        foreach (var query in new[] { "Model 1", "Model 2", "Model 3", "Model 5" }) ModelsSettingsPage.MatchingIDs(catalogue, Provider, query);
        Assert.NotSame(first, ModelsSettingsPage.MatchingIDs(catalogue, Provider, "Model 4"));
        Assert.Empty(ModelsSettingsPage.MatchingIDs(catalogue, Provider, "no such model"));
    });
}
