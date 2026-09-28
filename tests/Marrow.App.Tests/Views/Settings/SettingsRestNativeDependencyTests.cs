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
    public void Missing_framework_slider_form_is_a_precise_blocker_not_a_property_only_drag_proof()
    {
        // STOP opencode-SettingsView-rest #4: UI-THEME-009/012, SettingsView.cs BackdropOpacitySlider :535.
        // No native Slider theme/template is registered; its framework track/thumb and real drag semantics are absent.
        // This records the missing external view form, without inventing its size, colours or thumb geometry.
        With(() => new BackdropOpacitySlider(Binding<double>.Constant(0.5)), (view, _, _, w) =>
        {
            var slider = Assert.Single(Nodes<Slider>(view)); Assert.Equal(140, slider.Width); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum);
            Assert.Null(slider.Template); Assert.Empty(slider.GetVisualDescendants());
            var at = At(slider, w); w.MouseDown(at, MouseButton.Left); w.MouseMove(at + new Vector(40, 0)); w.MouseUp(at + new Vector(40, 0), MouseButton.Left); Drain(w); Assert.Equal(0.5, slider.Value);
        });
    }

    [Fact]
    [Trait("Decision", "ED-071")]
    public void Existing_switch_binding_preview_and_disabled_state_are_preserved_pending_native_switch_form()
    {
        // STOP opencode-SettingsView-rest #5: UI-SETTINGS-066, UI-THEME-026/027, UI-USAGE-044.
        // SettingsView.cs SmallSwitch :275-326 explicitly invents macOS-looking track/knob dimensions.
        // The shared ruled Windows small native switch form/theme is missing; do not call that a system-style proof.
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
