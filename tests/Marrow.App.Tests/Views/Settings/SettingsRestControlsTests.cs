using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestControlsTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-052")] // App/Views/Settings/SettingsView.swift:818-834
    public void Commit_editor_preserves_each_real_keystroke_and_placeholder_type_geometry_and_hit_pass_through()
    {
        With(() => new SourceControlSettingsPage(), (view, model, h, w) =>
        {
            var editor = Assert.Single(Nodes<TextBox>(view)); Assert.Equal(90, editor.MinHeight); Assert.Equal(13, editor.FontSize); Assert.True(editor.AcceptsReturn); Assert.Equal(TextWrapping.Wrap, editor.TextWrapping);
            var pad = editor.GetVisualAncestors().OfType<Border>().First(b => b.Padding == new Thickness(12)); Assert.Equal(new Thickness(12), pad.Padding);
            var placeholder = Nodes<TextBlock>(view).Single(t => t.Text == "For example: use conventional commit prefixes"); Font(placeholder, 13); Ink(placeholder, placeholder.Foreground, Chrome.SecondaryText); Assert.Equal(new Thickness(5, 0), placeholder.Margin); Assert.False(placeholder.IsHitTestVisible); Assert.True(placeholder.IsVisible);
            var panel = Assert.IsType<Panel>(pad.Child); Assert.Same(editor, panel.Children[0]); Assert.Same(placeholder, panel.Children[1]);
            Click(w, editor, new Point(8, 8)); Assert.True(editor.IsFocused); w.KeyTextInput("first"); Drain(w); Assert.Equal("first", model.Settings.CommitInstructions); Assert.Equal("first", h.Defaults.String("commitInstructions")); Assert.False(placeholder.IsVisible);
            w.KeyTextInput("\nsecond"); Drain(w); Assert.Equal("first\nsecond", model.Settings.CommitInstructions); var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single(); Assert.Equal(TextWrapping.Wrap, presenter.TextWrapping); Assert.Equal("first\nsecond", presenter.Text); Assert.Null(editor.Background);
            editor.Text = ""; Drain(w); Assert.True(placeholder.IsVisible); Assert.Equal("", model.Settings.CommitInstructions);
        });
    }

    [Fact]
    [Trait("Req", "UI-STORE-001")] // App/Views/Settings/SettingsView.swift:836-845; Chrome.swift:1055-1087
    [Trait("Req", "UI-STORE-002")] // App/Views/Settings/SettingsView.swift:838-840
    [Trait("Req", "UI-SETTINGS-069")] // App/Views/Settings/SettingsView.swift:838-840
    public void Worktree_card_has_exact_resolved_path_row_order_and_unconditional_real_Reveal_action()
    {
        With(() => new SourceControlSettingsPage(), (view, _, h, w) =>
        {
            var section = Nodes<ChromeSection>(view).Single(s => Text(s).First() == "Worktrees and pull requests"); var row = Row(section, "Worktrees");
            var path = System.IO.Path.Combine(h.Home, ".nth", "worktrees"); Assert.Equal(path, Storage.WorktreesDirectory); CheckRow(row, "Worktrees", path);
            Assert.Equal(new[] { "Worktrees", "Pull requests" }, Nodes<ChromeRow>(section).Select(r => Text(r).First())); var divider = Assert.Single(Nodes<ChromeRowDivider>(section)); Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin);
            var button = Assert.Single(Nodes<Button>(row)); Assert.Equal("Reveal", Text(button).Single()); Assert.True(button.IsEffectivelyEnabled); Assert.Single(button.GetVisualDescendants().OfType<Border>(), b => b.Height == 22 && SwiftGlass.GetEffect(b) is not null); /* the accent glass pill is the button itself (ED-280) */
            string? opened = null; using var platform = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend { Reveal = p => opened = p }); Click(w, button); Assert.Equal(path, opened);
            Click(w, Assert.IsType<Grid>(Assert.IsType<Border>(row.Child).Child).Children[0]); Assert.Equal(path, opened);
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-019")] // App/Views/Settings/SettingsView.swift:968-983; Core/Support/Text.swift:4-8
    public void About_header_uses_both_metadata_and_missing_key_fallbacks_with_exact_size_type_and_order()
    {
        foreach (var pair in new[] { (Info: AppInfo.Read(new Dictionary<string, object?> { ["CFBundleShortVersionString"] = "1.7.0", ["CFBundleVersion"] = "23" }, false), Text: "Version 1.7.0 (23)"), (Info: AppInfo.Read(new Dictionary<string, object?>(), false), Text: "Version 1.0.0 (1)") })
        {
            using var info = AppInfo.Running.Override(pair.Info);
            With(() => new AboutSettingsPage(), (view, _, _, _) =>
            {
                var card = Assert.IsType<ChromeCard>(Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child).Children[1]); var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]);
                var header = Assert.IsType<StackPanel>(rows.Children[0]); Assert.Equal(14, header.Spacing); Assert.Equal(new Thickness(16), header.Margin); Assert.Equal(Avalonia.Layout.Orientation.Horizontal, header.Orientation); Assert.Equal(2, header.Children.Count);
                var icon = Assert.IsType<Image>(header.Children[0]); Assert.Equal(56, icon.Width); Assert.Equal(56, icon.Height); Assert.NotNull(icon.Source);
                var labels = Assert.IsType<StackPanel>(header.Children[1]); Assert.Equal(3, labels.Spacing); var name = Assert.IsType<TextBlock>(labels.Children[0]); Assert.Equal("Nth", name.Text); Font(name, 15, FontWeight.SemiBold);
                var version = Assert.IsType<TextBlock>(labels.Children[1]); Assert.Equal(pair.Text, version.Text); Font(version, 12); Ink(version, version.Foreground, Chrome.SecondaryText);
            });
        }
    }

    [Fact]
    [Trait("Decision", "ED-266")] // SettingsView.swift:985-987 and :1019-1022: both About "Show" buttons are `.buttonStyle(.glass).controlSize(.small)` under the window's accent tint
    public void Both_About_Show_buttons_are_the_small_accent_glass_capsule_with_their_labels_and_clicks_unchanged()
    {
        With(() => new AboutSettingsPage(), (view, model, _, w) =>
        {
            var welcome = Row(Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child).Children[1], "Welcome tour");
            var licenses = Assert.Single(Nodes<LicensesRow>(view));
            foreach (var host in new Control[] { welcome, licenses })
            {
                var button = Assert.Single(Nodes<Button>(host));
                var label = Assert.Single(Nodes<TextBlock>(button));
                Assert.Equal("Show", label.Text); // the label is unchanged
                Assert.Equal(SmallAccentGlassButton.Height, button.Bounds.Height); // 20 pt, controlSize(.small)
                Assert.Equal(SmallAccentGlassButton.LabelSize, label.FontSize); // 11 pt
                Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // white on the accent
                var effect = Assert.Single(host.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).OfType<GlassEffect>(), e => e.Shape.Kind == GlassShapeKind.Capsule);
                Assert.True(effect.IsInteractive);
                Assert.Equal(1.0, effect.TintOpacity); // the accent at full strength
                Assert.Equal(Chrome.Accent.Resolve(label.ActualThemeVariant), effect.TintColor!.Resolve(label.ActualThemeVariant));
            }
            // The click of the first is unchanged: it replays the tour (the Licenses click is LicensesSelectionTests and LicensesShippingTests).
            Click(w, Assert.Single(Nodes<Button>(welcome)));
            Assert.True(Field<Avalonia.Controls.Window>(Marrow.App.Views.Tour.TourWindowController.Shared, "window").IsVisible);
            Marrow.App.Views.Tour.TourWindowController.Shared.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-070")] // App/Views/Settings/SettingsView.swift:985-987
    [Trait("Req", "UI-UPDATE-020")] // App/Views/Settings/SettingsView.swift:984-989
    public void Welcome_tour_is_always_enabled_replays_with_a_real_click_and_refocuses_the_same_window()
    {
        With(() => new AboutSettingsPage(), (view, model, _, w) =>
        {
            var card = Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child).Children[1]; var row = Row(card, "Welcome tour"); CheckRow(row, "Welcome tour", "The slideshow from the first launch: Hydra, the effort slider, panels and themes.");
            var button = Assert.Single(Nodes<Button>(row)); Assert.Equal("Show", Text(button).Single()); Assert.Equal(SmallAccentGlassButton.Height, button.Bounds.Height); Assert.Single(Nodes<Border>(row), b => !b.IsHitTestVisible && SwiftGlass.GetEffect(b) is not null); /* SmallAccentGlassButton (ED-266): the accent material is the pill's layer under the button */
            try
            {
                foreach (var seen in new[] { false, true }) { model.Settings.HasSeenTour = seen; Drain(w); Assert.True(button.IsEffectivelyEnabled); Click(w, button); var tour = Field<Avalonia.Controls.Window>(Marrow.App.Views.Tour.TourWindowController.Shared, "window"); Assert.True(tour.IsVisible); Assert.Equal(8, Marrow.App.Views.Tour.Tour.Pages.Count); Click(w, button); Assert.Same(tour, Field<Avalonia.Controls.Window>(Marrow.App.Views.Tour.TourWindowController.Shared, "window")); Marrow.App.Views.Tour.TourWindowController.Shared.Close(); Drain(w); }
            }
            finally { Marrow.App.Views.Tour.TourWindowController.Shared.Close(); }
        });
    }
}
