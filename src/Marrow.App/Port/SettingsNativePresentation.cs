// PORT-ONLY RR-37. Avalonia 12.1.2 WinUI composition: the Acrylic sprite is separate from the
// framebuffer; their common root owns the close fade and centre scale. Window.Opacity cannot do this.
// Bind once while cloaked; frames invoke cached delegates under the backend's own SyncRoot.
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Marrow.Core.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsNativePresentation : IDisposable
{
    internal static Ambient<Func<HostWindow, SettingsNativePresentation?>> Attach { get; } = new(TryCreate);
    internal static Ambient<Func<HostWindow, bool>> RequiresSurface { get; } = new(window =>
        OperatingSystem.IsWindows() && Field(window.PlatformImpl, "_glSurface")?.GetType().FullName == "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface");
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object sync;
    private readonly IDisposable rootReference;
    private readonly Action<float> blurAlpha, rootAlpha;
    private readonly Action<Vector3> rootScale, rootCentre;
    private readonly float originalBlurAlpha, originalRootAlpha;
    private readonly Vector3 originalScale, originalCentre;
    private bool disposed;

    private SettingsNativePresentation(object sync, object blur, object root)
    {
        this.sync = sync;
        rootReference = (IDisposable)root;
        originalBlurAlpha = Get<float>(blur, "IVisual", "Opacity");
        originalRootAlpha = Get<float>(root, "IVisual", "Opacity");
        originalScale = Get<Vector3>(root, "IVisual", "Scale");
        originalCentre = Get<Vector3>(root, "IVisual", "CenterPoint");
        blurAlpha = Setter<float>(blur, "SetOpacity");
        rootAlpha = Setter<float>(root, "SetOpacity");
        rootScale = Setter<Vector3>(root, "SetScale");
        rootCentre = Setter<Vector3>(root, "SetCenterPoint");
    }

    internal static SettingsNativePresentation? TryCreate(HostWindow window)
    {
        if (!OperatingSystem.IsWindows() || window.PlatformImpl is not { } platform) return null;
        var surface = Field(platform, "_glSurface");
        if (surface?.GetType().FullName != "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface") return null;
        return Bind(platform);
    }

    // Kept separate so the exact native graph, ownership and grouping can be proved without a COM session.
    internal static SettingsNativePresentation? Bind(object platform)
    {
        var native = Field(Field(platform, "_glSurface"), "_window");
        var shared = Field(native, "_shared");
        var blur = Field(native, "_blur");
        var target = Field(native, "_target");
        if (shared is null || blur is null || target is null) return null; // first native draw not yet made
        var sync = shared.GetType().GetProperty("SyncRoot", Members)!.GetValue(shared)!;
        lock (sync)
        {
            var root = Get<object>(target, "ICompositionTarget", "Root");
            try { return new SettingsNativePresentation(sync, blur, root); }
            catch { (root as IDisposable)?.Dispose(); throw; }
        }
    }

    internal static void ReportUnavailable(HostWindow window)
    {
        if (OperatingSystem.IsWindows()) Trace.WriteLine("RR-37: Settings native fade/scale unavailable on " + window.PlatformImpl?.GetType().FullName + "; native parity remains unverified.");
    }

    internal void Glass(double factor)
    {
        lock (sync) { if (!disposed) blurAlpha(originalBlurAlpha * (float)factor); }
    }

    internal void Close(double opacity, double scale, Vector2 physicalSize)
    {
        lock (sync)
        {
            if (disposed) return;
            rootCentre(new Vector3(physicalSize.X / 2, physicalSize.Y / 2, 0));
            rootScale(new Vector3(originalScale.X * (float)scale, originalScale.Y * (float)scale, originalScale.Z));
            rootAlpha(originalRootAlpha * (float)opacity); // single common layer: Acrylic, app glass, content and captions
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            try
            {
                blurAlpha(originalBlurAlpha);
                rootAlpha(originalRootAlpha);
                rootScale(originalScale);
                rootCentre(originalCentre);
            }
            finally { rootReference.Dispose(); } // Root getter acquired a COM ref. Never dispose the backend-owned blur.
        }
    }

    private static object? Field(object? owner, string name) => owner?.GetType().GetField(name, Members)?.GetValue(owner);
    private static Type Contract(object owner, string name) => owner.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == "Avalonia.Win32.WinRT." + name) ?? owner.GetType();
    private static T Get<T>(object owner, string contract, string name) => (T)Contract(owner, contract).GetProperty(name, Members)!.GetValue(owner)!;
    private static Action<T> Setter<T>(object owner, string name) => Contract(owner, "IVisual").GetMethod(name, [typeof(T)])!.CreateDelegate<Action<T>>(owner);
}
