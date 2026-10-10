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
    private Action? clipShell;

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
        var presentation = Bind(platform);
        if (presentation is not null && window.Content is SettingsShell { IsNative: true } shell)
        {
            var native = Field(Field(platform, "_glSurface"), "_window");
            try
            {
                lock (presentation.sync)
                    shell.AcrylicClip ??= new SettingsAcrylicClip(window, CreateShellClip(native!), presentation.sync,
                        shell.Inset, new Avalonia.Size(shell.Client.Width, shell.Client.Height));
                presentation.clipShell = () => shell.AcrylicClip?.Update(window.RenderScaling);
            }
            catch { presentation.Dispose(); throw; }
        }
        return presentation;
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
        lock (sync) { if (!disposed) { clipShell?.Invoke(); blurAlpha(originalBlurAlpha * (float)factor); } }
    }

    internal void Close(double opacity, double scale, Vector2 physicalSize)
    {
        lock (sync)
        {
            if (disposed) return;
            clipShell?.Invoke();
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

    // WindowDecorations.None has no backend-rounded clip. Own the inner clip for the HWND lifetime;
    // the backend's full-size framebuffer and transparent margin remain outside this Acrylic clip.
    private static object CreateShellClip(object native)
    {
        var shared = Field(native, "_shared")!;
        var compositor = shared.GetType().GetProperty("Compositor", Members)!.GetValue(shared)!;
        var blur = Field(native, "_blur")!;
        using var compositor5 = (IDisposable)Qi(compositor, "ICompositor5");
        using var compositor6 = (IDisposable)Qi(compositor, "ICompositor6");
        var geometry = Call(compositor5, "ICompositor5", "CreateRoundedRectangleGeometry")!;
        try
        {
            using var geometryBase = (IDisposable)Qi(geometry, "ICompositionGeometry");
            using var clip = (IDisposable)Call(compositor6, "ICompositor6", "CreateGeometricClipWithGeometry", geometryBase)!;
            using var clipBase = (IDisposable)Qi(clip, "ICompositionClip");
            Call(blur, "IVisual", "SetClip", clipBase);
            return geometry;
        }
        catch { ((IDisposable)geometry).Dispose(); throw; }
    }
    private static object? Call(object owner, string contract, string method, params object[] arguments) =>
        Contract(owner, contract).GetMethod(method)!.Invoke(owner, arguments);
    private static object Qi(object owner, string contract)
    {
        var type = owner.GetType().Assembly.GetType("Avalonia.Win32.WinRT." + contract, throwOnError: true)!;
        var unknown = type.GetInterfaces().First(i => i.Name == "IUnknown");
        var runtime = unknown.Assembly.GetType("MicroCom.Runtime.MicroComRuntime", throwOnError: true)!;
        return runtime.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition).MakeGenericMethod(type).Invoke(null, [owner])!;
    }
    private static Type Contract(object owner, string name) => owner.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == "Avalonia.Win32.WinRT." + name) ?? owner.GetType();
    private static T Get<T>(object owner, string contract, string name) => (T)Contract(owner, contract).GetProperty(name, Members)!.GetValue(owner)!;
    private static Action<T> Setter<T>(object owner, string name, string contract = "IVisual") => Contract(owner, contract).GetMethod(name, [typeof(T)])!.CreateDelegate<Action<T>>(owner);
}
