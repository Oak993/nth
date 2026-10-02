using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SystemButtons: SwiftUI `.bordered` / `.borderedProminent` at `.controlSize(.regular)` (every site: QuestionTab.swift:165,
// SidebarView.swift:1018) drawn as macOS draws them (OD-0042 "Mac look"). OD-0045: every value is a SOURCE FACT (Apple
// design resource) from the "Apple macOS 27 UI Kit" (Sketch, Buttons / <Dark|Light> / Content Area / Bordered and Bordered
// Default / 3 Rg, Inspect panel, read 2026-10-02; E:\Droppy\work\apple-kit\macos27-uikit-controls.md).
[Collection("Headless")]
public sealed class SystemButtonsTests(HeadlessSession headless)
{
    private static Color Rgba(double white, double opacity, ThemeVariant theme) => new SwiftColor(white: white, opacity: opacity).Resolve(theme);

    private static Color Fill(Border part) => Assert.IsAssignableFrom<ISolidColorBrush>(part.Background).Color;

    private static T Part<T>(Button button, string name) where T : Control =>
        button.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static (Window Window, StackPanel Parent) Host(Button button, ThemeVariant theme)
    {
        var parent = new StackPanel { Children = { button }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(10) };
        var window = new Window { Content = parent, Width = 300, Height = 100, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-231")]
    public void A_regular_button_is_24_high_with_16_by_4_padding_a_radius_of_6_and_a_13_point_medium_label(bool prominent) => headless.OnUiThread(() =>
    {
        var button = prominent ? SystemButtons.BorderedProminent("Label", () => { }) : SystemButtons.Bordered("Label", () => { });
        var (window, _) = Host(button, ThemeVariant.Dark);
        try
        {
            // 3 Rg: 66 x 24 around a 34 x 16 label, padding 4 / 4 / 16 / 16, radius 6 (Smooth), no border.
            var bezel = Part<Border>(button, "PART_Bezel");
            Assert.Equal(new Thickness(16, 4, 16, 4), bezel.Padding);
            Assert.Equal(new CornerRadius(6), bezel.CornerRadius);
            Assert.Equal(default, bezel.BorderThickness);
            Assert.Equal(24, button.Bounds.Height, 3);
            // Label: SF Pro Medium (500) 13 px, line height 16, centred.
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(13, label.FontSize);
            Assert.Equal(FontWeight.Medium, label.FontWeight);
            Assert.Equal(16, label.LineHeight);
            Assert.Equal(16, label.Bounds.Height, 3);
            Assert.Equal(button.Bounds.Width - 32, label.Bounds.Width, 3);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-231")]
    public void A_bordered_button_washes_7_or_8_percent_deepens_to_16_while_pressed_and_never_changes_under_the_pointer(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var dark = theme == ThemeVariant.Dark;
        var clicks = 0;
        var button = SystemButtons.Bordered("Cancel", () => clicks++);
        var (window, _) = Host(button, theme);
        try
        {
            var bezel = Part<Border>(button, "PART_Bezel");
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            // Layer style 01 - Bordered / 01 - Idle: Dark #FFFFFF@0.07, Light #000000@0.08. Label Labels 1 Primary:
            // Dark #FFFFFF@1, Light #000000@0.85.
            var idle = dark ? Rgba(1, 0.07, theme) : Rgba(0, 0.08, theme);
            Assert.Equal(idle, Fill(bezel));
            Assert.Equal(dark ? Rgba(1, 1, theme) : Rgba(0, 0.85, theme), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            // The kit has no hover state: the pointer over the button leaves it as it is.
            var inside = button.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseMove(inside);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(idle, Fill(bezel));
            // 03 - Clicked: #FFFFFF@0.16 / #000000@0.16, back to idle on release, and the click runs once.
            window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(dark ? Rgba(1, 0.16, theme) : Rgba(0, 0.16, theme), Fill(bezel));
            window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(idle, Fill(bezel));
            Assert.Equal(1, clicks);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-231")]
    public void A_disabled_bordered_button_washes_4_percent_under_a_tertiary_label_even_when_its_parent_disables_it(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var button = SystemButtons.Bordered("Cancel", () => { });
        var (window, parent) = Host(button, theme);
        try
        {
            var bezel = Part<Border>(button, "PART_Bezel");
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            button.Opacity = 0.5; // a site's own opacity (a fading row) is kept
            parent.IsEnabled = false; // the button's own IsEnabled stays true
            Dispatcher.UIThread.RunJobs();
            // 04 - Disabled: #FFFFFF@0.04 / #000000@0.04; the label Labels 3 Tertiary (@0.25).
            var white = theme == ThemeVariant.Dark ? 1 : 0;
            Assert.Equal(Rgba(white, 0.04, theme), Fill(bezel));
            Assert.Equal(Rgba(white, 0.25, theme), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            Assert.Equal(0.5, button.Opacity, 3);
            parent.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(theme == ThemeVariant.Dark ? Rgba(1, 0.07, theme) : Rgba(0, 0.08, theme), Fill(bezel));
            Assert.Equal(theme == ThemeVariant.Dark ? Rgba(1, 1, theme) : Rgba(0, 0.85, theme), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-231")]
    public void A_prominent_button_fills_with_the_accent_under_a_white_label_and_tints_while_pressed(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var button = SystemButtons.BorderedProminent("Send", () => { });
        var (window, parent) = Host(button, theme);
        try
        {
            var bezel = Part<Border>(button, "PART_Bezel");
            var press = Part<Border>(button, "PART_Press");
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            // 04 - Prominent (Default) / 01 - Idle: the accent fill (System Blue in the kit; the app's accent tints it),
            // label #FFFFFF.
            Assert.Equal(Marrow.UI.Chrome.Chrome.Accent.Resolve(theme), Fill(bezel));
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            Assert.False(press.IsVisible);
            // 03 - Clicked: the fill under #FFFFFF@0.1 (Dark, plus lighter) / #000000@0.1 (Light, plus darker).
            var inside = button.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(press.IsVisible);
            Assert.Equal(theme == ThemeVariant.Dark ? Rgba(1, 0.1, theme) : Rgba(0, 0.1, theme), Fill(press));
            Assert.Equal(new CornerRadius(6), press.CornerRadius);
            window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(press.IsVisible);
            // 04 - Disabled: the fill at 40 % layer opacity, the white label at 50 %.
            parent.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.4, bezel.Opacity, 3);
            Assert.Equal(0.5, label.Opacity, 3);
            parent.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, bezel.Opacity, 3);
            Assert.Equal(1, label.Opacity, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-231")]
    public void Keyboard_focus_rings_the_button_with_a_three_and_a_half_point_accent_band_and_a_click_does_not() => headless.OnUiThread(() =>
    {
        var clicked = SystemButtons.Bordered("Cancel", () => { });
        var button = SystemButtons.BorderedProminent("Rename", () => { });
        var (window, parent) = Host(clicked, ThemeVariant.Dark);
        try
        {
            parent.Children.Add(button);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var clickedRing = Part<Border>(clicked, "PART_Focus");
            var ring = Part<Border>(button, "PART_Focus");
            // A click focuses the button but shows no ring (:focus-visible only).
            var inside = clicked.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(clicked.IsFocused);
            Assert.False(clickedRing.IsVisible);
            // Tab moves keyboard focus on, and the ring follows it.
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused && ring.IsVisible, "keyboard focus shows the ring");
            Assert.False(clickedRing.IsVisible);
            // Focus ring style: 3.5 px #0088FF@0.25 outside the control (the accent stands for System Blue), following
            // the bezel's radius.
            Assert.Equal(new Thickness(3.5), ring.BorderThickness);
            Assert.Equal(new Thickness(-3.5), ring.Margin);
            Assert.Equal(new CornerRadius(9.5), ring.CornerRadius);
            Assert.Equal(Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
        }
        finally { window.Close(); }
    });
}
