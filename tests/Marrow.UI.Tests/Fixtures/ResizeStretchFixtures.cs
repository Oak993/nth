using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// Real Avalonia.Win32 windows for the ResizeStretch tests (ED-101, OD-0017). The remedy reaches Avalonia.Win32's WinUI
// composition objects, which exist only on a real Win32 window after its first frame, so these fixtures run a real
// Win32 platform. Two constraints shape them:
// - This test assembly already hosts the headless session (R-TEST-7), and Avalonia allows one platform per set of
//   Avalonia assemblies. The Win32 platform therefore runs in its own AssemblyLoadContext, with its own copies of the
//   Avalonia, MicroCom and Marrow assemblies, on its own STA thread; nothing it does reaches the headless session.
// - Marrow.UI.Tests has no Avalonia.Win32 dependency (only Marrow.App has Avalonia.Desktop). The binaries come from the
//   NuGet package folder at the pinned versions: avalonia.win32 12.1.2 (Directory.Packages.props) and its nuspec
//   dependency avalonia.angle.windows.natives 2.1.27548.20260419. They are the files the app ships.
// The composition state is read here with the fixture's own reflection, independent of ResizeStretch's, the way
// porting/probes/p4b/Program.cs (class Comp) read it.

/// <summary>Runs a RealWindowDriver scenario inside the isolated Win32 context and returns its key=value readings.</summary>
public static class RealWin32
{
    private static readonly Lazy<IsolatedAvaloniaContext> Context = new(() => new IsolatedAvaloniaContext());

    public static IReadOnlyDictionary<string, string> Run(string scenario)
    {
        var assembly = Context.Value.LoadFromAssemblyName(typeof(RealWin32).Assembly.GetName());
        var driver = assembly.GetType(typeof(RealWindowDriver).FullName!, throwOnError: true)!;
        var method = driver.GetMethod(scenario, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(driver.FullName, scenario);
        string[] lines;
        try { lines = (string[])method.Invoke(null, null)!; }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        var readings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            int at = line.IndexOf('=', StringComparison.Ordinal);
            readings[line[..at]] = line[(at + 1)..];
        }
        return readings;
    }
}

/// <summary>The isolated load context: Avalonia, MicroCom and Marrow load again from the test folder, Avalonia.Win32
/// and the ANGLE natives from the pinned NuGet packages; everything else is shared with the default context.</summary>
public sealed class IsolatedAvaloniaContext() : AssemblyLoadContext("ResizeStretch Win32", isCollectible: false)
{
    public static string PackageRoot =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } root
            ? root
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public static string Win32Folder => Path.Combine(PackageRoot, "avalonia.win32", "12.1.2", "lib", "net10.0");

    public static string AngleFolder => Path.Combine(PackageRoot, "avalonia.angle.windows.natives", "2.1.27548.20260419",
        "runtimes", RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64", "native");

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? "";
        if (name is "Avalonia.Win32" or "Avalonia.Win32.Automation")
            return LoadFromAssemblyPath(Path.Combine(Win32Folder, name + ".dll"));
        if (name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("MicroCom", StringComparison.Ordinal)
            || name.StartsWith("Marrow.", StringComparison.Ordinal))
        {
            string path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (File.Exists(path)) return LoadFromAssemblyPath(path);
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        unmanagedDllName.StartsWith("av_libglesv2", StringComparison.OrdinalIgnoreCase)
            ? LoadUnmanagedDllFromPath(Path.Combine(AngleFolder, "av_libglesv2.dll"))
            : IntPtr.Zero;
}

/// <summary>A resizable window that draws something, so every frame has content.</summary>
public sealed class StretchSubjectWindow : Window
{
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Magenta, new Rect(Bounds.Size));
        context.FillRectangle(Brushes.Cyan, new Rect(20, 20, 40, 40));
    }
}

/// <summary>Scenarios run on a real Win32 window. This type is only ever used inside IsolatedAvaloniaContext.</summary>
public static class RealWindowDriver
{
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly object Gate = new();
    private static bool started;

    private static Assembly Win32 =>
        AssemblyLoadContext.GetLoadContext(typeof(RealWindowDriver).Assembly)!.LoadFromAssemblyName(new AssemblyName("Avalonia.Win32"));

    private static Type WinRT(string name) => Win32.GetType("Avalonia.Win32.WinRT." + name, throwOnError: true)!;

    private static void EnsurePlatform()
    {
        lock (Gate)
        {
            if (started) return;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var builder = AppBuilder.Configure<Application>().UseSkia().UseHarfBuzz();
                    Win32.GetType("Avalonia.Win32ApplicationExtensions", throwOnError: true)!
                        .GetMethod("UseWin32", BindingFlags.Public | BindingFlags.Static)!
                        .Invoke(null, [builder]);
                    builder.SetupWithoutStarting();
                    ready.SetResult();
                }
                catch (Exception e)
                {
                    ready.SetException(e);
                    return;
                }
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            })
            { IsBackground = true, Name = "ResizeStretch Win32 UI" };
            if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Task.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("the Win32 platform did not start");
            started = true;
        }
    }

    private static string[] OnUi(Func<List<string>, Task> body)
    {
        EnsurePlatform();
        var lines = new List<string>();
        var task = Dispatcher.UIThread.InvokeAsync(() => body(lines));
        if (!task.Wait(TimeSpan.FromSeconds(90))) throw new TimeoutException("scenario timed out; readings so far: " + string.Join("; ", lines));
        return [.. lines];
    }

    // ---- scenarios ----

    /// <summary>The pinned versions and the members ResizeStretch needs, checked against Avalonia.Win32 12.1.2.</summary>
    public static string[] Members() => OnUi(lines =>
    {
        lines.Add("avalonia.base.version=" + typeof(AvaloniaObject).Assembly.GetName().Version);
        lines.Add("avalonia.controls.version=" + typeof(Window).Assembly.GetName().Version);
        lines.Add("avalonia.win32.version=" + Win32.GetName().Version);
        lines.Add("required=" + string.Join(";", ResizeStretch.RequiredMembers));
        lines.Add("missing=" + string.Join(";", ResizeStretch.MissingMembers(Win32)));
        return Task.CompletedTask;
    });

    /// <summary>Apply after the first frame, read back, resize, then close.</summary>
    public static string[] ApplyResizeClose() => OnUi(async lines =>
    {
        var window = NewWindow();
        window.Show();
        var (surface, cw, sync) = await FirstFrame(window, lines);
        object real, brush;
        lock (sync)
        {
            real = Field(cw, "_visual")!;
            brush = Field(cw, "_surfaceBrush")!;
            lines.Add("before.client.px=" + ClientPx(window));
            lines.Add("before.content.size=" + Size(real));
            lines.Add("before.content.relative=" + Relative(real));
            lines.Add("before.stretch=" + Stretch(brush));
        }

        var logs = new List<string>();
        ResizeStretch? applied;
        using (ResizeStretch.Log.Override(new ResizeStretchLog(logs.Add))) applied = ResizeStretch.Apply(window);
        lines.Add("applied=" + (applied != null));
        lines.Add("logs=" + logs.Count);

        object parked;
        lock (sync)
        {
            parked = Field(cw, "_visual")!;
            lines.Add("after.parked.is.content=" + ReferenceEquals(parked, real));
            lines.Add("after.content.size=" + Size(real));
            lines.Add("after.content.relative=" + Relative(real));
            lines.Add("after.stretch=" + Stretch(brush));
        }

        string restPx = ClientPx(window);
        window.Width = 560;
        window.Height = 380;
        bool resized = await Frames(window, () => { lock (sync) return ClientPx(window) != restPx && Size(parked) == ClientPx(window); }, 600);
        lock (sync)
        {
            lines.Add("resized.seen=" + resized);
            lines.Add("resized.client.px=" + ClientPx(window));
            lines.Add("resized.parked.size=" + Size(parked));
            lines.Add("resized.content.size=" + Size(real));
            lines.Add("resized.content.relative=" + Relative(real));
            lines.Add("resized.stretch=" + Stretch(brush));
        }

        window.Close();
        lines.Add("closed.surface.released=" + await Until(() => Field(surface, "_window") == null, 200));
        lines.Add("closed.visual.is.content=" + ReferenceEquals(Field(cw, "_visual"), real));
        lines.Add("closed.content.disposed=" + IsDisposed(real));
        lines.Add("closed.parked.disposed=" + IsDisposed(parked));
    });

    /// <summary>Attach before the window is shown: the remedy lands after the first frame and is undone on close.</summary>
    public static string[] AttachClose() => OnUi(async lines =>
    {
        var window = NewWindow();
        var pending = ResizeStretch.Attach(window);
        lines.Add("pending.before.show=" + !pending.IsCompleted);
        window.Show();
        var applied = await pending;
        lines.Add("applied=" + (applied != null));
        var surface = Field(Prop(window, "PlatformImpl")!, "_glSurface")!;
        var cw = Field(surface, "_window")!;
        var sync = Prop(Field(cw, "_shared")!, "SyncRoot")!;
        object parked;
        lock (sync)
        {
            parked = Field(cw, "_visual")!;
            lines.Add("stretch=" + Stretch(Field(cw, "_surfaceBrush")!));
        }
        window.Close();
        lines.Add("closed.surface.released=" + await Until(() => Field(surface, "_window") == null, 200));
        lines.Add("closed.visual.is.parked=" + ReferenceEquals(Field(cw, "_visual"), parked));
        lines.Add("closed.parked.disposed=" + IsDisposed(parked));
    });

    /// <summary>The window at rest before and after the remedy: geometry and the rendered surface size.</summary>
    public static string[] AtRest() => OnUi(async lines =>
    {
        var window = NewWindow();
        window.Show();
        var (_, cw, sync) = await FirstFrame(window, lines);
        void Read(string prefix)
        {
            lock (sync)
            {
                lines.Add(prefix + ".position=" + window.Position);
                lines.Add(prefix + ".client.size=" + window.ClientSize);
                lines.Add(prefix + ".client.px=" + ClientPx(window));
                lines.Add(prefix + ".window.rect=" + WindowRect(window));
                lines.Add(prefix + ".state=" + window.WindowState);
                lines.Add(prefix + ".scene.px=" + Pixel(Field(cw, "_size")!));
            }
        }
        Read("before");
        lines.Add("applied=" + (ResizeStretch.Apply(window) != null));
        int frames = 0;
        window.InvalidateVisual();
        lines.Add("frames.seen=" + await Frames(window, () => ++frames >= 10, 600));
        Read("after");
        window.Close();
    });

    /// <summary>A read-back of RelativeSizeAdjustment that differs from (1,1) (RESULT.md step 3).</summary>
    public static string[] RelativeReadBackDiffers() =>
        LeftAsAvaloniaMadeIt(() => ResizeStretch.ReadBack.Override((_, stretch) => (Vector2.Zero, stretch)));

    /// <summary>A read-back of the brush Stretch that differs from Fill (RESULT.md step 3).</summary>
    public static string[] StretchReadBackDiffers() =>
        LeftAsAvaloniaMadeIt(() => ResizeStretch.ReadBack.Override((relative, stretch) => (relative, Enum.Parse(stretch.GetType(), "Uniform"))));

    /// <summary>A read-back that fails with an exception after every value has been changed.</summary>
    public static string[] ReadBackThrows() =>
        LeftAsAvaloniaMadeIt(() => ResizeStretch.ReadBack.Override((_, _) => throw new InvalidOperationException("read-back refused")));

    /// <summary>A member that the Avalonia.Win32 in use lacks, on a real WindowImpl whose first frame is drawn.</summary>
    public static string[] MemberMissing() => LeftAsAvaloniaMadeIt(() => ResizeStretch.Unresolved.Override(
        new HashSet<string>(StringComparer.Ordinal) { "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow._surfaceBrush" }));

    /// <summary>
    /// ED-101: "if any member is missing or a read-back differs it logs once and leaves the window as Avalonia made it".
    /// Applies twice under the given seam after the first frame, then reads the composition state, resizes and closes.
    /// </summary>
    private static string[] LeftAsAvaloniaMadeIt(Func<IDisposable> seam) => OnUi(async lines =>
    {
        var window = NewWindow();
        window.Show();
        var (surface, cw, sync) = await FirstFrame(window, lines);
        object real, brush;
        lock (sync)
        {
            real = Field(cw, "_visual")!;
            brush = Field(cw, "_surfaceBrush")!;
            lines.Add("before.client.px=" + ClientPx(window));
            lines.Add("before.content.size=" + Size(real));
            lines.Add("before.content.relative=" + Relative(real));
            lines.Add("before.stretch=" + Stretch(brush));
        }

        var logs = new List<string>();
        ResizeStretch? first, second;
        using (ResizeStretch.Log.Override(new ResizeStretchLog(logs.Add)))
        using (seam())
        {
            first = ResizeStretch.Apply(window);
            second = ResizeStretch.Apply(window);
        }
        lines.Add("applied=" + (first != null || second != null));
        lines.Add("logs=" + logs.Count);
        lines.Add("log=" + string.Join(" | ", logs));

        lock (sync)
        {
            lines.Add("after.visual.is.content=" + ReferenceEquals(Field(cw, "_visual"), real));
            lines.Add("after.content.disposed=" + IsDisposed(real));
            lines.Add("after.client.px=" + ClientPx(window));
            lines.Add("after.content.size=" + Size(real));
            lines.Add("after.content.relative=" + Relative(real));
            lines.Add("after.stretch=" + Stretch(brush));
        }

        // Avalonia's own ResizeIfNeeded still sizes the real content visual.
        string restPx = ClientPx(window);
        window.Width = 560;
        window.Height = 380;
        bool resized = await Frames(window, () => { lock (sync) return ClientPx(window) != restPx && Size(real) == ClientPx(window); }, 600);
        lock (sync)
        {
            lines.Add("resized.seen=" + resized);
            lines.Add("resized.visual.is.content=" + ReferenceEquals(Field(cw, "_visual"), real));
            lines.Add("resized.content.relative=" + Relative(real));
            lines.Add("resized.stretch=" + Stretch(brush));
        }

        window.Close();
        lines.Add("closed.surface.released=" + await Until(() => Field(surface, "_window") == null, 200));
        lines.Add("closed.content.disposed=" + IsDisposed(real));
    });

    /// <summary>
    /// Apply, Apply again and Attach on one real window after its first frame (ED-101; RESULT.md steps 2 and 4): one
    /// remedy, one parked visual, and on close the real visual back in `_visual` and released by Avalonia.
    /// </summary>
    public static string[] ApplyTwiceClose() => OnUi(async lines =>
    {
        var window = NewWindow();
        window.Show();
        var (surface, cw, sync) = await FirstFrame(window, lines);
        object real, brush;
        lock (sync)
        {
            real = Field(cw, "_visual")!;
            brush = Field(cw, "_surfaceBrush")!;
        }

        var logs = new List<string>();
        ResizeStretch? first, second, attached;
        object parked;
        using (ResizeStretch.Log.Override(new ResizeStretchLog(logs.Add)))
        {
            first = ResizeStretch.Apply(window);
            lock (sync) parked = Field(cw, "_visual")!;
            second = ResizeStretch.Apply(window);
            attached = await ResizeStretch.Attach(window);
        }
        lines.Add("first.applied=" + (first != null));
        lines.Add("second.is.first=" + ReferenceEquals(first, second));
        lines.Add("attached.is.first=" + ReferenceEquals(first, attached));
        lines.Add("logs=" + logs.Count);
        lock (sync)
        {
            lines.Add("after.parked.is.content=" + ReferenceEquals(parked, real));
            lines.Add("after.visual.is.parked=" + ReferenceEquals(Field(cw, "_visual"), parked));
            lines.Add("after.parked.disposed=" + IsDisposed(parked));
            lines.Add("after.content.disposed=" + IsDisposed(real));
            lines.Add("after.content.size=" + Size(real));
            lines.Add("after.content.relative=" + Relative(real));
            lines.Add("after.stretch=" + Stretch(brush));
        }

        window.Close();
        lines.Add("closed.surface.released=" + await Until(() => Field(surface, "_window") == null, 200));
        lines.Add("closed.visual.is.content=" + ReferenceEquals(Field(cw, "_visual"), real));
        lines.Add("closed.content.disposed=" + IsDisposed(real));
        lines.Add("closed.parked.disposed=" + IsDisposed(parked));
    });

    /// <summary>
    /// For each of ResizeStretch.RequiredMembers in turn, Apply on one real window after its first frame while that one
    /// member does not resolve (ResizeStretch.Unresolved), then read what the attempt logged and left behind.
    /// Readings are keyed "missing.&lt;member&gt;.&lt;reading&gt;".
    /// </summary>
    public static string[] EachMemberMissing() => OnUi(async lines =>
    {
        var window = NewWindow();
        window.Show();
        var (surface, cw, sync) = await FirstFrame(window, lines);
        object real, brush;
        lock (sync)
        {
            real = Field(cw, "_visual")!;
            brush = Field(cw, "_surfaceBrush")!;
            lines.Add("before.client.px=" + ClientPx(window));
        }

        foreach (var member in ResizeStretch.RequiredMembers)
        {
            var logs = new List<string>();
            ResizeStretch? applied;
            using (ResizeStretch.Log.Override(new ResizeStretchLog(logs.Add)))
            using (ResizeStretch.Unresolved.Override(new HashSet<string>(StringComparer.Ordinal) { member }))
                applied = ResizeStretch.Apply(window);
            string key = "missing." + member + ".";
            lines.Add(key + "applied=" + (applied != null));
            lines.Add(key + "logs=" + logs.Count);
            lines.Add(key + "log=" + string.Join(" | ", logs));
            lock (sync)
            {
                lines.Add(key + "visual.is.content=" + ReferenceEquals(Field(cw, "_visual"), real));
                lines.Add(key + "content.size=" + Size(real));
                lines.Add(key + "content.relative=" + Relative(real));
                lines.Add(key + "stretch=" + Stretch(brush));
            }
        }

        window.Close();
        lines.Add("closed.surface.released=" + await Until(() => Field(surface, "_window") == null, 200));
        lines.Add("closed.content.disposed=" + IsDisposed(real));
    });

    // ---- helpers ----

    private static Window NewWindow() => new StretchSubjectWindow
    {
        Width = 480,
        Height = 320,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Position = new PixelPoint(160, 160),
        ShowActivated = false,
        CanResize = true,
        Title = "ResizeStretch test",
    };

    private static async Task<(object Surface, object Cw, object Sync)> FirstFrame(Window window, List<string> lines)
    {
        object? surface = Field(Prop(window, "PlatformImpl")!, "_glSurface");
        lines.Add("surface.type=" + surface?.GetType().FullName);
        object? cw = null;
        bool drawn = await Frames(window, () =>
        {
            cw = surface == null ? null : Field(surface, "_window");
            if (cw == null) return false;
            lock (Prop(Field(cw, "_shared")!, "SyncRoot")!) return Size(Field(cw, "_visual")!) == ClientPx(window);
        }, 600);
        if (!drawn || cw == null) throw new InvalidOperationException("no first frame at the client size; " + string.Join("; ", lines));
        return (surface!, cw, Prop(Field(cw, "_shared")!, "SyncRoot")!);
    }

    private static Task<bool> Frames(TopLevel window, Func<bool> done, int max)
    {
        var result = new TaskCompletionSource<bool>();
        int count = 0;
        void Tick(TimeSpan _)
        {
            if (done()) result.TrySetResult(true);
            else if (++count >= max) result.TrySetResult(false);
            else window.RequestAnimationFrame(Tick);
        }
        window.RequestAnimationFrame(Tick);
        return result.Task;
    }

    private static async Task<bool> Until(Func<bool> done, int max)
    {
        for (int i = 0; i < max; i++)
        {
            if (done()) return true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        return done();
    }

    private static object? Field(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, AnyInstance) is { } field) return field.GetValue(target);
        throw new MissingFieldException(target.GetType().FullName, name);
    }

    private static object? Prop(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetProperty(name, AnyInstance) is { } property) return property.GetValue(target);
        throw new MissingMemberException(target.GetType().FullName, name);
    }

    private static object QueryInterface(object proxy, Type iface)
    {
        var unknown = iface.GetInterfaces().First(i => i.FullName == "MicroCom.Runtime.IUnknown");
        var runtime = unknown.Assembly.GetType("MicroCom.Runtime.MicroComRuntime", throwOnError: true)!;
        var query = runtime.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        return query.MakeGenericMethod(iface).Invoke(null, [proxy])!;
    }

    private static object? Call(object target, Type iface, string method) => iface.GetMethod(method)!.Invoke(target, null);

    private static string Vector(Vector2 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X},{v.Y}");

    private static string Size(object visual) => Vector((Vector2)Call(visual, WinRT("IVisual"), "get_Size")!);

    private static string Relative(object visual)
    {
        var visual2 = QueryInterface(visual, WinRT("IVisual2"));
        try { return Vector((Vector2)Call(visual2, WinRT("IVisual2"), "get_RelativeSizeAdjustment")!); }
        finally { ((IDisposable)visual2).Dispose(); }
    }

    private static string Stretch(object brush) => Call(brush, WinRT("ICompositionSurfaceBrush"), "get_Stretch")!.ToString()!;

    private static bool IsDisposed(object proxy) => (bool)Prop(proxy, "IsDisposed")!;

    private static string Pixel(object pixelSize) => pixelSize is PixelSize p ? string.Create(CultureInfo.InvariantCulture, $"{p.Width},{p.Height}") : "?";

    private static IntPtr Hwnd(Window window) => window.TryGetPlatformHandle()!.Handle;

    private static string ClientPx(Window window)
    {
        GetClientRect(Hwnd(window), out var r);
        return string.Create(CultureInfo.InvariantCulture, $"{r.Right - r.Left},{r.Bottom - r.Top}");
    }

    private static string WindowRect(Window window)
    {
        GetWindowRect(Hwnd(window), out var r);
        return string.Create(CultureInfo.InvariantCulture, $"{r.Left},{r.Top},{r.Right},{r.Bottom}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
}
