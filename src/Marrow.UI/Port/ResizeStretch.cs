// PORT-ONLY: the live-resize remedy `stretch` of OD-0017 as ED-101 builds it; Droppy Code has no Swift twin (AppKit
// windows need no remedy). Evidence: porting/probes/p4b/RESULT.md, "What the strip is" and "How the production chrome
// should implement it" (steps 1 to 5, cited below as RESULT.md step n).
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// Where ResizeStretch reports that it left a window as Avalonia made it. It writes once per instance (ED-101: "logs
/// it once"), so one broken member does not log for every window. Tests replace it through ResizeStretch.Log.
/// </summary>
public sealed class ResizeStretchLog(Action<string> write)
{
    private int written;

    /// <summary>Writes the first message given to this log and drops the rest.</summary>
    public void Once(string message)
    {
        if (Interlocked.Exchange(ref written, 1) == 0) write(message);
    }
}

/// <summary>
/// Passes on what RESULT.md step 3 reads back: the content visual's RelativeSizeAdjustment and the surface brush's
/// Stretch (a CompositionStretch value). The process default returns both unchanged; tests replace it through
/// ResizeStretch.ReadBack to make a read-back differ or fail, which ED-101's failure path must survive.
/// </summary>
public delegate (Vector2 Relative, object Stretch) ResizeStretchReadBack(Vector2 relative, object stretch);

/// <summary>
/// `stretch` on one Avalonia.Win32 top-level window (ED-101). Avalonia 12.1.2 sizes its content SpriteVisual absolutely
/// at render time (WinUiCompositedWindow.ResizeIfNeeded: `_visual.SetSize(size)`), one frame after DWM applies the new
/// window size, which shows as an edge strip during a live resize (RESULT.md "What the strip is"). The remedy sizes the
/// content visual relative to the window and stretches the last frame to fill it until the new size is presented.
/// Everything is reached by reflection on Avalonia.Win32 internals (ED-101 accepts this in place of a patched
/// Avalonia.Win32); if a member is missing or a read-back differs, the window keeps Avalonia's own presentation and the
/// problem is logged once.
/// </summary>
public sealed class ResizeStretch
{
    private const string Keeps = "; the window keeps Avalonia's own presentation.";

    private static readonly ConditionalWeakTable<Window, ResizeStretch> Applied = new();

    private readonly Members members;
    private readonly object compositedWindow;
    private readonly object syncRoot;
    private readonly object content;
    private readonly object parked;
    private bool restored;

    private ResizeStretch(Members members, object compositedWindow, object syncRoot, object content, object parked)
    {
        this.members = members;
        this.compositedWindow = compositedWindow;
        this.syncRoot = syncRoot;
        this.content = content;
        this.parked = parked;
    }

    /// <summary>The one-time log. The process default writes to System.Diagnostics.Trace.</summary>
    public static Ambient<ResizeStretchLog> Log { get; } = new(() => new ResizeStretchLog(message => Trace.WriteLine(message)));

    /// <summary>The step 3 read-back (ResizeStretchReadBack). The process default passes the values on unchanged.</summary>
    public static Ambient<ResizeStretchReadBack> ReadBack { get; } =
        new(new ResizeStretchReadBack((relative, stretch) => (relative, stretch)));

    /// <summary>
    /// RequiredMembers that Apply treats as not found, as if the Avalonia.Win32 in use lacked them (ED-101: "if any
    /// member is missing"). Empty in the process; tests replace it to run the missing-member path on a real window.
    /// MissingMembers ignores it, so the Avalonia 12.1.2 pin always reads the real assembly.
    /// </summary>
    public static Ambient<IReadOnlySet<string>> Unresolved { get; } = new(FrozenSet<string>.Empty);

    /// <summary>Every member the remedy resolves, as "Type.Member" (ED-101; RESULT.md steps 1 and 2).</summary>
    public static IReadOnlyList<string> RequiredMembers { get; } =
    [
        "Avalonia.Controls.Window.PlatformImpl",
        Members.WindowImplType + "._glSurface",
        Members.SurfaceType + "._window",
        Members.CompositedWindowType + "._shared",
        Members.CompositedWindowType + "._visual",
        Members.CompositedWindowType + "._surfaceBrush",
        Members.SharedType + ".SyncRoot",
        Members.SharedType + ".Compositor",
        Members.IVisualType + ".SetSize",
        Members.IVisualType + ".get_Size",
        Members.IVisual2Type + ".SetRelativeSizeAdjustment",
        Members.IVisual2Type + ".get_RelativeSizeAdjustment",
        Members.BrushType + ".SetStretch",
        Members.BrushType + ".get_Stretch",
        Members.StretchType + ".Fill",
        Members.CompositorType + ".CreateSpriteVisual",
        Members.RuntimeType + ".QueryInterface",
    ];

    /// <summary>The RequiredMembers that <paramref name="avaloniaWin32"/> does not have; empty on Avalonia.Win32 12.1.2.</summary>
    public static IReadOnlyList<string> MissingMembers(Assembly avaloniaWin32) => Members.For(avaloniaWin32).Missing;

    /// <summary>
    /// Applies the remedy once the window has drawn its first frame, and undoes it when the window closes. The task
    /// completes with the applied remedy, or with null when the window keeps Avalonia's presentation or closes first.
    /// Call on the UI thread, before or after Show.
    /// </summary>
    public static Task<ResizeStretch?> Attach(Window window)
    {
        Dispatcher.UIThread.VerifyAccess();
        var result = new TaskCompletionSource<ResizeStretch?>();
        bool closed = false;
        window.Closed += (_, _) =>
        {
            closed = true;
            result.TrySetResult(null);
        };

        // RESULT.md step 1: the platform surface exists only after the renderer's first CreateRenderTarget, so the
        // attempt repeats on each animation frame until WinUiCompositedWindowSurface._window is there.
        void Attempt()
        {
            if (closed || result.Task.IsCompleted) return;
            var applied = TryApply(window, out bool beforeFirstFrame);
            if (beforeFirstFrame) window.RequestAnimationFrame(_ => Attempt());
            else result.TrySetResult(applied);
        }

        if (window.IsVisible) Attempt();
        else
        {
            void Opened(object? sender, EventArgs e)
            {
                window.Opened -= Opened;
                Attempt();
            }
            window.Opened += Opened;
        }
        return result.Task;
    }

    /// <summary>
    /// Applies the remedy to a window that has drawn its first frame. Returns null, after logging once, when a member
    /// is missing, a read-back differs or the first frame has not been drawn; the window is then as Avalonia made it.
    /// Applying twice returns the first remedy. Call on the UI thread.
    /// </summary>
    public static ResizeStretch? Apply(Window window)
    {
        var applied = TryApply(window, out bool beforeFirstFrame);
        if (beforeFirstFrame) Log.Current.Once("ResizeStretch: the window has not drawn its first frame" + Keeps);
        return applied;
    }

    /// <summary>
    /// Puts Avalonia's content visual back into WinUiCompositedWindow._visual under the composition lock and releases the
    /// parked visual (RESULT.md step 4). Runs on Window.Closed, which Avalonia raises before WindowImpl disposes the
    /// surface (WM_DESTROY: Closed, then _glSurface.Dispose, whose WinUiCompositedWindow.Dispose releases _visual).
    /// </summary>
    public void Restore()
    {
        lock (syncRoot)
        {
            if (restored) return;
            restored = true;
            members.Visual.SetValue(compositedWindow, content);
        }
        ((IDisposable)parked).Dispose();
    }

    private static ResizeStretch? TryApply(Window window, out bool beforeFirstFrame)
    {
        Dispatcher.UIThread.VerifyAccess();
        beforeFirstFrame = false;
        if (Applied.TryGetValue(window, out var existing)) return existing;
        var log = Log.Current;

        // RESULT.md step 1: Window.PlatformImpl, then WindowImpl._glSurface.
        var platformImpl = typeof(Window).GetProperty("PlatformImpl", Members.AnyInstance);
        if (platformImpl == null)
        {
            log.Once("ResizeStretch: Avalonia.Controls.Window has no property PlatformImpl" + Keeps);
            return null;
        }
        var impl = platformImpl.GetValue(window);
        if (impl == null)
        {
            log.Once("ResizeStretch: the window has no platform implementation" + Keeps);
            return null;
        }
        var implType = impl.GetType();
        if (!IsWindowImpl(implType))
        {
            log.Once("ResizeStretch: " + implType.FullName + " has no field _glSurface" + Keeps);
            return null;
        }
        var members = Members.For(implType.Assembly);
        var unresolved = Unresolved.Current;
        var missing = members.Missing.Concat(RequiredMembers.Where(unresolved.Contains)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            log.Once("ResizeStretch: " + implType.Assembly.GetName() + " lacks " + string.Join(", ", missing) + Keeps);
            return null;
        }
        var surface = members.GlSurface.GetValue(impl);
        if (surface == null || !members.Surface.IsInstanceOfType(surface))
        {
            log.Once("ResizeStretch: the render surface is " + (surface?.GetType().FullName ?? "null") + ", not " +
                     Members.SurfaceType + Keeps);
            return null;
        }
        var compositedWindow = members.SurfaceWindow.GetValue(surface);
        if (compositedWindow == null)
        {
            beforeFirstFrame = true;
            return null;
        }
        var shared = members.Shared.GetValue(compositedWindow)!;
        var syncRoot = members.SyncRoot.GetValue(shared)!;
        var compositor = members.Compositor.GetValue(shared)!;

        string? failure;
        ResizeStretch? applied = null;
        // RESULT.md step 2: the render thread takes the same lock for every frame (BeginTransaction), so no frame is
        // drawn half-changed.
        lock (syncRoot)
        {
            var content = members.Visual.GetValue(compositedWindow)!;
            var brush = members.SurfaceBrush.GetValue(compositedWindow)!;
            var content2 = members.QueryInterface(content, members.IVisual2);
            object? parked = null;
            var sizeBefore = (Vector2)members.GetSize.Invoke(content, null)!;
            var relativeBefore = (Vector2)members.GetRelative.Invoke(content2, null)!;
            var stretchBefore = members.GetStretch.Invoke(brush, null)!;
            try
            {
                members.SetSize.Invoke(content, [Vector2.Zero]);
                members.SetRelative.Invoke(content2, [Vector2.One]);
                members.SetStretch.Invoke(brush, [members.Fill]);
                var sprite = members.CreateSpriteVisual.Invoke(compositor, null)!;
                try { parked = members.QueryInterface(sprite, members.IVisual); }
                finally { ((IDisposable)sprite).Dispose(); }
                members.Visual.SetValue(compositedWindow, parked);

                // RESULT.md step 3: read both back.
                var (relative, stretch) = ReadBack.Current(
                    (Vector2)members.GetRelative.Invoke(content2, null)!, members.GetStretch.Invoke(brush, null)!);
                failure = relative == Vector2.One && Equals(stretch, members.Fill)
                    ? null
                    : string.Create(CultureInfo.InvariantCulture,
                        $"ResizeStretch: read back RelativeSizeAdjustment ({relative.X},{relative.Y}) and Stretch {stretch}, not (1,1) and Fill");
                if (failure == null) applied = new ResizeStretch(members, compositedWindow, syncRoot, content, parked);
            }
            catch (Exception e)
            {
                failure = "ResizeStretch: " + (e is TargetInvocationException { InnerException: { } inner } ? inner : e).Message;
            }
            if (failure != null)
            {
                // Leave the window as Avalonia made it: the real visual, its absolute size, the brush's own stretch.
                try
                {
                    members.Visual.SetValue(compositedWindow, content);
                    members.SetSize.Invoke(content, [sizeBefore]);
                    members.SetRelative.Invoke(content2, [relativeBefore]);
                    members.SetStretch.Invoke(brush, [stretchBefore]);
                }
                catch (Exception e)
                {
                    failure += "; undoing it failed: " + (e is TargetInvocationException { InnerException: { } inner } ? inner : e).Message;
                }
                (parked as IDisposable)?.Dispose();
            }
            ((IDisposable)content2).Dispose();
        }

        if (applied == null)
        {
            log.Once(failure + Keeps);
            return null;
        }
        Applied.Add(window, applied);
        window.Closed += (_, _) => applied.Restore();
        return applied;
    }

    private static bool IsWindowImpl(Type? type)
    {
        for (; type != null; type = type.BaseType)
            if (type.FullName == Members.WindowImplType) return true;
        return false;
    }

    /// <summary>The Avalonia.Win32 and MicroCom members ED-101 names, resolved once per Avalonia.Win32 assembly.</summary>
    private sealed class Members
    {
        public const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public const string WindowImplType = "Avalonia.Win32.WindowImpl";
        public const string SurfaceType = "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface";
        public const string CompositedWindowType = "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow";
        public const string SharedType = "Avalonia.Win32.WinRT.Composition.WinUiCompositionShared";
        public const string IVisualType = "Avalonia.Win32.WinRT.IVisual";
        public const string IVisual2Type = "Avalonia.Win32.WinRT.IVisual2";
        public const string BrushType = "Avalonia.Win32.WinRT.ICompositionSurfaceBrush";
        public const string StretchType = "Avalonia.Win32.WinRT.CompositionStretch";
        public const string CompositorType = "Avalonia.Win32.WinRT.ICompositor";
        public const string RuntimeType = "MicroCom.Runtime.MicroComRuntime";

        private static readonly ConditionalWeakTable<Assembly, Members> Cache = new();

        private readonly Assembly assembly;
        private readonly MethodInfo? queryInterface;

        private Members(Assembly assembly)
        {
            this.assembly = assembly;
            if (typeof(Window).GetProperty("PlatformImpl", AnyInstance) == null) Missing.Add("Avalonia.Controls.Window.PlatformImpl");
            GlSurface = Field(WindowImplType, "_glSurface")!;
            SurfaceWindow = Field(SurfaceType, "_window")!;
            Shared = Field(CompositedWindowType, "_shared")!;
            Visual = Field(CompositedWindowType, "_visual")!;
            SurfaceBrush = Field(CompositedWindowType, "_surfaceBrush")!;
            SyncRoot = Property(SharedType, "SyncRoot")!;
            Compositor = Property(SharedType, "Compositor")!;
            SetSize = Method(IVisualType, "SetSize")!;
            GetSize = Method(IVisualType, "get_Size")!;
            SetRelative = Method(IVisual2Type, "SetRelativeSizeAdjustment")!;
            GetRelative = Method(IVisual2Type, "get_RelativeSizeAdjustment")!;
            SetStretch = Method(BrushType, "SetStretch")!;
            GetStretch = Method(BrushType, "get_Stretch")!;
            CreateSpriteVisual = Method(CompositorType, "CreateSpriteVisual")!;
            Surface = assembly.GetType(SurfaceType)!;
            IVisual = assembly.GetType(IVisualType)!;
            IVisual2 = assembly.GetType(IVisual2Type)!;

            var stretch = assembly.GetType(StretchType);
            if (stretch is { IsEnum: true } && Enum.GetNames(stretch).Contains("Fill", StringComparer.Ordinal))
                Fill = Enum.Parse(stretch, "Fill");
            else Missing.Add(StretchType + ".Fill");

            // MicroComRuntime.QueryInterface<T>(IUnknown) lives in the MicroCom.Runtime that IVisual derives from.
            var runtime = assembly.GetType(IVisualType)?.GetInterfaces().FirstOrDefault(i => i.FullName == "MicroCom.Runtime.IUnknown")?.Assembly
                .GetType(RuntimeType);
            queryInterface = runtime?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
                m.Name == "QueryInterface" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            if (queryInterface == null) Missing.Add(RuntimeType + ".QueryInterface");
        }

        public List<string> Missing { get; } = [];
        public FieldInfo GlSurface { get; }
        public FieldInfo SurfaceWindow { get; }
        public FieldInfo Shared { get; }
        public FieldInfo Visual { get; }
        public FieldInfo SurfaceBrush { get; }
        public PropertyInfo SyncRoot { get; }
        public PropertyInfo Compositor { get; }
        public MethodInfo SetSize { get; }
        public MethodInfo GetSize { get; }
        public MethodInfo SetRelative { get; }
        public MethodInfo GetRelative { get; }
        public MethodInfo SetStretch { get; }
        public MethodInfo GetStretch { get; }
        public MethodInfo CreateSpriteVisual { get; }
        public Type Surface { get; }
        public Type IVisual { get; }
        public Type IVisual2 { get; }
        public object Fill { get; } = null!;

        public static Members For(Assembly assembly) => Cache.GetValue(assembly, a => new Members(a));

        public object QueryInterface(object proxy, Type iface) => queryInterface!.MakeGenericMethod(iface).Invoke(null, [proxy])!;

        private FieldInfo? Field(string type, string name)
        {
            var field = assembly.GetType(type)?.GetField(name, AnyInstance);
            if (field == null) Missing.Add(type + "." + name);
            return field;
        }

        private PropertyInfo? Property(string type, string name)
        {
            var property = assembly.GetType(type)?.GetProperty(name, AnyInstance);
            if (property == null) Missing.Add(type + "." + name);
            return property;
        }

        private MethodInfo? Method(string type, string name)
        {
            var method = assembly.GetType(type)?.GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            if (method == null) Missing.Add(type + "." + name);
            return method;
        }
    }
}
