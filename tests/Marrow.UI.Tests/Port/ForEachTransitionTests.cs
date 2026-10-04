using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-218: shared collection mechanics. Geometry is a 100x40 row; linear 0.4s isolates curve/clock behavior.
[Collection("Headless")]
public sealed class ForEachTransitionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")] // App/Views/Settings/MCPSettingsPage.swift:54-58; collection mechanics only
    public void Scale_opacity_insertion_and_removal_use_frames_and_dispose_only_after_the_exit() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var host = new ForEachHost(row => row.Transition(SwiftTransition.Scale(0.9).Combined(SwiftTransition.Opacity)));
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a"])); host.Layout();
        var row = Assert.Single(host.Made);
        Assert.Equal(0, host.Opacity(row)); Assert.Equal(5, host.Origin(row).X, 8); Assert.Equal(2, host.Origin(row).Y, 8);
        clocks.Time.Advance(TimeSpan.FromSeconds(0.2)); Assert.Equal(0, host.Opacity(row));
        clocks.Frames.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(0.5, host.Opacity(row), 8); Assert.Equal(2.5, host.Origin(row).X, 8);
        clocks.Frame(0.2); Assert.Equal(1, host.Opacity(row));
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply([]));
        Assert.NotEmpty(host.Panel.Children); Assert.Empty(host.Disposed);
        clocks.Frame(0.2); Assert.Equal(0.5, host.Opacity(row), 8); Assert.Empty(host.Disposed);
        clocks.Frame(0.2); Assert.Empty(host.Panel.Children); Assert.Equal(["a"], host.Disposed);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")] // App/Views/Settings/MCPSettingsPage.swift:54-58; collection mechanics only
    public void A_transition_with_its_own_animation_runs_without_a_transaction() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        using var host = new ForEachHost(row => row.Transition(SwiftTransition.Opacity.Animation(Animation.Linear(0.4))));
        host.Rows.Apply(["a"]); Assert.Equal(0, host.Opacity(host.Made[0]));
        clocks.Frame(0.4); host.Rows.Apply([]); Assert.Empty(host.Disposed);
        clocks.Frame(0.4); Assert.Equal(["a"], host.Disposed); Assert.Empty(host.Panel.Children);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void An_animated_transaction_uses_the_default_opacity_for_a_plain_row() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a"]));
        Assert.Equal(0, host.Opacity(host.Made[0])); clocks.Frame(0.4);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply([]));
        Assert.Empty(host.Disposed); clocks.Frame(0.4); Assert.Equal(["a"], host.Disposed);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void Modifier_wrappers_keep_identity_order_and_delayed_disposal() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); var bodies = new List<Control>(); var updates = new List<double>();
        using var host = new ForEachHost(row => row.Transition(SwiftTransition.Modifier(new SettleGlideSample(0, bodies, updates), new SettleGlideSample(1, bodies, updates))));
        host.Rows.Apply(["a", "b"]); host.Layout(); Assert.Equal(2, bodies.Count);
        host.Rows.Apply(["b", "a"]); Assert.Equal(2, bodies.Count); Assert.Same(bodies[1], host.Panel.Children[0]);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b"])); Assert.Empty(host.Disposed);
        clocks.Frame(0.4); Assert.Equal(["a"], host.Disposed); Assert.Single(host.Panel.Children);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void A_returning_id_has_a_new_identity_and_cannot_be_disposed_by_the_old_exit() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost(row => row.Transition(SwiftTransition.Opacity));
        host.Rows.Apply(["a", "b"]); var old = host.Made[0];
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b"]));
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "b"]));
        Assert.Equal(3, host.Made.Count); Assert.NotSame(old, host.Made[2]);
        clocks.Frame(0.4); Assert.Equal(["a"], host.Disposed); Assert.Contains(host.Made[2], host.Panel.Children);
        Assert.Equal(new Control[] { host.Made[2], host.Made[1] }, host.Panel.Children);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void Reordered_survivors_begin_at_old_positions_and_follow_the_transaction_curve_to_zero() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        host.Rows.Apply(["a", "b", "c"]); host.Layout(); var a = host.Made[0]; var c = host.Made[2];
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["c", "b", "a"])); host.Layout();
        Assert.Equal(0, host.Origin(a).Y, 8); Assert.Equal(80, host.Origin(c).Y, 8);
        clocks.Frame(0.2); Assert.Equal(40, host.Origin(a).Y, 8); Assert.Equal(40, host.Origin(c).Y, 8);
        clocks.Frame(0.2); Assert.Equal(80, host.Origin(a).Y, 8); Assert.Equal(0, host.Origin(c).Y, 8);
        Assert.Empty(host.Disposed);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void An_interrupted_glide_starts_at_the_current_visual_position_and_preserves_the_original_transform() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        host.Rows.Apply(["a", "b"]); host.Layout(); var a = host.Made[0];
        var original = new TranslateTransform(7, 3); a.RenderTransform = original;
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b", "a"])); host.Layout();
        clocks.Frame(0.2); Assert.Equal(23, host.Origin(a).Y, 8);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "b"])); host.Layout();
        Assert.Equal(23, host.Origin(a).Y, 8); clocks.Frame(0.4);
        Assert.Equal(3, host.Origin(a).Y, 8); Assert.Same(original, a.RenderTransform);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-6")] // App/Window/WindowManager.swift:113-140; injected environment gate only
    public void Reduce_motion_or_live_resize_suppresses_glides(bool liveResize) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        if (liveResize)
        {
            var seam = typeof(EnvironmentValues).GetMethod("SetWindowIsLiveResizing"); Assert.NotNull(seam);
            seam.Invoke(null, new object[] { host.Panel, (Func<bool>)(() => true) });
        }
        else EnvironmentValues.SetAccessibilityReduceMotion(host.Panel, true);
        host.Rows.Apply(["a", "b"]); host.Layout();
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b", "a"])); host.Layout();
        Assert.Equal(40, host.Origin(host.Made[0]).Y, 8); Assert.Equal(0, host.Origin(host.Made[1]).Y, 8);
        Assert.Equal(0, clocks.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void Detach_cancels_a_glide_and_restores_the_row_transform() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        host.Rows.Apply(["a", "b"]); host.Layout();
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b", "a"])); host.Layout();
        Assert.True(clocks.Frames.Pending > 0); host.Window.Close();
        Assert.Equal(0, clocks.Frames.Pending); Assert.Null(host.Made[0].RenderTransform);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")] // App/Views/Settings/MCPSettingsPage.swift:54-58; collection mechanics only
    public void A_grid_spring_glide_uses_the_critical_spring_curve_on_the_shared_clock() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        host.Rows.Apply(["a", "b"]); host.Layout();
        Transaction.WithAnimation(Animation.Spring(duration: 0.35), () => host.Rows.Apply(["b", "a"])); host.Layout();
        clocks.Frame(0.1);
        var omega = 2 * Math.PI / 0.35;
        var progress = 1 - Math.Exp(-omega * 0.1) * (1 + omega * 0.1); // ED-011 critical spring, not a sampled implementation output
        Assert.Equal(40 * progress, host.Origin(host.Made[0]).Y, 8);
        Assert.Equal(40 * (1 - progress), host.Origin(host.Made[1]).Y, 8);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void A_size_change_is_final_at_layout_while_a_surviving_neighbor_glides() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost();
        host.Rows.Apply(["a", "b"]); host.Layout(); host.Made[0].Height = 80;
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "b"])); host.Layout();
        Assert.Equal(80, host.Made[0].Bounds.Height); Assert.Null(host.Made[0].RenderTransform);
        Assert.Equal(40, host.Origin(host.Made[1]).Y, 8); clocks.Frame(0.4);
        Assert.Equal(80, host.Origin(host.Made[1]).Y, 8);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-6")] // App/Window/WindowManager.swift:113-140; injected environment gate only
    public void Live_resize_beginning_during_a_glide_stops_it_on_the_next_frame() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost(); var resizing = false;
        var seam = typeof(EnvironmentValues).GetMethod("SetWindowIsLiveResizing"); Assert.NotNull(seam);
        seam.Invoke(null, new object[] { host.Panel, (Func<bool>)(() => resizing) });
        host.Rows.Apply(["a", "b"]); host.Layout();
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["b", "a"])); host.Layout();
        Assert.Equal(0, host.Origin(host.Made[0]).Y, 8); resizing = true; clocks.Frame(0.1);
        Assert.Equal(40, host.Origin(host.Made[0]).Y, 8); Assert.Equal(0, clocks.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")]
    public void Closing_the_panel_ends_pending_removals_and_disposes_each_row_once() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks(); using var host = new ForEachHost(row => row.Transition(SwiftTransition.Opacity));
        host.Rows.Apply(["a"]);
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply([])); Assert.Empty(host.Disposed);
        host.Window.Close(); Assert.Equal(["a"], host.Disposed); Assert.Equal(0, clocks.Frames.Pending);
        clocks.Frame(0.4); Assert.Equal(["a"], host.Disposed);
    });

    [Theory]
    [InlineData(false, 0.1)]
    [InlineData(false, 1.0)]
    [InlineData(true, 0.1)]
    [InlineData(true, 1.0)]
    [Trait("Decision", "ED-218")]
    [Trait("Rule", "R-VIEW-8")] // App/Views/Settings/MCPSettingsPage.swift:54-58; concurrent transition and keyed layout
    public void Reordering_during_scale_or_move_insertion_composes_both_animations_and_finishes_at_identity(bool move, double glideSeconds) => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        // :54 supplies scale 0.9 + opacity. Move uses ED-092's own-height offset. Linear durations and
        // 100x40 rows are mechanics fixtures; ED-218 requires both effects regardless of which finishes first.
        using var host = new ForEachHost(row => row.Transition(
            (move ? SwiftTransition.Move(Edge.Top) : SwiftTransition.Scale(0.9)).Combined(SwiftTransition.Opacity)));
        Transaction.WithAnimation(Animation.Linear(0.4), () => host.Rows.Apply(["a", "b"])); host.Layout();
        var a = host.Made[0]; var b = host.Made[1];
        clocks.Frame(0.1);
        var insertionY = move ? -30 : 1.5; // 25% insertion: -40*0.75 or (1-0.925)*40/2.
        Assert.Equal(insertionY, host.Origin(a).Y, 8);
        Assert.Equal(40 + insertionY, host.Origin(b).Y, 8);
        Assert.Equal(0.25, host.Opacity(a), 8);
        Transaction.WithAnimation(Animation.Linear(glideSeconds), () => host.Rows.Apply(["b", "a"])); host.Layout();
        Assert.Same(a, host.Panel.Children[1]); Assert.Same(b, host.Panel.Children[0]);
        Assert.Equal(insertionY, host.Origin(a).Y, 8); Assert.Equal(40 + insertionY, host.Origin(b).Y, 8);

        clocks.Frame(0.1);
        var glide = Math.Min(0.1 / glideSeconds, 1); // elapsed since reorder, independent of insertion progress.
        insertionY = move ? -20 : 1; // 50% insertion: -40*0.5 or (1-0.95)*40/2.
        Assert.Equal(40 * glide + insertionY, host.Origin(a).Y, 8);
        Assert.Equal(40 * (1 - glide) + insertionY, host.Origin(b).Y, 8);
        Assert.Equal(move ? 0 : 2.5, host.Origin(a).X, 8);
        Assert.Equal(0.5, host.Opacity(a), 8);

        clocks.Frame(0.2); // Insertion finishes at 0.4 s; the long glide has advanced only 0.3 s.
        glide = Math.Min(0.3 / glideSeconds, 1);
        Assert.Equal(40 * glide, host.Origin(a).Y, 8); Assert.Equal(40 * (1 - glide), host.Origin(b).Y, 8);
        Assert.Equal(0, host.Origin(a).X, 8); Assert.Equal(1, host.Opacity(a));
        clocks.Frame(0.7); // Even the 1 s glide finishes at 1.1 s; no partial insertion transform may return.
        Assert.Equal(40, host.Origin(a).Y, 8); Assert.Equal(0, host.Origin(b).Y, 8);
        Assert.Equal(Avalonia.Matrix.Identity, a.RenderTransform?.Value ?? Avalonia.Matrix.Identity);
        Assert.Equal(Avalonia.Matrix.Identity, b.RenderTransform?.Value ?? Avalonia.Matrix.Identity);
        Assert.Equal(1, host.Opacity(b)); Assert.Equal(0, clocks.Frames.Pending); Assert.Empty(host.Disposed);
    });

    // STOP ForEach transitions #1 (the PR-WIN-017 half CLOSED 2026-10-04: native two-window resize and move-only drag are
    // proved by Marrow.App.Tests WindowManagerNativeTests, Req PR-WIN-017; the UI-MCP-018 half is still open)
    // Kind: partial record verification. Swift: MCPSettingsPage.swift:249-303,54-58; WindowManager.swift:118-140.
    // Records: UI-MCP-018, PR-WIN-017. These foundation fixtures prove ED-218 mechanics and environment gating,
    // not the full MCP card geometry/material/states.
    // Needed: the owning MCP card integration/visual checks.
    // Whole-record Req traits are deliberately absent here; existing assertions and Decision traits remain.
}
