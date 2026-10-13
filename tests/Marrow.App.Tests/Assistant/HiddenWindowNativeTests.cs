using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Window;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-10, THE SPIKE's second half, "Hidden window" (the lead runs it alone, in a quiet window, on a
// real Avalonia platform with a real HWND). The headless platform keeps a closed window alive whatever the app does, so the memory claim
// cannot be settled there. Here, with a real window holding a large tree of views: ParkMain closes the window for real, a full collection runs
// after the UI has drained, and the old tree must be gone (not reachable from anything, so its memory is free); the process's private bytes
// are measured before and after and reported if they did not fall. If the tree is still alive after the close, set WindowManager.Park to
// ParkKind.Hide (the design's fallback), record the measured cost in ED-NEW-dextra-as10-13, and run this again. Then showing the window again
// makes a new one that is on screen.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-as10-13")]
[Trait("Feature", "AS-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class HiddenWindowNativeTests(WindowManagerNativeSession native)
{
    private const int Rows = 4000;

    [Fact]
    public void Closing_the_main_window_for_real_frees_its_views_and_showing_it_again_builds_a_new_one()
    {
        native.Run(subject =>
        {
            WeakReference? tree = null;
            long shown = 0;
            long parked = 0;
            var (manager, window) = native.OnUi(() =>
            {
                subject.Main.Hide();
                subject.Settings.Hide();
                var made = new WindowManager
                {
                    Park = WindowManager.ParkKind.Close,
                    MainContent = () =>
                    {
                        var root = new StackPanel();
                        for (var row = 0; row < Rows; row++) root.Children.Add(new TextBlock { Text = "Row " + row + " of the thread, with a line of text long enough to matter." });
                        tree = new WeakReference(root);
                        return root;
                    },
                };
                var main = made.PrepareMain();
                main.ShowInTaskbar = false;
                native.Register(main);
                made.ShowMain();
                return (made, main);
            });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => window.IsVisible && window.IsMeasureValid), "the window did not show", 10000);
            shown = PrivateBytes();

            Assert.True(native.OnUi(manager.ParkMain));
            Assert.True(native.OnUi(() => manager.MainParked));
            Assert.False(native.OnUi(() => window.IsVisible));
            for (var pass = 0; pass < 6; pass++)
            {
                native.OnUi(() => Dispatcher.UIThread.RunJobs());
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                Thread.Sleep(150);
            }

            parked = PrivateBytes();
            Assert.False(tree!.IsAlive, $"The closed window's views are still in memory (private bytes {shown / 1024 / 1024} MB shown, {parked / 1024 / 1024} MB parked): set WindowManager.Park to Hide.");

            native.OnUi(manager.PrepareMain);
            native.OnUi(manager.ShowMain);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => manager.MainWindow is { IsVisible: true }), "the window did not come back", 10000);
            Assert.False(native.OnUi(() => manager.MainParked));
            Assert.NotSame(window, native.OnUi(() => manager.MainWindow));
            native.OnUi(() => manager.MainWindow!.Hide());
        });
    }

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }
}
