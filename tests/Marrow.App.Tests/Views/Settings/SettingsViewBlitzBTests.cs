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
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
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
            Assert.Equal("Marrow is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is Marrow's own, built on Windows ConPTY.", licence.Text); // OD-0072 for the clause after the semicolon
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
            Assert.Equal(10, Nodes<SidebarRow>(view).Length);
            Click(w, about); Drain(w);
            Assert.Single(Nodes<AboutSettingsPage>(view)); Assert.Empty(Nodes<GeneralSettingsPage>(view)); // the click selects the page
            for (var i = 0; i < 50 && checker.LastError is null; i++) Drain(w);
            Assert.Equal("No update feed is set.", checker.LastError); // ... and the page's section checked for updates (the feed is empty, so no request is made)
            foreach (var (query, expected) in new[] { ("version", 1), ("license", 1), ("VERSION", 1), ("licence", 0) }) // :77 keywords are the American "license"
            {
                field.Text = query; Drain(w);
                var shown = Nodes<SidebarRow>(view).Where(row => Text(row).Length > 0 && Text(row)[0] != "No results").ToArray();
                Assert.Equal(expected, shown.Length); if (expected == 1) Assert.Equal("About", Text(shown[0]).Single());
                if (expected == 0) { Assert.Contains("No results", Text(view)); Assert.Contains("Try a setting or a provider name.", Text(view)); }
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
        });
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

    private static Control Opened(Popup popup) { Open(popup); return PopoverHost.Content(popup)!; }
}
