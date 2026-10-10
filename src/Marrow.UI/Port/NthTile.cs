using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Core.Port;
using SkiaSharp;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0136, the empty chat's mark (nth-brand.html section 3, .hero .mk): the 44 px nth tile B, drawn as vectors by
// NthMark (the icon's own symbol), that sweeps once when the chat opens and then stays still (NthMotion.Hero). Frames are asked
// for only during that one sweep; reduced motion, or no frame clock, shows the still tile.
public sealed class NthTile : Control
{
    /// <summary>The tile's side in DIPs (the hero's <c>.mk{width:44px}</c>).</summary>
    public const double Side = 44;

    [ThreadStatic] private static NthMark? renderMark; // the render thread's one renderer
    [ThreadStatic] private static SKPaint? fade;
    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private TimeProvider? clock;
    private long started;
    private bool waiting;

    public NthTile()
    {
        Width = Side; Height = Side;
        IsHitTestVisible = false; Focusable = false;
        tick = OnFrame;
        AttachedToVisualTree += (_, _) =>
        {
            clock = SwiftClock.Provider.Current; started = clock.GetTimestamp(); Elapsed = 0;
            source = FrameClock.IsBound ? FrameClock.Source.Current : null;
            InvalidateVisual(); Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source?.CancelFrame(tick);
            waiting = false; source = null;
        };
    }

    /// <summary>Seconds since the tile appeared (the chat opened), as of the last frame.</summary>
    public double Elapsed { get; private set; }

    /// <summary>What the tile shows now.</summary>
    public NthPose Pose => NthMotion.Hero(Elapsed, Still);

    private bool Still => source is null || EnvironmentValues.GetAccessibilityReduceMotion(this) || Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void Request()
    {
        if (waiting || source is null || Still || Elapsed >= NthMotion.HeroDone) return;
        waiting = true; source.RequestFrame(tick);
    }

    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (clock is null || source is null) return;
        Elapsed = clock.GetElapsedTime(started).TotalSeconds;
        InvalidateVisual();
        Request();
    }

    public override void Render(DrawingContext context) => context.Custom(new Operation(new Rect(Bounds.Size), Pose));

    private sealed class Operation(Rect bounds, NthPose pose) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => other is Operation o && o.Bounds == bounds && o.Pose == pose;
        private NthPose Pose => pose;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var mark = renderMark ??= new NthMark();
            var opacity = Math.Clamp(lease.CurrentOpacity, 0, 1);
            if (opacity <= 0) return;
            var saved = canvas.Save();
            if (opacity < 1)
            {
                fade ??= new SKPaint();
                fade.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * opacity));
                canvas.SaveLayer(fade);
            }
            mark.Draw(canvas, new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), NthTileStyle.Icon, NthGlyph.Regular, pose);
            canvas.RestoreToCount(saved);
        }
    }
}

