using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for PR-GIT-137 (the changes popover, DroppyCode/App/Views/Diff/DiffInspector.swift:515-692): the real DiffPopoverCoordinator and its real Popup on a real Win32 window,
// driven by the system's pointer and keyboard input (SendInput, keybd_event), never by RaiseEvent. Semitransient with a pass-through monitor (:515-519): Escape closes it and is swallowed
// (:668-672), a press outside closes it first and still reaches its target, so another turn's Review button opens the diff in a single tap (:662-666). After a reopen onto another anchor the
// popover was in the state of upstream defect 67, which the port reproduced (PORT_CONTRACT section 8, ED-196, :640-658): its delayed close stopped the new show's monitors, so Escape and an outside
// press no longer dismissed it. Since T45 (ED-NEW-T45 item 11: the owner's rule that every popover closes on Escape and on an outside press) the reopened popover keeps its monitors; this test proves on
// a real window that Escape closes it and is swallowed, that an outside tap closes it, that its flag still closes it, and that a fresh show has its monitors. The 640 x 540 content size is asserted in points; the display scale of the PC it runs on is
// recorded (a second scale, 150 %, cannot be set without changing a system setting). Needs a free interactive desktop; the lead takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class DiffPopoverNativeTests(WindowManagerNativeSession native)
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    private static void SendEscape()
    {
        keybd_event(0x1B, 0, 0, 0);
        keybd_event(0x1B, 0, 2, 0); // KEYEVENTF_KEYUP
    }

    private static void ClickAt(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(50);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
    }

    [Fact]
    [Trait("Req", "PR-GIT-137")] // DiffInspector.swift:515-519, :599-612, :640-658, :662-686; upstream defect 67 no longer reproduced (ED-NEW-T45 item 11); native, SendInput
    public void On_a_real_window_escape_is_swallowed_an_outside_tap_on_another_review_button_reopens_there_and_the_reopened_popover_still_closes_by_its_flag() => native.Run(_ =>
    {
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            var clicks = 0;
            var escapesReaching = 0;
            var ui = native.OnUi(() =>
            {
                var window = new AvaloniaWindow { Width = 900, Height = 800, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(40, 40) };
                native.Register(window);
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window)); // the frame clock the app binds at start-up (the popover grows on it)
                window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
                var tab = new Border { Width = 120, Height = 28, Background = Brushes.Gray };
                Button Review(string name) => new()
                {
                    Content = name, Width = 100, Height = 30,
                    Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Brushes.DimGray }),
                };
                var first = Review("Review A");
                var second = Review("Review B");
                var canvas = new Canvas { Width = 900, Height = 800 };
                Canvas.SetLeft(tab, 40); Canvas.SetTop(tab, 700);
                Canvas.SetLeft(first, 760); Canvas.SetTop(first, 640);
                Canvas.SetLeft(second, 760); Canvas.SetTop(second, 700);
                canvas.Children.Add(tab); canvas.Children.Add(first); canvas.Children.Add(second);
                window.Content = canvas;
                window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) escapesReaching++; }, Avalonia.Interactivity.RoutingStrategies.Bubble);

                var model = new Marrow.App.AppModel();
                var id = Guid.NewGuid();
                DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
                var runtime = new ThreadRuntime(id, model);
                var coordinator = new DiffPopoverCoordinator();
                coordinator.SetAnchor(tab);
                coordinator.Sync(false, runtime);
                second.Click += (_, _) => { clicks++; runtime.ShowDiff(second, Guid.NewGuid()); coordinator.Reopen(runtime); };
                window.Show();
                window.Activate();
                var popup = (Popup)typeof(DiffPopoverCoordinator).GetField("popover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(coordinator)!;
                return (window, tab, second, runtime, coordinator, popup);
            });
            var (window, tab, second, runtime, coordinator, popup) = ui;
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(500);
            var scale = native.OnUi(() => window.RenderScaling);
            Console.WriteLine($"DiffPopoverNativeTests: display scale {scale:0.00}");

            PixelPoint CenterOf(Control control) => native.OnUi(() => control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2)));
            void Show() => native.OnUi(() => { runtime.IsDiffVisible = true; coordinator.Sync(true, runtime); });
            bool Open() => native.OnUi(() => popup.IsOpen);
            void Escape()
            {
                native.OnUi(() => window.Activate());
                N.Until(() => native.OnUi(() => window.IsActive), "the test window is not the active window");
                Thread.Sleep(150);
                SendEscape();
            }

            // 1. A fresh show: Escape closes it and is swallowed.
            Show();
            N.Until(Open, "the changes popover never opened");
            Assert.Equal(640, native.OnUi(() => popup.Width)); // :522-523, in points
            Assert.Equal(540, native.OnUi(() => popup.Height));
            Assert.Equal(0, escapesReaching);
            Escape();
            N.Until(() => !Open(), "Escape did not close the popover");
            Assert.False(native.OnUi(() => runtime.IsDiffVisible));
            Assert.Equal(0, escapesReaching); // swallowed (:668-672 returns nil)
            Escape(); // control: with it closed the monitor is gone and Escape passes through
            N.Until(() => escapesReaching == 1, "Escape did not reach the window with the popover closed");

            // 2. A fresh show again; a tap outside, on another turn's Review button, closes it first (the press monitor) and still reaches the button, which opens the diff there: one tap.
            Show();
            N.Until(Open, "the changes popover never opened again");
            ClickAt(CenterOf(second));
            N.Until(() => clicks == 1, "the tap did not reach the other Review button");
            N.Until(() => native.OnUi(() => popup.IsOpen && ReferenceEquals(popup.PlacementTarget, second)), "the popover did not reopen on the other Review button");
            Assert.True(native.OnUi(() => runtime.IsDiffVisible));
            Thread.Sleep(600);
            Escape(); // the reopened popover is a fresh show with its own monitors, so Escape closes it again
            N.Until(() => !Open(), "Escape did not close the popover reopened by the tap");

            // 3. ED-NEW-native-fix (ED-NEW-T45 item 11): upstream defect 67 is no longer reproduced on a real window. A reopen onto another anchor while the popover is shown (no press in between, e.g. the
            // changes opened from the keyboard or by a tool row's bump) used to leave the delayed close to stop the new show's monitors, so Escape and an outside tap left it open. The owner's T45 rule is that every
            // popover closes on Escape and on an outside press, so the reopened popover must keep its monitors: Escape closes it and is swallowed, an outside tap closes it, and its flag still closes it.
            void ReopenWhileShown()
            {
                native.OnUi(() => { runtime.ShowDiff(tab, null); coordinator.Sync(true, runtime); }); // the diff opens on the changes tab
                N.Until(() => native.OnUi(() => popup.IsOpen && ReferenceEquals(popup.PlacementTarget, tab)), "the changes popover never opened on the tab");
                native.OnUi(() => { runtime.ShowDiff(second, Guid.NewGuid()); coordinator.Reopen(runtime); });
                N.Until(() => native.OnUi(() => popup.IsOpen && ReferenceEquals(popup.PlacementTarget, second)), "the popover did not move to the other anchor");
                Thread.Sleep(600); // the delayed close has run
            }
            string State() => native.OnUi(() => string.Join(", ", new[] { "keyMonitor", "pointerMonitor", "desiredVisible", "isShown", "shownRuntime", "session", "pendingSession", "reopening", "notifiedCloseSession" }
                .Select(name => name + "=" + (typeof(DiffPopoverCoordinator).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(coordinator) is { } value ? (value is bool or long ? value.ToString() : "set") : "null"))));
            ReopenWhileShown();
            var before = State();
            Assert.Contains("keyMonitor=set", before, StringComparison.Ordinal);     // the delayed close left the new show's monitors in place
            Assert.Contains("pointerMonitor=set", before, StringComparison.Ordinal);
            var escapesBefore = escapesReaching;
            Escape();
            N.Until(() => !Open(), "Escape did not close the reopened popover; state before: " + before + "; after: " + State());
            Assert.False(native.OnUi(() => runtime.IsDiffVisible));
            Assert.Equal(escapesBefore, escapesReaching); // swallowed, as on a fresh show

            ReopenWhileShown();
            ClickAt(CenterOf(tab));
            N.Until(() => !Open(), "an outside tap did not close the reopened popover; state: " + State());
            Assert.False(native.OnUi(() => runtime.IsDiffVisible));

            // ... and its flag (the X button, the tab and the shortcut) still closes it, the popover following it.
            ReopenWhileShown();
            native.OnUi(() => { runtime.IsDiffVisible = false; coordinator.Sync(false, runtime); });
            N.Until(() => !Open(), "the reopened popover did not close by its flag");

            // 4. Shown afresh it has its monitors again: Escape closes it.
            Show();
            N.Until(Open, "the changes popover never opened a fourth time");
            Escape();
            N.Until(() => !Open(), "a fresh show did not get its Escape monitor back");
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, nameof(On_a_real_window_escape_is_swallowed_an_outside_tap_on_another_review_button_reopens_there_and_the_reopened_popover_still_closes_by_its_flag), typeof(DiffPopoverNativeTests));
}
