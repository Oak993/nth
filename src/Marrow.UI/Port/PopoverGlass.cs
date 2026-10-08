// PORT-ONLY (OD-0107 and its amendment, OD-0005; ED-357). The see-through material under a popover's card. On
// the Mac every popover is an NSPopover, the system's Liquid Glass: the scene behind it blurred and seen through it, under a
// thin light edge, the arrow part of the same glass. A popup on Windows is a window of its own, and OD-0005 rules out
// reading the screen under it, so what is behind it reaches the popup only through the window manager's backdrop.
//
// THE MECHANISM (ENGINEERING DECISION). Avalonia 12.1.2 draws a window through Windows.UI.Composition
// (Win32CompositionMode.WinUIComposition, the default the port keeps): a root container in the window's desktop target
// holding Avalonia's own Acrylic sprite (a host-backdrop brush through a Gaussian blur of 30 px, the one the main window
// shows, ED-236) under the sprite of the app's drawing. That Acrylic is the window's full rectangle, and DWM's own system
// backdrops (DWMWA_SYSTEMBACKDROP_TYPE) are the window's rectangle with DWM's 8 px corners; neither follows a card with a tip
// and a 20 pt corner inside a popup that also holds the card's shadow. So the popup keeps Avalonia's Transparent level, and
// this class adds, under the app's drawing, its own glass: the SAME blur brush (Avalonia's, shared by every window) in a
// container clipped to the card's body (a rounded rectangle, the window manager's anti-aliased geometric clip), and a second
// one clipped to the tip (a square turned 45 degrees with the tip's point rounded, which is exactly the card's tip: sides at
// 45 degrees, a 90 degree point of radius 3). Composition offers no blend that makes a translucent layer adapt to what is
// behind it, but it offers MinBlend: in Dark a MinBlend layer caps every channel of the blurred backdrop (a bright desktop
// behind a dark popover can never wash out its white text). Light needs no layer: its white tint (PopoverHost.Tint, 0.5)
// alone keeps black text at 4.5:1 over a black backdrop. (A floor for Light, DestinationInvert / MinBlend / DestinationInvert,
// was probed and dropped: an inversion is not linear, so where the tip's clip overlaps the body's, their anti-aliased edges
// drew a visible seam, the whole diamond of the tip's square.) The card then draws its tint, rim and edge over this
// (PopoverCard).
//
// Everything Avalonia keeps internal (the surface, the composited window, the MicroCom interfaces) is reached by reflection
// against Avalonia.Win32 12.1.2 (pinned, Directory.Packages.props); a member that is not found, a host that is not a
// WinUI-composited native window (the overlay popup host, headless, a fallback composition mode) or a call that fails ends
// in Failed, and the card then draws its opaque panel (ED-238) so it stays legible.
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Marrow.UI.Port;

/// <summary>The see-through glass a popover's card shows through its popup window (see the file note).</summary>
internal sealed class PopoverGlass : IDisposable
{
    /// <summary>Dark: the cap on each channel of the blurred backdrop (MinBlend), so a bright backdrop reads as mid grey
    /// under the card's tint: its label (white 0.85) keeps at least 4.5:1 and its secondary text (0.7 of that) 3:1 (ED-357 (3)).</summary>
    internal const byte DarkCap = 0x56;

    /// <summary>What a card's glass is: where the body and the tip are in the popup window's physical pixels.</summary>
    internal readonly record struct Shape(Rect Body, double Radius, Point TipCentre, double TipSide, double TipRadius, double Opacity);

    /// <summary>Why attaching gave up; null while it has not.</summary>
    internal string? Failure { get; private set; }

    /// <summary>Whether the glass is in the popup's composition (from then on Update moves it).</summary>
    internal bool Attached => body is not null;

    /// <summary>When the glass was first shown (Stopwatch ticks), for the timing log; 0 until then.</summary>
    internal long FirstShownTicks { get; private set; }

    private readonly TopLevel window;
    private bool dark;
    private object? syncRoot;
    private object? collection; // the root container's children (IVisualCollection)
    private object? glass;      // our container (IVisual), above Avalonia's Acrylic sprite and under the app's drawing
    private Layer? body;
    private Layer? tip;
    private Shape? last;
    private double? tooltipSigma;
    private double tooltipSaturation;
    private object? ownBrush;

    private PopoverGlass(TopLevel window, bool dark)
    {
        this.window = window;
        this.dark = dark;
        // A host-backdrop brush draws black in a window that has not asked DWM for the backdrop
        // (DWMWA_USE_HOSTBACKDROPBRUSH, which Avalonia sets only for a window whose own level is AcrylicBlur; probed: the
        // glass was solid black without it). Asked as soon as the window exists, so DWM has it by the first frame.
        if (window.TryGetPlatformHandle() is { } handle && handle.Handle != IntPtr.Zero)
        {
            var on = 1;
            if (DwmSetWindowAttribute(handle.Handle, DwmUseHostBackdropBrush, ref on, sizeof(int)) != 0) Failure = "DWM refused the host backdrop";
        }
    }

    private const int DwmUseHostBackdropBrush = 17; // DWMWA_USE_HOSTBACKDROPBRUSH

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>A glass for the popup window <paramref name="window"/>; it attaches on the first <see cref="Update"/> after
    /// Avalonia has made the window's composition (on its first frame).</summary>
    internal static PopoverGlass For(TopLevel window, bool dark) => new(window, dark);
    internal static PopoverGlass ForToolTip(TopLevel window, double sigma, double saturation) =>
        new(window, false) { tooltipSigma = sigma, tooltipSaturation = saturation };

    // One clipped stack: the container (IVisual), its geometric clip's rounded rectangle, and the theme layers in it.
    private sealed record Layer(object Container, object Geometry, object[] DarkLayers);

    /// <summary>Puts the glass where <paramref name="shape"/> says (attaching it first if it can); false while it cannot.</summary>
    internal bool Update(Shape shape)
    {
        if (Failure is not null) return false;
        try
        {
            if (body is null && !TryAttach()) return false;
            lock (syncRoot!)
            {
                Place(shape);
                if (FirstShownTicks == 0 && shape.Opacity > 0) FirstShownTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            last = shape;
            return true;
        }
        catch (Exception error) when (NonFatal(error))
        {
            Fail("a composition call failed: " + (error.InnerException ?? error).Message);
            return false;
        }
    }

    /// <summary>The appearance changed while the popover showed: the cap goes on or off.</summary>
    internal void SetDark(bool value)
    {
        if (dark == value) return;
        dark = value;
        if (body is null || Failure is not null) return;
        try
        {
            lock (syncRoot!) { Theme(body); Theme(tip!); }
        }
        catch (Exception error) when (NonFatal(error))
        {
            Fail("a composition call failed: " + (error.InnerException ?? error).Message);
        }
    }

    public void Dispose()
    {
        try
        {
            if (glass is null || syncRoot is null) return;
            lock (syncRoot)
            {
                Api.Call(collection!, Api.Current!.IVisualCollection, "Remove", glass);
                foreach (var layer in new[] { body, tip })
                {
                    if (layer is null) continue;
                    foreach (var visual in layer.DarkLayers) (visual as IDisposable)?.Dispose();
                    (layer.Geometry as IDisposable)?.Dispose();
                    (layer.Container as IDisposable)?.Dispose();
                }
                (glass as IDisposable)?.Dispose();
                (collection as IDisposable)?.Dispose();
            }
        }
        catch (Exception error) when (NonFatal(error))
        {
            // the window's composition went with the window: nothing left to take back
        }
        finally
        {
            // A tooltip owns its brush even if attachment failed before there
            // was a container to remove. Clear ownership before releasing it.
            var brush = ownBrush; ownBrush = null;
            try { (brush as IDisposable)?.Dispose(); }
            catch (Exception error) when (NonFatal(error)) { }
            glass = null; body = tip = null;
        }
    }

    // Anything a reflected call into Avalonia's internals can raise (a renamed or overloaded member, a COM error, a proxy of the
    // wrong type, a member gone null) ends the glass, not the layout pass it runs in (PopoverCard.SyncGlass): the card falls back
    // to its opaque panel. Only the process-fatal ones go on.
    internal static bool NonFatal(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException or ThreadInterruptedException);

    private static int logged;

    private void Fail(string why)
    {
        Failure ??= why;
        // Said once per process (every popover that opens after it fails the same way).
        if (Interlocked.Exchange(ref logged, 1) == 0) System.Diagnostics.Trace.WriteLine("Marrow: the popovers' see-through glass is off, they draw their opaque panel: " + why);
        Dispose();
    }

    private bool TryAttach()
    {
        if (Api.Current is not { } api)
        {
            Fail(Api.LoadFailure ?? "Avalonia.Win32's composition is not loaded");
            return false;
        }
        if (window.PlatformImpl is not { } impl || api.GlSurface.DeclaringType is not { } windowImpl || !windowImpl.IsInstanceOfType(impl))
        {
            Fail("the popup is not a native Win32 window");
            return false;
        }
        var surface = api.GlSurface.GetValue(impl);
        if (surface is null || !api.SurfaceType.IsInstanceOfType(surface))
        {
            Fail("the window is not drawn through Windows.UI.Composition (" + (surface?.GetType().Name ?? "no surface") + ")");
            return false;
        }
        if (api.CompositedWindow.GetValue(surface) is not { } composited) return false; // its first frame is still to come
        var shared = api.Shared.GetValue(composited)!;
        var blur = api.Blur.GetValue(composited)!;
        syncRoot = api.SyncRoot.GetValue(shared)!;
        var compositor = api.Compositor.GetValue(shared)!;
        var blurBrush = api.BlurBrush.GetValue(shared)!;
        lock (syncRoot)
        {
            if (tooltipSigma is { } sigma) blurBrush = ownBrush = ToolTipBackdropEffect.Create(compositor, sigma, tooltipSaturation);
            var parent = Api.Get(blur, api.IVisual, "Parent") ?? throw new InvalidOperationException("the Acrylic sprite has no container");
            collection = Api.Get(parent, api.IContainerVisual, "Children")!;
            (parent as IDisposable)?.Dispose();
            glass = NewContainer(api, compositor, relative: true);
            body = NewLayer(api, compositor, blurBrush, relative: true);
            tip = NewLayer(api, compositor, blurBrush, relative: false);
            // the tip's square turns 45 degrees about its middle (set in Place)
            var glassChildren = Api.Get(Api.Qi(glass, api.IContainerVisual), api.IContainerVisual, "Children")!;
            Api.Call(glassChildren, api.IVisualCollection, "InsertAtTop", body.Container);
            Api.Call(glassChildren, api.IVisualCollection, "InsertAtTop", tip.Container);
            (glassChildren as IDisposable)?.Dispose();
            Api.Call(glass, api.IVisual, "SetIsVisible", 0); // nothing until the first Place
            Api.Call(collection, api.IVisualCollection, "InsertAbove", glass, blur);
            Theme(body);
            Theme(tip);
        }
        return true;
    }

    private static object NewContainer(Api api, object compositor, bool relative)
    {
        var container = Api.Qi(Api.Call(compositor, api.ICompositor, "CreateContainerVisual")!, api.IVisual);
        if (relative) Api.Call(Api.Qi(container, api.IVisual2), api.IVisual2, "SetRelativeSizeAdjustment", new Vector2(1, 1));
        return container;
    }

    // A container clipped to a rounded rectangle, holding the blurred backdrop and the Dark and Light layers over it.
    private static Layer NewLayer(Api api, object compositor, object blurBrush, bool relative)
    {
        var container = NewContainer(api, compositor, relative);
        var geometry = Api.Call(Api.Qi(compositor, api.ICompositor5), api.ICompositor5, "CreateRoundedRectangleGeometry")!;
        var clip = Api.Call(Api.Qi(compositor, api.ICompositor6), api.ICompositor6, "CreateGeometricClipWithGeometry", Api.Qi(geometry, api.ICompositionGeometry))!;
        Api.Call(container, api.IVisual, "SetClip", Api.Qi(clip, api.ICompositionClip));
        var children = Api.Get(Api.Qi(container, api.IContainerVisual), api.IContainerVisual, "Children")!;
        object Sprite(object brush, int mode)
        {
            var sprite = Api.Call(compositor, api.ICompositor, "CreateSpriteVisual")!;
            Api.Call(sprite, api.ISpriteVisual, "SetBrush", Api.Qi(brush, api.ICompositionBrush));
            var visual = Api.Qi(sprite, api.IVisual);
            Api.Call(Api.Qi(sprite, api.IVisual2), api.IVisual2, "SetRelativeSizeAdjustment", new Vector2(1, 1));
            if (mode != 0) Api.Call(visual, api.IVisual, "SetCompositeMode", Enum.ToObject(api.CompositeMode, mode));
            Api.Call(children, api.IVisualCollection, "InsertAtTop", visual);
            return visual;
        }
        Sprite(blurBrush, 0);
        const int minBlend = 3; // CompositionCompositeMode
        var cap = Sprite(api.ColorBrush(compositor, DarkCap, DarkCap, DarkCap), minBlend);
        (children as IDisposable)?.Dispose();
        return new Layer(container, geometry, [cap]);
    }

    private void Theme(Layer layer)
    {
        var api = Api.Current!;
        foreach (var visual in layer.DarkLayers) Api.Call(visual, api.IVisual, "SetIsVisible", dark ? 1 : 0);
    }

    private void Place(Shape shape)
    {
        var api = Api.Current!;
        if (last == shape) return;
        var b = shape.Body;
        Api.Call(body!.Geometry, api.ICompositionRoundedRectangleGeometry, "SetOffset", new Vector2((float)b.X, (float)b.Y));
        Api.Call(body.Geometry, api.ICompositionRoundedRectangleGeometry, "SetSize", new Vector2((float)b.Width, (float)b.Height));
        Api.Call(body.Geometry, api.ICompositionRoundedRectangleGeometry, "SetCornerRadius", new Vector2((float)shape.Radius, (float)shape.Radius));
        var side = (float)shape.TipSide;
        var t = tip!.Container;
        Api.Call(t, api.IVisual, "SetSize", new Vector2(side, side));
        Api.Call(t, api.IVisual, "SetCenterPoint", new Vector3(side / 2, side / 2, 0));
        Api.Call(t, api.IVisual, "SetRotationAngleInDegrees", 45f);
        Api.Call(t, api.IVisual, "SetOffset", new Vector3((float)shape.TipCentre.X - side / 2, (float)shape.TipCentre.Y - side / 2, 0));
        Api.Call(tip.Geometry, api.ICompositionRoundedRectangleGeometry, "SetSize", new Vector2(side, side));
        Api.Call(tip.Geometry, api.ICompositionRoundedRectangleGeometry, "SetCornerRadius", new Vector2((float)shape.TipRadius, (float)shape.TipRadius));
        Api.Call(glass!, api.IVisual, "SetOpacity", (float)Math.Clamp(shape.Opacity, 0, 1));
        Api.Call(glass!, api.IVisual, "SetIsVisible", shape.Opacity > 0 && b.Width > 0 && b.Height > 0 ? 1 : 0);
    }

    // Avalonia.Win32 12.1.2's internals, found once.
    private sealed class Api
    {
        internal static string? LoadFailure { get; private set; }

        private static readonly Lazy<Api?> current = new(Load);

        internal static Api? Current => current.Value;

        internal required FieldInfo GlSurface { get; init; }          // WindowImpl._glSurface
        internal required Type SurfaceType { get; init; }             // WinUiCompositedWindowSurface
        internal required FieldInfo CompositedWindow { get; init; }   // WinUiCompositedWindowSurface._window
        internal required FieldInfo Shared { get; init; }             // WinUiCompositedWindow._shared
        internal required FieldInfo Blur { get; init; }               // WinUiCompositedWindow._blur
        internal required PropertyInfo SyncRoot { get; init; }
        internal required PropertyInfo Compositor { get; init; }
        internal required PropertyInfo BlurBrush { get; init; }
        internal required Type IVisual { get; init; }
        internal required Type IVisual2 { get; init; }
        internal required Type IContainerVisual { get; init; }
        internal required Type IVisualCollection { get; init; }
        internal required Type ISpriteVisual { get; init; }
        internal required Type ICompositor { get; init; }
        internal required Type ICompositor5 { get; init; }
        internal required Type ICompositor6 { get; init; }
        internal required Type ICompositionGeometry { get; init; }
        internal required Type ICompositionClip { get; init; }
        internal required Type ICompositionBrush { get; init; }
        internal required Type ICompositionColorBrush { get; init; }
        internal required Type ICompositionRoundedRectangleGeometry { get; init; }
        internal required Type CompositeMode { get; init; }
        internal required MethodInfo QueryInterface { get; init; }    // MicroComRuntime.QueryInterface<T>
        internal required MethodInfo CreateProxy { get; init; }       // MicroComRuntime.CreateProxyFor(Type, IntPtr, bool)
        internal required PropertyInfo NativePointer { get; init; }   // MicroComProxyBase.NativePointer

        private static Api? Load()
        {
            try
            {
                var win32 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Avalonia.Win32");
                if (win32 is null) { LoadFailure = "Avalonia.Win32 is not loaded"; return null; }
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type T(string name) => win32.GetType(name, throwOnError: true)!;
                var windowImpl = T("Avalonia.Win32.WindowImpl");
                var surface = T("Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface");
                var composited = T("Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow");
                var shared = T("Avalonia.Win32.WinRT.Composition.WinUiCompositionShared");
                var unknown = T("Avalonia.Win32.WinRT.ICompositor").GetInterfaces().First(i => i.Name == "IUnknown");
                var runtime = unknown.Assembly.GetType("MicroCom.Runtime.MicroComRuntime", throwOnError: true)!;
                var proxyBase = unknown.Assembly.GetType("MicroCom.Runtime.MicroComProxyBase", throwOnError: true)!;
                FieldInfo F(Type type, string name) => type.GetField(name, any) ?? throw new MissingFieldException(type.Name, name);
                PropertyInfo P(Type type, string name) => type.GetProperty(name, any) ?? throw new MissingMemberException(type.Name, name);
                return new Api
                {
                    GlSurface = F(windowImpl, "_glSurface"),
                    SurfaceType = surface,
                    CompositedWindow = F(surface, "_window"),
                    Shared = F(composited, "_shared"),
                    Blur = F(composited, "_blur"),
                    SyncRoot = P(shared, "SyncRoot"),
                    Compositor = P(shared, "Compositor"),
                    BlurBrush = P(shared, "BlurBrush"),
                    IVisual = T("Avalonia.Win32.WinRT.IVisual"),
                    IVisual2 = T("Avalonia.Win32.WinRT.IVisual2"),
                    IContainerVisual = T("Avalonia.Win32.WinRT.IContainerVisual"),
                    IVisualCollection = T("Avalonia.Win32.WinRT.IVisualCollection"),
                    ISpriteVisual = T("Avalonia.Win32.WinRT.ISpriteVisual"),
                    ICompositor = T("Avalonia.Win32.WinRT.ICompositor"),
                    ICompositor5 = T("Avalonia.Win32.WinRT.ICompositor5"),
                    ICompositor6 = T("Avalonia.Win32.WinRT.ICompositor6"),
                    ICompositionGeometry = T("Avalonia.Win32.WinRT.ICompositionGeometry"),
                    ICompositionClip = T("Avalonia.Win32.WinRT.ICompositionClip"),
                    ICompositionBrush = T("Avalonia.Win32.WinRT.ICompositionBrush"),
                    ICompositionColorBrush = T("Avalonia.Win32.WinRT.ICompositionColorBrush"),
                    ICompositionRoundedRectangleGeometry = T("Avalonia.Win32.WinRT.ICompositionRoundedRectangleGeometry"),
                    CompositeMode = T("Avalonia.Win32.WinRT.CompositionCompositeMode"),
                    QueryInterface = runtime.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition),
                    CreateProxy = runtime.GetMethod("CreateProxyFor", [typeof(Type), typeof(IntPtr), typeof(bool)]) ?? throw new MissingMethodException("MicroComRuntime", "CreateProxyFor"),
                    NativePointer = P(proxyBase, "NativePointer"),
                };
            }
            catch (Exception error) when (NonFatal(error)) // a missing type or member, an ambiguous one, a load failure: Lazy then caches null, not the throw
            {
                LoadFailure = "Avalonia.Win32's composition internals were not found: " + error.Message;
                return null;
            }
        }

        internal static object Qi(object unknown, Type type) => current.Value!.QueryInterface.MakeGenericMethod(type).Invoke(null, [unknown])!;

        // A member that is not there (an Avalonia that renamed it) or is there twice is an InvalidOperationException, which every
        // caller's NonFatal catch takes, as it does any other error of the call.
        internal static object? Call(object target, Type type, string name, params object?[] arguments) => Member(() => type.GetMethod(name), type, name).Invoke(target, arguments);

        internal static object? Get(object target, Type type, string name) => Member(() => type.GetProperty(name), type, name).GetValue(target);

        private static T Member<T>(Func<T?> find, Type type, string name) where T : MemberInfo
        {
            try
            {
                return find() ?? throw new InvalidOperationException($"Avalonia.Win32 has no {type.Name}.{name}");
            }
            catch (AmbiguousMatchException error)
            {
                throw new InvalidOperationException($"Avalonia.Win32 has more than one {type.Name}.{name}", error);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateColorBrushWithColor(IntPtr self, uint color, out IntPtr brush);

        // ICompositor::CreateColorBrushWithColor(Color, out CompositionColorBrush). The ABI passes Windows.UI.Color, four
        // bytes A R G B, by value; Avalonia's proxy declares a pointer (probed: the brush came out an arbitrary lavender), so
        // the call is made through the vtable (slot 8: IInspectable's six, CreateColorKeyFrameAnimation, CreateColorBrush).
        internal object ColorBrush(object compositor, byte r, byte g, byte b)
        {
            var self = Qi(compositor, ICompositor);
            var pointer = (IntPtr)NativePointer.GetValue(self)!;
            var slot = Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), 8 * IntPtr.Size);
            var create = Marshal.GetDelegateForFunctionPointer<CreateColorBrushWithColor>(slot);
            var color = 0xFFu | (uint)r << 8 | (uint)g << 16 | (uint)b << 24; // A, R, G, B in memory order
            var result = create(pointer, color, out var brush);
            (self as IDisposable)?.Dispose();
            if (result != 0) throw new COMException("CreateColorBrushWithColor failed", result);
            return CreateProxy.Invoke(null, [ICompositionColorBrush, brush, true])!;
        }
    }
}
