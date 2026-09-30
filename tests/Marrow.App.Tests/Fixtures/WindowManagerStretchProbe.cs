using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// Read-only observation of the actual factory's ED-101 state. Never calls ResizeStretch.Attach/Apply,
// never substitutes the platform surface and never disposes borrowed production COM proxies.
internal sealed class WindowManagerStretchProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly AvaloniaWindow window;
    private readonly object surface;
    private readonly object composition;
    private readonly object sync;
    private readonly object brush;

    internal WindowManagerStretchProbe(AvaloniaWindow window)
    {
        Dispatcher.UIThread.VerifyAccess();
        this.window = window;
        surface = Field(Property(window, "PlatformImpl")!, "_glSurface")!;
        composition = Field(surface, "_window")!;
        sync = Property(Field(composition, "_shared")!, "SyncRoot")!;
        Remedy = RemedyFor(window);
        lock (sync)
        {
            Content = Remedy is null ? Field(composition, "_visual")! : Field(Remedy, "content")!;
            Parked = Remedy is null ? null : Field(Remedy, "parked");
            brush = Field(composition, "_surfaceBrush")!;
        }
    }

    internal ResizeStretch? Remedy { get; }
    internal object Content { get; }
    internal object? Parked { get; }
    internal object CurrentVisual { get { lock (sync) return Field(composition, "_visual")!; } }
    internal Vector2 ContentSize { get { lock (sync) return Size(Content); } }
    internal Vector2 ParkedSize { get { lock (sync) return Parked is null ? default : Size(Parked); } }
    internal Vector2 ContentRelative
    {
        get
        {
            lock (sync)
            {
                var type = WinRT("IVisual2");
                var runtime = type.GetInterfaces().First(i => i.FullName == "MicroCom.Runtime.IUnknown").Assembly
                    .GetType("MicroCom.Runtime.MicroComRuntime", throwOnError: true)!;
                var query = runtime.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var visual2 = query.MakeGenericMethod(type).Invoke(null, [Content])!;
                try { return (Vector2)type.GetMethod("get_RelativeSizeAdjustment")!.Invoke(visual2, null)!; }
                finally { ((IDisposable)visual2).Dispose(); } // This QueryInterface reference is test-owned.
            }
        }
    }
    internal string Stretch { get { lock (sync) return WinRT("ICompositionSurfaceBrush").GetMethod("get_Stretch")!.Invoke(brush, null)!.ToString()!; } }
    internal bool SurfaceReleased => Field(surface, "_window") is null;
    internal bool ContentDisposed => (bool)Property(Content, "IsDisposed")!;
    internal bool ParkedDisposed => Parked is not null && (bool)Property(Parked, "IsDisposed")!;
    internal Vector2 ClientPixels
    {
        get
        {
            var handle = window.TryGetPlatformHandle()!.Handle;
            WindowManagerNativeMethods.RequireOwner(handle);
            WindowManagerNativeMethods.Check(GetClientRect(handle, out var rect), "GetClientRect");
            return new Vector2(rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    internal static ResizeStretch? RemedyFor(AvaloniaWindow window)
    {
        Dispatcher.UIThread.VerifyAccess();
        var applied = (ConditionalWeakTable<AvaloniaWindow, ResizeStretch>)typeof(ResizeStretch)
            .GetField("Applied", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return applied.TryGetValue(window, out var remedy) ? remedy : null;
    }
    internal static bool IsApplied(AvaloniaWindow window) => RemedyFor(window) is not null;
    internal static bool HasFirstSurface(AvaloniaWindow window)
    {
        Dispatcher.UIThread.VerifyAccess();
        var impl = Property(window, "PlatformImpl");
        var surface = impl is null ? null : Field(impl, "_glSurface");
        return surface is not null && Field(surface, "_window") is not null;
    }
    private Type WinRT(string name) => composition.GetType().Assembly.GetType("Avalonia.Win32.WinRT." + name, throwOnError: true)!;
    private Vector2 Size(object visual) => (Vector2)WinRT("IVisual").GetMethod("get_Size")!.Invoke(visual, null)!;
    private static object? Field(object target, string name)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
            if (type.GetField(name, Instance) is { } field) return field.GetValue(target);
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static object? Property(object target, string name)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
            if (type.GetProperty(name, Instance) is { } property) return property.GetValue(target);
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint window, out NativeWindowRect rectangle);
}
