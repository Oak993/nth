using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// ED-063 bridge checks are Windows port-only, not invented Swift parity claims.
// Factory/lifecycle source: _packet/swift/WindowManager.swift:26-37,86-110; resize:113-140.
// Each Fact automatically selects itself in a fresh child; the ordinary parent may run Headless.
// The painted fixture caption zones are not SettingsView's production WindowChrome overlay: the production sidebar (the real SettingsView,
// in the Settings panel) is the first Fact below, the corner and backdrop pixels are GlassWindowNativeTests'. (Was STOP
// SETTINGS-NATIVE-SURFACE; the continuous corner curve, ED-181, is the one part still open.)
// OD-0140 (ED-NEW-settingsinapp-1): Settings is a panel inside the main window (SettingsOverlay), so the app makes no Settings window. The
// fixture's second window (subject.Settings) is a stand-in the fixture makes itself; here it is only another top-level window of the
// app's thread (a foreground or capture target), never a subject. The Settings window's own caption, size, placement and close facts
// went with it; the facts below that open Settings open it as the panel in a main window.
// CLOSED STOP FACTORY-STRETCH (2026-10-04): WindowChrome.Configure binds ResizeStretch (WindowChrome.cs:170) for every factory
// window, and WindowManagerFactoryStretchTests proves it; the ED-101 composition remedy is not these messages' job.
[Collection("WindowManagerNative")]
public sealed class WindowManagerNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0140")] // Settings is a panel in the main window; its sidebar's drag region would drag the main window, so it is off
    public void The_actual_Settings_sidebar_background_in_the_panel_is_not_a_native_caption_of_the_main_window()
    {
        native.Run(subject =>
        {
            var (window, card, client) = native.OnUi(() =>
            {
                // the panel's open animates (SettingsOverlay.Open), which asks for frames: the fixture's window supplies them
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
                var manager = new WindowManager { MainContent = () => new Border() };
                manager.ShowSettings();
                var window = manager.MainWindow ?? throw new InvalidOperationException("Settings did not show the main window.");
                native.Register(window);
                var overlay = manager.SettingsOverlay ?? throw new InvalidOperationException("Settings did not open as the panel in the main window.");
                Assert.Same(window, overlay.Window);
                Assert.True(manager.IsSettingsOpen);
                HostTemplate(window);
                var view = Assert.Single(overlay.GetVisualDescendants().OfType<SettingsView>());
                var drag = Assert.Single(view.GetVisualDescendants().OfType<WindowDragArea>());
                Assert.False(drag.IsHitTestVisible);
                var client = drag.TranslatePoint(new Point(180, 18), window);
                Assert.NotNull(client);
                return (window, overlay.Card, client.Value);
            });
            N.Until(() => native.OnUi(() => window.InputHitTest(client) is Visual hit && (ReferenceEquals(hit, card) || card.IsVisualAncestorOf(hit))),
                "The actual Settings sidebar in the panel did not become a managed hit target inside the panel after native layout.");
            var point = native.OnUi(() => window.PointToScreen(client));
            var handle = native.OnUi(() => window.TryGetPlatformHandle()?.Handle ?? 0);
            N.RequireOwner(handle);
            Assert.NotEqual(N.HTCAPTION, N.Hit(handle, new NativeWindowPoint(point.X, point.Y)));
        });
    }

    // A bare Application has no production theme/Window template: the host template that displays the factory's content subtree
    // (as WindowManagerNativeSubject's).
    private static void HostTemplate(Avalonia.Controls.Window window)
    {
        window.ShowInTaskbar = false;
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) =>
            new ContentPresenter { Content = owner.Content });
        window.ApplyTemplate();
        window.UpdateLayout();
    }

    [Fact]
    [Trait("Req", "PR-WIN-009")] // WindowManager.swift:26-30,63,97; native title transport on an owned factory window.
    public void The_main_factory_publishes_the_injected_Unicode_display_name_to_its_own_HWND()
    {
        native.Run(subject => native.OnUi(() =>
        {
            const string displayName = "Native window proof \U000003A9 007";
            using var info = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
            {
                ["CFBundleDisplayName"] = displayName,
            }, isDevelopment: true));
            var manager = new WindowManager { MainContent = () => new Border() };
            manager.ShowMain();
            var made = manager.MainWindow ?? throw new InvalidOperationException("ShowMain did not retain its factory window.");
            native.Register(made);
            var window = Assert.IsType<ThreadWindow>(made);
            window.ShowInTaskbar = false;
            var handle = window.TryGetPlatformHandle();
            Assert.NotNull(handle);
            Assert.NotEqual((nint)0, handle.Handle);
            N.RequireOwner(handle.Handle);
            Assert.Equal(native.ThreadId, N.GetWindowThreadProcessId(handle.Handle, out _));
            Assert.True(N.IsWindowVisible(handle.Handle));
            Assert.Equal(displayName, window.Title);
            Assert.Equal(displayName, WindowManagerNativeTitle.ReadOnOwnerThread(handle.Handle));
        }));
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")] // WindowManager.swift:93-101; ED-063 actual caption toggle.
    public void Actual_maximize_caption_click_then_a_second_click_restores_the_main_window()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            var original = N.Rect(subject.MainProbe.Handle);
            using var pointer = new WindowManagerNativePointer(subject);
            var point = subject.Center(subject.MainContent.MaximizeZone);
            pointer.Down(subject.MainProbe, point, N.HTMAXBUTTON);
            N.Until(() => subject.MainProbe.Messages.Any(item => item.Message == N.WM_NCLBUTTONDOWN && item.Returned && item.PendingCaption == N.HTMAXBUTTON),
                "The actual maximize press did not reach the registered bridge.");
            pointer.Up(subject.MainProbe, point);
            N.Until(() => N.IsZoomed(subject.MainProbe.Handle), "The actual maximize caption click did not maximize its HWND.");
            subject.MainProbe.Fence();
            Assert.Equal(WindowState.Maximized, native.OnUi(() => subject.Main.WindowState));
            subject.Focus(subject.Main, subject.MainProbe);
            point = subject.Center(subject.MainContent.MaximizeZone);
            var presses = subject.MainProbe.Count(N.WM_NCLBUTTONDOWN, returned: true);
            pointer.Down(subject.MainProbe, point, N.HTMAXBUTTON);
            N.Until(() => subject.MainProbe.Count(N.WM_NCLBUTTONDOWN, returned: true) > presses,
                "The second actual maximize press did not return.");
            pointer.Up(subject.MainProbe, point);
            N.Until(() => !N.IsZoomed(subject.MainProbe.Handle), "The second actual maximize caption click did not restore its HWND.");
            subject.MainProbe.Fence();
            Assert.Equal(WindowState.Normal, native.OnUi(() => subject.Main.WindowState));
            var restored = N.Rect(subject.MainProbe.Handle);
            Assert.Equal(original.Left, restored.Left); Assert.Equal(original.Top, restored.Top);
            Assert.Equal(original.Width, restored.Width); Assert.Equal(original.Height, restored.Height);
            Assert.Equal(1, subject.MainProbe.CommandCount(N.SC_MAXIMIZE));
            Assert.Equal(1, subject.MainProbe.CommandCount(N.SC_RESTORE));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-017")] // WindowManager.swift:129-139, observer lifetime belongs to the retained window.
    public void A_second_real_resize_after_hide_and_reopen_reuses_the_same_registered_window_observer()
    {
        native.Run(subject =>
        {
            var handle = subject.MainProbe.Handle;
            var live = native.OnUi(() => AppEnvironment.GetWindowLiveResize(subject.MainContent));
            using var pointer = new WindowManagerNativePointer(subject);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                subject.Focus(subject.Main, subject.MainProbe);
                var before = N.Rect(handle);
                var edge = WindowManagerNativePointer.RightEdge(subject.MainProbe, resizable: true);
                var moved = new NativeWindowPoint(edge.X + 48, edge.Y);
                pointer.Down(subject.MainProbe, edge, N.HTRIGHT);
                N.Until(() => N.Gui(native.ThreadId).Capture == handle, "The retained-window resize did not capture.");
                pointer.Move(subject.MainProbe, moved);
                N.Until(() => N.Rect(handle).Width > before.Width,
                    "The actual retained-window drag did not resize its native frame.");
                // Observe the production flag while the real sizing loop still holds capture, but
                // release our physical press before asserting it. A missing flag must not leave a
                // held button until a behavior timeout, allowing cleanup to mask the actual failure.
                var mainWasResizing = subject.MainProbe.IsResizing;
                pointer.Up(subject.MainProbe, moved);
                N.Until(() => !subject.MainProbe.IsResizing && N.Gui(native.ThreadId).Capture == 0,
                    "The actual retained-window resize did not end.");
                subject.MainProbe.Fence();
                Assert.True(mainWasResizing, $"The actual retained-window resize did not publish its active state on attempt {attempt + 1}.");
                Assert.True(N.Rect(handle).Width > before.Width);
                if (attempt == 0)
                {
                    native.OnUi(() => { subject.Main.Hide(); subject.Manager.ShowMain(); });
                    Assert.Same(subject.Main, native.OnUi(() => subject.Manager.MainWindow));
                    Assert.Equal(handle, native.OnUi(() => subject.Main.TryGetPlatformHandle()!.Handle));
                    Assert.Same(live, native.OnUi(() => AppEnvironment.GetWindowLiveResize(subject.MainContent)));
                }
            }
            Assert.Equal(new[] { true, false, true, false }, subject.MainProbe.ResizeTransitions);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-008")] // WindowManager.swift:96, defer:false realizes the native window before presentation.
    public void The_factory_realizes_its_owned_native_handle_before_the_first_Show()
    {
        // A live invisible HWND proves realization (defer: false). RESOLVED FACTORY-RENDER-POLICY (ED-284): "buffered"
        // is NOT_APPLICABLE on Windows (an Avalonia window is drawn through DWM's composition buffer), and the system's window
        // animation is proved on real pixels by The_factory_windows_are_hidden_with_the_systems_fade_not_cut_...
        native.Run(subject => native.OnUi(() =>
        {
            var manager = new WindowManager { MainContent = () => new Border(), SettingsContent = () => new Border() };
            var factory = typeof(WindowManager).GetMethod("MakeMainWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(factory);
            var window = Assert.IsType<ThreadWindow>(factory.Invoke(manager, null));
            native.Register(window);
            Assert.False(window.IsVisible);
            var handle = window.TryGetPlatformHandle();
            Assert.NotNull(handle);
            Assert.NotEqual((nint)0, handle.Handle);
            Assert.True(N.IsWindow(handle.Handle));
            N.RequireOwner(handle.Handle);
            Assert.Equal(native.ThreadId, N.GetWindowThreadProcessId(handle.Handle, out _));
            Assert.False(N.IsWindowVisible(handle.Handle));
        }));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // WindowManager.swift:36-37, :57-60; an already visible window still becomes key.
    [Trait("Decision", "OD-0140")] // the window Settings opens in, and so the one brought forward, is the main window; it is not moved
    public void Reopening_Settings_activates_the_native_main_window_its_panel_is_in_and_leaves_that_window_where_it_is()
    {
        native.Run(subject =>
        {
            // The subject's manager hosts the fixture's stand-in content, so a manager of the app's own Settings content opens the panel.
            var (manager, window, handle) = native.OnUi(() =>
            {
                // the panel's open animates (SettingsOverlay.Open), which asks for frames: the fixture's window supplies them
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
                var manager = new WindowManager { MainContent = () => new Border() };
                manager.ShowSettings();
                var window = manager.MainWindow ?? throw new InvalidOperationException("Settings did not show the main window.");
                native.Register(window);
                Assert.True(manager.IsSettingsOpen);
                Assert.Same(window, manager.SettingsOverlay?.Window);
                HostTemplate(window);
                return (manager, window, window.TryGetPlatformHandle()?.Handle ?? 0);
            });
            N.RequireOwner(handle);
            // Another of the app's windows takes the foreground; the main window stays visible behind it with the panel open.
            subject.Focus(subject.Main, subject.MainProbe);
            native.OnUi(() =>
            {
                subject.Main.Topmost = false;
                window.Topmost = false;
                Assert.True(window.IsVisible);
                Assert.True(manager.IsSettingsOpen);
                Assert.True(subject.Main.IsActive);
                Assert.False(window.IsActive);
            });
            subject.MainProbe.Fence();
            WindowManagerNativeForeground.RequireStillForeground(subject.MainProbe.Handle, native.ThreadId);
            var before = native.OnUi(() => WindowManagerNativePlacementGeometry.Read(handle));

            // Neither a second focus helper nor another Show may manufacture the observed result.
            native.OnUi(() => manager.ShowSettings());
            N.Until(() => N.GetForegroundWindow() == handle, "Showing Settings did not bring the main window its panel is in to the foreground.");
            Assert.True(native.OnUi(() => window.IsActive));
            Assert.True(native.OnUi(() => manager.IsSettingsOpen));
            Assert.Same(window, native.OnUi(() => manager.SettingsOverlay?.Window));
            var after = native.OnUi(() => WindowManagerNativePlacementGeometry.Read(handle));
            Assert.True(after.Visible);
            Assert.Equal(before.Origin, after.Origin);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    public void Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe) }) // OD-0140: no Settings factory window
            {
                var (window, content, probe) = entry;
                subject.Focus(window, probe);
                var clientReleasesBeforePress = probe.Count(N.WM_LBUTTONUP, returned: true);
                var point = subject.Center(content.MinimizeZone);
                pointer.Down(probe, point, N.HTMINBUTTON);
                // A14: production down should return while the physical button remains held.
                N.Until(() => probe.Count(N.WM_NCLBUTTONDOWN, returned: true) > 0,
                    "Caption down did not return before release; native default tracking may have taken over.", 1000);
                Assert.Equal(probe.Handle, N.Gui(native.ThreadId).Capture);
                Assert.Equal((nint)N.HTMINBUTTON, subject.PendingCaption(window));
                Assert.False(N.IsIconic(probe.Handle));
                Assert.Equal(0, probe.CommandCount(N.SC_MINIMIZE));
                pointer.Up(probe, point);
                N.Until(() => probe.Count(N.WM_LBUTTONUP, returned: true) > clientReleasesBeforePress,
                    "The captured physical release did not arrive as WM_LBUTTONUP.");
                N.Until(() => N.IsIconic(probe.Handle), "Actual caption release never minimized its factory HWND.");
                N.Until(() => N.Gui(native.ThreadId).Capture == 0, "Caption release left native capture owned.");
                Assert.Equal((nint)0, subject.PendingCaption(window));
                Assert.Equal(1, probe.CommandCount(N.SC_MINIMIZE));
                Assert.True(probe.Count(N.WM_LBUTTONUP) > 0,
                    "Expected a captured client release. Do not substitute a nonclient release in this fixture.");
                subject.RestoreForNextCheck(window, probe);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")] // WindowManager.swift:93-101; ED-063 applies to the factory window (OD-0140: the main one only).
    public void Release_over_a_different_caption_control_cancels_the_original_command()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe) })
            {
                var (window, content, probe) = entry;
                subject.Focus(window, probe);
                var down = subject.Center(content.MinimizeZone);
                var up = subject.Center(content.CloseZone);
                Assert.Equal(N.HTCLOSE, N.Hit(probe.Handle, up));
                pointer.Down(probe, down, N.HTMINBUTTON);
                N.Until(() => N.Gui(native.ThreadId).Capture == probe.Handle, "Caption down did not capture.");
                pointer.Up(probe, up);
                N.Until(() => probe.Count(N.WM_LBUTTONUP, returned: true) + probe.Count(N.WM_NCLBUTTONUP, returned: true) > 0,
                    "No real pointer release reached the native window.");
                probe.Fence();
                Assert.Equal((nint)0, subject.PendingCaption(window));
                Assert.Equal((nint)0, N.Gui(native.ThreadId).Capture);
                Assert.Equal(0, probe.CommandCount(N.SC_MINIMIZE));
                Assert.Equal(0, probe.CommandCount(N.SC_CLOSE));
                Assert.Equal(0, subject.QuitRequests);
                Assert.False(N.IsIconic(probe.Handle));
                Assert.True(N.IsWindowVisible(probe.Handle));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")] // ED-063 cancellation on main and capture transfer away from main (OD-0140: no Settings factory window).
    public void Cancel_mode_and_real_capture_transfer_clear_the_pending_caption_press()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            subject.Focus(subject.Main, subject.MainProbe);
            var mainPoint = subject.Center(subject.MainContent.MinimizeZone);
            pointer.Down(subject.MainProbe, mainPoint, N.HTMINBUTTON);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Main caption did not capture.");
            Assert.Equal((nint)N.HTMINBUTTON, subject.PendingCaption(subject.Main));
            N.Post(subject.MainProbe.Handle, N.WM_CANCELMODE);
            N.Until(() => subject.MainProbe.Count(N.WM_CANCELMODE, returned: true) > 0, "Native cancel mode was not processed.");
            Assert.Equal((nint)0, subject.PendingCaption(subject.Main));
            pointer.Up(subject.MainProbe, mainPoint);
            N.Until(() => subject.MainProbe.Count(N.WM_LBUTTONUP, returned: true) + subject.MainProbe.Count(N.WM_NCLBUTTONUP, returned: true) > 0,
                "Cancelled native press did not receive its real release.");
            subject.MainProbe.Fence();
            Assert.Equal(0, subject.MainProbe.CommandCount(N.SC_MINIMIZE));

            // The capture transfer, on the main window now (it was the Settings window's): its press loses capture to the fixture's other
            // top-level window, a plain capture target.
            subject.Focus(subject.Main, subject.MainProbe);
            mainPoint = subject.Center(subject.MainContent.MinimizeZone);
            pointer.Down(subject.MainProbe, mainPoint, N.HTMINBUTTON);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Main caption did not capture again.");
            Assert.Equal((nint)N.HTMINBUTTON, subject.PendingCaption(subject.Main));
            var captureMessages = subject.MainProbe.Count(N.WM_CAPTURECHANGED);
            // Real ownership transfer causes WM_CAPTURECHANGED; no fabricated pointer or capture event.
            native.OnUi(() => { _ = N.SetCapture(subject.SettingsProbe.Handle); });
            N.Until(() => subject.MainProbe.Count(N.WM_CAPTURECHANGED) > captureMessages, "Capture transfer was not delivered.");
            Assert.Equal((nint)0, subject.PendingCaption(subject.Main));
            var otherReleases = subject.SettingsProbe.Count(N.WM_LBUTTONUP, returned: true);
            pointer.Up(subject.MainProbe, mainPoint);
            N.Until(() => subject.SettingsProbe.Count(N.WM_LBUTTONUP, returned: true) > otherReleases,
                "Transferred native capture did not receive the real release.");
            subject.MainProbe.Fence();
            native.OnUi(() => { _ = N.ReleaseCapture(); });
            Assert.Equal(0, subject.MainProbe.CommandCount(N.SC_MINIMIZE));
            Assert.False(N.IsIconic(subject.MainProbe.Handle));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")] // ED-063 keyboard system menu belongs to the factory window (OD-0140: the main one only).
    public void Space_key_menu_is_anchored_to_its_factory_window_instead_of_decoding_the_character_as_coordinates()
    {
        native.Run(subject =>
        {
            foreach (var entry in new[] { (subject.Main, subject.MainProbe) })
            {
                var (target, probe) = entry;
                subject.Focus(target, probe);
                var window = N.Rect(probe.Handle);
                var scale = native.OnUi(() => target.DesktopScaling);
                using var menu = new WindowManagerNativeMenu(native, probe);
                N.Post(probe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_KEYMENU, (nint)' ');
                var popup = menu.WaitForMenu();
                // ED-063/P7 anchors the keyboard menu to the window's leading caption edge. No Swift
                // pixel-exact offset is specified; this envelope tolerates native borders and DPI.
                Assert.InRange(popup.Bounds.Left, window.Left - (int)(16 * scale), window.Left + (int)(96 * scale));
                Assert.InRange(popup.Bounds.Top, window.Top - (int)(16 * scale), window.Top + (int)(100 * scale));
                Assert.False(menu.DeadlineFired);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    public void A_nonspace_key_menu_mnemonic_does_not_open_the_system_menu()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            using var menu = new WindowManagerNativeMenu(native, subject.MainProbe);
            N.Post(subject.MainProbe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_KEYMENU, (nint)'f');
            N.Until(() => subject.MainProbe.CommandCount(N.SC_KEYMENU) > 0, "SC_KEYMENU mnemonic never arrived.");
            Assert.False(menu.AppearsWithin(800), "The system menu opened for the nonspace mnemonic 'f'.");
            N.Until(() => subject.MainProbe.Messages.Any(item => item.Message == N.WM_SYSCOMMAND && item.Returned && item.LParam == (nint)'f'),
                "The nonspace command did not return.");
            Assert.Equal(0, subject.MainProbe.Count(N.WM_ENTERMENULOOP));
            Assert.Equal(0, subject.MainProbe.Count(N.WM_INITMENUPOPUP));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    public void A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe, true) }) // OD-0140: no Settings factory window
            {
                var (window, content, probe, canResize) = entry;
                subject.Focus(window, probe);
                using var menu = new WindowManagerNativeMenu(native, probe);
                pointer.RightClick(probe, subject.Caption(content), N.HTCAPTION);
                var popup = menu.WaitForMenu();
                Assert.True(probe.Count(N.WM_CONTEXTMENU) > 0, "The native right click did not reach WM_CONTEXTMENU.");
                Assert.Equal(canResize, popup.Enabled(N.SC_SIZE));
                // ED-NEW-native-fix (T45 owner override): Maximize is on (it was for the fixed-size Settings window too, which OD-0140
                // removed), and the managed CanMaximize agrees with what the native menu offers.
                Assert.True(popup.Enabled(N.SC_MAXIMIZE));
                Assert.True(native.OnUi(() => window.CanMaximize));
                Assert.Equal(canResize, native.OnUi(() => window.CanResize));
                Assert.True(popup.Enabled(N.SC_MINIMIZE));
                Assert.True(popup.Enabled(N.SC_CLOSE));
                Assert.False(popup.Enabled(N.SC_RESTORE));
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    public void Restoring_a_window_minimized_from_maximized_preserves_its_maximized_state()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            N.Post(subject.MainProbe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_MAXIMIZE);
            N.Until(() => N.IsZoomed(subject.MainProbe.Handle), "The setup SC_MAXIMIZE did not maximize the HWND.");
            N.Post(subject.MainProbe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_MINIMIZE);
            N.Until(() => N.IsIconic(subject.MainProbe.Handle), "The setup SC_MINIMIZE did not minimize the HWND.");
            var placement = new NativeWindowPlacement { Length = (uint)Marshal.SizeOf<NativeWindowPlacement>() };
            N.Check(N.GetWindowPlacement(subject.MainProbe.Handle, ref placement), "GetWindowPlacement");
            Assert.True((placement.Flags & 0x0002) != 0, "Native placement did not retain WPF_RESTORETOMAXIMIZED.");
            // Actual command delivery to the factory HWND, not a call of ExecuteSystemCommand.
            N.Post(subject.MainProbe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_RESTORE);
            N.Until(() => !N.IsIconic(subject.MainProbe.Handle), "SC_RESTORE did not unminimize the HWND.");
            Assert.True(N.IsZoomed(subject.MainProbe.Handle), "SC_RESTORE lost the maximized state.");
            Assert.Equal(WindowState.Maximized, native.OnUi(() => subject.Main.WindowState));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-017")]
    public void A_real_edge_drag_activates_only_the_factory_created_resize_state_and_clears_it_on_release()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            Assert.NotNull(native.OnUi(() => AppEnvironment.GetWindowLiveResize(subject.MainContent))); // OD-0140: the only factory window's own state
            using var pointer = new WindowManagerNativePointer(subject);
            var before = N.Rect(subject.MainProbe.Handle);
            var edge = WindowManagerNativePointer.RightEdge(subject.MainProbe, resizable: true);
            pointer.Down(subject.MainProbe, edge, N.HTRIGHT);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Native edge drag did not capture.");
            var moved = new NativeWindowPoint(edge.X + 64, edge.Y);
            pointer.Move(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_SIZING) > 0, "The actual drag produced no WM_SIZING.");
            N.Until(() => subject.MainProbe.IsResizing, "The factory-created live-resize hook did not activate during WM_SIZING.");
            pointer.Up(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > 0 && !subject.MainProbe.IsResizing,
                "Live resize did not end after actual pointer release.");
            var after = N.Rect(subject.MainProbe.Handle);
            Assert.True(after.Width > before.Width, "The pointer drag did not change the native frame width.");
            Assert.Equal(new[] { true, false }, subject.MainProbe.ResizeTransitions);
            Assert.True(subject.MainProbe.Count(N.WM_ENTERSIZEMOVE) > 0);
        });
    }

    // WindowManager.swift:65 window.contentMinSize = NSSize(width: 860, height: 560): "Resized below the minimum: prevented by
    // contentMinSize". The fixture's main window is 880 x 570, so a real drag of its right edge 300 px left and of its bottom edge
    // 300 px up asks for 580 x 270 points; the HWND must stop at the minimum, measured as the client size in points at its DPI.
    [Fact]
    [Trait("Req", "UI-LAUNCH-001")] // AppDelegate.swift:22; WindowManager.swift:62-69 (contentMinSize :65)
    [Trait("Req", "PR-LAUNCH-028")] // the same minimum
    public void The_main_window_refuses_an_actual_border_resize_below_860_by_560_points()
    {
        native.Run(subject =>
        {
            const int HTBOTTOM = 15;
            subject.Focus(subject.Main, subject.MainProbe);
            using var pointer = new WindowManagerNativePointer(subject);
            var scale = native.OnUi(() => subject.Main.DesktopScaling);
            var before = native.OnUi(() => subject.Main.ClientSize);
            // WM_GETMINMAXINFO read back from the HWND's own procedure: the minimum track size less the HWND's non-client frame
            // (outer less client, both measured) is the smallest client the window allows, in pixels.
            var track = N.MinTrackSize(subject.MainProbe.Handle);
            var outer = N.Rect(subject.MainProbe.Handle);
            var frameWidth = outer.Width - before.Width * scale;
            var frameHeight = outer.Height - before.Height * scale;
            Assert.Equal(860 * scale, track.X - frameWidth, 1.5);
            Assert.Equal(560 * scale, track.Y - frameHeight, 1.5);
            Assert.True(before.Width > 860 && before.Height > 560, $"the fixture's main window starts above the minimum: {before}");

            var edge = WindowManagerNativePointer.RightEdge(subject.MainProbe, resizable: true);
            pointer.Down(subject.MainProbe, edge, N.HTRIGHT);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Native edge drag did not capture.");
            var moved = new NativeWindowPoint(edge.X - (int)(300 * scale), edge.Y);
            pointer.Move(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_SIZING) > 0, "The actual drag produced no WM_SIZING.");
            pointer.Up(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > 0 && !subject.MainProbe.IsResizing, "Live resize did not end after the release.");
            var narrowed = native.OnUi(() => subject.Main.ClientSize);
            Assert.True(narrowed.Width < before.Width, $"the drag did not narrow the window: {before.Width} to {narrowed.Width}");
            Assert.InRange(narrowed.Width, 860 - 1.0 / scale, 860 + 1.0 / scale); // stopped at the minimum, 300 points short of the request
            Assert.Equal(before.Height, narrowed.Height, 1);

            subject.Focus(subject.Main, subject.MainProbe);
            var rectangle = N.Rect(subject.MainProbe.Handle);
            var bottom = default(NativeWindowPoint?);
            for (var inset = 1; inset <= 20 && bottom is null; inset++)
            {
                var point = new NativeWindowPoint(rectangle.Left + rectangle.Width / 2, rectangle.Bottom - inset);
                if (N.RootAt(point) == subject.MainProbe.Handle && N.Hit(subject.MainProbe.Handle, point) == HTBOTTOM) bottom = point;
            }
            Assert.NotNull(bottom);
            var exitsBefore = subject.MainProbe.Count(N.WM_EXITSIZEMOVE);
            pointer.Down(subject.MainProbe, bottom.Value, HTBOTTOM);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Native bottom edge drag did not capture.");
            var up = new NativeWindowPoint(bottom.Value.X, bottom.Value.Y - (int)(300 * scale));
            pointer.Move(subject.MainProbe, up);
            pointer.Up(subject.MainProbe, up);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > exitsBefore && !subject.MainProbe.IsResizing, "Live resize did not end after the second release.");
            var shortened = native.OnUi(() => subject.Main.ClientSize);
            Assert.True(shortened.Height < before.Height, $"the drag did not shorten the window: {before.Height} to {shortened.Height}");
            Assert.InRange(shortened.Height, 560 - 1.0 / scale, 560 + 1.0 / scale);
            Assert.InRange(shortened.Width, 860 - 1.0 / scale, 860 + 1.0 / scale);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-017")]
    public void A_real_caption_move_does_not_publish_a_resize_on_either_factory_window()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            using var pointer = new WindowManagerNativePointer(subject);
            var before = N.Rect(subject.MainProbe.Handle);
            var point = subject.Caption(subject.MainContent);
            pointer.Down(subject.MainProbe, point, N.HTCAPTION);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Caption move did not capture.");
            var moved = new NativeWindowPoint(point.X + 56, point.Y + 30);
            pointer.Move(subject.MainProbe, moved);
            N.Until(() => N.Rect(subject.MainProbe.Handle).Left != before.Left, "The caption drag did not move the native frame.");
            pointer.Up(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > 0, "Native caption move did not end.");
            Assert.Empty(subject.MainProbe.ResizeTransitions);
            Assert.Equal(0, subject.MainProbe.Count(N.WM_SIZING));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-WIN-008")] // OD-0140: the Settings window whose HWND survived close/reopen (:98) is gone; its panel closes in SettingsOverlayTests.
    public void An_actual_close_click_requests_main_termination_and_keeps_the_main_window()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            subject.Focus(subject.Main, subject.MainProbe);
            var mainPoint = subject.Center(subject.MainContent.CloseZone);
            pointer.Down(subject.MainProbe, mainPoint, N.HTCLOSE);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Main close did not capture.");
            pointer.Up(subject.MainProbe, mainPoint);
            N.Until(() => subject.QuitRequests == 1, "Main close did not reach the existing termination seam.");
            Assert.True(N.IsWindowVisible(subject.MainProbe.Handle));
            Assert.True(N.IsWindow(subject.MainProbe.Handle));
        });
    }

    [Fact]
    [Trait("Decision", "ED-107")] // (5) NativeContextMenus installs at the shared builder point; proposal ED-229 (1)
    public void A_right_click_menu_is_the_native_Win32_popup_and_choosing_its_first_row_clicks_that_item()
    {
        native.Run(_ =>
        {
            var clicked = 0;
            var (window, target) = native.OnUi(() =>
            {
                var rename = new MenuItem { Header = "Rename" };
                rename.Click += (_, _) => clicked++;
                var target = new Border
                {
                    Width = 200,
                    Height = 100,
                    Background = Avalonia.Media.Brushes.White,
                    ContextMenu = new ContextMenu { Items = { rename, new Separator(), new MenuItem { Header = "Archive" } } },
                };
                var window = new Avalonia.Controls.Window { Content = target, Width = 300, Height = 200, ShowInTaskbar = false };
                native.Register(window);
                window.Show();
                window.UpdateLayout();
                return (window, target);
            });
            var handle = native.OnUi(() => window.TryGetPlatformHandle()?.Handle ?? 0);
            native.OnUi(() => WindowManagerNativeForeground.Acquire(handle, native.ThreadId));
            var at = native.OnUi(() => target.PointToScreen(new Point(20, 20)));
            var point = new NativeWindowPoint(at.X, at.Y);
            Assert.True(N.GetCursorPos(out var originalCursor));
            // A real right-click, guarded like WindowManagerNativePointer's: the test window is in front, the point is its
            // client area, and no button is held. Nothing calls the presenter directly: the click reaches Avalonia as
            // WM_RBUTTONDOWN/UP, Avalonia raises ContextRequested, and the class handler NativeContextMenus.Install put in
            // place opens the Win32 menu. Its modal loop (TrackPopupMenuEx) runs on the UI thread, so it is driven from
            // here with posted keys, as a person would: Down selects the first row, Return chooses it.
            if ((N.GetAsyncKeyState(0x01) & 0x8000) != 0 || (N.GetAsyncKeyState(0x02) & 0x8000) != 0)
                throw new InvalidOperationException("NATIVE_PRECONDITION: a mouse button is already held. Native input was not started.");
            if (N.GetForegroundWindow() != handle || N.RootAt(point) != handle || N.Hit(handle, point) != N.HTCLIENT)
                throw new InvalidOperationException("NATIVE_PRECONDITION: the menu target is covered, not in front, or not client area.");
            nint popup = 0;
            try
            {
                N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos on the test window");
                N.Mouse(N.MOUSE_MOVE);
                if (N.RootAt(point) != handle) throw new InvalidOperationException("NATIVE_PRECONDITION: the test window lost the point.");
                N.Mouse(N.MOUSE_RIGHTDOWN, N.MOUSE_RIGHTUP);
                N.Until(() => (popup = N.MenuOn(native.ThreadId)) != 0, "No native popup menu appeared after a real right-click.", 3000);
                N.Post(popup, 0x0100, 0x28); // WM_KEYDOWN VK_DOWN
                N.Post(popup, 0x0100, 0x0D); // WM_KEYDOWN VK_RETURN
                N.Until(() => N.MenuOn(native.ThreadId) == 0, "The native menu did not close after Return.", 5000);
                N.Until(() => native.OnUi(() => clicked) == 1, "Choosing the first row did not click its item.", 5000);
                Assert.Equal(1, native.OnUi(() => clicked)); // once, not twice
            }
            finally
            {
                if (N.MenuOn(native.ThreadId) != 0) N.Post(handle, N.WM_CANCELMODE);
                N.Check(N.SetCursorPos(originalCursor.X, originalCursor.Y), "Restore test cursor position");
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-001")] // SidebarView.swift:74-80 the Color.clear clearance spacer, :146 `.background { if !inPopover { WindowDragArea() } }`: "pressing it drags the window"
    public void The_actual_main_sidebar_clearance_spacer_reaches_native_caption_hit_testing_and_drags_the_window()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            // The real SidebarView (the column, not the popover form) over the factory's main window, below the fixture's own caption zone,
            // so that the only caption under the points is the one the sidebar's own WindowDragArea gives.
            var (view, area, spacer, field) = native.OnUi(() =>
            {
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
                var host = new Border { Width = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(0, 60, 0, 0) };
                AppEnvironment.SetAppModel(host, subject.Manager.Model);
                var column = new Marrow.App.Views.Sidebar.SidebarView(false, null);
                host.Child = column;
                subject.MainContent.Children.Add(host);
                subject.Main.UpdateLayout();
                var drag = Assert.Single(column.GetVisualDescendants().OfType<WindowDragArea>());
                var search = Assert.Single(column.GetVisualDescendants().OfType<SidebarSearchField>());
                return (column, drag, new Point(140, 23), search.TranslatePoint(new Point(20, 14), column) ?? throw new InvalidOperationException("the search field is not under the column"));
            });
            // :74-80 the spacer sits 16..30 pt down, in the gutter beside the search row's top padding.
            N.Until(() => native.OnUi(() => view.InputHitTest(spacer) is Visual hit && (ReferenceEquals(hit, area) || hit.GetVisualAncestors().Contains(area))),
                "The sidebar's drag area did not become the managed hit target at the clearance spacer after native layout.");
            Assert.Equal(Win32Properties.Win32HitTestValue.Caption, native.OnUi(() => Win32Properties.GetNonClientHitTestResult(area)));
            NativeWindowPoint Screen(Point at) => native.OnUi(() => { var point = view.PointToScreen(at); return new NativeWindowPoint(point.X, point.Y); });
            var spacerPoint = Screen(spacer);
            var fieldPoint = Screen(field);
            Assert.Equal(N.HTCAPTION, N.Hit(subject.MainProbe.Handle, spacerPoint)); // the spacer is the window's caption to Windows
            Assert.Equal(N.HTCLIENT, N.Hit(subject.MainProbe.Handle, fieldPoint)); // the search field is the field's own
            using var pointer = new WindowManagerNativePointer(subject);
            var before = N.Rect(subject.MainProbe.Handle);
            pointer.Down(subject.MainProbe, spacerPoint, N.HTCAPTION);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Pressing the clearance spacer did not start the system move loop.");
            var moved = new NativeWindowPoint(spacerPoint.X + 56, spacerPoint.Y + 30);
            pointer.Move(subject.MainProbe, moved);
            N.Until(() => N.Rect(subject.MainProbe.Handle).Left != before.Left, "Dragging from the clearance spacer did not move the native frame.");
            pointer.Up(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > 0, "The move from the clearance spacer did not end.");
            Assert.InRange(N.Rect(subject.MainProbe.Handle).Left, before.Left + 56 - 8, before.Left + 56 + 8); // it followed the pointer (the system may clamp to the work area by a few pixels)
            Assert.Empty(subject.MainProbe.ResizeTransitions); // a move, not a resize
        });
    }
}
