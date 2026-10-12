using Marrow.App.Port.Settings;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView blitz, records of the second half of the unit: the proofs the earlier tests did not carry.
// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792). Every test mounts the production control; literals quote the Swift.
[Collection("Headless")]
public sealed class SettingsViewBlitzBTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-068")] // SettingsView.swift:808-816; AppSettings.swift:3-19; Provider.swift:60
    public void Text_generation_marks_resolve_to_the_catalogue_assets_and_the_popover_lists_four_options_with_a_check_on_the_current_one()
    {
        With(() => new SourceControlSettingsPage(), (view, model, _, w) =>
        {
            var picker = Assert.Single(Nodes<GlassPickerButton<TextGenerationChoice>>(view));
            // :813 `ProviderKind(rawValue: $0.rawValue)?.iconName`: "claude" and "codex" are provider kinds, "automatic" and "off" are not.
            foreach (var (choice, asset) in new (TextGenerationChoice, string?)[] { (TextGenerationChoice.Automatic, null), (TextGenerationChoice.Claude, "provider-claude"), (TextGenerationChoice.Codex, "provider-codex"), (TextGenerationChoice.Off, null) })
            {
                model.Settings.TextGeneration = choice; Drain(w);
                var marks = Nodes<SwiftImage>(picker).Where(image => image.AssetName is not null).ToArray();
                if (asset is null) { Assert.Empty(marks); continue; }
                var mark = Assert.Single(marks); Assert.Equal(asset, mark.AssetName);
                Assert.False(mark.IsEmpty, asset + " is not in the asset catalogue: the mark draws nothing"); // the name must find a drawing, not just be spelled
                Assert.Equal(14, Assert.IsType<Border>(mark.Parent).Width); Assert.Equal(14, Assert.IsType<Border>(mark.Parent).Height); // :894 .frame(width: 14, height: 14)
            }
            var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
            model.Settings.TextGeneration = TextGenerationChoice.Codex; Drain(w);
            Click(w, Assert.Single(Nodes<Button>(picker))); Drain(w);
            var menu = Opened(presenter.Calls.Last(x => x.Presented).Popup);
            var items = Nodes<PopoverItem>(menu); Assert.Equal(new[] { "Automatic", "Claude", "Codex", "Off" }, items.Select(item => Text(item).First()));
            foreach (var item in items) Assert.Equal(Text(item).First() == "Codex" ? 1 : 0, Nodes<SwiftImage>(item).Single(image => image.SymbolName == "checkmark").Opacity);
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-024")] // SettingsView.swift:997-1002; OD-0012 F1, H; OD-0072 (the licence line's terminal clause)
    public void Licence_paragraph_is_wrapped_12_point_secondary_text_with_the_source_margins_under_a_divider_and_ends_the_app_card()
    {
        With(() => new AboutSettingsPage(), (view, _, _, _) =>
        {
            var root = Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child);
            var card = Assert.IsType<ChromeCard>(root.Children[1]); var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]);
            var licence = Assert.IsType<TextBlock>(rows.Children[^1]); // the last entry of the app card
            Assert.Equal("nth is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is nth's own, built on Windows ConPTY.", licence.Text); // OD-0072 for the clause after the semicolon
            Font(licence, 12); // .system(.callout) = 12 point (:1001)
            Ink(licence, licence.Foreground, Chrome.SecondaryText); // Chrome.secondaryText (:1002)
            Assert.Equal(new Thickness(16, 11, 10, 11), licence.Margin); // .padding(.leading, 16) .padding(.trailing, 10) .padding(.vertical, 11)
            Assert.Equal(TextWrapping.Wrap, licence.TextWrapping);
            Assert.IsType<ChromeRowDivider>(rows.Children[^2]); // :996 the divider above it
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-033")] // SettingsView.swift:15,30,47,62,77,148-164; AboutUpdateViews.swift:728-732
    public void About_row_selects_the_page_and_runs_one_update_check_and_the_search_matches_version_and_license_but_not_licence()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            using var running = UpdateChecker.OverrideShared(new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], "")); // a released build (the harness's own is a development build, which never checks)
            var checker = UpdateChecker.Shared; Assert.Null(checker.LastError);
            var sidebar = Field<Control>(view, "sidebar"); var field = Assert.Single(Nodes<TextBox>(sidebar));
            var about = Nodes<SidebarRow>(view).Single(row => Text(row).Contains("About"));
            Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Nodes<SidebarRow>(view).Length); // CZ-4: Customization is listed
            Reveal(w, h, about); Click(w, about); Drain(w);
            Assert.Single(Nodes<AboutSettingsPage>(view)); Assert.Empty(Nodes<GeneralSettingsPage>(view)); // the click selects the page
            for (var i = 0; i < 50 && checker.LastError is null; i++) Drain(w);
            Assert.Equal("No update feed is set.", checker.LastError); // ... and the page's section checked for updates (the feed is empty, so no request is made)
            // :77 keywords are the American "license". "version" is also a keyword of the Updates page (SA-1, ED-251), listed since UE-4.
            foreach (var (query, expectedTitles) in new[] { ("version", new[] { "Updates", "About" }), ("license", new[] { "About" }), ("VERSION", new[] { "Updates", "About" }), ("licence", Array.Empty<string>()) })
            {
                field.Text = query; Drain(w);
                var shown = Nodes<SidebarRow>(view).Where(row => Text(row).Length > 0 && Text(row)[0] != "No results").ToArray();
                Assert.Equal(expectedTitles.Length, shown.Length); Assert.Equal(expectedTitles, shown.Select(row => Text(row).Single()));
                if (expectedTitles.Length == 0) { Assert.Contains("No results", Text(view)); Assert.Contains("Try a setting or a provider name.", Text(view)); }
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-THEME-012")] // SettingsView.swift:524,543-560
    public void Transparency_reset_hits_its_whole_22_point_square_with_no_background_in_any_state_and_a_click_animates_back_over_snappy_0_2()
    {
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            var control = Assert.Single(Nodes<BackdropOpacitySlider>(view)); var reset = Assert.Single(Nodes<Button>(control));
            Assert.Equal(22, reset.Width); Assert.Equal(22, reset.Height); Assert.Equal("Back to the stock look", ToolTip.GetTip(reset));
            Assert.False(reset.IsVisible); // within 0.005 of 0.5 the reset is not there
            model.Settings.BackdropOpacity = 0.8; Tick(h, 1); Tick(h, 250); Drain(w); Assert.True(reset.IsVisible); Assert.Equal(1, reset.Opacity);
            // contentShape(.rect): the glyph's painted pixels are a small part of the 22 x 22 slot; every corner of the slot is the button.
            foreach (var corner in new[] { new Point(1, 1), new Point(21, 1), new Point(1, 21), new Point(21, 21), new Point(11, 11) })
            {
                var hit = w.InputHitTest(At(reset, w, corner)) as Visual; Assert.NotNull(hit); Assert.True(hit == reset || reset.IsVisualAncestorOf(hit), "the point " + corner + " of the reset is not the reset: " + hit);
            }
            // plain button style, no background in any state: the slot's corners read the same at rest, with the pointer over it and while pressed.
            (int, int)[] spots = [((int)At(reset, w, new Point(1.5, 1.5)).X, (int)At(reset, w, new Point(1.5, 1.5)).Y), ((int)At(reset, w, new Point(20.5, 1.5)).X, (int)At(reset, w, new Point(20.5, 1.5)).Y), ((int)At(reset, w, new Point(1.5, 20.5)).X, (int)At(reset, w, new Point(1.5, 20.5)).Y), ((int)At(reset, w, new Point(20.5, 20.5)).X, (int)At(reset, w, new Point(20.5, 20.5)).Y)];
            var rest = FramePixels.Levels(w, spots);
            w.MouseMove(At(reset, w, new Point(1.5, 1.5))); Drain(w); Assert.True(reset.IsPointerOver); var hover = FramePixels.Levels(w, spots);
            w.MouseDown(At(reset, w, new Point(1.5, 1.5)), MouseButton.Left); Drain(w); var pressed = FramePixels.Levels(w, spots);
            Assert.Equal(rest, hover); Assert.Equal(rest, pressed);
            foreach (var border in reset.GetVisualDescendants().OfType<Border>()) Assert.True(border.Background is null || border.Background == Brushes.Transparent, "a background drawn on the reset: " + border.Background);
            Assert.True(reset.Background is null || reset.Background == Brushes.Transparent);
            w.MouseUp(At(reset, w, new Point(1.5, 1.5)), MouseButton.Left); Drain(w);
            // the click is the reset: back to 0.5 (a click at the slot's far corner does it too), animated on the frame clock over 0.2 s.
            var slider = Assert.Single(Nodes<Slider>(control)); Assert.Equal(0.5, model.Settings.BackdropOpacity); Assert.InRange(slider.Value, 0.5, 0.8);
            Tick(h, 100); Assert.InRange(slider.Value, 0.501, 0.799); Assert.Equal(0.5, model.Settings.BackdropOpacity); Tick(h, 400); Assert.Equal(0.5, slider.Value, 6);
            model.Settings.BackdropOpacity = 0.2; Tick(h, 250); Drain(w); Assert.True(reset.IsVisible);
            Click(w, reset, new Point(20.5, 20.5)); Assert.Equal(0.5, model.Settings.BackdropOpacity);
        });
    }

    [Fact]
    [Trait("Req", "UI-THEME-010")] // SettingsView.swift:528-531 (and :535-538 for the Solid glyph beside it)
    public void The_end_glyphs_show_their_tips_over_their_whole_frame_not_only_where_the_ring_was_painted()
    {
        With(() => new BackdropOpacitySlider(Binding<double>.Constant(0.5)), (view, _, _, w) =>
        {
            var glyphs = Nodes<SwiftImage>(view).Where(image => image.SymbolName != "arrow.counterclockwise").ToArray(); Assert.Equal(new[] { "circle.dotted", "circle.fill" }, glyphs.Select(image => image.SymbolName));
            var clear = glyphs[0]; var frame = Assert.IsType<Border>(clear.Parent);
            Assert.Equal("Clear", ToolTip.GetTip(frame)); Assert.Equal("Solid", ToolTip.GetTip(Assert.IsType<Border>(glyphs[1].Parent)));
            // an unfilled ring paints nothing at its centre; SwiftUI still hits the image's frame there (the pointer is over the glyph, so the tip shows)
            Assert.Equal(clear.Bounds.Size, frame.Bounds.Size); Assert.True(frame.Bounds.Width > 6);
            foreach (var spot in new[] { new Point(frame.Bounds.Width / 2, frame.Bounds.Height / 2), new Point(1, 1), new Point(frame.Bounds.Width - 1, frame.Bounds.Height - 1) })
            {
                var hit = w.InputHitTest(At(frame, w, spot)) as Visual; Assert.NotNull(hit); Assert.True(hit == frame || frame.IsVisualAncestorOf(hit), "the point " + spot + " of the Clear glyph's frame is not the glyph: " + hit);
            }
            Hover(w, frame); Assert.True(frame.IsPointerOver);
            Assert.Equal(SwiftFont.System(11), SwiftImage.GetFont(clear)); Ink(clear, clear.Foreground, Chrome.SecondaryText); // static: .subheadline, secondary text
            var row = Assert.IsType<StackPanel>(frame.Parent); Assert.Equal(8, row.Spacing); Assert.Same(frame, row.Children[0]); Assert.IsType<Slider>(row.Children[1]); // leading the slider, 8 points away
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-079")] // SettingsView.swift:460-517 (.contentShape(.rect) at :478, :494, :506); AppZoom.swift:6-15
    public void The_three_zoom_buttons_hit_their_whole_22_point_square_with_a_real_pointer_and_step_the_levels()
    {
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            var zoom = Row(view, "Zoom"); var buttons = Nodes<Button>(zoom);
            var smaller = buttons.Single(x => Equals(ToolTip.GetTip(x), "Zoom out")); var larger = buttons.Single(x => Equals(ToolTip.GetTip(x), "Zoom in")); var reset = buttons.Single(x => Equals(ToolTip.GetTip(x), "Back to the stock size"));
            Assert.Equal(new Control[] { smaller, larger, reset }.Select(b => b.Bounds.Size), new[] { new Size(22, 22), new Size(22, 22), new Size(22, 22) });
            var grid = Assert.IsType<StackPanel>(smaller.Parent); Assert.Equal(8, grid.Spacing); Assert.Equal(new Control[] { smaller, Nodes<TextBlock>(zoom).Single(t => t.MinWidth == 46), larger, reset }, grid.Children);
            // every corner of each 22 x 22 slot belongs to its button, over the glyph or beside it
            foreach (var button in new[] { smaller, larger })
                foreach (var corner in new[] { new Point(1, 1), new Point(21, 1), new Point(1, 21), new Point(21, 21) })
                {
                    var hit = w.InputHitTest(At(button, w, corner)) as Visual; Assert.NotNull(hit); Assert.True(hit == button || button.IsVisualAncestorOf(hit), "the point " + corner + " of " + ToolTip.GetTip(button) + " is not the button: " + hit);
                }
            Click(w, larger, new Point(1.5, 1.5)); Assert.Equal(110, model.Settings.AppZoom);
            Click(w, larger, new Point(20.5, 20.5)); Assert.Equal(125, model.Settings.AppZoom);
            Drain(w); Tick(h, 400); Drain(w); Assert.True(reset.IsEnabled);
            Click(w, smaller, new Point(20.5, 1.5)); Assert.Equal(110, model.Settings.AppZoom);
            Click(w, smaller, new Point(1.5, 20.5)); Assert.Equal(100, model.Settings.AppZoom);
            Drain(w); Tick(h, 400); Drain(w); Assert.False(reset.IsEnabled); Assert.Equal(0, reset.Opacity);
            Click(w, reset, new Point(11, 11)); Assert.Equal(100, model.Settings.AppZoom); // at 100 the reset is disabled and invisible: it does nothing
            model.Settings.AppZoom = 150; Drain(w); Tick(h, 400); Drain(w); Assert.True(reset.IsEnabled); Assert.Equal(1, reset.Opacity);
            Click(w, reset, new Point(1.5, 20.5)); Assert.Equal(100, model.Settings.AppZoom);
            model.Settings.AppZoom = 75; Drain(w); Tick(h, 400); Drain(w); Assert.False(smaller.IsEnabled); Click(w, smaller, new Point(11, 11)); Assert.Equal(75, model.Settings.AppZoom);
            model.Settings.AppZoom = 200; Drain(w); Tick(h, 400); Drain(w); Assert.False(larger.IsEnabled); Click(w, larger, new Point(11, 11)); Assert.Equal(200, model.Settings.AppZoom);
        }, height: 3600); // WL-2 (ED-NEW-wltwo-1): the wallpaper library above the zoom card makes the unscrolled page taller than 1600
    }

    [Fact]
    [Trait("Req", "UI-THEME-009")] // SettingsView.swift:532-534; AppSettings.swift:293-299
    public void A_press_on_the_bare_track_jumps_the_knob_there_without_glass_and_the_arrow_keys_step_it_by_a_small_amount()
    {
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            var control = Assert.Single(Nodes<BackdropOpacitySlider>(view)); var slider = Assert.Single(Nodes<Slider>(control)); var knob = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            Assert.Equal(140, slider.Width); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum); Assert.Equal(0.5, model.Settings.BackdropOpacity);
            // the filled side is the window's accent, the rest is the track
            var fill = Assert.Single(slider.GetVisualDescendants().OfType<Border>().Where(b => b.Height == 4 && b.Background is ISolidColorBrush brush && brush.Color == Chrome.Accent.Resolve(b.ActualThemeVariant)).Take(1)); Assert.NotNull(fill);
            var travel = 140.0 - 18; var at = new Point(9 + 0.8 * travel, slider.Bounds.Height / 2);
            var press = At(slider, w, at); w.MouseMove(press); w.MouseDown(press, MouseButton.Left); Drain(w);
            Assert.Equal(new Size(18, 14), knob.Bounds.Size); // OD-0043: a press on the track shows no glass (the knob has not been grabbed)
            w.MouseUp(press, MouseButton.Left); Drain(w);
            Assert.InRange(slider.Value, 0.8 - 1.0 / travel, 0.8 + 1.0 / travel); Assert.Equal(slider.Value, model.Settings.BackdropOpacity, 6); Assert.Equal(new Size(18, 14), knob.Bounds.Size);
            // arrow keys where the framework supports them: a small step, never the whole range
            slider.Focus(); Drain(w); var before = slider.Value;
            w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); w.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); Drain(w);
            Assert.True(slider.Value < before && before - slider.Value <= 0.1, "an arrow key moved the slider from " + before + " to " + slider.Value); Assert.Equal(slider.Value, model.Settings.BackdropOpacity, 6);
        });
    }

    private static (SwiftSound Sound, float Volume) Direct(Action play)
    {
        var recorder = new SettingsViewSoundRecorder(); using (SoundOutput.Current.Override(recorder)) play();
        return Assert.Single(recorder.Plays);
    }

    [Fact]
    [Trait("Req", "UI-THEME-026")] // SettingsView.swift:358-364; AppSettings.swift:122,713
    public void Chime_row_plays_the_finish_chime_once_at_its_own_volume_when_switched_on_and_is_silent_when_switched_off()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var finish = Direct(Marrow.UI.Sound.FinishChime.Play); var settle = Direct(Marrow.UI.Sound.SettleChime.Play);
            Assert.Equal(0.35f, finish.Volume); Assert.Equal(0.18f, settle.Volume); Assert.NotSame(finish.Sound, settle.Sound); // the two chimes are told apart
            var recorder = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(recorder);
            var row = Row(view, "Chime when a turn finishes"); CheckRow(row, "Chime when a turn finishes", "A soft chime, whether or not the thread is in view");
            Assert.True(model.Settings.ChimeWhenFinished); Assert.True(row.IsEffectivelyEnabled); // on by default
            var toggle = Assert.Single(Nodes<SmallSwitch>(row));
            Click(w, toggle); Assert.False(model.Settings.ChimeWhenFinished); Assert.Empty(recorder.Plays); Assert.False(h.Defaults.Bool("chimeWhenFinished")); // off is silent, under the key chimeWhenFinished
            Click(w, toggle); Assert.True(model.Settings.ChimeWhenFinished); Assert.True(h.Defaults.Bool("chimeWhenFinished"));
            var play = Assert.Single(recorder.Plays); Assert.Same(finish.Sound, play.Sound); Assert.Equal(0.35f, play.Volume); // on plays the finish chime, once, as the preview
            model.Settings.ChimeWhenFinished = false; Drain(w); Assert.Single(recorder.Plays); model.Settings.ChimeWhenFinished = true; Drain(w); // a change from elsewhere is heard the same
            Assert.Equal(2, recorder.Plays.Count); Assert.Same(finish.Sound, recorder.Plays[1].Sound); Assert.Equal(0.35f, recorder.Plays[1].Volume);
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Drain(w); Tick(h, 300); Assert.True(row.IsEffectivelyEnabled); Assert.Equal(1, row.Opacity); // no disabled state
        });
    }

    [Fact]
    [Trait("Req", "UI-THEME-027")] // SettingsView.swift:401-409; AppSettings.swift:126,717; OD-0076
    public void Settle_row_plays_the_settle_chime_on_enable_and_dims_over_Chrome_hover_when_the_finish_action_becomes_Archive_leaving_the_stored_value()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var finish = Direct(Marrow.UI.Sound.FinishChime.Play); var settleDirect = Direct(Marrow.UI.Sound.SettleChime.Play);
            var recorder = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(recorder);
            var row = Row(view, "Sound when settling"); CheckRow(row, "Sound when settling", "A small note as the thread settles");
            Assert.True(model.Settings.SettleSound); Assert.True(row.IsEnabled); Assert.Equal(1, row.Opacity); // on by default
            var toggle = Assert.Single(Nodes<SmallSwitch>(row)); var rows = Assert.IsType<StackPanel>(row.Parent); Assert.IsType<ChromeRowDivider>(rows.Children[rows.Children.IndexOf(row) - 1]); // below a divider under the finish-action picker
            Click(w, toggle); Assert.False(model.Settings.SettleSound); Assert.Empty(recorder.Plays); Assert.False(h.Defaults.Bool("settleSound"));
            Click(w, toggle); Assert.True(model.Settings.SettleSound);
            var play = Assert.Single(recorder.Plays); Assert.Same(settleDirect.Sound, play.Sound); Assert.NotSame(finish.Sound, play.Sound); Assert.Equal(0.18f, play.Volume);
            // the tile's click runs in Chrome.hover: the row is disabled at once and eases to 0.5 over that animation
            var tiles = Nodes<ChromeVisualTile>(Row(view, "Done with a thread")); var archive = tiles.Single(tile => AutomationProperties.GetName(tile) == "Archive"); var settleTile = tiles.Single(tile => AutomationProperties.GetName(tile) == "Settle");
            Click(w, archive); Assert.Equal(ThreadFinishAction.Archive, model.Settings.ThreadFinishAction); Assert.False(row.IsEnabled); Assert.False(toggle.IsEffectivelyEnabled);
            Tick(h, 50); Assert.InRange(row.Opacity, 0.501, 0.999); Tick(h, 60); Assert.Equal(0.5, row.Opacity);
            Assert.True(model.Settings.SettleSound); Assert.Single(recorder.Plays); // the stored value is left alone and nothing was played
            Click(w, toggle); Assert.True(model.Settings.SettleSound); Assert.Single(recorder.Plays); // the disabled switch does nothing
            Click(w, settleTile); Assert.True(row.IsEnabled); Tick(h, 50); Assert.InRange(row.Opacity, 0.501, 0.999); Tick(h, 60); Assert.Equal(1, row.Opacity); Assert.Single(recorder.Plays);
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-044")] // SettingsView.swift:374-379; AppSettings.swift:635-637,774
    public void The_usage_panel_switch_is_last_in_Conversation_off_by_default_writes_at_once_and_follows_writes_from_elsewhere()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var row = Row(view, "Show a usage panel"); var toggle = Assert.Single(Nodes<SmallSwitch>(row));
            var rows = Assert.IsType<StackPanel>(row.Parent); Assert.Same(row, rows.Children[^1]); Assert.IsType<ChromeRowDivider>(rows.Children[^2]); Assert.Equal("Continue after a usage limit", Text(rows.Children[^3]).First());
            var peer = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            Assert.False(model.Settings.ShowsUsagePanel); Assert.Equal(ToggleState.Off, peer.ToggleState); // default off on a fresh install
            Click(w, toggle); Assert.True(model.Settings.ShowsUsagePanel); Assert.True(h.Defaults.Bool("showsUsagePanel")); Assert.Equal(ToggleState.On, peer.ToggleState); // written immediately
            // the usage popover's pop-out button writes ShowsUsagePanel = true (ComposerView.cs, proved by ComposerViewTests), the panel's close button writes false (ChatView.cs): the row follows
            model.Settings.ShowsUsagePanel = false; Drain(w); Assert.Equal(ToggleState.Off, peer.ToggleState);
            Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.ShowsUsagePanel = true); Drain(w); Assert.Equal(ToggleState.On, peer.ToggleState);
            Click(w, toggle); Assert.False(model.Settings.ShowsUsagePanel); Assert.False(h.Defaults.Bool("showsUsagePanel"));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-066")] // SettingsView.swift:668-671; AppSettings.swift:882-889
    public void Provider_switch_sits_at_the_trailing_edge_of_the_first_row_in_every_provider_with_the_display_name_and_clears_duplicates_when_switched()
    {
        With(() => new ProvidersSettingsPage(), (view, model, h, w) =>
        {
            var sections = Nodes<ProviderSettingsSection>(view); Assert.Equal(SwiftEnum<ProviderKind>.AllCases.Count, sections.Length); // one per provider, installed or not
            foreach (var section in sections)
            {
                var provider = Field<ProviderKind>(section, "provider"); var toggle = Assert.Single(Nodes<SmallSwitch>(section));
                var statusRow = Assert.IsType<Grid>(toggle.Parent!.Parent); Assert.Equal(2, Grid.GetColumn((Control)toggle.Parent)); Assert.Same(statusRow, Nodes<Grid>(section).First(g => g.ColumnDefinitions.Count == 3)); // the provider's first row, its trailing cell
                Assert.Equal(provider.DisplayName, AutomationProperties.GetName(toggle)); // its accessible name is the section header's
            }
            var codex = Nodes<ProviderSettingsSection>(view).Single(x => Field<ProviderKind>(x, "provider") == ProviderKind.Codex);
            // SetEnabled removes every entry for the provider and appends one only when switching off
            var toggleCodex = Assert.Single(Nodes<SmallSwitch>(codex)); var header = Assert.Single(Nodes<CollapsibleProviderHeader>(codex)); Click(w, Assert.Single(Nodes<Button>(header))); Drain(w); Tick(h, 400); Drain(w);
            Assert.Equal(ToggleState.Off, ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(toggleCodex)).ToggleState);
            Click(w, toggleCodex); Assert.Equal(new[] { "claude", "claude", "grok" }, model.Settings.DisabledProviders); // switching on: every "codex" entry gone, the others kept
            Click(w, toggleCodex); Assert.Equal(new[] { "claude", "claude", "grok", "codex" }, model.Settings.DisabledProviders); // switching off: one appended
        }, before: h => h.Defaults.Set("disabledProviders", new List<object> { "claude", "codex", "claude", "codex", "grok" }));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-060, UI-UPDATE-031 and UI-UPDATE-032 (the popover's contents, its last place in the Credits card, its dismissal, the unreadable-file sentence); the geometry, selection and shipped texts are asserted by Licences_are_read_once_and_the_three_markdown_blocks_use_fixed_popover_geometry and The_three_licence_documents_ship_beside_the_executable_upstream_first_and_the_popover_shows_them
    [Trait("Req", "UI-SETTINGS-060")] // SettingsView.swift:1007-1057
    [Trait("Req", "UI-UPDATE-031")] // SettingsView.swift:1018-1023
    [Trait("Req", "UI-UPDATE-032")] // SettingsView.swift:1012-1056
    public void Licences_row_closes_the_credits_card_and_its_popover_titles_bodies_and_dismissal_are_the_source_s()
    {
        With(() => new AboutSettingsPage(), (view, model, h, w) =>
        {
            var row = Assert.Single(Nodes<LicensesRow>(view)); var chromeRow = Assert.Single(Nodes<ChromeRow>(row));
            CheckRow(chromeRow, "Licenses", "AGPL-3.0 with attribution terms, and the notices for what the app builds on."); // title 13, detail 11, the exact words
            var credits = Assert.IsType<StackPanel>(row.Parent); Assert.Same(row, credits.Children[^1]); // the last entry of the Credits card
            Assert.IsType<ChromeRowDivider>(credits.Children[^2]); Assert.Equal("Welcome tour", Text(Assert.IsType<ChromeRow>(credits.Children[^3])).First()); Assert.Equal(9, credits.Children.Count); // Droppy's row, the three attribution rows and the licences row, with their dividers
            var show = Assert.Single(Nodes<Button>(row)); Assert.True(show.IsEnabled); Assert.Equal("Show", Text(show).Single()); // always enabled
            var popup = Assert.Single(Nodes<Popup>(row)); Assert.False(popup.IsOpen);
            Click(w, show); Assert.True(popup.IsOpen); Assert.Equal(PlacementMode.Bottom, popup.Placement); Assert.Same(show.Parent, popup.PlacementTarget); // arrowEdge .bottom; the anchor is the SmallAccentGlassButton pill, its material and its button (ED-266)
            var content = PopoverHost.Content(popup)!; var scroll = Assert.Single(Nodes<ScrollViewer>(content), s => s.Width == 480); Assert.Equal(440, scroll.Height);
            var blocks = Assert.Single(Nodes<StackPanel>(content), x => x.Spacing == 18 && x.Children.Count == 3);
            foreach (var (block, title) in blocks.Children.Zip(new[] { "nth", "Third-party notices", "Trademarks" }))
            {
                var inner = Assert.IsType<StackPanel>(block); Assert.Equal(8, inner.Spacing); var heading = Assert.IsType<TextBlock>(inner.Children[0]); Assert.Equal(title, heading.Text); Font(heading, 13, FontWeight.SemiBold);
                var markdown = Assert.IsType<Marrow.UI.Markdown.MarkdownView>(inner.Children[1]); Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown)); Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownBlockSelection(markdown));
                // the bodies take Chrome.secondaryText: the markdown's inherited text colour and the colour the prose actually draws in
                Ink(markdown, Avalonia.Controls.Documents.TextElement.GetForeground(markdown), Chrome.SecondaryText);
                var prose = Nodes<TextBlock>(markdown).Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.Inlines is not { Count: > 0 }).ToArray();
                if (prose.Length > 0) Assert.Contains(prose, t => t.Foreground is ISolidColorBrush brush && brush.Color == Chrome.SecondaryText.Resolve(t.ActualThemeVariant));
            }
            // click outside: dismisses (light dismiss on a real popup)
            w.MouseMove(new Point(3, 3)); w.MouseDown(new Point(3, 3), MouseButton.Left); w.MouseUp(new Point(3, 3), MouseButton.Left); Drain(w); Assert.False(popup.IsOpen);
        }, nativePopup: true);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-060")] // SettingsView.swift:1046-1056: `try? String(contentsOf: url, encoding: .utf8)` takes the sentence for a file it cannot read
    [Trait("Rule", "R-TEST-4")] // the unreadable-file clause of UI-SETTINGS-060 and UI-UPDATE-032; the other clauses are asserted by the tests above
    public void A_licence_file_that_is_not_valid_UTF_8_shows_the_sentence_and_the_texts_are_read_once()
    {
        var bad = System.IO.Path.Combine(AppContext.BaseDirectory, "blitz-bad-utf8.md");
        System.IO.File.WriteAllBytes(bad, [0x23, 0x20, 0x52, 0xFF, 0xFE, 0x20, 0x62, 0x61, 0x64]);
        try
        {
            var read = typeof(LicensesRow).GetMethod("Read", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, [typeof(string)])!; // Read(resource): beside the executable (the other overload is the folder seam)
            Assert.Equal("The blitz-bad-utf8 file is missing from this copy of the app.", read.Invoke(null, ["blitz-bad-utf8"]));
            var good = System.IO.Path.Combine(AppContext.BaseDirectory, "blitz-good-utf8.md"); System.IO.File.WriteAllText(good, "# Fine é中");
            try { Assert.Equal("# Fine é中", read.Invoke(null, ["blitz-good-utf8"])); } finally { System.IO.File.Delete(good); }
        }
        finally { System.IO.File.Delete(bad); }
        // read once: the cache holds one string per document and answers every later call with it
        var text = typeof(LicensesRow).GetMethod("Text", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var resource in new[] { "LICENSE", "THIRD_PARTY_NOTICES", "TRADEMARK" }) Assert.Same(text.Invoke(null, [resource]), text.Invoke(null, [resource]));
    }

    private static (Border Surface, TextBlock Label, GlassEffect Effect) Pill(Button button)
    {
        var surface = Assert.Single(button.GetVisualDescendants().OfType<Border>(), border => SwiftGlass.GetEffect(border) is not null);
        return (surface, Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()), SwiftGlass.GetEffect(surface)!);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-016: the pill's look (the small accent glass of ED-280) and its dim; no reference frame shows a disabled glass button (UNVERIFIED dim), and the hover lift and press growth of the catalogue are not drawn
    [Trait("Req", "UI-SETTINGS-016")] // SettingsView.swift:1125-1155
    public void Marks_edit_is_the_small_accent_glass_pill_with_a_white_11_point_label_dimmed_while_it_is_disabled()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var marks = Assert.Single(Nodes<ProjectMarksRow>(view)); var edit = Assert.Single(Nodes<Button>(marks)); var (surface, label, effect) = Pill(edit);
            Assert.Equal(20, surface.Bounds.Height, 3); Assert.Equal(SwiftGlass.GlassButtonSmallHeight, surface.Height); Assert.Equal(new Thickness(10.5, 0), surface.Padding); // .controlSize(.small): 20 high, 10.5 either side (S 41.4: about 40 x 20)
            Assert.Equal("Edit", label.Text); Assert.Equal(11, label.FontSize); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); Assert.InRange(surface.Bounds.Width - (label.Bounds.Width + 21), -1.0, 1.0);
            Assert.Equal(Chrome.Accent, effect.TintColor); Assert.Equal(1, effect.TintOpacity); Assert.True(effect.IsInteractive); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
            Assert.Equal(Chrome.Accent.Resolve(surface.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color); // an accent capsule, not the neutral one
            Assert.False(edit.IsEnabled); Tick(h, 200); Assert.Equal(0.4, surface.Opacity); // no projects: disabled, and it reads as disabled
            model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("One").Replace('\\', '/'))); Drain(w); Assert.True(edit.IsEnabled); Tick(h, 50); Assert.InRange(surface.Opacity, 0.401, 0.999); Tick(h, 100); Assert.Equal(1, surface.Opacity);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-051: the pills' look and the link texts; the gates, the 16 point label clamp and the browser and terminal callbacks are asserted by Every_CLI_install_and_login_row_has_literal_text_glass_gates_and_real_terminal_callback and Provider_login_calls_the_workspace_boundary_with_the_literal_command; the hover lift and press growth of the catalogue are not drawn
    [Trait("Req", "UI-SETTINGS-051")] // SettingsView.swift:719-757; Components.swift:117-133; OD-0072
    public void Install_get_key_and_login_pills_are_the_regular_accent_glass_pill_with_the_texts_of_the_source()
    {
        foreach (var (provider, rowTitle, text) in new[] { (ProviderKind.Codex, "Install", "Get Codex"), (ProviderKind.Meta, "Get a key", "Get a Meta key"), (ProviderKind.OpenRouter, "Get a key", "Get an OpenRouter key"), (ProviderKind.Commandcode, "Sign in", "Login") })
            With(() => new ProviderSettingsSection(provider, Binding<bool>.Constant(true)), (view, model, h, w) =>
            {
                var row = Row(view, rowTitle); var button = Assert.Single(Nodes<Button>(row)); var (surface, label, effect) = Pill(button);
                Assert.Equal(text, label.Text); Assert.Equal(Chrome.RowButtonLabelHeight, label.Height); // .frame(height: Chrome.rowButtonLabelHeight)
                Assert.Equal(22, surface.Bounds.Height, 3); Assert.Equal(new Thickness(13, 0), surface.Padding); Assert.Equal(13, label.FontSize); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // .buttonStyle(.glass): 22 high, 13 either side, white 13 point (S 166.5-180.5 and S 199.5)
                Assert.InRange(surface.Bounds.Width - (label.Bounds.Width + 26), -1.0, 1.0); Assert.Equal(Chrome.Accent, effect.TintColor); Assert.Equal(1, effect.TintOpacity); Assert.True(effect.IsInteractive);
                Assert.True(button.IsEffectivelyEnabled); Assert.Equal(1, surface.Opacity);
            }, model => Seed(model, provider, provider == ProviderKind.Commandcode ? new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/command-code"), ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() } : new ProviderStatus()));
    }

    // The state matrix of ChromeVisualPicker.swift:36-105 for the pickers SettingsView itself owns (Done with a thread, Sidebar, Activity list): the
    // fill, ring, symbol and caption of a selected, a hovered and a plain tile, each moving over Chrome.hover, and the press dim.
    private static void CheckTileStates(Control row, Avalonia.Controls.Window w, AppModelHarness h, string[] titles, string[] symbols)
    {
        var tiles = Nodes<ChromeVisualTile>(row); Assert.Equal(titles, tiles.Select(AutomationProperties.GetName)); Assert.Equal(symbols, tiles.Select(tile => Assert.Single(Nodes<SwiftImage>(tile)).SymbolName));
        Click(w, Assert.Single(Nodes<Button>(tiles[0]))); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); // the first tile chosen, the pointer away, everything settled
        var parts = tiles.Select(t => (Stage: Assert.Single(Nodes<Border>(t), b => b.Width == 76 && b.Height == 48), Ring: Assert.Single(Nodes<SwiftShapeBorder>(t), b => b.BorderThickness == new Thickness(2)) /* the continuous ring (ED-181) */, Caption: Assert.Single(Nodes<TextBlock>(t)), Symbol: Assert.Single(Nodes<SwiftImage>(t)), Content: Assert.Single(Nodes<StackPanel>(t)))).ToArray();
        Color Of(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color; Color Resolved(SwiftColor c) => c.Resolve(tiles[0].ActualThemeVariant);
        var stageTo = Resolved(Chrome.Overlay(0.10)); var stageFrom = Resolved(Chrome.Overlay(0.06)); var accent = Resolved(Chrome.Accent); var none = Resolved(Chrome.Accent.Opacity(0)); var primary = Resolved(Chrome.PrimaryText); var secondary = Resolved(Chrome.SecondaryText);
        // selected: accent ring and symbol, primary caption, the 0.10 fill; plain: no ring, secondary symbol and caption, the 0.06 fill
        Assert.Equal(accent, Of(parts[0].Ring.BorderBrush)); Assert.Equal(accent, Of(parts[0].Symbol.Foreground)); Assert.Equal(primary, Of(parts[0].Caption.Foreground)); Assert.Equal(stageTo, Of(parts[0].Stage.Background));
        for (var i = 1; i < tiles.Length; i++) { Assert.Equal(none, Of(parts[i].Ring.BorderBrush)); Assert.Equal(secondary, Of(parts[i].Symbol.Foreground)); Assert.Equal(secondary, Of(parts[i].Caption.Foreground)); Assert.Equal(stageFrom, Of(parts[i].Stage.Background)); }
        // a click selects inside withAnimation(Chrome.hover): both tiles are part-way after 40 ms and settled after the 0.1 s
        Click(w, Assert.Single(Nodes<Button>(tiles[1]))); Tick(h, 40);
        Assert.NotEqual(none, Of(parts[1].Ring.BorderBrush)); Assert.NotEqual(accent, Of(parts[1].Ring.BorderBrush)); Assert.NotEqual(accent, Of(parts[0].Ring.BorderBrush)); Assert.NotEqual(none, Of(parts[0].Ring.BorderBrush));
        Assert.NotEqual(secondary, Of(parts[1].Symbol.Foreground)); Assert.NotEqual(accent, Of(parts[1].Symbol.Foreground)); Assert.NotEqual(secondary, Of(parts[1].Caption.Foreground)); Assert.NotEqual(primary, Of(parts[1].Caption.Foreground));
        Assert.NotEqual(stageFrom, Of(parts[1].Stage.Background)); Assert.NotEqual(stageTo, Of(parts[1].Stage.Background));
        Tick(h, 101);
        Assert.Equal(none, Of(parts[0].Ring.BorderBrush)); Assert.Equal(accent, Of(parts[1].Ring.BorderBrush)); Assert.Equal(secondary, Of(parts[0].Symbol.Foreground)); Assert.Equal(accent, Of(parts[1].Symbol.Foreground));
        Assert.Equal(secondary, Of(parts[0].Caption.Foreground)); Assert.Equal(primary, Of(parts[1].Caption.Foreground)); Assert.Equal(stageTo, Of(parts[1].Stage.Background));
        // hovering lifts the plain tile's fill to the 0.10 over the same curve and the pointer leaving takes it back; the symbol and caption stay secondary
        Hover(w, Assert.Single(Nodes<Button>(tiles[0]))); Tick(h, 40); Assert.NotEqual(stageFrom, Of(parts[0].Stage.Background)); Assert.NotEqual(stageTo, Of(parts[0].Stage.Background)); Tick(h, 101);
        Assert.Equal(stageTo, Of(parts[0].Stage.Background)); Assert.Equal(secondary, Of(parts[0].Symbol.Foreground)); Assert.Equal(secondary, Of(parts[0].Caption.Foreground)); Assert.Equal(none, Of(parts[0].Ring.BorderBrush));
        w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150); Assert.Equal(stageFrom, Of(parts[0].Stage.Background));
        // the geometry every tile shares: 76 x 48 whatever the number of options, 6 radius, ring 2 wide, caption 6 below, at most two lines
        foreach (var part in parts) { Assert.Equal(new Size(76, 48), part.Stage.Bounds.Size); Assert.Equal(new CornerRadius(6), part.Stage.CornerRadius); Assert.Equal(2, part.Caption.MaxLines); }
        // press.row-tile-dim: held down the tile's content is at 0.8 and its fill at x0.7; letting go restores both and selects the tile
        var at = tiles[0].TranslatePoint(new Point(20, 20), w)!.Value; w.MouseMove(at); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 51);
        Assert.Equal(0.8, parts[0].Content.Opacity, 3); Assert.Equal(1, parts[1].Content.Opacity); Assert.Equal(Resolved(Chrome.Overlay(0.10).Opacity(0.7)), Of(parts[0].Stage.Background));
        w.MouseUp(at, MouseButton.Left); Drain(w); Tick(h, 151); Assert.Equal(1, parts[0].Content.Opacity, 3); Assert.Equal(accent, Of(parts[0].Ring.BorderBrush)); w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 150);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-065 for the pickers SettingsView.swift owns (Settle/Archive, Column/Floating/Panel only, Icon/Project name); the Hydra pickers are asserted by HydraSettingsPageSpecTests, the continuous corner awaits ED-181
    [Trait("Req", "UI-SETTINGS-065")] // SettingsView.swift:395-399,432-442; ChromeVisualPicker.swift:10-105
    public void The_owned_visual_pickers_have_the_selected_hovered_and_plain_states_over_Chrome_hover_and_the_press_dim()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) => CheckTileStates(Row(view, "Done with a thread"), w, h, ["Settle", "Archive"], ["checkmark", "archivebox"]));
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            CheckTileStates(Row(view, "Sidebar"), w, h, ["Column", "Floating", "Panel only"], ["sidebar.left", "macwindow.on.rectangle", "macwindow"]);
            CheckTileStates(Row(view, "Activity list"), w, h, ["Icon", "Project name"], ["list.bullet", "text.alignleft"]);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-065: the rows whose detail follows the selected tile (the Hydra rows are asserted by HydraSettingsPageSpecTests)
    [Trait("Req", "UI-SETTINGS-065")] // SettingsView.swift:395,432,438
    public void The_row_detail_follows_the_selected_tile_in_Done_with_a_thread_Sidebar_and_Activity_list()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var tiles = Nodes<ChromeVisualTile>(Row(view, "Done with a thread"));
            foreach (var action in new[] { ThreadFinishAction.Archive, ThreadFinishAction.Settle })
            {
                Click(w, Assert.Single(Nodes<Button>(tiles[action == ThreadFinishAction.Archive ? 1 : 0]))); Tick(h, 150); Assert.Equal(action, model.Settings.ThreadFinishAction);
                Assert.Contains(action.Detail, Text(Row(view, "Done with a thread")));
            }
        });
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            var sidebar = Row(view, "Sidebar"); var sidebarTiles = Nodes<ChromeVisualTile>(sidebar);
            for (var i = 0; i < SwiftEnum<SidebarMode>.AllCases.Count; i++) { Click(w, Assert.Single(Nodes<Button>(sidebarTiles[i]))); Tick(h, 150); Assert.Equal(SwiftEnum<SidebarMode>.AllCases[i], model.Settings.SidebarMode); Assert.Contains(SwiftEnum<SidebarMode>.AllCases[i].Detail, Text(sidebar)); }
            var activity = Row(view, "Activity list"); var activityTiles = Nodes<ChromeVisualTile>(activity);
            for (var i = 0; i < SwiftEnum<ActivityThreadStyle>.AllCases.Count; i++) { Click(w, Assert.Single(Nodes<Button>(activityTiles[i]))); Tick(h, 150); Assert.Equal(SwiftEnum<ActivityThreadStyle>.AllCases[i], model.Settings.ActivityThreadStyle); Assert.Contains(SwiftEnum<ActivityThreadStyle>.AllCases[i].Detail, Text(activity)); }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-018: the reset's animation reaching the window backdrop (SettingsView.swift:546, OD-0076); the drag, the end glyphs, the reset, the tooltips and the layout are asserted by Transparency_slider_follows_a_real_drag_live_and_shows_its_reset_only_off_stock_without_taking_layout_room, The_end_glyphs_show_their_tips_over_their_whole_frame_not_only_where_the_ring_was_painted and Transparency_reset_hits_its_whole_22_point_square_with_no_background_in_any_state_and_a_click_animates_back_over_snappy_0_2
    [Trait("Req", "UI-SETTINGS-018")] // SettingsView.swift:428-430,519-563
    public void Reset_glides_the_window_backdrop_to_stock_in_the_snappy_transaction_where_a_drag_moves_it_live()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            Click(w, Nodes<SidebarRow>(view).Single(row => Text(row).Contains("Appearance"))); var settings = model.Settings; var backdrop = Assert.Single(Nodes<WindowBackdrop>(view));
            var reset = Assert.Single(Nodes<Button>(Assert.Single(Nodes<BackdropOpacitySlider>(view)))); var isDark = Marrow.UI.Port.PlainControls.IsDark(backdrop.ActualThemeVariant);
            double Scrim(double value) => WindowBackdrop.Scrim(value, isDark);
            // outside a transaction the backdrop follows the value at once (a drag)
            settings.BackdropOpacity = 0.9; Drain(w); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(0.9)); Tick(h, 400); Drain(w);
            var material = Assert.IsType<Panel>(Assert.Single(Assert.IsType<Panel>(Assert.IsType<Panel>(backdrop.Child).Children[0]).Children)); var scrim = Assert.IsType<SwiftShapeBorder>(material.Children[1]); /* the window shape is Apple's continuous one (ED-275) */
            Click(w, reset); Assert.Equal(0.5, settings.BackdropOpacity); // the setting is written at once; the window's glass takes the snappy 0.2 to get there
            Assert.Equal(Scrim(0.9), scrim.Opacity, 6); Tick(h, 60); Assert.InRange(scrim.Opacity, Scrim(0.5) + 0.0005, Scrim(0.9) - 0.0005);
            var mid = scrim.Opacity; Tick(h, 60); Assert.True(scrim.Opacity < mid + 1e-9, "the scrim moves one way: " + mid + " then " + scrim.Opacity);
            Tick(h, 400); Drain(w); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(0.5));
            // a live write stops a glide: the backdrop is where the value says
            settings.BackdropOpacity = 0.9; Drain(w); Click(w, reset); Tick(h, 40); settings.BackdropOpacity = 0.7; Drain(w); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(0.7)); Tick(h, 400); SettingsBackdropAssertions.GlassOpacity(backdrop, Scrim(0.7));
        }, height: 1100);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-018: the held knob's own transition (OD-0076); the drag itself is asserted by Transparency_slider_follows_a_real_drag_live_and_shows_its_reset_only_off_stock_without_taking_layout_room
    [Trait("Req", "UI-SETTINGS-018")] // SettingsView.swift:532-534; the reference runtime 1-settings-tour.mov 125.868-125.975 (press), 130.893-131.022 (release)
    public void The_slider_knob_turns_to_glass_over_the_press_and_back_over_the_release_not_in_one_frame()
    {
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            var slider = Assert.Single(Nodes<Slider>(view)); var knob = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>()); var face = Assert.Single(knob.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius.TopLeft >= 7 && b.BoxShadow.Count > 0);
            Assert.Equal(new Size(18, 14), knob.Bounds.Size); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
            var at = At(knob, w); w.MouseMove(at); w.MouseDown(at, MouseButton.Left); w.MouseMove(at + new Vector(10, 0)); Drain(w);
            Tick(h, 40); Drain(w); Assert.InRange(knob.Bounds.Width, 18.1, 22.9); Assert.InRange(knob.Bounds.Height, 14.1, 19.9); var mid = Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color.A; Assert.InRange(mid, 0x31, 0xFE); // part-way: neither the white capsule nor the glass one
            Tick(h, 100); Drain(w); Assert.Equal(new Size(23, 20), knob.Bounds.Size); Assert.Equal(0x30, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color.A);
            w.MouseUp(at + new Vector(10, 0), MouseButton.Left); Drain(w);
            Tick(h, 30); Drain(w); Assert.InRange(knob.Bounds.Width, 18.1, 22.9); Assert.InRange(knob.Bounds.Height, 14.1, 19.9);
            Tick(h, 100); Drain(w); Assert.Equal(new Size(18, 14), knob.Bounds.Size); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-050: the field, the eye and their states; the refused-key line is asserted by Secure_key_presenter_masks_the_actual_rendered_text_and_refused_keys_show_the_owner_message_under_the_field, the 600 ms re-check by Edits_save_immediately_replace_debounce_and_recheck_at_600_ms
    public void The_API_key_field_and_its_eye_prove_every_clause_of_the_record_in_one_place()
    {
        With(() => new StackPanel { Children = { new ProviderSettingsSection(ProviderKind.Meta, Binding<bool>.Constant(true)), new ProviderSettingsSection(ProviderKind.Zai, Binding<bool>.Constant(true)), new ProviderSettingsSection(ProviderKind.Commandcode, Binding<bool>.Constant(true)) } }, (view, model, h, w) =>
        {
            var meta = Nodes<ProviderSettingsSection>(view).Single(s => Field<ProviderKind>(s, "provider") == ProviderKind.Meta); var zai = Nodes<ProviderSettingsSection>(view).Single(s => Field<ProviderKind>(s, "provider") == ProviderKind.Zai); var commandCode = Nodes<ProviderSettingsSection>(view).Single(s => Field<ProviderKind>(s, "provider") == ProviderKind.Commandcode);
            Assert.NotNull(Row(meta, "API key")); Assert.NotNull(Row(zai, "API key")); Assert.NotNull(Row(commandCode, "API key (optional)")); // the title by the kind of provider
            var field = Assert.Single(Nodes<TextBox>(meta)); var other = Assert.Single(Nodes<TextBox>(zai));
            // the field: a rounded border (the Apple kit's text field), 200 wide, 12 point monospaced, the prompt "sk-" and an ellipsis
            Assert.Equal("sk-…", field.PlaceholderText); Assert.Equal("RoundedBorder", field.GetValue(Marrow.App.Port.ChatDialogControls.InputStyleProperty)); Assert.Equal(200, field.Width);
            Assert.Equal(SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Resolve().FontSize, field.FontSize); Assert.Equal(SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, field.FontFamily);
            var bezel = field.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius == new CornerRadius(6)); Assert.Equal(24, bezel.Bounds.Height, 1); Assert.Equal(200, bezel.Bounds.Width, 1);
            // the eye: 8 points from the field, 12 point, secondary, hidden by default with its symbol and tip
            var row = Assert.IsType<StackPanel>(field.Parent); Assert.Equal(8, row.Spacing); var eye = Assert.Single(Nodes<Button>(row)); var symbol = Assert.Single(Nodes<SwiftImage>(eye));
            Assert.Equal('•', field.PasswordChar); Assert.Equal("eye", symbol.SymbolName); Assert.Equal("Show the API key", ToolTip.GetTip(eye)); Assert.Equal(SwiftFont.System(12), SwiftImage.GetFont(symbol)); Ink(symbol, symbol.Foreground, Chrome.SecondaryText);
            // the corners of the eye's frame belong to it, the symbol's own pixels or not
            foreach (var corner in new[] { new Point(1, 1), new Point(eye.Bounds.Width - 1, 1), new Point(1, eye.Bounds.Height - 1), new Point(eye.Bounds.Width - 1, eye.Bounds.Height - 1) })
            {
                var hit = w.InputHitTest(At(eye, w, corner)) as Visual; Assert.NotNull(hit); Assert.True(hit == eye || eye.IsVisualAncestorOf(hit), "the point " + corner + " of the eye is not the eye: " + hit);
            }
            Click(w, eye); Assert.Equal('\0', field.PasswordChar); symbol = Assert.Single(Nodes<SwiftImage>(eye)); Assert.Equal("eye.slash", symbol.SymbolName); Assert.Equal("Hide the API key", ToolTip.GetTip(eye)); Assert.Equal(SwiftFont.System(12), SwiftImage.GetFont(symbol)); Ink(symbol, symbol.Foreground, Chrome.SecondaryText);
            Assert.Equal('•', other.PasswordChar); // the reveal state belongs to its section
            // typing writes the setting at once
            field.Text = "fixture-key-never-sent"; Drain(w); Assert.Equal("fixture-key-never-sent", model.Settings.ApiKeyInput(ProviderKind.Meta)); field.Text = ""; Drain(w);
        });
        // not persisted: another copy of the page starts hidden
        With(() => new ProviderSettingsSection(ProviderKind.Meta, Binding<bool>.Constant(true)), (view, model, h, w) => { Assert.Equal('•', Assert.Single(Nodes<TextBox>(view)).PasswordChar); Assert.Equal("Show the API key", ToolTip.GetTip(Assert.Single(Nodes<Button>(Assert.IsType<StackPanel>(Assert.Single(Nodes<TextBox>(view)).Parent))))); });
    }

    private static Control Opened(Popup popup) { Open(popup); return PopoverHost.Content(popup)!; }
}
