using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Audit-fix tests for DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class ModelsSettingsPageAuditTests(HeadlessSession headless)
{
    private static ModelOption Option(string id, string name) => new(id, name);

    private void With(Action<AppModel> seed, string query, Action<ModelsSettingsPage, AppModel, Avalonia.Controls.Window> check) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness(); var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Providers)!;
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases) attempted.Add(provider); // never launch a provider CLI
        typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[ProviderKind.Claude] = new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch") }));
        seed(model);
        var host = new Border { Padding = new Thickness(20) }; AppEnvironment.SetAppModel(host, model);
        var page = new ModelsSettingsPage(query); host.Child = page;
        var window = new Avalonia.Controls.Window { Width = 700, Height = 1600, Content = host };
        try { window.Show(); Sync(window); check(page, model, window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static void Sync(Avalonia.Controls.Window window) { for (var i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
    private static void Advance(Avalonia.Controls.Window window, double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        ((ManualTimeProvider)SwiftClock.Provider.Current).Advance(span); ((ManualFrameSource)FrameClock.Source.Current).Advance(span); Sync(window);
    }
    private static IEnumerable<T> Of<T>(Control c) where T : class => LogicalQuery.SelfAndDescendants<T>(c);
    private static string AllText(Control c) => string.Join("\n", Of<TextBlock>(c).Select(t => t.Text ?? ""));
    private static ProviderModelsSection ClaudeSection(Control page) => Of<ProviderModelsSection>(page).First(s => Of<Button>(s).Any(b => AutomationProperties.GetName(b) == "Claude"));
    private static ModelPin Pin(string id) => new(ProviderKind.Claude, id);
    private static Point Centre(Control c, Visual to) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), to)!.Value;
    private static void SeedCatalog(AppModel model, params ModelOption[] options) => model.Providers.UpdateCatalog(new ValueList<ModelOption>(options), ProviderKind.Claude);

    [Fact]
    [Trait("Req", "PR-SETTINGS-183")] // ModelsSettingsPage.swift:20-22,66-71 the body re-evaluates when settings.modelList changes
    public void Your_models_follows_model_list_changes() => With(m => { m.Settings.AddToModelList(Pin("a")); m.Settings.AddToModelList(Pin("b")); }, "", (page, model, window) =>
    {
        Assert.Contains("2 of 15.", AllText(page), StringComparison.Ordinal);
        model.Settings.RemoveFromModelList(Pin("a")); Sync(window);
        Assert.Contains("1 of 15.", AllText(page), StringComparison.Ordinal);
        model.Settings.AddToModelList(Pin("c")); model.Settings.AddToModelList(Pin("d")); Sync(window);
        Assert.Contains("3 of 15.", AllText(page), StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-185")] // ModelsSettingsPage.swift:49-57,176-177 a pinned row re-reads registry.model when the catalogue arrives
    public void A_pinned_row_refreshes_when_its_catalogue_loads() => With(m => m.Settings.AddToModelList(Pin("raw-id")), "", (page, model, window) =>
    {
        Assert.Contains("raw-id", AllText(page), StringComparison.Ordinal);
        SeedCatalog(model, Option("raw-id", "Friendly Name")); Sync(window);
        Assert.DoesNotContain("raw-id", AllText(page), StringComparison.Ordinal);
        Assert.Contains("Friendly Name", AllText(Of<PinnedModelRow>(page).Single()), StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-271")] // ModelsSettingsPage.swift:315-324,423-436 collapsing the provider card closes its rows
    public void Collapsing_a_provider_card_closes_its_rows() => With(m => SeedCatalog(m, Option("a", "Alpha"), Option("b", "Beta")), "", (page, _, window) =>
    {
        var reveals = Of<ChromeRevealLayout>(ClaudeSection(page)).ToArray();
        Assert.NotEmpty(reveals);
        Assert.All(reveals.Where(r => Of<ChromeRow>(r).Any()), r => Assert.Equal(1, r.Fraction));
        var header = Of<Button>(ClaudeSection(page)).First(b => AutomationProperties.GetName(b) == "Claude");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Sync(window);
        Advance(window, 1);
        Assert.All(Of<ChromeRevealLayout>(ClaudeSection(page)).Where(r => Of<ChromeRow>(r).Any()), r => Assert.Equal(0, r.Fraction));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-192")] // ModelsSettingsPage.swift:346-395 stable ForEach identity: the activated button keeps keyboard focus
    public void Adding_a_catalogue_model_keeps_keyboard_focus_on_its_button() => With(m => SeedCatalog(m, Option("a", "Alpha"), Option("b", "Beta")), "", (page, _, window) =>
    {
        var section = ClaudeSection(page);
        Button Toggle() => Of<Button>(section).First(b => AutomationProperties.GetName(b) is "Add Alpha to the picker" or "Remove Alpha from the picker");
        var toggle = Toggle();
        toggle.Focus(); Sync(window);
        Assert.True(toggle.IsFocused);
        toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Sync(window);
        Assert.Same(toggle, Toggle());
        Assert.True(toggle.IsFocused);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-271")] // ModelsSettingsPage.swift:346-395 a nonmatching row is accessibilityHidden(true)
    public void Nonmatching_catalogue_rows_are_hidden_from_accessibility() => With(m => SeedCatalog(m, Option("a", "Alpha"), Option("b", "Beta")), "alpha", (page, _, _) =>
    {
        var section = ClaudeSection(page);
        var beta = Of<ChromeRow>(section).Single(r => AllText(r).Contains("Beta", StringComparison.Ordinal));
        var reveal = beta.GetLogicalAncestors().OfType<ChromeRevealLayout>().First();
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
        var alpha = Of<ChromeRow>(section).Single(r => AllText(r).Contains("Alpha", StringComparison.Ordinal));
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(alpha.GetLogicalAncestors().OfType<ChromeRevealLayout>().First()));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-186")] // ModelsSettingsPage.swift:47-61,112-129 the grabbed row follows the pointer at once, lifted and topmost
    public void The_grabbed_row_follows_the_pointer_before_any_reorder() => With(m => { m.Settings.AddToModelList(Pin("a")); m.Settings.AddToModelList(Pin("b")); m.Settings.AddToModelList(Pin("c")); }, "", (page, model, window) =>
    {
        var grip = Of<Border>(Of<PinnedModelRow>(page).First()).First(b => AutomationProperties.GetName(b) == "Drag to reorder");
        var start = Centre(grip, window);
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left); Sync(window);
        window.MouseMove(new Point(start.X, start.Y + 5)); Sync(window);
        var first = Of<PinnedModelRow>(page).First();
        Assert.Equal(Pin("a"), model.Settings.ModelList[0]);
        var offset = new[] { (Control)first, (Control)first.GetVisualParent()! }.Max(c => (c.RenderTransform as TranslateTransform)?.Y ?? 0);
        Assert.InRange(offset, 4, 6);
        Assert.Equal(1, new[] { (Control)first, (Control)first.GetVisualParent()! }.Max(c => c.ZIndex));
        // The gesture outlives the rebuild the first move caused: a later move still reaches it and drags the rebuilt row.
        window.MouseMove(new Point(start.X, start.Y + 12)); Sync(window);
        var later = Of<PinnedModelRow>(page).First();
        Assert.InRange(new[] { (Control)later, (Control)later.GetVisualParent()! }.Max(c => (c.RenderTransform as TranslateTransform)?.Y ?? 0), 11, 13);
        window.MouseUp(new Point(start.X, start.Y + 12), MouseButton.Left); Sync(window);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-188")] // ModelsSettingsPage.swift:9-10,47-59 the reported height is the slot: divider plus row
    public void A_pinned_rows_reported_height_includes_its_divider() => With(m => { m.Settings.AddToModelList(Pin("a")); m.Settings.AddToModelList(Pin("b")); }, "", (page, _, _) =>
    {
        var heights = (ValueMap<ModelPin, double>)typeof(ModelsSettingsPage).GetField("rowHeights", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var second = Of<PinnedModelRow>(page).ElementAt(1);
        Assert.Equal(second.Bounds.Height + 1, heights[Pin("b")], 0.5);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-185")] // ModelsSettingsPage.swift:173-217 HStack(spacing: 10) around the grip, label HStack(spacing: 12), Spacer(minLength: 8)
    public void A_pinned_rows_grip_icon_and_text_use_the_sources_spacing() => With(m => m.Settings.AddToModelList(Pin("a")), "", (page, _, window) =>
    {
        var row = Of<PinnedModelRow>(page).Single();
        var grip = Of<Border>(row).First(b => AutomationProperties.GetName(b) == "Drag to reorder");
        var icon = Of<ProviderIcon>(row).First();
        var title = Of<TextBlock>(row).First(t => t.Text == "a");
        double Left(Control c) => c.TranslatePoint(default, window)!.Value.X;
        double Right(Control c) => Left(c) + c.Bounds.Width;
        Assert.Equal(10, Left(icon.Parent is Border host ? host : icon) - Right(grip), 0.5);
        Assert.Equal(12, Left(title) - Right(icon.Parent is Border h2 ? h2 : icon), 0.5);
    });
    [Fact]
    [Trait("Req", "PR-SETTINGS-185")] // ModelsSettingsPage.swift:195-212 the label HStack(spacing: 12) puts its spacing on both sides of Spacer(minLength: 8): 32 points at least between the text and the hint
    public void A_pinned_rows_text_keeps_the_sources_minimum_gap_before_its_hint() => With(m => m.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, new string('x', 300))), "", (page, _, window) =>
    {
        var row = Of<PinnedModelRow>(page).Single();
        var title = Of<TextBlock>(row).First(t => t.Text!.StartsWith("xxxx", StringComparison.Ordinal));
        var hint = Of<Grid>(row).First().Children.Single(c => Grid.GetColumn(c) == 3);
        Assert.InRange(hint.TranslatePoint(default, window)!.Value.X - (title.TranslatePoint(default, window)!.Value.X + title.Bounds.Width), 31.5, 32.5);
    });
}
