using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// web surfaces W20 (work/design/web-surfaces/DESIGN.md 10 "High contrast: glass falls back to solid system colours; splitter
// hairlines become 1 DIP system lines"; lean rule 10 "no state shown by colour alone"; ED-NEW-web-w20-1): the contrast theme is
// an inherited environment value; a solid contrast surface draws colours with a role as the system's, and nothing else changes
// anywhere; the chosen item is ringed, a workspace gutter draws a 1 DIP system line, the grip, its chips and the popover card
// are solid system colours; and turning the theme off gives back exactly what was there.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-1")]
public sealed class HighContrastTests(HeadlessSession headless)
{
    [Fact]
    public void A_contrast_surface_preserves_its_explicit_local_appearance_after_contrast_ends() => headless.OnUiThread(() =>
    {
        var surface = new Border();
        var window = Show(surface, ThemeVariant.Light);
        try
        {
            surface.SetValue(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Dark);
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.White);
            ContrastSurface.MatchTheme(surface);
            Assert.Equal(ThemeVariant.Light, surface.ActualThemeVariant);
            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            ContrastSurface.MatchTheme(surface);
            Assert.Equal(ThemeVariant.Dark, surface.ActualThemeVariant);
            Assert.True(surface.IsSet(ThemeVariantScope.ActualThemeVariantProperty));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_chosen_cue_preserves_the_faces_existing_shadow_before_during_and_after_contrast() => headless.OnUiThread(() =>
    {
        var shadow = BoxShadows.Parse("0 2 8 0 #73000000");
        var face = new Border { BoxShadow = shadow };
        ContrastCue.SetChosen(face, false);
        Assert.Equal(shadow, face.BoxShadow);
        ContrastCue.SetChosen(face, true);
        Assert.Equal(shadow, face.BoxShadow);
        EnvironmentValues.SetAccessibilityHighContrast(face, HighContrastPalette.Black);
        Assert.True(ContrastCue.ShowsRing(face));
        EnvironmentValues.SetAccessibilityHighContrast(face, null);
        Assert.Equal(shadow, face.BoxShadow);
    });

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    public void A_solid_contrast_surface_never_paints_composition_glass_over_its_system_fill() => headless.OnUiThread(() =>
    {
        var surface = new Border { Width = 200, Height = 100, Background = Brushes.Black };
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var window = Show(new Border { Background = Brushes.Magenta, Child = surface }, ThemeVariant.Dark);
        try
        {
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            ContrastSurface.MatchTheme(surface);
            var at = surface.TranslatePoint(new Point(100, 50), window)!.Value;
            Assert.Equal(Colors.Black, Pixels(window)((int)at.X, (int)at.Y));
        }
        finally { window.Close(); }
    });

    private static Window Show(Control content, ThemeVariant theme)
    {
        var window = new Window { Width = 400, Height = 300, RequestedThemeVariant = theme, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Func<int, int, Color> Pixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var size = frame.PixelSize;
        var rgba = (frame.Format ?? Avalonia.Platform.PixelFormat.Rgba8888) == Avalonia.Platform.PixelFormat.Rgba8888;
        var bytes = new byte[size.Width * size.Height * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { pin.Free(); }
        return (x, y) =>
        {
            var p = (y * size.Width + x) * 4;
            return rgba ? Color.FromRgb(bytes[p], bytes[p + 1], bytes[p + 2]) : Color.FromRgb(bytes[p + 2], bytes[p + 1], bytes[p]);
        };
    }

    [Fact]
    public void Each_role_draws_its_system_colour_and_both_schemes_keep_words_at_7_to_1() => headless.OnUiThread(() =>
    {
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            Assert.Equal(palette.WindowText, palette.For(ContrastRole.Text));
            Assert.Equal(palette.WindowText, palette.For(ContrastRole.Line));
            Assert.Equal(palette.Window, palette.For(ContrastRole.Surface));
            Assert.Equal(palette.Highlight, palette.For(ContrastRole.Highlight));
            Assert.Equal(palette.HighlightText, palette.For(ContrastRole.HighlightText));
            Assert.Equal(palette.GrayText, palette.For(ContrastRole.Disabled));
            Assert.Equal(palette.HotLight, palette.For(ContrastRole.Link));
            Assert.Equal(Colors.Transparent, palette.For(ContrastRole.None));
            Assert.True(HighContrastPalette.ContrastRatio(palette.WindowText, palette.Window) >= 7, "words on the surface");
            Assert.True(HighContrastPalette.ContrastRatio(palette.HighlightText, palette.Highlight) >= 7, "words on the highlight");
            Assert.True(HighContrastPalette.ContrastRatio(palette.Highlight, palette.Window) >= 3, "a ring on the surface");
        }
        Assert.True(HighContrastPalette.Black.IsDark);
        Assert.False(HighContrastPalette.White.IsDark);
    });

    [Fact]
    public void A_colour_with_a_role_is_exactly_what_it_was_without_a_contrast_theme_or_off_a_solid_surface() => headless.OnUiThread(() =>
    {
        var plain = SwiftColor.Dynamic(light: new SwiftColor(0, 0, 0, .86), dark: new SwiftColor(.93, .93, .94, 1));
        var roled = plain.InHighContrast(ContrastRole.Text);
        Assert.Equal(ContrastRole.Text, roled.HighContrastRole);
        Assert.Equal(ContrastRole.Text, roled.Opacity(.5).HighContrastRole); // a fade keeps the role
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.Equal(plain.Resolve(theme), roled.Resolve(theme));
            Assert.Equal(plain.Opacity(.4).Resolve(theme), roled.Opacity(.4).Resolve(theme));
            Assert.Equal(plain.Resolve(theme), roled.Resolve(theme, (HighContrastPalette?)null));
            Assert.Equal(HighContrastPalette.Black.WindowText, roled.Resolve(theme, HighContrastPalette.Black));
        }

        // A contrast theme in the window, but glass that stays glass: the role does not apply there.
        var text = new TextBlock { Text = "Visus" };
        var window = Show(new Border { Child = text }, ThemeVariant.Dark);
        try
        {
            text.Foreground = roled.Brush(text);
            Assert.Equal(plain.Resolve(ThemeVariant.Dark), BrushColor(text.Foreground));
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            Assert.False(EnvironmentValues.GetAccessibilityContrastSurface(text));
            Assert.Null(EnvironmentValues.ContrastPaletteFor(text));
            Assert.Equal(plain.Resolve(ThemeVariant.Dark), BrushColor(text.Foreground));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void On_a_solid_contrast_surface_a_role_draws_the_system_colour_and_gives_the_old_one_back_when_contrast_ends() => headless.OnUiThread(() =>
    {
        var text = new TextBlock { Text = "Visus" };
        var faded = new TextBlock { Text = "gone" };
        var half = new TextBlock { Text = "half" };
        var surface = new Border { Child = new StackPanel { Children = { text, faded, half } } };
        var window = Show(surface, ThemeVariant.Light);
        try
        {
            text.Foreground = PaneTokens.Text1.Brush(text);
            faded.Foreground = PaneTokens.Text1.Opacity(0).Brush(faded);
            half.Foreground = PaneTokens.Text2.Opacity(.5).Brush(half);
            var before = (BrushColor(text.Foreground), BrushColor(faded.Foreground), BrushColor(half.Foreground));

            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            ContrastSurface.MatchTheme(surface);
            Assert.True(ContrastSurface.IsMatched(surface));
            Assert.Same(HighContrastPalette.Black, EnvironmentValues.ContrastPaletteFor(text));
            Assert.Equal(Colors.White, BrushColor(text.Foreground));
            Assert.Equal(0, BrushColor(faded.Foreground).A); // a fade to nothing still fades
            Assert.Equal(Color.FromArgb(128, 255, 255, 255), BrushColor(half.Foreground));

            // The colours change with the theme the user picks.
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.White);
            ContrastSurface.MatchTheme(surface);
            Assert.Equal(Colors.Black, BrushColor(text.Foreground));

            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            ContrastSurface.MatchTheme(surface);
            Assert.False(ContrastSurface.IsMatched(surface));
            Assert.Equal(before, (BrushColor(text.Foreground), BrushColor(faded.Foreground), BrushColor(half.Foreground)));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_solid_surface_draws_its_content_for_the_contrast_themes_lightness_and_inherits_again_after() => headless.OnUiThread(() =>
    {
        var inner = new TextBlock { Text = "status" };
        var surface = new Border { Child = inner };
        var window = Show(new Border { Child = surface }, ThemeVariant.Light);
        try
        {
            Assert.Equal(ThemeVariant.Light, inner.ActualThemeVariant);
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            ContrastSurface.MatchTheme(surface);
            Assert.Equal(ThemeVariant.Dark, surface.ActualThemeVariant);
            Assert.Equal(ThemeVariant.Dark, inner.ActualThemeVariant); // nothing dark lands on the black surface

            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.White);
            ContrastSurface.MatchTheme(surface);
            Assert.Equal(ThemeVariant.Light, inner.ActualThemeVariant);

            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            ContrastSurface.MatchTheme(surface);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, inner.ActualThemeVariant); // inherited again: it follows the window
            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Light, inner.ActualThemeVariant);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_chosen_item_is_ringed_in_the_highlight_only_under_contrast_and_a_toggle_is_outlined() => headless.OnUiThread(() =>
    {
        var chosen = new Border { Width = 40, Height = 20 };
        var other = new Border { Width = 40, Height = 20 };
        var toggle = new Avalonia.Controls.Primitives.ToggleButton { Content = "On" };
        var rest = new SolidColorBrush(Colors.Gray);
        var window = Show(new StackPanel { Children = { chosen, other, toggle } }, ThemeVariant.Dark);
        try
        {
            ContrastCue.SetChosen(chosen, true);
            ContrastCue.SetChosen(other, false);
            ContrastCue.Toggle(toggle, true, rest, new Thickness(0.5));
            Assert.Equal(default, chosen.BoxShadow); // glass shows the chosen tint; no ring
            Assert.False(ContrastCue.ShowsRing(chosen));
            Assert.Same(rest, toggle.BorderBrush);
            Assert.Equal(new Thickness(0.5), toggle.BorderThickness);

            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            Assert.True(ContrastCue.ShowsRing(chosen));
            Assert.Equal(1, chosen.BoxShadow.Count);
            var ring = chosen.BoxShadow[0];
            Assert.True(ring.IsInset);
            Assert.Equal(ContrastCue.RingWidth, ring.Spread);
            Assert.Equal(HighContrastPalette.Black.Highlight, ring.Color);
            Assert.Equal(default, other.BoxShadow);
            ContrastCue.Toggle(toggle, true, rest, new Thickness(0.5));
            Assert.Equal(HighContrastPalette.Black.Highlight, BrushColor(toggle.BorderBrush));
            Assert.Equal(new Thickness(1), toggle.BorderThickness);
            ContrastCue.Toggle(toggle, false, rest, new Thickness(0.5));
            Assert.Equal(Colors.Transparent, BrushColor(toggle.BorderBrush));
            Assert.Equal(new Thickness(1), toggle.BorderThickness); // on and off are the same size: nothing moves

            ContrastCue.SetChosen(chosen, false);
            ContrastCue.SetChosen(other, true);
            Assert.Equal(default, chosen.BoxShadow);
            Assert.True(ContrastCue.ShowsRing(other));

            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            Assert.Equal(default, other.BoxShadow);
            ContrastCue.Toggle(toggle, false, rest, new Thickness(0.5));
            Assert.Same(rest, toggle.BorderBrush);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_system_setting_reaches_every_window_it_follows_and_a_refresh_reads_it_again() => headless.OnUiThread(() =>
    {
        HighContrastPalette? system = null;
        var follower = new SystemHighContrast(() => system);
        var first = new Window { Content = new Border() };
        var second = new Window { Content = new Border() };
        try
        {
            follower.Follow(first);
            Assert.Null(EnvironmentValues.GetAccessibilityHighContrast(first));
            system = HighContrastPalette.Black;
            follower.Follow(second);
            Assert.Same(HighContrastPalette.Black, EnvironmentValues.GetAccessibilityHighContrast(second));
            follower.Refresh();
            Assert.Same(HighContrastPalette.Black, EnvironmentValues.GetAccessibilityHighContrast(first));
            Assert.Same(HighContrastPalette.Black, EnvironmentValues.GetAccessibilityHighContrast((Control)first.Content!)); // inherited
            system = null;
            follower.Refresh();
            Assert.Null(EnvironmentValues.GetAccessibilityHighContrast(first));
            Assert.Null(EnvironmentValues.GetAccessibilityHighContrast(second));
        }
        finally
        {
            first.Close();
            second.Close();
        }
    });

    [Fact]
    public void A_workspace_gutter_draws_a_1_DIP_system_line_only_under_contrast_and_never_on_a_folded_side() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        Assert.False(rig.Splitter.ShowsContrastLine); // off unless the host asks: other dividers keep the edge their host draws
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, HighContrastPalette.Black);
        Assert.Null(rig.Splitter.ContrastLine);
        rig.Splitter.ShowsContrastLine = true;
        var line = Assert.IsType<Rect>(rig.Splitter.ContrastLine);
        Assert.Equal(1, line.Width);
        Assert.Equal(rig.Splitter.Bounds.Height, line.Height);
        Assert.Equal(Math.Floor(Splitter.DividerThickness / 2), line.X);

        var pixels = Pixels(rig.Window);
        var origin = rig.Splitter.TranslatePoint(new Point(line.X, 0), rig.Window)!.Value;
        var y = (int)(rig.Window.Bounds.Height / 2);
        var x = (int)Math.Round(origin.X);
        Assert.Equal(Colors.White, pixels(x, y));
        Assert.NotEqual(Colors.White, pixels(x - 2, y)); // one DIP, not a band

        rig.Value.Collapse(true);
        rig.Settle();
        rig.Splitter.Refresh();
        Assert.Null(rig.Splitter.ContrastLine); // the rail's own line is the edge

        rig.Value.Collapse(false);
        rig.Settle();
        rig.Splitter.Refresh();
        Assert.NotNull(rig.Splitter.ContrastLine);
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null);
        Assert.Null(rig.Splitter.ContrastLine);
    });

    [Fact]
    public void Under_contrast_the_grip_and_its_chips_are_solid_system_colours_and_their_glass_comes_back_after() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var grip = rig.Splitter.Grip;
        var glassBody = BrushColor(grip.Body.Background);
        var glassChip = BrushColor(rig.Splitter.TipChip.Background);
        Assert.NotEqual(default, grip.Body.BoxShadow);

        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, HighContrastPalette.Black);
        Assert.Equal(Colors.White, BrushColor(grip.Body.Background));
        Assert.Equal(HighContrastPalette.Black.Highlight, BrushColor(grip.Bright.Background));
        Assert.Equal(HighContrastPalette.Black.Highlight, BrushColor(grip.Ring.BorderBrush));
        Assert.Equal(default, grip.Body.BoxShadow);
        var chip = rig.Splitter.TipChip;
        Assert.Equal(Colors.Black, BrushColor(chip.Background));
        Assert.Equal(Colors.White, BrushColor(chip.BorderBrush));
        Assert.Equal(new Thickness(1), chip.BorderThickness);
        Assert.Equal(default, chip.BoxShadow);
        Assert.Equal(Colors.White, BrushColor(Assert.IsType<TextBlock>(chip.Child).Foreground));

        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null);
        Assert.Equal(glassBody, BrushColor(grip.Body.Background));
        Assert.Equal(glassChip, BrushColor(rig.Splitter.TipChip.Background));
        Assert.NotEqual(default, grip.Body.BoxShadow);
    });

    [Fact]
    public void A_popover_card_is_the_solid_system_surface_inside_a_system_line_and_its_content_matches_it() => headless.OnUiThread(() =>
    {
        var words = new TextBlock { Text = "Allow" };
        var card = new PopoverCard
        {
            Child = new Border { Width = 160, Height = 80, Child = words },
            ShowTip = false,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), Child = card, Padding = new Thickness(20) };
        var window = Show(host, ThemeVariant.Light);
        try
        {
            words.Foreground = PaneTokens.Text1.Brush(words);
            EnvironmentValues.SetAccessibilityHighContrast(window, HighContrastPalette.Black);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ContrastSurface.IsMatched(card));
            Assert.Equal(ThemeVariant.Dark, words.ActualThemeVariant);
            Assert.Equal(Colors.White, BrushColor(words.Foreground));
            var pixels = Pixels(window);
            var inside = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height - 6), window)!.Value;
            Assert.Equal(Colors.Black, pixels((int)inside.X, (int)inside.Y)); // solid: none of the grey behind shows
            var edge = card.TranslatePoint(new Point(card.Bounds.Width / 2, 0), window)!.Value;
            Assert.Equal(Colors.White, pixels((int)edge.X, (int)edge.Y)); // the 1 DIP system line

            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ContrastSurface.IsMatched(card));
            Assert.Equal(ThemeVariant.Light, words.ActualThemeVariant);
            Assert.Equal(PaneTokens.Text1.Resolve(ThemeVariant.Light), BrushColor(words.Foreground));
        }
        finally { window.Close(); }
    });
}
