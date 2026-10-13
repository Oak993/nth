using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.UI.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-2")]
public sealed class WebW20ContrastTests(HeadlessSession headless)
{
    [Fact]
    public void A_focused_stepper_updates_its_ring_to_the_current_system_highlight() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var stepper = new NthUiStepper { Minimum = 0, Maximum = 10, Value = 5 };
        var surface = (Border)rig.Window.Content!;
        surface.Child = stepper;
        rig.Pump();
        Assert.True(stepper.Focus(NavigationMethod.Tab));
        Assert.Contains(":focus-visible", stepper.Classes);
        var capsule = stepper.GetVisualDescendants().OfType<Border>().First(border => border.Height == NthUiTokens.ControlHeight);
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            ContrastSurface.MatchTheme(surface);
            rig.Pump();
            Assert.Equal(palette.Highlight, capsule.BoxShadow[0].Color);
        }
    });

    [Fact]
    public void A_primary_buttons_focus_ring_has_contrast_against_its_highlight_fill() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var button = NthUiPress.Button("Save", NthUiFaceStyle.Primary, null, () => { });
        ((Border)rig.Window.Content!).Child = button;
        rig.Pump();
        Assert.True(button.Focus(NavigationMethod.Tab));
        Assert.Contains(":focus-visible", button.Classes);
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, HighContrastPalette.Black);
        rig.Pump();
        var face = button.GetVisualDescendants().OfType<NthUiFace>().Single();
        Assert.True(HighContrastPalette.ContrastRatio(face.BoxShadow[0].Color, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color) >= 3);
    });

    [Fact]
    public void Custom_switch_and_checklist_marks_use_highlight_text_on_highlight() => headless.OnUiThread(() =>
    {
        var check = new NthUiCheck(new NthUiMotion()) { IsChecked = true };
        var toggle = new NthUiSwitch(new NthUiMotion()) { IsChecked = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { check, toggle } };
        using var rig = new NthUiRig();
        var surface = (Border)rig.Window.Content!;
        surface.Child = row;
        rig.Settle();
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            ContrastSurface.MatchTheme(surface);
            surface.Background = palette.Brush(ContrastRole.Surface);
            rig.Pump();
            var frame = rig.Capture();
            var origin = check.TranslatePoint(default, rig.Window)!.Value;
            Assert.Equal(((int)palette.Highlight.R, (int)palette.Highlight.G, (int)palette.Highlight.B), frame.At((int)origin.X + 9, (int)origin.Y + 4));
            origin = toggle.TranslatePoint(default, rig.Window)!.Value;
            Assert.Equal(((int)palette.HighlightText.R, (int)palette.HighlightText.G, (int)palette.HighlightText.B), frame.At((int)origin.X + 27, (int)origin.Y + 11));
        }
    });

    [Fact]
    public void All_status_tones_keep_system_text_and_solid_fills_in_contrast_themes()
    {
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
            foreach (var tone in new[] { "info", "success", "warning", "danger", "neutral" })
            {
                Assert.Equal(palette.WindowText, NthUiTokens.Tone(tone).Resolve(ThemeVariant.Light, palette));
                Assert.Equal(palette.Window, NthUiTokens.ToneFill(tone).Resolve(ThemeVariant.Light, palette));
            }
    }

    [Fact]
    public void Custom_painted_slider_tracks_and_thumbs_use_the_system_palette() => headless.OnUiThread(() =>
    {
        var slider = new NthUiSlider { Width = 180, Minimum = 0, Maximum = 100, Value = 50, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        using var rig = new NthUiRig();
        ((Border)rig.Window.Content!).Child = slider;
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            ContrastSurface.MatchTheme(slider);
            rig.Pump();
            var origin = slider.TranslatePoint(default, rig.Window)!.Value;
            var frame = rig.Capture();
            var track = frame.At((int)origin.X + 40, (int)origin.Y + 12);
            var thumb = frame.At((int)origin.X + 90, (int)origin.Y + 12);
            Assert.Equal(((int)palette.Highlight.R, (int)palette.Highlight.G, (int)palette.Highlight.B), track);
            Assert.Equal(((int)palette.WindowText.R, (int)palette.WindowText.G, (int)palette.WindowText.B), thumb);
        }
    });

    [Fact]
    public void Live_answers_match_both_system_palettes_keep_selection_and_return_to_the_approved_glass() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show("card \"Inventory\"\n  text \"Long answer العربية שלום\"\n  state n = 2\n  stepper n label=\"Count\" min=1 max=9\n  button \"Save\" primary=true\n", culture: NthUiRig.English), theme: ThemeVariant.Light);
        var card = rig.View.GetVisualDescendants().OfType<NthUiSlot>().First(slot => slot.Surface?.Shadow == true);
        var text = rig.View.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Long answer العربية שלום");
        var before = ((ISolidColorBrush)text.Foreground!).Color;
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            rig.Pump();
            Assert.True(ContrastSurface.IsMatched(card));
            Assert.Equal(palette.WindowText, ((ISolidColorBrush)text.Foreground!).Color);
            Assert.Equal(palette.Window, NthUiTokens.CardFill.Resolve(card.ActualThemeVariant, palette));
            Assert.Equal(palette.Highlight, NthUiTokens.Strong.Resolve(card.ActualThemeVariant, palette));
            Assert.Equal(palette.HighlightText, NthUiTokens.OnStrong.Resolve(card.ActualThemeVariant, palette));
            foreach (var role in new[] { NthUiTokens.Text1, NthUiTokens.Text2, NthUiTokens.Text3 })
                Assert.True(HighContrastPalette.ContrastRatio(role.Resolve(card.ActualThemeVariant, palette), palette.Window) >= 4.5);
            var pixel = rig.Capture().At(20, 45);
            Assert.Equal((palette.Window.R, palette.Window.G, palette.Window.B), ((byte)pixel.Red, (byte)pixel.Green, (byte)pixel.Blue));
        }
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null);
        rig.Pump();
        Assert.False(ContrastSurface.IsMatched(card));
        Assert.Equal(before, ((ISolidColorBrush)text.Foreground!).Color);
    });

    [Fact]
    public void A_selected_toggle_keeps_a_visible_system_outline_without_changing_its_size() => headless.OnUiThread(() =>
    {
        var toggle = NthUiPress.Toggle("Chosen", NthUiFaceStyle.Filter, 28, () => { }, out var face, out _);
        using var rig = new NthUiRig();
        ((Border)rig.Window.Content!).Child = toggle;
        toggle.IsChecked = true;
        rig.Pump();
        var before = face.Bounds.Size;
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, HighContrastPalette.Black);
        rig.Pump();
        Assert.Equal(before, face.Bounds.Size);
        Assert.Equal(1, face.BoxShadow.Count);
        Assert.Equal(HighContrastPalette.Black.Highlight, face.BoxShadow[0].Color);
    });
}
