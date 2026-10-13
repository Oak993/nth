using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Fixtures;

// Owner-scheduled only. A test-owned window and profile; all page requests receive the in-memory fixture.
public sealed class VisusNativeRig : IDisposable
{
    public VisusNativeRig(WindowManagerNativeSession native)
    {
        Folder = Path.Combine(Path.GetTempPath(), "nth-w8c-native-" + Guid.NewGuid().ToString("N"));
        VisusHost.ResetForTests();
        VisusHost.DataFolder = null;
        VisusHost.SurfaceFactory = () => new WebSurface(new WebView2Engine(), new WebProfiles(Folder, "en-US"));
        Session = new VisusSession();
        Tab = Session.NewTab(VisusTabKind.Browse, "https://nth-w8c.invalid/page");
        Tab.View!.Engine!.SetResourceHandler(new VisusWebDocument());
        Tab.View.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess) LastSuccessfulUrl = e.Url;
            Loaded = e.IsSuccess && e.Url == "https://nth-w8c.invalid/page";
        };
        Body = new VisusPaneBody(Session);
        Window = new Avalonia.Controls.Window
        {
            Title = "W8c native fixture", Width = 800, Height = 600,
            Content = Body, ShowInTaskbar = false,
        };
        Window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(Window);
        Window.Show();
    }

    public string Folder { get; }
    public VisusSession Session { get; }
    public VisusTab Tab { get; }
    public VisusPaneBody Body { get; }
    public Avalonia.Controls.Window Window { get; }
    public bool Loaded { get; private set; }
    public string? LastSuccessfulUrl { get; private set; }

    internal static nint PrintDialogOn(uint thread)
    {
        nint found = 0;
        NativeWindowEnumerator callback = (window, _) =>
        {
            if (!WindowManagerNativeMethods.IsWindowVisible(window) || WindowManagerNativeMethods.ClassName(window) != "#32770") return true;
            WindowManagerNativeMethods.RequireOwner(window);
            found = window;
            return false;
        };
        _ = WindowManagerNativeMethods.EnumThreadWindows(thread, callback, 0);
        GC.KeepAlive(callback);
        return found;
    }

    public void Dispose()
    {
        Window.Close();
        Session.Dispose();
        VisusHost.ResetForTests();
        try { Directory.Delete(Folder, true); }
        catch (IOException) { } // The test-owned browser process may still be releasing its profile.
    }
}
