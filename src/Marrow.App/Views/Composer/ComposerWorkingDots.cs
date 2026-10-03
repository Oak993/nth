using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
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
    // :13-19 Uses DotFieldLayerView with Chrome.accentNSColor and animated: !reduceMotion
    public ComposerWorkingDots()
    {
        // :15-18 Passes accessibility settings to the underlying control
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);

        // Create the underlying dot field
        var dotField = new DotFieldControl(Chrome.AccentNSColor, animated: true, sweeps: false);

        Child = dotField;

        // Track accessibility changes
        this.Track(
            () => EnvironmentValues.GetAccessibilityReduceMotion(this),
            motion => dotField.UpdateAnimated(!motion)
        );
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
    private Size laidOutSize;                   // :62

    // Visual tree for rendering
    private Canvas? dotCanvas;

    public Visual? Layer => dotCanvas;
    private CompositionCustomVisual? compositionVisual;
    private DotFieldCompositionHandler? compositionHandler;
    internal DotFieldCompositionHandler? CompositionHandler => compositionHandler;
    internal DotFieldCompositionFrame? LastRenderedFrame => compositionHandler?.LastFrame;
    private ContinuousInstant sweepStart;
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
        AttachedToVisualTree += (_, _) => { InstallAnimation(); warmupActivity ??= GpuWarmup.AddActivity(IsAnimatingOnCompositor); };
        DetachedFromVisualTree += (_, _) => { RemoveComposition(); warmupActivity?.Dispose(); warmupActivity = null; };
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

        // Clear and recreate the dot layers
        dotCanvas?.Children.Clear();

        // :139-148 Position dots based on grid
        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < columnCount; col++)
            {
                var x = Pitch / 2 + col * Pitch;
                // :137-145 AppKit's origin is bottom-left. Avalonia's is top-left.
                var y = size.Height - (Pitch / 2 + row * Pitch);
                var dot = CreateDot(row, rowCount);
                dotCanvas?.Children.Add(dot);
                Canvas.SetLeft(dot, x - DotSize / 2);
                Canvas.SetTop(dot, y - DotSize / 2);
            }
        }

        SynchronizeComposition();
    }

    private Ellipse CreateDot(int row, int rowCount)
    {
        var ellipse = new Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Fill = GetBrush(),
        };

        // :148 Apply per-row alpha offset
        var baseOpacity = animated ? 0.08 : (sweeps ? 0 : 0.28);
        // :148 Replicator instance alpha modulates the base layer's opacity.
        ellipse.Opacity = baseOpacity * (sweeps ? 1 : 1.0 - (double)row / rowCount);

        return ellipse;
    }

    private IBrush GetBrush()
    {
        return color.Brush(this);
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
            compositionHandler = new DotFieldCompositionHandler();
            compositionVisual = host.Compositor.CreateCustomVisual(compositionHandler);
            ElementComposition.SetElementChildVisual(this, compositionVisual);
        }
        compositionVisual.Size = new Vector(laidOutSize.Width, laidOutSize.Height);
        compositionVisual.Visible = animated;
        // :5-11,177-190 Core Animation plays on the render server. Capture the Swift clock provider once
        // per state update; never use Avalonia's animation time and never request a UI-thread frame.
        compositionVisual.SendHandlerMessage(new DotFieldCompositionFrame(laidOutSize,
            GetBrush().ToImmutable(), animated, sweeps, SwiftClock.Provider.Current, sweepStart, SwiftClock.Now));
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
    public void UpdateColor(SwiftColor newColor)
    {
        // :104-111 Configure: only update color if changed
        if (newColor != color)
        {
            color = newColor;
            UpdateBrushes();
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

    public void OnAppearanceChanged()
    {
        // :118-122 viewDidChangeEffectiveAppearance re-resolves the dynamic accent
        UpdateBrushes();
    }

    private void UpdateBrushes()
    {
        // Re-apply the color to static dots and send one immutable brush to the render server.
        var brush = GetBrush();
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
