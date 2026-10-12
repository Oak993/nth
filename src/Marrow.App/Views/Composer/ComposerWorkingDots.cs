using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :1-192.

/// <summary>
/// The composer's working animation: a field of accent-tinted dots along the pill's
/// bottom, pulsing in a wave that flows left to right and fades out upward. (:5-11)
/// </summary>
public sealed class ComposerWorkingDots : Border
{
    // PORT-ONLY CZ-5 (customization-SPEC CZ-10.4; OWNER OD-0089 amendment): the colour the dots wear. The source draws them in Chrome.accentNSColor (:13-19), which stays the default; the
    // workingDots setting (WorkingDotsColor) can hand another. A new colour reaches the running field through Retarget(ease: true), the field's own 0.32 s ease; the still dots take it at once.
    public SwiftColor Tint
    {
        get => tint;
        set
        {
            // an instance the field already wears starts nothing, a new one eases (animated) or lands at once (still)
            tint = value;
            dotField.UpdateColor(value, ease: true);
        }
    }

    private readonly DotFieldControl dotField;
    private SwiftColor tint = Chrome.AccentNSColor;

    // :13-19 Uses DotFieldLayerView with Chrome.accentNSColor and animated: !reduceMotion
    public ComposerWorkingDots()
    {
        // :15-18 Passes accessibility settings to the underlying control
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        // Create the underlying dot field
        dotField = new DotFieldControl(tint, animated: true, sweeps: false) { EaseAppearanceChanges = true };

        Child = dotField;

        // Track accessibility changes
        this.Track(
            () => EnvironmentValues.GetAccessibilityReduceMotion(this),
            motion => dotField.UpdateAnimated(!motion)
        );
        // :13-19, :104-111 the body re-reads accessibilityReduceMotion on every update and DotFieldLayerView.configure reinstalls only when the
        // flag changed, so turning reduce motion on or off while a turn runs takes effect without rebuilding the view. The attached
        // environment property is not observable state (Track runs once on attach), so a later change arrives through the property
        // notification, as in DotFieldSweep below.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
                dotField.UpdateAnimated(!EnvironmentValues.GetAccessibilityReduceMotion(this));
        };
    }
}

/// <summary>
/// The same field over a whole surface, once: one wave of dots in tint flows through it
/// from left to right and is gone, the way a head's panel marks its head finishing. With
/// reduced motion nothing is drawn. (:22-64)
/// </summary>
public sealed class DotFieldSweep : Border
{
    // :27-28 Takes tint parameter
    public static readonly StyledProperty<SwiftColor> TintProperty =
        AvaloniaProperty.Register<DotFieldSweep, SwiftColor>(nameof(Tint), new SwiftColor(1, 1, 1));

    public SwiftColor Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public DotFieldSweep()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        // :30-34 Only show if not reduce motion. The environment value is inherited from the hosting window, so it is read
        // again on attach and whenever it changes: the body re-evaluates, removing the field or making a fresh one that
        // starts its wave.
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        Apply(reduceMotion);
        // The attached environment property is not observable state: attaching applies the inherited value once (Track), and a
        // later change reaches the sweep through the property notification.
        this.Track(() => EnvironmentValues.GetAccessibilityReduceMotion(this), Apply);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Apply(EnvironmentValues.GetAccessibilityReduceMotion(this));
        };
    }

    private void Apply(bool reduceMotion)
    {
        if (reduceMotion) Child = null;
        else if (Child is null) Child = new DotFieldControl(Tint, animated: true, sweeps: true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // :24-34 the supplied tint reaches the field on every update (configure(color:animated:) recolors the layer).
        if (change.Property == TintProperty && Child is DotFieldControl field) field.UpdateColor(Tint);
    }
}

/// <summary>
/// The actual dot field visual. (:38-192)
/// Animated replicas are drawn by the compositor; static dots retain their layout and brushes.
/// </summary>
public sealed class DotFieldControl : Canvas
{
    // :67-75 Static configuration constants
    private const double DotSize = 2;           // :67
    private const double Pitch = 8;             // :68
    private const int MaxRows = 3;              // :71

    // :60-62 Instance state
    private SwiftColor color;
    private bool animated;
    private bool sweeps;
    // PORT-ONLY CZ-5, CZ-10.4 / Q-CZ-7 / OD-0076: ComposerWorkingDots (including the preview) eases every appearance change.
    // Raw fields and finishing sweeps retain their existing ED-253 rules; Retarget still lands instantly when motion is reduced.
    internal bool EaseAppearanceChanges { get; init; }
    private Size laidOutSize;                   // :62

    // Visual tree for rendering
    private Canvas? dotCanvas;

    public Visual? Layer => dotCanvas;
    private CompositionCustomVisual? compositionVisual;
    private DotFieldCompositionHandler? compositionHandler;
    internal DotFieldCompositionHandler? CompositionHandler => compositionHandler;
    internal DotFieldCompositionFrame? LastRenderedFrame => compositionHandler?.LastFrame;
    private ContinuousInstant sweepStart;
    // ED-253 (round 4): the resolved colour the field is drawn in, and the blend to it from what it showed (render server).
    private Color? target;
    private Color? blendFrom;
    private ContinuousInstant blendStart;
    private IPlatformSettings? platformSettings;
    private IDisposable? warmupActivity;

    public DotFieldControl(SwiftColor color, bool animated, bool sweeps)
    {
        this.color = color;
        this.animated = animated;
        this.sweeps = sweeps;
        this.laidOutSize = default;
        UseLayoutRounding = false; // :124-138 bounds and layer positions retain fractional points.

        // :84-86 Initialize visual properties
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        // Create the canvas for rendering
        dotCanvas = new Canvas { IsVisible = !animated };
        Children.Add(dotCanvas);
        // :113-122 Window attachment installs motion; theme-aware brushes also follow system accent changes.
        AttachedToVisualTree += (_, _) =>
        {
            InstallAnimation();
            warmupActivity ??= GpuWarmup.AddActivity(IsAnimatingOnCompositor);
            Marrow.UI.Theme.ThemeManager.DidChange += OnThemeChanged;
            platformSettings = Application.Current?.PlatformSettings;
            if (platformSettings is not null) platformSettings.ColorValuesChanged += OnColorValuesChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Marrow.UI.Theme.ThemeManager.DidChange -= OnThemeChanged;
            if (platformSettings is not null) platformSettings.ColorValuesChanged -= OnColorValuesChanged;
            platformSettings = null;
            RemoveComposition();
            warmupActivity?.Dispose();
            warmupActivity = null;
        };
        ActualThemeVariantChanged += (_, _) => OnAppearanceChanged();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);

        // :124-150 Layout the dot field
        if (finalSize.Width > 0 && finalSize.Height > 0 && finalSize != laidOutSize)
        {
            laidOutSize = finalSize;
            LayoutDots(finalSize);
        }

        return result;
    }

    private void LayoutDots(Size size)
    {
        // :127-132 Calculate grid dimensions
        var columnCount = (int)Math.Ceiling(size.Width / Pitch) + 1;
        var rowCount = sweeps
            ? Math.Max(1, (int)Math.Ceiling(size.Height / Pitch))
            : Math.Min(MaxRows, Math.Max(1, (int)Math.Ceiling(size.Height / Pitch)));

        // ED-NEW-lean-9 (hidden grid): the dot layers are laid out again in place: the dots already there are kept, moved and given
        // the new fill and opacity, the missing ones are added at the end and the extra ones removed from it, so a new size makes no
        // new dot but those it adds (it cleared and recreated every dot, 1.87 MB a one-point resize). The same dots in the same
        // order, at the same places, fills and opacities as a fresh layout (ComposerWorkingDotsResizeBudgetTests).
        var fill = StillBrush();
        var count = rowCount * columnCount;
        if (dotCanvas is { } canvas)
        {
            var dots = canvas.Children;
            if (dots.Count > count) dots.RemoveRange(count, dots.Count - count);
            // :139-148 Position dots based on grid
            for (int row = 0, place = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columnCount; col++, place++)
                {
                    var x = Pitch / 2 + col * Pitch;
                    // :137-145 AppKit's origin is bottom-left. Avalonia's is top-left.
                    var y = size.Height - (Pitch / 2 + row * Pitch);
                    Ellipse dot;
                    if (place < dots.Count)
                    {
                        dot = (Ellipse)dots[place];
                        dot.Fill = fill;
                        dot.Opacity = DotOpacity(row, rowCount);
                    }
                    else
                    {
                        dot = CreateDot(row, rowCount, fill);
                        dots.Add(dot);
                    }
                    Canvas.SetLeft(dot, x - DotSize / 2);
                    Canvas.SetTop(dot, y - DotSize / 2);
                }
            }
        }

        SynchronizeComposition();
    }

    private Ellipse CreateDot(int row, int rowCount, IBrush fill)
    {
        var ellipse = new Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Fill = fill,
        };

        ellipse.Opacity = DotOpacity(row, rowCount);
        return ellipse;
    }

    private double DotOpacity(int row, int rowCount)
    {
        // :148 Apply per-row alpha offset
        var baseOpacity = animated ? 0.08 : (sweeps ? 0 : 0.28);
        // :148 Replicator instance alpha modulates the base layer's opacity.
        return baseOpacity * (sweeps ? 1 : 1.0 - (double)row / rowCount);
    }

    // ED-253 (round 4): the colour is resolved here and sent as it is (an immutable brush); a SwiftColor brush made at each
    // resend kept a binding listening to the field for as long as the field lived. The still dots (shown under Reduce Motion)
    // take a change at once, as every accent use does under Reduce Motion.
    private Color Resolved() => color.Resolve(ActualThemeVariant);

    private IBrush StillBrush() => new ImmutableSolidColorBrush(target ??= Resolved());

    /// <summary>
    /// Draws the field in the colour it resolves now. With <paramref name="ease"/> the animated field blends there on the
    /// render server from what it shows, over the accent's 0.32 s ease (SPEC 6.4), a later change retargeting from where
    /// the blend has got to; otherwise it is at once.
    /// </summary>
    private void Retarget(bool ease)
    {
        var to = Resolved();
        var now = SwiftClock.Now;
        var shown = target is { } current ? DotFieldCompositionFrame.Shown(current, blendFrom, blendStart, now) : to;
        if (ease && animated && shown != to)
        {
            blendFrom = shown;
            blendStart = now;
        }
        else blendFrom = null;
        target = to;
    }

    private void InstallAnimation()
    {
        // :113-116,152-190 Reattachment and a motion change reinstall; color/layout never reset the sweep.
        sweepStart = SwiftClock.Now;
        if (dotCanvas is not null)
        {
            dotCanvas.IsVisible = !animated;
            var columns = Math.Max(1, (int)Math.Ceiling(laidOutSize.Width / Pitch) + 1);
            var rows = sweeps ? Math.Max(1, (int)Math.Ceiling(laidOutSize.Height / Pitch))
                : Math.Min(MaxRows, Math.Max(1, (int)Math.Ceiling(laidOutSize.Height / Pitch)));
            for (var i = 0; i < dotCanvas.Children.Count; i++)
                dotCanvas.Children[i].Opacity = (sweeps ? 0 : 0.28) * (sweeps ? 1 : 1.0 - (double)(i / columns) / rows);
        }
        SynchronizeComposition();
    }

    private void SynchronizeComposition()
    {
        if (TopLevel.GetTopLevel(this) is null) return;
        if (compositionVisual is null)
        {
            if (ElementComposition.GetElementVisual(this) is not { } host) return;
            compositionHandler = new DotFieldCompositionHandler(Marrow.UI.Port.AmbientRefreshPacing.For(host.Compositor));
            compositionVisual = host.Compositor.CreateCustomVisual(compositionHandler);
            compositionVisual.ClipToBounds = false;
            ElementComposition.SetElementChildVisual(this, compositionVisual);
        }
        compositionVisual.Size = new Vector(laidOutSize.Width, laidOutSize.Height);
        compositionVisual.Visible = animated;
        // :5-11,177-190 Core Animation plays on the render server. Capture the Swift clock provider once
        // per state update; never use Avalonia's animation time and never request a UI-thread frame.
        var now = SwiftClock.Now;
        var drawn = target ??= Resolved();
        compositionVisual.SendHandlerMessage(DotFieldCompositionFrame.Create(laidOutSize, drawn, blendFrom, blendStart,
            animated, sweeps, SwiftClock.Provider.Current, sweepStart, now));
    }

    // PORT-ONLY (ED-252): the dots move on the render server, where the frame clock cannot see them; while attached they
    // tell GpuWarmup's idle fallback when they move, so it compiles the effort knob's programs in other frames.
    private bool IsAnimatingOnCompositor() => compositionHandler?.IsAnimating == true;

    private void RemoveComposition()
    {
        // Stop immediately, including a server frame already queued when detachment reaches the UI thread.
        compositionHandler?.Stop();
        ElementComposition.SetElementChildVisual(this, null);
        compositionVisual = null;
        compositionHandler = null;
    }
    public void UpdateColor(SwiftColor newColor, bool ease = false)
    {
        // :104-111 Configure: only update color if changed
        if (newColor != color)
        {
            color = newColor;
            UpdateBrushes(ease);
        }
    }

    public void UpdateAnimated(bool newAnimated)
    {
        // :104-111 Configure: only reinstall animation if motion flag changed
        if (newAnimated != animated)
        {
            animated = newAnimated;
            InstallAnimation();
        }
    }

    // OD-0088 (ED-253): Chrome.accentNSColor (:15-16) is the theme's accent or the user's pick, read as the body is drawn
    // again (:104-111 configure(color:)); a pick or a theme posts ThemeManager.DidChange, and the field sends the colour it
    // now resolves to the render server (the still dots take it too).
    // A pick or a theme eases the animated dots with every other use of the accent (SPEC 6.4, round 4).
    private void OnThemeChanged()
    {
        if (CheckAccess()) UpdateBrushes(ease: true);
        else Dispatcher.Post(() => { if (TopLevel.GetTopLevel(this) is not null) UpdateBrushes(ease: true); });
    }

    // The system accent changed: working dots ease over the same 0.32 s as a tint pick; other fields keep their existing rule.
    private void OnColorValuesChanged(object? sender, PlatformColorValues values)
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateBrushes(ease: EaseAppearanceChanges);
        else Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(this) is not null) UpdateBrushes(ease: EaseAppearanceChanges); });
    }

    public void OnAppearanceChanged()
    {
        // :118-122 viewDidChangeEffectiveAppearance re-resolves the dynamic accent; inside a tree that turns over (ED-254) it
        // eases, as SwiftColor's brushes there do.
        UpdateBrushes(ease: EaseAppearanceChanges || AppearanceTurn.Eases(this));
    }

    private void UpdateBrushes(bool ease)
    {
        // Re-apply the color to static dots and send it to the render server.
        Retarget(ease);
        var brush = StillBrush();
        if (dotCanvas != null)
        {
            foreach (var child in dotCanvas.Children)
            {
                if (child is Ellipse ellipse)
                {
                    ellipse.Fill = brush;
                }
            }
        }
        SynchronizeComposition();
    }
}
