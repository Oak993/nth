using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestNativeDependencyTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Decision", "ED-071")]
    [Trait("Req", "UI-THEME-009")] // App/Views/Settings/SettingsView.swift:532-534 Slider(value:in: 0...1).controlSize(.small).frame(width: 140)
    public void The_transparency_slider_is_drawn_and_a_drag_changes_the_setting()
    {
        // Formerly STOP opencode-SettingsView-rest #4 (no Slider template: nothing drawn, drags ignored). Resolved by
        // PortControlThemes' Slider theme, measured from the reference runtime (proposal ED-229 (2)): a 4 pt track across
        // the 140 pt, a white 18 x 14 pt knob kept inside its ends.
        var value = 0.5;
        With(() => new BackdropOpacitySlider(new Binding<double>(() => value, v => value = v)), (view, _, h, w) =>
        {
            var slider = Assert.Single(Nodes<Slider>(view)); Assert.Equal(140, slider.Width); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum);
            var knob = Assert.Single(slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>());
            Assert.Equal(new Size(18, 14), knob.Bounds.Size);
            Assert.All(slider.GetVisualDescendants().OfType<Border>().Where(b => b.Height == 4), bar => Assert.True(bar.Bounds.Right <= 140.5));
            var at = knob.TranslatePoint(new Point(9, 7), w)!.Value; w.MouseDown(at, MouseButton.Left); w.MouseMove(at + new Vector(40, 0)); Drain(w); Tick(h, 150); Drain(w); // the knob turns to glass over 0.08 s
            // While held the knob is the larger glass capsule, the fill showing through (1-settings-tour.mov 128-131 s).
            Assert.Equal(new Size(23, 20), knob.Bounds.Size);
            Assert.True(((Avalonia.Media.ISolidColorBrush)knob.GetVisualDescendants().OfType<Border>().First().Background!).Color.A < 0x80, "the held knob is not translucent");
            w.MouseUp(at + new Vector(40, 0), MouseButton.Left); Drain(w); Tick(h, 150); Drain(w);
            Assert.Equal(new Size(18, 14), knob.Bounds.Size);
            Assert.True(slider.Value > 0.7, "the drag did not move the knob: " + slider.Value);
            Assert.Equal(slider.Value, value, 6); // the setting follows the knob
        });
    }

    [Fact]
    [Trait("Decision", "ED-071")]
    public void Existing_switch_binding_preview_and_disabled_state_are_preserved_pending_native_switch_form()
    {
        // Resolved STOP opencode-SettingsView-rest #5 (UI-SETTINGS-066, UI-THEME-026/027, UI-USAGE-044): SmallSwitch is no longer an invented 28 x 16 control;
        // it was the Mac's switch measured in the reference runtime (ED-280 (5)) and is now the owner's Nocta (OD-0141, ED-NEW-nocta-1: 46 x 26 track,
        // 22 dome, lume, press scale, spring slide and light crossover; SmallSwitchMotionTests). This test keeps the binding, preview and disabled-state checks; SettingsViewBlitzBTests prove each record's rows.
        var sound = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(sound);
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var finish = Row(view, "Chime when a turn finishes"); var settle = Row(view, "Sound when settling");
            CheckRow(finish, "Chime when a turn finishes", "A soft chime, whether or not the thread is in view");
            CheckRow(settle, "Sound when settling", "A small note as the thread settles");
            Assert.True(model.Settings.ChimeWhenFinished); Assert.True(model.Settings.SettleSound); Assert.False(model.Settings.ShowsUsagePanel);
            var toggle = Assert.Single(Nodes<SmallSwitch>(finish)); Click(w, toggle); Assert.False(model.Settings.ChimeWhenFinished); Assert.Equal(0, sound.Starts); Click(w, toggle); Assert.True(model.Settings.ChimeWhenFinished); Assert.Equal(1, sound.Starts);
            toggle = Assert.Single(Nodes<SmallSwitch>(settle)); Click(w, toggle); Assert.False(model.Settings.SettleSound); Assert.Equal(1, sound.Starts); Click(w, toggle); Assert.True(model.Settings.SettleSound); Assert.Equal(2, sound.Starts);
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Drain(w); Assert.False(settle.IsEffectivelyEnabled); Assert.Equal(0.5, settle.Opacity); Click(w, toggle); Assert.True(model.Settings.SettleSound); Assert.Equal(2, sound.Starts);
            var usage = Row(view, "Show a usage panel"); Click(w, Assert.Single(Nodes<SmallSwitch>(usage))); Assert.True(model.Settings.ShowsUsagePanel); Assert.True(h.Defaults.Bool("showsUsagePanel")); Click(w, Assert.Single(Nodes<SmallSwitch>(usage))); Assert.False(model.Settings.ShowsUsagePanel);
        });
    }
}
