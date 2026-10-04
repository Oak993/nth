using System.Runtime.InteropServices;
using System.Reflection;
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
// The painted fixture caption zones are not SettingsView's production WindowChrome overlay: the production sidebar's drag region
// is the first Fact below (the real SettingsView), the corner and backdrop pixels are GlassWindowNativeTests'. (Was STOP
// SETTINGS-NATIVE-SURFACE; the continuous corner curve, ED-181, is the one part still open.)
// CLOSED STOP FACTORY-STRETCH (2026-10-04): WindowChrome.Configure binds ResizeStretch (WindowChrome.cs:170) for every factory
// window, and WindowManagerFactoryStretchTests proves it; the ED-101 composition remedy is not these messages' job.
[Collection("WindowManagerNative")]
public sealed partial class WindowManagerNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    public void The_actual_Settings_sidebar_background_reaches_native_caption_hit_testing()
    {
        native.Run(subject =>
        {
            var (window, drag, client) = native.OnUi(() =>
            {
                // the production open is Rise (OD-0074, ED-249), which asks for frames: the fixture's window supplies them
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
                var manager = new WindowManager();
                manager.ShowSettings();
                var window = (Avalonia.Controls.Window)(typeof(WindowManager)
                    .GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(manager) ?? throw new InvalidOperationException("Settings window was not retained."));
                native.Register(window);
                window.ShowInTaskbar = false;
                window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) =>
                    new ContentPresenter { Content = owner.Content });
                window.ApplyTemplate();
                window.UpdateLayout();
                var view = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
                var drag = Assert.Single(view.GetVisualDescendants().OfType<WindowDragArea>());
                var client = drag.TranslatePoint(new Point(180, 18), window);
                Assert.NotNull(client);
                return (window, drag, client.Value);
            });
            N.Until(() => native.OnUi(() => ReferenceEquals(window.InputHitTest(client), drag)),
                "The actual Settings sidebar did not become the managed hit target after native layout.");
            var point = native.OnUi(() => window.PointToScreen(client));
            var handle = native.OnUi(() => window.TryGetPlatformHandle()?.Handle ?? 0);
            N.RequireOwner(handle);
            Assert.Equal(N.HTCAPTION, N.Hit(handle, new NativeWindowPoint(point.X, point.Y)));
        });
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
                var settingsWasResizing = subject.SettingsProbe.IsResizing;
                pointer.Up(subject.MainProbe, moved);
                N.Until(() => !subject.MainProbe.IsResizing && N.Gui(native.ThreadId).Capture == 0,
                    "The actual retained-window resize did not end.");
                subject.MainProbe.Fence();
                Assert.True(mainWasResizing, $"The actual retained-window resize did not publish its active state on attempt {attempt + 1}.");
                Assert.False(settingsWasResizing);
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
            Assert.Empty(subject.SettingsProbe.ResizeTransitions);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-008")] // WindowManager.swift:96, defer:false realizes the native window before presentation.
    public void The_factory_realizes_its_owned_native_handle_before_the_first_Show()
    {
        // A live invisible HWND proves realization (defer: false). RESOLVED FACTORY-RENDER-POLICY (ED-NEW-windowmanager): "buffered"
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
    [Trait("Req", "PR-WIN-011")] // WindowManager.swift:46-54; native final placement is checked too.
    public void Reopening_visible_Settings_activates_its_native_window_after_centering()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Main, subject.MainProbe);
            native.OnUi(() =>
            {
                subject.Main.Topmost = false;
                subject.Settings.Topmost = false;
                Assert.True(subject.Settings.IsVisible);
                Assert.True(subject.Main.IsActive);
                Assert.False(subject.Settings.IsActive);
            });
            subject.MainProbe.Fence();
            WindowManagerNativeForeground.RequireStillForeground(subject.MainProbe.Handle, native.ThreadId);
            var topology = native.OnUi(WindowManagerNativeMonitorTopology.Read);
            var main = native.OnUi(() => WindowManagerNativePlacementGeometry.Read(subject.MainProbe.Handle));
            var settings = native.OnUi(() => WindowManagerNativePlacementGeometry.Read(subject.SettingsProbe.Handle));
            var primary = Assert.Single(topology.Monitors, monitor => monitor.Primary);
            var ownerMonitor = Assert.Single(topology.Monitors, monitor => monitor.Handle == main.Monitor);
            var expected = WindowManagerNativePlacementGeometry.Expected(main, settings, primary.Bounds, ownerMonitor.Work);

            // Neither a second focus helper nor another Show may manufacture the observed result.
            native.OnUi(() => subject.Manager.ShowSettings());
            subject.SettingsProbe.Fence();
            Assert.Equal(subject.SettingsProbe.Handle, N.GetForegroundWindow());
            Assert.True(native.OnUi(() => subject.Settings.IsActive));
            var after = native.OnUi(() => WindowManagerNativePlacementGeometry.Read(subject.SettingsProbe.Handle));
            Assert.True(after.Visible);
            Assert.Equal(expected.Origin, after.Origin);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // WindowManager.swift:32-55; native placement on the observed topology.
    [Trait("Req", "PR-WIN-011")] // Two-monitor verification remains open when fewer than two physical regions are available.
    public void Settings_reopening_uses_the_actual_main_monitor_workarea_and_native_frame_DPI()
    {
        native.Run(subject => WindowManagerNativePlacementExercise.Run(subject));
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")]
    public void Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe), (subject.Settings, subject.SettingsContent, subject.SettingsProbe) })
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
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")] // WindowManager.swift:93-101; ED-063 applies to both factory windows.
    public void Release_over_a_different_caption_control_cancels_the_original_command()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe), (subject.Settings, subject.SettingsContent, subject.SettingsProbe) })
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
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")] // ED-063 cancellation on main and capture transfer away from Settings.
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

            subject.Focus(subject.Settings, subject.SettingsProbe);
            var settingsPoint = subject.Center(subject.SettingsContent.MinimizeZone);
            pointer.Down(subject.SettingsProbe, settingsPoint, N.HTMINBUTTON);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.SettingsProbe.Handle, "Settings caption did not capture.");
            var captureMessages = subject.SettingsProbe.Count(N.WM_CAPTURECHANGED);
            // Real ownership transfer causes WM_CAPTURECHANGED; no fabricated pointer or capture event.
            native.OnUi(() => { _ = N.SetCapture(subject.MainProbe.Handle); });
            N.Until(() => subject.SettingsProbe.Count(N.WM_CAPTURECHANGED) > captureMessages, "Capture transfer was not delivered.");
            Assert.Equal((nint)0, subject.PendingCaption(subject.Settings));
            var mainReleases = subject.MainProbe.Count(N.WM_LBUTTONUP, returned: true);
            pointer.Up(subject.MainProbe, settingsPoint);
            N.Until(() => subject.MainProbe.Count(N.WM_LBUTTONUP, returned: true) > mainReleases,
                "Transferred native capture did not receive the real release.");
            subject.SettingsProbe.Fence();
            native.OnUi(() => { _ = N.ReleaseCapture(); });
            Assert.Equal(0, subject.SettingsProbe.CommandCount(N.SC_MINIMIZE));
            Assert.False(N.IsIconic(subject.SettingsProbe.Handle));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")]
    public void Settings_maximize_is_rejected_at_caption_acceptance_and_at_system_command_dispatch()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Settings, subject.SettingsProbe);
            Assert.False(native.OnUi(() => subject.Settings.CanMaximize));
            using var pointer = new WindowManagerNativePointer(subject);
            var point = subject.Center(subject.SettingsContent.MaximizeZone);
            var releases = subject.SettingsProbe.InputQueue.ReleaseCount;
            pointer.Down(subject.SettingsProbe, point, N.HTMAXBUTTON);
            pointer.Up(subject.SettingsProbe, point);
            N.Until(() => subject.SettingsProbe.Count(N.WM_NCLBUTTONDOWN, returned: true) > 0, "Native maximize press never returned.");
            N.Until(() => subject.SettingsProbe.InputQueue.ReleaseCount > releases,
                "The native disabled-button tracking loop did not dequeue the physical release.");
            subject.SettingsProbe.Fence();
            // A09: observes acceptance before the separate command guard can mask this mutation.
            var press = Assert.Single(subject.SettingsProbe.Messages, item => item.Message == N.WM_NCLBUTTONDOWN && item.Returned);
            Assert.Equal((nint?)0, press.PendingCaption);
            Assert.False(N.IsZoomed(subject.SettingsProbe.Handle));

            // A10: a native non-caption command bypasses IsCaptionButton entirely.
            var before = subject.SettingsProbe.CommandCount(N.SC_MAXIMIZE);
            N.Post(subject.SettingsProbe.Handle, N.WM_SYSCOMMAND, (nint)N.SC_MAXIMIZE);
            N.Until(() => subject.SettingsProbe.CommandCount(N.SC_MAXIMIZE) > before, "Native SC_MAXIMIZE was not delivered.");
            subject.SettingsProbe.Fence();
            Assert.Equal(WindowState.Normal, native.OnUi(() => subject.Settings.WindowState));
            Assert.False(N.IsZoomed(subject.SettingsProbe.Handle));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")] // ED-063 keyboard system menu belongs to each factory window.
    public void Space_key_menu_is_anchored_to_its_factory_window_instead_of_decoding_the_character_as_coordinates()
    {
        native.Run(subject =>
        {
            foreach (var entry in new[] { (subject.Main, subject.MainProbe), (subject.Settings, subject.SettingsProbe) })
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
    [Trait("Req", "PR-SETTINGS-001")]
    public void A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities()
    {
        native.Run(subject =>
        {
            using var pointer = new WindowManagerNativePointer(subject);
            foreach (var entry in new[] { (subject.Main, subject.MainContent, subject.MainProbe, true), (subject.Settings, subject.SettingsContent, subject.SettingsProbe, false) })
            {
                var (window, content, probe, canResize) = entry;
                subject.Focus(window, probe);
                using var menu = new WindowManagerNativeMenu(native, probe);
                pointer.RightClick(probe, subject.Caption(content), N.HTCAPTION);
                var popup = menu.WaitForMenu();
                Assert.True(probe.Count(N.WM_CONTEXTMENU) > 0, "The native right click did not reach WM_CONTEXTMENU.");
                Assert.Equal(canResize, popup.Enabled(N.SC_SIZE));
                Assert.Equal(canResize, popup.Enabled(N.SC_MAXIMIZE));
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
            Assert.NotSame(native.OnUi(() => AppEnvironment.GetWindowLiveResize(subject.MainContent)),
                native.OnUi(() => AppEnvironment.GetWindowLiveResize(subject.SettingsContent)));
            using var pointer = new WindowManagerNativePointer(subject);
            var before = N.Rect(subject.MainProbe.Handle);
            var edge = WindowManagerNativePointer.RightEdge(subject.MainProbe, resizable: true);
            pointer.Down(subject.MainProbe, edge, N.HTRIGHT);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.MainProbe.Handle, "Native edge drag did not capture.");
            var moved = new NativeWindowPoint(edge.X + 64, edge.Y);
            pointer.Move(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_SIZING) > 0, "The actual drag produced no WM_SIZING.");
            N.Until(() => subject.MainProbe.IsResizing, "The factory-created live-resize hook did not activate during WM_SIZING.");
            Assert.False(subject.SettingsProbe.IsResizing);
            pointer.Up(subject.MainProbe, moved);
            N.Until(() => subject.MainProbe.Count(N.WM_EXITSIZEMOVE) > 0 && !subject.MainProbe.IsResizing,
                "Live resize did not end after actual pointer release.");
            var after = N.Rect(subject.MainProbe.Handle);
            Assert.True(after.Width > before.Width, "The pointer drag did not change the native frame width.");
            Assert.Equal(new[] { true, false }, subject.MainProbe.ResizeTransitions);
            Assert.Empty(subject.SettingsProbe.ResizeTransitions);
            Assert.True(subject.MainProbe.Count(N.WM_ENTERSIZEMOVE) > 0);
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
            Assert.Empty(subject.SettingsProbe.ResizeTransitions);
            Assert.Equal(0, subject.MainProbe.Count(N.WM_SIZING));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")] // WindowManager.swift:81, the fixed secondary refuses a native resize.
    public void Settings_refuses_an_actual_border_resize_attempt()
    {
        native.Run(subject =>
        {
            subject.Focus(subject.Settings, subject.SettingsProbe);
            using var pointer = new WindowManagerNativePointer(subject);
            var before = N.Rect(subject.SettingsProbe.Handle);
            var edge = WindowManagerNativePointer.RightEdge(subject.SettingsProbe, resizable: false);
            var hit = N.Hit(subject.SettingsProbe.Handle, edge);
            pointer.Down(subject.SettingsProbe, edge, hit);
            var moved = new NativeWindowPoint(edge.X - 64, edge.Y);
            pointer.Move(subject.SettingsProbe, moved);
            pointer.Up(subject.SettingsProbe, moved);
            N.Until(() => subject.SettingsProbe.Count(N.WM_LBUTTONUP, returned: true) + subject.SettingsProbe.Count(N.WM_NCLBUTTONUP, returned: true) > 0,
                "The Settings border attempt did not receive actual native pointer release.");
            subject.SettingsProbe.Fence();
            var after = N.Rect(subject.SettingsProbe.Handle);
            Assert.Equal(before.Width, after.Width);
            Assert.Equal(before.Height, after.Height);
            Assert.Equal(0, subject.SettingsProbe.Count(N.WM_SIZING));
            Assert.Empty(subject.SettingsProbe.ResizeTransitions);
            Assert.False(native.OnUi(() => subject.Settings.CanResize));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-007")]
    [Trait("Req", "PR-WIN-008")]
    [Trait("Req", "PR-SETTINGS-001")] // WindowManager.swift:98, the Settings HWND survives actual close/reopen.
    public void Actual_close_clicks_request_main_termination_and_retain_the_hidden_Settings_window()
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

            subject.Focus(subject.Settings, subject.SettingsProbe);
            var settingsPoint = subject.Center(subject.SettingsContent.CloseZone);
            pointer.Down(subject.SettingsProbe, settingsPoint, N.HTCLOSE);
            N.Until(() => N.Gui(native.ThreadId).Capture == subject.SettingsProbe.Handle, "Settings close did not capture.");
            pointer.Up(subject.SettingsProbe, settingsPoint);
            N.Until(() => !N.IsWindowVisible(subject.SettingsProbe.Handle), "Settings close did not hide the retained window.");
            Assert.True(N.IsWindow(subject.SettingsProbe.Handle));
            native.OnUi(() => subject.Manager.ShowSettings());
            Assert.True(N.IsWindowVisible(subject.SettingsProbe.Handle));
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
}
