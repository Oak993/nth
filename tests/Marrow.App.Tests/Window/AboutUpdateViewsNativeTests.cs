using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-UPDATE-015 (DroppyCode/App/Views/Settings/AboutUpdateViews.swift:241-251, :255-301): the real release-section cards
// (UpdateReleaseSectionCards) and their real Popup, a PopupRoot window opened by the port's own presenter, on a real window, driven by the
// system's pointer (SetCursorPos, mouse_event). The popover opens above its card with its content already there on the first open, a wheel over
// the 40 bullets moves them inside the 320 point cap, a press outside closes it and clears the presented card, and a press on the open popover's own
// card leaves it closed (the card's own tap does not open it again). The headless counterpart on the overlay host is
// AboutUpdateViewsRealPopupTests. Needs a free interactive desktop.
[Collection("WindowManagerNative")]
public sealed class AboutUpdateViewsNativeTests(WindowManagerNativeSession native)
{
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, nuint extra);

    private static void MoveTo(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(120);
    }

    private static void ClickAt(PixelPoint point)
    {
        MoveTo(point);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
    }

    private static void Wheel(PixelPoint point, int notches)
    {
        MoveTo(point);
        mouse_event(0x0800, 0, 0, notches * 120, 0); // MOUSEEVENTF_WHEEL: negative turns toward the user, which scrolls the content up
    }

    private static ReleaseNoteCategory? Presented(UpdateReleaseSectionCards cards)
    {
        var state = typeof(UpdateReleaseSectionCards).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cards)!;
        return (ReleaseNoteCategory?)state.GetType().GetProperty("Presented")!.GetValue(state);
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-015")] // AboutUpdateViews.swift:241-251, :255-301 on a real PopupRoot
    public void On_a_real_window_a_release_card_opens_its_popover_above_itself_with_its_content_scrolls_it_and_a_press_outside_or_on_the_card_closes_it() => native.Run(_ =>
    {
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            var recorder = native.OnUi(() => new NativePopupRecorder(Presentation.Presenter.Current));
            var ui = native.OnUi(() =>
            {
                Presentation.Presenter.SetProcessDefault(recorder); // the app's own presenter, remembered
                var window = new AvaloniaWindow { Width = 1000, Height = 800, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(60, 60) };
                native.Register(window);
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window));
                window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new VisualLayerManager { Child = new ContentPresenter { Content = owner.Content } }); // the layer a light-dismiss popup registers with
                var sections = ReleaseNoteCategoryExtensions.AllCases.Select((category, index) => new UpdateReleaseNotesDigest.Section
                {
                    Category = category,
                    Bullets = new ValueList<string>(Enumerable.Range(1, index == 0 ? 40 : 2).Select(number => $"Change number {number}, long enough to need more than one line in a popover that is 320 points wide.")),
                    RollupCount = 0,
                }).ToArray();
                var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) }, "9.0.0");
                var holder = new Border { Margin = new Thickness(60, 450, 60, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = cards, Background = Brushes.Transparent };
                window.Content = new Border { Background = Brushes.Transparent, Child = holder };
                window.Show();
                window.Activate();
                return (window, cards);
            });
            var (window, cards) = ui;
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(800);
            var card = native.OnUi(() => cards.GetVisualDescendants().OfType<Button>().First());
            PixelPoint CenterOf(Control control) => native.OnUi(() => control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2)));
            bool Open() => native.OnUi(() => recorder.Popups.Count == 1 && recorder.Popups[0].IsOpen);
            nint HostHandle() => native.OnUi(() => typeof(Popup).GetProperty("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recorder.Popups[0]) is TopLevel host ? host.TryGetPlatformHandle()?.Handle ?? 0 : 0);
            void Front()
            {
                native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId));
                N.Until(() => native.OnUi(() => window.IsActive) && N.GetForegroundWindow() == native.OnUi(() => window.TryGetPlatformHandle()!.Handle), "the test window is not the foreground window");
                Thread.Sleep(300);
            }

            // 1. Open: a popup window above the card, its content there on the first open.
            Front();
            ClickAt(CenterOf(card));
            N.Until(Open, "the release popover never opened");
            N.Until(() => HostHandle() != 0 && N.IsWindowVisible(HostHandle()), "the popover has no visible window");
            Thread.Sleep(1500); // it grows in from its arrow
            Assert.Equal(ReleaseNoteCategory.NewFeatures, native.OnUi(() => Presented(cards)));
            var popup = native.OnUi(() => recorder.Popups[0]);
            var texts = native.OnUi(() => popup.Child!.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray());
            Assert.Contains("New features", texts);
            Assert.Contains("9.0.0", texts);
            Assert.Contains(texts, text => text is not null && text.StartsWith("Change number 1,", StringComparison.Ordinal));
            var rectangle = N.Rect(HostHandle());
            var cardTop = native.OnUi(() => card.PointToScreen(new Point(0, 0)));
            var cardCentre = CenterOf(card);
            Assert.InRange(rectangle.Bottom, cardTop.Y - 2, cardTop.Y + 2); // :244 arrowEdge .top: the popover lies above the card
            Assert.InRange(rectangle.Left + (rectangle.Right - rectangle.Left) / 2, cardCentre.X - 2, cardCentre.X + 2);

            // 2. Scroll: a wheel over the 40 bullets moves them within the 320 point cap.
            var scroll = native.OnUi(() => popup.Child!.GetVisualDescendants().OfType<ScrollViewer>().Single());
            Assert.Equal(320, native.OnUi(() => scroll.Viewport.Height));
            Assert.True(native.OnUi(() => scroll.Extent.Height) > 320);
            Wheel(CenterOf(scroll), -3);
            N.Until(() => native.OnUi(() => scroll.Offset.Y) > 0, "the wheel did not scroll the release notes");

            // 3. A press outside closes it and clears the presented card.
            var outside = native.OnUi(() => window.PointToScreen(new Point(30, 30)));
            ClickAt(outside);
            N.Until(() => !Open(), "a press outside did not close the release popover");
            Thread.Sleep(300);
            Assert.Null(native.OnUi(() => Presented(cards)));

            // 4. The next tap opens it again; a tap on the open popover's own card closes it and does not reopen it.
            ClickAt(CenterOf(card));
            N.Until(Open, "the release popover did not open again");
            Thread.Sleep(1500);
            Assert.Equal(ReleaseNoteCategory.NewFeatures, native.OnUi(() => Presented(cards)));
            ClickAt(CenterOf(card));
            N.Until(() => !Open(), "a tap on the open popover's card did not close it");
            Thread.Sleep(1200); // a reopening toggle would land now
            Assert.False(Open(), "the card's own tap opened the popover again after the press had closed it");
            Assert.Null(native.OnUi(() => Presented(cards)));

            // and the tap after that opens it.
            ClickAt(CenterOf(card));
            N.Until(Open, "the next tap on the card did not open the release popover");
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(AboutUpdateViewsNativeTests));
}
