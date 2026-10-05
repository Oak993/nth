using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Accessibility;

// Positive checks distinguish missing visuals from broken input, and exercise actual headless keys.
[Collection("Headless")]
public sealed class A11yKeyboardParityTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "VT-13")]
    public void Keyboard_Voice_typing_tabs_through_engine_language_microphone_and_customization() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Window.FocusManager!.Focus(null);
        Control[] expected = [scene.Page.EngineControl.Buttons.Single(b => b.Focusable), scene.Page.LanguageMenu, scene.Page.MicrophoneMenu, scene.Page.OrbButton];
        foreach (var target in expected)
        {
            scene.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            scene.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Same(target, scene.Window.FocusManager.GetFocusedElement());
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(target)));
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "VT-13")]
    public void Keyboard_Voice_language_menu_enter_opens_and_escape_returns_focus() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var menu = scene.Page.LanguageMenu;
        Assert.True(menu.Focus(NavigationMethod.Tab));
        scene.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.True(menu.IsOpen);
        scene.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(menu.IsOpen);
        Assert.Same(menu, scene.Window.FocusManager!.GetFocusedElement());
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "CZ-1")]
    public void Keyboard_Custom_capsule_focus_changes_rendered_pixels() => headless.OnUiThread(() =>
    {
        var button = new CzCapsuleButton("Review focus calibration");
        var window = new Avalonia.Controls.Window { Content = new Border { Padding = new Avalonia.Thickness(15), Child = button }, Width = 280, Height = 100 };
        try { window.Show(); A11yReview.VisibleFocus(window, button); }
        finally { window.Close(); }
    });
}
