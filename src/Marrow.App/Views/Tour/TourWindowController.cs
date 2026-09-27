// Source: DroppyCode/App/Views/Tour/TourWindowController.swift (B2 1fbf81e), :1-179.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using AvaloniaWindow = Avalonia.Controls.Window;
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
            Content = rootView,
            Topmost = true,
            ShowInTaskbar = false,
            TransparencyLevelHint = [Avalonia.Controls.WindowTransparencyLevel.Transparent],
        };

        // Set up transparent background
        createdWindow.Background = null;

        // :92-101 Position the window
        var mainWindow = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow
            : null;

        if (mainWindow != null && mainWindow.IsVisible)
        {
            var mainFrame = mainWindow.Bounds;
            var originX = mainFrame.Left + (mainFrame.Width - width) / 2;
            var originY = mainFrame.Top + (mainFrame.Height - totalHeight) / 2;
            createdWindow.Position = new PixelPoint((int)originX, (int)originY);
        }

        // :103-111 Set up delegate and show window
        var del = new WindowDelegate(() =>
        {
            window = null;
            windowDelegate = null;
        });
        windowDelegate = del;
        createdWindow.Closed += (s, e) => del.OnClose();

        createdWindow.Show();
        createdWindow.Activate();

        // :113 Store and return
        window = createdWindow;
        return window;
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
        // Estimate the panel height based on page content
        const double TitleFontSize = 28;
        const double DescriptionFontSize = 13;
        const double ButtonHeight = 42;
        const double HorizontalPadding = 64; // 32 * 2
        const double TopPadding = 12;
        const double BottomPadding = 24;
        const double InternalSpacing = 6;
        const double TextMarginSpacing = 24; // 12 * 2

        var availableWidth = width - HorizontalPadding;
        var titleLines = Math.Ceiling(page.Title.Length / (availableWidth / 8));
        var descriptionLines = Math.Ceiling(page.Description.Length / (availableWidth / 7));
        var titleHeight = titleLines * TitleFontSize * 1.2;
        var descriptionHeight = descriptionLines * DescriptionFontSize * 1.4;

        return TopPadding + TextMarginSpacing + titleHeight + InternalSpacing + descriptionHeight + TextMarginSpacing + BottomPadding + ButtonHeight;
    }

    /// <summary>
    /// close() (:167-171): Closes the window and clears references so present() builds fresh next time.
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
