using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Marrow.App.Views;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The real factory main window (WindowManager.ShowMain, the real RootView) and the OLE drop target Avalonia registered on its HWND
// (RegisterDragDrop keeps it under the window property "OleDropTargetInterface"): what Explorer's drag talks to. The driver calls
// DragEnter, DragOver and Drop on that object with a real IDataObject, on the window's own thread, as OLE would.
internal sealed class RootViewDropNativeRig : IDisposable
{
    internal const int DropEffectCopy = 1;
    private const int AllEffects = 7;

    private readonly WindowManagerNativeSession native;
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "marrow-native-drop-" + Guid.NewGuid().ToString("N"));
    private readonly NativeOleDropTarget target;
    private readonly AvaloniaWindow window;

    internal RootViewDropNativeRig(WindowManagerNativeSession native, WindowManagerNativeSubject subject)
    {
        this.native = native;
        Directory.CreateDirectory(scratch);
        var (manager, shown) = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings)); // Marrow.App sets this at start-up
            var manager = new WindowManager { MainContent = () => new RootView() };
            manager.ShowMain();
            var shown = manager.MainWindow ?? throw new InvalidOperationException("ShowMain did not retain its window.");
            shown.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(shown);
            return (manager, shown);
        }, TimeSpan.FromSeconds(30)); // the real RootView and its first layout
        Manager = manager;
        window = shown;
        // The wrapper is made on the window's own thread (an apartment of its own): OLE calls the target there.
        target = native.OnUi(() =>
        {
            var raw = GetProp(window.TryGetPlatformHandle()!.Handle, "OleDropTargetInterface");
            if (raw == 0) throw new InvalidOperationException("The main HWND has no registered OLE drop target.");
            return (NativeOleDropTarget)Marshal.GetObjectForIUnknown(raw);
        });
        Thread.Sleep(600); // the first layout of the real window
    }

    internal WindowManager Manager { get; }

    internal AppModel Model => Manager.Model;

    internal string Folder(string name)
    {
        var path = Path.Combine(scratch, name);
        Directory.CreateDirectory(path);
        return path;
    }

    internal string File(string name)
    {
        var path = Path.Combine(scratch, name);
        System.IO.File.WriteAllText(path, name);
        return path;
    }

    // A point of the window's client area, as a screen pixel (the drop target is given screen coordinates).
    internal NativeWindowPoint Screen(double fractionX, double fractionY) => native.OnUi(() =>
    {
        var point = window.PointToScreen(new Point(window.ClientSize.Width * fractionX, window.ClientSize.Height * fractionY));
        return new NativeWindowPoint(point.X, point.Y);
    });

    // The middle of the selected thread's composer, as a screen pixel.
    internal NativeWindowPoint ComposerCenter() => native.OnUi(() =>
    {
        var editor = window.GetVisualDescendants().OfType<ComposerNSTextView>().Single();
        if (editor.Bounds.Width <= 0 || editor.Bounds.Height <= 0) throw new InvalidOperationException("The composer has no laid-out drop target.");
        var inWindow = editor.TranslatePoint(new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2), window) ?? throw new InvalidOperationException("The composer is not in the window.");
        var point = window.PointToScreen(inWindow);
        return new NativeWindowPoint(point.X, point.Y);
    });

    // The effect the window reported, for IDropTarget.DragEnter / DragOver / Drop.
    internal int Enter(NativeDropDataObject data, NativeWindowPoint point) => native.OnUi(() =>
    {
        var effect = AllEffects;
        Marshal.ThrowExceptionForHR(target.DragEnter(data, 0, point, ref effect));
        return effect;
    });

    internal int Over(NativeWindowPoint point) => native.OnUi(() =>
    {
        var effect = AllEffects;
        Marshal.ThrowExceptionForHR(target.DragOver(0, point, ref effect));
        return effect;
    });

    internal int Drop(NativeDropDataObject data, NativeWindowPoint point) => native.OnUi(() =>
    {
        var effect = AllEffects;
        Marshal.ThrowExceptionForHR(target.Drop(data, 0, point, ref effect));
        return effect;
    });

    internal T OnUi<T>(Func<T> read) => native.OnUi(read);

    // Polls the model until it satisfies the condition (imports run on a detached task and hop back to the main thread).
    internal void Until(Func<bool> condition, string reason) => WindowManagerNativeMethods.Until(() => native.OnUi(condition), reason, 15000);

    public void Dispose()
    {
        try { native.OnUi(() => Manager.MainWindow?.Hide()); }
        catch (Exception) { }
        try { Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")] private static extern nint GetProp(nint window, string name);
}
