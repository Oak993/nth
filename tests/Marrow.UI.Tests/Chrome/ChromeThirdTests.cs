using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), part 1 of 4 continued (R-TEST-3: a third file for
// the same Swift file): the layers WindowBackdrop draws (:201-273) and the theme tint every chrome glass surface
// carries (:151-185). Expected values come from those lines and the records:
//   :8 windowCornerRadius = 26; :71-73 overlay(o) = primaryText.opacity(o); :76 primaryText = labelColor (ED-051,
//   SwiftColor.LabelColor); :96 glassTint = ThemeManager.spec.surface; :211-215 the scrim; :224 isSolid = opacity >= 0.98;
//   :249-250 the solid base; :251-259 the scrim over the glass; :263-265 the tint at 0.12; :269-271 the one-point
//   strokeBorder (an inner stroke) at overlay(0.14); :180, :182 the capsule and circle glass tinted with glassTint at 0.3.
// Port shape read (R-VIEW-1, R-VIEW-11, OD-0005, OD-0014): each `shape.fill` and `shape.strokeBorder` of the backdrop is
// an Avalonia Border the size of the backdrop with a uniform 26 point corner radius (an Avalonia Border draws its border
// inside its bounds, the strokeBorder's inner stroke); `.opacity(x)` on a fill is the Border's Opacity. Over the desktop
// (no wallpaper, OD-0014) the glass is the window's own Acrylic (OD-0005, ADR-0002 Glass (a)), so the app paints nothing
// under the scrim. A chrome glass surface's theme tint is a Border behind the control filled with glassTint at 0.3, with a
// capsule's corner radius (half its height) or a circle's.
// Not asserted (dependency not in the tree, see the unit report): the app-drawn glass material over a wallpaper or the
// solid base and its .interactive() press response (OD-0005, ADR-0002 Glass (b)), `.continuous` corner curvature, and
// the frames of the 0.3 second wallpaper cross-fade.
[Collection("Headless")]
public sealed class ChromeThirdTests(HeadlessSession headless)
{
    private static readonly Point Away = new(395, 295);

    private static Window Show(Control content, ThemeVariant theme, double width = 400, double height = 300)
    {
        var window = new Window { Width = width, Height = height, Content = content, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        return control;
    }

    private static Color Effective(IBrush? brush)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static bool Same(Color expected, Color actual) =>
        (expected.R, expected.G, expected.B) == (actual.R, actual.G, actual.B) && Math.Abs(expected.A - actual.A) <= 1;

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1); // alpha x opacity may round either way by one step
    }

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static WriteableBitmap Picture() =>
        new(new PixelSize(8, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    // Every Border of the backdrop, in drawing order (the visual tree walked depth first, earlier siblings first).
    private static List<SwiftShapeBorder> Layers(WindowBackdrop backdrop) => VisualQuery.Descendants<SwiftShapeBorder>(backdrop).ToList(); // ED-181: the backdrop's layers are SwiftShapeBorders

    private static List<SwiftShapeBorder> Filled(WindowBackdrop backdrop) => Layers(backdrop).Where(b => b.Background is not null).ToList();

    private static void AssertWholeShape(SwiftShapeBorder layer, WindowBackdrop backdrop)
    {
        Assert.Equal(new Rect(backdrop.Bounds.Size), BoundsIn(layer, backdrop));
        Assert.Equal(new CornerRadius(26), layer.CornerRadius); // :218 RoundedRectangle(cornerRadius: Chrome.windowCornerRadius)
    }

    private static SwiftShapeBorder Tint(WindowBackdrop backdrop, ThemeVariant theme) =>
        Assert.Single(Filled(backdrop), b => Same(ThemeManager.Spec.Surface.Opacity(0.12).Resolve(theme), Effective(b.Background)) && b.Opacity == 1);

    private static SwiftShapeBorder Hairline(WindowBackdrop backdrop) =>
        Assert.Single(Layers(backdrop), b => b.BorderThickness != default);

    private static T WithTheme<T>(AppTheme theme, Func<T> body)
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = theme;
            return body();
        }
        finally
        {
            ThemeManager.Current = original;
        }
    }

    // ---- WindowBackdrop's tint and hairline, :263-271 ----

    [Theory]
    [Trait("Req", "PR-WIN-058")] // UI/Chrome/Chrome.swift:263-271 .overlay { shape.fill(Chrome.glassTint.opacity(0.12)) } .overlay { shape.strokeBorder(Chrome.overlay(0.14), lineWidth: 1) }
    [Trait("Req", "PR-THEME-038")]
    [InlineData(0.5, false)]
    [InlineData(0.5, true)]
    [InlineData(0.0, true)]
    [InlineData(1.0, false)]
    [InlineData(1.0, true)]
    public void The_window_backdrop_carries_the_theme_tint_at_0_12_then_a_one_point_inner_hairline_at_0_14(double setting, bool dark) => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var backdrop = new WindowBackdrop(opacity: setting);
            var window = Show(backdrop, theme);

            var tint = Tint(backdrop, theme);
            AssertWholeShape(tint, backdrop);
            var hairline = Hairline(backdrop);
            AssertWholeShape(hairline, backdrop);
            Assert.Equal(new Thickness(1), hairline.BorderThickness);
            AssertColor(SwiftColor.LabelColor.Opacity(0.14).Resolve(theme), Effective(hairline.BorderBrush));
            Assert.Null(hairline.Background); // a stroke only
            // Order: the base first, then the tint over it, then the hairline over everything.
            var layers = Layers(backdrop);
            Assert.True(layers.IndexOf(hairline) > layers.IndexOf(tint));
            Assert.Same(tint, Filled(backdrop)[^1]);
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-058")] // UI/Chrome/Chrome.swift:263-271: the tint and the hairline sit on the wallpaper as well
    public void Over_a_wallpaper_the_backdrop_still_carries_the_tint_and_the_hairline() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var backdrop = new WindowBackdrop(opacity: 0.6, wallpaper: Picture());
            var window = Show(backdrop, ThemeVariant.Dark);

            AssertWholeShape(Tint(backdrop, ThemeVariant.Dark), backdrop);
            AssertWholeShape(Hairline(backdrop), backdrop);
            window.Close();
            return 0;
        });
    });

    // ---- The scrim over the glass, :251-259 ----

    [Theory]
    [Trait("Req", "PR-WIN-059")] // UI/Chrome/Chrome.swift:255-258 `shape.fill(isDark ? Color.black : Color.white).opacity(Self.scrim(for: opacity, isDark: isDark))`
    [InlineData(0.5, true, 0.26)]
    [InlineData(0.5, false, 0.18)]
    [InlineData(0.25, true, 0.13)]
    [InlineData(0.0, false, 0.0)]
    [InlineData(0.9, true, 0.852)]
    public void Without_a_wallpaper_the_glass_is_covered_by_black_or_white_at_the_scrim(double setting, bool dark, double scrim) => headless.OnUiThread(() =>
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var backdrop = new WindowBackdrop(opacity: setting);
        var window = Show(backdrop, theme);

        var baseColour = dark ? Colors.Black : Colors.White;
        var layer = Assert.Single(Filled(backdrop), b => Effective(b.Background) == baseColour);
        AssertWholeShape(layer, backdrop);
        Assert.InRange(layer.Opacity, scrim - 1e-9, scrim + 1e-9);
        Assert.InRange(layer.Opacity, WindowBackdrop.Scrim(setting, dark) - 1e-12, WindowBackdrop.Scrim(setting, dark) + 1e-12);
        // Over the desktop the glass is the window's own Acrylic (OD-0005, OD-0014): the app paints nothing under the
        // scrim, so the scrim and the tint are the only fills.
        Assert.Equal(2, Filled(backdrop).Count);
        Assert.Same(layer, Filled(backdrop)[0]);
        Assert.Empty(VisualQuery.Descendants<Image>(backdrop));
        window.Close();
    });

    // ---- The solid end, :220-224, :249-250 ----

    [Theory]
    [Trait("Req", "PR-WIN-060")] // UI/Chrome/Chrome.swift:224 `let isSolid = opacity >= 0.98`, :249-250 `shape.fill(isDark ? Color.black : Color.white)`
    [InlineData(0.98, true)]
    [InlineData(0.98, false)]
    [InlineData(1.0, true)]
    [InlineData(7.0, false)]
    public void At_0_98_and_above_the_base_is_a_plain_black_or_white_fill_with_no_scrim(double setting, bool dark) => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var backdrop = new WindowBackdrop(opacity: setting);
            var window = Show(backdrop, theme);

            var filled = Filled(backdrop);
            Assert.Equal(2, filled.Count); // the plain base and the tint: no scrim layer
            var solid = filled[0];
            AssertWholeShape(solid, backdrop);
            Assert.Equal(dark ? Colors.Black : Colors.White, Effective(solid.Background));
            Assert.Equal(1, solid.Opacity);
            Assert.Same(Tint(backdrop, theme), filled[1]);
            AssertWholeShape(Hairline(backdrop), backdrop); // :266-268 the hairline sits on the solid fill as well
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-060")] // UI/Chrome/Chrome.swift:224: a greater-than-or-equal comparison against exactly 0.98
    public void Just_below_0_98_the_backdrop_is_still_glass_under_the_scrim() => headless.OnUiThread(() =>
    {
        var setting = 0.97999999;
        var backdrop = new WindowBackdrop(opacity: setting);
        var window = Show(backdrop, ThemeVariant.Dark);

        var scrim = Filled(backdrop)[0];
        Assert.Equal(Colors.Black, Effective(scrim.Background));
        Assert.InRange(scrim.Opacity, WindowBackdrop.Scrim(setting, true) - 1e-12, WindowBackdrop.Scrim(setting, true) + 1e-12);
        Assert.True(scrim.Opacity < 1);
        window.Close();
    });

    // ---- The wallpaper branch, :226-248 ----

    [Theory]
    [Trait("Req", "PR-WIN-061")] // UI/Chrome/Chrome.swift:226-240: no scrim in this branch; the picture clipped to the shape (:240 .clipShape(shape))
    [InlineData(0.3, true)]
    [InlineData(0.3, false)]
    [InlineData(1.0, true)]
    public void Over_a_wallpaper_no_scrim_is_drawn_and_the_picture_is_clipped_to_the_rounded_shape(double setting, bool dark) => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var backdrop = new WindowBackdrop(opacity: setting, wallpaper: Picture());
            var window = Show(backdrop, theme);

            Assert.DoesNotContain(Layers(backdrop), b => b.Background is not null && Effective(b.Background) is { } c && (c.R, c.G, c.B) is (0, 0, 0) or (255, 255, 255));
            var fills = Filled(backdrop);
            Assert.Same(Tint(backdrop, theme), Assert.Single(fills)); // the tint is the only fill
            var image = Assert.Single(VisualQuery.Descendants<Image>(backdrop));
            var clip = Assert.IsType<SwiftShapeBorder>(image.GetVisualParent());
            AssertWholeShape(clip, backdrop);
            Assert.True(clip.ClipToBounds);
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-061")] // UI/Chrome/Chrome.swift:205-207 the two inputs; :226 `if let wallpaper`, re-read when the parent passes new ones
    public void Setting_a_wallpaper_on_a_drawn_backdrop_brings_the_picture_in_at_the_new_setting() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // :262 the change cross-fades
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var backdrop = new WindowBackdrop(opacity: 0.5);
        var window = Show(backdrop, ThemeVariant.Dark);
        Assert.Empty(VisualQuery.Descendants<Image>(backdrop));

        var picture = Picture();
        backdrop.Update(opacity: 0.4, wallpaper: picture);
        Dispatcher.UIThread.RunJobs();

        var image = Assert.Single(VisualQuery.Descendants<Image>(backdrop));
        Assert.Same(picture, image.Source);
        Assert.Equal(0.4, image.Opacity);
        window.Close();
    });

    // ---- The chrome glass surface's tint, :126-143, :162-183 ----

    private static Border GlassTint(Control root, ThemeVariant theme) =>
        Assert.Single(VisualQuery.Descendants<Border>(root), b => Same(ThemeManager.Spec.Surface.Opacity(0.3).Resolve(theme), Effective(b.Background)));

    [Theory]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:165 Capsule(style: .continuous), :180 .glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive(), in: shape)
    [Trait("Req", "PR-WIN-181")]
    [Trait("Req", "PR-THEME-038")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_chrome_capsule_is_washed_with_the_theme_surface_at_0_3_in_its_capsule_shape(bool dark) => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var capsule = new ChromeCapsule(content: () => new LayoutProbe(new Size(56, 28)));
            var window = Show(TopLeft(capsule), theme);

            var glass = GlassTint(capsule, theme);
            Assert.Equal(new Rect(0, 0, 62, 32), BoundsIn(glass, capsule));
            Assert.Equal(new CornerRadius(16), glass.CornerRadius); // a capsule: half its height
            Assert.False(glass.IsHitTestVisible); // presses go to the control or to the no-drag region behind it
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:141-143 chromeGlassCircle, :167 Circle(); ChromeCircleButton :429
    [Trait("Req", "PR-WIN-186")]
    public void A_circle_button_is_washed_with_the_theme_surface_at_0_3_in_a_32_point_circle() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
            var window = Show(TopLeft(control), ThemeVariant.Dark);

            var glass = GlassTint(control, ThemeVariant.Dark);
            Assert.Equal(new Rect(0, 0, 32, 32), BoundsIn(glass, control));
            Assert.Equal(new CornerRadius(16), glass.CornerRadius);
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:180: the tint is exactly 0.3 and does not vary with hover
    [Trait("Req", "PR-WIN-189")]
    public void The_glass_tint_does_not_change_under_the_pointer() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
            var control = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { });
            var window = Show(TopLeft(control), ThemeVariant.Light);
            var glass = GlassTint(control, ThemeVariant.Light);
            Assert.Equal(new CornerRadius(16), glass.CornerRadius);
            Assert.Equal(control.Bounds.Size, glass.Bounds.Size);

            var centre = BoundsIn(control, window).Center;
            window.MouseMove(centre, RawInputModifiers.None);
            for (var i = 0; i < 30; i++)
            {
                (SwiftClock.Provider.Current as ManualTimeProvider)?.Advance(TimeSpan.FromMilliseconds(10));
                frames.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Same(glass, GlassTint(control, ThemeVariant.Light));
            window.MouseMove(Away, RawInputModifiers.None);
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:137-139 chromeGlassFieldCapsule, :182 the same 0.3 tint without .interactive(), :864
    [Trait("Req", "PR-THEME-038")]
    public void The_chrome_search_field_is_washed_with_the_theme_surface_at_0_3_in_its_capsule() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var control = new ChromeSearchField(query: Binding<string>.Constant(""));
            var window = Show(TopLeft(control), ThemeVariant.Dark);

            var glass = GlassTint(control, ThemeVariant.Dark);
            Assert.Equal(new Rect(0, 0, 200, 32), BoundsIn(glass, control));
            Assert.Equal(new CornerRadius(16), glass.CornerRadius);
            window.Close();
            return 0;
        });
    });
}
