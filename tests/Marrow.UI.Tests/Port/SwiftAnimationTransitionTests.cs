using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.SwiftAnimation, part 1: SwiftTransition (AnyTransition, ED-092 part 2; R-VIEW-4) on Avalonia 12.1.2 headless
// (R-TEST-7). The surface these tests fix: `control.Transition(t)` is SwiftUI's `.transition(t)`;
// `SwiftTransition.Insert(panel, index, child)` and `SwiftTransition.Remove(panel, child)` insert and remove a child
// the way SwiftUI inserts and removes a view, reading Transaction.Current at the call. Under an animation (the
// transaction's, or the transition's own `.animation(_:)`), an inserted child starts at the transition's ACTIVE state
// and animates to identity; a removed child starts at identity, animates to the active state and leaves the panel
// after. Without any animation a child is inserted at identity and removed at once.
//
// What the assertions read is independent of how the port applies the effects: the child's opacity times its
// ancestors' up to the panel, its corners mapped into the panel (render transforms included), and whether it is still
// under the panel. Every test runs on AnimationClocks (ED-098 (1), ED-117 (1)): frames from a ManualFrameSource are the
// only wake-ups and elapsed time is read on SwiftClock. The tests before the frame section drive no frame between the
// call and the assertion, so the state asserted is the one the transition starts from. The frame section moves
// SwiftClock and delivers frames by hand; its expected values are the curves of SwiftAnimationTests (derived there
// from the closed forms), sampled at the elapsed time, and the end state once the animation's duration is complete.
//
// Geometry: a 100 x 40 child at the panel origin. Scale is about the centre (SwiftUI's default anchor): at 0.6 the
// child is 60 x 24 with its top left at (20, 8). Move(edge) offsets the child by its own size towards that edge.
// The default `.scale` is scale 0 and `.move` uses the view's own size: documented SwiftUI behaviour, platform
// knowledge rather than B2 facts, UNVERIFIED under OD-0001 as ED-092 records.
[Collection("Headless")]
public sealed class SwiftAnimationTransitionTests(HeadlessSession headless)
{
    private const double Tolerance = 1e-9;

    private static (Window Window, Canvas Panel) Host()
    {
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, panel);
    }

    private static Border Box() => new() { Width = 100, Height = 40 };

    private static bool IsUnder(Visual child, Visual panel) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(child).Contains(panel);

    private static double EffectiveOpacity(Visual child, Visual panel)
    {
        var opacity = child.Opacity;
        foreach (var ancestor in Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(child))
        {
            if (ReferenceEquals(ancestor, panel)) return opacity;
            opacity *= ancestor.Opacity;
        }
        throw new InvalidOperationException("the child is not under the panel");
    }

    private static double BlurRadius(Visual child, Visual panel)
    {
        var radius = 0.0;
        for (Visual? visual = child; visual is not null && !ReferenceEquals(visual, panel); visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual))
            if (visual.Effect is IBlurEffect blur) radius = Math.Max(radius, blur.Radius);
        return radius;
    }

    private static void AssertCorners(Control child, Visual panel, Point topLeft, Point bottomRight, double tolerance = Tolerance)
    {
        var from = child.TranslatePoint(new Point(0, 0), panel);
        var to = child.TranslatePoint(new Point(child.Bounds.Width, child.Bounds.Height), panel);
        Assert.NotNull(from);
        Assert.NotNull(to);
        Assert.Equal(topLeft.X, from.Value.X, tolerance);
        Assert.Equal(topLeft.Y, from.Value.Y, tolerance);
        Assert.Equal(bottomRight.X, to.Value.X, tolerance);
        Assert.Equal(bottomRight.Y, to.Value.Y, tolerance);
    }

    private static void InsertAnimated(Window window, Canvas panel, Control child)
    {
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
    }

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void Without_an_animation_a_child_is_inserted_at_identity_and_removed_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        Assert.Null(Transaction.Current);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        SwiftTransition.Remove(panel, child);
        Assert.False(IsUnder(child, panel));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void Under_an_animation_an_opacity_insertion_starts_transparent() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/HydraPanel.swift:68 `.transition(.opacity)`
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        InsertAnimated(window, panel, child);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void Under_an_animation_a_removal_starts_at_identity_and_the_child_stays_until_it_ends() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_scale_insertion_starts_scaled_about_the_centre() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/HydraButton.swift:42 `.scale(scale: 0.6).combined(with: .opacity)`
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Scale(scale: 0.6).Combined(SwiftTransition.Opacity));
        InsertAnimated(window, panel, child);
        AssertCorners(child, panel, new Point(20, 8), new Point(80, 32));
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void The_default_scale_transition_starts_from_nothing_at_the_centre() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/HydraButton.swift:56 `.scale.combined(with: .opacity)`
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Scale().Combined(SwiftTransition.Opacity));
        InsertAnimated(window, panel, child);
        AssertCorners(child, panel, new Point(50, 20), new Point(50, 20));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void An_offset_insertion_starts_displaced_by_the_offset() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/TimelineRows.swift:1068 `.opacity.combined(with: .offset(y: 5))`, SettingsView.swift:557 `.offset(x: 6)`
        var (window, panel) = Host();
        var down = Box().Transition(SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 5)));
        InsertAnimated(window, panel, down);
        AssertCorners(down, panel, new Point(0, 5), new Point(100, 45));
        Assert.Equal(0, EffectiveOpacity(down, panel), Tolerance);
        var across = Box().Transition(SwiftTransition.Opacity.Combined(SwiftTransition.Offset(x: 6)));
        InsertAnimated(window, panel, across);
        AssertCorners(across, panel, new Point(6, 0), new Point(106, 40));
        window.Close();
    });

    [Theory]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [InlineData(Edge.Top, 0, -40)]
    [InlineData(Edge.Bottom, 0, 40)]
    [InlineData(Edge.Leading, -100, 0)]
    [InlineData(Edge.Trailing, 100, 0)]
    public void A_move_insertion_starts_one_own_size_beyond_its_edge(Edge edge, double x, double y) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ChatView.swift:278 `.move(edge: .bottom)`, App/Views/Usage/UsageViews.swift:251 `.move(edge: .top)`
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Move(edge: edge));
        InsertAnimated(window, panel, child);
        AssertCorners(child, panel, new Point(x, y), new Point(x + 100, y + 40));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void An_asymmetric_transition_inserts_with_its_insertion_half() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ChatView.swift:601-605 panelTransition
        var (window, panel) = Host();
        var panelTransition = SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Scale(scale: 0.92).Combined(SwiftTransition.Opacity),
            removal: SwiftTransition.Scale(scale: 0.96).Combined(SwiftTransition.Opacity));
        var child = Box().Transition(panelTransition);
        InsertAnimated(window, panel, child);
        AssertCorners(child, panel, new Point(4, 1.6), new Point(96, 38.4));
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void An_asymmetric_transition_with_an_identity_insertion_inserts_at_identity_and_still_animates_its_removal() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // UI/Markdown/MarkdownView.swift:53 pairs `.identity` with another half; App/Views/Sidebar/SidebarView.swift:1074
        // gives the removal half its own `.opacity.animation(.easeOut(duration: 0.06))`, which animates the removal
        // although the removal itself is not inside withAnimation.
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Identity,
            removal: SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06))));
        InsertAnimated(window, panel, child);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        SwiftTransition.Remove(panel, child);
        Assert.True(IsUnder(child, panel));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void The_identity_transition_inserts_unchanged_even_under_an_animation() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ChatView.swift:288 `.transition(.identity)`
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Identity);
        InsertAnimated(window, panel, child);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void The_identity_transition_leaves_at_once_even_under_an_animation() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ChatView.swift:283-288: the swap "must replace in place, never slide or fade" with
        // `.transition(.identity)`: a removal that touches nothing does not linger for the transaction's animation
        // (documented SwiftUI behaviour the source relies on, UNVERIFIED under OD-0001 as ED-092 records).
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Identity);
        InsertAnimated(window, panel, child);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.False(IsUnder(child, panel));
        Assert.DoesNotContain(child, panel.Children);
        Assert.Empty(panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void An_asymmetric_transition_with_an_identity_removal_leaves_at_once_under_an_animation() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // UI/Markdown/MarkdownView.swift:46-53 `.asymmetric(insertion: .softAppear, removal: .identity)`: the card "goes
        // without a transition of its own"; the opacity insertion half stands in for the soft appear here.
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Opacity,
            removal: SwiftTransition.Identity));
        InsertAnimated(window, panel, child);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_modifier_insertion_with_an_identity_removal_takes_its_wrapper_out_at_once_under_an_animation() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ThreadTimeline.swift:316-320 rowTransition: insertion `.modifier(...).animation(.softAppear)`,
        // removal `.identity`. The row and the wrapper its modifier built leave the panel together, at once.
        var (window, panel) = Host();
        List<Control> bodies = [];
        List<double> updates = [];
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Modifier(
                active: new SettleGlideSample(1, bodies, updates), identity: new SettleGlideSample(0, bodies, updates))
                .Animation(Animation.EaseOut(duration: 0.2)),
            removal: SwiftTransition.Identity));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Single(bodies);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_transition_with_its_own_animation_animates_without_a_transaction_animation() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Composer/ComposerView.swift:665-673: the working dots appear and leave on `runtime.isRunning`
        // (`phase != .idle`, ThreadRuntime.swift:657), set outside any withAnimation (ThreadRuntime.swift:1196, :1351,
        // :2277), and fade on the transition's own `.animation(.easeInOut(duration: 0.4))`.
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity.Animation(Animation.EaseInOut(duration: 0.4)));
        Assert.Null(Transaction.Current);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        SwiftTransition.Remove(panel, child);
        Assert.True(IsUnder(child, panel));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_blur_replace_insertion_starts_blurred() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Usage/UsageViews.swift:273 `.transition(.blurReplace)`. Only "it starts blurred" is asserted: the
        // exact blur and scale are platform knowledge, UNVERIFIED under OD-0001 (ED-092).
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.BlurReplace);
        InsertAnimated(window, panel, child);
        Assert.True(BlurRadius(child, panel) > 0);
        var still = Box().Transition(SwiftTransition.BlurReplace);
        SwiftTransition.Insert(panel, 1, still);
        window.UpdateLayout();
        Assert.Equal(0, BlurRadius(still, panel), Tolerance);
        Assert.Equal(1, EffectiveOpacity(still, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-159")]
    [Trait("Oracle", "O-51")]
    public void Blur_replace_insertion_animates_from_the_parked_active_state() => headless.OnUiThread(() =>
    {
        // UNVERIFIED O-51: UsageViews.swift:273 calls blurReplace without configuration.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.BlurReplace);
        InsertAnimated(window, panel, child);
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(7, BlurRadius(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(5, 2), new Point(95, 38));
        clocks.Frame(0.0685);
        clocks.Frame(0.1315);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, BlurRadius(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-159")]
    [Trait("Oracle", "O-51")]
    public void Blur_replace_removal_ends_at_the_parked_active_state() => headless.OnUiThread(() =>
    {
        // UNVERIFIED O-51: UsageViews.swift:273 uses blurReplace for removal as well as insertion.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.BlurReplace);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.0685);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        Assert.Equal(3.5, BlurRadius(child, panel), 1e-6);
        AssertCorners(child, panel, new Point(2.5, 1), new Point(97.5, 39), tolerance: 1e-6);
        clocks.Frame(0.1315);
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-159")]
    [Trait("Oracle", "O-52")]
    public void Combined_transitions_keep_the_inner_scale_then_outer_offset() => headless.OnUiThread(() =>
    {
        // UNVERIFIED O-52: ChatView.swift:1098-1099 combines scale, opacity and offset.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Scale(scale: 0.6)
            .Combined(SwiftTransition.Opacity).Combined(SwiftTransition.Offset(y: 10)));
        InsertAnimated(window, panel, child);
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(20, 18), new Point(80, 42));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-159")]
    public void Identity_transition_does_not_clobber_the_controls_own_effects() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box();
        child.Opacity = 0.5;
        child.RenderTransform = new TranslateTransform(3, 4);
        child.Transition(SwiftTransition.Identity);
        InsertAnimated(window, panel, child);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(3, 4), new Point(103, 44));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void The_insertion_index_places_the_child_among_its_siblings() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var panel = new StackPanel();
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var first = Box().Transition(SwiftTransition.Opacity);
        var last = Box().Transition(SwiftTransition.Opacity);
        var middle = Box().Transition(SwiftTransition.Opacity);
        SwiftTransition.Insert(panel, 0, first);
        SwiftTransition.Insert(panel, 1, last);
        SwiftTransition.Insert(panel, 1, middle);
        window.UpdateLayout();
        Assert.Equal(0, first.TranslatePoint(new Point(0, 0), panel)!.Value.Y, Tolerance);
        Assert.Equal(40, middle.TranslatePoint(new Point(0, 0), panel)!.Value.Y, Tolerance);
        Assert.Equal(80, last.TranslatePoint(new Point(0, 0), panel)!.Value.Y, Tolerance);
        window.Close();
    });

    // Frame section. EaseOut(duration: 0.2) reaches 0.5 at 0.3425 of its duration, 0.0685 s (SwiftAnimationTests,
    // EaseOut_is_the_cubic_Bezier_0_0_0_58_1); spring(response: 0.32, dampingFraction: 0.9) is 0.2698264498200065 at
    // 0.05 s and settles in ln(1000) / (0.9 * 2 * pi / 0.32) = 0.3909 s; the glide spring is 0.30155107726937647 at
    // 0.1 s, 1.0049814577061145 at 0.6 s (its overshoot) and settles in 0.767 s (ED-011).

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void An_insertion_follows_its_curve_at_each_frame_and_settles_at_identity_once_its_duration_is_complete() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        InsertAnimated(window, panel, child);
        Assert.Equal(1, clocks.Frames.Pending);
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        Assert.Equal(1, clocks.Frames.Pending);
        clocks.Frame(0.1315);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-098")]
    [Trait("Decision", "ED-117")]
    public void Frames_are_the_only_wake_ups_and_elapsed_time_is_read_on_SwiftClock() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        InsertAnimated(window, panel, child);
        clocks.Frames.Advance(TimeSpan.FromMilliseconds(16)); // a frame with SwiftClock held still: no time has passed
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(1, clocks.Frames.Pending);
        clocks.Time.Advance(TimeSpan.FromTicks(685_000)); // time without a frame: nothing moves
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frames.Advance(TimeSpan.FromMilliseconds(16)); // the next frame reads 0.0685 s on SwiftClock
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void A_removal_follows_its_curve_and_the_child_leaves_at_the_frame_its_duration_is_complete() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.0685);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        clocks.Frame(0.1315);
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void A_spring_insertion_samples_the_closed_form_for_scale_and_opacity_and_ends_at_identity_once_settled() => headless.OnUiThread(() =>
    {
        // App/Views/Chat/ChatView.swift:601-605 panelTransition under UI/Chrome/Chrome.swift:102 panelSlide.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Scale(scale: 0.92).Combined(SwiftTransition.Opacity),
            removal: SwiftTransition.Scale(scale: 0.96).Combined(SwiftTransition.Opacity)));
        Transaction.WithAnimation(Animation.Spring(response: 0.32, dampingFraction: 0.9), () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        clocks.Frame(0.05);
        var p = 0.2698264498200065;
        var scale = 0.92 + (1 - 0.92) * p;
        AssertCorners(child, panel, new Point(50 - 50 * scale, 20 - 20 * scale), new Point(50 + 50 * scale, 20 + 20 * scale));
        Assert.Equal(p, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frame(0.35); // 0.4 s: past the 0.3909 s settling duration
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-098")]
    public void Without_an_animation_a_modifier_transition_wraps_the_content_once_at_identity_and_removes_it_at_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        List<Control> bodies = [];
        List<double> updates = [];
        var child = Box().Transition(SwiftTransition.Modifier(
            active: new SettleGlideSample(1, bodies, updates), identity: new SettleGlideSample(0, bodies, updates)));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Single(bodies);
        Assert.Same(bodies[0], child.Parent);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(0, updates[^1]);
        SwiftTransition.Remove(panel, child);
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        Assert.Single(bodies);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-098")]
    public void A_modifier_insertion_runs_its_animatable_data_from_active_to_identity_with_the_curve() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        List<Control> bodies = [];
        List<double> updates = [];
        var child = Box().Transition(SwiftTransition.Modifier(
            active: new SettleGlideSample(1, bodies, updates), identity: new SettleGlideSample(0, bodies, updates)));
        InsertAnimated(window, panel, child);
        Assert.Single(bodies);
        Assert.Equal(1, updates[^1]);
        clocks.Frame(0.0685);
        Assert.Equal(0.5, updates[^1], 1e-6);
        clocks.Frame(0.1315);
        Assert.Equal(0, updates[^1], Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        Assert.Single(bodies);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-011")]
    [Trait("Decision", "ED-098")]
    public void A_modifier_removal_on_the_transition_s_own_spring_interpolates_with_the_overshoot_then_the_control_leaves() => headless.OnUiThread(() =>
    {
        // UI/Chrome/Chrome.swift:994-999 settleGlide's removal half: `.modifier(active: SettleGlideModifier(progress: 1),
        // identity: SettleGlideModifier(progress: 0)).animation(.spring(Chrome.glideSpring))`; the insertion half is
        // trimmed to identity here.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        List<Control> bodies = [];
        List<double> updates = [];
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Identity,
            removal: SwiftTransition.Modifier(active: new SettleGlideSample(1, bodies, updates), identity: new SettleGlideSample(0, bodies, updates))
                .Animation(Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86)))));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Single(bodies);
        Assert.Equal(0, updates[^1]);
        SwiftTransition.Remove(panel, child); // outside withAnimation: the transition's own spring animates it
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.1);
        Assert.Equal(0.30155107726937647, updates[^1], Tolerance);
        clocks.Frame(0.5);
        Assert.Equal(1.0033822145841729, updates[^1], Tolerance); // the free spring's 1.0049814577061145 with its distance from 1 drawn in after its last crossing (ED-NEW-perf-motion-10 (ED-333 UNRESOLVED run-out, the lead's ruling 2026-10-13))
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.2); // 0.8 s: past the 0.767 s settling duration
        Assert.Equal(1, updates[^1], Tolerance);
        Assert.False(IsUnder(child, panel));
        Assert.Equal(0, clocks.Frames.Pending);
        Assert.Single(bodies);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-098")]
    public void A_modifier_without_animatable_data_takes_its_end_state_at_the_start_and_leaves_when_the_animation_ends() => headless.OnUiThread(() =>
    {
        // App/Views/Composer/ComposerView.swift:940-941 ChipRowCollapse under UI/Chrome/Chrome.swift:102 panelSlide.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        List<Control> bodies = [];
        List<bool> updates = [];
        var child = Box().Transition(SwiftTransition.Modifier(
            active: new ChipRowCollapseSample(true, bodies, updates), identity: new ChipRowCollapseSample(false, bodies, updates)));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.False(updates[^1]);
        Transaction.WithAnimation(Animation.Spring(response: 0.32, dampingFraction: 0.9), () => SwiftTransition.Remove(panel, child));
        Assert.True(updates[^1]);
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.1);
        Assert.True(updates[^1]);
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.3); // 0.4 s: past the 0.3909 s settling duration
        Assert.False(IsUnder(child, panel));
        Assert.Single(bodies);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-011")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void Both_modifier_halves_of_the_settle_glide_shape_wrap_the_content_and_each_updates_its_own_wrapper() => headless.OnUiThread(() =>
    {
        // UI/Chrome/Chrome.swift:994-999 settleGlide, whole: insertion `.modifier(active: SoftAppearModifier(isVisible:
        // false), identity: SoftAppearModifier(isVisible: true)).animation(.softAppear)` (:960 `.smooth(duration: 0.32)`),
        // removal `.modifier(active: SettleGlideModifier(progress: 1), identity: SettleGlideModifier(progress: 0))
        // .animation(.spring(Chrome.glideSpring))`. Each modifier wraps the content itself; the fixture's Update asserts
        // it is handed the wrapper its own Body built. SoftAppearModifier is stood in by a second SettleGlideSample with
        // its own logs (one animatable value from 1 to 0). smooth(duration: 0.32) is the critically damped spring,
        // 1 - e^(-wt)(1 + wt) with w = 2 pi / 0.32: 0.5840232690915959 at 0.1 s; it settles in 0.3518 s.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        List<Control> appearBodies = [];
        List<double> appearUpdates = [];
        List<Control> glideBodies = [];
        List<double> glideUpdates = [];
        var child = Box().Transition(SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Modifier(active: new SettleGlideSample(1, appearBodies, appearUpdates), identity: new SettleGlideSample(0, appearBodies, appearUpdates))
                .Animation(Animation.Smooth(duration: 0.32)),
            removal: SwiftTransition.Modifier(active: new SettleGlideSample(1, glideBodies, glideUpdates), identity: new SettleGlideSample(0, glideBodies, glideUpdates))
                .Animation(Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86)))));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Single(appearBodies);
        Assert.Single(glideBodies);
        Assert.True(IsUnder(child, appearBodies[0]));
        Assert.True(IsUnder(child, glideBodies[0]));
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, appearUpdates[^1]);
        Assert.Equal(0, glideUpdates[^1]);
        clocks.Frame(0.1);
        Assert.Equal(1 - 0.5840232690915959, appearUpdates[^1], Tolerance);
        clocks.Frame(0.3); // 0.4 s: past the 0.3518 s settling duration
        Assert.Equal(0, appearUpdates[^1], Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        SwiftTransition.Remove(panel, child);
        clocks.Frame(0.1);
        Assert.Equal(0.30155107726937647, glideUpdates[^1], Tolerance);
        Assert.Equal(0, appearUpdates[^1], Tolerance);
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.7); // 0.8 s: past the 0.767 s settling duration
        Assert.Equal(1, glideUpdates[^1], Tolerance);
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        Assert.Equal(0, clocks.Frames.Pending);
        Assert.Single(appearBodies);
        Assert.Single(glideBodies);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-159")]
    public void Reinserting_a_child_during_its_animated_removal_keeps_it_once_under_the_panel() => headless.OnUiThread(() =>
    {
        // A conditional view whose state flips back before its removal ends, e.g. App/Views/Chat/HydraButton.swift:42
        // `.transition(.scale(scale: 0.6).combined(with: .opacity))`. The removal is cancelled; the view is inserted
        // again and the removal's frames never take it out. (ED-159 follow-up: how an interrupted transition continues
        // is not ruled, so only the end state is asserted.)
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.05);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Insert(panel, 0, child));
        window.UpdateLayout();
        Assert.Single(panel.Children);
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.25); // past both 0.2 s curves
        Assert.Single(panel.Children);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void Reinserting_a_child_without_an_animation_during_its_animated_removal_cancels_the_removal() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box().Transition(SwiftTransition.Opacity);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        clocks.Frame(0.05);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Single(panel.Children);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        clocks.Frame(0.25);
        Assert.Single(panel.Children);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_child_without_a_transition_fades_in_and_out_under_an_animation() => headless.OnUiThread(() =>
    {
        // SwiftUI's default transition for a view with no `.transition(_:)` is `.opacity`: documented SwiftUI behaviour,
        // platform knowledge rather than a B2 fact, UNVERIFIED under OD-0001 as ED-092 records for the other defaults.
        // Many B2 conditional views inside withAnimation carry no `.transition`.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box();
        InsertAnimated(window, panel, child);
        Assert.True(IsUnder(child, panel));
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        clocks.Frame(0.1315);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        Assert.Equal(0, clocks.Frames.Pending);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.True(IsUnder(child, panel));
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.1315);
        Assert.False(IsUnder(child, panel));
        Assert.Empty(panel.Children);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    public void A_child_never_inserted_through_the_applier_still_fades_out_under_an_animation() => headless.OnUiThread(() =>
    {
        // The same `.opacity` default (UNVERIFIED under OD-0001, ED-092) for a child added to the panel directly.
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = Box();
        panel.Children.Add(child);
        window.UpdateLayout();
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        Assert.True(IsUnder(child, panel));
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        clocks.Frame(0.1315);
        Assert.False(IsUnder(child, panel));
        window.Close();
    });

    // Review point (ED-092 (2), R-VIEW-4): both B2 move sites are sized by their content, with no fixed frame
    // (App/Views/Chat/ChatView.swift:277-278 `TerminalPanel(...).transition(.move(edge: .bottom).combined(with: .opacity))`,
    // App/Views/Usage/UsageViews.swift:250-251 `.padding(.top, 12).transition(.opacity.combined(with: .move(edge: .top)))`).
    // Such a child has no size until its first layout pass. Move's identity state is a zero offset whatever the size;
    // its active state is one own size towards the edge, the size the layout gives the child.
    private static Border ContentSized() => new() { Child = new Border { Width = 100, Height = 40 } };

    [Theory]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [InlineData(Edge.Top)]
    [InlineData(Edge.Bottom)]
    [InlineData(Edge.Leading)]
    [InlineData(Edge.Trailing)]
    public void Without_an_animation_a_content_sized_move_child_is_inserted_at_identity(Edge edge) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = ContentSized().Transition(SwiftTransition.Move(edge: edge));
        Assert.Null(Transaction.Current);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Equal(new Size(100, 40), child.Bounds.Size);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Theory]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [InlineData(Edge.Top, 0, -40)]
    [InlineData(Edge.Bottom, 0, 40)]
    [InlineData(Edge.Leading, -100, 0)]
    [InlineData(Edge.Trailing, 100, 0)]
    public void A_content_sized_move_insertion_starts_one_own_laid_out_size_beyond_its_edge(Edge edge, double x, double y) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var (window, panel) = Host();
        var child = ContentSized().Transition(SwiftTransition.Move(edge: edge));
        InsertAnimated(window, panel, child);
        AssertCorners(child, panel, new Point(x, y), new Point(x + 100, y + 40));
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void A_content_sized_move_and_opacity_insertion_follows_its_curve_from_below_and_settles_at_identity() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Chat/ChatView.swift:278 `.transition(.move(edge: .bottom).combined(with: .opacity))`
        var (window, panel) = Host();
        var child = ContentSized().Transition(SwiftTransition.Move(edge: Edge.Bottom).Combined(SwiftTransition.Opacity));
        InsertAnimated(window, panel, child);
        Assert.Equal(0, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 40), new Point(100, 80));
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        AssertCorners(child, panel, new Point(0, 20), new Point(100, 60), 1e-4);
        clocks.Frame(0.1315);
        Assert.Equal(1, EffectiveOpacity(child, panel), Tolerance);
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        Assert.Equal(0, clocks.Frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-098")]
    public void A_content_sized_opacity_and_move_removal_leaves_towards_its_edge() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // App/Views/Usage/UsageViews.swift:251 `.transition(.opacity.combined(with: .move(edge: .top)))`
        var (window, panel) = Host();
        var child = ContentSized().Transition(SwiftTransition.Opacity.Combined(SwiftTransition.Move(edge: Edge.Top)));
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.2), () => SwiftTransition.Remove(panel, child));
        AssertCorners(child, panel, new Point(0, 0), new Point(100, 40));
        clocks.Frame(0.0685);
        Assert.Equal(0.5, EffectiveOpacity(child, panel), 1e-6);
        AssertCorners(child, panel, new Point(0, -20), new Point(100, 20), 1e-4);
        clocks.Frame(0.1315);
        Assert.False(IsUnder(child, panel));
        window.Close();
    });
}
