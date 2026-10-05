using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-THEME-042 (DroppyCode/UI/Markdown/LinkPopover.swift:9-66, :68-108): the real LinkPopover.Shared and its real Popup (a PopupRoot
// HWND) on a real window, driven by the system's pointer and keyboard input (SetCursorPos and mouse_event, keybd_event), never by RaiseEvent. A link's popover is
// .transient (:20-21): a press outside it closes it and popoverDidClose resyncs (:48-50); a second show on an open popover closes the first and opens below the
// second link (:28, :39); Escape closes it and is swallowed (:53-60); the content is 220 wide (:97) and as high as it measures (:35-37). The window, the two
// "links" and their rects are the test's; the popover, its placement, its input handling and its window are production. Needs a free interactive desktop.
[Collection("WindowManagerNative")]
public sealed class LinkPopoverNativeTests(WindowManagerNativeSession native)
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeWindowRect rectangle);

    private static void SendEscape()
    {
        keybd_event(0x1B, 0, 0, 0);
        keybd_event(0x1B, 0, 2, 0); // KEYEVENTF_KEYUP
    }

    private static void ClickAt(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(80);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
    }

    // The view LinkPopover hosts (LinkPopoverContent), inside whatever the popup wraps it in.
    private static Control Content(Popup popup) => popup.Child!.GetVisualDescendants().OfType<Control>().First(visual => visual.GetType().Name == "LinkPopoverContent");

    private static async Task<string?> ReadText(IClipboard clipboard)
    {
        using var data = await clipboard.TryGetDataAsync();
        return data is null ? null : await data.TryGetTextAsync();
    }

    [Fact]
    [Trait("Req", "UI-THEME-042")] // LinkPopover.swift:20-21, :28, :39, :48-50, :53-60, :97 on a real PopupRoot
    public void On_a_real_window_an_outside_click_closes_the_popover_a_second_show_moves_it_below_the_new_link_and_Escape_is_swallowed() => native.Run(_ =>
    {
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        string? clipboardBefore = null;
        IClipboard? clipboard = null;
        try
        {
            var escapesReaching = 0;
            var ui = native.OnUi(() =>
            {
                var window = new AvaloniaWindow { Width = 1000, Height = 700, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(60, 60) };
                native.Register(window);
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window));
                window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new VisualLayerManager { Child = new ContentPresenter { Content = owner.Content } }); // the layer manager holds the layer a light-dismiss popup registers with
                var first = new Border { Width = 120, Height = 20, Background = Brushes.SteelBlue };
                var second = new Border { Width = 160, Height = 20, Background = Brushes.IndianRed };
                var canvas = new Canvas { Width = 1000, Height = 700, Background = Brushes.Transparent };
                Canvas.SetLeft(first, 100); Canvas.SetTop(first, 100);
                Canvas.SetLeft(second, 560); Canvas.SetTop(second, 300);
                canvas.Children.Add(first); canvas.Children.Add(second);
                window.Content = canvas;
                window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) escapesReaching++; }, Avalonia.Interactivity.RoutingStrategies.Bubble);
                window.Show();
                window.Activate();
                clipboard = window.Clipboard;
                var popup = (Popup)typeof(LinkPopover).GetField("popover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(LinkPopover.Shared)!;
                return (window, first, second, popup);
            });
            var (window, first, second, popup) = ui;
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(500);
            var scale = native.OnUi(() => window.RenderScaling);
            Console.WriteLine($"LinkPopoverNativeTests: display scale {scale:0.00}");
            clipboardBefore = clipboard is null ? null : native.OnUi(() => ReadText(clipboard)).GetAwaiter().GetResult();

            var firstUrl = SwiftURL.Create("https://example.com/first")!;
            var secondUrl = SwiftURL.Create("https://example.org/second")!;
            bool Open() => native.OnUi(() => popup.IsOpen);
            nint HostHandle() => native.OnUi(() => typeof(Popup).GetProperty("Host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(popup) is TopLevel host ? host.TryGetPlatformHandle()?.Handle ?? 0 : 0);
            void Show(SwiftURL url, Control link) => native.OnUi(() => LinkPopover.Shared.Show(url, null, new Rect(0, 0, link.Bounds.Width, link.Bounds.Height), link));
            PixelPoint ScreenOf(Control control, double x, double y) => native.OnUi(() => control.PointToScreen(new Point(x, y)));
            void Front()
            {
                native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId));
                N.Until(() => native.OnUi(() => window.IsActive) && N.GetForegroundWindow() == native.OnUi(() => window.TryGetPlatformHandle()!.Handle), "the test window is not the foreground window");
                Thread.Sleep(300);
            }

            // 1. Shown under the first link: a PopupRoot window, 220 points wide, as high as the content measures, its top at the link's bottom edge.
            Show(firstUrl, first);
            N.Until(Open, "the link popover never opened");
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the popover has no visible window");
            var firstHandle = HostHandle();
            Thread.Sleep(1500); // the popover rises into place (animates = true, :21)
            var low = ScreenOf(first, first.Bounds.Width / 2, first.Bounds.Height);
            var rectangle = N.Rect(firstHandle);
            N.Check(GetClientRect(firstHandle, out var client), "GetClientRect");
            // The popup window carries the port's popover margin (the drop shadow) around the content; the content is what the source sizes (:97 220 wide,
            // :35-37 as high as the hosted view measures: the frame SetFixedContent gives it), the window is what is placed, centred under the words.
            var (contentSize, contentAt, frameHeight) = native.OnUi(() =>
            {
                var shown = Content(popup);
                return (shown.Bounds.Size, shown.PointToScreen(new Point(0, 0)), ((Border)shown.Parent!).Height);
            });
            Assert.Equal(220, contentSize.Width);
            Assert.True(contentSize.Height > 12, $"content height {contentSize.Height}");
            Assert.Equal(frameHeight, contentSize.Height);
            var contentPixels = (int)Math.Round(contentSize.Width * scale);
            var side = contentAt.X - rectangle.Left;
            Assert.InRange(side, 1, 40);
            Assert.InRange(client.Right - client.Left, contentPixels + 2 * side - 1, contentPixels + 2 * side + 1); // the same margin on both sides
            Assert.InRange(client.Bottom - client.Top, (int)Math.Round(contentSize.Height * scale) + 2 * side - 1, (int)Math.Round(contentSize.Height * scale) + 2 * side + 3); // and below (the top also holds the arrow)
            Assert.InRange(rectangle.Top, low.Y - 1, low.Y + 1); // :39 preferredEdge .minY: below the words
            Assert.InRange(rectangle.Left + (rectangle.Right - rectangle.Left) / 2, low.X - 1, low.X + 1); // the arrow points at the words

            // 1b. A fresh show: Escape closes it and is swallowed (:53-60).
            Front();
            SendEscape();
            N.Until(() => !Open(), "Escape did not close the freshly shown link popover");
            Thread.Sleep(300);
            Assert.Equal(0, escapesReaching);

            // 2. Shown again while open, on the second link (:28): it moves below the second link and is still an open popover that Escape closes and swallows.
            Show(firstUrl, first);
            N.Until(Open, "the popover did not open again");
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the reopened popover has no visible window");
            var firstHandle2 = HostHandle();
            Thread.Sleep(1200);
            Show(secondUrl, second);
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the moved popover has no visible window");
            Thread.Sleep(1500);
            var secondHandle = HostHandle();
            var lowSecond = ScreenOf(second, second.Bounds.Width / 2, second.Bounds.Height);
            var moved = N.Rect(secondHandle);
            Assert.InRange(moved.Top, lowSecond.Y - 1, lowSecond.Y + 1);
            Assert.InRange(moved.Left + (moved.Right - moved.Left) / 2, lowSecond.X - 1, lowSecond.X + 1);
            if (secondHandle != firstHandle2) Assert.False(N.IsWindowVisible(firstHandle2), "the first popover window is still showing");
            Assert.True(Open(), "the popover is not open after the second show");
            Front();
            SendEscape();
            N.Until(() => !Open(), "Escape did not close the link popover after a second show");
            Thread.Sleep(300);
            Assert.False(N.IsWindowVisible(secondHandle), "the popover window is still showing after Escape");
            Assert.True(escapesReaching == 0, $"Escape reached the window after a second show moved the popover ({escapesReaching})"); // :58 return nil

            // 2b. Its rows act on the second link: Copy Link copies the second URL and, like every row of a house popover, closes the popover.
            Show(secondUrl, second);
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the popover has no visible window");
            Thread.Sleep(1500);
            var copyRow = native.OnUi(() => Content(popup).GetVisualDescendants().OfType<PopoverItem>().ToArray()[1]); // Open Link, Copy Link (no merge row)
            var copyAt = native.OnUi(() => copyRow.PointToScreen(new Point(copyRow.Bounds.Width / 2, copyRow.Bounds.Height / 2)));
            ClickAt(copyAt);
            string? copied = null;
            N.Until(() => (copied = native.OnUi(() => ReadText(clipboard!)).GetAwaiter().GetResult()) == secondUrl.AbsoluteString, $"Copy Link did not copy the second link's URL (clipboard '{copied}', row at {copyAt})");
            N.Until(() => !Open(), "Copy Link did not close the link popover");

            // 3. With it closed, Escape reaches the window.
            Front();
            SendEscape();
            N.Until(() => escapesReaching == 1, "Escape did not reach the window with the popover closed");

            // 4. An outside press closes it (transient, :20-21) and popoverDidClose resyncs (:48-50): the popup is gone and a later Escape is not swallowed.
            Show(firstUrl, first);
            N.Until(Open, "the popover did not open again");
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the reopened popover has no visible window");
            var reopened = HostHandle();
            Thread.Sleep(1200);
            Front();
            var empty = ScreenOf(second, 40, 10);
            ClickAt(new PixelPoint(empty.X, empty.Y + 220)); // empty window area, away from the popover
            N.Until(() => !Open(), "an outside click did not close the link popover");
            Thread.Sleep(200);
            Assert.False(N.IsWindowVisible(reopened), "the popover window is still showing after an outside click");
            Front();
            SendEscape();
            N.Until(() => escapesReaching == 2, "Escape was swallowed after the popover had been closed by an outside click");
        }
        finally
        {
            if (clipboard is not null)
            {
                try { native.OnUi(() => clipboardBefore is null ? clipboard.ClearAsync() : clipboard.SetTextAsync(clipboardBefore)).GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(LinkPopoverNativeTests));
}
