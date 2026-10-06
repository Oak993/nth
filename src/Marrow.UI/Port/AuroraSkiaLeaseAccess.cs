using System.Linq.Expressions;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port;

// Pinned Avalonia backend protocol, compiled once; no reflection, boxing, or managed wrappers per draw.
// Other backends retain the public API path. Missing pinned members fail during warm-up rather than corrupting a lease.
internal static class AuroraSkiaLeaseAccess
{
    private static readonly Type Backend = typeof(ISkiaSharpApiLeaseFeature).Assembly.GetType("Avalonia.Skia.DrawingContextImpl", true)!;
    private static readonly Func<ImmediateDrawingContext, object> GetBackend = CompileBackend();
    private static readonly Func<object, SKCanvas> GetCanvas = Getter<SKCanvas>("Canvas", false);
    private static readonly Func<object, double> GetOpacity = Getter<double>("_currentOpacity", true);
    private static readonly Func<object, bool> GetLeased = Getter<bool>("_leased", true);
    private static readonly Action<object, bool> SetLeased = CompileSetter();
    internal static void Prepare() => _ = SetLeased;

    private static Func<ImmediateDrawingContext, object> CompileBackend()
    {
        var context = Expression.Parameter(typeof(ImmediateDrawingContext));
        return Expression.Lambda<Func<ImmediateDrawingContext, object>>(Expression.Convert(Expression.Property(context, "PlatformImpl"), typeof(object)), context).Compile();
    }
    private static Func<object, T> Getter<T>(string name, bool field)
    {
        var owner = Expression.Parameter(typeof(object)); var typed = Expression.Convert(owner, Backend);
        Expression member = field ? Expression.Field(typed, Backend.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!) : Expression.Property(typed, name);
        return Expression.Lambda<Func<object, T>>(member, owner).Compile();
    }
    private static Action<object, bool> CompileSetter()
    {
        var owner = Expression.Parameter(typeof(object)); var value = Expression.Parameter(typeof(bool));
        return Expression.Lambda<Action<object, bool>>(Expression.Assign(Expression.Field(Expression.Convert(owner, Backend), "_leased"), value), owner, value).Compile();
    }
    internal static bool TryBorrow(ImmediateDrawingContext context, out AuroraSkiaLease lease)
    {
        var owner = GetBackend(context);
        if (owner.GetType() != Backend) { lease = default; return false; }
        if (GetLeased(owner)) throw new InvalidOperationException("The underlying graphics API is currently leased");
        var canvas = GetCanvas(owner); var opacity = GetOpacity(owner);
        SetLeased(owner, true);
        lease = new AuroraSkiaLease(owner, canvas, opacity);
        return true;
    }
    internal static void Release(object owner) => SetLeased(owner, false);
}
