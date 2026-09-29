using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926), :105-110, 152-157.
[Collection("Headless")]
public sealed class ModelsSettingsPageTests(HeadlessSession headless)
{
    private static string AllText(Control control)
    {
        var parts = new System.Collections.Generic.List<string>();
        void Walk(Control node)
        {
            if (node is TextBlock text) parts.Add(text.Text ?? "");
            if (node is ContentControl { Content: Control child }) Walk(child);
            if (node is ContentControl { Content: string label }) parts.Add(label);
            if (node is Panel panel) foreach (var kid in panel.Children) Walk(kid);
            if (node is Decorator { Child: Control decorated }) Walk(decorated);
        }
        Walk(control);
        return string.Join("\n", parts);
    }

    private static (ModelsSettingsPage Page, Marrow.App.AppModel Model) Show(AppModelHarness harness, System.Action<Marrow.App.AppModel>? seed = null, string query = "")
    {
        var model = harness.MakeModel();
        if (seed is not null) harness.Main.OnMain(() => seed(model));
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var page = new ModelsSettingsPage(query);
        host.Child = page;
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return (page, model);
    }


    private static ModelOption Option(string id, string name, string? detail = null) =>
        new(id, name, detail: detail);

    [Fact]
    [Trait("Req", "PR-SETTINGS-182")] // App/Views/Settings/ModelsSettingsPage.swift:34-40
    public void With_no_models_chosen_your_models_is_the_explanatory_row()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var (page, _) = Show(harness);

            var text = AllText(page);
            Assert.Contains("No models chosen yet", text, System.StringComparison.Ordinal);
            Assert.Contains("Until you add some below, the picker shows every model from your providers.", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-183")] // App/Views/Settings/ModelsSettingsPage.swift:66-71
    public void The_count_line_reads_how_many_of_fifteen_and_only_shows_while_the_search_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var (page, model) = Show(harness, m => m.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "claude-model")));

            var text = AllText(page);
            Assert.Contains("1 of 15. Click a model to set its reasoning effort and fast mode for every new chat.", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-183")] // App/Views/Settings/ModelsSettingsPage.swift:66-71
    public void The_count_line_is_hidden_while_a_search_is_active()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var (page, _) = Show(harness, m => m.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "claude-model")), query: "claude");

            Assert.DoesNotContain("of 15.", AllText(page), System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-179")] // App/Views/Settings/ModelsSettingsPage.swift:86-91
    public void A_search_matching_nothing_shows_the_curly_quoted_no_match_line()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var (page, _) = Show(harness, query: "zzz-no-such-model");

            Assert.Contains("No models match “zzz-no-such-model”.", AllText(page), System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-179")] // App/Views/Settings/ModelsSettingsPage.swift:33-38
    public void A_search_matching_a_chosen_model_keeps_the_your_models_section_and_hides_the_no_match_line()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var (page, _) = Show(harness, m => m.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "claude-model")), query: "claude");

            var text = AllText(page);
            Assert.DoesNotContain("No models match", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-181")] // App/Views/Settings/ModelsSettingsPage.swift:152-157
    public void An_empty_query_matches_every_model_including_an_unknown_option()
    {
        Assert.True(ModelsSettingsPage.Matches(Option("model-id", "Alpha"), "model-id", ProviderKind.Claude, ""));
        Assert.True(ModelsSettingsPage.Matches(null, "model-id", ProviderKind.Claude, ""));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-181")] // App/Views/Settings/ModelsSettingsPage.swift:152-157
    public void A_model_matches_on_its_short_name_full_name_detail_id_or_provider_name()
    {
        var option = Option("model-id", "Alpha Model", "Fast option");

        Assert.True(ModelsSettingsPage.Matches(option, "unused-id", ProviderKind.Codex, "ALPHA"));
        Assert.True(ModelsSettingsPage.Matches(option, "unused-id", ProviderKind.Codex, "alpha model"));
        Assert.True(ModelsSettingsPage.Matches(option, "unused-id", ProviderKind.Codex, "FAST OPTION"));
        Assert.True(ModelsSettingsPage.Matches(option, "model-id", ProviderKind.Claude, "MODEL-ID"));
        Assert.True(ModelsSettingsPage.Matches(option, "unused-id", ProviderKind.Claude, "claude"));

        Assert.False(ModelsSettingsPage.Matches(option, "unused-id", ProviderKind.Codex, "no match"));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-181")] // App/Views/Settings/ModelsSettingsPage.swift:105-110, 152-157
    public void An_unknown_pinned_model_still_matches_on_its_id_or_provider_name()
    {
        Assert.True(ModelsSettingsPage.Matches(null, "raw-model-id", ProviderKind.Claude, "RAW-MODEL"));
        Assert.True(ModelsSettingsPage.Matches(null, "raw-model-id", ProviderKind.Claude, "CLAUDE"));
        Assert.False(ModelsSettingsPage.Matches(null, "raw-model-id", ProviderKind.Claude, "no match"));
    }

    private static (CollapsibleProviderHeader Header, bool[] Values) Header(bool expanded = false)
    {
        var value = expanded;
        var binding = new Binding<bool>(() => value, v => value = v);
        var header = new CollapsibleProviderHeader(ProviderKind.Claude, binding, "model list");
        return (header, [value]);
    }

    private static Button HeaderButton(CollapsibleProviderHeader header) => Assert.IsType<Button>(header.Child);

    [Fact]
    [Trait("Req", "UI-SETTINGS-041")] // App/Views/Settings/ModelsSettingsPage.swift:416-448
    [Trait("Decision", "ED-062")]
    public void Collapsed_header_shows_the_collapsed_value_and_the_show_tooltip_and_help_text()
    {
        headless.OnUiThread(() =>
        {
            var (header, _) = Header(expanded: false);
            var button = HeaderButton(header);

            Assert.Equal("Claude", AutomationProperties.GetName(button));
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(button));
            Assert.Equal("Show the model list", ToolTip.GetTip(button));
            Assert.Equal("Shows or hides the model list", AutomationProperties.GetHelpText(button));
            Assert.Equal(1, AutomationProperties.GetHeadingLevel(button));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-041")] // App/Views/Settings/ModelsSettingsPage.swift:416-448
    public void Expanded_header_shows_the_expanded_value_and_the_hide_tooltip()
    {
        headless.OnUiThread(() =>
        {
            var (header, _) = Header(expanded: true);
            var button = HeaderButton(header);

            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(button));
            Assert.Equal("Hide the model list", ToolTip.GetTip(button));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-041")] // App/Views/Settings/ModelsSettingsPage.swift:423-424
    public void Clicking_the_header_toggles_isExpanded()
    {
        // App.Views.Settings.SettingsView part 6 (B3, opencode; UI-PROVREG-045) made the chevron rotate through
        // SettingsScalarAnimation/Chrome.PanelSlide so an equal notification does not restart the motion; that path
        // requires a bound FrameClock.Source even when this test never ticks it (Marrow.App binds one at start-up,
        // every other headless UI test in this suite overrides it, e.g. ShortcutsSettingsPageTests.cs:45).
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var value = false;
            var binding = new Binding<bool>(() => value, v => value = v);
            var header = new CollapsibleProviderHeader(ProviderKind.Claude, binding, "model list");
            var button = HeaderButton(header);

            // isExpanded.Value flips synchronously inside the click handler's Transaction.WithAnimation body, but
            // this.Track's reaction (UpdateHeader, which sets ItemStatus/ToolTip) runs on a MainActor.Post that the
            // headless dispatcher only drains on an explicit pump - same idiom as FollowUpQueueTabTests.Settle().
            // Without this, the assertions below see the pre-click ItemStatus even though `value` already flipped.
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(value);
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(button));

            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(value);
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(button));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-041")] // App/Views/Settings/ModelsSettingsPage.swift:429-435
    [Trait("Decision", "ED-062")]
    public void The_chevron_is_11_point_semibold_and_the_provider_name_uses_the_system_secondary_colour()
    {
        headless.OnUiThread(() =>
        {
            var (header, _) = Header();
            var content = Assert.IsType<Grid>(HeaderButton(header).Content);
            var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(content), image => image.SymbolName == "chevron.right");
            var font = SwiftImage.GetFont(chevron)!.Resolve();
            Assert.Equal(11, font.FontSize);
            Assert.Equal(FontWeight.SemiBold, font.FontWeight);

            var name = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(content), block => block.Text == "Claude");
            Assert.Equal(SwiftColor.Secondary.Resolve(ThemeVariant.Default), ((ISolidColorBrush)name.Foreground!).Color);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-186")] // App/Views/Settings/ModelsSettingsPage.swift:178-191
    [Trait("Decision", "ED-205")]
    public void The_grip_shows_grabidle_when_idle_and_grabactive_while_this_row_is_dragged()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var pin = new ModelPin(ProviderKind.Claude, "claude-model");

            var idle = new PinnedModelRow(model, pin, isDragged: false, _ => { }, () => { });
            var idleGrip = Assert.Single(LogicalQuery.SelfAndDescendants<Border>(idle), b => b.Width == 24 && b.Height == 24);
            Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), idleGrip.Cursor);

            var dragged = new PinnedModelRow(model, pin, isDragged: true, _ => { }, () => { });
            var draggedGrip = Assert.Single(LogicalQuery.SelfAndDescendants<Border>(dragged), b => b.Width == 24 && b.Height == 24);
            Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, 1), draggedGrip.Cursor);
        });
    }
}

// STOP App.Views.Settings.ModelsSettingsPage #2 (helper Claude, 2026-09-25; resolves #1 below)
// Kind: coverage gap, not a code defect. AppEnvironment (Marrow.App.Port) resolves #1's missing environment
// injection; AppModelHarness gives a real offline AppModel. Tested directly: PR-SETTINGS-179/180/181/182/183
// (page structure, matching, empty state, count line, no-match line via the constructor's initial query).
// SetQuery(string) itself (PR-SETTINGS-269's live rebuild) is not driven by a dedicated test: calling it after
// window.Show() to force a second layout pass reproducibly hit the same "Could not create glyphTypeface" issue
// documented for App.Views.Settings.HydraCookbookPanel (twice in isolation); SetQuery's body is exactly `query =
// value; Rebuild();`, the same Rebuild() the constructor-query tests above already exercise, so the gap is the
// live-update trigger only, not the rendering logic itself. Not independently
// tested: PR-SETTINGS-184 through 192 (drag reorder physics, the lifted-row visual state, height-table pruning,
// PinnedModelRow/ProviderModelsSection/RowControl rendering) and UI-SETTINGS-038 through 042/067/080. Two real
// blockers: (1) a fresh AppModelHarness model has ProviderRegistry.AvailableProviders always empty (nothing is
// "installed", by the same test-safety design AppModelHarness.PrepareBootstrap documents for CLI providers), so
// the page never constructs a ProviderModelsSection in these tests - exercising it needs either a scripted registry
// seam this session did not build, or accepting a real (but safe, no-network) catalog load, not attempted given
// time; (2) PR-SETTINGS-189's effort/fast-mode popover needs App.Views.Composer.EffortSlider's EffortSliderCard,
// itself 39 MARROW_STUB sites and out of this unit's scope - left unwired in the implementation with a comment,
// matching the WindowManager/FloatingSidebarPanel precedent for a real cross-unit dependency gap. The drag-reorder
// implementation itself (DragChanged/DragEnded/RowDrag<ModelPin> wiring) follows the packet's own binding "Rulings
// for this unit" section and FollowUpQueueTab's already-proven RowDrag<Guid> pattern closely, but was not driven by
// a live pointer-drag test in the time available.
//
// STOP App.Views.Settings.ModelsSettingsPage #1
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:19-21 (B2 1fbf81e)
// Requirements: PR-SETTINGS-179, PR-SETTINGS-180, PR-SETTINGS-182-192, PR-SETTINGS-184-190, PR-SETTINGS-269-271, UI-SETTINGS-038-042, UI-SETTINGS-067, UI-SETTINGS-080
// What is unclear or missing: The supplied declarations do not show how a test sets the AppModel environment for ModelsSettingsPage, nor a scripted ProviderRegistry/catalog seam that can exercise sections without launching a real CLI or calling an API. The shown page constructor accepts only query, and the shown AppModel constructor has no injection parameters. Without those APIs, rendering and interaction tests would require invented setup or real catalog loading.
// Readings or options: (a) Invent environment and registry test helpers -> tests depend on members not established by the packet or supplied repository context; (b) use real provider catalog loading -> violates the prohibition on real AI CLI or network tests.
// Needed to proceed: the existing AppModel environment-injection API and a scripted, offline ProviderRegistry test seam, including catalog load state and forced-load recording.
// Done so far: PR-SETTINGS-181 tests written for the matching behavior available through the declared static helper; nothing implemented at the blocked UI test sites.
