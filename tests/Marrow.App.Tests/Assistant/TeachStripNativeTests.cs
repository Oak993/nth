using Avalonia.Controls;
using Marrow.App.Assistant.Teach;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it alone, with focus): DESIGN.md rev 5, 10 step 1, 16.1 "Native gate", AS-19. The recording
// strip in a real window: it is always on top, borderless, out of the taskbar, at the top centre of the main screen's work area; Windows has it
// left out of every screen copy (display affinity), and it takes no focus when it opens or when a button is pressed, so what the owner types
// keeps going to the app he is showing. The owner looks once at the strip while a recording runs, then pauses and resumes it, and stops it.
[Collection("WindowManagerNative")]
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class TeachStripNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    public void The_strip_is_on_top_out_of_every_frame_and_never_takes_the_focus()
    {
        native.Run(_ =>
        {
            using var rig = new TeachControllerRig(strip: false);
            var host = new TeachStripHost();
            Assert.True(native.OnUi(() => host.Show(rig.Controller)));
            var window = native.OnUi(() => host.Window)!;
            native.Register(window);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => window.IsVisible && window.IsMeasureValid), "the strip did not show", 10000);
            var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
            Assert.True(native.OnUi(() => window.Topmost));
            Assert.False(native.OnUi(() => window.ShowInTaskbar));
            Assert.True(TeachNative.GetWindowDisplayAffinity(handle, out var affinity));
            Assert.Equal(TeachNative.WdaExcludeFromCapture, affinity);
            var style = (long)TeachNative.GetWindowLongPtr(handle, TeachNative.GwlExStyle);
            Assert.NotEqual(0, style & TeachNative.WsExNoActivate);
            Assert.NotEqual(TeachNative.GetForegroundWindow(), handle);
            Assert.True(native.OnUi(() => window.Position.Y) < 80);
            native.OnUi(() => host.Hide());
            WindowManagerNativeMethods.Until(() => native.OnUi(() => !window.IsVisible), "the strip did not go away", 10000);
        });
    }
}
