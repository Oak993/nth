using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Web;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Store;
using Marrow.UI.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for the web surfaces security review's finding M6 (ED-NEW-web-sec-mcpapps-1; DESIGN.md 7.6 "Pop-out ...
// always on top if pinned", 8.2). Write but NEVER run as an implementing agent: the lead takes native.lock and runs it alone,
// with focus. The real main window holds the foreground; an MCP App's pop-out window then opens as a real Win32 window over
// the fake web engine: nth's main window keeps the foreground, the pop-out is not WS_EX_TOPMOST, the strip's pin makes it
// topmost and a second press takes that off again, and the foreground never moves. The headless half is
// McpAppsReviewCardsTests.The_pop_out_window_opens_unpinned_and_without_taking_focus.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-mcpapps-1")]
public sealed class McpAppWindowNativeTests(WindowManagerNativeSession native)
{
    private const int ExtendedStyle = -20; // GWL_EXSTYLE
    private const long TopmostStyle = 0x8; // WS_EX_TOPMOST

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);

    private static bool IsTopmost(nint window) => (GetWindowLongPtr(window, ExtendedStyle).ToInt64() & TopmostStyle) != 0;

    [Fact]
    public void A_real_pop_out_window_neither_takes_the_foreground_nor_the_top_until_the_user_pins_it() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        native.OnUi(() => subject.Settings.Hide());
        subject.Focus(subject.Main, subject.MainProbe);
        var main = native.OnUi(() => subject.Main.TryGetPlatformHandle()!.Handle);
        Assert.Equal(main, N.GetForegroundWindow());

        var (rig, session) = native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
            var model = new Marrow.App.AppModel();
            var id = Guid.NewGuid();
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
            DocumentPrefetch.Shared.Warm([id], urgent: true);
            var runtime = new ThreadRuntime(id, model);
            var apps = new McpAppsAppRig(runtime);
            var app = McpAppSessions.For(runtime, "call_1", McpAppsAppRig.Board)!;
            app.SetMode(McpAppDisplayMode.PopOut);
            app.Window!.Settle();
            return (apps, app);
        });
        try
        {
            Thread.Sleep(600); // the window's first frames
            var popped = native.OnUi(() => session.Window!.Window.TryGetPlatformHandle()!.Handle);
            Assert.NotEqual(main, popped);
            Assert.Equal(main, N.GetForegroundWindow()); // nth's own window keeps the foreground
            Assert.False(IsTopmost(popped));

            native.OnUi(() => session.Window!.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Thread.Sleep(200);
            Assert.True(IsTopmost(popped));
            native.OnUi(() => session.Window!.PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Thread.Sleep(200);
            Assert.False(IsTopmost(popped));
            Assert.Equal(main, N.GetForegroundWindow());
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    });
}
