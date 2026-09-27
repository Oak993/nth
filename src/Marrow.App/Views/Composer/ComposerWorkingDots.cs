using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
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

        // :30-34 Only show if not reduce motion
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        if (!reduceMotion)
        {
            var dotField = new DotFieldControl(Tint, animated: true, sweeps: true);
            Child = dotField;
        }
    }
}

/// <summary>
/// The actual dot field visual. (:38-219)
/// Uses a custom visual tree to render animated dots without blocking the UI thread.
/// </summary>
public sealed class DotFieldControl : Canvas
{
    // :67-75 Static configuration constants
    private const double DotSize = 2;           // :67
    private const double Pitch = 8;             // :68
    private const int MaxRows = 3;              // :71
    private const double Period = 2.2;          // :73
    private const double ColumnDelay = 0.045;   // :74
    private const double RowDelay = 0.09;       // :75
    private const double SweepPeriod = 0.8;     // :107
    private const double SweepColumnDelay = 0.022; // :108

    // :60-62 Instance state
    private SwiftColor color;
    private bool animated;
    private bool sweeps;
    private Size laidOutSize;                   // :62

    // Visual tree for rendering
    private Canvas? dotCanvas;

    public Visual? Layer => dotCanvas;

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
        dotCanvas = new Canvas();
        Children.Add(dotCanvas);
        // :113-122 Window attachment installs motion; theme-aware brushes also follow system accent changes.
        AttachedToVisualTree += (_, _) => InstallAnimation();
        DetachedFromVisualTree += (_, _) => { link?.Invalidate(); link = null; };
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

        ApplyOpacities();
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
        // :152-190 ED-098/ED-117 bind animations to the shared frame and SwiftClock seams.
        link?.Invalidate();
        link = null;
        sweepStart = SwiftClock.Now;
        ApplyOpacities();
        if (!animated || TopLevel.GetTopLevel(this) is null) return;
        if (sweeps) ApplySweepAnimation();
        else ApplyPulseAnimation();
    }

    private void ApplyPulseAnimation()
    {
        // :177-190 Repeating shared-clock keyframe, column and row phase delays.
        link = FrameClock.DisplayLink(ApplyOpacities);
        link.Add();
    }

    private void ApplySweepAnimation()
    {
        // :158-175 Keep drawing until the last delayed replica has finished.
        link = FrameClock.DisplayLink(ApplyOpacities);
        link.Add();
    }

    private DisplayLink? link;
    private ContinuousInstant sweepStart;

    // :129-148,152-190 Evaluate the source opacity keyframes for each replica.
    private void ApplyOpacities()
    {
        if (dotCanvas is null || laidOutSize.Width <= 0 || laidOutSize.Height <= 0) return;
        var columns = (int)Math.Ceiling(laidOutSize.Width / Pitch) + 1;
        var rows = sweeps ? Math.Max(1, (int)Math.Ceiling(laidOutSize.Height / Pitch))
            : Math.Min(MaxRows, Math.Max(1, (int)Math.Ceiling(laidOutSize.Height / Pitch)));
        var elapsed = sweeps ? (SwiftClock.Now - sweepStart).TotalSeconds
            : SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond - 1;
        for (var i = 0; i < dotCanvas.Children.Count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var opacity = sweeps ? 0 : 0.28;
            if (animated)
            {
                var seconds = elapsed - col * (sweeps ? SweepColumnDelay : ColumnDelay)
                    - row * (sweeps ? 0 : RowDelay);
                var period = sweeps ? SweepPeriod : Period;
                if (!sweeps && seconds >= 0) seconds %= period;
                opacity = KeyframeOpacity(seconds, period, sweeps ? 0.4 : 0.35,
                    sweeps ? 0 : 0.08, sweeps ? 0.7 : 0.75);
            }
            dotCanvas.Children[i].Opacity = opacity * (sweeps ? 1 : 1.0 - (double)row / rows);
        }
        if (sweeps && elapsed >= SweepPeriod + (columns - 1) * SweepColumnDelay)
        {
            link?.Invalidate();
            link = null;
        }
    }

    // :164-168,179-185 CA's easeOut/easeIn are the same cubic Beziers as the port's timing animations.
    private static double KeyframeOpacity(double seconds, double duration, double peakTime, double start, double peak)
    {
        if (seconds <= 0 || seconds >= duration) return start;
        var split = duration * peakTime;
        return seconds <= split
            ? start + (peak - start) * Animation.EaseOut(split).Progress(seconds)
            : peak + (start - peak) * Animation.EaseIn(duration - split).Progress(seconds - split);
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
        // Re-apply the color to all dots
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
    }
}
