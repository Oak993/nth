// Source: DroppyCode/App/Views/Tour/TourWindowController.swift (B3 52bc792), :1-186.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaWindow = Avalonia.Controls.Window;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Tour;

/// <summary>
/// TourWindowController (:4-179): Singleton that manages a single tour window.
/// If present() is called while a tour is open, it refocuses the existing window and ignores
/// the new arguments. The window is sized by measuring all pages before opening, so it does
/// not grow or shrink between pages. It is borderless, transparent, floating, centered on
/// the main window or screen, and can become key and main to allow Return shortcut.
/// </summary>
public sealed class TourWindowController
{
    public static TourWindowController Shared { get; } = new();

    private AvaloniaWindow? window;
    private WindowDelegate? windowDelegate;
    internal Action<PointerPressedEventArgs>? MoveDragForTesting { get; set; }
    internal AvaloniaWindow? MainWindowForTesting { get; set; }

    /// <summary>
    /// HostWindow (:8-11): Custom NSWindow that allows becoming key and main.
    /// </summary>
    private sealed class HostWindow : AvaloniaWindow
    {
        // :8-11 Override to allow becoming key and main
    }

    /// <summary>
    /// WindowDelegate (:173-177): Holds cleanup callback for when window closes.
    /// </summary>
    private sealed class WindowDelegate
    {
        private readonly Action onClose;

        public WindowDelegate(Action onClose)
        {
            this.onClose = onClose;
        }

        public void OnClose()
        {
            onClose();
        }
    }

    private TourWindowController()
    {
    }

    /// <summary>
    /// present() (:18-143): Creates and shows a tour window, or refocuses if one is already open.
    /// </summary>
    public AvaloniaWindow Present(
        IReadOnlyList<TourPage> pages,
        double width = 660,
        string continueButtonTitle = "Continue",
        string finishButtonTitle = "Done",
        Action? onFinish = null,
        Action? onClose = null
    )
    {
        // :27-31 if let existing = window { existing.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); return existing }
        if (window != null)
        {
            window.Activate();
            return window;
        }

        // :33-35 let dismiss: () -> Void = { [weak self] in self?.close() }
        void Dismiss()
        {
            Close();
        }

        // :37-46 Calculate window size by measuring all pages
        var imageHeight = SwiftNumber.Rounded(width / TourView.ImageAspectRatio);
        var maxPanelHeight = MaxBottomPanelHeight(pages, width, continueButtonTitle, finishButtonTitle);
        var totalHeight = pages.Count == 0
            ? TourView.EmptyStateHeight
            : Math.Max(imageHeight + maxPanelHeight, 1);

        // :48-66 Create the tour view with callbacks
        var rootView = new TourView(
            pages,
            width,
            totalHeight,
            initialPageIndex: 0,
            continueButtonTitle,
            finishButtonTitle,
            onFinish: () =>
            {
                if (onFinish != null)
                {
                    onFinish();
                }
                else
                {
                    onClose?.Invoke();
                }
                Dismiss();
            },
            onClose: () =>
            {
                onClose?.Invoke();
                Dismiss();
            }
        );

        // :68-90 Create the window
        var createdWindow = new HostWindow
        {
            Width = width,
            Height = totalHeight,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            CanResize = false, // :75-80 the borderless style mask has no .resizable: no resizing or maximizing by the user
            Content = rootView,
            Topmost = true,
            ShowInTaskbar = false,
            // ED-236 (OD-0005, OD-0014): the card is glass (TourView.swift:79-86 `.glassEffect(.regular, in: shape)`) over
            // a clear window (TourWindowController.swift:82-83), so the window composites Acrylic under the card's
            // app-drawn tint and scrim, as WindowChrome.Configure does for the main window.
            TransparencyLevelHint = Marrow.UI.Chrome.WindowChrome.GlassBackdrop,
        };

        // Set up transparent background
        createdWindow.Background = null;

        // :84-86 Windows floating-panel adaptation: shadow on the unclipped rounded scrim,
        // using the port's existing borderless-panel recipe; background presses enter the system move loop.
        if (rootView.Children[0] is Border scrim)
            scrim.BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb(166, 0, 0, 0), Blur = 28, OffsetY = 10 });
        rootView.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(rootView).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            if (MoveDragForTesting is { } move) move(e);
            else createdWindow.BeginMoveDrag(e);
            e.Handled = true;
        };

        // :92-101 Position the window
        var mainWindow = MainWindowForTesting ?? (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow
            : null);

        // ED-NEW-textsize-1: the tour card is drawn at the app zoom of the main window, as every other surface of the app.
        var zoom = Marrow.UI.Common.AppZoom.ScaleOfWindow(mainWindow);
        // ED-NEW-perf-motion-12: the card leaves the window's content before it goes into its zoom layout (a control has one logical
        // parent): put in the layout while still the window's content, it lost that parent when the layout replaced it, and showing
        // the window threw ("AttachedToLogicalTreeCore called for 'TourView' but control has no logical parent"; WindowZoomFollowTests, at 100 %).
        createdWindow.Content = null;
        var zoomed = Marrow.UI.Common.AppZoom.Zoomed(rootView, zoom);
        createdWindow.Content = zoomed;
        if (!ReferenceEquals(zoomed, rootView))
        {
            createdWindow.Width = width * zoom;
            createdWindow.Height = totalHeight * zoom;
        }
        else zoom = 1;

        if (mainWindow != null && mainWindow.IsVisible) Center(createdWindow, mainWindow, width * zoom, totalHeight * zoom);
        else createdWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // ED-NEW-perf-motion-12: a zoom changed while the tour shows (its window answers the app's key equivalents, Ctrl+Plus among them)
        // reaches the card on every frame of the main window's glide, the window's size and its centre on the main window with it.
        _ = Marrow.UI.Common.WindowZoom.Follow(createdWindow, rootView, mainWindow, next =>
        {
            createdWindow.Width = width * next;
            createdWindow.Height = totalHeight * next;
            if (mainWindow != null && mainWindow.IsVisible) Center(createdWindow, mainWindow, width * next, totalHeight * next);
        });

        // :103-111 Set up delegate and show window
        var del = new WindowDelegate(() =>
        {
            window = null;
            windowDelegate = null;
            SwiftImage.ReleaseTourPictures(); // PORT-ONLY (ED-NEW-perf-memory-6): the decoded pictures are not kept for the life of the process
        });
        windowDelegate = del;
        createdWindow.Closed += (s, e) => del.OnClose();

        // PORT-ONLY (OD-0008 C3): this window can become key (:8-11, :110), and while it is key the menu bar of
        // DroppyCodeApp.swift:21-25, `AppCommands(model: WindowManager.shared.model)`, answers its key equivalents. With no
        // menu bar the window carries the port's dispatch of those chords (Port/MenuKeyEquivalents.cs), the command table
        // read afresh at each key-down, as WindowManager.MakeWindow does for the main and Settings windows.
        _ = MenuKeyEquivalents.Attach(createdWindow, (positional, typed) => new AppCommands(WindowManager.Shared.Model).PerformKeyDown(positional, typed));

        createdWindow.Show();
        createdWindow.Activate();

        // :113 Store and return
        window = createdWindow;
        return window;
    }

    // :92-101 the tour window centred on the main window, at its drawn size.
    private static void Center(AvaloniaWindow window, AvaloniaWindow mainWindow, double drawnWidth, double drawnHeight)
    {
        var mainFrame = mainWindow.FrameSize ?? mainWindow.Bounds.Size;
        var scale = mainWindow.RenderScaling;
        var originX = mainWindow.Position.X + (mainFrame.Width - drawnWidth) * scale / 2;
        var originY = mainWindow.Position.Y + (mainFrame.Height - drawnHeight) * scale / 2;
        window.Position = new PixelPoint((int)Math.Round(originX), (int)Math.Round(originY));
    }

    /// <summary>
    /// maxBottomPanelHeight() (:145-165): Measures the bottom panel across all pages
    /// to find the maximum height, so the window doesn't resize when paging.
    /// </summary>
    private static double MaxBottomPanelHeight(
        IReadOnlyList<TourPage> pages,
        double width,
        string continueButtonTitle,
        string finishButtonTitle
    )
    {
        if (pages.Count == 0)
        {
            return 0;
        }

        double maxHeight = 0;
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var isLast = (i == pages.Count - 1);
            var buttonTitle = isLast ? finishButtonTitle : continueButtonTitle;
            var panelHeight = MeasurePanelHeight(page, buttonTitle, width);
            if (panelHeight > maxHeight)
            {
                maxHeight = panelHeight;
            }
        }
        return maxHeight;
    }

    /// <summary>
    /// Measures the height of a single tour bottom panel.
    /// </summary>
    private static double MeasurePanelHeight(TourPage page, string buttonTitle, double width)
    {
        var panel = new TourBottomPanelSizingView(page, buttonTitle);
        panel.Measure(new Size(width, double.PositiveInfinity));
        return panel.DesiredSize.Height;
    }

    /// <summary>
    /// close() (:139-143): Closes the window and clears references so present() builds fresh next time.
    /// </summary>
    public void Close()
    {
        if (window != null)
        {
            window.Close();
        }
        window = null;
        windowDelegate = null;
    }
}

/// <summary>:152-186. The literal off-screen sizing tree, measured at each requested window width.</summary>
internal sealed class TourBottomPanelSizingView : Border
{
    public TourBottomPanelSizingView(TourPage page, string buttonTitle)
    {
        // :152-186 ED-052: measure the real sizing view, including only the source's
        // 220x42 text frame (no native button chrome) and the two 12 DIP spacer floors.
        var title = new TextBlock { Text = page.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        SwiftFont.System(28, SwiftFont.Weight.Bold).Apply(title);
        var description = new TextBlock { Text = page.Description, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        SwiftFont.Body.Apply(description);
        var label = new TextBlock { Text = buttonTitle, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        SwiftFont.Title3.FontWeight(SwiftFont.Weight.Semibold).Apply(label);
        var stack = new StackPanel
        {
            Spacing = 0,
            Children = { new Border { Height = 12 }, title, description, new Border { Height = 12 }, new Border { Width = 220, Height = 42, Child = label } },
        };
        Padding = new Thickness(32, 12, 32, 24);
        Child = stack;
    }
}
