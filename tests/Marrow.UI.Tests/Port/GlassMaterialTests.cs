// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184.
// OD-0005: the app-drawn material must frost its own backdrop. Pixel thresholds below are
// engineering acceptance criteria for that requirement, not measurements of macOS (OD-0001).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182 .regular frosts the in-window backdrop (OD-0005)
    public void FineTextBehindGlassLosesContrast() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern();
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        using var scene = new Scene(backdrop, glass);
        var pixels = scene.Pixels();
        var contrast = Enumerable.Range(90, 60).Select(x => pixels(x, 80).R).ToArray();
        Assert.True(contrast.Max() - contrast.Min() < 64, "Fine black/white backdrop strokes must lose at least 75% of their contrast.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182 .regular (OD-0005 in-window frost)
    public void BlurSpreadsAnIsolatedBrightStroke() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { IsolatedStroke = true };
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var pixels = scene.Pixels();
        Assert.True(pixels(124, 80).R > pixels(96, 80).R + 2, "The bright stroke must spread into neighbouring backdrop pixels.");
        Assert.True(pixels(128, 80).R < 240, "The stroke must be softened rather than passed through.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:180 tint(color.opacity(0.3)); :166-167 Circle()
    public void TintIsAppliedAndCircleIsClipped() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { Flat = true };
        var glass = Surface(GlassShape.Circle);
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.Circle).Tint(new SwiftColor(1, 0, 0), 0.3));
        using var scene = new Scene(backdrop, glass);
        var pixels = scene.Pixels();
        Assert.True(pixels(128, 80).R > pixels(128, 80).G + 50, "The exact source tint opacity must affect the material.");
        Assert.Equal(pixels(10, 10), pixels(49, 21)); // outside the centred circle, inside its rectangular bounds
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; OD-0005 LIVE app content
    public void BackdropChangesReachTheMaterial() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern { Flat = true };
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var before = scene.Pixels()(128, 80);
        backdrop.Blue = true;
        backdrop.InvalidateVisual();
        var after = scene.Pixels()(128, 80);
        Assert.True(after.B > before.B + 40, "Changing app content behind glass must update its sampled backdrop.");
        Assert.True(after.R < 240, "The material must continue to frost the changed backdrop.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; F.GlassMaterial performance guard
    public void IdleFramesDoNotRerecordTheWindowBackdrop() => headless.OnUiThread(() =>
    {
        var backdrop = new Pattern();
        using var scene = new Scene(backdrop, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        _ = scene.Pixels();
        var renders = backdrop.RenderCount;
        for (var i = 0; i < 8; i++) Scene.Drain();
        Assert.Equal(renders, backdrop.RenderCount);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:173-182 glass removed on an existing glass panel
    public void RemovingEffectRestoresTheUnfilteredBackdrop() => headless.OnUiThread(() =>
    {
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        using var scene = new Scene(new Pattern(), glass);
        _ = scene.Pixels();
        SwiftGlass.SetEffect(glass, null);
        var pixels = scene.Pixels();
        Assert.Equal((byte)255, pixels(100, 80).R);
        Assert.Equal((byte)0, pixels(102, 80).R);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:171-182 content stays above the glass
    public void ForegroundIsSharpAndMaterialDoesNotConsumeInput() => headless.OnUiThread(() =>
    {
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        glass.Child = new Border { Width = 4, Height = 24, Background = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        using var scene = new Scene(new Pattern { Flat = true }, glass);
        var pixels = scene.Pixels();
        Assert.Equal((byte)255, pixels(128, 80).R);
        Assert.True(pixels(132, 80).R < 200);
        var pressed = 0;
        glass.PointerPressed += (_, _) => pressed++;
        scene.Window.MouseDown(new Point(128, 80), Avalonia.Input.MouseButton.Left);
        scene.Window.MouseUp(new Point(128, 80), Avalonia.Input.MouseButton.Left);
        Assert.Equal(1, pressed);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:180 .interactive(); :182 non-interactive
    public void InteractiveGlassRespondsToHoverAndPress() => headless.OnUiThread(() =>
    {
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        SwiftGlass.SetEffect(glass, SwiftGlass.GetEffect(glass)!.Interactive());
        using var scene = new Scene(new Pattern { Flat = true }, glass);
        var idle = scene.Pixels()(128, 80).R;
        scene.Window.MouseMove(new Point(128, 80));
        var hovered = scene.Pixels()(128, 80).R;
        scene.Window.MouseDown(new Point(128, 80), Avalonia.Input.MouseButton.Left);
        var pressed = scene.Pixels()(128, 80).R;
        scene.Window.MouseUp(new Point(128, 80), Avalonia.Input.MouseButton.Left);
        scene.Window.MouseMove(new Point(10, 10));
        var restored = scene.Pixels()(128, 80).R;
        Assert.True(hovered > idle);
        Assert.True(pressed > hovered);
        Assert.Equal(idle, restored);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:180 .interactive(); addendum pointer-following highlight
    public void InteractiveHighlightFollowsThePointer() => headless.OnUiThread(() =>
    {
        var glass = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous));
        SwiftGlass.SetEffect(glass, SwiftGlass.GetEffect(glass)!.Interactive());
        using var scene = new Scene(new Pattern { Flat = true }, glass);
        scene.Window.MouseMove(new Point(90, 80));
        var first = scene.Pixels();
        scene.Window.MouseMove(new Point(170, 80));
        var second = scene.Pixels();
        Assert.True(first(90, 80).R > second(90, 80).R + 2);
        Assert.True(second(170, 80).R > first(170, 80).R + 2);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; addendum specular rim; OD-0107 the Mac frames' rim (ED-356)
    public void AFlatBackdropHasASpecularRimBrightestAtTheTopAndALightInnerEdge() => headless.OnUiThread(() =>
    {
        // ED-356 changed the design this test measured (P1's light from the bottom right, a dark inner shade at the top left): the Mac frames
        // (the composer pill, the title capsules) show the rim brightest at the top, bright at the bottom, faint at the sides, and a light inner edge under
        // the top rim, not a shade. The rim is now each edge's first pixel; the old points (138 and 27) lie 1.5 and 7.5 pt in.
        using var scene = new Scene(new Pattern { Flat = true }, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous), GlassLook.Release));
        var pixels = scene.Pixels();
        var centre = pixels(128, 80).R;
        Assert.True(pixels(128, 20).R > centre + 8, "The light-facing (top) rim must pick up a specular highlight.");
        Assert.True(pixels(128, 139).R > pixels(128, 133).R + 8, "The bottom rim must pick up a specular highlight.");
        Assert.True(pixels(128, 21).R > pixels(128, 29).R + 2, "The inner edge under the top rim must be lighter.");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:179-182; addendum edge refraction; ED-356 bend 40 / fall-off 11 (rizzytoday's map)
    public void EdgeRefractionBendsABroadGradientOutward() => headless.OnUiThread(() =>
    {
        using var scene = new Scene(new Pattern { Gradient = true }, Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous), GlassLook.Release));
        var pixels = scene.Pixels("glass-material-refraction.png");
        // An affine gradient survives blur. ED-356 replaced the 0.18 dark frost (alone ~50/~170 here, the old thresholds 42/180) with
        // glass-g3's gain law (c + 0.95 c (1 - c)^2): undisplaced it gives 86 at x 54, 210 at x 201 and 158 in the middle.
        // These thresholds still require a displacement of at least 9 px beyond the body, rim and inner light.
        Assert.True(pixels(54, 80).R < 75, "The left edge must sample farther left than the plain film.");
        Assert.True(pixels(201, 80).R > 217, "The right edge must sample farther right than the plain film.");
        Assert.InRange(pixels(128, 80).R, (byte)148, (byte)168); // deep interior retains the undistorted gradient (128 lifted to 158)
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:165 Capsule; ThreadChangesTab.swift:15,38 top-only corners
    public void UnevenCornersAreKeptAndVisibilityRemovesTheUnderlay() => headless.OnUiThread(() =>
    {
        var glass = Surface(GlassShape.RoundedRectangle(12, RoundedCornerStyle.Continuous), GlassLook.Release); // OD-0147: the canvas's shadow darkens the cut corner
        glass.CornerRadius = new CornerRadius(12, 12, 0, 0);
        using var scene = new Scene(new Pattern { Flat = true }, glass);
        var pixels = scene.Pixels();
        Assert.Equal(pixels(10, 10), pixels(49, 21));
        Assert.NotEqual(pixels(10, 10), pixels(49, 138));
        glass.IsVisible = false;
        pixels = scene.Pixels();
        Assert.Equal(pixels(10, 10), pixels(128, 80));
    });

    [Theory]
    [InlineData(8, true)]
    [InlineData(2, false)]
    [Trait("Req", "PR-WIN-180")] // App/Views/Composer/ComposerView.swift:23 spacing 8; ChatView.swift:1123/DiffInspector.swift:25 spacing 2
    public void ContainerSpacingDeterminesWhetherNearbyGlassJoins(double spacing, bool joins) => headless.OnUiThread(() =>
    {
        // OD-0147: the release material, whose gap shows the pattern untouched (the canvas's shadow darkens it); the joining is the same for both
        var left = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous), GlassLook.Release);
        var right = Surface(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous), GlassLook.Release);
        left.Width = right.Width = 76;
        left.HorizontalAlignment = right.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        left.Margin = new Thickness(48, 0, 0, 0);
        right.Margin = new Thickness(130, 0, 0, 0); // six-DIP gap
        var container = new Panel { Children = { left, right } };
        var setSpacing = typeof(SwiftGlass).GetMethod("SetContainerSpacing");
        Assert.NotNull(setSpacing);
        setSpacing.Invoke(null, [container, spacing]);
        using var scene = new Scene(new Pattern(), container);
        var pixels = scene.Pixels();
        // ED-356: the gap's first pixels (124 and, through the smooth union's neck, 125) carry the left piece's contact line, so the gap is
        // read at 127 and 128, mid-gap (black and white in the pattern, as 126 and 124 were).
        var contrast = Math.Abs(pixels(127, 80).R - pixels(128, 80).R);
        Assert.True(joins ? contrast < 64 : contrast == 255, $"Source container spacing {spacing} must govern joining the six-DIP gap: " + string.Join(" ", Enumerable.Range(120, 14).Select(x => $"{x}:{pixels(x, 80).R}")));
    });

    // ---- PR-CHAT-171 / UI-CHAT-073: a container draws every piece from the opacity and place the piece has NOW ---------------------
    // A soft appear (Chrome.swift:933-960) moves only the Opacity and the RenderTransform of a parent of the glass piece; no layout pass follows. The container's one
    // material pass must refresh in the frame that change is made. Each case drives one such change with only the dispatcher and the render tick in between.

    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;

    // The regions the container's material last received: (opacity, translation x, translation y) of each piece.
    private static List<(double Opacity, double X, double Y)> Drawn(Control container)
    {
        var table = Material.GetField("attachments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { container, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!, "the container has a material attachment");
        var state = args[1]!.GetType().GetField("previous", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(args[1]);
        if (state is null) return [];
        var drawn = new List<(double, double, double)>();
        foreach (var region in (Array)state.GetType().GetProperty("Regions")!.GetValue(state)!)
        {
            var transform = (Matrix)region.GetType().GetProperty("Transform")!.GetValue(region)!;
            drawn.Add(((double)region.GetType().GetProperty("Opacity")!.GetValue(region)!, transform.M31, transform.M32));
        }
        return drawn;
    }

    // container (spacing 2) > outer > inner > glass piece, the piece 40 x 40 at (30, 20) of the container.
    private static (Panel Container, Border Outer, Border Inner, Border Piece) Group()
    {
        var piece = Circle(30);
        var inner = new Border { Child = piece };
        var outer = new Border { Child = inner };
        var container = new Panel { Width = 200, Height = 100, Children = { outer } };
        SwiftGlass.SetContainerSpacing(container, 2);
        return (container, outer, inner, piece);
    }

    private static Border Circle(double left)
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 40, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(left, 20, 0, 0) };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.Circle));
        return piece;
    }

    private static bool SynchronisationRequested(Control container)
    {
        var table = Material.GetField("attachments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { container, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        return (bool)args[1]!.GetType().GetField("requested", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(args[1])!;
    }

    private static void Settle() { for (var i = 0; i < 4; i++) GlassScene.Drain(); }

    private static void AssertDrawn(Panel container, double opacity, double x, double y, string because)
    {
        var drawn = Drawn(container);
        Assert.True(opacity <= 0 ? drawn.Count == 0 : drawn.Count == 1 && Math.Abs(drawn[0].Opacity - opacity) < 1e-6 && Math.Abs(drawn[0].X - x) < 1e-6 && Math.Abs(drawn[0].Y - y) < 1e-6,
            $"{because}: expected ({opacity}, {x}, {y}), drawn " + string.Join(" ", drawn));
    }

    [Theory]
    [InlineData(0)] // the visual directly above the container
    [InlineData(1)] // the one above the piece
    [InlineData(2)] // the piece itself
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1120-1123 (UI-CHAT-073): the container's one pass fades with the pieces
    public void AnOpacityChangeBetweenThePieceAndTheContainerReachesTheOnePassInTheSameFrame(int level) => headless.OnUiThread(() =>
    {
        var (container, outer, inner, piece) = Group();
        Control[] levels = [outer, inner, piece];
        using var scene = new Scene(new Pattern(), container);
        Settle();
        AssertDrawn(container, 1, 30, 20, "at rest");
        levels[level].Opacity = 0.4;
        Settle();
        AssertDrawn(container, 0.4, 30, 20, "faded");
        levels[level].Opacity = 0;
        Settle();
        AssertDrawn(container, 0, 0, 0, "invisible");
        levels[level].Opacity = 1; // arrival: the piece was inserted at opacity 0 and its glass was skipped
        Settle();
        AssertDrawn(container, 1, 30, 20, "arrived");
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Req", "PR-CHAT-171")]
    public void AVisibilityChangeBetweenThePieceAndTheContainerReachesTheOnePassInTheSameFrame(int level) => headless.OnUiThread(() =>
    {
        var (container, outer, inner, _) = Group();
        Control[] levels = [outer, inner];
        using var scene = new Scene(new Pattern(), container);
        Settle();
        levels[level].IsVisible = false;
        Settle();
        AssertDrawn(container, 0, 0, 0, "hidden");
        levels[level].IsVisible = true;
        Settle();
        AssertDrawn(container, 1, 30, 20, "shown again");
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Req", "PR-CHAT-171")]
    public void ARenderTransformChangeBetweenThePieceAndTheContainerReachesTheOnePassInTheSameFrame(int level) => headless.OnUiThread(() =>
    {
        var (container, outer, inner, _) = Group();
        Control[] levels = [outer, inner];
        using var scene = new Scene(new Pattern(), container);
        Settle();
        levels[level].RenderTransform = new TranslateTransform(0, 4);
        Settle();
        AssertDrawn(container, 1, 30, 24, "the soft appear's 4-point rise");
        levels[level].RenderTransform = new TranslateTransform(0, 0);
        Settle();
        AssertDrawn(container, 1, 30, 20, "risen");
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Req", "PR-CHAT-171")]
    public void ATransformChangedInPlaceBetweenThePieceAndTheContainerReachesTheOnePassInTheSameFrame(int level) => headless.OnUiThread(() =>
    {
        var (container, outer, inner, _) = Group();
        Control[] levels = [outer, inner];
        var translate = new TranslateTransform(0, 0);
        levels[level].RenderTransform = translate;
        using var scene = new Scene(new Pattern(), container);
        Settle();
        AssertDrawn(container, 1, 30, 20, "at rest");
        translate.X = 25;
        Settle();
        AssertDrawn(container, 1, 55, 20, "the transform moved in place");
        var replacement = new TranslateTransform(-5, 0); // a transform that replaces it is the one watched from then on
        levels[level].RenderTransform = replacement;
        Settle();
        AssertDrawn(container, 1, 25, 20, "replaced");
        replacement.X = 10;
        Settle();
        AssertDrawn(container, 1, 40, 20, "the replacement moved in place");
    });

    [Fact]
    [Trait("Req", "PR-CHAT-171")]
    public void APieceAddedUnderAnotherVisualLaterIsWatchedToo() => headless.OnUiThread(() =>
    {
        var (container, _, _, _) = Group();
        using var scene = new Scene(new Pattern(), container);
        Settle();
        var later = new Border { Opacity = 0, Child = Circle(100) }; // inserted at opacity 0, as a soft appear does
        container.Children.Add(later);
        Settle();
        Assert.Single(Drawn(container));
        later.Opacity = 0.7; // nothing but this opacity changes from here on
        Settle();
        var drawn = Drawn(container).OrderBy(r => r.X).ToList();
        Assert.Equal(2, drawn.Count);
        Assert.Equal(0.7, drawn[1].Opacity, 6);
    });

    [Theory]
    [InlineData(0, "opacity")]
    [InlineData(1, "opacity")]
    [InlineData(2, "opacity")]
    [InlineData(0, "replaced transform")]
    [InlineData(1, "replaced transform")]
    [InlineData(2, "replaced transform")]
    [InlineData(0, "transform in place")]
    [InlineData(1, "transform in place")]
    [InlineData(2, "transform in place")]
    [Trait("Req", "PR-CHAT-171")]
    public void TheChangeIsDrawnByTheOneRenderFrameThatFollowsIt(int level, string change) => headless.OnUiThread(() =>
    {
        var (container, outer, inner, piece) = Group();
        Control[] levels = [outer, inner, piece];
        var translate = new TranslateTransform(0, 0);
        levels[level].RenderTransform = translate;
        using var scene = new Scene(new Pattern(), container);
        Settle();
        Assert.False(SynchronisationRequested(container));
        double opacity = 1, y = 20;
        switch (change)
        {
            case "opacity": levels[level].Opacity = opacity = 0.25; break;
            case "replaced transform": levels[level].RenderTransform = new TranslateTransform(0, 3); y = 23; break;
            default: translate.Y = 3; y = 23; break;
        }
        // The change asks the compositor for the synchronisation itself, at once (not through a posted dispatcher job that would add a hop before the frame).
        Assert.True(SynchronisationRequested(container), "the change has asked for the next composition update");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); // one frame
        AssertDrawn(container, opacity, 30, y, "the frame of the change");
        Assert.False(SynchronisationRequested(container), "and the request is spent");
    });

    // OD-0147: a test that measures the release material's own design (its rim, its bend, its contact line) names it (GlassLook.Release), which a
    // piece that names it still draws; the shipped canvas material is measured by GlassTuningTests.
    private static Border Surface(GlassShape shape, GlassLook? look = null)
    {
        // :171 shape.fill(.clear): give the fixture the source's clear hit area for pointer tests.
        var glass = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        SwiftGlass.SetEffect(glass, look is null ? GlassEffect.Regular(shape) : GlassEffect.Regular(shape).WithLook(look));
        return glass;
    }

}
