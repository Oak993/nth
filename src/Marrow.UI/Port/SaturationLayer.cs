// PORT-ONLY RR-37, Getting Ready Variant I: CSS saturate(.6 + .4 * hi) over the live pane.
// Skia's save-layer brackets the actual child draws. There is no raster snapshot/readback and no second tree.
// Both sentinels cover the same bounds, so a child-only dirty region visits both. They have no opacity,
// clip, transform, effect or cached content of their own; all child scopes must finish between them.
using System.Linq.Expressions;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Port;

public sealed class SaturationLayer : Panel
{
    private readonly Scope scope = new();
    private readonly Sentinel begin;
    private readonly Sentinel end;
    private double amount = 1;

    public SaturationLayer(Control content)
    {
        begin = new Sentinel(scope, true) { IsHitTestVisible = false };
        end = new Sentinel(scope, false) { IsHitTestVisible = false };
        Children.Add(content); // identity has no sentinels, draw operations or offscreen layer
    }

    public double Amount
    {
        get => amount;
        set
        {
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (amount == value) return;
            amount = value;
            if (value == 1)
            {
                Children.Remove(begin);
                Children.Remove(end);
                return;
            }
            if (begin.Parent is null) { Children.Insert(0, begin); Children.Add(end); }
            begin.Amount = value;
            begin.InvalidateVisual();
            end.InvalidateVisual();
        }
    }

    private sealed class Sentinel(Scope scope, bool begin) : Control
    {
        public double Amount { get; set; } = 1;
        public override void Render(DrawingContext context) => context.Custom(new Operation(scope, begin, Amount, new Rect(Bounds.Size)));
    }

    private sealed class Operation(Scope scope, bool begin, double amount, Rect bounds) : ICustomDrawOperation
    {
        private readonly Scope held = scope.Retain();
        private int disposed;
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) held.Release(); }
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("Getting Ready saturation requires the pinned Skia renderer.");
            using var lease = feature.Lease();
            var backend = BackendOpacity.Owner(lease);
            if (begin) scope.Begin(lease.SkCanvas, bounds, amount, backend, lease.CurrentOpacity);
            else scope.End(lease.SkCanvas, backend);
        }
    }

    private sealed class Scope
    {
        private SKCanvas? canvas;
        private int saved;
        private SKPaint? paint;
        private SKColorFilter? filter;
        private double previous = double.NaN;
        private double opacity;
        private int references;
        public Scope Retain() { Interlocked.Increment(ref references); return this; }
        public void Release()
        {
            if (Interlocked.Decrement(ref references) != 0) return;
            paint?.Dispose(); paint = null;
            filter?.Dispose(); filter = null;
            previous = double.NaN;
        }

        public void Begin(SKCanvas target, Rect bounds, double amount, object backend, double alpha)
        {
            if (canvas is not null) throw new InvalidOperationException("Unbalanced live saturation layer.");
            canvas = target;
            saved = target.SaveCount;
            opacity = alpha;
            if (amount == 1 && alpha == 1) return; // identity: no offscreen allocation
            if (previous != amount)
            {
                paint?.Dispose();
                filter?.Dispose();
                Span<float> matrix = stackalloc float[20];
                VoiceGlowFilters.Saturate(matrix, amount);
                filter = SKColorFilter.CreateColorMatrix(matrix);
                paint = new SKPaint { ColorFilter = filter };
                previous = amount;
            }
            paint!.ColorF = new SKColorF(1, 1, 1, (float)alpha);
            BackendOpacity.Set(backend, 1);
            target.SaveLayer(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), paint);
        }

        public void End(SKCanvas target, object backend)
        {
            if (!ReferenceEquals(target, canvas)) throw new InvalidOperationException("Saturation layer changed render targets.");
            target.RestoreToCount(saved);
            BackendOpacity.Set(backend, opacity);
            canvas = null;
        }
    }
    private static class BackendOpacity
    {
        private static readonly Type Backend = typeof(ISkiaSharpApiLeaseFeature).Assembly.GetType("Avalonia.Skia.DrawingContextImpl", true)!;
        internal static readonly Func<ISkiaSharpApiLease, object> Owner = MakeOwner();
        internal static readonly Action<object, double> Set = MakeSetter();
        private static Func<ISkiaSharpApiLease, object> MakeOwner()
        {
            var type = Backend.GetNestedType("SkiaLeaseFeature", BindingFlags.NonPublic)!.GetNestedType("ApiLease", BindingFlags.NonPublic)!;
            var lease = Expression.Parameter(typeof(ISkiaSharpApiLease));
            return Expression.Lambda<Func<ISkiaSharpApiLease, object>>(Expression.Convert(Expression.Field(Expression.Convert(lease, type), "_context"), typeof(object)), lease).Compile();
        }
        private static Action<object, double> MakeSetter()
        {
            var owner = Expression.Parameter(typeof(object)); var value = Expression.Parameter(typeof(double));
            return Expression.Lambda<Action<object, double>>(Expression.Assign(Expression.Field(Expression.Convert(owner, Backend), "_currentOpacity"), value), owner, value).Compile();
        }
    }
}
