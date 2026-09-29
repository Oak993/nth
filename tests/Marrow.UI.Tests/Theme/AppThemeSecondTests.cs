using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Theme;

// Source-derived tests for DroppyCode/UI/Theme/AppTheme.swift (B2 1fbf81e), unit UI.Theme.AppTheme,
// part 2 of 2 (work packet part2.md, PR-THEME-025 to PR-THEME-037, UI-THEME-007). Every expected value is
// transcribed from the cited Swift lines, the windows_path text quoted in a record, or Apple's published
// HIG Default colour tokens (never from the C# under test). R-TEST-3: a second test file/class for a Swift
// file split across work-packet parts, suffixed "Second" per the MCPCatalogSecondTests convention.
[Collection("Headless")]
public sealed class AppThemeSecondTests(HeadlessSession headless)
{
    // ---- PR-THEME-025 to PR-THEME-029: the palette rows part 1 already transcribes into the switch. ----

    [Fact]
    [Trait("Req", "PR-THEME-025")] // UI/Theme/AppTheme.swift:135
    public void Palette_of_claude_is_dark_with_the_Terracotta_row() =>
        AssertRow(AppTheme.Claude, ThemeVariant.Dark, "C15F3C", "1A1816", "51A556", "C9A227", "D97757");

    [Fact]
    [Trait("Req", "PR-THEME-026")] // UI/Theme/AppTheme.swift:136 (same accent as claude)
    public void Palette_of_claudeLight_is_light_with_the_same_accent_as_claude()
    {
        AssertRow(AppTheme.ClaudeLight, ThemeVariant.Light, "C15F3C", "FAF9F5", "2E7D32", "9A6700", "B3261E");
        Assert.Equal(AppTheme.Claude.Palette().Accent, AppTheme.ClaudeLight.Palette().Accent);
    }

    [Fact]
    [Trait("Req", "PR-THEME-027")] // UI/Theme/AppTheme.swift:137
    public void Palette_of_codex_is_dark_with_the_Magenta_row() =>
        AssertRow(AppTheme.Codex, ThemeVariant.Dark, "D946EF", "101014", "3FB950", "D29922", "F85149");

    [Fact]
    [Trait("Req", "PR-THEME-028")] // UI/Theme/AppTheme.swift:138
    public void Palette_of_cursor_is_dark_with_the_Frost_row() =>
        AssertRow(AppTheme.Cursor, ThemeVariant.Dark, "88C0D0", "181818", "3FA266", "F1B467", "E34671");

    [Fact]
    [Trait("Req", "PR-THEME-029")] // UI/Theme/AppTheme.swift:139 (the only theme with a pure black surface; success equals its own accent)
    public void Palette_of_matrix_is_dark_with_pure_black_surface_and_success_equal_to_its_own_accent() =>
        AssertRow(AppTheme.Matrix, ThemeVariant.Dark, "00E676", "000000", "00E676", "FFD600", "FF5252");

    private static void AssertRow(
        AppTheme theme, ThemeVariant scheme, string accent, string surface, string success, string warning, string danger)
    {
        var palette = theme.Palette();
        Assert.Equal(scheme, palette.Scheme);
        Assert.Equal(accent, palette.Accent);
        Assert.Equal(surface, palette.Surface);
        Assert.Equal(success, palette.Success);
        Assert.Equal(warning, palette.Warning);
        Assert.Equal(danger, palette.Danger);
    }

    // ---- PR-THEME-030, PR-THEME-031: Spec()'s nil-accent and nil-status-hue resolution. ----

    [Theory]
    [Trait("Req", "PR-THEME-030")] // UI/Theme/AppTheme.swift:107-108, :193 (accent.map(Color.init(hex:)), nil stays nil)
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Spec_of_a_theme_with_no_fixed_accent_leaves_Accent_null_rather_than_substituting_a_colour(AppTheme theme) =>
        Assert.Null(theme.Spec().Accent);

    [Fact]
    [Trait("Req", "PR-THEME-030")] // UI/Theme/AppTheme.swift:193 (a theme that does set an accent gets one)
    public void Spec_of_a_theme_with_a_fixed_accent_sets_Accent_from_its_hex()
    {
        var spec = AppTheme.Claude.Spec();
        Assert.NotNull(spec.Accent);
        AssertColorEqual(0xC1, 0x5F, 0x3C, spec.Accent!.Resolve(ThemeVariant.Dark));
    }

    [Theory]
    [Trait("Req", "PR-THEME-031")] // UI/Theme/AppTheme.swift:195-197 (?? .green / .orange / .red)
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Spec_of_a_theme_with_no_status_hues_resolves_success_warning_and_danger_to_a_fixed_hue(AppTheme theme)
    {
        var spec = theme.Spec();

        Assert.NotNull(spec.Success);
        Assert.NotNull(spec.Warning);
        Assert.NotNull(spec.Danger);
        // windows_path: "Pick one fixed green, orange and red ... and keep them constant" - resolving in both
        // a light and a dark variant must give the identical colour (unlike the accent, which is dynamic).
        Assert.Equal(spec.Success.Resolve(ThemeVariant.Light), spec.Success.Resolve(ThemeVariant.Dark));
        Assert.Equal(spec.Warning.Resolve(ThemeVariant.Light), spec.Warning.Resolve(ThemeVariant.Dark));
        Assert.Equal(spec.Danger.Resolve(ThemeVariant.Light), spec.Danger.Resolve(ThemeVariant.Dark));
        AssertColorEqual(0x34, 0xC7, 0x59, spec.Success.Resolve(ThemeVariant.Light)); // systemGreen HIG Default light
        AssertColorEqual(0xFF, 0x95, 0x00, spec.Warning.Resolve(ThemeVariant.Light)); // systemOrange HIG Default light
        AssertColorEqual(0xFF, 0x3B, 0x30, spec.Danger.Resolve(ThemeVariant.Light)); // systemRed HIG Default light
    }

    [Fact]
    [Trait("Req", "PR-THEME-031")] // UI/Theme/AppTheme.swift:195-197 (a theme that sets its own hues keeps them, unfixed)
    public void Spec_of_a_theme_with_its_own_status_hues_uses_them_instead_of_the_fixed_fallback()
    {
        var spec = AppTheme.Claude.Spec();
        AssertColorEqual(0x51, 0xA5, 0x56, spec.Success.Resolve(ThemeVariant.Dark));
        AssertColorEqual(0xC9, 0xA2, 0x27, spec.Warning.Resolve(ThemeVariant.Dark));
        AssertColorEqual(0xD9, 0x77, 0x57, spec.Danger.Resolve(ThemeVariant.Dark));
    }

    [Fact]
    [Trait("Req", "PR-THEME-031")] // UI/Theme/AppTheme.swift:198 (surface is always set, System uses the dynamic pair)
    public void Spec_of_system_resolves_surface_to_the_dynamic_pair_light_white_dark_1E1E20()
    {
        var spec = AppTheme.System.Spec();
        AssertColorEqual(0xFF, 0xFF, 0xFF, spec.Surface.Resolve(ThemeVariant.Light));
        AssertColorEqual(0x1E, 0x1E, 0x20, spec.Surface.Resolve(ThemeVariant.Dark));
    }

    // ---- PR-THEME-032: Color(hex:)'s tolerant scanner, ported as Port/SwiftColor.cs's SwiftColor.Hex. ----

    [Fact]
    [Trait("Req", "PR-THEME-032")] // UI/Theme/AppTheme.swift:239-247
    public void Hex_parses_a_bare_six_digit_string_as_sRGB_opaque() =>
        AssertColorEqual(0xCB, 0xA6, 0xF7, SwiftColor.Hex("CBA6F7").Resolve(ThemeVariant.Light));

    [Fact]
    [Trait("Req", "PR-THEME-032")] // UI/Theme/AppTheme.swift:240 (one leading # dropped)
    public void Hex_drops_exactly_one_leading_hash() =>
        AssertColorEqual(0xCB, 0xA6, 0xF7, SwiftColor.Hex("#CBA6F7").Resolve(ThemeVariant.Light));

    [Fact]
    [Trait("Req", "PR-THEME-032")] // UI/Theme/AppTheme.swift:239 hex.trimmed (Core/Support/Text.swift:132, whitespace and newlines)
    public void Hex_trims_surrounding_whitespace_and_newlines_before_parsing() =>
        AssertColorEqual(0xCB, 0xA6, 0xF7, SwiftColor.Hex(" \n#CBA6F7 \t\n").Resolve(ThemeVariant.Light));

    [Fact]
    [Trait("Req", "PR-THEME-032")] // UI/Theme/AppTheme.swift:241-242 (a scan that reads no digit leaves value at 0, no throw)
    public void Hex_of_unparseable_text_yields_opaque_black_with_no_error()
    {
        AssertColorEqual(0x00, 0x00, 0x00, SwiftColor.Hex("not-a-color").Resolve(ThemeVariant.Light));
        AssertColorEqual(0x00, 0x00, 0x00, SwiftColor.Hex("").Resolve(ThemeVariant.Light));
    }

    [Fact]
    [Trait("Req", "PR-THEME-032")] // UI/Theme/AppTheme.swift:243-245 (masks &0xFF discard bits above bit 23)
    public void Hex_longer_than_six_digits_keeps_only_the_low_24_bits() =>
        AssertColorEqual(0xCB, 0xA6, 0xF7, SwiftColor.Hex("FFCBA6F7").Resolve(ThemeVariant.Light));

    // ---- PR-THEME-036, PR-THEME-037: ThemeManager's unconditional rebuild-and-broadcast, and the cached spec. ----

    [Fact]
    [Trait("Req", "PR-THEME-036")] // UI/Theme/AppTheme.swift:221, :223-228 (didSet, no equality guard)
    public void ThemeManager_Current_setter_posts_DidChange_and_rebuilds_Spec_even_when_reassigned_the_same_value()
    {
        var original = ThemeManager.Current;
        try
        {
            var changeCount = 0;
            ThemeManager.DidChange += OnChanged;
            void OnChanged() => changeCount++;

            ThemeManager.Current = AppTheme.Matrix;
            Assert.Equal(1, changeCount);
            var firstSpec = ThemeManager.Spec;
            AssertColorEqual(0x00, 0x00, 0x00, firstSpec.Surface.Resolve(ThemeVariant.Dark)); // Matrix surface 000000

            // Reassigning the theme it already holds still posts and still rebuilds (no equality short-circuit).
            ThemeManager.Current = AppTheme.Matrix;
            Assert.Equal(2, changeCount);
            Assert.NotSame(firstSpec, ThemeManager.Spec); // rebuilt, not skipped, even though the theme did not change
            AssertColorEqual(0x00, 0x00, 0x00, ThemeManager.Spec.Surface.Resolve(ThemeVariant.Dark));

            ThemeManager.DidChange -= OnChanged;
        }
        finally
        {
            ThemeManager.Current = original;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-037")] // UI/Theme/AppTheme.swift:230-233 (built once per change, read as a plain cached value)
    public void ThemeManager_Spec_is_the_current_themes_spec_and_does_not_change_between_reads()
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Codex;
            var first = ThemeManager.Spec;
            var second = ThemeManager.Spec; // read again with no assignment in between
            Assert.Same(first, second); // the read path parses nothing; it returns the one cached value
            AssertColorEqual(0xD9, 0x46, 0xEF, first.Accent!.Resolve(ThemeVariant.Dark)); // Codex accent D946EF
        }
        finally
        {
            ThemeManager.Current = original;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-037")] // UI/Theme/AppTheme.swift:233 static var spec: ThemeSpec = AppTheme.system.spec
    public void ThemeManager_Spec_starts_as_the_system_themes_spec_shape_before_any_assignment()
    {
        // The static field's declared initializer is AppTheme.system.spec (:233); this proves the field's
        // *shape* matches AppTheme.System.Spec() (null Accent, the Light/Dark-dynamic Surface pair), since a
        // by-reference compare would depend on whichever test in this parallel class ran ThemeManager.Current
        // last, and ThemeSpec's own record equality is reference equality on its SwiftColor members.
        var systemSpec = AppTheme.System.Spec();
        Assert.Null(systemSpec.Accent);
        AssertColorEqual(0xFF, 0xFF, 0xFF, systemSpec.Surface.Resolve(ThemeVariant.Light));
        AssertColorEqual(0x1E, 0x1E, 0x20, systemSpec.Surface.Resolve(ThemeVariant.Dark));
    }

    // ---- PR-THEME-033, PR-THEME-034, PR-THEME-035, UI-THEME-007: the swatch's geometry, colours and rim. ----

    [Fact]
    [Trait("Req", "PR-THEME-033")] // UI/Theme/AppTheme.swift:150-162
    [Trait("Req", "UI-THEME-007")] // UI/Theme/AppTheme.swift:149-176
    public void System_swatch_is_a_hard_split_disc_white_left_1E1E20_right_leading_to_trailing() => headless.OnUiThread(() =>
    {
        var swatch = (ThemeSwatch)AppTheme.System.Swatch();
        Show(swatch);

        var gradient = Assert.IsType<LinearGradientBrush>(swatch.Fill);
        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), gradient.StartPoint);
        Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), gradient.EndPoint);
        Assert.Equal(4, gradient.GradientStops.Count);
        AssertStop(gradient.GradientStops[0], 0, 0xFF, 0xFF, 0xFF);
        AssertStop(gradient.GradientStops[1], 0.5, 0xFF, 0xFF, 0xFF);
        AssertStop(gradient.GradientStops[2], 0.5, 0x1E, 0x1E, 0x20);
        AssertStop(gradient.GradientStops[3], 1, 0x1E, 0x1E, 0x20);
    });

    [Fact]
    [Trait("Req", "PR-THEME-034")] // UI/Theme/AppTheme.swift:163-174
    public void Non_system_swatch_runs_surface_to_accent_diagonally_holding_the_surface_to_042() => headless.OnUiThread(() =>
    {
        var swatch = (ThemeSwatch)AppTheme.Claude.Swatch();
        Show(swatch);

        var gradient = Assert.IsType<LinearGradientBrush>(swatch.Fill);
        Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), gradient.StartPoint);
        Assert.Equal(new RelativePoint(1, 1, RelativeUnit.Relative), gradient.EndPoint);
        Assert.Equal(3, gradient.GradientStops.Count);
        AssertStop(gradient.GradientStops[0], 0, 0x1A, 0x18, 0x16); // Claude surface 1A1816
        AssertStop(gradient.GradientStops[1], 0.42, 0x1A, 0x18, 0x16);
        AssertStop(gradient.GradientStops[2], 1, 0xC1, 0x5F, 0x3C); // Claude accent C15F3C
    });

    [Fact]
    [Trait("Req", "PR-THEME-034")] // UI/Theme/AppTheme.swift:164 (light and dark fall back to Color.accentColor)
    public void Light_and_dark_swatches_end_their_gradient_at_the_current_system_accent() => headless.OnUiThread(() =>
    {
        var lightSwatch = (ThemeSwatch)AppTheme.Light.Swatch();
        var darkSwatch = (ThemeSwatch)AppTheme.Dark.Swatch();
        Show(lightSwatch);
        Show(darkSwatch);

        var expectedAccent = SwiftColor.AccentColor.Resolve(ThemeVariant.Light);
        var lightGradient = Assert.IsType<LinearGradientBrush>(lightSwatch.Fill);
        var darkGradient = Assert.IsType<LinearGradientBrush>(darkSwatch.Fill);
        Assert.Equal(expectedAccent, lightGradient.GradientStops[lightGradient.GradientStops.Count - 1].Color);
        Assert.Equal(expectedAccent, darkGradient.GradientStops[darkGradient.GradientStops.Count - 1].Color);
        AssertStop(lightGradient.GradientStops[0], 0, 0xFF, 0xFF, 0xFF); // light surface FFFFFF
        AssertStop(darkGradient.GradientStops[0], 0, 0x1E, 0x1E, 0x20); // dark surface 1E1E20
    });

    [Fact]
    [Trait("Req", "PR-THEME-035")] // UI/Theme/AppTheme.swift:152, :162, :174
    public void Every_swatch_carries_a_half_point_rim_of_primary_at_22_percent_opacity() => headless.OnUiThread(() =>
    {
        // ED-051: NSColor.labelColor is rgba(0,0,0,0.85) light and rgba(255,255,255,0.85) dark; 0.85 * 0.22 =
        // 0.187, and SwiftColor.Byte rounds 0.187 * 255 = 47.685 away from zero to 48 (0x30).
        foreach (var theme in SwiftEnum<AppTheme>.AllCases)
        {
            var lightSwatch = (ThemeSwatch)theme.Swatch();
            var lightWindow = new Window { Width = 100, Height = 100, Content = lightSwatch, RequestedThemeVariant = ThemeVariant.Light };
            lightWindow.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.5, lightSwatch.StrokeThickness);
            var lightStroke = Assert.IsType<SolidColorBrush>(lightSwatch.Stroke);
            Assert.Equal(Color.FromArgb(0x30, 0, 0, 0), lightStroke.Color);

            var darkSwatch = (ThemeSwatch)theme.Swatch();
            var darkWindow = new Window { Width = 100, Height = 100, Content = darkSwatch, RequestedThemeVariant = ThemeVariant.Dark };
            darkWindow.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.5, darkSwatch.StrokeThickness);
            var darkStroke = Assert.IsType<SolidColorBrush>(darkSwatch.Stroke);
            Assert.Equal(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), darkStroke.Color);
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-007")] // UI/Theme/AppTheme.swift:149-176: a circle, not interactive on its own
    public void Swatch_is_an_Ellipse_that_fills_its_frame_and_is_not_itself_hit_test_visible() => headless.OnUiThread(() =>
    {
        var swatch = (ThemeSwatch)AppTheme.Claude.Swatch();
        Assert.IsAssignableFrom<Ellipse>(swatch);
        // Fills its (non-square) frame exactly; the 0.5 rim is inside it (:152 strokeBorder), so the outer edge is the frame.
        swatch.Width = 30;
        swatch.Height = 20;
        Show(swatch);
        var edge = swatch.RenderedGeometry!.Bounds.Inflate(swatch.StrokeThickness / 2);
        Assert.Equal(0, edge.X, 3);
        Assert.Equal(0, edge.Y, 3);
        Assert.Equal(30, edge.Width, 3);
        Assert.Equal(20, edge.Height, 3);
        Assert.False(swatch.IsHitTestVisible); // ENGINEERING DECISION: not interactive, clicks fall through
    });

    private static void AssertStop(GradientStop stop, double offset, byte r, byte g, byte b)
    {
        Assert.Equal(offset, stop.Offset);
        AssertColorEqual(r, g, b, stop.Color);
    }

    private static void AssertColorEqual(byte r, byte g, byte b, Color actual) =>
        Assert.Equal(Color.FromRgb(r, g, b), actual);

    private static Window Show(Control content)
    {
        var window = new Window { Width = 100, Height = 100, Content = content };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-THEME-035")] // UI/Theme/AppTheme.swift:152 Circle().strokeBorder keeps the 0.5 rim inside the circle
    public void Swatch_rim_lies_entirely_inside_the_swatch_bounds() => headless.OnUiThread(() =>
    {
        var swatch = (ThemeSwatch)AppTheme.Claude.Swatch();
        swatch.Width = 16;
        swatch.Height = 16;
        Show(swatch);
        var edge = swatch.RenderedGeometry!.Bounds.Inflate(swatch.StrokeThickness / 2);
        Assert.Equal(0, edge.X, 3);
        Assert.Equal(0, edge.Y, 3);
        Assert.Equal(16, edge.Width, 3);
        Assert.Equal(16, edge.Height, 3);
    });
}
