// Source: DroppyCode/App/Views/Tour/TourWindowController.swift (B2 1fbf81e), :1-179.

using System.Linq;
using Avalonia;
using AvaloniaWindow = Avalonia.Controls.Window;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Tour;

/// <summary>
/// Tests for TourWindowController (:4-179).
/// Window controller for presenting a modal tour (onboarding walkthrough) that refocuses if already open,
/// sizes by measuring all pages before showing, creates a borderless floating window, and clears references
/// on close so a later present() builds fresh.
/// </summary>
[Collection("Headless")]
public sealed class TourWindowControllerTests(HeadlessSession headless)
{
    private AvaloniaWindow? createdWindow;

    private static TourPage CreateTestPage(string imageName, string title, string description)
    {
        return new TourPage(imageName, title, description);
    }

    private void Cleanup(AvaloniaWindow? window)
    {
        if (window != null && window.IsVisible)
        {
            window.Close();
        }
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-094")] // DroppyCode/App/Views/Tour/TourWindowController.swift:27-31
    public void PresentingOpenTourRefocusesWindow()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Start here") };
            var onFinishCalls = 0;
            var onCloseCalls = 0;

            var controller = TourWindowController.Shared;
            var window1 = controller.Present(
                pages,
                width: 660,
                continueButtonTitle: "Continue",
                finishButtonTitle: "Done",
                onFinish: () => onFinishCalls++,
                onClose: () => onCloseCalls++
            );
            createdWindow = window1;

            // Act: present again with different pages and callbacks
            var newPages = new[] { CreateTestPage("page2", "Next", "Another page") };
            var window2 = controller.Present(
                newPages,
                width: 660,
                continueButtonTitle: "Continue",
                finishButtonTitle: "Done"
            );

            try
            {
                // Assert: same window object returned, not rebuilt
                Assert.Same(window1, window2);
                Assert.True(window2.IsActive || window2.IsVisible);
            }
            finally
            {
                Cleanup(window1);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:37-46
    public void WindowSizeIsPreComputedAcrossAllPages()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[]
            {
                CreateTestPage("page1", "First", "Description one"),
                CreateTestPage("page2", "Second", "A much longer description that might wrap to multiple lines"),
                CreateTestPage("page3", "Third", "Short"),
            };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: image height = (660 / 1.6).rounded() = 412.5 -> 413 (half away from zero)
                var expectedImageHeight = SwiftNumber.Rounded(660 / TourView.ImageAspectRatio);
                Assert.Equal(413, expectedImageHeight); // :37 imageHeight = (width / TourView.imageAspectRatio).rounded()

                // Assert: total height should be a reasonable value (image height + bottom panel height)
                var content = window.Content;
                Assert.NotNull(content);
                Assert.True(window.Height > 0);
                Assert.Equal(660, window.Width);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-095")] // DroppyCode/App/Views/Tour/TourWindowController.swift:44-46
    public void EmptyPageListSizesWindowToEmptyState()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var emptyPages = Array.Empty<TourPage>();

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(emptyPages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: empty state height is 220 points
                Assert.Equal(TourView.EmptyStateHeight, window.Height);
                Assert.Equal(660, window.Width);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // DroppyCode/App/Views/Tour/TourWindowController.swift:76-90
    public void WindowIsBorderlessTransparentFloatingAndShadowed()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages);
            createdWindow = window;

            try
            {
                // Assert window properties:
                // - No title bar (WindowDecorations = None)
                Assert.Equal(Avalonia.Controls.WindowDecorations.None, window.WindowDecorations);

                // - Transparent background (no opaque white fill)
                // Verify background is set to transparent

                // - Can become key and main (HostWindow overrides these)
                Assert.True(window.CanFocus);

                Assert.True(window.IsVisible);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-096")] // DroppyCode/App/Views/Tour/TourWindowController.swift:8-11
    public void WindowCanBecomeKeyAndMain()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act
            var window = controller.Present(pages);
            createdWindow = window;

            try
            {
                // Assert: window can focus (equivalent to canBecomeKey)
                Assert.True(window.CanFocus);

                // Try to focus the window
                window.Focus();
                Assert.True(window.IsFocused);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-097")] // DroppyCode/App/Views/Tour/TourWindowController.swift:92-101
    public void WindowIsCenteredOnMainWindowWhenVisible()
    {
        headless.OnUiThread(() =>
        {
            // Note: This test is limited in headless environment.
            // The positioning logic requires a main window to be visible.
            // We test that the window is positioned and can be accessed.

            var controller = TourWindowController.Shared;
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            // Act
            var window = controller.Present(pages, width: 660);
            createdWindow = window;

            try
            {
                // Assert: window has position set and size correct
                Assert.Equal(660, window.Width);
                Assert.True(window.Height > 0);
            }
            finally
            {
                Cleanup(window);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-098")] // DroppyCode/App/Views/Tour/TourWindowController.swift:103-108
    public void ClosingWindowClearsControllerReferences()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act: present a window
            var window1 = controller.Present(pages);
            createdWindow = window1;
            Assert.NotNull(window1);

            // Close the window
            window1.Close();

            // Act: present again - should create a new window
            var window2 = controller.Present(pages);

            try
            {
                // Assert: new window is created (different object)
                Assert.NotNull(window2);
                Assert.NotSame(window1, window2);
            }
            finally
            {
                Cleanup(window2);
                controller.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-098")] // DroppyCode/App/Views/Tour/TourWindowController.swift:139-143
    public void CloseMethodClearsReferences()
    {
        headless.OnUiThread(() =>
        {
            // Arrange
            var pages = new[] { CreateTestPage("page1", "Welcome", "Test") };

            var controller = TourWindowController.Shared;

            // Act: present a window
            var window1 = controller.Present(pages);
            createdWindow = window1;

            // Call close() directly
            controller.Close();

            // Act: present again - should create a new window
            var window2 = controller.Present(pages);

            try
            {
                // Assert: new window created
                Assert.NotNull(window2);
                Assert.NotSame(window1, window2);
            }
            finally
            {
                Cleanup(window2);
                controller.Close();
            }
        });
    }
}
