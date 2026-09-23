using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/Genie.swift (B2 1fbf81e), :1-214, and the shader it calls,
// DroppyCode/UI/Common/Genie.metal, :16-54. Every expected value below is transcribed from those files:
//   :12      coordinateSpace = "droppy-code-window"            :13  duration = 0.58
//   :47-51   morphDuration = 0.5, morphCrossfade = 0.12, morphAnimation = .smooth(duration: morphDuration)
//   :63-64   morph refuses unless from.width > 1, from.height > 1, to.width > 1, to.height > 1 and not reduce motion
//   :65-69   morph appends Morph(from, fromRadius, to, toRadius, fadeIn, holdsFor, surface) and returns true;
//            fadeIn and holdsFor default to 0 (:60)
//   :72-74   finishMorph removes the morphs with that id
//   :83-84   launch refuses unless frame.width > 1, frame.height > 1 and not reduce motion
//   :85-88   ImageRenderer(GenieGhost(size: frame.size, content: ghost()).environment(\.colorScheme, colorScheme)),
//            scale NSScreen.main?.backingScaleFactor ?? 2, refusing when nsImage is nil
//   :89      Flight(image, frame, target, isArrival: arriving); target defaults to nil, arriving to false (:82)
//   :93-95   finish removes the flights with that id
//   :101-108 GenieGhost is content.frame(width:height:alignment: .topLeading), no fill
//   :112-127 GenieLayer: GeometryReader > ZStack(.topLeading) { flights; morphs } framed to the proxy size,
//            allowsHitTesting(false), accessibilityHidden(true)
//   :137     target = flight.target ?? CGPoint(x: 24, y: canvas.height - 12)
//   :141     (start, end) = isArrival ? (1, 0) : (0, 1)
//   :146-149 reach = (|target.x - frame.midX| + frame.width, |target.y - frame.midY| + frame.height)
//   :150-165 the image at the frame's size and centre in a canvas-sized frame, distorted by genie(progress,
//            float4(minX, minY, width, height), float2(target)), opacity progress > 0.9 ? max(0, (1 - progress) / 0.1) : 1
//   :170-174 the flight is removed after duration - 0.03 seconds
//   :191-201 MorphView: frame and radius are `to` once landed, animated by morphAnimation.delay(holdsFor);
//            opacity(isFading ? 0 : 1) under easeOut(morphCrossfade); opacity(hasStarted || fadeIn <= 0 ? 1 : 0)
//            under easeOut(fadeIn)
//   :202-205 onAppear sets hasStarted and hasLanded
//   :206-212 sleep(holdsFor + morphDuration - morphCrossfade), isFading = true, sleep(morphCrossfade + 0.05),
//            finishMorph
// Port conventions: NSImage is IImage, CGRect is Rect, ColorScheme is ThemeVariant (ED-095, AppTheme.cs), the
// `.smooth` and `.easeOut` curves are F.SwiftAnimation's (ED-092), `morph(...)` is GenieAnimatorMorph (R-NAME-3: the
// nested type Morph keeps the name), and a parameter without a default moves before the defaulted ones (ED-095).
[Collection("Headless")]
public sealed class GenieTests(HeadlessSession headless)
{
    private const double Tolerance = 1e-9;
    private static readonly Rect RowFrame = new(200, 100, 100, 40);

    private static Window Show(Control content, double width = 800, double height = 600)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // Moves SwiftClock by `seconds` (firing each sleep at its own deadline), then delivers one frame.
    private static void Step(AnimationClocks clocks, double seconds)
    {
        var span = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        clocks.Time.Advance(span, () => Dispatcher.UIThread.RunJobs());
        clocks.Frames.Advance(span);
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Surface(double radius) => new Border { CornerRadius = new CornerRadius(radius), Background = Brushes.Gray };

    private static double Lerp(double a, double b, double p) => a + (b - a) * p;

    private static double EffectiveOpacity(Visual from, Visual to)
    {
        var opacity = from.Opacity;
        foreach (var ancestor in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(from))
        {
            opacity *= ancestor.Opacity;
            if (ReferenceEquals(ancestor, to)) break;
        }
        return opacity;
    }

    // ---------------------------------------------------------------------------------------------------------
    // PR-THEME-172: morph
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:12-13, :47-51
    public void The_animator_constants_are_the_window_space_name_the_flight_duration_and_the_morph_timings()
    {
        Assert.Equal("droppy-code-window", GenieAnimator.CoordinateSpace);
        Assert.Equal(0.58, GenieAnimator.Duration);
        Assert.Equal(0.5, GenieAnimator.MorphDuration);
        Assert.Equal(0.12, GenieAnimator.MorphCrossfade);
        Assert.Equal(Marrow.UI.Port.Animation.Smooth(duration: 0.5), GenieAnimator.MorphAnimation);
    }

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:8-11
    public void All_records_live_in_one_shared_main_actor_observable_animator()
    {
        Assert.Same(GenieAnimator.Shared.Current, GenieAnimator.Shared.Current);
        Assert.IsAssignableFrom<MainActorModel>(GenieAnimator.Shared.Current);
        Assert.False(typeof(GenieAnimator).GetProperty(nameof(GenieAnimator.Morphs))!.SetMethod?.IsPublic ?? false);
        Assert.False(typeof(GenieAnimator).GetProperty(nameof(GenieAnimator.Flights))!.SetMethod?.IsPublic ?? false);
    }

    public static TheoryData<Rect, Rect> DegenerateMorphs() => new()
    {
        { new Rect(0, 0, 1, 50), new Rect(0, 0, 50, 50) },
        { new Rect(0, 0, 50, 1), new Rect(0, 0, 50, 50) },
        { new Rect(0, 0, 50, 50), new Rect(0, 0, 1, 50) },
        { new Rect(0, 0, 50, 50), new Rect(0, 0, 50, 1) },
        { new Rect(0, 0, 0.5, 50), new Rect(0, 0, 50, 50) },
        { new Rect(0, 0, 50, 50), new Rect(0, 0, 50, 0) },
        { new Rect(0, 0, 0, 0), new Rect(0, 0, 0, 0) },
    };

    [Theory]
    [MemberData(nameof(DegenerateMorphs))]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:63-64
    public void A_morph_with_any_side_of_one_point_or_less_returns_false_and_queues_nothing(Rect from, Rect to)
    {
        using var runtime = new TestRuntime();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        var result = runtime.OnMain(() => animator.GenieAnimatorMorph(from: from, fromRadius: 4, to: to, toRadius: 8, surface: Surface));
        Assert.False(result);
        Assert.Empty(animator.Morphs);
    }

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:63-64
    public void A_morph_just_over_one_point_on_every_side_is_accepted()
    {
        using var runtime = new TestRuntime();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        var result = runtime.OnMain(() => animator.GenieAnimatorMorph(
            from: new Rect(0, 0, 1.0001, 1.0001), fromRadius: 0, to: new Rect(5, 5, 1.0001, 1.0001), toRadius: 0, surface: Surface));
        Assert.True(result);
        Assert.Single(animator.Morphs);
    }

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:63-64
    public void Under_reduce_motion_a_morph_returns_false_and_queues_nothing()
    {
        using var runtime = new TestRuntime();
        var platform = new FakeGeniePlatform(reduceMotion: true);
        var animator = new GenieAnimator(platform);
        var result = runtime.OnMain(() => animator.GenieAnimatorMorph(
            from: new Rect(0, 0, 40, 40), fromRadius: 20, to: new Rect(100, 100, 300, 200), toRadius: 16, surface: Surface));
        Assert.False(result);
        Assert.Empty(animator.Morphs);
        Assert.Equal(1, platform.ReduceMotionReads);
    }

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:30-43, :57-69
    public void A_morph_appends_one_record_with_both_shapes_the_timings_and_its_surface_and_returns_true()
    {
        using var runtime = new TestRuntime();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        var from = new Rect(10, 20, 40, 30);
        var to = new Rect(100, 120, 360, 240);
        var drawn = new List<double>();
        Control Draw(double radius)
        {
            drawn.Add(radius);
            return new Border();
        }
        var first = runtime.OnMain(() => animator.GenieAnimatorMorph(from: from, fromRadius: 15, to: to, toRadius: 18, surface: Draw, fadeIn: 0.1, holdsFor: 0.2));
        var second = runtime.OnMain(() => animator.GenieAnimatorMorph(from: to, fromRadius: 18, to: from, toRadius: 15, surface: Draw));
        Assert.True(first);
        Assert.True(second);
        Assert.Equal(2, animator.Morphs.Count);
        var morph = animator.Morphs[0];
        Assert.Equal(from, morph.From);
        Assert.Equal(15, morph.FromRadius);
        Assert.Equal(to, morph.To);
        Assert.Equal(18, morph.ToRadius);
        Assert.Equal(0.1, morph.FadeIn);
        Assert.Equal(0.2, morph.HoldsFor);
        Assert.Equal(0, animator.Morphs[1].FadeIn);   // :60 fadeIn: Double = 0
        Assert.Equal(0, animator.Morphs[1].HoldsFor); // :60 holdsFor: Double = 0
        Assert.Empty(drawn);                          // the surface is drawn by the layer, not by morph(...)
        Assert.IsType<Border>(morph.Surface(7));
        Assert.Equal([7.0], drawn);
        Assert.NotEqual(morph.Id, animator.Morphs[1].Id);
    }

    [Fact]
    [Trait("Req", "PR-THEME-172")] // UI/Common/Genie.swift:72-74
    public void Finishing_a_morph_removes_only_the_morph_with_that_id()
    {
        using var runtime = new TestRuntime();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        runtime.OnMain(() =>
        {
            animator.GenieAnimatorMorph(from: new Rect(0, 0, 10, 10), fromRadius: 1, to: new Rect(0, 0, 20, 20), toRadius: 2, surface: Surface);
            animator.GenieAnimatorMorph(from: new Rect(0, 0, 30, 30), fromRadius: 3, to: new Rect(0, 0, 40, 40), toRadius: 4, surface: Surface);
        });
        var keep = animator.Morphs[1];
        runtime.OnMain(() => animator.FinishMorph(animator.Morphs[0].Id));
        Assert.Equal([keep], animator.Morphs.ToArray());
        runtime.OnMain(() => animator.FinishMorph(Guid.NewGuid()));
        Assert.Equal([keep], animator.Morphs.ToArray());
    }

    // ---------------------------------------------------------------------------------------------------------
    // PR-THEME-173: the morph view
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-THEME-173")] // UI/Common/Genie.swift:191-205
    public void A_morph_holds_its_first_shape_then_moves_frame_and_radius_together_on_one_delayed_smooth_curve() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        var from = new Rect(40, 500, 40, 30);
        var to = new Rect(200, 100, 400, 300);
        var radii = new List<double>();
        Control Draw(double radius)
        {
            radii.Add(radius);
            return Surface(radius);
        }
        animator.GenieAnimatorMorph(from: from, fromRadius: 15, to: to, toRadius: 18, surface: Draw, holdsFor: 0.2);
        var layer = new GenieLayer();
        var window = Show(layer);
        var view = Assert.Single(VisualQuery.Descendants<MorphView>(layer));
        Assert.Equal(15, radii[0]); // the first body draws the starting shape
        var curve = GenieAnimator.MorphAnimation.Delay(0.2);
        double elapsed = 0;
        foreach (var at in new[] { 0.1, 0.2, 0.25, 0.3, 0.45, 0.6 })
        {
            Step(clocks, at - elapsed);
            elapsed = at;
            var p = curve.Progress(at);
            var midX = Lerp(from.Center.X, to.Center.X, p);
            var midY = Lerp(from.Center.Y, to.Center.Y, p);
            var width = Lerp(from.Width, to.Width, p);
            var height = Lerp(from.Height, to.Height, p);
            var frame = view.PresentedFrame;
            Assert.Equal(midX, frame.Center.X, Tolerance);
            Assert.Equal(midY, frame.Center.Y, Tolerance);
            Assert.Equal(width, frame.Width, Tolerance);
            Assert.Equal(height, frame.Height, Tolerance);
            Assert.Equal(Lerp(15, 18, p), view.PresentedRadius, Tolerance);
            Assert.Equal(Lerp(15, 18, p), radii[^1], Tolerance); // the surface is drawn for the radius it has now
            if (at <= 0.2) Assert.Equal(from, frame);            // holding its first shape
        }
        window.UpdateLayout();
        var surface = view.Surface!;
        var placed = surface.Bounds;
        Assert.Equal(view.PresentedFrame.Width, placed.Width, 1e-6);
        Assert.Equal(view.PresentedFrame.Height, placed.Height, 1e-6);
        var origin = surface.TranslatePoint(new Point(0, 0), layer)!.Value;
        Assert.Equal(view.PresentedFrame.X, origin.X, 1e-6);
        Assert.Equal(view.PresentedFrame.Y, origin.Y, 1e-6);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-173")] // UI/Common/Genie.swift:198-201, :202-205
    public void A_morph_fades_in_over_its_own_fade_in_duration_on_an_ease_out_curve() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.GenieAnimatorMorph(from: new Rect(10, 10, 40, 40), fromRadius: 20, to: new Rect(100, 100, 300, 200), toRadius: 16, surface: Surface, fadeIn: 0.1, holdsFor: 0.1);
        var layer = new GenieLayer();
        var window = Show(layer);
        var view = Assert.Single(VisualQuery.Descendants<MorphView>(layer));
        Assert.Equal(0, EffectiveOpacity(view.Surface!, layer), Tolerance);
        double elapsed = 0;
        foreach (var at in new[] { 0.02, 0.05, 0.08, 0.1, 0.3 })
        {
            Step(clocks, at - elapsed);
            elapsed = at;
            var expected = Marrow.UI.Port.Animation.EaseOut(duration: 0.1).Progress(at);
            if (at >= 0.1) expected = 1;
            Assert.Equal(expected, EffectiveOpacity(view.Surface!, layer), Tolerance);
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-173")] // UI/Common/Genie.swift:200
    public void A_morph_without_a_fade_in_is_fully_opaque_from_the_first_frame() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.GenieAnimatorMorph(from: new Rect(10, 10, 40, 40), fromRadius: 20, to: new Rect(100, 100, 300, 200), toRadius: 16, surface: Surface, fadeIn: 0);
        var layer = new GenieLayer();
        var window = Show(layer);
        var view = Assert.Single(VisualQuery.Descendants<MorphView>(layer));
        Assert.Equal(1, EffectiveOpacity(view.Surface!, layer), Tolerance);
        Step(clocks, 0.01);
        Assert.Equal(1, EffectiveOpacity(view.Surface!, layer), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-173")] // UI/Common/Genie.swift:198-199, :206-212
    public void A_morph_fades_out_over_the_last_crossfade_before_it_lands_and_is_removed_five_hundredths_after_it_has_faded() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.GenieAnimatorMorph(from: new Rect(10, 10, 40, 40), fromRadius: 20, to: new Rect(100, 100, 300, 200), toRadius: 16, surface: Surface, holdsFor: 0.2);
        var layer = new GenieLayer();
        var window = Show(layer);
        var view = Assert.Single(VisualQuery.Descendants<MorphView>(layer));
        // lands = 0.2 + 0.5 = 0.7; the fade starts at 0.7 - 0.12 = 0.58; removal 0.12 + 0.05 later, at 0.75.
        Step(clocks, 0.579);
        Assert.Equal(1, EffectiveOpacity(view.Surface!, layer), Tolerance);
        Step(clocks, 0.001);
        double elapsed = 0.58;
        foreach (var at in new[] { 0.6, 0.64, 0.68, 0.7 })
        {
            Step(clocks, at - elapsed);
            elapsed = at;
            var expected = 1 - Marrow.UI.Port.Animation.EaseOut(duration: 0.12).Progress(at - 0.58);
            if (at - 0.58 >= 0.12) expected = 0;
            Assert.Equal(expected, EffectiveOpacity(view.Surface!, layer), 1e-6);
        }
        Step(clocks, 0.7499 - elapsed);
        Assert.Single(animator.Morphs);
        Assert.Single(VisualQuery.Descendants<MorphView>(layer));
        Step(clocks, 0.0001);
        Assert.Empty(animator.Morphs);
        Assert.Empty(VisualQuery.Descendants<MorphView>(layer));
        window.Close();
    });

    // ---------------------------------------------------------------------------------------------------------
    // PR-THEME-174: launch
    // ---------------------------------------------------------------------------------------------------------

    public static TheoryData<Rect> DegenerateFrames() => new()
    {
        new Rect(0, 0, 1, 40), new Rect(0, 0, 100, 1), new Rect(0, 0, 0.5, 40), new Rect(0, 0, 100, 0), new Rect(0, 0, 0, 0),
    };

    [Theory]
    [MemberData(nameof(DegenerateFrames))]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:83-84
    public void A_launch_from_a_frame_of_one_point_or_less_returns_false_renders_nothing_and_queues_nothing(Rect frame)
    {
        using var runtime = new TestRuntime();
        var platform = new FakeGeniePlatform();
        var animator = new GenieAnimator(platform);
        var ghosts = 0;
        var result = runtime.OnMain(() => animator.Launch(frame: frame, colorScheme: ThemeVariant.Dark, ghost: () =>
        {
            ghosts++;
            return new Border();
        }));
        Assert.False(result);
        Assert.Empty(platform.Renders);
        Assert.Equal(0, ghosts);
        Assert.Empty(animator.Flights);
    }

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:83-84
    public void Under_reduce_motion_a_launch_returns_false_renders_nothing_and_queues_nothing()
    {
        using var runtime = new TestRuntime();
        var platform = new FakeGeniePlatform(reduceMotion: true);
        var animator = new GenieAnimator(platform);
        var result = runtime.OnMain(() => animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border()));
        Assert.False(result);
        Assert.Empty(platform.Renders);
        Assert.Empty(animator.Flights);
    }

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:88
    public void When_the_render_gives_no_image_a_launch_returns_false_and_queues_nothing()
    {
        using var runtime = new TestRuntime();
        var platform = new FakeGeniePlatform(render: (_, _) => null);
        var animator = new GenieAnimator(platform);
        var result = runtime.OnMain(() => animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border()));
        Assert.False(result);
        Assert.Single(platform.Renders);
        Assert.Empty(animator.Flights);
    }

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:15-23, :82, :89-90
    public void A_launch_appends_a_flight_with_the_image_the_frame_the_target_and_the_arrival_flag_and_returns_true()
    {
        using var runtime = new TestRuntime();
        var image = new StandInImage();
        var animator = new GenieAnimator(new FakeGeniePlatform(render: (_, _) => image));
        var landing = new Point(700, 20);
        var first = runtime.OnMain(() => animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border()));
        var second = runtime.OnMain(() => animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border(), target: landing, arriving: true));
        Assert.True(first);
        Assert.True(second);
        Assert.Equal(2, animator.Flights.Count);
        var departure = animator.Flights[0];
        Assert.Same(image, departure.Image);
        Assert.Equal(RowFrame, departure.Frame);
        Assert.Null(departure.Target);     // :82 target: CGPoint? = nil
        Assert.False(departure.IsArrival); // :82 arriving: Bool = false
        var arrival = animator.Flights[1];
        Assert.Equal(landing, arrival.Target);
        Assert.True(arrival.IsArrival);
        Assert.NotEqual(departure.Id, arrival.Id);
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(null, 2.0)]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:87
    public void The_ghost_is_rendered_at_the_main_screen_backing_scale_or_at_2_without_a_screen(double? screen, double expected)
    {
        using var runtime = new TestRuntime();
        var platform = new FakeGeniePlatform(scale: screen);
        var animator = new GenieAnimator(platform);
        runtime.OnMain(() => animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border()));
        Assert.Equal(expected, Assert.Single(platform.Renders).Scale);
    }

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:85-86, :101-108
    public void The_ghost_forces_the_callers_colour_scheme_adds_no_fill_and_frames_the_row_to_its_size_top_leading() => headless.OnUiThread(() =>
    {
        var platform = new FakeGeniePlatform();
        var animator = new GenieAnimator(platform);
        var fixedRow = new Border { Width = 50, Height = 10, Background = Brushes.Red };
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Dark, ghost: () => fixedRow);
        var root = Assert.Single(platform.Renders).Content;
        var scope = Assert.IsType<ThemeVariantScope>(root);
        Assert.Equal(ThemeVariant.Dark, scope.RequestedThemeVariant);
        var ghost = Assert.IsType<GenieGhost>(scope.Child);
        Assert.Null(ghost.Background); // no fill of its own
        Assert.Same(fixedRow, Assert.Single(ghost.Children));
        root.Measure(Size.Infinity);
        root.Arrange(new Rect(root.DesiredSize));
        Assert.Equal(RowFrame.Size, root.DesiredSize);
        Assert.Equal(new Rect(0, 0, 100, 40), ghost.Bounds);
        Assert.Equal(new Rect(0, 0, 50, 10), fixedRow.Bounds); // a fixed-size row sits top-leading
        Assert.Equal(ThemeVariant.Dark, fixedRow.ActualThemeVariant);

        var flexibleRow = new Border { Background = Brushes.Red };
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => flexibleRow);
        var second = platform.Renders[1].Content;
        second.Measure(Size.Infinity);
        second.Arrange(new Rect(second.DesiredSize));
        Assert.Equal(new Rect(0, 0, 100, 40), flexibleRow.Bounds); // a flexible row takes the whole frame
        Assert.Equal(ThemeVariant.Light, flexibleRow.ActualThemeVariant);
    });

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:85-88, :98-100
    public void The_windows_renderer_captures_the_ghost_once_at_the_given_scale_and_a_transparent_row_stays_transparent() => headless.OnUiThread(() =>
    {
        var platform = new WindowsGeniePlatform();
        var scripted = new FakeGeniePlatform(render: platform.RenderImage);
        var animator = new GenieAnimator(scripted);
        var clearRow = new Border { Width = 100, Height = 40 };
        Assert.True(animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Dark, ghost: () => clearRow));
        var image = Assert.IsAssignableFrom<Bitmap>(Assert.Single(animator.Flights).Image);
        Assert.Equal(new PixelSize(200, 80), image.PixelSize); // scale 2: no main screen in the headless session
        Assert.Equal(new Size(100, 40), image.Size);
        Assert.All(GenieBitmaps.Pixels(image).Where((_, i) => i % 4 == 3), alpha => Assert.Equal(0, alpha));
        Assert.Equal(ThemeVariant.Dark, clearRow.ActualThemeVariant);

        var twoColour = new GenieAnimator(new FakeGeniePlatform(scale: 1.5, render: platform.RenderImage));
        Assert.True(twoColour.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => GenieBitmaps.RedBlueRow(RowFrame.Size)));
        var bitmap = Assert.IsAssignableFrom<Bitmap>(Assert.Single(twoColour.Flights).Image);
        Assert.Equal(new PixelSize(150, 60), bitmap.PixelSize);
        var pixels = GenieBitmaps.Pixels(bitmap);
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), GenieBitmaps.At(pixels, 150, 30, 30));  // red, left half
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), GenieBitmaps.At(pixels, 150, 120, 30)); // blue, right half
    });

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:64, :84, :87
    public void The_windows_platform_reads_the_live_animation_effects_setting_and_has_no_screen_without_a_main_window() => headless.OnUiThread(() =>
    {
        var platform = new WindowsGeniePlatform();
        Assert.Equal(!SystemAnimation.ClientAreaAnimation(), platform.AccessibilityDisplayShouldReduceMotion);
        Assert.Null(platform.MainScreenBackingScaleFactor);
    });

    [Fact]
    [Trait("Req", "PR-THEME-174")] // UI/Common/Genie.swift:93-95
    public void Finishing_a_flight_removes_only_the_flight_with_that_id()
    {
        using var runtime = new TestRuntime();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        runtime.OnMain(() =>
        {
            animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
            animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        });
        var keep = animator.Flights[1];
        runtime.OnMain(() => animator.Finish(animator.Flights[0].Id));
        Assert.Equal([keep], animator.Flights.ToArray());
    }

    // ---------------------------------------------------------------------------------------------------------
    // PR-THEME-175: the flight
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-THEME-175")] // UI/Common/Genie.swift:137, :141, :146-149
    public void A_flight_lands_on_its_target_or_the_archive_corner_runs_forwards_or_backwards_and_reaches_by_distance_plus_size() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        animator.Launch(frame: new Rect(500, 300, 200, 30), colorScheme: ThemeVariant.Light, ghost: () => new Border(), target: new Point(650, 40), arriving: true);
        var layer = new GenieLayer();
        var window = Show(layer, 800, 600);
        var views = VisualQuery.Descendants<GenieFlightView>(layer).ToArray();
        Assert.Equal(2, views.Length);
        var departure = views.Single(v => !v.Flight.IsArrival);
        var arrival = views.Single(v => v.Flight.IsArrival);
        Assert.Equal(new Size(800, 600), departure.Canvas);
        Assert.Equal(new Point(24, 588), departure.Target);
        Assert.Equal(new Point(650, 40), arrival.Target);
        Assert.Equal((0.0, 1.0), (departure.Start, departure.End));
        Assert.Equal((1.0, 0.0), (arrival.Start, arrival.End));
        Assert.Equal(0.0, departure.Progress); // keyframeAnimator(initialValue: start)
        Assert.Equal(1.0, arrival.Progress);
        Assert.Equal(new Size(Math.Abs(24 - 250.0) + 100, Math.Abs(588 - 120.0) + 40), departure.Reach);
        Assert.Equal(new Size(Math.Abs(650 - 600.0) + 200, Math.Abs(40 - 315.0) + 30), arrival.Reach);
        window.Close();
    });

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(0.9, 1.0)]
    [InlineData(0.95, 0.5)]
    [InlineData(0.99, 0.1)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.05, 0.0)]
    [Trait("Req", "PR-THEME-175")] // UI/Common/Genie.swift:165
    public void A_flight_is_opaque_until_nine_tenths_and_then_fades_linearly_to_nothing(double progress, double opacity) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        var layer = new GenieLayer();
        var window = Show(layer);
        var view = Assert.Single(VisualQuery.Descendants<GenieFlightView>(layer));
        view.Present(progress);
        Assert.Equal(progress, view.Progress);
        var expected = progress > 0.9 ? Math.Max(0, (1 - progress) / 0.1) : 1;
        Assert.Equal(opacity, expected, 1e-9); // the table agrees with :165
        Assert.Equal(expected, EffectiveOpacity(view, layer), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-175")] // UI/Common/Genie.swift:169-174
    public void A_flight_is_removed_three_hundredths_before_its_keyframes_end() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        var layer = new GenieLayer();
        var window = Show(layer);
        Assert.Single(VisualQuery.Descendants<GenieFlightView>(layer));
        Step(clocks, 0.5499);
        Assert.Single(animator.Flights);
        Assert.Single(VisualQuery.Descendants<GenieFlightView>(layer));
        Step(clocks, 0.0001); // 0.58 - 0.03
        Assert.Empty(animator.Flights);
        Assert.Empty(VisualQuery.Descendants<GenieFlightView>(layer));
        window.Close();
    });

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [Trait("Req", "PR-THEME-175")] // UI/Common/Genie.swift:150-164; UI/Common/Genie.metal:16-54
    public void Each_pixel_of_a_flight_shows_the_row_pixel_the_genie_shader_maps_it_to_or_nothing(double progress) => headless.OnUiThread(() =>
    {
        var windows = new WindowsGeniePlatform();
        var animator = new GenieAnimator(new FakeGeniePlatform(render: windows.RenderImage));
        using var shared = GenieAnimator.Shared.Override(animator);
        Assert.True(animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => GenieBitmaps.RedBlueRow(RowFrame.Size)));
        var layer = new GenieLayer();
        var window = Show(layer, 400, 600);
        var view = Assert.Single(VisualQuery.Descendants<GenieFlightView>(layer));
        view.Present(progress);
        var target = view.Target; // (24, 588)
        var target1 = new RenderTargetBitmap(new PixelSize(400, 600));
        target1.Render(layer);
        var pixels = GenieBitmaps.Pixels(target1);
        var checkedSome = (red: 0, blue: 0, nothing: 0);
        // Which row pixel a destination pixel centre shows, by Genie.metal; 0 nothing, 1 red (left half), 2 blue.
        int Kind(double x, double y)
        {
            var s = GenieMetal.Genie((float)x, (float)y, (float)progress, (float)RowFrame.X, (float)RowFrame.Y,
                (float)RowFrame.Width, (float)RowFrame.Height, (float)target.X, (float)target.Y);
            if (s is not { } source) return 0;
            return source.X < RowFrame.Center.X ? 1 : 2;
        }
        for (var y = 2; y < 598; y += 4)
        {
            for (var x = 2; x < 398; x += 4)
            {
                var cx = x + 0.5;
                var cy = y + 0.5;
                var kind = Kind(cx, cy);
                // Only pixels well inside one region: every neighbour within 2 points maps to the same kind.
                if (Kind(cx - 2, cy) != kind || Kind(cx + 2, cy) != kind || Kind(cx, cy - 2) != kind || Kind(cx, cy + 2) != kind) continue;
                var (b, g, r, a) = GenieBitmaps.At(pixels, 400, x, y);
                switch (kind)
                {
                    case 0:
                        Assert.True(a == 0, $"({x},{y}) should sample nothing at {progress}, was a={a}");
                        checkedSome.nothing++;
                        break;
                    case 1:
                        Assert.True(r == 255 && g == 0 && b == 0 && a == 255, $"({x},{y}) should be red at {progress}, was {r},{g},{b},{a}");
                        checkedSome.red++;
                        break;
                    default:
                        Assert.True(r == 0 && g == 0 && b == 255 && a == 255, $"({x},{y}) should be blue at {progress}, was {r},{g},{b},{a}");
                        checkedSome.blue++;
                        break;
                }
            }
        }
        Assert.True(checkedSome.red > 10 && checkedSome.blue > 10 && checkedSome.nothing > 1000, checkedSome.ToString());
        window.Close();
    });

    // ---------------------------------------------------------------------------------------------------------
    // PR-THEME-176 and UI-THEME-051: the layer
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-THEME-176")] // UI/Common/Genie.swift:112-124
    [Trait("Req", "UI-THEME-051")] // UI/Common/Genie.swift:111-127
    public void The_layer_fills_the_window_and_stacks_every_flight_below_every_morph_each_given_the_window_size() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        var layer = new GenieLayer();
        var window = Show(layer, 640, 480);
        Assert.Equal(new Rect(0, 0, 640, 480), layer.Bounds);
        Assert.Empty(VisualQuery.Descendants<GenieFlightView>(layer)); // idle: nothing drawn
        Assert.Empty(VisualQuery.Descendants<MorphView>(layer));
        animator.GenieAnimatorMorph(from: new Rect(0, 0, 40, 40), fromRadius: 20, to: new Rect(100, 100, 300, 200), toRadius: 16, surface: Surface);
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        animator.Launch(frame: new Rect(10, 10, 50, 20), colorScheme: ThemeVariant.Light, ghost: () => new Border());
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var flights = VisualQuery.Descendants<GenieFlightView>(layer).ToArray();
        var morphs = VisualQuery.Descendants<MorphView>(layer).ToArray();
        Assert.Equal(animator.Flights.Select(f => f.Id), flights.Select(v => v.Flight.Id)); // in order, keyed by id
        Assert.Single(morphs);
        var drawOrder = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(layer)
            .Where(v => v is GenieFlightView or MorphView).ToArray();
        Assert.Equal(new Visual[] { flights[0], flights[1], morphs[0] }, drawOrder); // flights below, morphs above
        foreach (var view in flights)
        {
            Assert.Equal(new Size(640, 480), view.Canvas);
            Assert.Equal(new Rect(0, 0, 640, 480), view.Bounds);
            Assert.Equal(new Point(0, 0), view.TranslatePoint(new Point(0, 0), layer));
        }
        Assert.Equal(new Size(640, 480), morphs[0].Canvas);
        window.Width = 700;
        window.Height = 500;
        window.UpdateLayout();
        Assert.Equal(new Size(700, 500), flights[0].Canvas);
        Assert.Equal(new Point(24, 488), flights[0].Target);
        Assert.Equal(new Size(700, 500), morphs[0].Canvas);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-176")] // UI/Common/Genie.swift:125-126
    [Trait("Req", "UI-THEME-051")] // UI/Common/Genie.swift:111-127
    public void The_layer_never_takes_a_click_and_is_hidden_from_assistive_technology() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.Launch(frame: new Rect(0, 0, 400, 300), colorScheme: ThemeVariant.Light, ghost: () => new Border());
        animator.GenieAnimatorMorph(from: new Rect(0, 0, 400, 300), fromRadius: 4, to: new Rect(0, 0, 400, 300), toRadius: 4, surface: Surface);
        var button = new Border { Background = Brushes.White };
        var layer = new GenieLayer();
        var root = new Panel { Children = { button, layer } };
        var window = Show(root, 400, 300);
        Assert.False(layer.IsHitTestVisible);
        var hit = window.InputHitTest(new Point(200, 150)) as Visual;
        Assert.NotNull(hit);
        Assert.True(ReferenceEquals(hit, button) || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(hit!).Contains(button));
        var peer = ControlAutomationPeer.CreatePeerForElement(layer);
        Assert.False(peer.IsControlElement());
        Assert.False(peer.IsContentElement());
        Assert.Empty(peer.GetChildren());
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-THEME-176")] // UI/Common/Genie.swift:116-121
    public void A_record_that_stays_keeps_its_view_and_a_finished_one_leaves_the_layer() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var animator = new GenieAnimator(new FakeGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        animator.Launch(frame: RowFrame, colorScheme: ThemeVariant.Light, ghost: () => new Border());
        var layer = new GenieLayer();
        var window = Show(layer);
        var before = VisualQuery.Descendants<GenieFlightView>(layer).ToArray();
        animator.Finish(animator.Flights[0].Id);
        Dispatcher.UIThread.RunJobs();
        var after = VisualQuery.Descendants<GenieFlightView>(layer).ToArray();
        Assert.Same(before[1], Assert.Single(after));
        window.Close();
    });
}
