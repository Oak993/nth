using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Sound;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:296-305 (SettingsSwitch), :307-413 (GeneralSettingsPage), :1125-1155 (ProjectMarksRow);
// UI/Chrome/Chrome.swift:871-932 (GlassPickerButton); UI/Chrome/ChromeVisualPicker.swift:1-106; Core/Models/Provider.swift:161-213.
[Collection("Headless")]
public sealed class SettingsViewFinishGeneralTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static bool IsOn(Control toggle) => ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(toggle)!).ToggleState == ToggleState.On;

    [Fact]
    [Trait("Req", "UI-SETTINGS-012")] // App/Views/Settings/SettingsView.swift:296-305,401-409,444-452; App/AppSettings.swift:119-170
    public void Settings_switch_has_no_label_toggles_its_setting_by_click_and_space_follows_the_accent_and_dims_with_its_disabled_row()
    {
        var sound = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(sound);
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings;
            // Every General row that carries a boolean (SettingsView.swift:336-402): title, the setting it binds, its UserDefaults key (AppSettings.swift Key enum).
            var rows = new (string Title, Func<bool> Read, string Key)[]
            {
                ("Show thinking", () => s.ShowReasoning, "showReasoning"), ("Concise AI replies", () => s.AntiSlopEnabled, "antiSlopEnabled"), ("Recent downloads", () => s.RecentDownloadsPicker, "recentDownloadsPicker"),
                ("Notify when a turn finishes", () => s.NotifyWhenFinished, "notifyWhenFinished"), ("Chime when a turn finishes", () => s.ChimeWhenFinished, "chimeWhenFinished"), ("Confirm before deleting threads", () => s.ConfirmBeforeDeleting, "confirmBeforeDeleting"),
                ("Continue after a usage limit", () => s.AutoContinueAfterLimit, "autoContinueAfterLimit"), ("Show a usage panel", () => s.ShowsUsagePanel, "showsUsagePanel"), ("Projects", () => s.ProjectsEnabled, "projectsEnabled"), ("Sound when settling", () => s.SettleSound, "settleSound"),
            };
            foreach (var (title, read, key) in rows)
            {
                var row = Row(view, title); var wrapper = Assert.Single(Nodes<SettingsSwitch>(row)); var toggle = Assert.Single(Nodes<SmallSwitch>(row));
                // :300-301 labelsHidden: the control contributes no label and no width beyond the switch itself.
                Assert.Empty(Text(wrapper)); Assert.Equal(toggle.Bounds.Width, wrapper.Bounds.Width, 3); Assert.Equal(toggle.Bounds.Height, wrapper.Bounds.Height, 3); Assert.True(toggle.Focusable);
                var before = read(); Assert.Equal(before, IsOn(toggle));
                Click(w, toggle); Assert.Equal(!before, read()); Assert.Equal(!before, IsOn(toggle)); Assert.Equal(!before, h.Defaults.Object(key)); // click toggles, and the change is written under the source key
                Click(w, toggle); Assert.Equal(before, read()); Assert.Equal(before, IsOn(toggle)); Assert.Equal(before, h.Defaults.Object(key));
                Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.Equal(!before, read()); Assert.Equal(!before, h.Defaults.Object(key)); Press(w, Key.Space); Assert.Equal(before, read()); // Space toggles when focused
            }
            // :408-409 the settle sound row is disabled and halved while the finish action is not Settle: neither a click nor Space reaches the setting.
            s.ThreadFinishAction = ThreadFinishAction.Archive; Drain(w); var settle = Row(view, "Sound when settling"); var gated = Assert.Single(Nodes<SmallSwitch>(settle)); var was = s.SettleSound;
            Assert.False(settle.IsEnabled); Assert.Equal(0.5, settle.Opacity, 6); Click(w, gated); Assert.Equal(was, s.SettleSound); Assert.True(gated.Focus() || !gated.IsEffectivelyEnabled); Press(w, Key.Space); Assert.Equal(was, s.SettleSound);
            foreach (var other in rows.Where(r => r.Title != "Sound when settling")) { var live = Row(view, other.Title); Assert.True(live.IsEnabled); Assert.Equal(1, live.Opacity, 6); }
            s.ThreadFinishAction = ThreadFinishAction.Settle; Drain(w); Assert.True(settle.IsEnabled); Assert.Equal(1, settle.Opacity, 6); Click(w, gated); Assert.Equal(!was, s.SettleSound);
            // :296-305 the system switch takes the window's tint, Chrome.accent (the theme's accent, or the system accent): the on track is drawn in it and an off track is not.
            try { var thinking = Assert.Single(Nodes<SmallSwitch>(Row(view, "Show thinking"))); s.ShowReasoning = true; Drain(w); Tick(h, 300); var system = Chrome.Accent.Resolve(thinking.ActualThemeVariant); // the track crossfades over 0.25 s (ED-280), so the colours are read once it has settled
            Assert.True(Count(thinking, system) > 0); s.ShowReasoning = false; Drain(w); Tick(h, 300); Assert.Equal(0, Count(thinking, system)); s.ShowReasoning = true; s.Theme = AppTheme.Nord; Drain(w); Tick(h, 300);
            var nord = Chrome.Accent.Resolve(thinking.ActualThemeVariant); Assert.Equal(SwiftColor.Hex("88C0D0").Resolve(thinking.ActualThemeVariant), nord); Assert.True(Count(thinking, nord) > 0); Assert.Equal(0, Count(thinking, system)); } finally { s.Theme = AppTheme.System; }
        }, height: 2600);
    }

    private static int Count(Control control, Color color)
    {
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height)); using (var dc = bitmap.CreateDrawingContext()) control.Render(dc);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        return pixels.Pixels.Count(p => p.Red == color.R && p.Green == color.G && p.Blue == color.B && p.Alpha == color.A);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-013")] // App/Views/Settings/SettingsView.swift:323-325; UI/Chrome/Chrome.swift:871-932; Core/Models/Provider.swift:161-195; App/AppSettings.swift:710
    public void Permissions_picker_is_a_fitted_28pt_glass_capsule_whose_popover_lists_the_four_modes_with_symbols_and_a_check_and_drives_the_row_detail()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var modes = new (RuntimeMode Mode, string Title, string Symbol, string Summary, string Raw)[]
            {
                (RuntimeMode.Supervised, "Supervised", "hand.raised", "Asks before commands and file changes", "supervised"), (RuntimeMode.AutoAcceptEdits, "Auto-accept edits", "pencil.line", "Edits files freely, asks before other actions", "autoAcceptEdits"),
                (RuntimeMode.Auto, "Auto", "wand.and.sparkles", "The provider's reviewer approves routine actions", "auto"), (RuntimeMode.FullAccess, "Full access", "lock.open", "Runs commands and edits without asking", "fullAccess"),
            };
            var row = Row(view, "Permissions"); var picker = Assert.Single(Nodes<GlassPickerButton<RuntimeMode>>(row)); var button = Assert.Single(Nodes<Button>(picker));
            // AppSettings.swift:710 a fresh install starts on Full access; :323 the row detail is the mode's summary.
            Assert.Equal(RuntimeMode.FullAccess, model.Settings.DefaultRuntimeMode); CheckRow(row, "Permissions", modes[3].Summary);
            // Chrome.swift:889-911: 12pt either side, 6pt between symbol, title and chevron, content height 28 with no vertical capsule padding (so 28, not 32), sized to its content.
            var symbol = Assert.Single(Nodes<SwiftImage>(picker), i => i.SymbolName == "lock.open"); var title = Assert.Single(Nodes<TextBlock>(picker)); var chevron = Assert.Single(Nodes<SwiftImage>(picker), i => i.SymbolName == "chevron.down");
            Assert.Equal(28, picker.Bounds.Height, 3); Assert.Equal(12, TopLeft(symbol, picker).X, 3); Assert.Equal(6, TopLeft(title, picker).X - (TopLeft(symbol, picker).X + symbol.Bounds.Width), 3); Assert.Equal(6, TopLeft(chevron, picker).X - (TopLeft(title, picker).X + title.Bounds.Width), 3); Assert.Equal(picker.Bounds.Width - 12, TopLeft(chevron, picker).X + chevron.Bounds.Width, 3);
            Assert.Equal("Full access", title.Text); Font(title, 12.5, FontWeight.Medium); Assert.Equal(1, title.MaxLines); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), SwiftImage.GetFont(symbol)); Ink(symbol, symbol.Foreground, Chrome.SecondaryText);
            Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(chevron)); Ink(chevron, chevron.Foreground, Chrome.SecondaryText); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
            var effect = Glass(picker); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.Equal(Chrome.GlassTint, effect.TintColor); Assert.Equal(0.3, effect.TintOpacity); Assert.True(effect.IsInteractive);
            // Hovering lifts the label to full opacity over Chrome.hover (easeOut 0.1); the symbol and chevron stay secondary.
            var width = picker.Bounds.Width; Hover(w, button); Tick(h, 50); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92 + 0.08 * Animation.EaseOut(0.1).Progress(0.05))); Tick(h, 51); Ink(title, title.Foreground, Chrome.PrimaryText); Ink(symbol, symbol.Foreground, Chrome.SecondaryText); Ink(chevron, chevron.Foreground, Chrome.SecondaryText); Assert.Equal(width, picker.Bounds.Width, 3);
            // Open raises the label like hovering does: opened from the keyboard with the pointer away, it is full opacity while the popover shows (isHovering || isPresented, Chrome.swift:909).
            w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92)); Assert.True(button.Focus()); Press(w, Key.Space); var popup = Assert.Single(Nodes<Popup>(picker)); Assert.True(popup.IsOpen); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText);
            // The popover opens below, listing the four modes in order with their symbols and a checkmark on the current one only.
            Assert.Equal(PlacementMode.Bottom, popup.Placement); var menu = popup.Child!;
            var items = Nodes<PopoverItem>(menu); Assert.Equal(modes.Select(m => m.Title), items.Select(i => Text(i).First()));
            Assert.Equal(modes.Select(m => m.Symbol), items.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName != "checkmark").SymbolName));
            Assert.Equal(new[] { 0.0, 0.0, 0.0, 1.0 }, items.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName == "checkmark").Opacity));
            // Open keeps the label at full opacity even with the pointer away.
            w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Assert.True(popup.IsOpen); Ink(title, title.Foreground, Chrome.PrimaryText);
            foreach (var next in new[] { modes[0], modes[1], modes[2], modes[3] })
            {
                Click(w, Assert.Single(Nodes<Button>(Nodes<PopoverItem>(menu).Single(i => Text(i).First() == next.Title)))); Tick(h, 150);
                // The row is rebuilt around the new selection (SettingsView.swift:323 reads the summary in body), so read it afresh.
                row = Row(view, "Permissions"); picker = Assert.Single(Nodes<GlassPickerButton<RuntimeMode>>(row)); button = Assert.Single(Nodes<Button>(picker)); popup = Assert.Single(Nodes<Popup>(picker)); title = Assert.Single(Nodes<TextBlock>(picker));
                Assert.False(popup.IsOpen); Assert.Equal(next.Mode, model.Settings.DefaultRuntimeMode); Assert.Equal(next.Raw, h.Defaults.Object("defaultRuntimeMode")); CheckRow(row, "Permissions", next.Summary);
                Assert.Equal(next.Title, title.Text); Assert.Single(Nodes<SwiftImage>(picker), i => i.SymbolName == next.Symbol); Assert.Equal(28, picker.Bounds.Height, 3);
                Click(w, button); Assert.True(popup.IsOpen); menu = popup.Child!; Assert.Equal(modes.Select(m => m.Mode == next.Mode ? 1.0 : 0.0), Nodes<PopoverItem>(menu).Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName == "checkmark").Opacity));
            }
            w.MouseDown(new Point(2, 2), MouseButton.Left); w.MouseUp(new Point(2, 2), MouseButton.Left); Drain(w); Assert.False(popup.IsOpen); Tick(h, 150); Ink(title, title.Foreground, Chrome.PrimaryText.Opacity(0.92));
        }, nativePopup: true, height: 1100);
    }

    private void CheckTiles(Control row, Avalonia.Controls.Window w, AppModelHarness h, string[] names, string[] symbols, Action<int> picked, Func<int> current)
    {
        var tiles = Nodes<ChromeVisualTile>(row); Assert.Equal(names, tiles.Select(AutomationProperties.GetName)); Assert.Equal(0, current());
        // ChromeVisualPicker.swift:37-49: one row of tiles 12 apart, the whole picker a no-window-drag region.
        var picker = Assert.Single(Nodes<StackPanel>(row), p => p.Orientation == Avalonia.Layout.Orientation.Horizontal && p.Spacing == 12); Assert.Single(Nodes<NoWindowDragArea>(row)); Assert.Equal(names.Length, picker.Children.Count);
        void Check(int selected, int hovered)
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i]; var on = i == selected; var stage = Assert.Single(Nodes<Border>(tile), b => b.Width == 76 && b.Height == 48); var ring = Assert.Single(Nodes<SwiftShapeBorder>(tile), b => b.BorderThickness == new Thickness(2)); var caption = Assert.Single(Nodes<TextBlock>(tile));
                // :86-102 the stage is 76 x 48 with 6pt corners; .padding(ringInset 3) makes the tile 82 x 54, the ring (radius 6 + 3) drawn 2pt wide in that padding; the caption 6pt below.
                Assert.Equal(82, tile.Bounds.Width, 3); Assert.Equal(new Point(3, 3), TopLeft(stage, tile)); Assert.Equal(new CornerRadius(6), stage.CornerRadius); Assert.Equal(new Point(0, 0), TopLeft(ring, tile)); Assert.Equal(82, ring.Bounds.Width, 3); Assert.Equal(54, ring.Bounds.Height, 3); Assert.Equal(new CornerRadius(9), ring.CornerRadius);
                Assert.Equal(54 + 6, TopLeft(caption, tile).Y, 3); Assert.Equal(3, TopLeft(caption, tile).X, 3); Assert.Equal(76, caption.Bounds.Width, 3);
                if (i > 0) Assert.Equal(82 + 12, TopLeft(tile, picker).X - TopLeft(tiles[i - 1], picker).X, 3);
                // :76-84 the caption .subheadline, centred, at most two lines, primary when chosen and secondary otherwise; :88-89 the symbol .title2 medium, accent when chosen.
                Font(caption, 11); Assert.Equal(TextAlignment.Center, caption.TextAlignment); Assert.Equal(2, caption.MaxLines); Assert.Equal(names[i], caption.Text); Ink(caption, caption.Foreground, on ? Chrome.PrimaryText : Chrome.SecondaryText);
                var image = Assert.Single(Nodes<SwiftImage>(tile)); Assert.Equal(symbols[i], image.SymbolName); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium), SwiftImage.GetFont(image)); Ink(image, image.Foreground, on ? Chrome.Accent : Chrome.SecondaryText);
                // :93 fill overlay 0.10 hovering or chosen, else 0.06; :101 the ring is accent when chosen and clear (the accent at no opacity: nothing drawn) otherwise; :72-73 the label and the selected trait.
                Ink(stage, stage.Background, Chrome.Overlay(on || i == hovered ? 0.10 : 0.06)); Ink(ring, ring.BorderBrush, on ? Chrome.Accent : Chrome.Accent.Opacity(0)); // the unselected stroke is the accent at no opacity, so that it can fade in
                Assert.Equal(on, ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(tile)!).IsSelected); Assert.Equal(on, tile.IsSelected);
            }
        }
        Check(0, -1);
        Hover(w, Assert.Single(Nodes<Button>(tiles[1]))); Tick(h, 150); Check(0, 1); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Check(0, -1);
        Click(w, Assert.Single(Nodes<Button>(tiles[1]))); picked(1); Tick(h, 150); Assert.Equal(1, current()); Check(1, 1); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Check(1, -1);
        Click(w, Assert.Single(Nodes<Button>(tiles[0]))); Tick(h, 150); Assert.Equal(0, current()); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Check(0, -1);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-014")] // App/Views/Settings/SettingsView.swift:327-331; UI/Chrome/ChromeVisualPicker.swift:10-105; Core/Models/Provider.swift:197-213; App/AppSettings.swift:711
    public void Workspace_tiles_are_local_and_new_worktree_with_the_visual_picker_metrics_and_select_by_click()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var row = Row(view, "Workspace"); CheckRow(row, "Workspace", "Where a new thread makes its changes"); Assert.Equal(WorkspaceMode.Local, model.Settings.DefaultWorkspaceMode); // AppSettings.swift:711 default .local
            CheckTiles(row, w, h, ["Local", "New worktree"], ["folder", "arrow.triangle.branch"], _ => Assert.Equal(WorkspaceMode.Worktree, model.Settings.DefaultWorkspaceMode), () => (int)model.Settings.DefaultWorkspaceMode);
            Assert.Equal("local", h.Defaults.Object("defaultWorkspaceMode")); // the second click set it back
            Click(w, Assert.Single(Nodes<Button>(Nodes<ChromeVisualTile>(row)[1]))); Assert.Equal("worktree", h.Defaults.Object("defaultWorkspaceMode"));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-015")] // App/Views/Settings/SettingsView.swift:344-348; UI/Chrome/ChromeVisualPicker.swift:10-105; App/AppSettings.swift:721
    public void Working_line_tiles_are_card_then_line_with_card_selected_by_default_and_the_same_tile_metrics()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var row = Row(view, "Working line"); CheckRow(row, "Working line", "A card with the turn's progress bar, or a plain line"); Assert.True(model.Settings.ShowsWorkingCard); // AppSettings.swift:721 the stored value defaults to true: Card
            // The options are (true, "Card") then (false, "Line"): the first tile is the stored true, so index 0 is "card" and selecting the second stores false.
            CheckTiles(row, w, h, ["Card", "Line"], ["rectangle.fill", "minus"], _ => Assert.False(model.Settings.ShowsWorkingCard), () => model.Settings.ShowsWorkingCard ? 0 : 1);
            Assert.Equal(true, h.Defaults.Object("showsWorkingCard")); Click(w, Assert.Single(Nodes<Button>(Nodes<ChromeVisualTile>(row)[1]))); Assert.False(model.Settings.ShowsWorkingCard); Assert.Equal(false, h.Defaults.Object("showsWorkingCard"));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-014")] // App/Views/Settings/SettingsView.swift:327-331; UI/Chrome/ChromeVisualPicker.swift:36-48,69-70,80,89,93,97 (selection and hover inside withAnimation(Chrome.hover)); the reference runtime (settings.choice-tile-selection S 21.628, 27.282, 27.878, 45.343; hover.choice-tile-fill; press.row-tile-dim S 21.543)
    [Trait("Req", "UI-SETTINGS-015")] // App/Views/Settings/SettingsView.swift:344-348; the same tile
    public void Tiles_crossfade_ring_symbol_caption_and_fill_over_chrome_hover_on_selection_and_hover_and_dim_while_pressed()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            foreach (var title in new[] { "Workspace", "Working line" })
            {
                var row = Row(view, title); var tiles = Nodes<ChromeVisualTile>(row); Assert.Equal(2, tiles.Length); Click(w, Assert.Single(Nodes<Button>(tiles[0]))); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); // the first tile chosen, the pointer away, everything settled
                var parts = tiles.Select(t => (Stage: Assert.Single(Nodes<Border>(t), b => b.Width == 76 && b.Height == 48), Ring: Assert.Single(Nodes<SwiftShapeBorder>(t), b => b.BorderThickness == new Thickness(2)) /* the continuous ring (ED-181) */, Caption: Assert.Single(Nodes<TextBlock>(t)), Symbol: Assert.Single(Nodes<SwiftImage>(t)), Content: Assert.Single(Nodes<StackPanel>(t)))).ToArray();
                Color Of(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color; Color Resolved(SwiftColor c) => c.Resolve(tiles[0].ActualThemeVariant);
                var stageTo = Resolved(Chrome.Overlay(0.10)); var stageFrom = Resolved(Chrome.Overlay(0.06)); var accent = Resolved(Chrome.Accent); var none = Resolved(Chrome.Accent.Opacity(0)); var primary = Resolved(Chrome.PrimaryText); var secondary = Resolved(Chrome.SecondaryText);
                Assert.Equal(accent, Of(parts[0].Ring.BorderBrush)); Assert.Equal(none, Of(parts[1].Ring.BorderBrush));

                // :40-41 a click selects inside withAnimation(Chrome.hover): both tiles move on the one clock, part-way after 40 ms and settled after the 0.1 s.
                Click(w, Assert.Single(Nodes<Button>(tiles[1]))); Tick(h, 40);
                Assert.NotEqual(none, Of(parts[1].Ring.BorderBrush)); Assert.NotEqual(accent, Of(parts[1].Ring.BorderBrush)); Assert.NotEqual(accent, Of(parts[0].Ring.BorderBrush)); Assert.NotEqual(none, Of(parts[0].Ring.BorderBrush));
                Assert.NotEqual(secondary, Of(parts[1].Symbol.Foreground)); Assert.NotEqual(accent, Of(parts[1].Symbol.Foreground)); Assert.NotEqual(secondary, Of(parts[1].Caption.Foreground)); Assert.NotEqual(primary, Of(parts[1].Caption.Foreground));
                Assert.NotEqual(accent, Of(parts[0].Symbol.Foreground)); Assert.NotEqual(primary, Of(parts[0].Caption.Foreground)); Assert.NotEqual(stageFrom, Of(parts[1].Stage.Background)); Assert.NotEqual(stageTo, Of(parts[1].Stage.Background));
                Tick(h, 101);
                Assert.Equal(none, Of(parts[0].Ring.BorderBrush)); Assert.Equal(accent, Of(parts[1].Ring.BorderBrush)); Assert.Equal(secondary, Of(parts[0].Symbol.Foreground)); Assert.Equal(accent, Of(parts[1].Symbol.Foreground));
                Assert.Equal(secondary, Of(parts[0].Caption.Foreground)); Assert.Equal(primary, Of(parts[1].Caption.Foreground)); Assert.Equal(stageTo, Of(parts[1].Stage.Background));

                // :69-70 hover lightens the unselected tile's fill over the same curve, and the pointer leaving takes it back.
                Hover(w, Assert.Single(Nodes<Button>(tiles[0]))); Tick(h, 40); Assert.NotEqual(stageFrom, Of(parts[0].Stage.Background)); Assert.NotEqual(stageTo, Of(parts[0].Stage.Background)); Tick(h, 101); Assert.Equal(stageTo, Of(parts[0].Stage.Background));
                // press.row-tile-dim: held down, the tile's content is at 0.8 and its fill at x0.7 of its hovered level; letting go restores both and selects the tile.
                var at = tiles[0].TranslatePoint(new Point(20, 20), w)!.Value; w.MouseMove(at); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 51);
                Assert.Equal(0.8, parts[0].Content.Opacity, 3); Assert.Equal(1, parts[1].Content.Opacity); Assert.Equal(Resolved(Chrome.Overlay(0.10).Opacity(0.7)), Of(parts[0].Stage.Background));
                w.MouseUp(at, MouseButton.Left); Drain(w); Tick(h, 151); Assert.Equal(1, parts[0].Content.Opacity, 3); Assert.Equal(accent, Of(parts[0].Ring.BorderBrush)); Assert.Equal(stageTo, Of(parts[0].Stage.Background)); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-016")] // App/Views/Settings/SettingsView.swift:384-390,1125-1155
    public void Marks_edit_button_is_disabled_without_projects_and_opens_a_400_wide_popover_below_it_of_one_row_per_project()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var row = Row(view, "Marks"); CheckRow(row, "Marks", "An emoji or symbol for each project, drawn before the thread's name in the activity list and on the project's own row");
            var marks = Assert.Single(Nodes<ProjectMarksRow>(row)); var edit = Assert.Single(Nodes<Button>(marks)); Assert.Equal("Edit", Text(edit).Single());
            // :1134-1135 disabled while there are no projects; the verbatim help text. Glass style (:1132): regular interactive capsule.
            Assert.False(edit.IsEnabled); Assert.Equal("Choose a mark for each project", ToolTip.GetTip(edit)); var effect = SwiftGlass.GetEffect(Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(edit).OfType<Control>(), c => SwiftGlass.GetEffect(c) is not null))!; Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); /* the small accent glass pill (ED-280) */ Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.True(effect.IsInteractive);
            var popup = Assert.Single(Nodes<Popup>(marks)); Click(w, edit); Assert.False(popup.IsOpen);
            var one = model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("One").Replace('\\', '/')))!; var two = model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("Two").Replace('\\', '/')))!; Drain(w); Assert.True(edit.IsEnabled);
            Assert.Equal(2, model.Projects.Count); Click(w, edit); Assert.True(popup.IsOpen); Assert.Equal(PlacementMode.Bottom, popup.Placement); Assert.Same(edit, popup.PlacementTarget);
            // :1137-1145 a VStack of ChromeRows (project name, path) with a divider between them, exactly 400 wide and as tall as its rows.
            var list = Assert.IsType<StackPanel>(popup.Child); Assert.Equal(400, list.Width); var rows = Nodes<ChromeRow>(list); Assert.Equal(new[] { one.Name, two.Name }, rows.Select(r => Text(r).First())); Assert.Equal(new[] { one.Path, two.Path }, rows.Select(r => Text(r)[1]));
            Assert.Single(Nodes<ChromeRowDivider>(list)); Assert.Equal(2, Nodes<ProjectIconButton>(list).Length); Assert.Equal(400, list.Bounds.Width, 3); Assert.Equal(list.Children.Sum(c => c.Bounds.Height), list.Bounds.Height, 3); Assert.Equal(list.Bounds.Height, rows.Sum(r => r.Bounds.Height) + Nodes<ChromeRowDivider>(list).Sum(d => d.Bounds.Height), 3);
            foreach (var r in rows) CheckRow(r, Text(r).First(), Text(r)[1]);
            w.MouseDown(new Point(2, 2), MouseButton.Left); w.MouseUp(new Point(2, 2), MouseButton.Left); Drain(w); Assert.False(popup.IsOpen);
            // The button follows the project list: removing every project disables it again (:1134).
            model.RemoveProject(one); model.RemoveProject(two); Drain(w); Assert.False(edit.IsEnabled);
        }, nativePopup: true, height: 2600);
    }
}
